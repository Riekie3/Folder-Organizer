using System.Runtime.InteropServices;

namespace GlassFolders.Native.Shell;

/// <summary>The wallpaper on one monitor: the picture (null for a plain colour), how it's fitted, and the colour around it.</summary>
/// <param name="Position">The shell's DESKTOP_WALLPAPER_POSITION.</param>
/// <param name="BackgroundColor">COLORREF, 0x00BBGGRR.</param>
public sealed record WallpaperInfo(string? ImagePath, int Position, uint BackgroundColor, RECT Monitor);

/// <summary>
/// Reads the wallpaper settings through the shell's documented <c>IDesktopWallpaper</c>. Read-only.
/// </summary>
public static class DesktopWallpaper
{
    private static readonly Guid CLSID_DesktopWallpaper = new("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD");

    /// <summary>Returns the wallpaper of the monitor that contains the point, or null if the shell can't say.</summary>
    public static WallpaperInfo? TryRead(int screenX, int screenY)
    {
        object? instance = null;
        try
        {
            Type? type = Type.GetTypeFromCLSID(CLSID_DesktopWallpaper, throwOnError: false);
            if (type is null) return null;
            instance = Activator.CreateInstance(type);
            if (instance is not IDesktopWallpaper wallpaper) return null;

            if (wallpaper.GetMonitorDevicePathCount(out uint count) != 0 || count == 0) return null;
            for (uint i = 0; i < count; i++)
            {
                if (wallpaper.GetMonitorDevicePathAt(i, out string? id) != 0 || string.IsNullOrEmpty(id)) continue;
                if (wallpaper.GetMonitorRECT(id, out RECT rect) != 0) continue;
                if (!rect.Contains(new POINT(screenX, screenY))) continue;

                wallpaper.GetWallpaper(id, out string? path);
                wallpaper.GetPosition(out int position);
                wallpaper.GetBackgroundColor(out uint color);
                return new WallpaperInfo(string.IsNullOrWhiteSpace(path) ? null : path, position, color, rect);
            }
            return null;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or UnauthorizedAccessException)
        {
            return null;
        }
        finally
        {
            if (instance is not null && Marshal.IsComObject(instance)) Marshal.ReleaseComObject(instance);
        }
    }

    /// <summary>Windows keeps a copy of the current wallpaper here, used when the original file has moved.</summary>
    public static string TranscodedWallpaperPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Windows", "Themes", "TranscodedWallpaper");

    [ComImport, Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDesktopWallpaper
    {
        void SetWallpaper();
        [PreserveSig] int GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorId, [MarshalAs(UnmanagedType.LPWStr)] out string? wallpaper);
        [PreserveSig] int GetMonitorDevicePathAt(uint index, [MarshalAs(UnmanagedType.LPWStr)] out string? monitorId);
        [PreserveSig] int GetMonitorDevicePathCount(out uint count);
        [PreserveSig] int GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorId, out RECT rect);
        void SetBackgroundColor();
        [PreserveSig] int GetBackgroundColor(out uint color);
        void SetPosition();
        [PreserveSig] int GetPosition(out int position);
    }
}
