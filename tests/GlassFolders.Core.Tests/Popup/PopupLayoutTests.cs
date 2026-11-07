using GlassFolders.Core.Layout;
using GlassFolders.Core.Models;
using GlassFolders.Core.Popup;

namespace GlassFolders.Core.Tests.Popup;

public class PopupGridTests
{
    [Fact]
    public void DefaultIsFourByTwoAndExpandsToFourByThree()
    {
        Assert.Equal(new PopupGrid(4, 2), PopupGrid.Default);
        Assert.Equal(new PopupGrid(4, 3), PopupGrid.Default.Expanded);
        Assert.Equal(8, PopupGrid.Default.PageSize);
    }

    [Theory]
    [InlineData(0, 0, 3, 2)]
    [InlineData(99, 99, 6, 5)]
    [InlineData(5, 4, 5, 4)]
    public void SettingsOutOfRangeAreRepaired(int columns, int rows, int wantColumns, int wantRows)
    {
        var grid = PopupGrid.From(new AppSettings { PopupColumns = columns, PopupRows = rows });
        Assert.Equal(new PopupGrid(wantColumns, wantRows), grid);
    }
}

public class PopupMetricsTests
{
    [Fact]
    public void DefaultCardMatchesTheReferenceSize()
    {
        Assert.Equal(864, PopupMetrics.CardWidth(4));
        Assert.Equal(536, PopupMetrics.CardHeight(2));
    }

    [Fact]
    public void CardHeightGrowsByExactlyOneRow()
    {
        Assert.Equal(PopupMetrics.CellHeight, PopupMetrics.CardHeight(3) - PopupMetrics.CardHeight(2));
    }

    [Fact]
    public void CardThatFitsIsNotScaled()
    {
        var work = new RectInt(0, 0, 1920, 1032);
        Assert.Equal(1, PopupMetrics.FitScale(864, 536, work, 1.25));
    }

    [Fact]
    public void CardOnASmallScreenShrinksToFitInsideTheEdgeMargins()
    {
        var work = new RectInt(0, 0, 1366, 720);
        double fit = PopupMetrics.FitScale(864, 715, work, 1.0);
        Assert.True(fit < 1);
        Assert.InRange(715 * fit, 0, 720 - PopupPlacement.EdgeMargin * 2 + 0.001);
    }
}

public class PopupPagingTests
{
    [Theory]
    [InlineData(0, 8, 1)]
    [InlineData(1, 8, 1)]
    [InlineData(8, 8, 1)]
    [InlineData(9, 8, 2)]
    [InlineData(33, 8, 5)]
    public void TotalPagesCoverEveryItem(int items, int pageSize, int pages)
    {
        Assert.Equal(pages, PopupPaging.TotalPages(items, pageSize));
    }

    [Theory]
    [InlineData(-3, 20, 8, 0)]
    [InlineData(1, 20, 8, 1)]
    [InlineData(9, 20, 8, 2)]
    [InlineData(4, 0, 8, 0)]
    public void PageIsAlwaysOneThatExists(int page, int items, int pageSize, int expected)
    {
        Assert.Equal(expected, PopupPaging.ClampPage(page, items, pageSize));
    }

    [Fact]
    public void ExpandingKeepsTheFirstVisibleItemOnScreen()
    {
        // Page 2 of the 8-per-page view starts at item 16. In the 12-per-page view, that's page 1.
        Assert.Equal(1, PopupPaging.PageOfItem(2 * 8, 12));
        // Back again, item 12 is on page 1 of the smaller view.
        Assert.Equal(1, PopupPaging.PageOfItem(1 * 12, 8));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(4, 1)]
    [InlineData(5, 2)]
    [InlineData(8, 2)]
    [InlineData(40, 2)]
    public void CardDrawsOnlyTheRowsItsItemsUse(int items, int rows)
    {
        Assert.Equal(rows, PopupPaging.VisibleRows(items, PopupGrid.Default));
    }
}

