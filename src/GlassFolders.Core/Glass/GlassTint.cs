namespace GlassFolders.Core.Glass;

/// <summary>An sRGB colour with alpha, each channel 0–255.</summary>
public readonly record struct TintColor(byte A, byte R, byte G, byte B)
{
    public static TintColor FromOpacity(double opacity, byte r, byte g, byte b) =>
        new((byte)Math.Round(Math.Clamp(opacity, 0, 1) * 255), r, g, b);

    public double Opacity => A / 255.0;
}

/// <summary>How bright the screen behind a glass card is, as relative luminance (0 black, 1 white).</summary>
/// <param name="Mean">Average across the card.</param>
/// <param name="Bright">The 90th percentile: how bright the bright parts get, ignoring tiny highlights.</param>
/// <param name="Dark">The 10th percentile: how dark the dark parts get. Dark text on a light panel is judged against this.</param>
public readonly record struct BackdropLight(double Mean, double Bright, double Dark = 0);

/// <summary>
/// The colour layers painted over the blurred snapshot, from bottom to top: a flat scrim,
/// then a gradient from <see cref="Top"/> (top-left) to <see cref="Bottom"/> (bottom-right).
/// </summary>
public readonly record struct GlassTint(TintColor Scrim, TintColor Top, TintColor Bottom)
{
    /// <summary>
    /// Minimum contrast against white text, measured on the bright parts of the card with the tint applied.
    /// WCAG asks for 3:1 for large text; item labels also carry a drop shadow.
    /// </summary>
    public const double MinTextContrast = 2.6;

    /// <summary>Opacity of the white gloss at the very top edge of the card.</summary>
    public const double TopGlossOpacity = 0.22;

    /// <summary>How much of that gloss is left where the header text sits. Used in the contrast check.</summary>
    private const double GlossBehindHeader = 0.14;

    private const double MaxTintOpacity = 0.92;

    /// <summary>Contrast the dark ink needs on a light panel (WCAG AA for normal text).</summary>
    public const double MinInkContrast = 4.5;

    /// <summary>The dark ink used on light panels.</summary>
    public static readonly TintColor LightPanelInk = new(255, 0x14, 0x22, 0x3F);

    private const double DarkBelow = 0.35;   // mean luminance under this gets lifted toward the reference's bright blue
    private const double BrightAbove = 0.50; // mean luminance over this loses the milky top stop

    /// <summary>
    /// Chooses a tint for this backdrop. Normal wallpapers get the reference look. Dark ones get a
    /// lighter, bluer tint so the card still reads as glass. Bright ones get a dark scrim, just strong
    /// enough to keep white text readable.
    /// </summary>
    public static GlassTint ForBackdrop(BackdropLight light, double strength = 1)
    {
        double lift = Math.Clamp((DarkBelow - light.Mean) / DarkBelow, 0, 1);
        double fade = Math.Clamp((light.Mean - BrightAbove) / (1 - BrightAbove), 0, 1);

        // The strength setting scales the tint before the scrim is chosen, so contrast holds at any setting.
        // Past 1 the tint also moves toward deeper blue: a nearly solid panel in the pale reference tint couldn't carry white text.
        // If a strength still can't reach the contrast limit even with a full scrim, it is eased back until it can.
        for (double s = strength; s > 1; s -= 0.05)
        {
            var (t, bt) = Stops(lift, fade, s);
            if (TryScrim(light.Bright, t, bt, out var scrimAtStrength)) return new GlassTint(scrimAtStrength, t, bt);
        }

        var (top, bottom) = Stops(lift, fade, Math.Min(strength, 1));
        var scrim = ScrimFor(light.Bright, top, bottom);
        return new GlassTint(scrim, top, bottom);
    }

    /// <summary>
    /// The light theme's panel: milky white glass with a hint of blue, as in reference 3. A white scrim is added
    /// where the wallpaper's dark parts would otherwise make the dark ink hard to read.
    /// </summary>
    public static GlassTint ForLightPanel(BackdropLight light, double strength = 1)
    {
        var top = TintColor.FromOpacity(Math.Min(MaxTintOpacity, 0.34 * strength), 0xF6, 0xFA, 0xFF);
        var bottom = TintColor.FromOpacity(Math.Min(MaxTintOpacity, 0.30 * strength), 0xDC, 0xEA, 0xFF);

        double grey = LuminanceToSrgb(light.Dark);
        var none = TintColor.FromOpacity(0, 255, 255, 255);
        if (InkContrast(grey, none, top, bottom) >= MinInkContrast) return new GlassTint(none, top, bottom);

        double low = 0, high = 1;
        for (int i = 0; i < 12; i++)
        {
            double mid = (low + high) / 2;
            var scrim = TintColor.FromOpacity(mid, 255, 255, 255);
            if (InkContrast(grey, scrim, top, bottom) >= MinInkContrast) high = mid; else low = mid;
        }
        return new GlassTint(new TintColor((byte)Math.Min(255, Math.Ceiling(high * 255)), 255, 255, 255), top, bottom);
    }

    /// <summary>Contrast between the dark ink and this light tint over a backdrop of the given luminance.</summary>
    public double InkContrastOver(double backdropLuminance) =>
        InkContrast(LuminanceToSrgb(backdropLuminance), Scrim, Top, Bottom);

    // The tint is lightest at its top stop with the gloss on, so the darker of the two stops decides.
    private static double InkContrast(double grey, TintColor scrim, TintColor top, TintColor bottom)
    {
        double worst = double.MaxValue;
        foreach (var stop in new[] { top, bottom })
        {
            var (r, g, b) = Over(Over((grey, grey, grey), scrim), stop);
            double ink = BackdropAnalyzer.RelativeLuminance(LightPanelInk.R / 255.0, LightPanelInk.G / 255.0, LightPanelInk.B / 255.0);
            double surface = BackdropAnalyzer.RelativeLuminance(r, g, b);
            worst = Math.Min(worst, (surface + 0.05) / (ink + 0.05));
        }
        return worst;
    }

    private static (TintColor Top, TintColor Bottom) Stops(double lift, double fade, double strength)
    {
        double deep = Math.Clamp((strength - 1) / 0.8, 0, 1);
        var top = TintColor.FromOpacity(
            Math.Min(MaxTintOpacity, (0.22 + 0.30 * lift) * (1 - 0.6 * fade) * strength),
            Lerp(Lerp(0xCF, 0x72, lift), 0x5A, deep), Lerp(Lerp(0xE2, 0x9C, lift), 0x8A, deep), Lerp(Lerp(0xFF, 0xF4, lift), 0xEC, deep));
        var bottom = TintColor.FromOpacity(
            Math.Min(MaxTintOpacity, (0.28 + 0.34 * lift) * strength),
            Lerp(Lerp(0x2A, 0x5A, lift), 0x24, deep), Lerp(Lerp(0x5C, 0x8C, lift), 0x44, deep), Lerp(Lerp(0xD8, 0xF2, lift), 0xB4, deep));
        return (top, bottom);
    }

    /// <summary>The smallest scrim that gives the contrast limit, or false if even a full scrim can't.</summary>
    private static bool TryScrim(double bright, TintColor top, TintColor bottom, out TintColor scrim)
    {
        double grey = LuminanceToSrgb(bright);
        var full = TintColor.FromOpacity(1, ScrimR, ScrimG, ScrimB);
        if (Math.Min(ContrastAtTop(grey, full, top), ContrastAtBottom(grey, full, bottom)) < MinTextContrast)
        {
            scrim = default;
            return false;
        }
        scrim = ScrimFor(bright, top, bottom);
        return true;
    }

    /// <summary>Contrast between white text and this tint over a backdrop of the given luminance.</summary>
    public double TextContrastOver(double backdropLuminance)
    {
        double grey = LuminanceToSrgb(backdropLuminance);
        return Math.Min(ContrastAtTop(grey, Scrim, Top), ContrastAtBottom(grey, Scrim, Bottom));
    }

    private static TintColor ScrimFor(double bright, TintColor top, TintColor bottom)
    {
        double grey = LuminanceToSrgb(bright);
        var none = TintColor.FromOpacity(0, ScrimR, ScrimG, ScrimB);
        if (Math.Min(ContrastAtTop(grey, none, top), ContrastAtBottom(grey, none, bottom)) >= MinTextContrast)
            return none;

        // Smallest opacity that passes. Contrast only rises as the scrim gets stronger, so bisection is safe.
        double low = 0, high = 1;
        for (int i = 0; i < 12; i++)
        {
            double mid = (low + high) / 2;
            var scrim = TintColor.FromOpacity(mid, ScrimR, ScrimG, ScrimB);
            if (Math.Min(ContrastAtTop(grey, scrim, top), ContrastAtBottom(grey, scrim, bottom)) >= MinTextContrast)
                high = mid;
            else
                low = mid;
        }
        // Round up to the next alpha step: rounding down could land just under the limit.
        return new TintColor((byte)Math.Min(255, Math.Ceiling(high * 255)), ScrimR, ScrimG, ScrimB);
    }

    // A vivid mid blue, not navy: where the wallpaper is bright the glass is held back toward the reference's blue.
    private const byte ScrimR = 0x2B, ScrimG = 0x5B, ScrimB = 0xD6;

    private static double ContrastAtTop(double grey, TintColor scrim, TintColor stop)
    {
        var (r, g, b) = Over(Over((grey, grey, grey), scrim), stop);
        (r, g, b) = Over((r, g, b), TintColor.FromOpacity(GlossBehindHeader, 255, 255, 255));
        return ContrastWithWhite(r, g, b);
    }

    private static double ContrastAtBottom(double grey, TintColor scrim, TintColor stop)
    {
        var (r, g, b) = Over(Over((grey, grey, grey), scrim), stop);
        return ContrastWithWhite(r, g, b);
    }

    // WPF blends in gamma-encoded sRGB, so this does too. Channels here are 0–1.
    private static (double R, double G, double B) Over((double R, double G, double B) under, TintColor over)
    {
        double a = over.Opacity;
        return (under.R * (1 - a) + over.R / 255.0 * a,
                under.G * (1 - a) + over.G / 255.0 * a,
                under.B * (1 - a) + over.B / 255.0 * a);
    }

    private static double ContrastWithWhite(double r, double g, double b) =>
        1.05 / (BackdropAnalyzer.RelativeLuminance(r, g, b) + 0.05);

    private static double LuminanceToSrgb(double luminance)
    {
        luminance = Math.Clamp(luminance, 0, 1);
        return luminance <= 0.0031308 ? luminance * 12.92 : 1.055 * Math.Pow(luminance, 1 / 2.4) - 0.055;
    }

    private static byte Lerp(int from, int to, double t) => (byte)Math.Round(from + (to - from) * t);
}

