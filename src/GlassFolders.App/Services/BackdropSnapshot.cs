using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GlassFolders.Core.Glass;
using GlassFolders.Native;
using Microsoft.Win32;

namespace GlassFolders.App.Services;

/// <summary>
/// A frosted copy of the screen, captured once before a glass window appears. It stays in memory while that
/// window is open and is never saved. Only the shrunken, blurred version is kept.
/// </summary>
public sealed class BackdropSnapshot
{
    private readonly FrostedImage _frost;

    private BackdropSnapshot(FrostedImage frost, BitmapSource image, RECT screenArea, double elapsedMs)
    {
        _frost = frost;
        Image = image;
        ScreenArea = screenArea;
        ElapsedMilliseconds = elapsedMs;
    }

    public BitmapSource Image { get; }

    /// <summary>The part of the screen this image covers, in physical pixels.</summary>
    public RECT ScreenArea { get; }

    /// <summary>Time taken by capture and filtering.</summary>
    public double ElapsedMilliseconds { get; }

    /// <summary>
    /// False under high contrast, or when Windows' transparency effects are off.
    /// The solid panel is used in those cases, and no capture is taken.
    /// </summary>
    public static bool IsGlassAllowed() => !SystemParameters.HighContrast && IsTransparencyEnabled();

    /// <summary>Captures and frosts an area of the screen. Returns null if the capture fails or comes back black.</summary>
    public static BackdropSnapshot? Capture(RECT area, double dpiScale)
    {
        try
        {
            long start = Stopwatch.GetTimestamp();
            var settings = FrostSettings.ForScale(dpiScale);

            if (!ScreenCapture.TryRead(area, (pixels, w, h, stride) => FrostFilter.Apply(pixels, w, h, stride, settings), out FrostedImage? frost)
                || frost is null || frost.IsBlank)
            {
                DiagnosticService.Instance.LogWarn($"Backdrop capture unavailable for {area}; using solid panel");
                return null;
            }

            var image = BitmapSource.Create(frost.Width, frost.Height, 96, 96, PixelFormats.Pbgra32, null, frost.Pixels, frost.Stride);
            image.Freeze();
            return new BackdropSnapshot(frost, image, area, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
        catch (Exception ex)
        {
            DiagnosticService.Instance.LogError("Backdrop capture failed; using solid panel", ex);
            return null;
        }
    }

    /// <summary>Picks the tint for a card at this screen position.</summary>
    public GlassTint TintFor(Rect cardOnScreen)
    {
        double sx = _frost.Width / (double)ScreenArea.Width;
        double sy = _frost.Height / (double)ScreenArea.Height;
        return GlassTint.ForBackdrop(BackdropAnalyzer.Measure(
            _frost,
            (int)((cardOnScreen.X - ScreenArea.Left) * sx),
            (int)((cardOnScreen.Y - ScreenArea.Top) * sy),
            (int)Math.Ceiling(cardOnScreen.Width * sx),
            (int)Math.Ceiling(cardOnScreen.Height * sy)));
    }

    private static bool IsTransparencyEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("EnableTransparency") is not int value || value != 0;
        }
        catch (Exception ex)
        {
            DiagnosticService.Instance.LogError("Could not read the Windows transparency setting", ex);
            return true;
        }
    }
}
