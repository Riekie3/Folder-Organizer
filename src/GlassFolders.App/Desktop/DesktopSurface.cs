using System.Windows;
using System.Windows.Interop;
using GlassFolders.Native;
using static GlassFolders.Native.NativeMethods;

namespace GlassFolders.App.Desktop;

/// <summary>One native window on the desktop layer that hosts a WPF element (a desktop folder).</summary>
public sealed class DesktopSurface : IDisposable
{
    private bool _disposed;
    private readonly Action? _onDispose;

    internal DesktopSurface(string id, HwndSource source, UIElement content, Action? onDispose = null)
    {
        Id = id;
        Source = source;
        Content = content;
        _onDispose = onDispose;
        source.Disposed += (_, _) =>
        {
            if (_disposed) return;
            _disposed = true;
            Lost?.Invoke(this);
        };
    }

    public string Id { get; }
    public HwndSource Source { get; }
    public UIElement Content { get; }
    public nint Hwnd => Source.Handle;
    public bool IsAlive => !_disposed && IsWindow(Hwnd);

    /// <summary>Screen rectangle in physical pixels.</summary>
    public RECT ScreenRect => Win32.Rect(Hwnd);

    /// <summary>Raised when the window is destroyed by someone else (e.g. Explorer tore down its parent).</summary>
    public event Action<DesktopSurface>? Lost;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _onDispose?.Invoke();
        Source.RootVisual = null;
        Source.Dispose();
    }
}