/// <summary>Measures how bright a frosted image is, in the area a card will cover.</summary>
public static class BackdropAnalyzer
{
    private static readonly double[] Linear = BuildLinearTable();

    /// <summary>Measures a region given in image pixels. The region is clamped to the image.</summary>
    public static BackdropLight Measure(FrostedImage image, int x, int y, int width, int height)
    {
        int x0 = Math.Clamp(x, 0, image.Width - 1), y0 = Math.Clamp(y, 0, image.Height - 1);
        int x1 = Math.Clamp(x + Math.Max(1, width), x0 + 1, image.Width);
        int y1 = Math.Clamp(y + Math.Max(1, height), y0 + 1, image.Height);

        Span<int> histogram = stackalloc int[256];
        histogram.Clear();
        double total = 0;
        int count = 0;
        byte[] px = image.Pixels;
        for (int row = y0; row < y1; row++)
        {
            int i = (row * image.Width + x0) * 4;
            for (int col = x0; col < x1; col++, i += 4)
            {
                double l = 0.2126 * Linear[px[i + 2]] + 0.7152 * Linear[px[i + 1]] + 0.0722 * Linear[px[i]];
                total += l;
                histogram[(int)Math.Round(l * 255)]++;
                count++;
            }
        }

        int target = (int)Math.Ceiling(count * 0.9), seen = 0, bucket = 0;
        for (; bucket < 255; bucket++)
        {
            seen += histogram[bucket];
            if (seen >= target) break;
        }

        int darkTarget = (int)Math.Ceiling(count * 0.1), darkSeen = 0, darkBucket = 0;
        for (; darkBucket < 255; darkBucket++)
        {
            darkSeen += histogram[darkBucket];
            if (darkSeen >= darkTarget) break;
        }
        return new BackdropLight(total / count, bucket / 255.0, darkBucket / 255.0);
    }

    /// <summary>WCAG relative luminance of an sRGB colour with channels 0–1.</summary>
    public static double RelativeLuminance(double r, double g, double b) =>
        0.2126 * ToLinear(r) + 0.7152 * ToLinear(g) + 0.0722 * ToLinear(b);

    private static double ToLinear(double c) =>
        c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);

    private static double[] BuildLinearTable()
    {
        var table = new double[256];
        for (int i = 0; i < 256; i++) table[i] = ToLinear(i / 255.0);
        return table;
    }
}
