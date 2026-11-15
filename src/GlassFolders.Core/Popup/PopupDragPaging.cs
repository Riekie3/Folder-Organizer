namespace GlassFolders.Core.Popup;

/// <summary>
/// Which page a drag should turn to while the pointer rests near an edge of the item grid or over a
/// page dot. Kept free of WPF so the zone rules can be unit tested.
/// </summary>
public static class PopupDragPaging
{
    /// <summary>How wide the left and right edge zones are, in the grid's units.</summary>
    public const double EdgeZone = 56;

    /// <summary>Identifies the left edge zone, so a flip does not repeat until the pointer leaves it.</summary>
    public const int LeftZone = -1;

    /// <summary>Identifies the right edge zone, so a flip does not repeat until the pointer leaves it.</summary>
    public const int RightZone = -2;

    /// <summary>A page the pointer asks for, and the zone it is asking from.</summary>
    public readonly record struct Turn(int Page, int Zone);

    /// <summary>
    /// The page a drag should turn to. A page dot wins and targets its page directly; otherwise the
    /// pointer must be inside the grid's height and near its left or right edge. The page is one step
    /// away, and null means there is no page that way or the pointer is outside every zone.
    /// </summary>
    public static Turn? Target(double x, double y, double gridWidth, double gridHeight,
        int currentPage, int totalPages, int? dotPage, double edgeZone = EdgeZone)
    {
        if (totalPages <= 1) return null;

        if (dotPage is int dot && dot >= 0 && dot < totalPages)
            return dot == currentPage ? null : new Turn(dot, dot);

        if (gridWidth <= 0 || gridHeight <= 0 || edgeZone <= 0 || y < 0 || y >= gridHeight) return null;

        if (x >= 0 && x < edgeZone)
            return currentPage > 0 ? new Turn(currentPage - 1, LeftZone) : null;

        if (x <= gridWidth && x >= gridWidth - edgeZone)
            return currentPage < totalPages - 1 ? new Turn(currentPage + 1, RightZone) : null;

        return null;
    }
}
