namespace GlassFolders.Core.Models;

/// <summary>
/// The Appearance sliders turned into what the glass needs: tint strength, blur radius, edge and shadow opacity,
/// corner radius and icon size. Pure maths, so the mapping can be tested. The defaults reproduce the look
/// that was tuned against the reference image.
/// </summary>
public readonly record struct GlassAppearance(
    double TintStrength,
    int BlurRadius,
    double RimOpacity,
    double ShadowOpacity,
    double CornerRadius,
    double IconSize,
    double LowResolutionIconSize)
{
    public const int DefaultTransparency = 50;
    public const int DefaultBlur = 50;
    public const int DefaultBorder = 70;
    public const int DefaultShadow = 60;
    public const int DefaultCornerRadius = 40;

    public const int MinCornerRadius = 16;
    public const int MaxCornerRadius = 48;

    /// <summary>Settings, menus and dialogs round their corners less than the popup, in the ratio the Stage 3 look used (28 to 40).</summary>
    public const double ChromeRadiusRatio = 0.7;

    /// <summary>Shadow opacity at the default slider position.</summary>
    private const double DefaultShadowOpacity = 0.38;

    public static GlassAppearance Default { get; } = From(new AppSettings());

    public static GlassAppearance From(AppSettings settings) => new(
        TintStrength: TintStrengthFor(settings.GlassTransparency),
        BlurRadius: BlurRadiusFor(settings.GlassBlur),
        RimOpacity: Percent(settings.GlassBorder),
        ShadowOpacity: Math.Min(0.7, DefaultShadowOpacity * Clamp(settings.GlassShadow, 0, 100) / DefaultShadow),
        CornerRadius: Clamp(settings.CornerRadius, MinCornerRadius, MaxCornerRadius),
        IconSize: IconSizeFor(settings.PopupIconSize),
        LowResolutionIconSize: LowResolutionIconSizeFor(settings.PopupIconSize));

    /// <summary>Corner radius for Settings, menus and dialogs.</summary>
    public double ChromeCornerRadius => Math.Round(CornerRadius * ChromeRadiusRatio);

    /// <summary>
    /// A multiplier for the tint's opacity: 1 at the default, up to 1.8 when the glass is made nearly solid,
    /// down to 0.3 when it is made mostly clear.
    /// </summary>
    public static double TintStrengthFor(int transparency)
    {
        int t = Clamp(transparency, 0, 100);
        return t <= DefaultTransparency
            ? 1 + (DefaultTransparency - t) / 50.0 * 0.8
            : 1 - (t - DefaultTransparency) / 50.0 * 0.7;
    }

    /// <summary>Box blur radius in snapshot pixels: 1 at 0, 3 at the default, 5 at 100.</summary>
    public static int BlurRadiusFor(int blur) =>
        (int)Math.Round(1 + 4 * Clamp(blur, 0, 100) / 100.0, MidpointRounding.AwayFromZero);

    public static double IconSizeFor(PopupIconSize size) => size switch
    {
        PopupIconSize.Small => 80,
        PopupIconSize.Large => 104,
        _ => 96,
    };

    public static double LowResolutionIconSizeFor(PopupIconSize size) => size switch
    {
        PopupIconSize.Small => 50,
        PopupIconSize.Large => 64,
        _ => 60,
    };

    private static double Percent(int value) => Clamp(value, 0, 100) / 100.0;

    private static int Clamp(int value, int min, int max) => Math.Clamp(value, min, max);
}
