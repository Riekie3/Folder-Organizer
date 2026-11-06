using GlassFolders.Core.Layout;

namespace GlassFolders.Core.Tests.Layout;

public sealed class MonitorDisplacementTests
{
    private const int TileW = 104;
    private const int TileH = 112;

    private static readonly MonitorDescriptor Monitor1 = new()
    {
        Key = "DISPLAY1",
        Name = "Primary Display",
        Bounds = new RectInt(0, 0, 1920, 1080),
        WorkArea = new RectInt(0, 0, 1920, 1040),
        IsPrimary = true
    };

    private static readonly MonitorDescriptor Monitor2 = new()
    {
        Key = "DISPLAY2",
        Name = "Secondary Display Right",
        Bounds = new RectInt(1920, 0, 1920, 1080),
        WorkArea = new RectInt(1920, 0, 1920, 1080),
        IsPrimary = false
    };

    private static readonly MonitorDescriptor Monitor3 = new()
    {
        Key = "DISPLAY3",
        Name = "Tertiary Display Left",
        Bounds = new RectInt(-1920, 0, 1920, 1080),
        WorkArea = new RectInt(-1920, 0, 1920, 1080),
        IsPrimary = false
    };

    [Fact]
    public void Single_monitor_normal_registration_and_placement()
    {
        var mgr = new MonitorDisplacementManager();
        var monitors = new[] { Monitor1 };

        var p = mgr.RegisterFolder("work", Monitor1.Key, 0.5, 0.5, monitors, TileW, TileH);

        Assert.False(p.IsDisplaced);
        Assert.Equal(Monitor1.Key, p.HomeMonitorKey);
        Assert.True(p.AbsoluteX > 0 && p.AbsoluteX < 1920);
    }

    [Fact]
    public void Secondary_monitor_placement_is_active_when_online()
    {
        var mgr = new MonitorDisplacementManager();
        var monitors = new[] { Monitor1, Monitor2 };

        var p = mgr.RegisterFolder("games", Monitor2.Key, 0.1, 0.1, monitors, TileW, TileH);

        Assert.False(p.IsDisplaced);
        Assert.Equal(Monitor2.Key, p.HomeMonitorKey);
        Assert.True(p.AbsoluteX >= 1920, "Should be placed in secondary monitor coordinate space");
    }

    [Fact]
    public void Secondary_monitor_disconnect_displaces_to_primary_preserving_home()
    {
        var mgr = new MonitorDisplacementManager();
        var monitors = new[] { Monitor1, Monitor2 };

        var p = mgr.RegisterFolder("games", Monitor2.Key, 0.5, 0.5, monitors, TileW, TileH);
        Assert.False(p.IsDisplaced);

        // Disconnect Monitor2: active is now only Monitor1
        mgr.UpdateMonitors(new[] { Monitor1 }, TileW, TileH);

        Assert.True(p.IsDisplaced);
        Assert.Equal(Monitor2.Key, p.HomeMonitorKey); // Preserved!
        Assert.True(p.AbsoluteX >= 0 && p.AbsoluteX < 1920, "Displaced onto primary monitor");
    }

    [Fact]
    public void Secondary_monitor_reconnect_restores_home_position()
    {
        var mgr = new MonitorDisplacementManager();
        var twoMonitors = new[] { Monitor1, Monitor2 };
        var oneMonitor = new[] { Monitor1 };

        var p = mgr.RegisterFolder("games", Monitor2.Key, 0.2, 0.3, twoMonitors, TileW, TileH);
        int initialX = p.AbsoluteX;
        int initialY = p.AbsoluteY;

        // Disconnect
        mgr.UpdateMonitors(oneMonitor, TileW, TileH);
        Assert.True(p.IsDisplaced);

        // Reconnect
        mgr.UpdateMonitors(twoMonitors, TileW, TileH);
        Assert.False(p.IsDisplaced);
        Assert.Equal(initialX, p.AbsoluteX);
        Assert.Equal(initialY, p.AbsoluteY);
    }

    [Fact]
    public void Dragging_displaced_folder_is_temporary_unless_keepHere_is_true()
    {
        var mgr = new MonitorDisplacementManager();
        var p = mgr.RegisterFolder("games", Monitor2.Key, 0.5, 0.5, new[] { Monitor1 }, TileW, TileH);
        Assert.True(p.IsDisplaced);

        // Drag on fallback monitor without keepHere
        mgr.SetPosition("games", 300, 300, new[] { Monitor1 }, TileW, TileH, keepHere: false);
        Assert.True(p.IsDisplaced);
        Assert.Equal(Monitor2.Key, p.HomeMonitorKey); // Home preserved
        Assert.Equal(300, p.AbsoluteX);

        // Drag with keepHere = true
        mgr.SetPosition("games", 400, 400, new[] { Monitor1 }, TileW, TileH, keepHere: true);
        Assert.False(p.IsDisplaced);
        Assert.Equal(Monitor1.Key, p.HomeMonitorKey); // Re-homed to Monitor1
    }

    [Fact]
    public void Three_monitors_reorder_and_primary_swap()
    {
        var mgr = new MonitorDisplacementManager();
        var allThree = new[] { Monitor3, Monitor1, Monitor2 };

        var p3 = mgr.RegisterFolder("code", Monitor3.Key, 0.5, 0.5, allThree, TileW, TileH);
        Assert.False(p3.IsDisplaced);
        Assert.True(p3.AbsoluteX < 0, "Placed on left monitor (negative coordinates)");

        // Swap primary from Monitor1 to Monitor2
        var swapped = new[]
        {
            new MonitorDescriptor { Key = Monitor1.Key, Bounds = Monitor1.Bounds, WorkArea = Monitor1.WorkArea, IsPrimary = false },
            new MonitorDescriptor { Key = Monitor2.Key, Bounds = Monitor2.Bounds, WorkArea = Monitor2.WorkArea, IsPrimary = true },
            Monitor3
        };

        mgr.UpdateMonitors(swapped, TileW, TileH);
        Assert.False(p3.IsDisplaced);
        Assert.True(p3.AbsoluteX < 0, "Left monitor position remains stable across primary swap");
    }

    [Fact]
    public void MoveToMonitor_updates_home_and_repositions()
    {
        var mgr = new MonitorDisplacementManager();
        var monitors = new[] { Monitor1, Monitor2 };

        var p = mgr.RegisterFolder("media", Monitor1.Key, 0.5, 0.5, monitors, TileW, TileH);
        Assert.Equal(Monitor1.Key, p.HomeMonitorKey);

        bool moved = mgr.MoveToMonitor("media", Monitor2.Key, monitors, TileW, TileH);
        Assert.True(moved);
        Assert.Equal(Monitor2.Key, p.HomeMonitorKey);
        Assert.False(p.IsDisplaced);
        Assert.True(p.AbsoluteX >= 1920);
    }
}
