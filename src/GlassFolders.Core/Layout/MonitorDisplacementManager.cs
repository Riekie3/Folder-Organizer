namespace GlassFolders.Core.Layout;

/// <summary>
/// Manages multi-monitor folder placement, home monitor persistence, and graceful displacement/restoration.
/// When a secondary monitor is unplugged or turned off, folders are temporarily displayed on the primary
/// monitor with a displaced flag. When the monitor is reconnected, folders automatically return home.
/// </summary>
public sealed class MonitorDisplacementManager
{
    private readonly Dictionary<string, FolderPlacement> _placements = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, FolderPlacement> Placements => _placements;

    public FolderPlacement? GetPlacement(string folderId) =>
        _placements.TryGetValue(folderId, out var p) ? p : null;

    public FolderPlacement RegisterFolder(
        string folderId,
        string? homeMonitorKey,
        double relX,
        double relY,
        IReadOnlyList<MonitorDescriptor> activeMonitors,
        int tileWidth,
        int tileHeight)
    {
        var primary = activeMonitors.FirstOrDefault(m => m.IsPrimary) ?? activeMonitors.FirstOrDefault();
        string resolvedHome = string.IsNullOrWhiteSpace(homeMonitorKey) ? (primary?.Key ?? "PRIMARY") : homeMonitorKey;

        var placement = new FolderPlacement
        {
            FolderId = folderId,
            HomeMonitorKey = resolvedHome,
            RelativeX = Math.Clamp(relX, 0.0, 1.0),
            RelativeY = Math.Clamp(relY, 0.0, 1.0)
        };

        _placements[folderId] = placement;
        RecalculateSingle(placement, activeMonitors, tileWidth, tileHeight);
        return placement;
    }

    /// <summary>
    /// Recalculates positions for all tracked folders when the active monitor configuration changes.
    /// </summary>
    public void UpdateMonitors(IReadOnlyList<MonitorDescriptor> activeMonitors, int tileWidth, int tileHeight)
    {
        if (activeMonitors.Count == 0) return;

        foreach (var placement in _placements.Values)
        {
            RecalculateSingle(placement, activeMonitors, tileWidth, tileHeight);
        }
    }

    private static void RecalculateSingle(FolderPlacement placement, IReadOnlyList<MonitorDescriptor> activeMonitors, int tileWidth, int tileHeight)
    {
        var homeMonitor = activeMonitors.FirstOrDefault(m => string.Equals(m.Key, placement.HomeMonitorKey, StringComparison.OrdinalIgnoreCase));
        var primaryMonitor = activeMonitors.FirstOrDefault(m => m.IsPrimary) ?? activeMonitors.First();

        if (homeMonitor != null)
        {
            // Home monitor is online! Restore to home position.
            placement.IsDisplaced = false;
            var (x, y) = PositionMath.ToAbsolute(placement.RelativeX, placement.RelativeY, homeMonitor.WorkArea, tileWidth, tileHeight);
            placement.AbsoluteX = x;
            placement.AbsoluteY = y;
        }
        else
        {
            // Home monitor is offline. Displace onto primary/fallback monitor WITHOUT modifying home layout.
            placement.IsDisplaced = true;
            var (x, y) = PositionMath.ToAbsolute(placement.RelativeX, placement.RelativeY, primaryMonitor.WorkArea, tileWidth, tileHeight);
            placement.AbsoluteX = x;
            placement.AbsoluteY = y;
        }
    }

    /// <summary>
    /// Updates the position of a folder after dragging.
    /// If displaced and keepHere is false, stores as temporary position without overwriting home layout.
    /// If keepHere is true, re-homes the folder to the new monitor.
    /// </summary>
    public void SetPosition(
        string folderId,
        int pixelX,
        int pixelY,
        IReadOnlyList<MonitorDescriptor> activeMonitors,
        int tileWidth,
        int tileHeight,
        bool keepHere = false)
    {
        if (!_placements.TryGetValue(folderId, out var placement)) return;

        var monitorUnderPoint = activeMonitors.FirstOrDefault(m => m.Bounds.Contains(pixelX, pixelY))
            ?? activeMonitors.FirstOrDefault(m => m.IsPrimary)
            ?? activeMonitors.FirstOrDefault();

        if (monitorUnderPoint == null) return;

        var (clampedX, clampedY) = PositionMath.ClampToWorkArea(pixelX, pixelY, tileWidth, tileHeight, monitorUnderPoint.WorkArea);

        if (placement.IsDisplaced && !keepHere)
        {
            // Displaced folder dragged on fallback monitor: update coordinates only, keep home saved!
            placement.AbsoluteX = clampedX;
            placement.AbsoluteY = clampedY;
        }
        else
        {
            // Re-home or normal move
            placement.HomeMonitorKey = monitorUnderPoint.Key;
            placement.IsDisplaced = false;
            placement.AbsoluteX = clampedX;
            placement.AbsoluteY = clampedY;

            var (relX, relY) = PositionMath.ToRelative(clampedX, clampedY, monitorUnderPoint.WorkArea, tileWidth, tileHeight);
            placement.RelativeX = relX;
            placement.RelativeY = relY;
        }
    }

    /// <summary>
    /// Explicitly moves a folder to a target monitor ("Move to Monitor ▸"), updating its home assignment.
    /// </summary>
    public bool MoveToMonitor(string folderId, string targetMonitorKey, IReadOnlyList<MonitorDescriptor> activeMonitors, int tileWidth, int tileHeight)
    {
        if (!_placements.TryGetValue(folderId, out var placement)) return false;

        var target = activeMonitors.FirstOrDefault(m => string.Equals(m.Key, targetMonitorKey, StringComparison.OrdinalIgnoreCase));
        if (target == null) return false;

        placement.HomeMonitorKey = target.Key;
        placement.IsDisplaced = false;

        var (x, y) = PositionMath.ToAbsolute(placement.RelativeX, placement.RelativeY, target.WorkArea, tileWidth, tileHeight);
        placement.AbsoluteX = x;
        placement.AbsoluteY = y;
        return true;
    }
}
