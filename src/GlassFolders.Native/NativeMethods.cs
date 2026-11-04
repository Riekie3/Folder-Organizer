using System.Runtime.InteropServices;

namespace GlassFolders.Native;

[StructLayout(LayoutKind.Sequential)]
public struct RECT
{
    public int Left, Top, Right, Bottom;
    public RECT(int left, int top, int right, int bottom) { Left = left; Top = top; Right = right; Bottom = bottom; }
    public readonly int Width => Right - Left;
    public readonly int Height => Bottom - Top;
    public readonly bool Contains(POINT p) => p.X >= Left && p.X < Right && p.Y >= Top && p.Y < Bottom;
    public readonly bool Intersects(RECT r) => Left < r.Right && r.Left < Right && Top < r.Bottom && r.Top < Bottom;
    public readonly POINT Center => new(Left + Width / 2, Top + Height / 2);
    public override readonly string ToString() => $"({Left},{Top})-({Right},{Bottom}) {Width}x{Height}";
}

[StructLayout(LayoutKind.Sequential)]
public struct POINT
{
    public int X, Y;
    public POINT(int x, int y) { X = x; Y = y; }
    public override readonly string ToString() => $"({X},{Y})";
}

[StructLayout(LayoutKind.Sequential)]
public struct MONITORINFO
{
    public int cbSize;
    public RECT rcMonitor;
    public RECT rcWork;
    public uint dwFlags;
}

[StructLayout(LayoutKind.Sequential)]
public struct WINDOWPOS
{
    public nint hwnd;
    public nint hwndInsertAfter;
    public int x, y, cx, cy;
    public uint flags;
}

[StructLayout(LayoutKind.Sequential)]
public struct INPUT
{
    public uint type;
    public INPUTUNION u;
}

[StructLayout(LayoutKind.Explicit)]
public struct INPUTUNION
{
    [FieldOffset(0)] public MOUSEINPUT mi;
    [FieldOffset(0)] public KEYBDINPUT ki;
}

[StructLayout(LayoutKind.Sequential)]
public struct MOUSEINPUT
{
    public int dx, dy;
    public uint mouseData, dwFlags, time;
    public nint dwExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
public struct KEYBDINPUT
{
    public ushort wVk, wScan;
    public uint dwFlags, time;
    public nint dwExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
public struct MSG
{
    public nint hwnd;
    public uint message;
    public nint wParam, lParam;
    public uint time;
    public POINT pt;
}

public delegate bool EnumWindowsProc(nint hwnd, nint lParam);
public delegate void WinEventProc(nint hook, uint evt, nint hwnd, int idObject, int idChild, uint thread, uint time);

/// <summary>Raw Win32 declarations. Higher-level helpers live in <see cref="Win32"/> and <see cref="DesktopWindows"/>.</summary>
public static partial class NativeMethods
{
    public const int GWL_STYLE = -16, GWL_EXSTYLE = -20, GWLP_HWNDPARENT = -8;

    public const uint WS_POPUP = 0x80000000, WS_CHILD = 0x40000000, WS_VISIBLE = 0x10000000,
        WS_CLIPSIBLINGS = 0x04000000, WS_CLIPCHILDREN = 0x02000000, WS_OVERLAPPEDWINDOW = 0x00CF0000;

    public const uint WS_EX_TOOLWINDOW = 0x80, WS_EX_APPWINDOW = 0x40000, WS_EX_NOACTIVATE = 0x08000000,
        WS_EX_LAYERED = 0x80000, WS_EX_TOPMOST = 0x8, WS_EX_TRANSPARENT = 0x20;

    public const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10,
        SWP_SHOWWINDOW = 0x40, SWP_FRAMECHANGED = 0x20, SWP_NOOWNERZORDER = 0x200;

    public static readonly nint HWND_BOTTOM = 1, HWND_TOP = 0, HWND_TOPMOST = -1, HWND_NOTOPMOST = -2;

    public const uint GW_HWNDNEXT = 2, GW_HWNDPREV = 3, GW_OWNER = 4, GW_CHILD = 5;
    public const uint GA_PARENT = 1, GA_ROOT = 2, GA_ROOTOWNER = 3;

    public const uint WM_CLOSE = 0x0010, WM_DESTROY = 0x0002, WM_WINDOWPOSCHANGING = 0x0046,
        WM_MOUSEACTIVATE = 0x0021, WM_DISPLAYCHANGE = 0x007E, WM_DPICHANGED = 0x02E0,
        WM_SETTINGCHANGE = 0x001A, WM_NCHITTEST = 0x0084;
    public const int MA_NOACTIVATE = 3;

    public const uint SMTO_NORMAL = 0x0;
    public const uint MONITOR_DEFAULTTONEAREST = 2, MONITOR_DEFAULTTOPRIMARY = 1;

