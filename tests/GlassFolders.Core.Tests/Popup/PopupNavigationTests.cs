using GlassFolders.Core.Popup;

namespace GlassFolders.Core.Tests.Popup;

public class PopupNavigationTests
{
    // A 4 x 2 page: indexes 0-3 on the top row, 4-7 on the bottom row.
    private static int Move(int index, PopupMove move, int items = 20, int pageStart = 0) =>
        PopupNavigation.Move(index, move, columns: 4, pageSize: 8, itemCount: items, pageStart: pageStart);

    [Theory]
    [InlineData(0, PopupMove.Left, 0)]
    [InlineData(1, PopupMove.Left, 0)]
    [InlineData(3, PopupMove.Right, 3)]
    [InlineData(7, PopupMove.Right, 7)]
    [InlineData(1, PopupMove.Right, 2)]
    [InlineData(1, PopupMove.Up, 1)]
    [InlineData(5, PopupMove.Up, 1)]
    [InlineData(1, PopupMove.Down, 5)]
    [InlineData(5, PopupMove.Down, 5)]
    [InlineData(5, PopupMove.Home, 0)]
    [InlineData(5, PopupMove.End, 7)]
    public void ArrowKeysMoveWithinTheVisiblePage(int index, PopupMove move, int expected)
    {
        Assert.Equal(expected, Move(index, move));
    }

    [Theory]
    [InlineData(0, 4, 5)]   // the item below exists
    [InlineData(1, 4, 5)]   // no item below the second column: stops on the last item
    [InlineData(3, 4, 5)]
    [InlineData(4, 4, 5)]   // already on the last row
    public void DownOnAPartlyFilledLastRowStopsOnTheLastItem(int index, int expected, int items)
    {
        // 5 items on a 4 x 2 page: cell 4 is the only one on the second row.
        Assert.Equal(expected, Move(index, PopupMove.Down, items: items));
    }

    [Fact]
    public void NavigationStaysOnTheSecondPage()
    {
        Assert.Equal(8, Move(8, PopupMove.Left, pageStart: 8));
        Assert.Equal(15, Move(15, PopupMove.Right, pageStart: 8));
        Assert.Equal(8, Move(12, PopupMove.Home, pageStart: 8));
        Assert.Equal(15, Move(12, PopupMove.End, pageStart: 8));
    }

    [Fact]
    public void LastPageClampsToTheItemsItHas()
    {
        // Items 16-19 are on the last page (page start 16), and only the first row is used.
        Assert.Equal(19, Move(18, PopupMove.End, items: 20, pageStart: 16));
        Assert.Equal(19, Move(19, PopupMove.Down, items: 20, pageStart: 16));
        Assert.Equal(18, Move(17, PopupMove.Right, items: 20, pageStart: 16));
        Assert.Equal(19, Move(18, PopupMove.Right, items: 20, pageStart: 16));
    }

    [Fact]
    public void EmptyFolderAndInvalidGridDoNothing()
    {
        Assert.Equal(0, PopupNavigation.Move(3, PopupMove.Right, columns: 4, pageSize: 8, itemCount: 0, pageStart: 0));
        Assert.Equal(0, PopupNavigation.Move(3, PopupMove.Right, columns: 0, pageSize: 8, itemCount: 20, pageStart: 0));
        Assert.Equal(0, PopupNavigation.Move(3, PopupMove.Right, columns: 4, pageSize: 0, itemCount: 20, pageStart: 0));
    }
}
