using GlassFolders.Core.Glass;

namespace GlassFolders.Core.Tests.Glass;

public class FrostFilterTests
{
    private static readonly FrostSettings NoBlur = new(Downsample: 4, BlurRadius: 0, BlurPasses: 0, Saturation: 1);

    private static byte[] Solid(int w, int h, byte b, byte g, byte r, int stride = 0)
    {
        if (stride == 0) stride = w * 4;
        var px = new byte[stride * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * stride + x * 4;
                px[i] = b; px[i + 1] = g; px[i + 2] = r; px[i + 3] = 0; // alpha is undefined in a capture
            }
        return px;
    }

    private static byte Blue(FrostedImage img, int x, int y) => img.Pixels[(y * img.Width + x) * 4];

    [Fact]
    public void OutputSizeIsTheSourceDividedByTheDownsample()
    {
        var img = FrostFilter.Apply(Solid(1920, 1080, 10, 20, 30), 1920, 1080, 1920 * 4, FrostSettings.ForScale(1.25));
        Assert.Equal(240, img.Width);
        Assert.Equal(135, img.Height);
        Assert.Equal(240 * 135 * 4, img.Pixels.Length);
    }

    [Fact]
    public void ImageSmallerThanTheDownsampleStillProducesAPixel()
    {
        var img = FrostFilter.Apply(Solid(3, 2, 10, 20, 30), 3, 2, 12, new FrostSettings(8, 3, 3, 1));
        Assert.Equal(1, img.Width);
        Assert.Equal(1, img.Height);
        Assert.Equal(10, img.Pixels[0]);
    }

    [Fact]
    public void SolidColourStaysExactlyTheSameThroughBlur()
    {
        var img = FrostFilter.Apply(Solid(160, 90, 37, 121, 201), 160, 90, 640, new FrostSettings(8, 3, 3, 1));
        for (int i = 0; i < img.Pixels.Length; i += 4)
        {
            Assert.Equal(37, img.Pixels[i]);
            Assert.Equal(121, img.Pixels[i + 1]);
            Assert.Equal(201, img.Pixels[i + 2]);
            Assert.Equal(255, img.Pixels[i + 3]);
        }
    }

    [Fact]
    public void DownsampleAveragesEachBlock()
    {
        // Left half of each 4 px block is 0, right half is 200.
        var px = Solid(8, 4, 0, 0, 0);
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 8; x++)
                px[(y * 8 + x) * 4] = (byte)(x % 4 >= 2 ? 200 : 0);