public class PopupPlacementTests
{
    private static readonly RectInt Work = new(0, 0, 1920, 1032);
    private const double Scale = 1.25;
    private const int W = 1080, H = 670; // the default card at 125 %

    private static RectInt Folder(int x, int y) => new(x, y, 130, 140);

    private static void AssertInsideWork(RectInt card, RectInt work)
    {
        Assert.InRange(card.Left, work.Left, work.Right - card.Width);
        Assert.InRange(card.Top, work.Top, work.Bottom - card.Height);
    }

    [Fact]
    public void ReferenceSceneLandsWhereTheReferenceHasIt()
    {
        // The reference's Work folder, scaled from its 1672 px image to a 1920 px screen.
        double k = 1920 / 1672.0;
        var folder = new RectInt((int)(25 * k), (int)(328 * k), (int)(155 * k), (int)(124 * k));
        var card = PopupPlacement.Place(folder, Work, W, H, Scale);

        Assert.InRange(card.Left, 564 * k - 1920 * 0.04, 564 * k + 1920 * 0.04);
        Assert.InRange(card.Top, 153 * k - 1080 * 0.04, 153 * k + 1080 * 0.04);
    }

    [Fact]
    public void FolderOnTheLeftOpensToTheRightWithTheFullGap()
    {
        var folder = Folder(20, 300);
        var card = PopupPlacement.Place(folder, Work, W, H, Scale);
        Assert.Equal(folder.Right + (int)Math.Round(350 * Scale), card.Left);
    }

    [Fact]
    public void GapShrinksBeforeTheCardChangesSide()
    {
        var folder = Folder(500, 300);
        var card = PopupPlacement.Place(folder, Work, W, H, Scale);
        Assert.True(card.Left > folder.Right);
        Assert.True(card.Left - folder.Right < 350 * Scale);
        Assert.Equal(Work.Right - (int)Math.Round(PopupPlacement.EdgeMargin * Scale), card.Right);
    }

    [Fact]
    public void FolderOnTheRightOpensToTheLeft()
    {
        var folder = Folder(1780, 300);
        var card = PopupPlacement.Place(folder, Work, W, H, Scale);
        Assert.True(card.Right < folder.Left);
        Assert.Equal((int)Math.Round(350 * Scale), folder.Left - card.Right);
    }

    [Fact]
    public void CardOverlapsTheFolderOnlyWhenNeitherSideHasRoom()
    {
        var folder = Folder(895, 300);
        var card = PopupPlacement.Place(folder, Work, W, H, Scale);
        AssertInsideWork(card, Work);
        Assert.True(card.Left < folder.Right && card.Right > folder.Left);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1790, 0)]
    [InlineData(0, 892)]
    [InlineData(1790, 892)]
    public void CardStaysInTheWorkAreaFromEveryCorner(int x, int y)
    {
        AssertInsideWork(PopupPlacement.Place(Folder(x, y), Work, W, H, Scale), Work);
    }

    [Fact]
    public void WorkAreaOnASecondMonitorIsRespected()
    {
        var work = new RectInt(-1920, -200, 1920, 1040);
        var card = PopupPlacement.Place(new RectInt(-1900, 100, 130, 140), work, W, H, Scale);
        AssertInsideWork(card, work);
        Assert.True(card.Left > -1900 + 130);
    }

    [Fact]
    public void CardLargerThanTheWorkAreaStartsAtItsEdgeInsteadOfThrowing()
    {
        var work = new RectInt(0, 0, 800, 600);
        var card = PopupPlacement.Place(new RectInt(10, 10, 50, 50), work, 900, 700, 1.0);
        Assert.Equal(0, card.Left);
        Assert.Equal(0, card.Top);
    }

    [Fact]
    public void TallerCardStaysCentredOnTheSamePoint()
    {
        var folder = Folder(20, 400);
        var small = PopupPlacement.Place(folder, Work, W, H, Scale);
        var tall = PopupPlacement.Place(folder, Work, W, H + 200, Scale);
        Assert.Equal(small.Top + H / 2, tall.Top + (H + 200) / 2);
    }
}
