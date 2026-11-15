namespace GlassFolders.Core.Popup;

/// <summary>A move the user can make with the arrow or Home/End keys in the item grid.</summary>
public enum PopupMove
{
    Left,
    Right,
    Up,
    Down,
    Home,
    End,
}

/// <summary>
/// Keyboard movement inside one page of the folder popup. Kept free of WPF so the edge cases
/// (empty cells, a partly filled last row, page boundaries) can be unit tested.
/// </summary>
public static class PopupNavigation
{
    /// <summary>
    /// The item index to focus after a move. The result is always on the same page and is clamped to the
    /// items that exist there. When no move is possible the current index is returned.
    /// </summary>
    /// <param name="currentIndex">The focused item's index in the whole folder.</param>
    /// <param name="pageStart">Index of the first item on the visible page.</param>
    /// <param name="pageSize">Cells on the visible page, including empty ones.</param>
    public static int Move(int currentIndex, PopupMove move, int columns, int pageSize, int itemCount, int pageStart)
    {
        if (itemCount <= 0 || columns <= 0 || pageSize <= 0) return 0;

        pageStart = Math.Clamp(pageStart, 0, itemCount - 1);
        int pageCount = Math.Min(pageSize, itemCount - pageStart);
        int local = Math.Clamp(currentIndex - pageStart, 0, pageCount - 1);

        int next = move switch
        {
            PopupMove.Left => local % columns == 0 ? local : local - 1,
            PopupMove.Right => local % columns == columns - 1 || local == pageCount - 1 ? local : local + 1,
            PopupMove.Up => local - columns >= 0 ? local - columns : local,
            PopupMove.Down => Down(local, columns, pageSize, pageCount),
            PopupMove.Home => 0,
            PopupMove.End => pageCount - 1,
            _ => local,
        };

        return pageStart + next;
    }

    // Down only moves when there is another row on this page. On a partly filled last row it stops on
    // the last item instead of disappearing into empty cells.
    private static int Down(int local, int columns, int pageSize, int pageCount)
    {
        int candidate = local + columns;
        if (candidate >= pageSize) return local;
        return candidate < pageCount ? candidate : pageCount - 1;
    }
}
