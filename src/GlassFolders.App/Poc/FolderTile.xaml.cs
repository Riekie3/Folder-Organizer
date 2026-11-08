using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GlassFolders.App.Services;
using GlassFolders.App.Themes;
using GlassFolders.Core.Glass;
using GlassFolders.Core.Layout;
using GlassFolders.Core.Models;
using GlassFolders.Native;
using static GlassFolders.Native.NativeMethods;

namespace GlassFolders.App.Poc;

/// <summary>
/// A folder on the desktop: the folder art and its label, on a frosted plate when hovered or open.
/// Click to open, drag to move, drop files to add. Drag maths uses physical cursor pixels, so it doesn't
/// depend on the window's DPI.
/// </summary>
public partial class FolderTile : UserControl
{
    private const int DragThreshold = 4;
    private const double HoverPlateOpacity = 0.7;

    private static readonly Brush DarkInkBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x1C, 0x22, 0x30)));
    private static readonly Brush OutlineBrush = Frozen(new SolidColorBrush(Color.FromArgb(0xE0, 0x24, 0x29, 0x34)));
    private static readonly Brush GlowBrush = Frozen(new SolidColorBrush(Color.FromArgb(0x8C, 0xFF, 0xFF, 0xFF)));

    private static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }

    private POINT _pressCursor;
    private RECT _pressRect;
    private bool _pressed, _dragging, _dragOver;
    private double _luminance;
    private FolderTileMetrics _metrics;

    public FolderTile(string id)
    {
        InitializeComponent();
        Id = id;
        MouseEnter += (_, _) => UpdatePlate();
        MouseLeave += (_, _) => UpdatePlate();

        DragEnter += OnTileDragOver;
        DragOver += OnTileDragOver;
        DragLeave += (_, _) => { _dragOver = false; UpdatePlate(); };
        Drop += OnTileDrop;
    }

    public string Id { get; }

    /// <summary>Optional diagnostic sink for input events.</summary>
    public static Action<string>? Trace;
    public bool IsOpen { get; private set; }

    /// <summary>The sizes this tile is drawn at, in DIPs.</summary>
    public FolderTileMetrics Metrics => _metrics;

    /// <summary>Provides the current screen rect of the hosting window.</summary>
    public Func<RECT>? GetScreenRect { get; set; }

    public event Action<FolderTile>? Clicked;
    public event Action<FolderTile, int, int>? DragMoved;   // new top-left, screen px
    public event Action<FolderTile, int, int>? DragEnded;
    public event Action<FolderTile, string[]>? FilesDropped;

    /// <summary>Draws the folder at the given size, with its name, art and optional item count.</summary>
    public void Apply(FolderDef folder, FolderTileMetrics metrics, bool showCount)
    {
        _metrics = metrics;

        Root.Width = metrics.WindowWidth;
        Root.Height = metrics.WindowHeight;
        Plate.Width = metrics.PlateWidth;
        Plate.Height = metrics.PlateHeight;
        Box.Width = metrics.BoxWidth;
        Box.Height = metrics.BoxHeight;

        BoxStack.Margin = new Thickness(0, FolderTileMetrics.BoxTopPadding, 0, 0);
        Art.Width = metrics.IconWidth;
        Art.Height = metrics.IconHeight;
        Art.Show(folder);

        Label.Text = folder.Name;
        Label.FontSize = metrics.LabelFontSize;
        Label.Height = metrics.LabelLineHeight;
        Label.MaxWidth = metrics.BoxWidth;
        Label.Margin = new Thickness(0, FolderTileMetrics.IconLabelGap, 0, 0);
        ToolTip = folder.Name;

        int count = folder.Items.Count;
        TxtBadgeCount.Text = count.ToString();
        CountBadge.Visibility = showCount && count > 0 ? Visibility.Visible : Visibility.Collapsed;

        UpdateLabel();
    }

    /// <summary>
    /// Sets what is behind the tile: the blurred wallpaper for the plate (or a flat colour, or nothing), and the
    /// wallpaper's brightness under the label, as relative luminance.
    /// </summary>
    public void SetBackdrop(Brush? plateBackdrop, double labelLuminance)
    {
        PlateBackdrop.Background = plateBackdrop;
        _luminance = double.IsFinite(labelLuminance) ? Math.Clamp(labelLuminance, 0, 1) : 0;
        UpdateLabel();
    }

    public void SetOpen(bool open)
    {
        IsOpen = open;
        UpdatePlate();
    }

    private bool PlateShown => IsOpen || _dragOver || IsMouseOver;

    private void UpdatePlate()
    {
        double target = IsOpen || _dragOver ? 1.0 : IsMouseOver ? HoverPlateOpacity : 0.0;
        Animate.Fade(Plate, target, TimeSpan.FromMilliseconds(150), AnimationCategory.TileHover);
        UpdateLabel();
    }

    private void UpdateLabel()
    {
        var style = PlateShown ? LabelContrast.OnPlate(_luminance) : LabelContrast.OnWallpaper(_luminance);
        bool dark = style.Ink == LabelInk.Dark;
        Label.Fill = dark ? DarkInkBrush : Brushes.White;
        Label.Stroke = dark ? GlowBrush : OutlineBrush;
        // A firmer edge on bright wallpapers; on dark ones the outline only needs to be a hint.
        Label.StrokeThickness = dark ? 2.4 : style.ShadowOpacity >= 0.9 ? 2.2 : 1.6;
        LabelShadow.Opacity = dark ? 0 : style.ShadowOpacity * 0.4;
        LabelShadow.BlurRadius = style.ShadowBlur;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        GetCursorPos(out _pressCursor);
        _pressRect = GetScreenRect?.Invoke() ?? default;
        _pressed = true;
        _dragging = false;
        bool captured = CaptureMouse();
        Animate.Scale(Box, 0.96, TimeSpan.FromMilliseconds(100), AnimationCategory.ItemPress);
        Trace?.Invoke($"DOWN {Id} captured={captured} clicks={e.ClickCount}");
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!_pressed) return;
        GetCursorPos(out var cur);
        int dx = cur.X - _pressCursor.X;
        int dy = cur.Y - _pressCursor.Y;
        if (!_dragging && (Math.Abs(dx) >= DragThreshold || Math.Abs(dy) >= DragThreshold))
        {
            _dragging = true;
            Trace?.Invoke($"DRAG-START {Id}");
        }
        if (_dragging)
        {
            DragMoved?.Invoke(this, _pressRect.Left + dx, _pressRect.Top + dy);
            e.Handled = true;
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        Animate.Scale(Box, 1.0, TimeSpan.FromMilliseconds(100), AnimationCategory.ItemPress);

        if (!_pressed) return;
        bool wasDragging = _dragging;
        _pressed = false;
        _dragging = false;
        ReleaseMouseCapture();

        if (wasDragging)
        {
            GetCursorPos(out var cur);
            int dx = cur.X - _pressCursor.X;
            int dy = cur.Y - _pressCursor.Y;
            Trace?.Invoke($"DRAG-END {Id} dx={dx} dy={dy}");
            DragEnded?.Invoke(this, _pressRect.Left + dx, _pressRect.Top + dy);
        }
        else
        {
            Trace?.Invoke($"CLICK {Id}");
            Clicked?.Invoke(this);
        }
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        Animate.Scale(Box, 1.0, TimeSpan.FromMilliseconds(100), AnimationCategory.ItemPress);
        _pressed = false;
        _dragging = false;
        base.OnLostMouseCapture(e);
    }

    private void OnTileDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy | DragDropEffects.Link;
            if (!_dragOver)
            {
                _dragOver = true;
                UpdatePlate();
            }
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void OnTileDrop(object sender, DragEventArgs e)
    {
        _dragOver = false;
        UpdatePlate();
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            try
            {
                if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
                {
                    FilesDropped?.Invoke(this, files);
                }
            }
            catch (Exception ex)
            {
                DiagnosticService.Instance.LogError("Error handling dropped files on tile", ex);
            }
        }
        e.Handled = true;
    }
}
