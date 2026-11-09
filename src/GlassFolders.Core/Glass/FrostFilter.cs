namespace GlassFolders.Core.Glass;

/// <summary>Controls how a screen capture is turned into frosted glass.</summary>
/// <param name="Downsample">Source pixels per output pixel. Larger is faster and softer.</param>
/// <param name="BlurRadius">Box blur radius, in output pixels.</param>
/// <param name="BlurPasses">Box blur passes. Three passes look close to a Gaussian.</param>
/// <param name="Saturation">1 keeps colours as they are; above 1 makes them stronger.</param>
public readonly record struct FrostSettings(int Downsample, int BlurRadius, int BlurPasses, double Saturation)
{
    /// <summary>
    /// The default look, scaled so the blur has the same on-screen size at any display scale.
    /// At 125 % this is a downsample of 8, the value tuned against the reference image.
    /// </summary>
    public static FrostSettings ForScale(double dpiScale, int blurRadius = DefaultBlurRadius) =>
        new(Math.Max(2, (int)Math.Round(6.4 * dpiScale)), BlurRadius: Math.Max(0, blurRadius), BlurPasses: 3, Saturation: 1.25);

    /// <summary>The blur radius the reference look was tuned with.</summary>
    public const int DefaultBlurRadius = 3;
}

/// <summary>A small, blurred copy of part of the screen. Pixels are BGRA, top-down, alpha 255.</summary>
public sealed class FrostedImage
{
    public FrostedImage(byte[] pixels, int width, int height)
    {
        Pixels = pixels;
        Width = width;
        Height = height;
    }

    public byte[] Pixels { get; }
    public int Width { get; }
    public int Height { get; }
    public int Stride => Width * 4;

    /// <summary>
    /// True when every pixel is black. A failed or protected capture looks like this, so callers
    /// use the solid panel instead of showing a black card.
    /// </summary>
    public bool IsBlank
    {
        get
        {
            for (int i = 0; i < Pixels.Length; i += 4)
            {
                if (Pixels[i] > 1 || Pixels[i + 1] > 1 || Pixels[i + 2] > 1) return false;
            }
            return true;
        }
    }
}

/// <summary>Pure image maths for snapshot glass: shrink, blur, saturate. Touches no Windows API.</summary>
public static class FrostFilter
{
    private const int SparseSamplingFrom = 6;

    public static FrostedImage Apply(ReadOnlySpan<byte> bgra, int width, int height, int stride, FrostSettings settings)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfLessThan(stride, width * 4);
        if (bgra.Length < stride * (height - 1) + width * 4)
            throw new ArgumentException("Pixel buffer is smaller than the stated size.", nameof(bgra));

        int factor = Math.Max(1, settings.Downsample);
        int outW = Math.Max(1, width / factor);
        int outH = Math.Max(1, height / factor);

        // Interleaved BGR while filtering; alpha is only needed in the result.
        var bgr = new byte[outW * outH * 3];
        Downsample(bgra, width, height, stride, bgr, outW, outH);

        if (settings.BlurRadius > 0 && settings.BlurPasses > 0)
        {
            var scratch = new byte[bgr.Length];
            for (int pass = 0; pass < settings.BlurPasses; pass++)
            {
                BoxBlurHorizontal(bgr, scratch, outW, outH, settings.BlurRadius);
                BoxBlurVertical(scratch, bgr, outW, outH, settings.BlurRadius);
            }
        }