        var img = FrostFilter.Apply(px, 8, 4, 32, NoBlur);
        Assert.Equal(100, Blue(img, 0, 0));
        Assert.Equal(100, Blue(img, 1, 0));
    }

    [Fact]
    public void StrideLargerThanTheRowIsRespected()
    {
        int w = 8, h = 8, stride = w * 4 + 16;
        var px = Solid(w, h, 50, 60, 70, stride);
        // Padding bytes hold junk that must not reach the output.
        for (int y = 0; y < h; y++)
            for (int i = w * 4; i < stride; i++)
                px[y * stride + i] = 255;

        var img = FrostFilter.Apply(px, w, h, stride, NoBlur);
        Assert.All(Enumerable.Range(0, img.Width * img.Height), i => Assert.Equal(50, img.Pixels[i * 4]));
    }

    [Fact]
    public void EdgesThatDoNotDivideEvenlyAreStillCounted()
    {
        // 10 px wide at a downsample of 4 gives 2 output pixels covering 5 px each.
        var px = Solid(10, 4, 0, 0, 0);
        for (int y = 0; y < 4; y++) px[(y * 10 + 9) * 4] = 250;

        var img = FrostFilter.Apply(px, 10, 4, 40, NoBlur);
        Assert.Equal(2, img.Width);
        Assert.Equal(0, Blue(img, 0, 0));
        Assert.Equal(50, Blue(img, 1, 0));
    }

    [Fact]
    public void BlurSpreadsABrightSpotAndKeepsItsTotalBrightness()
    {
        int w = 41, h = 41;
        var px = Solid(w, h, 0, 0, 0);
        px[(20 * w + 20) * 4] = 255;

        var settings = new FrostSettings(1, BlurRadius: 2, BlurPasses: 1, Saturation: 1);
        var blurred = FrostFilter.Apply(px, w, h, w * 4, settings);

        Assert.True(Blue(blurred, 20, 20) < 255);
        Assert.True(Blue(blurred, 22, 22) > 0);
        Assert.Equal(0, Blue(blurred, 26, 20));

        // 255 spread over a 5x5 box is 10.2 per pixel.
        Assert.Equal(10, Blue(blurred, 20, 20));
    }

    [Fact]
    public void BlurDoesNotDarkenOrBrightenAGradientOnAverage()
    {
        int w = 200, h = 100;
        var px = Solid(w, h, 0, 0, 0);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                px[(y * w + x) * 4] = (byte)(x * 255 / (w - 1));

        var img = FrostFilter.Apply(px, w, h, w * 4, new FrostSettings(4, 3, 3, 1));
        double mean = Enumerable.Range(0, img.Width * img.Height).Average(i => img.Pixels[i * 4]);
        Assert.InRange(mean, 126.5, 128.5);
    }

    [Fact]
    public void SaturationOfOneChangesNothing()
    {
        var img = FrostFilter.Apply(Solid(8, 8, 40, 90, 210), 8, 8, 32, NoBlur);
        Assert.Equal(40, img.Pixels[0]);
        Assert.Equal(90, img.Pixels[1]);
        Assert.Equal(210, img.Pixels[2]);
    }

    [Fact]
    public void SaturationLeavesGreysAlone()
    {
        var img = FrostFilter.Apply(Solid(8, 8, 128, 128, 128), 8, 8, 32, NoBlur with { Saturation = 1.5 });
        Assert.Equal(128, img.Pixels[0]);
        Assert.Equal(128, img.Pixels[1]);
        Assert.Equal(128, img.Pixels[2]);
    }

    [Fact]
    public void SaturationPushesColoursApartAndClamps()
    {
        var img = FrostFilter.Apply(Solid(8, 8, 40, 90, 250), 8, 8, 32, NoBlur with { Saturation = 1.5 });
        Assert.True(img.Pixels[0] < 40);
        Assert.Equal(255, img.Pixels[2]);
    }

    [Fact]
    public void LargeDownsampleMatchesAFullAverageOnceBlurred()
    {
        // Above a downsample of 6 only every second source pixel is read. Text-like detail must not show through.
        int w = 320, h = 200;
        var px = Solid(w, h, 0, 0, 0);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                px[(y * w + x) * 4] = (byte)((x * 255 / (w - 1) + (x % 7 == 0 && y % 5 == 0 ? 60 : 0)) % 256);

        var sparse = FrostFilter.Apply(px, w, h, w * 4, new FrostSettings(8, 3, 3, 1));
        var full = FrostFilter.Apply(px, w, h, w * 4, new FrostSettings(5, 3, 3, 1));

        double mean(FrostedImage img) => Enumerable.Range(0, img.Width * img.Height).Average(i => (double)img.Pixels[i * 4]);
        Assert.InRange(mean(sparse), mean(full) - 3, mean(full) + 3);
    }

    [Fact]
    public void BufferTooSmallIsRejected()
    {
        Assert.Throws<ArgumentException>(() => FrostFilter.Apply(new byte[10], 8, 8, 32, NoBlur));
    }

    [Theory]
    [InlineData(1.0, 6)]
    [InlineData(1.25, 8)]
    [InlineData(1.5, 10)]
    [InlineData(2.0, 13)]
    public void DownsampleScalesWithTheDisplay(double scale, int expected)
    {
        Assert.Equal(expected, FrostSettings.ForScale(scale).Downsample);
    }

    [Fact]
    public void BlackCaptureCountsAsBlank()
    {
        var black = FrostFilter.Apply(Solid(16, 16, 0, 0, 0), 16, 16, 64, NoBlur);
        var dark = FrostFilter.Apply(Solid(16, 16, 8, 8, 8), 16, 16, 64, NoBlur);
        Assert.True(black.IsBlank);
        Assert.False(dark.IsBlank);
    }
}
