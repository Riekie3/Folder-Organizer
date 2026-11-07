using System.Runtime.InteropServices;

namespace GlassFolders.Native;

[StructLayout(LayoutKind.Sequential)]
public struct MARGINS
{
    public int cxLeftWidth;
    public int cxRightWidth;
    public int cyTopHeight;
    public int cyBottomHeight;

    public MARGINS(int left, int right, int top, int bottom)
    {
        cxLeftWidth = left;
        cxRightWidth = right;
        cyTopHeight = top;
        cyBottomHeight = bottom;
    }
}

public static partial class DwmHelpers
{
    public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    public const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

    // Backdrops
    public const int DWMSBT_AUTO = 0;
    public const int DWMSBT_NONE = 1;
    public const int DWMSBT_MAINWINDOW = 2;       // Mica
    public const int DWMSBT_TRANSIENTWINDOW = 3;  // Acrylic
    public const int DWMSBT_TABBEDWINDOW = 4;     // Mica Alt

    // Corners
    public const int DWMWCP_DEFAULT = 0;
    public const int DWMWCP_DONOTROUND = 1;
    public const int DWMWCP_ROUND = 2;
    public const int DWMWCP_ROUNDSMALL = 3;

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmSetWindowAttribute(nint hwnd, int dwAttribute, in int pvAttribute, int cbAttribute);

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmExtendFrameIntoClientArea(nint hwnd, in MARGINS pMarInset);

    [LibraryImport("dwmapi.dll")]

    public static partial int DwmIsCompositionEnabled(out int pfEnabled);

    public static bool IsCompositionEnabled()
    {
        try { return DwmIsCompositionEnabled(out int e) == 0 && e != 0; } catch { return false; }
    }

    /// <summary>
    /// Enables Windows 11 Acrylic backdrop blur on a popup or dialog window.
    /// Extends the frame into the client area and configures dark or light theme.
    /// </summary>
    public static bool EnableAcrylic(nint hwnd, bool darkMode = true)
    {
        if (hwnd == 0 || !IsCompositionEnabled()) return false;

        try
        {
            // 1. Extend frame into client area (-1 = all margins)
            var margins = new MARGINS(-1, -1, -1, -1);
            DwmExtendFrameIntoClientArea(hwnd, in margins);

            // 2. Set Immersive Dark Mode
            int dark = darkMode ? 1 : 0;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, in dark, sizeof(int));

            // 3. Set Corner Preference (Round)
            int corner = DWMWCP_ROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, in corner, sizeof(int));

            // 4. Set Backdrop Type to Transient Window (Acrylic)
            int backdrop = DWMSBT_TRANSIENTWINDOW;
            int hr = DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, in backdrop, sizeof(int));

            return hr == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Enables Windows 11 Mica backdrop on a settings or main application window.
    /// </summary>
    public static bool EnableMica(nint hwnd, bool darkMode = true)
    {
        if (hwnd == 0 || !IsCompositionEnabled()) return false;

        try
        {
            var margins = new MARGINS(-1, -1, -1, -1);
            DwmExtendFrameIntoClientArea(hwnd, in margins);

            int dark = darkMode ? 1 : 0;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, in dark, sizeof(int));

            int corner = DWMWCP_ROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, in corner, sizeof(int));

            int backdrop = DWMSBT_MAINWINDOW;
            int hr = DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, in backdrop, sizeof(int));

            return hr == 0;
        }
        catch
        {
            return false;
        }
    }
}
