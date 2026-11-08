namespace GlassFolders.Core.Glass;

public enum LabelInk
{
    /// <summary>White text with a dark shadow, as Windows draws desktop labels.</summary>
    Light,

    /// <summary>Near-black text with a soft white glow, for light glass.</summary>
    Dark,
}

/// <summary>How a folder label is drawn: its ink and how strong its shadow or glow is.</summary>
public readonly record struct LabelStyle(LabelInk Ink, double ShadowOpacity, double ShadowBlur);

/// <summary>
/// Chooses a folder label's look from the brightness behind it (relative luminance, 0 to 1).
/// On the bare wallpaper labels stay white, as Windows' own do, with a firmer shadow on bright wallpapers.
/// On the frosted plate the ink flips to dark once the plate is light enough, as in the reference.
/// </summary>
public static class LabelContrast
{
    /// <summary>Opacity of the plate's light cyan veil where the label sits.</summary>
    public const double PlateVeil = 0.3;

    /// <summary>Relative luminance of the veil's colour, #BFE9FF.</summary>
    public const double VeilLuminance = 0.76;

    /// <summary>Wallpaper brightness above which white labels get the firmer shadow.</summary>
    public const double BrightWallpaper = 0.4;

    /// <summary>Plate brightness above which dark ink reads better than white.</summary>
    public const double DarkInkFrom = 0.32;

    public static LabelStyle OnWallpaper(double luminance) =>
        luminance >= BrightWallpaper
            ? new LabelStyle(LabelInk.Light, 0.95, 2.5)
            : new LabelStyle(LabelInk.Light, 0.65, 4);

    public static LabelStyle OnPlate(double wallpaperLuminance)
    {
        double behind = PlateLuminance(wallpaperLuminance);
        return behind >= DarkInkFrom
            ? new LabelStyle(LabelInk.Dark, 0.55, 3)
            : new LabelStyle(LabelInk.Light, 0.65, 4);
    }

    /// <summary>Brightness under the label once the plate's veil is laid over the wallpaper.</summary>
    public static double PlateLuminance(double wallpaperLuminance) =>
        Math.Clamp(wallpaperLuminance, 0, 1) * (1 - PlateVeil) + PlateVeil * VeilLuminance;
}
