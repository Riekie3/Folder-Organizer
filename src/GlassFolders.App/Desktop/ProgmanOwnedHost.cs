using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using GlassFolders.Native;
using static GlassFolders.Native.NativeMethods;

namespace GlassFolders.App.Desktop;

/// <summary>
/// Option A: each surface is a top-level popup owned by Progman.
/// Owned windows always stay above their owner, so the surface sits just above the desktop and below
/// every normal app window. It is a non-activating tool window: never in the taskbar or Alt-Tab,
/// and a click on it does not bring it in front of other windows.
/// </summary>
public sealed class ProgmanOwnedHost : IDesktopHost
{
    public string Name => "owned";
    public DesktopWindows? Desktop { get; private set; }

    public bool RefreshDesktop()
    {
        var d = DesktopWindows.Find();
        Desktop = d.IsValid ? d : null;
        return Desktop is not null;
    }

    public DesktopSurface CreateSurface(string id, UIElement content, int x, int y)
    {
        if (Desktop is null && !RefreshDesktop())
            throw new InvalidOperationException("Desktop shell is not available.");

        var p = new HwndSourceParameters("GlassFolders.Tile." + id)
        {
            WindowStyle = unchecked((int)(WS_POPUP | WS_CLIPCHILDREN)),
            ExtendedWindowStyle = (int)(WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE),
            PositionX = x,
            PositionY = y,
            UsesPerPixelTransparency = true,
        };
        var source = new HwndSource(p) { SizeToContent = SizeToContent.WidthAndHeight, RootVisual = content as System.Windows.Media.Visual };
        source.AddHook(WndProc);

        var surface = new DesktopSurface(id, source, content);
        Attach(surface.Hwnd);
        SetWindowPos(surface.Hwnd, HWND_BOTTOM, x, y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        return surface;
    }

    public bool Reattach(DesktopSurface surface)
    {
        if (!surface.IsAlive || Desktop is null) return false;
        Attach(surface.Hwnd);
        SetWindowPos(surface.Hwnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        return true;
    }

    public void Move(DesktopSurface surface, int x, int y) =>
        SetWindowPos(surface.Hwnd, 0, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);

    public bool IsAttached(DesktopSurface surface) =>
        surface.IsAlive && Desktop is not null && Win32.Owner(surface.Hwnd) == Desktop.Progman && IsWindowVisible(surface.Hwnd);

    private void Attach(nint hwnd) => SetWindowLongPtr(hwnd, GWLP_HWNDPARENT, Desktop!.Progman);

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        switch ((uint)msg)
        {
            case WM_MOUSEACTIVATE:
                handled = true;
                return MA_NOACTIVATE;

            case WM_WINDOWPOSCHANGING:
                // Never let the surface be raised above other windows (clicks, ShowWindow, drag loops).
                // Pushing it to the bottom is safe: the window manager keeps owned windows above their owner.
                var pos = Marshal.PtrToStructure<WINDOWPOS>(lParam);
                if ((pos.flags & SWP_NOZORDER) == 0 && pos.hwndInsertAfter != HWND_BOTTOM)
                {
                    pos.hwndInsertAfter = HWND_BOTTOM;
                    Marshal.StructureToPtr(pos, lParam, false);
                }
                break;
        }
        return 0;
    }
}
