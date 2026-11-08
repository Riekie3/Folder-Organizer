using GlassFolders.Core.Layout;

namespace GlassFolders.Core.Glass;

/// <summary>How Windows lays the wallpaper on a monitor. Values match the shell's DESKTOP_WALLPAPER_POSITION.</summary>
public enum WallpaperFit
{
    Center = 0,
    Tile = 1,
    Stretch = 2,
    Fit = 3,
    Fill = 4,
    Span = 5,
}

/// <summary>A rectangle in wallpaper image pixels. <paramref name="Partial"/> means part of the target is over the plain background.</summary>
public readonly record struct ImageRegion(double X, double Y, double Width, double Height, bool Partial = false)
{
    public ImageRegion Scale(double factor) => this with { X = X * factor, Y = Y * factor, Width = Width * factor, Height = Height * factor };
}

/// <summary>
/// Works out which part of the wallpaper image lies under a screen rectangle. Desktop folders sit directly on
/// the wallpaper, so this gives their glass and label contrast without capturing the screen.
/// </summary>
public static class WallpaperMapping
{
    /// <summary>Where the whole image is drawn, in screen pixels.</summary>
    public static (double X, double Y, double Width, double Height) Placement(
        WallpaperFit fit, int imageWidth, int imageHeight, RectInt monitor, RectInt virtualScreen)
    {
        RectInt frame = fit == WallpaperFit.Span ? virtualScreen : monitor;
        double scale = fit switch
        {
            WallpaperFit.Fill or WallpaperFit.Span => Math.Max(frame.Width / (double)imageWidth, frame.Height / (double)imageHeight),
            WallpaperFit.Fit => Math.Min(frame.Width / (double)imageWidth, frame.Height / (double)imageHeight),
            _ => 1,
        };

        if (fit == WallpaperFit.Stretch) return (frame.Left, frame.Top, frame.Width, frame.Height);
        if (fit == WallpaperFit.Tile) return (frame.Left, frame.Top, imageWidth, imageHeight);

        double w = imageWidth * scale, h = imageHeight * scale;
        return (frame.Left + (frame.Width - w) / 2, frame.Top + (frame.Height - h) / 2, w, h);
    }

    /// <summary>
    /// The image region under <paramref name="target"/>, or null when the target is entirely over the plain
    /// background colour that Center and Fit leave around the picture.
    /// </summary>
    public static ImageRegion? SourceRegion(
        WallpaperFit fit, int imageWidth, int imageHeight, RectInt monitor, RectInt virtualScreen, RectInt target)
    {
        if (imageWidth <= 0 || imageHeight <= 0 || target.Width <= 0 || target.Height <= 0) return null;

        var (px, py, pw, ph) = Placement(fit, imageWidth, imageHeight, monitor, virtualScreen);
        double sx = imageWidth / pw, sy = imageHeight / ph;
        double x = (target.Left - px) * sx, y = (target.Top - py) * sy;
        double w = target.Width * sx, h = target.Height * sy;

        if (fit == WallpaperFit.Tile)
        {
            // Start in the tile the target begins in. A target crossing a tile edge reads a little past it.
            x = ((x % imageWidth) + imageWidth) % imageWidth;
            y = ((y % imageHeight) + imageHeight) % imageHeight;
            return new ImageRegion(x, y, w, h);
        }

        // Keep only the part over the picture.
        double left = Math.Max(0, x), top = Math.Max(0, y);
        double right = Math.Min(imageWidth, x + w), bottom = Math.Min(imageHeight, y + h);
        if (right - left < 1 || bottom - top < 1) return null;
        bool partial = left > x + 0.5 || top > y + 0.5 || right < x + w - 0.5 || bottom < y + h - 0.5;
        return new ImageRegion(left, top, right - left, bottom - top, partial);
    }
}
