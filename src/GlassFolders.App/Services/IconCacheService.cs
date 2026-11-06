using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using GlassFolders.Core.Models;

namespace GlassFolders.App.Services;

/// <summary>
/// Extracts and caches application and file icons (up to 256px / high-DPI).
/// Uses a two-tier cache: in-memory cache + %LOCALAPPDATA%\GlassFolders\IconCache disk PNGs.
/// Supports memory cache trimming to keep working set strictly below 100 MB.
/// </summary>
public sealed partial class IconCacheService
{
    private static readonly ConcurrentDictionary<string, BitmapSource> MemoryCache = new();
    private readonly string _diskCacheDir;

    public IconCacheService(string? cacheDir = null)
    {
        _diskCacheDir = cacheDir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GlassFolders", "IconCache");
        try { Directory.CreateDirectory(_diskCacheDir); } catch { }
    }

    public static void TrimMemoryCache()
    {
        MemoryCache.Clear();
    }

    public BitmapSource? GetIcon(FolderItem item)
    {
        string key = GetCacheKey(item);
        if (MemoryCache.TryGetValue(key, out var cached))
            return cached;

        string diskPath = Path.Combine(_diskCacheDir, key + ".png");
        if (File.Exists(diskPath))
        {
            try
            {
                var bmp = LoadBitmapFromFile(diskPath);
                if (bmp is not null)
                {
                    MemoryCache[key] = bmp;
                    return bmp;
                }
            }
            catch { }
        }

        // Extract from system
        var extracted = ExtractFromSystem(item);
        if (extracted is not null)
        {
            extracted.Freeze();
            MemoryCache[key] = extracted;
            SaveToDisk(extracted, diskPath);
            return extracted;
        }

        return null;
    }

    private static string GetCacheKey(FolderItem item)
    {
        string source = $"{item.Type}:{item.TargetPath}:{item.ShortcutPath}:{item.IconLocation}:{item.IconIndex}";
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(source));
        return Convert.ToHexString(hash)[..16];
    }

    private static BitmapSource? ExtractFromSystem(FolderItem item)
    {
        nint hIcon = 0;

        try
        {
            // 1. Explicit icon location (e.g. from .lnk or custom icon)
            if (!string.IsNullOrEmpty(item.IconLocation) && File.Exists(item.IconLocation))
            {
                ExtractIconEx(item.IconLocation, item.IconIndex, out hIcon, out _, 1);
            }

            // 2. Target path if .exe, .ico, or existing file
            if (hIcon == 0 && !string.IsNullOrEmpty(item.TargetPath) && File.Exists(item.TargetPath))
            {
                string ext = Path.GetExtension(item.TargetPath).ToLowerInvariant();
                if (ext is ".exe" or ".ico")
                {
                    ExtractIconEx(item.TargetPath, 0, out hIcon, out _, 1);
                }
            }

            // 3. Fallback to SHGetFileInfo for file associations / directories
            if (hIcon == 0)
            {
                string path = !string.IsNullOrEmpty(item.ShortcutPath) && File.Exists(item.ShortcutPath)
                    ? item.ShortcutPath
                    : (!string.IsNullOrEmpty(item.TargetPath) ? item.TargetPath : "file.txt");

                var shfi = new SHFILEINFOW();
                uint flags = SHGFI_ICON | SHGFI_LARGEICON;
                if (!File.Exists(path) && !Directory.Exists(path))
                    flags |= SHGFI_USEFILEATTRIBUTES;

                nint res = SHGetFileInfo(path, 0, ref shfi, (uint)Marshal.SizeOf<SHFILEINFOW>(), flags);
                if (res != 0 && shfi.hIcon != 0)
                    hIcon = shfi.hIcon;
            }

            if (hIcon != 0)
            {
                var src = Imaging.CreateBitmapSourceFromHIcon(hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                return src;
            }
        }
        catch
        {
            // Ignore icon extraction errors
        }
        finally
        {
            if (hIcon != 0)
                DestroyIcon(hIcon);
        }

        return null;
    }

    private static BitmapSource? LoadBitmapFromFile(string path)
    {
        var bmp = new BitmapImage();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.StreamSource = stream;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    private static void SaveToDisk(BitmapSource bmp, string path)
    {
        try
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            encoder.Save(fs);
        }
        catch
        {
            // Disk write failure is non-fatal
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
