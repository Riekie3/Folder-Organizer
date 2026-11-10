using GlassFolders.Core.Models;

namespace GlassFolders.Core.Layout;

/// <summary>
/// Sizes, in DIPs, of a desktop folder: the folder art, its one-line label, the box they fill on the icon grid,
/// and the frosted plate shown when it's hovered or open.
/// </summary>
/// <remarks>
/// The box is what must not cover a desktop icon. The plate may reach a little past the box, as the reference
/// shows, but is held back so it doesn't swallow the neighbouring icons on a tight grid.
/// </remarks>
public readonly record struct FolderTileMetrics(
    double IconWidth,
    double IconHeight,
    double LabelFontSize,
    double LabelLineHeight,
    double BoxWidth,
    double BoxHeight,
    double PlateWidth,
    double PlateHeight)
{
    /// <summary>Room around the plate for its glow.</summary>
    public const double GlowMargin = 20;

    /// <summary>Folder art keeps the reference's 64 × 54 shape.</summary>
    public const double ArtAspect = 54.0 / 64.0;

    public const double BoxTopPadding = 4, IconLabelGap = 8, BoxBottomPadding = 4;

    /// <summary>Reference plate around a medium folder: 142 × 115.</summary>
    public const double ReferencePlateWidth = 142, ReferencePlateHeight = 115;

    /// <summary>How far the plate may reach past the box: in total across, and in total down.</summary>
    public const double PlateReachX = 36, PlateReachY = 18;

    public double WindowWidth => PlateWidth + 2 * GlowMargin;
    public double WindowHeight => PlateHeight + 2 * GlowMargin;

    /// <summary>
    /// The size, in DIPs, of Windows' own desktop icons (48 for "Medium icons"). A medium folder is drawn this
    /// size so it matches the icons around it. The app sets it from the desktop view each time it reads the grid.
    /// </summary>
    public static double DesktopIconSize { get; set; } = 48;

    public static double IconSizeFor(FolderSize size) => size switch
    {
        FolderSize.Small => Math.Round(DesktopIconSize * 0.75),
        FolderSize.Large => Math.Round(DesktopIconSize * 1.25),
        _ => DesktopIconSize,
    };

    public static double ScaleFor(FolderSize size) => IconSizeFor(size) / 64.0;

    /// <summary>Metrics for a folder size on a desktop grid whose cells are the given size in DIPs.</summary>
    public static FolderTileMetrics For(FolderSize size, double cellWidth, double cellHeight)
    {
        double icon = IconSizeFor(size);
        double iconHeight = Math.Round(icon * ArtAspect, 1);
        double font = size switch { FolderSize.Small => 11, FolderSize.Large => 13.5, _ => 12 };
        double line = Math.Ceiling(font * 1.34);

        // Labels are as wide as a grid cell, like Windows' own, but never narrower than the art.
        double boxWidth = Math.Max(icon + 8, cellWidth - 4);
        double boxHeight = BoxTopPadding + iconHeight + IconLabelGap + line + BoxBottomPadding;

        int columns = Math.Max(1, (int)Math.Ceiling(boxWidth / cellWidth - 0.001));
        int rows = Math.Max(1, (int)Math.Ceiling(boxHeight / cellHeight - 0.001));
        double blockWidth = columns * cellWidth, blockHeight = rows * cellHeight;

        double scale = ScaleFor(size);
        double plateWidth = Math.Min(ReferencePlateWidth * scale, blockWidth + PlateReachX);
        double plateHeight = Math.Min(ReferencePlateHeight * scale, blockHeight + PlateReachY);
        plateWidth = Math.Max(plateWidth, boxWidth + 8);
        plateHeight = Math.Max(plateHeight, boxHeight + 8);

        return new FolderTileMetrics(icon, iconHeight, font, line, boxWidth, boxHeight,
            Math.Round(plateWidth), Math.Round(plateHeight));
    }
}
