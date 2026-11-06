using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GlassFolders.Native;
using static GlassFolders.Native.NativeMethods;

namespace GlassFolders.App.Poc;

/// <summary>
/// Desktop folder tile: click to open, drag to move, drop files to add.
/// Drag math uses physical cursor pixels so it is independent of the window's DPI.
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

        DragOver += OnTileDragOver;
        Drop += OnTileDrop;
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
    public event Action<FolderTile, string[]>? FilesDropped;

    public void SetOpen(bool open)
    {
        IsOpen = open;
        SetPlate();
    }

    public void SetLabel(string label) => Label.Text = label;

    public void SetColors(Color a, Color b)
    {
        StopA.Color = a;
        StopB.Color = b;
    }

    public void SetItemCount(int count)
    {
        TxtBadgeCount.Text = count.ToString();
        CountBadge.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public void SetGlyph(string glyph)
    {
        string pathData = glyph.ToLowerInvariant() switch
        {
            "briefcase" or "work" =>
                "M20,26 L44,26 L44,46 L20,46 Z M26,26 L26,20 A2,2 0 0 1 28,18 L36,18 A2,2 0 0 1 38,20 L38,26 M20,33 L44,33",
            "gamepad" or "games" =>
                "M18,30 C18,24 23,20 32,20 C41,20 46,24 46,30 C46,38 42,46 39,46 C37,46 36,42 32,42 C28,42 27,46 25,46 C22,46 18,38 18,30 Z M23,29 L27,29 M25,27 L25,31 M37,28 A1.5,1.5 0 1 1 37,28.1 M41,31 A1.5,1.5 0 1 1 41,31.1",
            "play" or "media" =>
                "M24,24 L44,34 L24,44 Z",
            "gear" or "settings" =>
                "M32,22 A8,8 0 1 1 31.9,22 Z M29,18 L35,18 L36,22 L40,24 L43,21 L47,25 L44,29 L46,33 L50,34 L50,40 L46,41 L44,45 L47,49 L43,53 L39,50 L35,52 L34,56 L28,56 L27,52 L23,50 L19,53 L15,49 L18,45 L16,41 L12,40 L12,34 L16,33 L18,29 L15,25 L19,21 L23,24 L27,22 Z",
            "code" =>
                "M25,26 L17,34 L25,42 M39,26 L47,34 L39,42 M34,22 L30,46",
            _ =>
                "M24,28 A2,2 0 0 1 26,26 L38,26 A2,2 0 0 1 40,28 L40,42 A2,2 0 0 1 38,44 L26,44 A2,2 0 0 1 24,42 Z"
        };
        try { GlyphPath.Data = Geometry.Parse(pathData); } catch { }
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

    private void OnTileDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            Plate.Opacity = 0.9;
            e.Handled = true;
        }
    }

    private void OnTileDrop(object sender, DragEventArgs e)
    {
        Plate.Opacity = 0.0;
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files is not null && files.Length > 0)
            {
                FilesDropped?.Invoke(this, files);
            }
            e.Handled = true;
        }
    }
}
