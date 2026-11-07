using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using GlassFolders.Core.Models;

namespace GlassFolders.App.UI;

/// <summary>
/// The solid coloured folder from the reference: a darker back with a tab, a lighter front and a white glyph.
/// Scales to any size, because it is drawn from shapes.
/// </summary>
public sealed class FolderIconView : Viewbox
{
    private const double DesignWidth = 64, DesignHeight = 54;

    private static readonly Geometry BackShape = Frozen(Geometry.Parse(
        "M0,6 A6,6 0 0 1 6,0 L21,0 A5,5 0 0 1 24.6,1.5 L29,6 L58,6 A6,6 0 0 1 64,12 L64,30 L0,30 Z"));

    private static readonly Geometry FrontShape = Frozen(Geometry.Parse(
        "M0,14 A6,6 0 0 1 6,8 L58,8 A6,6 0 0 1 64,14 L64,47 A7,7 0 0 1 57,54 L7,54 A7,7 0 0 1 0,47 Z"));

    private readonly Path _back = new() { Data = BackShape };
    private readonly Path _front = new() { Data = FrontShape };
    private readonly Path _glyph = new()
    {
        Fill = Brushes.White,
        Effect = new DropShadowEffect { BlurRadius = 5, ShadowDepth = 1.5, Direction = 270, Opacity = 0.28, Color = Color.FromRgb(0x08, 0x2A, 0x78) },
    };

    public FolderIconView()
    {
        var art = new Canvas { Width = DesignWidth, Height = DesignHeight };
        art.Children.Add(_back);
        art.Children.Add(_front);
        art.Children.Add(_glyph);
        Child = art;
        Stretch = Stretch.Uniform;
    }

    /// <summary>Draws this folder's colours and glyph.</summary>
    public void Show(FolderDef folder)
    {
        Color top = Parse(folder.ColorA, Color.FromRgb(0x7F, 0xB2, 0xFF));
        Color bottom = Parse(folder.ColorB, Color.FromRgb(0x5B, 0x5C, 0xF2));

        // The reference lifts the front's colour towards white at the top, and uses the deep tone for the back.
        _front.Fill = Frozen(new LinearGradientBrush(Mix(top, Colors.White, 0.22), top, 90));
        _back.Fill = Frozen(new SolidColorBrush(Mix(top, bottom, 0.75)));

        _glyph.Data = folder.Glyph == "Folder" ? null : TryFindResource("Glyph" + folder.Glyph) as Geometry;
    }

    private static Color Mix(Color a, Color b, double t) => Color.FromRgb(
        (byte)Math.Round(a.R + (b.R - a.R) * t),
        (byte)Math.Round(a.G + (b.G - a.G) * t),
        (byte)Math.Round(a.B + (b.B - a.B) * t));

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
