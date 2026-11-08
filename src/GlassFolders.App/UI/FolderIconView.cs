using File = System.IO.File;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using GlassFolders.Core.Models;

namespace GlassFolders.App.UI;

/// <summary>
/// A folder's art, drawn from shapes so it is sharp at any size:
/// <list type="bullet">
/// <item>Solid (reference 1): a darker back with a tab, a lighter front and a white glyph.</item>
/// <item>Glass (reference 4): a coloured back with a soft glow and a frosted front flap.</item>
/// <item>A picture the user chose, in place of either.</item>
/// </list>
/// </summary>
public sealed class FolderIconView : Viewbox
{
    private const double DesignWidth = 64, DesignHeight = 54;

    private static readonly Geometry SolidBack = Frozen(Geometry.Parse(
        "M0,6 A6,6 0 0 1 6,0 L21,0 A5,5 0 0 1 24.6,1.5 L29,6 L58,6 A6,6 0 0 1 64,12 L64,30 L0,30 Z"));

    private static readonly Geometry SolidFront = Frozen(Geometry.Parse(
        "M0,14 A6,6 0 0 1 6,8 L58,8 A6,6 0 0 1 64,14 L64,47 A7,7 0 0 1 57,54 L7,54 A7,7 0 0 1 0,47 Z"));

    // Traced from reference 4: an upright back with a tab, and a front flap leaning to the right.
    private static readonly Geometry GlassBack = Frozen(Geometry.Parse(
        "M0,6 A4.2,4.2 0 0 1 4.2,1.8 L21,1.8 A2.5,2.5 0 0 1 23.3,3.3 L25.3,7.4 A2.5,2.5 0 0 0 27.5,8.2 L50.6,8.2 " +
        "A4.2,4.2 0 0 1 54.8,12.4 L54.8,44.5 A4.2,4.2 0 0 1 50.6,48.7 L4.2,48.7 A4.2,4.2 0 0 1 0,44.5 Z"));

    private static readonly Geometry GlassFront = Frozen(Geometry.Parse(
        "M17.1,15 L61,15 A3,3 0 0 1 63.9,18.6 L58.3,49.2 A3.6,3.6 0 0 1 54.8,52.2 L8.9,52.2 " +
        "A2.8,2.8 0 0 1 6.2,48.8 L13.2,18.2 A4,4 0 0 1 17.1,15 Z"));

    private static readonly Brush GlassRim = Frozen(new LinearGradientBrush(
        Color.FromArgb(0xF0, 255, 255, 255), Color.FromArgb(0x70, 255, 255, 255), 90));

    private readonly Canvas _art = new() { Width = DesignWidth, Height = DesignHeight };
    private readonly Path _back = new();
    private readonly Path _front = new();
    private readonly Path _glyph = new();
    private readonly Image _picture = new() { Stretch = Stretch.Uniform };
    private readonly Grid _root = new() { Width = DesignWidth, Height = DesignHeight };

    public FolderIconView()
    {
        _art.Children.Add(_back);
        _art.Children.Add(_front);
        _art.Children.Add(_glyph);
        _root.Children.Add(_art);
        _root.Children.Add(_picture);
        RenderOptions.SetBitmapScalingMode(_picture, BitmapScalingMode.HighQuality);
        Child = _root;
        Stretch = Stretch.Uniform;
    }

    /// <summary>Draws this folder's colours, style and glyph, or its picture.</summary>
    public void Show(FolderDef folder)
    {
        if (TryLoadPicture(folder.CustomIconPath) is { } picture)
        {
            _picture.Source = picture;
            _picture.Visibility = Visibility.Visible;
            _art.Visibility = Visibility.Collapsed;
            return;
        }

        _picture.Source = null;
        _picture.Visibility = Visibility.Collapsed;
        _art.Visibility = Visibility.Visible;

        Color front = Parse(folder.ColorA, Parse(FolderPalette.Default.Front, Colors.DeepSkyBlue));
        Color back = Parse(folder.ColorB, Parse(FolderPalette.Default.Back, Colors.DodgerBlue));
        var glyph = folder.Glyph == "Folder" ? null : TryFindResource("Glyph" + folder.Glyph) as Geometry;

        if (folder.Style == FolderStyle.Glass) DrawGlass(front, back, glyph);
        else DrawSolid(front, back, glyph);
    }

