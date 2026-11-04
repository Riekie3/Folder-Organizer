using System.Windows;
using System.Windows.Interop;
using GlassFolders.Native;
using static GlassFolders.Native.NativeMethods;

namespace GlassFolders.App.Desktop;

/// <summary>
/// Option B: each surface is a child window inside Explorer's desktop, either inside SHELLDLL_DefView
/// (above the icon list view) or directly inside Progman (above DefView).
/// Being a real child of the desktop means it can never appear over app windows, but it ties our
/// input queue to Explorer's (cross-process parenting) and dies whenever Explorer recreates the desktop.
/// </summary>
public sealed class DesktopChildHost(bool useDefView) : IDesktopHost
{
    public string Name => useDefView ? "child-defview" : "child-progman";
    public DesktopWindows? Desktop { get; private set; }

    private nint Parent => useDefView ? Desktop!.DefView : Desktop!.Progman;

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

        var client = ToParentClient(x, y);
        var p = new HwndSourceParameters("GlassFolders.Tile." + id)
        {
            WindowStyle = unchecked((int)(WS_CHILD | WS_CLIPSIBLINGS | WS_CLIPCHILDREN)),
            ParentWindow = Parent,
            PositionX = client.X,
            PositionY = client.Y,
            UsesPerPixelTransparency = true,
        };
        var source = new HwndSource(p) { SizeToContent = SizeToContent.WidthAndHeight, RootVisual = content as System.Windows.Media.Visual };
        var surface = new DesktopSurface(id, source, content);
        SetWindowPos(surface.Hwnd, HWND_TOP, client.X, client.Y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        return surface;
    }

    public bool Reattach(DesktopSurface surface)
    {
        // A child window cannot outlive its parent; if it survived (e.g. only DefView moved), re-parent it.
        if (!surface.IsAlive || Desktop is null) return false;
        var rect = surface.ScreenRect;
        SetParent(surface.Hwnd, Parent);
        var client = ToParentClient(rect.Left, rect.Top);
        SetWindowPos(surface.Hwnd, HWND_TOP, client.X, client.Y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        return true;
    }

    public void Move(DesktopSurface surface, int x, int y)
    {
        var client = ToParentClient(x, y);
        SetWindowPos(surface.Hwnd, HWND_TOP, client.X, client.Y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
    }

    public bool IsAttached(DesktopSurface surface) =>
        surface.IsAlive && Desktop is not null && GetParent(surface.Hwnd) == Parent && IsWindowVisible(surface.Hwnd);

    private POINT ToParentClient(int screenX, int screenY)
    {
        var r = new RECT(screenX, screenY, screenX, screenY);
        MapWindowPoints(0, Parent, ref r, 1);
        return new POINT(r.Left, r.Top);
    }
}
