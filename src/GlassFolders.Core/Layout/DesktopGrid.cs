namespace GlassFolders.Core.Layout;

/// <summary>
/// Windows' desktop icon grid on one work area, in screen pixels. Folders use it so they line up with the
/// desktop's own icons and never land on one.
/// </summary>
public sealed record DesktopGrid(RectInt Area, int OriginX, int OriginY, int CellWidth, int CellHeight)
{
    /// <summary>Overlap, in pixels, that still counts as touching rather than covering.</summary>
    private const int Tolerance = 2;

    /// <summary>
    /// Builds the grid from the cells the desktop's icons fill. Their offset from the work area gives the grid's
    /// origin; without icons inside the area the grid starts at the area's corner.
    /// </summary>
    public static DesktopGrid Create(RectInt area, int cellWidth, int cellHeight, IEnumerable<RectInt> iconCells)
    {
        if (cellWidth <= 0 || cellHeight <= 0) throw new ArgumentOutOfRangeException(nameof(cellWidth), "Cells need a size.");

        var inside = iconCells.Where(c => area.Contains(c.Left + c.Width / 2, c.Top + c.Height / 2)).ToList();
        int phaseX = MostCommon(inside.Select(c => Mod(c.Left - area.Left, cellWidth)));
        int phaseY = MostCommon(inside.Select(c => Mod(c.Top - area.Top, cellHeight)));
        return new DesktopGrid(area, area.Left + phaseX, area.Top + phaseY, cellWidth, cellHeight);
    }

    public int Columns => Math.Max(0, (Area.Right - OriginX) / CellWidth);
    public int Rows => Math.Max(0, (Area.Bottom - OriginY) / CellHeight);

    /// <summary>A block of whole cells, with its top-left cell at the given column and row.</summary>
    public RectInt Block(int column, int row, int columns, int rows) =>
        new(OriginX + column * CellWidth, OriginY + row * CellHeight, columns * CellWidth, rows * CellHeight);

    /// <summary>How many cells across and down something of this size needs.</summary>
    public (int Columns, int Rows) SpanFor(int width, int height) =>
        (Math.Max(1, (width + CellWidth - 1 - Tolerance) / CellWidth), Math.Max(1, (height + CellHeight - 1 - Tolerance) / CellHeight));

    /// <summary>
    /// The first block of free cells, searched down each column from the left, as Windows fills the desktop.
    /// Returns null when the work area has no room.
    /// </summary>
    public RectInt? FindFreeBlock(int columns, int rows, IReadOnlyCollection<RectInt> obstacles)
    {
        for (int c = 0; c + columns <= Columns; c++)
        {
            for (int r = 0; r + rows <= Rows; r++)
            {
                var block = Block(c, r, columns, rows);
                if (!obstacles.Any(o => Covers(o, block))) return block;
            }
        }
        return null;
    }

    /// <summary>The block whose centre is closest to a point, kept inside the grid. Used to snap a dragged folder.</summary>
    public RectInt NearestBlock(int centerX, int centerY, int columns, int rows)
    {
        int maxColumn = Math.Max(0, Columns - columns);
        int maxRow = Math.Max(0, Rows - rows);
        int c = (int)Math.Round((centerX - OriginX - columns * CellWidth / 2.0) / CellWidth);
        int r = (int)Math.Round((centerY - OriginY - rows * CellHeight / 2.0) / CellHeight);
        return Block(Math.Clamp(c, 0, maxColumn), Math.Clamp(r, 0, maxRow), columns, rows);
    }

    /// <summary>
    /// The free block closest to a point, as Windows snaps a dropped icon to the nearest empty cell.
    /// Falls back to <see cref="NearestBlock"/> when every block is taken.
    /// </summary>
    public RectInt NearestFreeBlock(int centerX, int centerY, int columns, int rows, IReadOnlyCollection<RectInt> obstacles)
    {
        RectInt? best = null;
        double bestDistance = double.MaxValue;
        for (int c = 0; c + columns <= Columns; c++)
        {
            for (int r = 0; r + rows <= Rows; r++)
            {
                var block = Block(c, r, columns, rows);
                double dx = block.Left + block.Width / 2.0 - centerX, dy = block.Top + block.Height / 2.0 - centerY;
                double distance = dx * dx + dy * dy;
                if (distance >= bestDistance || obstacles.Any(o => Covers(o, block))) continue;
                best = block;
                bestDistance = distance;
            }
        }
        return best ?? NearestBlock(centerX, centerY, columns, rows);
    }

    /// <summary>True when the two overlap by more than a rounding error.</summary>
    public static bool Covers(RectInt a, RectInt b)
    {
        int w = Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left);
        int h = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top);
        return w > Tolerance && h > Tolerance;
    }

    private static int Mod(int value, int m) => ((value % m) + m) % m;

    private static int MostCommon(IEnumerable<int> values) =>
        values.GroupBy(v => v).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).Select(g => g.Key).FirstOrDefault();
}
