using GlassFolders.Core.Icons;

namespace GlassFolders.Core.Tests.Icons;

public class IconAnalysisTests
{
    private const int Size = 64;

    private static byte[] Canvas(Func<int, int, (byte C, byte A)> pixel)
    {
        var px = new byte[Size * Size * 4];
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                var (c, a) = pixel(x, y);
                int i = (y * Size + x) * 4;
                px[i] = px[i + 1] = px[i + 2] = c;
                px[i + 3] = a;
            }
        return px;
    }

    private static IconAnalysis Analyze(byte[] px) => IconAnalysis.Analyze(px, Size, Size);

    // A square whose edge fades from clear to solid over the given number of pixels.
    private static byte[] Square(int margin, int fade) => Canvas((x, y) =>
    {
        int inset = Math.Min(Math.Min(x, y), Math.Min(Size - 1 - x, Size - 1 - y)) - margin;
        if (inset < 0) return (0, 0);
        byte a = inset >= fade ? (byte)255 : (byte)(255 * (inset + 1) / (fade + 1));
        return (a, a);
    });

    [Fact]
    public void CrispFullSizeArtworkIsNotLowResolution()
    {
        var result = Analyze(Square(margin: 2, fade: 1));
        Assert.False(result.IsPadded);
        Assert.False(result.IsLowResolution);
        Assert.InRange(result.EdgeSoftness, 0, 2);
    }

    [Fact]
    public void UpscaledArtworkWithSoftEdgesIsLowResolution()
    {
        var result = Analyze(Square(margin: 2, fade: 6));
        Assert.False(result.IsPadded);
        Assert.True(result.IsLowResolution);
    }

    [Fact]
    public void SmallArtInAnEmptyCanvasIsPaddedAndLowResolution()
    {
        var result = Analyze(Square(margin: 22, fade: 1));
        Assert.True(result.IsPadded);
        Assert.True(result.IsLowResolution);
    }

    [Fact]
    public void ContentIgnoresAFaintFrameAroundTheArt()
    {
        // This is what the shell returns for some small icons: art in the middle, faint square around it.
        var px = Canvas((x, y) =>
        {
            bool frame = x == 0 || y == 0 || x == Size - 1 || y == Size - 1;
            bool art = x is >= 26 and < 38 && y is >= 26 and < 38;
            return art ? ((byte)200, (byte)255) : frame ? ((byte)20, (byte)30) : ((byte)0, (byte)0);
        });

        var result = Analyze(px);
        Assert.Equal(26, result.Content.Left);
        Assert.Equal(26, result.Content.Top);
        Assert.Equal(12, result.Content.Width);
        Assert.Equal(12, result.Content.Height);
        Assert.True(result.IsPadded);
    }

    [Fact]
    public void ColourAboveAlphaMeansStraightAlpha()
    {
        Assert.True(Analyze(Canvas((_, _) => (200, 100))).IsStraightAlpha);
    }

    [Fact]
    public void ColourAtOrBelowAlphaIsTreatedAsPremultiplied()
    {
        Assert.False(Analyze(Canvas((_, _) => (100, 100))).IsStraightAlpha);
        Assert.False(Analyze(Canvas((_, _) => (0, 0))).IsStraightAlpha);
    }

    [Fact]
    public void FullyOpaqueImageIsNotPaddedOrSoft()
    {
        var result = Analyze(Canvas((x, _) => ((byte)(x * 4), (byte)255)));
        Assert.Equal(Size, result.Content.Width);
        Assert.Equal(0, result.EdgeSoftness);
        Assert.False(result.IsLowResolution);
    }

    [Fact]
    public void EmptyImageHasNoContent()
    {
        var result = Analyze(Canvas((_, _) => (0, 0)));
        Assert.Equal(default, result.Content);
        Assert.False(result.IsPadded);
    }

    [Fact]
    public void BufferTooSmallIsRejected()
    {
        Assert.Throws<ArgumentException>(() => IconAnalysis.Analyze(new byte[10], Size, Size));
    }
}
