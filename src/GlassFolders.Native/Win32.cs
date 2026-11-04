using static GlassFolders.Native.NativeMethods;

namespace GlassFolders.Native;

/// <summary>Small, allocation-light wrappers over <see cref="NativeMethods"/>.</summary>
public static class Win32
{
    public static unsafe string ClassName(nint hwnd)
    {
        char* buf = stackalloc char[256];
        int n = GetClassName(hwnd, buf, 256);
        return new string(buf, 0, n);
    }

    public static unsafe string Text(nint hwnd)
    {
        char* buf = stackalloc char[256];
        int n = GetWindowText(hwnd, buf, 256);
        return new string(buf, 0, n);
    }

    public static RECT Rect(nint hwnd) => GetWindowRect(hwnd, out var r) ? r : default;

    public static uint Style(nint hwnd) => (uint)GetWindowLongPtr(hwnd, GWL_STYLE);
    public static uint ExStyle(nint hwnd) => (uint)GetWindowLongPtr(hwnd, GWL_EXSTYLE);
    public static nint Owner(nint hwnd) => GetWindow(hwnd, GW_OWNER);

    public static uint ProcessId(nint hwnd)
    {
        GetWindowThreadProcessId(hwnd, out uint pid);
        return pid;
    }

    public static List<nint> TopLevelWindows()
    {
        var list = new List<nint>();
        EnumWindows((h, _) => { list.Add(h); return true; }, 0);
        return list;
    }

    /// <summary>Direct children only (EnumChildWindows is recursive, so filter by parent).</summary>
    public static List<nint> Children(nint parent)
    {
        var list = new List<nint>();
        for (nint c = GetWindow(parent, GW_CHILD); c != 0; c = GetWindow(c, GW_HWNDNEXT))
            list.Add(c);
        return list;
    }

    public static MONITORINFO MonitorInfo(nint monitor)
    {
        var mi = new MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(monitor, ref mi);
        return mi;
    }

    public static RECT PrimaryWorkArea() => MonitorInfo(MonitorFromPoint(new POINT(0, 0), MONITOR_DEFAULTTOPRIMARY)).rcWork;

    public static RECT VirtualScreen()
    {
        int x = GetSystemMetrics(SM_XVIRTUALSCREEN), y = GetSystemMetrics(SM_YVIRTUALSCREEN);
        return new RECT(x, y, x + GetSystemMetrics(SM_CXVIRTUALSCREEN), y + GetSystemMetrics(SM_CYVIRTUALSCREEN));
    }

    public static string Describe(nint hwnd)
    {
        if (hwnd == 0) return "(null)";
        return $"0x{hwnd:X8} [{ClassName(hwnd)}] \"{Text(hwnd)}\" pid={ProcessId(hwnd)} vis={IsWindowVisible(hwnd)} {Rect(hwnd)} " +
               $"style=0x{Style(hwnd):X8} ex=0x{ExStyle(hwnd):X8} owner=0x{Owner(hwnd):X} parent=0x{GetParent(hwnd):X}";
    }
}
