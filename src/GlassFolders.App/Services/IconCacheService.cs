using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GlassFolders.Core.Config;
using GlassFolders.Core.Icons;
using GlassFolders.Core.Layout;
using GlassFolders.Core.Models;
using GlassFolders.Native.Shell;

namespace GlassFolders.App.Services;

/// <summary>An app icon ready to draw.</summary>
/// <param name="IsLowResolution">The app has no large artwork, so the icon should sit on a tile.</param>
public sealed record AppIcon(BitmapSource Image, bool IsLowResolution);

/// <summary>
/// Gets app, shortcut and file icons at up to 256 px, decoded at the size they are drawn.
/// Originals are kept on disk as PNGs. Memory holds only display-size copies and can be cleared at any time.
/// Reading from Windows can take over a second on a cold disk, so that part runs on its own thread.
/// </summary>
public sealed partial class IconCacheService
{
    private const int SourceSize = 256;
    private const string FilePrefix = "v2-";
    private const string LowResolutionSuffix = "-low";

    private static readonly ConcurrentDictionary<string, AppIcon> MemoryCache = new();
    private readonly string _diskCacheDir;

    public IconCacheService(string? cacheDir = null)
    {
        _diskCacheDir = cacheDir ?? AppPaths.IconCacheDir;
        try
        {
            Directory.CreateDirectory(_diskCacheDir);
            DeleteOldCacheFiles();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DiagnosticService.Instance.LogWarn($"Icon cache folder unavailable: {ex.Message}");
        }
    }

    public static void TrimMemoryCache() => MemoryCache.Clear();

    /// <summary>Returns the icon if it is in memory or on disk. Never calls into the shell, so it's quick enough for the UI thread.</summary>
    public AppIcon? TryGetCached(FolderItem item, int pixelSize)
    {
        string key = GetCacheKey(item);
        string memoryKey = $"{key}@{pixelSize}";
        if (MemoryCache.TryGetValue(memoryKey, out var cached)) return cached;

        foreach (bool lowResolution in new[] { false, true })
        {
            string path = DiskPath(key, lowResolution);
            if (!File.Exists(path)) continue;

            var bitmap = LoadBitmap(path, pixelSize);
            if (bitmap is null) continue;

            var icon = new AppIcon(bitmap, lowResolution);
            MemoryCache[memoryKey] = icon;
            return icon;
        }
        return null;
    }

