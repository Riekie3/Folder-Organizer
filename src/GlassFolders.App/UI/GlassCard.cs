using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Effects;
using GlassFolders.App.Services;
using GlassFolders.Core.Glass;
using GlassFolders.Core.Models;

namespace GlassFolders.App.UI;

/// <summary>
/// A rounded glass panel: frosted snapshot, tint, top gloss, light rim and a soft shadow.
/// Without a snapshot it paints a solid panel, which covers high contrast, transparency off and capture failure.
/// The host window must leave room around the card for the shadow (see <see cref="ShadowMargin"/>).
/// </summary>
[ContentProperty(nameof(CardContent))]
public sealed class GlassCard : UserControl
{
    /// <summary>Space, in DIPs, that the shadow needs on every side.</summary>
    public const double ShadowMargin = 40;

    private static readonly Brush SolidPanel = Freeze(new LinearGradientBrush(
        Color.FromRgb(0x1A, 0x2B, 0x52), Color.FromRgb(0x10, 0x1B, 0x36), new Point(0, 0), new Point(1, 1)));

    private static readonly Brush LightSolidPanel = Freeze(new LinearGradientBrush(
        Color.FromRgb(0xF4, 0xF8, 0xFF), Color.FromRgb(0xDC, 0xE8, 0xFA), new Point(0, 0), new Point(1, 1)));

    private static readonly Brush Gloss = Freeze(new LinearGradientBrush(
        new GradientStopCollection
        {
            new(Color.FromArgb((byte)Math.Round(GlassTint.TopGlossOpacity * 255), 255, 255, 255), 0),
            new(Color.FromArgb(0, 255, 255, 255), 0.25),
        },
        new Point(0, 0), new Point(0, 1)));

    private static readonly Brush Rim = Freeze(new SolidColorBrush(Color.FromRgb(255, 255, 255)));
    private static readonly Brush LightRim = Freeze(new SolidColorBrush(Color.FromRgb(255, 255, 255)));

    private readonly DropShadowEffect _shadowEffect = new()
    {
        BlurRadius = 48,
        ShadowDepth = 14,
        Direction = 270,
        Opacity = 0.38,
        Color = Color.FromRgb(0x0A, 0x1E, 0x78),
    };
    private readonly Border _shadow;
    private readonly Border _backdrop = new() { Background = SolidPanel };
    private readonly Border _scrim = new() { IsHitTestVisible = false };
    private readonly Border _tint = new() { IsHitTestVisible = false };
    private readonly Border _gloss = new() { Background = Gloss, IsHitTestVisible = false };
    private readonly Border _rim = new() { BorderBrush = Rim, BorderThickness = new Thickness(1.2), IsHitTestVisible = false };
    private readonly ContentPresenter _content = new();

    private BackdropSnapshot? _snapshot;
    private ImageBrush? _snapshotBrush;
    private Window? _hostWindow;
    private double _tintStrength = 1;
    private bool _light;
    private bool _outsideSnapshot;

