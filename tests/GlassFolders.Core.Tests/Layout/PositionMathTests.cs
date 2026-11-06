using GlassFolders.Core.Layout;

namespace GlassFolders.Core.Tests.Layout;

public sealed class PositionMathTests
{
    private readonly RectInt _primaryWork = new(0, 0, 1920, 1040);
    private readonly RectInt _leftSecondaryWork = new(-1920, 0, 1920, 1080);
    private const int TileW = 104;
    private const int TileH = 112;

    [Fact]
    public void ToRelative_and_ToAbsolute_round_trip()
    {
        int originalX = 500;
        int originalY = 400;

        var (relX, relY) = PositionMath.ToRelative(originalX, originalY, _primaryWork, TileW, TileH);
        var (px, py) = PositionMath.ToAbsolute(relX, relY, _primaryWork, TileW, TileH);

        Assert.Equal(originalX, px);
        Assert.Equal(originalY, py);
    }

    [Fact]
    public void ToRelative_clamps_out_of_bounds()
    {
        var (relX, relY) = PositionMath.ToRelative(-500, 2500, _primaryWork, TileW, TileH);
        Assert.Equal(0.0, relX);
        Assert.Equal(1.0, relY);
    }

    [Fact]
    public void ToAbsolute_handles_negative_coordinates()
    {
        // Monitor placed to the left of primary (-1920 to 0)
        double relX = 0.5;
        double relY = 0.5;

        var (px, py) = PositionMath.ToAbsolute(relX, relY, _leftSecondaryWork, TileW, TileH);

        Assert.True(px >= _leftSecondaryWork.Left && px <= _leftSecondaryWork.Right - TileW);
        Assert.True(py >= _leftSecondaryWork.Top && py <= _leftSecondaryWork.Bottom - TileH);
    }

    [Fact]
    public void SnapToGrid_aligns_to_grid_boundary()
    {
        int gridSize = 20;
        var (sx, sy) = PositionMath.SnapToGrid(123, 237, gridSize, _primaryWork);

        Assert.Equal(120, sx);
        Assert.Equal(240, sy);
    }

    [Fact]
    public void ClampToWorkArea_restricts_inside_edges()
    {
        var (cx, cy) = PositionMath.ClampToWorkArea(5000, -100, TileW, TileH, _primaryWork);

        Assert.Equal(_primaryWork.Right - TileW, cx);
        Assert.Equal(_primaryWork.Top, cy);
    }

    [Fact]
    public void FindNearestFreeCell_avoids_occupied_space()
    {
        var occupied = new List<RectInt>
        {
            new(100, 100, TileW, TileH)
        };

        // Target exact occupied cell
        var (freeX, freeY) = PositionMath.FindNearestFreeCell(100, 100, TileW, TileH, _primaryWork, occupied, step: 20);

        var candidate = new RectInt(freeX, freeY, TileW, TileH);
        Assert.False(candidate.Intersects(occupied[0]));
    }
}