    /// <summary>
    /// Fetches icons that aren't cached yet on a background thread. <paramref name="onLoaded"/> runs on that
    /// thread once per item, with null when Windows has no icon for it.
    /// </summary>
    public void LoadMissing(IReadOnlyList<FolderItem> items, int pixelSize, Action<FolderItem, AppIcon?> onLoaded)
    {
        // The shell's image factory needs a single-threaded COM apartment.
        var thread = new Thread(() =>
        {
            foreach (var item in items)
            {
                AppIcon? icon = null;
                try
                {
                    icon = Load(item, pixelSize);
                }
                catch (Exception ex)
                {
                    DiagnosticService.Instance.LogError($"Could not load the icon for {item.DisplayName}", ex);
                }
                onLoaded(item, icon);
            }
        })
        {
            IsBackground = true,
            Name = "GlassFolders icon loader",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    private AppIcon? Load(FolderItem item, int pixelSize)
    {
        var known = TryGetCached(item, pixelSize);
        if (known is not null) return known;

        BitmapSource? source;
        bool lowResolution = true;
        if (ExtractFromShell(item) is { } fromShell) (source, lowResolution) = fromShell;
        else source = ExtractFromFileIcon(item);
        if (source is null) return null;

        string key = GetCacheKey(item);
        string path = DiskPath(key, lowResolution);
        SaveToDisk(source, path);

        // If the disk write failed the icon still shows, just at full size.
        var icon = new AppIcon(LoadBitmap(path, pixelSize) ?? source, lowResolution);
        MemoryCache[$"{key}@{pixelSize}"] = icon;
        return icon;
    }

    private static (BitmapSource Image, bool LowResolution)? ExtractFromShell(FolderItem item)
    {
        foreach (string? path in ShellPaths(item))
        {
            if (string.IsNullOrEmpty(path)) continue;
            if (!ShellImage.TryGetIcon(path, SourceSize, out byte[] pixels, out int width, out int height)) continue;

            var analysis = IconAnalysis.Analyze(pixels, width, height);
            if (analysis.Content == default) continue;

            var format = analysis.IsStraightAlpha ? PixelFormats.Bgra32 : PixelFormats.Pbgra32;
            BitmapSource image = BitmapSource.Create(width, height, 96, 96, format, null, pixels, width * 4);
            if (analysis.IsPadded) image = new CroppedBitmap(image, ToInt32Rect(analysis.Content));
            image.Freeze();
            return (image, analysis.IsLowResolution);
        }
        return null;
    }

    // The shortcut comes first because the shell then honours a custom icon set on it.
    private static IEnumerable<string?> ShellPaths(FolderItem item)
    {
        if (File.Exists(item.ShortcutPath)) yield return item.ShortcutPath;
        if (!string.IsNullOrEmpty(item.Aumid)) yield return @"shell:AppsFolder\" + item.Aumid;
        if (File.Exists(item.TargetPath) || Directory.Exists(item.TargetPath)) yield return item.TargetPath;
    }

    private static Int32Rect ToInt32Rect(RectInt rect) => new(rect.Left, rect.Top, rect.Width, rect.Height);

    /// <summary>Older route for items the shell can't open, such as a removed shortcut with a known icon file.</summary>
    private static BitmapSource? ExtractFromFileIcon(FolderItem item)
    {
        nint icon = 0;
        try
        {
            if (File.Exists(item.IconLocation))
                ExtractIconEx(item.IconLocation, item.IconIndex, out icon, out _, 1);

            if (icon == 0)
            {
                string path = !string.IsNullOrEmpty(item.TargetPath) ? item.TargetPath : "file.txt";
                var info = new SHFILEINFOW();
                uint flags = SHGFI_ICON | SHGFI_LARGEICON;
                if (!File.Exists(path) && !Directory.Exists(path)) flags |= SHGFI_USEFILEATTRIBUTES;
                if (SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFOW>(), flags) != 0)
                    icon = info.hIcon;
            }

            if (icon == 0) return null;
            var bitmap = Imaging.CreateBitmapSourceFromHIcon(icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex) when (ex is COMException or ArgumentException)
        {
            DiagnosticService.Instance.LogWarn($"No file icon for {item.DisplayName}: {ex.Message}");
            return null;
        }
        finally
        {
            if (icon != 0) DestroyIcon(icon);
        }
    }

    private string DiskPath(string key, bool lowResolution) =>
        Path.Combine(_diskCacheDir, FilePrefix + key + (lowResolution ? LowResolutionSuffix : "") + ".png");

    // Earlier versions cached 32 px icons under plain names.
    private void DeleteOldCacheFiles()
    {
        foreach (string file in Directory.EnumerateFiles(_diskCacheDir, "*.png"))
        {
            if (!Path.GetFileName(file).StartsWith(FilePrefix, StringComparison.Ordinal))
                File.Delete(file);
        }
    }

    private static string GetCacheKey(FolderItem item)
    {
        string source = $"{item.Type}:{item.TargetPath}:{item.ShortcutPath}:{item.Aumid}:{item.IconLocation}:{item.IconIndex}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)))[..16];
    }

    private static BitmapSource? LoadBitmap(string path, int pixelSize)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var bitmap = new BitmapImage();
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = pixelSize;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or FileFormatException or UnauthorizedAccessException)
        {
            DiagnosticService.Instance.LogWarn($"Unreadable cached icon {Path.GetFileName(path)}: {ex.Message}");
            return null;
        }
    }

    private static void SaveToDisk(BitmapSource bitmap, string path)
    {
        try
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            encoder.Save(stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DiagnosticService.Instance.LogWarn($"Could not cache icon {Path.GetFileName(path)}: {ex.Message}");
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFOW
    {
        public nint hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_LARGEICON = 0x000000000;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;

    [DllImport("shell32.dll", EntryPoint = "SHGetFileInfoW", CharSet = CharSet.Unicode)]
    private static extern nint SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFOW psfi, uint cbFileInfo, uint uFlags);

    [LibraryImport("shell32.dll", EntryPoint = "ExtractIconExW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint ExtractIconEx(string lpszFile, int nIconIndex, out nint phiconLarge, out nint phiconSmall, uint nIcons);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(nint hIcon);
}