        var result = new byte[outW * outH * 4];
        WriteSaturated(bgr, result, settings.Saturation);
        return new FrostedImage(result, outW, outH);
    }

    private static void Downsample(ReadOnlySpan<byte> src, int width, int height, int stride, byte[] dst, int outW, int outH)
    {
        // Above this size, every second pixel each way gives the same result once blurred, in a quarter of the reads.
        int step = width / outW >= SparseSamplingFrom ? 2 : 1;

        // Each output pixel averages its share of the source, so no edge pixels are dropped
        // when the size doesn't divide evenly and the image still lines up with the screen.
        Span<int> xEdges = outW <= 1024 ? stackalloc int[outW + 1] : new int[outW + 1];
        for (int x = 0; x <= outW; x++) xEdges[x] = (int)((long)x * width / outW);

        int d = 0;
        for (int oy = 0; oy < outH; oy++)
        {
            int y0 = (int)((long)oy * height / outH);
            int y1 = (int)((long)(oy + 1) * height / outH);
            for (int ox = 0; ox < outW; ox++)
            {
                int x0 = xEdges[ox], x1 = xEdges[ox + 1];
                int b = 0, g = 0, r = 0;
                int count = 0;
                for (int y = y0; y < y1; y += step)
                {
                    ReadOnlySpan<byte> row = src.Slice(y * stride + x0 * 4, (x1 - x0) * 4);
                    for (int i = 0; i < row.Length; i += 4 * step)
                    {
                        b += row[i];
                        g += row[i + 1];
                        r += row[i + 2];
                        count++;
                    }
                }
                int half = count / 2;
                dst[d++] = (byte)((b + half) / count);
                dst[d++] = (byte)((g + half) / count);
                dst[d++] = (byte)((r + half) / count);
            }
        }
    }

    // Running-sum box blur. Cost doesn't grow with the radius; edge pixels repeat outward.
    private static void BoxBlurHorizontal(byte[] src, byte[] dst, int w, int h, int radius)
    {
        int window = radius * 2 + 1, half = window / 2;
        for (int y = 0; y < h; y++)
        {
            int rowStart = y * w * 3;
            for (int c = 0; c < 3; c++)
            {
                int first = rowStart + c;
                int last = first + (w - 1) * 3;
                int sum = src[first] * (radius + 1);
                for (int i = 1; i <= radius; i++) sum += src[Math.Min(first + i * 3, last)];

                int o = first;
                for (int x = 0; x < w; x++, o += 3)
                {
                    dst[o] = (byte)((sum + half) / window);
                    sum += src[Math.Min(first + (x + radius + 1) * 3, last)];
                    sum -= src[Math.Max(first + (x - radius) * 3, first)];
                }
            }
        }
    }

    private static void BoxBlurVertical(byte[] src, byte[] dst, int w, int h, int radius)
    {
        int window = radius * 2 + 1, half = window / 2;
        int rowBytes = w * 3;
        for (int x = 0; x < rowBytes; x++)
        {
            int first = x;
            int last = x + (h - 1) * rowBytes;
            int sum = src[first] * (radius + 1);
            for (int i = 1; i <= radius; i++) sum += src[Math.Min(first + i * rowBytes, last)];

            int o = first;
            for (int y = 0; y < h; y++, o += rowBytes)
            {
                dst[o] = (byte)((sum + half) / window);
                sum += src[Math.Min(first + (y + radius + 1) * rowBytes, last)];
                sum -= src[Math.Max(first + (y - radius) * rowBytes, first)];
            }
        }
    }

    private static void WriteSaturated(byte[] bgr, byte[] bgra, double saturation)
    {
        int sat = (int)Math.Round(saturation * 256);
        int o = 0;
        for (int i = 0; i < bgr.Length; i += 3, o += 4)
        {
            int b = bgr[i], g = bgr[i + 1], r = bgr[i + 2];
            int luma = (114 * b + 587 * g + 299 * r + 500) / 1000;
            bgra[o] = ClampByte(luma + ((b - luma) * sat >> 8));
            bgra[o + 1] = ClampByte(luma + ((g - luma) * sat >> 8));
            bgra[o + 2] = ClampByte(luma + ((r - luma) * sat >> 8));
            bgra[o + 3] = 255;
        }
    }

    private static byte ClampByte(int value) => (byte)Math.Clamp(value, 0, 255);
}