    public const uint EVENT_OBJECT_DESTROY = 0x8001, EVENT_OBJECT_CREATE = 0x8000, EVENT_SYSTEM_FOREGROUND = 0x0003;
    public const uint WINEVENT_OUTOFCONTEXT = 0x0, WINEVENT_SKIPOWNPROCESS = 0x2;

    public const uint INPUT_MOUSE = 0, INPUT_KEYBOARD = 1;
    public const uint MOUSEEVENTF_MOVE = 0x1, MOUSEEVENTF_LEFTDOWN = 0x2, MOUSEEVENTF_LEFTUP = 0x4,
        MOUSEEVENTF_ABSOLUTE = 0x8000, MOUSEEVENTF_VIRTUALDESK = 0x4000;
    public const uint KEYEVENTF_KEYUP = 0x2;
    public const ushort VK_LWIN = 0x5B, VK_SHIFT = 0x10;
    public const uint PM_REMOVE = 1;
    public const int SM_XVIRTUALSCREEN = 76, SM_YVIRTUALSCREEN = 77, SM_CXVIRTUALSCREEN = 78, SM_CYVIRTUALSCREEN = 79;

    [LibraryImport("user32.dll", EntryPoint = "FindWindowW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint FindWindow(string? className, string? windowName);

    [LibraryImport("user32.dll", EntryPoint = "FindWindowExW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint FindWindowEx(nint parent, nint childAfter, string? className, string? windowName);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EnumWindows(EnumWindowsProc callback, nint lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EnumChildWindows(nint parent, EnumWindowsProc callback, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW")]
    public static unsafe partial int GetClassName(nint hwnd, char* buffer, int max);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW")]
    public static unsafe partial int GetWindowText(nint hwnd, char* buffer, int max);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetWindowRect(nint hwnd, out RECT rect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetClientRect(nint hwnd, out RECT rect);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    public static partial nint GetWindowLongPtr(nint hwnd, int index);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    public static partial nint SetWindowLongPtr(nint hwnd, int index, nint value);

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial nint SetParent(nint child, nint newParent);

    [LibraryImport("user32.dll")]
    public static partial nint GetParent(nint hwnd);

    [LibraryImport("user32.dll")]
    public static partial nint GetAncestor(nint hwnd, uint flags);

    [LibraryImport("user32.dll")]
    public static partial nint GetWindow(nint hwnd, uint cmd);

    [LibraryImport("user32.dll")]
    public static partial nint GetTopWindow(nint hwnd);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int cx, int cy, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindowVisible(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsIconic(nint hwnd);

    [LibraryImport("user32.dll")]
    public static partial uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [LibraryImport("user32.dll")]
    public static partial nint WindowFromPoint(POINT p);

    [LibraryImport("user32.dll")]
    public static partial nint GetShellWindow();

    [LibraryImport("user32.dll")]
    public static partial nint GetDesktopWindow();

    [LibraryImport("user32.dll")]
    public static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetForegroundWindow(nint hwnd);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageTimeoutW")]
    public static partial nint SendMessageTimeout(nint hwnd, uint msg, nint wParam, nint lParam, uint flags, uint timeoutMs, out nint result);

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostMessage(nint hwnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "RegisterWindowMessageW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint RegisterWindowMessage(string name);

    [LibraryImport("user32.dll")]
    public static partial nint SetWinEventHook(uint eventMin, uint eventMax, nint module, WinEventProc proc, uint processId, uint threadId, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnhookWinEvent(nint hook);

    [LibraryImport("user32.dll")]
    public static partial nint MonitorFromWindow(nint hwnd, uint flags);

    [LibraryImport("user32.dll")]
    public static partial nint MonitorFromPoint(POINT p, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetMonitorInfo(nint monitor, ref MONITORINFO info);

    [LibraryImport("user32.dll")]
    public static partial uint GetDpiForWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    public static partial int GetSystemMetrics(int index);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetCursorPos(out POINT p);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetCursorPos(int x, int y);

    [LibraryImport("user32.dll", SetLastError = true)]
    public static unsafe partial uint SendInput(uint count, INPUT* inputs, int size);

    [LibraryImport("user32.dll", EntryPoint = "PeekMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PeekMessage(out MSG msg, nint hwnd, uint min, uint max, uint remove);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool TranslateMessage(in MSG msg);

    [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")]
    public static partial nint DispatchMessage(in MSG msg);

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    public static partial nint CreateWindowEx(uint exStyle, string className, string windowName, uint style,
        int x, int y, int w, int h, nint parent, nint menu, nint instance, nint param);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ShowWindow(nint hwnd, int cmd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool MapWindowPoints(nint from, nint to, ref RECT points, uint count);
}
