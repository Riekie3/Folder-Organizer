namespace GlassFolders.Core.Layout;

/// <summary>
/// Mathematical utilities for multi-monitor positioning, work-area relative projections,
/// coordinate clamping, grid snapping, and collision avoidance.
/// </summary>
public static class PositionMath
{
    /// <summary>
    /// Converts absolute screen coordinates to relative fractions (0.0 .. 1.0) within the given work area.
    /// </summary>
    public static (double RelX, double RelY) ToRelative(int pixelX, int pixelY, RectInt workArea, int tileWidth, int tileHeight)
    {
        int availableW = Math.Max(1, workArea.Width - tileWidth);
        int availableH = Math.Max(1, workArea.Height - tileHeight);

        double relX = (pixelX - workArea.Left) / (double)availableW;
        double relY = (pixelY - workArea.Top) / (double)availableH;

        return (Math.Clamp(relX, 0.0, 1.0), Math.Clamp(relY, 0.0, 1.0));
    }

    /// <summary>
    /// Converts relative fractions (0.0 .. 1.0) to absolute screen coordinates within the given work area.
    /// </summary>
    public static (int PixelX, int PixelY) ToAbsolute(double relX, double relY, RectInt workArea, int tileWidth, int tileHeight)
    {
        int availableW = Math.Max(0, workArea.Width - tileWidth);
        int availableH = Math.Max(0, workArea.Height - tileHeight);

        int px = workArea.Left + (int)Math.Round(Math.Clamp(relX, 0.0, 1.0) * availableW);
        int py = workArea.Top + (int)Math.Round(Math.Clamp(relY, 0.0, 1.0) * availableH);

        return ClampToWorkArea(px, py, tileWidth, tileHeight, workArea);
    }

    /// <summary>
    /// Snaps pixel coordinates to the nearest grid cell aligned to the work area.
    /// </summary>
    public static (int SnappedX, int SnappedY) SnapToGrid(int pixelX, int pixelY, int gridSize, RectInt workArea)
    {
        if (gridSize <= 1) return (pixelX, pixelY);

        int offsetX = pixelX - workArea.Left;
        int offsetY = pixelY - workArea.Top;

        int snappedOffsetX = (int)Math.Round((double)offsetX / gridSize) * gridSize;
        int snappedOffsetY = (int)Math.Round((double)offsetY / gridSize) * gridSize;

        return (workArea.Left + snappedOffsetX, workArea.Top + snappedOffsetY);
    }

    /// <summary>
    /// Clamps tile coordinates strictly inside the monitor's work area boundaries.
    /// Handles multi-monitor negative coordinates (e.g. monitors placed to the left or above primary).
    /// </summary>
    public static (int ClampedX, int ClampedY) ClampToWorkArea(int pixelX, int pixelY, int tileWidth, int tileHeight, RectInt workArea)
    {
        int maxX = Math.Max(workArea.Left, workArea.Right - tileWidth);
        int maxY = Math.Max(workArea.Top, workArea.Bottom - tileHeight);

        int cx = Math.Clamp(pixelX, workArea.Left, maxX);
        int cy = Math.Clamp(pixelY, workArea.Top, maxY);

        return (cx, cy);
    }

    /// <summary>
    /// Finds the nearest non-overlapping position within the work area if the target position collides with occupied tiles.
    /// </summary>
    public static (int FreeX, int FreeY) FindNearestFreeCell(int startX, int startY, int tileWidth, int tileHeight, RectInt workArea, IEnumerable<RectInt> occupied, int step = 20)
    {
        var targetRect = new RectInt(startX, startY, tileWidth, tileHeight);
        var occupiedList = occupied.ToList();

        if (!occupiedList.Any(o => o.Intersects(targetRect)))
        {
            return ClampToWorkArea(startX, startY, tileWidth, tileHeight, workArea);
        }

        // Spiral search in grid steps
        int maxRadius = Math.Max(workArea.Width, workArea.Height);
        for (int r = step; r < maxRadius; r += step)
        {
            // Check 8 directions around concentric square
            for (int dx = -r; dx <= r; dx += step)
            {
                foreach (int dy in new[] { -r, r })
                {
                    int candidateX = startX + dx;
                    int candidateY = startY + dy;
                    var (clampedX, clampedY) = ClampToWorkArea(candidateX, candidateY, tileWidth, tileHeight, workArea);
                    var candidateRect = new RectInt(clampedX, clampedY, tileWidth, tileHeight);

                    if (!occupiedList.Any(o => o.Intersects(candidateRect)))
                        return (clampedX, clampedY);
                }
            }
            for (int dy = -r + step; dy <= r - step; dy += step)
            {
                foreach (int dx in new[] { -r, r })
                {
                    int candidateX = startX + dx;
                    int candidateY = startY + dy;
                    var (clampedX, clampedY) = ClampToWorkArea(candidateX, candidateY, tileWidth, tileHeight, workArea);
                    var candidateRect = new RectInt(clampedX, clampedY, tileWidth, tileHeight);

                    if (!occupiedList.Any(o => o.Intersects(candidateRect)))
                        return (clampedX, clampedY);
                }
            }
        }

        return (startX, startY);
    }
}
