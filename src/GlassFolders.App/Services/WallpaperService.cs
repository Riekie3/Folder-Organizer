using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GlassFolders.Core.Glass;
using GlassFolders.Core.Layout;
using GlassFolders.Native;
using GlassFolders.Native.Shell;
using static GlassFolders.Native.NativeMethods;

namespace GlassFolders.App.Services;

/// <summary>What lies behind a desktop folder: a brush for its plate and the brightness under its label.</summary>
/// <param name="Plate">Blurred wallpaper, a flat colour, or null when unknown.</param>
/// <param name="LabelLuminance">Relative luminance, 0 to 1; NaN when unknown.</param>
public readonly record struct TileBackdrop(Brush? Plate, double LabelLuminance)
{
    public static TileBackdrop Unknown => new(null, double.NaN);
}

/// <summary>
/// Reads the wallpaper under each desktop folder. The folders sit directly on the wallpaper, so this gives them
/// frosted glass and label contrast without capturing the screen. The picture is decoded once at a small size
/// and kept until the next memory trim.
/// </summary>
public sealed class WallpaperService
{
    /// <summary>Decoded width. The plate is blurred anyway, so a third of a 1080p screen is plenty.</summary>
    private const int DecodeWidth = 640;

    private Decoded? _decoded;

    private sealed record Decoded(string Path, DateTime WriteTime, byte[] Pixels, int Width, int Height, int OriginalWidth, int OriginalHeight);

    /// <summary>Looks up the plate and label backdrop for a folder whose plate and label cover these screen rectangles.</summary>
    public TileBackdrop Measure(RectInt plate, RectInt label)
    {
        try
        {
            long start = Stopwatch.GetTimestamp();
            var info = DesktopWallpaper.TryRead(plate.Left + plate.Width / 2, plate.Top + plate.Height / 2);
            if (info is null) return TileBackdrop.Unknown;

            var background = FromColorRef(info.BackgroundColor);
            var flat = new TileBackdrop(Frozen(new SolidColorBrush(background)), Luminance(background));

            var image = Load(info.ImagePath);
            if (image is null) return flat;

            var monitor = ToRect(info.Monitor);
            var virtualScreen = new RectInt(
                GetSystemMetrics(SM_XVIRTUALSCREEN), GetSystemMetrics(SM_YVIRTUALSCREEN),
                GetSystemMetrics(SM_CXVIRTUALSCREEN), GetSystemMetrics(SM_CYVIRTUALSCREEN));
            var fit = Enum.IsDefined(typeof(WallpaperFit), info.Position) ? (WallpaperFit)info.Position : WallpaperFit.Fill;

            var region = WallpaperMapping.SourceRegion(fit, image.OriginalWidth, image.OriginalHeight, monitor, virtualScreen, plate);
            if (region is not { } r || r.Partial) return flat;

            var backdrop = FromPicture(image.Pixels, image.Width, image.Height, r.Scale(image.Width / (double)image.OriginalWidth), plate, label);
            DiagnosticService.Instance.LogDebug(
                $"Wallpaper under folder at {plate}: {fit}, label luminance {backdrop.LabelLuminance:0.00}, {Stopwatch.GetElapsedTime(start).TotalMilliseconds:0.0} ms");
            return backdrop;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException
                                       or ArgumentException or InvalidOperationException or OverflowException)
        {
            DiagnosticService.Instance.LogWarn($"Wallpaper could not be read for a folder backdrop: {ex.Message}");
            return TileBackdrop.Unknown;
        }
    }

    /// <summary>Drops the decoded picture. It is read again the next time a folder needs it.</summary>
    public void ReleaseCache() => _decoded = null;

    private Decoded? Load(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) path = DesktopWallpaper.TranscodedWallpaperPath;
        if (!File.Exists(path)) return null;

        DateTime written = File.GetLastWriteTimeUtc(path);
        if (_decoded is { } cached && cached.Path == path && cached.WriteTime == written) return cached;

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.IgnoreColorProfile | BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
        var frame = decoder.Frames[0];
        int originalWidth = frame.PixelWidth, originalHeight = frame.PixelHeight;
        if (originalWidth <= 0 || originalHeight <= 0) return null;

        stream.Position = 0;
        var small = new BitmapImage();
        small.BeginInit();
        small.StreamSource = stream;
        small.CacheOption = BitmapCacheOption.OnLoad;
        small.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        small.DecodePixelWidth = Math.Min(originalWidth, DecodeWidth);
        small.EndInit();

        var bgra = new FormatConvertedBitmap(small, PixelFormats.Bgra32, null, 0);
        int w = bgra.PixelWidth, h = bgra.PixelHeight;
        var pixels = new byte[w * h * 4];
        bgra.CopyPixels(pixels, w * 4, 0);

        _decoded = new Decoded(path, written, pixels, w, h, originalWidth, originalHeight);
        return _decoded;
    }

    /// <summary>
    /// Builds a folder's backdrop from part of a picture: <paramref name="region"/> is in the picture's pixels
    /// (BGRA, top-down) and lies under <paramref name="plate"/>; <paramref name="label"/> is inside the plate.
    /// </summary>
    public static TileBackdrop FromPicture(byte[] pixels, int width, int height, ImageRegion region, RectInt plate, RectInt label)
    {
        var frost = Frost(pixels, width, height, region);
        var bitmap = BitmapSource.Create(frost.Width, frost.Height, 96, 96, PixelFormats.Bgra32, null, frost.Pixels, frost.Stride);
        bitmap.Freeze();

        // The label's share of the plate, in the frosted image's pixels.
        double sx = frost.Width / (double)plate.Width, sy = frost.Height / (double)plate.Height;
        var light = BackdropAnalyzer.Measure(frost,
            (int)Math.Floor((label.Left - plate.Left) * sx), (int)Math.Floor((label.Top - plate.Top) * sy),
            (int)Math.Ceiling(label.Width * sx), (int)Math.Ceiling(label.Height * sy));

        return new TileBackdrop(Frozen(new ImageBrush(bitmap) { Stretch = Stretch.Fill }), light.Mean);
    }

    private static FrostedImage Frost(byte[] pixels, int width, int height, ImageRegion region)
    {
        int x0 = Math.Clamp((int)Math.Floor(region.X), 0, width - 1);
        int y0 = Math.Clamp((int)Math.Floor(region.Y), 0, height - 1);
        int w = Math.Clamp((int)Math.Ceiling(region.Width), 1, width - x0);
        int h = Math.Clamp((int)Math.Ceiling(region.Height), 1, height - y0);

        var crop = new byte[w * h * 4];
        for (int row = 0; row < h; row++)
            Buffer.BlockCopy(pixels, ((y0 + row) * width + x0) * 4, crop, row * w * 4, w * 4);

        // About a twelfth of the plate's width: soft like the reference, with the wallpaper's shapes still visible.
        var settings = new FrostSettings(Downsample: 1, BlurRadius: Math.Max(1, w / 12), BlurPasses: 3, Saturation: 1.15);
        return FrostFilter.Apply(crop, w, h, w * 4, settings);
    }

    private static Color FromColorRef(uint colorRef) =>
        Color.FromRgb((byte)(colorRef & 0xFF), (byte)((colorRef >> 8) & 0xFF), (byte)((colorRef >> 16) & 0xFF));

    private static double Luminance(Color c) => BackdropAnalyzer.RelativeLuminance(c.R / 255.0, c.G / 255.0, c.B / 255.0);

    private static RectInt ToRect(RECT r) => new(r.Left, r.Top, r.Width, r.Height);

    private static T Frozen<T>(T freezable) where T : System.Windows.Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
