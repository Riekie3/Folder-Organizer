using GlassFolders.Core.Layout;

namespace GlassFolders.Core.Tests.Layout;

public class DesktopGridTests
{
    // The dev PC: 1920 x 1080 at 125 %, work area above a 48 px taskbar, 95 x 122 px cells starting at (0, 2).
    private static readonly RectInt Work = new(0, 0, 1920, 1032);

    private static RectInt Cell(int column, int row) => new(column * 95, 2 + row * 122, 95, 122);

    [Fact]
    public void OriginComesFromTheIconsOffset()
    {
        var grid = DesktopGrid.Create(Work, 95, 122, [Cell(0, 0), Cell(0, 1), Cell(3, 4)]);

        Assert.Equal((0, 2), (grid.OriginX, grid.OriginY));
        Assert.Equal(20, grid.Columns);
        Assert.Equal(8, grid.Rows);
        Assert.Equal(Cell(2, 3), grid.Block(2, 3, 1, 1));
    }

    [Fact]
    public void WithoutIconsTheGridStartsAtTheWorkAreaCorner()
    {
        var area = new RectInt(-1920, 40, 1920, 1000);
        var grid = DesktopGrid.Create(area, 95, 122, [new RectInt(5, 5, 95, 122)]);

        Assert.Equal((-1920, 40), (grid.OriginX, grid.OriginY));
    }

    [Fact]
    public void FirstFreeCellIsUnderTheIconsInTheLeftColumn()
    {
        // This PC and Recycle Bin, as in the reference: the first folder goes right under them.
        var icons = new[] { Cell(0, 0), Cell(0, 1) };
        var grid = DesktopGrid.Create(Work, 95, 122, icons);

        Assert.Equal(Cell(0, 2), grid.FindFreeBlock(1, 1, icons));
    }

    [Fact]
    public void AFullColumnMovesTheSearchToTheNextColumn()
    {
        var icons = Enumerable.Range(0, 8).Select(r => Cell(0, r)).Append(Cell(1, 0)).ToList();
        var grid = DesktopGrid.Create(Work, 95, 122, icons);

        Assert.Equal(Cell(1, 1), grid.FindFreeBlock(1, 1, icons));
    }

    [Fact]
    public void FoldersAlreadyPlacedAreObstaclesToo()
    {
        var icons = new[] { Cell(0, 0) };
        var grid = DesktopGrid.Create(Work, 95, 122, icons);
        // An existing folder's box, a little narrower than its cell and drawn 3 px lower.
        var folder = new RectInt(5, 2 + 122 + 3, 85, 110);

        Assert.Equal(Cell(0, 2), grid.FindFreeBlock(1, 1, [.. icons, folder]));
    }

    [Fact]
    public void TallFoldersNeedTwoFreeCellsInAColumn()
    {
        var icons = new[] { Cell(0, 0), Cell(0, 2) };
        var grid = DesktopGrid.Create(Work, 95, 122, icons);

        Assert.Equal(new RectInt(0, 2 + 3 * 122, 95, 244), grid.FindFreeBlock(1, 2, icons));
    }

    [Fact]
    public void TouchingEdgesDoNotCountAsOverlap()
    {
        Assert.False(DesktopGrid.Covers(new RectInt(0, 0, 95, 122), new RectInt(95, 0, 95, 122)));
        Assert.False(DesktopGrid.Covers(new RectInt(0, 0, 96, 122), new RectInt(95, 0, 95, 122)));
        Assert.True(DesktopGrid.Covers(new RectInt(0, 0, 100, 122), new RectInt(95, 0, 95, 122)));
    }

    [Fact]
    public void NoRoomReturnsNull()
    {
        var grid = DesktopGrid.Create(new RectInt(0, 0, 190, 244), 95, 122, []);
        var all = new[] { new RectInt(0, 0, 190, 244) };

        Assert.Null(grid.FindFreeBlock(1, 1, all));
        Assert.Null(grid.FindFreeBlock(3, 1, []));
    }

