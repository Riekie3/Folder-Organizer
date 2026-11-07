using GlassFolders.Core.Glass;

namespace GlassFolders.Core.Tests.Glass;

public class GlassTintTests
{
    private static GlassTint For(double luminance) => GlassTint.ForBackdrop(new BackdropLight(luminance, luminance));

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.02)]
    [InlineData(0.1)]
    [InlineData(0.2)]
    [InlineData(0.35)]
    [InlineData(0.5)]
    [InlineData(0.75)]
    [InlineData(0.9)]
    [InlineData(1.0)]
    public void WhiteTextStaysReadableOnAnyBackdrop(double luminance)
    {
        Assert.True(For(luminance).TextContrastOver(luminance) >= GlassTint.MinTextContrast,
            $"contrast {For(luminance).TextContrastOver(luminance):F2} at luminance {luminance}");
    }

    [Fact]
    public void BrightSpotsDecideTheScrimEvenWhenTheAverageIsDark()
    {
        var tint = GlassTint.ForBackdrop(new BackdropLight(Mean: 0.05, Bright: 0.9));
        Assert.True(tint.Scrim.A > 0);
        Assert.True(tint.TextContrastOver(0.9) >= GlassTint.MinTextContrast);
    }

    [Fact]
    public void MidToneWallpaperGetsTheReferenceTintWithoutAScrim()
    {
        var tint = For(0.12);
        Assert.Equal(0, tint.Scrim.A);
        Assert.InRange(tint.Top.Opacity, 0.20, 0.30);
        Assert.InRange(tint.Bottom.Opacity, 0.20, 0.30);
    }

    [Fact]
    public void DarkWallpaperGetsAStrongerLighterTint()
    {
        var dark = For(0.0);
        var normal = For(0.12);
        Assert.True(dark.Top.A > normal.Top.A);
        Assert.True(dark.Bottom.A > normal.Bottom.A);
        Assert.True(dark.Bottom.R > normal.Bottom.R);
        Assert.Equal(0, dark.Scrim.A);
    }

    [Fact]
    public void ScrimNeverGetsWeakerAsTheBackdropGetsBrighter()
    {
        int previous = 0;
        for (double l = 0; l <= 1.0; l += 0.02)
        {
            int alpha = GlassTint.ForBackdrop(new BackdropLight(0.2, l)).Scrim.A;
            Assert.True(alpha >= previous, $"scrim dropped at luminance {l}");
            previous = alpha;
        }
        Assert.True(previous > 0);
    }

    [Fact]
    public void WhiteBackdropStaysGlassRatherThanOpaque()
    {
        Assert.InRange(For(1.0).Scrim.Opacity, 0.2, 0.8);
    }
}

public class BackdropAnalyzerTests
{
    private static FrostedImage Filled(int w, int h, Func<int, int, byte> grey)
    {
        var px = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 4;
                px[i] = px[i + 1] = px[i + 2] = grey(x, y);
                px[i + 3] = 255;
            }
        return new FrostedImage(px, w, h);
    }

    [Fact]
    public void BlackAndWhiteMeasureAsZeroAndOne()
    {
        var black = BackdropAnalyzer.Measure(Filled(10, 10, (_, _) => 0), 0, 0, 10, 10);
        var white = BackdropAnalyzer.Measure(Filled(10, 10, (_, _) => 255), 0, 0, 10, 10);
        Assert.Equal(0, black.Mean, 3);
        Assert.Equal(0, black.Bright, 3);
        Assert.Equal(1, white.Mean, 3);
        Assert.Equal(1, white.Bright, 3);
    }

    [Fact]
    public void MidGreyIsMeasuredInLinearLight()
    {
        // sRGB 128 is about 21.6 % of white's light, not 50 %.
        var light = BackdropAnalyzer.Measure(Filled(4, 4, (_, _) => 128), 0, 0, 4, 4);
        Assert.Equal(0.216, light.Mean, 2);
    }

    [Fact]
    public void OnlyTheRequestedRegionCounts()
    {
        var image = Filled(20, 10, (x, _) => (byte)(x < 10 ? 0 : 255));
        Assert.Equal(0, BackdropAnalyzer.Measure(image, 0, 0, 10, 10).Mean, 3);
        Assert.Equal(1, BackdropAnalyzer.Measure(image, 10, 0, 10, 10).Mean, 3);
    }

    [Fact]
    public void BrightValueIgnoresTinyHighlightsButNotLargeOnes()
    {
        var tiny = Filled(100, 1, (x, _) => (byte)(x < 5 ? 255 : 0));
        var large = Filled(100, 1, (x, _) => (byte)(x < 30 ? 255 : 0));
        Assert.Equal(0, BackdropAnalyzer.Measure(tiny, 0, 0, 100, 1).Bright, 3);
        Assert.Equal(1, BackdropAnalyzer.Measure(large, 0, 0, 100, 1).Bright, 3);
    }

    [Theory]
    [InlineData(-50, -50, 500, 500)]
    [InlineData(5, 5, 0, 0)]
    [InlineData(1000, 1000, 10, 10)]
    public void RegionsOutsideTheImageAreClampedInsteadOfThrowing(int x, int y, int w, int h)
    {
        var light = BackdropAnalyzer.Measure(Filled(10, 10, (_, _) => 255), x, y, w, h);
        Assert.Equal(1, light.Mean, 3);
    }
}
