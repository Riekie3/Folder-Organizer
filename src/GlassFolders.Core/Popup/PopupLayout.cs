using GlassFolders.Core.Layout;
using GlassFolders.Core.Models;

namespace GlassFolders.Core.Popup;

/// <summary>How many item cells a popup shows per page.</summary>
public readonly record struct PopupGrid(int Columns, int Rows)
{
    public const int MinColumns = 3, MaxColumns = 6;
    public const int MinRows = 2, MaxRows = 5;

    public static readonly PopupGrid Default = new(4, 2);

    public int PageSize => Columns * Rows;

    /// <summary>The larger view behind the expand button: one more row.</summary>
    public PopupGrid Expanded => this with { Rows = Rows + 1 };

    /// <summary>Reads the grid from settings, repairing values a hand-edited file could put out of range.</summary>
    public static PopupGrid From(AppSettings settings) => new(
        Math.Clamp(settings.PopupColumns, MinColumns, MaxColumns),
        Math.Clamp(settings.PopupRows, MinRows, MaxRows));
}

/// <summary>Popup sizes in DIPs, measured from the reference image. They add up to its 864 × 536 card.</summary>
public static class PopupMetrics
{
    public const double HeaderHeight = 98;
    public const double GridTopGap = 32;
    public const double FooterHeight = 54;
    public const double GridLeft = 30;
    public const double GridRight = 14;
    public const double CellWidth = 205;
    public const double CellHeight = 176;

    /// <summary>The popup is drawn this much of the reference size, so it sits more lightly on the desktop.</summary>
    public const double SizeScale = 0.9;

    public static double CardWidth(int columns) => GridLeft + columns * CellWidth + GridRight;

    public static double CardHeight(int rows) => HeaderHeight + GridTopGap + rows * CellHeight + FooterHeight;

    /// <summary>Scale that fits a card on a small screen. <see cref="SizeScale"/> when it fits; smaller when the screen is tight. It never grows past that.</summary>
    public static double FitScale(double cardWidthDips, double cardHeightDips, RectInt work, double dpiScale)
    {
        double edge = PopupPlacement.EdgeMargin * dpiScale * 2;
        return Math.Min(SizeScale, Math.Min(
            (work.Width - edge) / (cardWidthDips * dpiScale),
            (work.Height - edge) / (cardHeightDips * dpiScale)));
    }
}

/// <summary>Which items are on which page, and how many rows they need.</summary>
public static class PopupPaging
{
    public static int TotalPages(int itemCount, int pageSize) =>
        Math.Max(1, (int)Math.Ceiling(itemCount / (double)pageSize));

    public static int ClampPage(int page, int itemCount, int pageSize) =>
        Math.Clamp(page, 0, TotalPages(itemCount, pageSize) - 1);

    /// <summary>The page showing an item, used to keep the same items in view when the page size changes.</summary>
    public static int PageOfItem(int itemIndex, int pageSize) => Math.Max(0, itemIndex) / pageSize;

    /// <summary>Rows the card draws: only as many as the items use, so there is never an empty row.</summary>
    public static int VisibleRows(int itemCount, PopupGrid grid) =>
        Math.Clamp((int)Math.Ceiling(itemCount / (double)grid.Columns), 1, grid.Rows);
}

/// <summary>Works out where the card goes, in physical screen pixels.</summary>
public static class PopupPlacement
{
    public const double PreferredGap = 350;
    public const double MinGap = 12;
    public const double EdgeMargin = 12;

    // The reference's card sits a little below the folder's centre.
    public const double VerticalBias = 50;

    /// <summary>
    /// Beside the folder, on the right when it fits. The gap shrinks before the card changes side, and
    /// the card overlaps the folder only when neither side has room. The card always stays in the work area.
    /// </summary>
    public static RectInt Place(RectInt anchor, RectInt work, int cardWidth, int cardHeight, double dpiScale)
    {
        int edge = Px(EdgeMargin, dpiScale), minGap = Px(MinGap, dpiScale), wantGap = Px(PreferredGap, dpiScale);

        int spareRight = work.Right - edge - anchor.Right - cardWidth;
        int spareLeft = anchor.Left - work.Left - edge - cardWidth;

        int x;
        if (spareRight >= minGap) x = anchor.Right + Math.Min(wantGap, spareRight);
        else if (spareLeft >= minGap) x = anchor.Left - Math.Min(wantGap, spareLeft) - cardWidth;
        else x = spareRight >= spareLeft ? work.Right - edge - cardWidth : work.Left + edge;

        int y = anchor.Top + anchor.Height / 2 + Px(VerticalBias, dpiScale) - cardHeight / 2;

        return new RectInt(
            ClampToSpan(x, work.Left, work.Right, cardWidth, edge),
            ClampToSpan(y, work.Top, work.Bottom, cardHeight, edge),
            cardWidth,
            cardHeight);
    }

    private static int ClampToSpan(int value, int start, int end, int size, int edge)
    {
        int max = end - edge - size;
        int min = start + edge;
        return max < min ? start + Math.Max(0, (end - start - size) / 2) : Math.Clamp(value, min, max);
    }

    private static int Px(double dips, double dpiScale) => (int)Math.Round(dips * dpiScale);
}
