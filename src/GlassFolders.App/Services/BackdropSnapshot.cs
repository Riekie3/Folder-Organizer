using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GlassFolders.Core.Glass;
using GlassFolders.Native;
using Microsoft.Win32;

namespace GlassFolders.App.Services;

/// <summary>
/// A frosted copy of the screen, captured once before a glass window appears. It stays in memory while that
/// window is open and is never saved. Only the shrunken, blurred version is kept.
/// </summary>
public sealed class BackdropSnapshot
{
    private readonly FrostedImage _frost;
    private readonly FrostedImage? _source;
    private readonly double _dpiScale;

    private BackdropSnapshot(FrostedImage frost, FrostedImage? source, double dpiScale, int blurRadius, BitmapSource image, RECT screenArea, double elapsedMs)
    {
        BlurRadius = blurRadius;
        _frost = frost;
        _source = source;
        _dpiScale = dpiScale;
        Image = image;
        ScreenArea = screenArea;
        ElapsedMilliseconds = elapsedMs;
    }

    public BitmapSource Image { get; }

    /// <summary>The box blur radius this image was made with. See <see cref="GlassFolders.Core.Models.GlassAppearance.BlurRadius"/>.</summary>
    public int BlurRadius { get; }

    /// <summary>The part of the screen this image covers, in physical pixels.</summary>
    public RECT ScreenArea { get; }

    /// <summary>Time taken by capture and filtering.</summary>
    public double ElapsedMilliseconds { get; }

    /// <summary>
    /// False under high contrast, or when Windows' transparency effects are off.
    /// The solid panel is used in those cases, and no capture is taken.
    /// </summary>
    public static bool IsGlassAllowed() => !SystemParameters.HighContrast && IsTransparencyEnabled();

    /// <summary>
    /// Captures and frosts an area of the screen. Returns null if the capture fails or comes back black.
    /// With <paramref name="allowReblur"/>, a smaller unblurred copy is kept in memory so <see cref="WithBlur"/> can change the
    /// blur later without capturing again. A window that stays open and can't capture itself needs this. It is never saved.
    /// </summary>
    public static BackdropSnapshot? Capture(RECT area, double dpiScale, int blurRadius = FrostSettings.DefaultBlurRadius, bool allowReblur = false)
    {
        try
        {
            long start = Stopwatch.GetTimestamp();
            var settings = FrostSettings.ForScale(dpiScale, blurRadius);

            FrostedImage? source = null;
            FrostedImage? frost;
            bool read;
            if (allowReblur)
            {
                // Shrink to half the final size first, keep that, then finish from it: the same result in a fraction of the memory.
                var first = new FrostSettings(Math.Max(1, settings.Downsample / 2), 0, 0, 1.0);
                read = ScreenCapture.TryRead(area, (pixels, w, h, stride) => FrostFilter.Apply(pixels, w, h, stride, first), out source);
                frost = read && source is not null ? Reblur(source, blurRadius, dpiScale) : null;
            }
            else
            {
                read = ScreenCapture.TryRead(area, (pixels, w, h, stride) => FrostFilter.Apply(pixels, w, h, stride, settings), out frost);
            }

            if (!read || frost is null || frost.IsBlank)
            {
                DiagnosticService.Instance.LogWarn($"Backdrop capture unavailable for {area}; using solid panel");
                return null;
            }

            return new BackdropSnapshot(frost, source, dpiScale, blurRadius, ToBitmap(frost), area, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
        catch (Exception ex)
        {
            DiagnosticService.Instance.LogError("Backdrop capture failed; using solid panel", ex);
            return null;
        }
    }

    /// <summary>
    /// Builds a snapshot from a picture instead of the screen, as if the picture were the screen area <paramref name="area"/>.
    /// For tests and the screenshot tools, which render windows without showing them.
    /// </summary>
    public static BackdropSnapshot FromPixels(byte[] bgra, int width, int height, RECT area, double dpiScale,
        int blurRadius = FrostSettings.DefaultBlurRadius, bool allowReblur = true)
    {
        var settings = FrostSettings.ForScale(dpiScale, blurRadius);
        FrostedImage? source = null;
        FrostedImage frost;
        if (allowReblur)
        {
            source = FrostFilter.Apply(bgra, width, height, width * 4, new FrostSettings(Math.Max(1, settings.Downsample / 2), 0, 0, 1.0));
            frost = Reblur(source, blurRadius, dpiScale);
        }
        else
        {
            frost = FrostFilter.Apply(bgra, width, height, width * 4, settings);
        }
        return new BackdropSnapshot(frost, source, dpiScale, blurRadius, ToBitmap(frost), area, 0);
    }

    /// <summary>The same capture with a different blur radius, or null if this snapshot wasn't taken with reblur allowed.</summary>
    public BackdropSnapshot? WithBlur(int blurRadius)
    {
        if (_source is null) return null;
        long start = Stopwatch.GetTimestamp();
        var frost = Reblur(_source, blurRadius, _dpiScale);
        return new BackdropSnapshot(frost, _source, _dpiScale, blurRadius, ToBitmap(frost), ScreenArea, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
    }

    /// <summary>Picks the tint for a card at this screen position. <paramref name="light"/> is the light theme's milky panel.</summary>
    public GlassTint TintFor(Rect cardOnScreen, double strength = 1, bool light = false)
    {
        double sx = _frost.Width / (double)ScreenArea.Width;
        double sy = _frost.Height / (double)ScreenArea.Height;
        var measured = BackdropAnalyzer.Measure(
            _frost,
            (int)((cardOnScreen.X - ScreenArea.Left) * sx),
            (int)((cardOnScreen.Y - ScreenArea.Top) * sy),
            (int)Math.Ceiling(cardOnScreen.Width * sx),
            (int)Math.Ceiling(cardOnScreen.Height * sy));
        return light ? GlassTint.ForLightPanel(measured, strength) : GlassTint.ForBackdrop(measured, strength);
    }

    private static FrostedImage Reblur(FrostedImage source, int blurRadius, double dpiScale)
    {
        var settings = FrostSettings.ForScale(dpiScale, blurRadius);
        return FrostFilter.Apply(source.Pixels, source.Width, source.Height, source.Stride, settings with { Downsample = 2 });
    }

    private static BitmapSource ToBitmap(FrostedImage frost)
    {
        var image = BitmapSource.Create(frost.Width, frost.Height, 96, 96, PixelFormats.Pbgra32, null, frost.Pixels, frost.Stride);
        image.Freeze();
        return image;
    }

    private static bool IsTransparencyEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("EnableTransparency") is not int value || value != 0;
        }
        catch (Exception ex)
        {
            DiagnosticService.Instance.LogError("Could not read the Windows transparency setting", ex);
            return true;
        }
    }
}