    private void DrawSolid(Color front, Color back, Geometry? glyph)
    {
        // Measured from the reference: the front is nearly flat, lifting a little towards the top,
        // and the back is the deep tone, a touch lighter at its top edge.
        _back.Data = SolidBack;
        _back.Fill = Frozen(new LinearGradientBrush(Mix(back, front, 0.22), back, 90));
        _back.Effect = null;

        _front.Data = SolidFront;
        _front.Fill = Frozen(new LinearGradientBrush(Mix(front, Colors.White, 0.1), Mix(front, back, 0.06), 90));
        _front.Stroke = null;

        _glyph.Data = glyph;
        _glyph.Fill = Brushes.White;
        _glyph.Opacity = 1;
        _glyph.Effect = new DropShadowEffect
        {
            BlurRadius = 5,
            ShadowDepth = 1.5,
            Direction = 270,
            Opacity = 0.3,
            Color = Mix(back, Colors.Black, 0.45),
        };
    }

    private void DrawGlass(Color front, Color back, Geometry? glyph)
    {
        // Reference 4: the back runs from the light tone at the bottom left to the deep tone at the top right,
        // and glows; the flap is frosted white, brightest along its right edge.
        _back.Data = GlassBack;
        _back.Fill = Frozen(new LinearGradientBrush(front, back, new Point(0, 1), new Point(1, 0)));
        _back.Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 0, Opacity = 0.65, Color = back };

        _front.Data = GlassFront;
        _front.Fill = Frozen(new LinearGradientBrush(
            new GradientStopCollection
            {
                new(WithAlpha(Mix(front, Colors.White, 0.35), 0xA6), 0),
                new(WithAlpha(Mix(back, Colors.White, 0.45), 0xA0), 0.72),
                new(Color.FromArgb(0xF0, 0xF2, 0xF3, 0xFF), 1),
            },
            new Point(0, 0.5), new Point(1, 0.5)));
        _front.Stroke = GlassRim;
        _front.StrokeThickness = 0.7;

        // On the light flap a white glyph would vanish, so it takes the folder's deep colour, a little smaller,
        // centred on the flap.
        if (glyph is null)
        {
            _glyph.Data = null;
        }
        else
        {
            var onFlap = Frozen(new TransformGroup { Children = { new ScaleTransform(0.8, 0.8, 32, 34), new TranslateTransform(3, 0) } });
            _glyph.Data = Frozen(new GeometryGroup { Children = { glyph }, Transform = onFlap });
        }
        _glyph.Fill = new SolidColorBrush(Mix(back, Colors.Black, 0.1));
        _glyph.Opacity = 0.85;
        _glyph.Effect = null;
    }

    private BitmapSource? TryLoadPicture(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            // Big enough for a large folder on a 200 % screen, small enough to keep memory down.
            image.DecodePixelWidth = 192;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is System.IO.IOException or NotSupportedException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            Services.DiagnosticService.Instance.LogWarn($"Folder picture could not be read, drawing the folder instead: {ex.Message}");
            return null;
        }
    }

    private static Color Mix(Color a, Color b, double t) => Color.FromRgb(
        (byte)Math.Round(a.R + (b.R - a.R) * t),
        (byte)Math.Round(a.G + (b.G - a.G) * t),
        (byte)Math.Round(a.B + (b.B - a.B) * t));

    private static Color WithAlpha(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);

    private static Color Parse(string hex, Color fallback)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(hex);
        }
        catch (FormatException)
        {
            return fallback;
        }
    }

    private static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
