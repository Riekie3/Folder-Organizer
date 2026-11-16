using GlassFolders.Core.Popup;

namespace GlassFolders.Core.Tests.Popup;

public class PopupDragPagingTests
{
    // A 4 x 2 grid, 400 x 200 units, with a 56-unit turning zone at each side.
    private const double W = 400, H = 200;

    private static PopupDragPaging.Turn? At(double x, double y, int page, int pages, int? dot = null) =>
        PopupDragPaging.Target(x, y, W, H, page, pages, dot);

    [Fact]
    public void LeftEdgeTurnsOnePageBack()
    {
        Assert.Equal(new PopupDragPaging.Turn(0, PopupDragPaging.LeftZone), At(10, 100, 1, 3));
    }

    [Fact]
    public void RightEdgeTurnsOnePageForward()
    {
        Assert.Equal(new PopupDragPaging.Turn(1, PopupDragPaging.RightZone), At(390, 100, 0, 2));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void LeftEdgeOnTheFirstPageHasNowhereToGo(int page)
    {
        Assert.Null(At(10, 100, page, 3));
    }

    [Fact]
    public void RightEdgeOnTheLastPageHasNowhereToGo()
    {
        Assert.Null(At(390, 100, 2, 3));
    }

    [Theory]
    [InlineData(56)]
    [InlineData(200)]
    [InlineData(343)]
    public void TheMiddleOfTheGridDoesNotTurnThePage(double x)
    {
        Assert.Null(At(x, 100, 1, 3));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(200)]
    public void EdgeZonesApplyOnlyInsideTheGridsHeight(double y)
    {
        Assert.Null(At(10, y, 1, 3));
        Assert.Null(At(390, y, 1, 3));
    }

    [Fact]
    public void APageDotTargetsItsPageDirectly()
    {
        // From page 0, dot 3 jumps straight to page 3 even though it is outside the grid.
        Assert.Equal(new PopupDragPaging.Turn(3, 3), At(200, 500, 0, 4, dot: 3));
    }

    [Fact]
    public void ADotForTheCurrentPageDoesNotTurnIt()
    {
        Assert.Null(At(200, 500, 2, 4, dot: 2));
    }

    [Fact]
    public void ADotOutsideThePageRangeIsIgnored()
    {
        Assert.Null(At(200, 500, 0, 4, dot: 4));
    }

    [Fact]
    public void ASinglePageNeverTurns()
    {
        Assert.Null(At(10, 100, 0, 1));
        Assert.Null(At(390, 100, 0, 1));
        Assert.Null(At(200, 500, 0, 1, dot: 0));
    }

    [Fact]
    public void DraggingForwardClosesTheSourcesCellAndOpensTheGap()
    {
        // Five tiles, the second one dragged to the fourth cell: tiles 3–5 move back one.
        Assert.Equal(new[] { 0, -1, 1, 2, 4 }, PopupDragPaging.ReflowSlots(5, 1, 3));
    }

    [Fact]
    public void DraggingBackwardPushesTheTilesBetweenTheGapAndTheSource()
    {
        // Five tiles, the fourth one dragged to the second cell: tiles 2–4 move on one.
        Assert.Equal(new[] { 0, 2, 3, -1, 4 }, PopupDragPaging.ReflowSlots(5, 3, 1));
    }

    [Fact]
    public void DroppingBackOnItsOwnCellMovesNothing()
    {
        Assert.Equal(new[] { 0, 1, -1, 3, 4 }, PopupDragPaging.ReflowSlots(5, 2, 2));
    }

    [Fact]
    public void AnItemFromAnotherPageOpensAGapWithoutHidingATile()
    {
        Assert.Equal(new[] { 0, 1, 3, 4, 5 }, PopupDragPaging.ReflowSlots(5, -1, 2));
    }

    [Fact]
    public void ADropPastTheLastTileLeavesThePageAlone()
    {
        Assert.Equal(new[] { 0, 1, 2 }, PopupDragPaging.ReflowSlots(3, -1, 3));
        Assert.Equal(new[] { 0, 1, 2 }, PopupDragPaging.ReflowSlots(3, -1, 99));
    }

    [Fact]
    public void AGapBeforeTheFirstTilePushesEveryTileOn()
    {
        Assert.Equal(new[] { 1, 2, 3, 4 }, PopupDragPaging.ReflowSlots(4, -1, -5));
    }

    [Fact]
    public void TheOnlyTileOnAPageIsJustHidden()
    {
        Assert.Equal(new[] { -1 }, PopupDragPaging.ReflowSlots(1, 0, 0));
    }

    [Fact]
    public void AnEmptyPageHasNothingToReflow()
    {
        Assert.Empty(PopupDragPaging.ReflowSlots(0, -1, 0));
    }
}
