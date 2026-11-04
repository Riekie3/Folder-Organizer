using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GlassFolders.Native;
using static GlassFolders.Native.NativeMethods;

namespace GlassFolders.App.Poc;

/// <summary>
/// PoC desktop folder: click to open, drag to move. Drag math uses physical cursor pixels so it is
/// independent of the window's DPI and of whether the surface is a top-level or child window.
/// </summary>
public partial class FolderTile : UserControl
{
    private const int DragThreshold = 4;
    private POINT _pressCursor;
    private RECT _pressRect;
    private bool _pressed, _dragging;

    public FolderTile(string id, string label, Color a, Color b)
    {
        InitializeComponent();
        Id = id;
        Label.Text = label;
        StopA.Color = a;
        StopB.Color = b;
        MouseEnter += (_, _) => SetPlate();
        MouseLeave += (_, _) => SetPlate();
    }

    public string Id { get; }

    /// <summary>Optional diagnostic sink for input events.</summary>
    public static Action<string>? Trace;
    public bool IsOpen { get; private set; }

    /// <summary>Provides the current screen rect of the hosting window.</summary>
    public Func<RECT>? GetScreenRect { get; set; }

    public event Action<FolderTile>? Clicked;
    public event Action<FolderTile, int, int>? DragMoved;   // new top-left, screen px
    public event Action<FolderTile, int, int>? DragEnded;

    public void SetOpen(bool open)
    {
        IsOpen = open;
        SetPlate();
    }

    private void SetPlate() => Plate.Opacity = IsOpen ? 1.0 : IsMouseOver ? 0.7 : 0.0;

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        GetCursorPos(out _pressCursor);
        _pressRect = GetScreenRect?.Invoke() ?? default;
        _pressed = true;
        _dragging = false;
        bool captured = CaptureMouse();
        Trace?.Invoke($"DOWN {Id} captured={captured} clicks={e.ClickCount}");
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!_pressed) return;
        GetCursorPos(out var now);
        int dx = now.X - _pressCursor.X, dy = now.Y - _pressCursor.Y;
        if (!_dragging && Math.Abs(dx) + Math.Abs(dy) < DragThreshold) return;
        if (!_dragging) Trace?.Invoke($"DRAG-START {Id}");
        _dragging = true;
        DragMoved?.Invoke(this, _pressRect.Left + dx, _pressRect.Top + dy);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        Trace?.Invoke($"UP {Id} pressed={_pressed} dragging={_dragging}");
        if (!_pressed) return;
        // ReleaseMouseCapture raises LostMouseCapture synchronously, which clears the flags, so read them first.
        bool wasDragging = _dragging;
        _pressed = false;
        ReleaseMouseCapture();
        if (wasDragging)
        {
            var r = GetScreenRect?.Invoke() ?? default;
            DragEnded?.Invoke(this, r.Left, r.Top);
        }
        else
        {
            Clicked?.Invoke(this);
        }
        _dragging = false;
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        Trace?.Invoke($"LOST-CAPTURE {Id} pressed={_pressed} dragging={_dragging}");
        _pressed = false;
        _dragging = false;
    }
}
