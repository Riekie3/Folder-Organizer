using GlassFolders.Core.Layout;

namespace GlassFolders.Core.Icons;

/// <summary>What a 256 px icon from the shell really contains. Pure pixel maths, so it can be unit tested.</summary>
/// <param name="IsStraightAlpha">True when colour isn't premultiplied. The shell returns both kinds.</param>
/// <param name="Content">The solid part of the icon, ignoring faint frames and shadows. Empty if nothing is solid.</param>
/// <param name="EdgeSoftness">Typical width in pixels of the icon's soft edge. Crisp artwork is 1 or 2.</param>
/// <param name="IsPadded">True when the art is small and sits in the middle of an empty canvas.</param>
/// <param name="IsLowResolution">True when the app ships no large artwork, so the shell had to pad or upscale it.</param>
public readonly record struct IconAnalysis(
    bool IsStraightAlpha,
    RectInt Content,
    double EdgeSoftness,
    bool IsPadded,
    bool IsLowResolution)
{
    private const byte SolidAlpha = 128;
    private const byte EdgeAlphaLow = 24, EdgeAlphaHigh = 232;
    private const int EdgeRowStep = 4;

    // Measured on 76 real icons: every 256 px original scored under 4, every upscaled one 5 or more.
    private const double SoftEdgeThreshold = 4;
    private const double PaddedFraction = 0.45;

    /// <summary>Analyses top-down BGRA pixels with no padding between rows.</summary>
    public static IconAnalysis Analyze(ReadOnlySpan<byte> bgra, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (bgra.Length < width * height * 4)
            throw new ArgumentException("Pixel buffer is smaller than the stated size.", nameof(bgra));

        bool straight = false;
        int minX = width, minY = height, maxX = -1, maxY = -1;

        for (int y = 0, i = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++, i += 4)
            {
                byte a = bgra[i + 3];
                if (bgra[i] > a || bgra[i + 1] > a || bgra[i + 2] > a) straight = true;
                if (a < SolidAlpha) continue;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
        }

        var content = maxX < 0 ? default : new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
        double softness = MedianEdgeWidth(bgra, width, height);
        bool padded = maxX >= 0 && Math.Max(content.Width, content.Height) <= Math.Max(width, height) * PaddedFraction;

        return new IconAnalysis(straight, content, softness, padded, padded || softness >= SoftEdgeThreshold);
    }

    private static double MedianEdgeWidth(ReadOnlySpan<byte> bgra, int width, int height)
    {
        var widths = new List<int>();
        for (int y = 0; y < height; y += EdgeRowStep)
        {
            int start = -1;
            for (int x = 0; x < width; x++)
            {
                byte a = bgra[(y * width + x) * 4 + 3];
                bool onEdge = a is >= EdgeAlphaLow and <= EdgeAlphaHigh;
                if (onEdge && start < 0)
                {
                    start = x;
                }
                else if (!onEdge && start >= 0)
                {
                    widths.Add(x - start);
                    start = -1;
                }
            }
        }

        if (widths.Count == 0) return 0;
        widths.Sort();
        return widths[widths.Count / 2];
    }
}