    [Theory]
    [InlineData(80, 100, 1, 1)]
    [InlineData(95, 122, 1, 1)]
    [InlineData(97, 122, 1, 1)]
    [InlineData(110, 131, 2, 2)]
    public void SpanRoundsUpPastAFewPixels(int width, int height, int columns, int rows)
    {
        var grid = DesktopGrid.Create(Work, 95, 122, []);
        Assert.Equal((columns, rows), grid.SpanFor(width, height));
    }

    [Fact]
    public void SnapPicksTheNearestCellAndStaysOnScreen()
    {
        var grid = DesktopGrid.Create(Work, 95, 122, [Cell(0, 0)]);

        Assert.Equal(Cell(2, 1), grid.NearestBlock(2 * 95 + 60, 2 + 122 + 50, 1, 1));
        Assert.Equal(Cell(0, 0), grid.NearestBlock(-500, -500, 1, 1));
        Assert.Equal(Cell(19, 7), grid.NearestBlock(5000, 5000, 1, 1));
    }

    [Fact]
    public void SnapSkipsCellsThatAreTaken()
    {
        var icons = new[] { Cell(2, 1), Cell(3, 1) };
        var grid = DesktopGrid.Create(Work, 95, 122, icons);

        // Dropped on the upper half of the icon at (2, 1): the free cell above is nearest.
        Assert.Equal(Cell(2, 0), grid.NearestFreeBlock(2 * 95 + 50, 2 + 122 + 20, 1, 1, icons));
        // Nothing free at all: snap to the nearest cell anyway rather than lose the drop.
        Assert.Equal(Cell(2, 1), grid.NearestFreeBlock(2 * 95 + 50, 2 + 122 + 61, 1, 1, [Work]));
    }
}

public class FolderTileMetricsTests
{
    // The medium icon grid at 125 %: 95 x 122 px is 76 x 97.6 DIPs.
    private const double CellW = 76, CellH = 97.6;

    [Fact]
    public void MediumFolderFitsOneDesktopCell()
    {
        var m = FolderTileMetrics.For(Models.FolderSize.Medium, CellW, CellH);

        Assert.Equal(52, m.IconWidth);
        Assert.Equal(43.9, m.IconHeight);
        Assert.True(m.BoxWidth <= CellW, $"box {m.BoxWidth} wide");
        Assert.True(m.BoxHeight <= CellH, $"box {m.BoxHeight} tall");
    }

    [Fact]
    public void PlateIsTheReferenceSizeWhenThereIsRoom()
    {
        var m = FolderTileMetrics.For(Models.FolderSize.Medium, 120, 110);
        double scale = 52 / 64.0;
        Assert.Equal((Math.Max(Math.Round(142 * scale), m.BoxWidth + 8), Math.Round(115 * scale)), (m.PlateWidth, m.PlateHeight));
        Assert.Equal((m.PlateWidth + 40, m.PlateHeight + 40), (m.WindowWidth, m.WindowHeight));
    }

    [Fact]
    public void PlateIsHeldBackOnATightGrid()
    {
        var m = FolderTileMetrics.For(Models.FolderSize.Medium, CellW, CellH);
        Assert.Equal(CellW + FolderTileMetrics.PlateReachX, m.PlateWidth);
        Assert.Equal(Math.Round(115 * 52 / 64.0), m.PlateHeight);
    }

    [Fact]
    public void SizesGrowInOrder()
    {
        var s = FolderTileMetrics.For(Models.FolderSize.Small, CellW, CellH);
        var m = FolderTileMetrics.For(Models.FolderSize.Medium, CellW, CellH);
        var l = FolderTileMetrics.For(Models.FolderSize.Large, CellW, CellH);

        Assert.True(s.IconWidth < m.IconWidth && m.IconWidth < l.IconWidth);
        Assert.True(s.LabelFontSize < m.LabelFontSize && m.LabelFontSize < l.LabelFontSize);
        Assert.True(s.BoxHeight < m.BoxHeight && m.BoxHeight < l.BoxHeight);
        // The plate is never smaller than the box it holds.
        Assert.True(l.PlateWidth >= l.BoxWidth && l.PlateHeight >= l.BoxHeight);
    }
}
