using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace GlassFolders.App.UI;

/// <summary>
/// One line of text with an outline drawn around each letter, as the reference draws desktop labels:
/// white text with a tight dark edge that reads on any wallpaper. Long text ends in an ellipsis.
/// </summary>
public sealed class OutlinedText : FrameworkElement
{
    private FormattedText? _formatted;

    public static readonly DependencyProperty TextProperty = Register(nameof(Text), typeof(string), "");
    public static readonly DependencyProperty FontSizeProperty = Register(nameof(FontSize), typeof(double), 12.0);
    public static readonly DependencyProperty FillProperty = Register(nameof(Fill), typeof(Brush), Brushes.White);
    public static readonly DependencyProperty StrokeProperty = Register(nameof(Stroke), typeof(Brush), Brushes.Black);
    public static readonly DependencyProperty StrokeThicknessProperty = Register(nameof(StrokeThickness), typeof(double), 2.0);

    private static readonly Typeface Face = new(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public double FontSize { get => (double)GetValue(FontSizeProperty); set => SetValue(FontSizeProperty, value); }
    public Brush Fill { get => (Brush)GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }

    /// <summary>Total stroke width; half of it shows outside the letters.</summary>
    public double StrokeThickness { get => (double)GetValue(StrokeThicknessProperty); set => SetValue(StrokeThicknessProperty, value); }

    private static DependencyProperty Register(string name, Type type, object fallback) =>
        DependencyProperty.Register(name, type, typeof(OutlinedText),
            new FrameworkPropertyMetadata(fallback, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    protected override Size MeasureOverride(Size available)
    {
        double pad = StrokeThickness / 2;
        _formatted = new FormattedText(Text ?? "", CultureInfo.CurrentUICulture, FlowDirection, Face, FontSize, Fill,
            VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Left,
        };
        double limit = Math.Min(available.Width, MaxWidth);
        if (double.IsFinite(limit)) _formatted.MaxTextWidth = Math.Max(1, limit - 2 * pad);

        return new Size(_formatted.WidthIncludingTrailingWhitespace + 2 * pad, _formatted.Height);
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_formatted is null || string.IsNullOrEmpty(Text)) return;
        double pad = StrokeThickness / 2;
        var origin = new Point(pad, 0);

        if (StrokeThickness > 0 && Stroke is not null)
        {
            var pen = new Pen(Stroke, StrokeThickness) { LineJoin = PenLineJoin.Round };
            pen.Freeze();
            dc.DrawGeometry(null, pen, _formatted.BuildGeometry(origin));
        }
        // The fill as real text keeps the letters as crisp as any other label.
        _formatted.SetForegroundBrush(Fill);
        dc.DrawText(_formatted, origin);
    }
}