    public GlassCard()
    {
        _shadow = new Border { Background = Brushes.Black, IsHitTestVisible = false, Effect = _shadowEffect };
        var root = new Grid();
        // Separate from the card so items and text aren't drawn through the shadow's effect pass.
        root.Children.Add(_shadow);
        root.Children.Add(_backdrop);
        root.Children.Add(_scrim);
        root.Children.Add(_tint);
        root.Children.Add(_gloss);
        root.Children.Add(_content);
        root.Children.Add(_rim);
        Content = root;

        ApplyCornerRadius(CornerRadius);

        SizeChanged += (_, _) => AlignBackdrop();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(
        nameof(CornerRadius), typeof(double), typeof(GlassCard),
        new PropertyMetadata(28.0, (d, e) => ((GlassCard)d).ApplyCornerRadius((double)e.NewValue)));

    public double CornerRadius
    {
        get => (double)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public object? CardContent
    {
        get => _content.Content;
        set => _content.Content = value;
    }

    /// <summary>The snapshot in use, or null when showing the solid panel.</summary>
    public BackdropSnapshot? Backdrop => _snapshot;

    /// <summary>The tint currently applied, or null when showing the solid panel.</summary>
    public GlassTint? CurrentTint { get; private set; }

    /// <summary>Shows a snapshot behind the card. Null switches to the solid panel.</summary>
    public void SetBackdrop(BackdropSnapshot? snapshot)
    {
        _snapshot = snapshot;
        if (snapshot is null)
        {
            _snapshotBrush = null;
            CurrentTint = null;
            _backdrop.Background = SolidFor(_light);
            _scrim.Background = null;
            _tint.Background = null;
            return;
        }

        _snapshotBrush = new ImageBrush(snapshot.Image)
        {
            Stretch = Stretch.Fill,
            ViewboxUnits = BrushMappingMode.RelativeToBoundingBox,
        };
        RenderOptions.SetBitmapScalingMode(_snapshotBrush, BitmapScalingMode.HighQuality);
        _backdrop.Background = _snapshotBrush;
        AlignBackdrop();
    }

    /// <summary>
    /// Applies the Appearance settings: tint strength, edge, shadow and corners. <paramref name="light"/> switches to the light
    /// theme's milky panel. Takes effect at once, including on a card that is already open.
    /// </summary>
    public void ApplyAppearance(GlassAppearance appearance, bool light, bool chrome = false)
    {
        _tintStrength = appearance.TintStrength;
        bool themeChanged = _light != light;
        _light = light;
        CornerRadius = chrome ? appearance.ChromeCornerRadius : appearance.CornerRadius;

        _rim.Opacity = appearance.RimOpacity;
        _rim.BorderBrush = light ? LightRim : Rim;
        _shadowEffect.Opacity = appearance.ShadowOpacity;
        _shadow.Visibility = appearance.ShadowOpacity <= 0.001 ? Visibility.Collapsed : Visibility.Visible;
        _gloss.Opacity = light ? 0.55 : 1;

        if (_snapshot is null)
        {
            _backdrop.Background = SolidFor(light);
        }
        else if (themeChanged || CurrentTint is not null)
        {
            CurrentTint = null; // force the tint to be rebuilt for the new strength or theme
            AlignBackdrop();
        }
    }

    private static Brush SolidFor(bool light) => light ? LightSolidPanel : SolidPanel;

    /// <summary>
    /// Measures the card against the snapshot again. A host that places itself after giving the card its snapshot
    /// calls this once it is in its final spot; until then the card may have been measured at the wrong place.
    /// </summary>
    public void RealignBackdrop() => AlignBackdrop();

    /// <summary>Lines the snapshot up with where the card is on screen. Runs when the card moves or resizes.</summary>
    private void AlignBackdrop()
    {
        if (_snapshot is null || _snapshotBrush is null || ActualWidth <= 0 || ActualHeight <= 0) return;
        if (PresentationSource.FromVisual(this) is null) return;

        // Measured from the parent, so an open or close animation on this card doesn't move the glass.
        if (VisualTreeHelper.GetParent(this) is not Visual parent) return;
        Vector offset = VisualTreeHelper.GetOffset(this);
        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        Point topLeft = parent.PointToScreen(new Point(offset.X, offset.Y));
        var card = new Rect(topLeft.X, topLeft.Y, ActualWidth * dpi.DpiScaleX, ActualHeight * dpi.DpiScaleY);

        // A card dragged to a different monitor has no snapshot under it, so it shows the solid panel until it comes back.
        var area = _snapshot.ScreenArea;
        double cx = card.X + card.Width / 2, cy = card.Y + card.Height / 2;
        bool outside = cx < area.Left || cx > area.Right || cy < area.Top || cy > area.Bottom;        if (outside != _outsideSnapshot)
        {
            _outsideSnapshot = outside;
            _backdrop.Background = outside ? SolidFor(_light) : _snapshotBrush;
            if (outside)
            {
                CurrentTint = null;
                _scrim.Background = null;
                _tint.Background = null;
            }
        }
        if (outside) return;

        // If the card only partly leaves the captured area, slide the image rather than show a gap.
        // The blur is soft enough that the small shift can't be seen.
        double w = Math.Min(1, card.Width / area.Width), h = Math.Min(1, card.Height / area.Height);
        _snapshotBrush.Viewbox = new Rect(
            Math.Clamp((card.X - area.Left) / area.Width, 0, 1 - w),
            Math.Clamp((card.Y - area.Top) / area.Height, 0, 1 - h),
            w,
            h);

        ApplyTint(_snapshot.TintFor(card, _tintStrength, _light));
    }

    private void ApplyTint(GlassTint tint)
    {
        if (CurrentTint == tint) return;
        CurrentTint = tint;
        _scrim.Background = tint.Scrim.A == 0 ? null : Freeze(new SolidColorBrush(ToColor(tint.Scrim)));
        _tint.Background = Freeze(new LinearGradientBrush(ToColor(tint.Top), ToColor(tint.Bottom), 55));
    }

    private void ApplyCornerRadius(double radius)
    {
        var corners = new CornerRadius(radius);
        _shadow.CornerRadius = corners;
        _backdrop.CornerRadius = corners;
        _scrim.CornerRadius = corners;
        _tint.CornerRadius = corners;
        _gloss.CornerRadius = corners;
        _rim.CornerRadius = corners;
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        AlignBackdrop();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _hostWindow = Window.GetWindow(this);
        if (_hostWindow is not null) _hostWindow.LocationChanged += OnHostMoved;
        AlignBackdrop();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_hostWindow is not null) _hostWindow.LocationChanged -= OnHostMoved;
        _hostWindow = null;
    }

    private void OnHostMoved(object? sender, EventArgs e) => AlignBackdrop();

    private static Color ToColor(TintColor c) => Color.FromArgb(c.A, c.R, c.G, c.B);

    private static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
