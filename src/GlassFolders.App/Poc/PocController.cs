using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GlassFolders.App.Desktop;
using GlassFolders.App.Services;
using GlassFolders.App.UI;
using GlassFolders.Core.Config;
using GlassFolders.Core.Folders;
using GlassFolders.Core.Layout;
using GlassFolders.Core.Models;
using GlassFolders.Core.Popup;
using GlassFolders.Core.Poc;
using GlassFolders.Native;
using GlassFolders.Native.Shell;
using Microsoft.Win32;
using static GlassFolders.Native.NativeMethods;

namespace GlassFolders.App.Poc;

/// <summary>
/// Drives the desktop organizer: manages folders on the desktop layer,
/// multi-monitor placement and displacement, application launching, system tray integration,
/// memory management/trimming, and automated recovery via ExplorerRecoveryService.
/// </summary>
public sealed class PocController : IDisposable
{
    /// <summary>Folder window size before Stage 3, in DIPs. Used once to move old saved positions.</summary>
    private const double V1TileWidth = 104, V1TileHeight = 112;

    /// <summary>Windows' medium desktop grid in DIPs, used if the desktop's own grid can't be read.</summary>
    private const double FallbackCellWidth = 76, FallbackCellHeight = 98;

    /// <summary>Largest picture accepted for Change Icon.</summary>
    private const long MaxPictureBytes = 10 * 1024 * 1024;

    private static readonly string[] Glyphs = ["Folder", "Briefcase", "Gamepad", "Play", "Gear", "Code", "Palette", "Document"];

    private readonly IDesktopHost _host;
    private readonly ShellWatcher _watcher;
    private readonly ExplorerRecoveryService _recoveryService;
    private readonly TrayService _tray;
    private readonly MonitorService _monitorService;
    private readonly MonitorDisplacementManager _displacementManager;
    private readonly TileLayoutStore _store;
    private readonly TileLayout _layout;
    private readonly ConfigurationManager _configManager;
    private readonly FolderManager _folderManager;
    private readonly IconCacheService _iconCache;
    private readonly ShellShortcutResolver _resolver;
    private readonly WallpaperService _wallpaper = new();
    private readonly Dictionary<string, (FolderTile Tile, DesktopSurface Surface)> _tiles = [];
    private readonly string _dataDir;
    private readonly DispatcherTimer _memoryTrimTimer;
    private readonly DispatcherTimer _backdropTimer;
    private AppSettings _settings;
    private double _cellWidthDip = FallbackCellWidth, _cellHeightDip = FallbackCellHeight;
    private FolderPopup? _popup;
    private string? _popupFor;
    private string? _lastClosedPopupId;
    private long _lastClosedPopupTimestamp;
    private SettingsWindow? _activeSettingsWindow;
    private bool _areFoldersHidden;
    private bool _exiting;

    public PocController(DesktopHostKind kind, bool resetLayout)
    {
        _dataDir = Path.Combine(AppPaths.BaseLocalAppDataDir, "poc");
        Directory.CreateDirectory(_dataDir);
        string layoutPath = Path.Combine(AppPaths.BaseAppDataDir, $"poc-layout-{kind}.json");
        if (resetLayout && File.Exists(layoutPath)) File.Delete(layoutPath);

        _store = new TileLayoutStore(layoutPath);
        _layout = _store.Load();

        _configManager = new ConfigurationManager();
        _folderManager = new FolderManager(_configManager);
        _iconCache = new IconCacheService();
        _resolver = new ShellShortcutResolver();
        _settings = _configManager.LoadSettings();
        AnimationService.Instance.Settings = _settings.Animation;
        AppearanceService.Instance.Apply(_settings);
        DiagnosticService.Instance.CurrentLogLevel = DiagnosticService.LevelFor(_settings.LogLevel);

        _host = DesktopHostFactory.Create(kind);
        _watcher = new ShellWatcher();
        _recoveryService = new ExplorerRecoveryService(Dispatcher.CurrentDispatcher);
        _tray = new TrayService();
        _monitorService = new MonitorService();
        _displacementManager = new MonitorDisplacementManager();

        _memoryTrimTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _memoryTrimTimer.Tick += (_, _) =>
        {
            _memoryTrimTimer.Stop();
            PerformMemoryTrim();
        };

        // Wallpaper and display changes arrive in bursts; redraw the folders' glass once they settle.
        _backdropTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _backdropTimer.Tick += (_, _) =>
        {
            _backdropTimer.Stop();
            RefreshAllBackdrops();
        };

        // Wire recovery state machine
        _recoveryService.ReattachCallback = DoReattach;
        _recoveryService.VerifyCallback = VerifyDesktopWindows;

        _watcher.ShellLost += () => _recoveryService.NotifyShellLost();
        _watcher.ShellReady += () =>
        {
            _tray.RecreateIcon();
            _recoveryService.NotifyShellReady();
        };
        _watcher.SystemResumed += () => _recoveryService.NotifySystemResume();

        // Wire display and monitor change events
        _watcher.SystemMessage += msg =>
        {
            if (msg is WM_DISPLAYCHANGE or WM_DPICHANGED or WM_SETTINGCHANGE)
            {
                _monitorService.NotifyDisplayChanged();
                // A new wallpaper is announced as a setting change.
                _backdropTimer.Stop();
                _backdropTimer.Start();
            }
        };
        _monitorService.MonitorsChanged += OnMonitorsChanged;

        // Wire system tray actions
        _tray.NewFolderRequested += OpenNewFolderDialog;
        _tray.ToggleHideAllRequested += ToggleHideAll;
        _tray.TogglePauseRequested += () => SetPause(!_recoveryService.IsPaused);
        _tray.SettingsRequested += () => OpenSettings();
        _tray.RecoverNowRequested += () => _recoveryService.RecoverNow();
        _tray.AboutRequested += () => OpenSettings("About");
        _tray.ExitRequested += () => Application.Current.Shutdown();

        // Wire folder manager updates to sync desktop tiles and popups
        _folderManager.Changed += OnFolderManagerChanged;
    }

    private static double Scale => GetDpiForSystem() / 96.0;

    private void OnFolderManagerChanged()
    {
        foreach (var id in _tiles.Keys.ToList()) RefreshTile(id);
        if (_popup is not null && _popup.IsLoaded)
        {
            _popup.RefreshItems();
        }
    }

    public void Start()
    {
        DiagnosticService.Instance.LogInfo($"Starting GlassFolders (host={_host.Name}, pid={Environment.ProcessId})");
        FolderTile.Trace = msg => DiagnosticService.Instance.LogDebug($"[FolderTile] {msg}");

        if (!_host.RefreshDesktop())
        {
            DiagnosticService.Instance.LogWarn("Desktop not ready at startup; initiating recovery state machine");
            _recoveryService.RecoverNow();
            return;
        }

        CreateAllTiles();
        if (_host.Desktop != null)
            _watcher.Watch(_host.Desktop.Progman);

        WriteState("READY");
        _recoveryService.RecordEvent("Startup complete, desktop ready");

        _memoryTrimTimer.Start();
    }

    private void PerformMemoryTrim()
    {
        try
        {
            IconCacheService.TrimMemoryCache();
            _wallpaper.ReleaseCache();
            GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            EmptyWorkingSet(Process.GetCurrentProcess().Handle);
            DiagnosticService.Instance.LogDebug($"Memory trimmed. WorkingSet: {Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024)} MB");
        }
        catch (Exception ex)
        {
            DiagnosticService.Instance.LogError("Memory trim failed", ex);
        }
    }

    private void OnMonitorsChanged(IReadOnlyList<MonitorDescriptor> monitors)
    {
        var (tileW, tileH) = WindowPx(MetricsFor(FolderSize.Medium));
        _displacementManager.UpdateMonitors(monitors, tileW, tileH);

        foreach (var (id, pair) in _tiles)
        {
            var p = _displacementManager.GetPlacement(id);
            if (p != null)
            {
                _host.Move(pair.Surface, p.AbsoluteX, p.AbsoluteY);
            }
        }
        _backdropTimer.Stop();
        _backdropTimer.Start();
        WriteState("MONITORS-CHANGED");
    }

    public void OpenSettings(string? initialPage = null)
    {
        if (_activeSettingsWindow != null && _activeSettingsWindow.IsLoaded)
        {
            if (initialPage != null) _activeSettingsWindow.ShowPage(initialPage);
            _activeSettingsWindow.Activate();
            _activeSettingsWindow.Focus();
            return;
        }

        _activeSettingsWindow = new SettingsWindow(_configManager, _folderManager, _recoveryService, _monitorService);
        _activeSettingsWindow.FolderCreated += newFolder => PlaceAndCreateTile(newFolder);
        _activeSettingsWindow.SettingsChanged += () =>
        {
            bool countChanged = _settings.ShowItemCount != _configManager.LoadSettings().ShowItemCount;
            _settings = _configManager.LoadSettings();
            if (countChanged) OnFolderManagerChanged();
        };

        _activeSettingsWindow.Closed += (_, _) =>
        {
            _activeSettingsWindow = null;
            _memoryTrimTimer.Stop();
            _memoryTrimTimer.Start();
        };

        if (initialPage != null) _activeSettingsWindow.ShowPage(initialPage);
        _activeSettingsWindow.Show();
        _activeSettingsWindow.Activate();
    }

    public void OpenNewFolderDialog()
    {
        var dialog = new FolderEditorDialog(null, _folderManager.Folders.Count);
        if (dialog.ShowDialog() == true)
        {
            var res = dialog.ResultFolder;
            var created = _folderManager.CreateFolder(res.Name, res.ColorA, res.ColorB, res.Glyph);
            if (res.Size != created.Size) _folderManager.SetFolderSize(created.Id, res.Size);
            if (res.Style != created.Style) _folderManager.SetFolderStyle(created.Id, res.Style);
            PlaceAndCreateTile(created);
        }
    }

    public void ToggleHideAll()
    {
        _areFoldersHidden = !_areFoldersHidden;
        _tray.AreFoldersHidden = _areFoldersHidden;

        int showCmd = _areFoldersHidden ? 0 : 5; // SW_HIDE : SW_SHOW
        foreach (var pair in _tiles.Values)
        {
            ShowWindow(pair.Surface.Hwnd, showCmd);
        }

        if (_areFoldersHidden) _popup?.Close();
        DiagnosticService.Instance.LogInfo($"Desktop folders visibility toggled: hidden={_areFoldersHidden}");
    }

    public void SetPause(bool pause)
    {
        _recoveryService.SetPaused(pause);
        _tray.IsPaused = pause;

        int showCmd = pause ? 0 : 5;
        foreach (var pair in _tiles.Values)
        {
            ShowWindow(pair.Surface.Hwnd, showCmd);
        }

        if (pause)
        {
            _popup?.Close();
            PerformMemoryTrim();
            DiagnosticService.Instance.LogInfo("GlassFolders paused: desktop surfaces hidden, idle resources minimized");
        }
        else
        {
            DiagnosticService.Instance.LogInfo("GlassFolders resumed: verifying desktop surfaces");
            _recoveryService.VerifyOrRecover();
        }
    }

    // ----- Placement on the desktop icon grid -----

    /// <summary>
    /// Reads Windows' desktop icon grid for a work area. If Explorer can't answer, it assumes the default
    /// medium grid with no icons, and says so in the log.
    /// </summary>
    private DesktopGrid ReadGrid(RectInt workArea, out List<RectInt> iconCells)
    {
        double scale = Scale;
        var snapshot = DesktopIconGrid.TryRead(out string failure);
        int cellW, cellH;
        if (snapshot is null)
        {
            DiagnosticService.Instance.LogWarn($"Desktop icon grid unavailable ({failure}); assuming the default grid with no icons");
            cellW = (int)Math.Round(FallbackCellWidth * scale);
            cellH = (int)Math.Round(FallbackCellHeight * scale);
            iconCells = [];
        }
        else
        {
            cellW = snapshot.CellWidth;
            cellH = snapshot.CellHeight;
            iconCells = [.. snapshot.Cells];
            if (snapshot.AutoArrange)
                DiagnosticService.Instance.LogInfo("Desktop 'Auto arrange icons' is on: Windows may later move its icons under a folder");
        }

        _cellWidthDip = cellW / scale;
        _cellHeightDip = cellH / scale;
        return DesktopGrid.Create(workArea, cellW, cellH, iconCells);
    }

    private FolderTileMetrics MetricsFor(FolderSize size) => FolderTileMetrics.For(size, _cellWidthDip, _cellHeightDip);

    private static (int Width, int Height) WindowPx(FolderTileMetrics m) =>
        ((int)Math.Round(m.WindowWidth * Scale), (int)Math.Round(m.WindowHeight * Scale));

    /// <summary>The folder's box (art and label) on screen, for a window whose top-left is at (x, y).</summary>
    private static RectInt BoxAt(int x, int y, FolderTileMetrics m)
    {
        var (ww, wh) = WindowPx(m);
        int bw = (int)Math.Round(m.BoxWidth * Scale), bh = (int)Math.Round(m.BoxHeight * Scale);
        return new RectInt(x + (ww - bw) / 2, y + (wh - bh) / 2, bw, bh);
    }

    /// <summary>Window top-left that centres the box across a block of cells, with its top on the block's top like a desktop icon.</summary>
    private static (int X, int Y) PositionFor(RectInt block, FolderTileMetrics m)
    {
        var (ww, wh) = WindowPx(m);
        int bh = (int)Math.Round(m.BoxHeight * Scale);
        return (block.Left + block.Width / 2 - ww / 2, block.Top + bh / 2 - wh / 2);
    }

    private List<RectInt> Obstacles(IEnumerable<RectInt> iconCells, string? except = null)
    {
        var all = new List<RectInt>(iconCells);
        foreach (var (id, pair) in _tiles)
        {
            if (id == except || !pair.Surface.IsAlive) continue;
            var r = pair.Surface.ScreenRect;
            all.Add(BoxAt(r.Left, r.Top, pair.Tile.Metrics));
        }
        return all;
    }

    /// <summary>
    /// Finds a free spot on the primary monitor's icon grid, down each column from the left, so a new folder
    /// never lands on a desktop icon or another folder.
    /// </summary>
    private (int X, int Y) FindFreeSpot(FolderDef folder, IEnumerable<RectInt>? extraObstacles = null)
    {
        var work = Win32.PrimaryWorkArea();
        var grid = ReadGrid(ToRectInt(work), out var icons);
        var m = MetricsFor(folder.Size);
        var box = BoxAt(0, 0, m);
        var (columns, rows) = grid.SpanFor(box.Width, box.Height);

        var obstacles = Obstacles(icons);
        if (extraObstacles is not null) obstacles.AddRange(extraObstacles);

        var block = grid.FindFreeBlock(columns, rows, obstacles);
        if (block is null)
        {
            DiagnosticService.Instance.LogWarn($"No free desktop cell for folder '{folder.Name}'; placing it in the first cell");
            block = grid.Block(0, 0, columns, rows);
        }

        var spot = PositionFor(block.Value, m);
        DiagnosticService.Instance.LogInfo($"Placed folder '{folder.Name}' in free desktop cell {block} -> window ({spot.X}, {spot.Y})");
        return spot;
    }

    private void PlaceAndCreateTile(FolderDef folder)
    {
        var (x, y) = FindFreeSpot(folder);
        SavePosition(folder.Id, x, y);
        CreateTile(folder, x, y);
    }

    private void SavePosition(string id, int x, int y)
    {
        _layout.Set(id, x, y);
        _store.Save(_layout);
        _folderManager.SetFolderPosition(id, x, y);
    }

    /// <summary>
    /// Version 1 saved the top-left of a smaller window. Move each saved position once so the folder keeps
    /// the same centre, which leaves it where the user put it.
    /// </summary>
    private void MigrateLayoutToV2()
    {
        if (_layout.SchemaVersion >= TileLayout.CurrentSchemaVersion) return;

        double scale = Scale;
        int oldW = (int)Math.Round(V1TileWidth * scale), oldH = (int)Math.Round(V1TileHeight * scale);
        foreach (var placement in _layout.Tiles)
        {
            var folder = _folderManager.GetFolder(placement.Id);
            var (newW, newH) = WindowPx(MetricsFor(folder?.Size ?? FolderSize.Medium));
            placement.X += (oldW - newW) / 2;
            placement.Y += (oldH - newH) / 2;
        }
        _layout.SchemaVersion = TileLayout.CurrentSchemaVersion;
        _store.Save(_layout);
        DiagnosticService.Instance.LogInfo($"Moved {_layout.Tiles.Count} saved folder positions to the larger folder window, keeping their centres");
    }

    private void CreateAllTiles()
    {
        // Reading the grid first sets the cell size every tile's metrics depend on.
        ReadGrid(ToRectInt(Win32.PrimaryWorkArea()), out _);
        MigrateLayoutToV2();

        var folders = _folderManager.Folders.Where(f => f.IsVisible).ToList();
        var unplaced = new List<FolderDef>();
        foreach (var f in folders)
        {
            var saved = _layout.Find(f.Id);
            if (saved is not null) CreateTile(f, saved.X, saved.Y);
            else if (HasRestoredPosition(f)) CreateTile(f, f.X, f.Y);
            else unplaced.Add(f);
        }

        // Folders without a position, such as the first-run samples, go into free cells after the placed ones.
        foreach (var f in unplaced) PlaceAndCreateTile(f);
    }

    /// <summary>
    /// A position in folders.json without one in the layout comes from a restored backup. (0, 0) and (100, 100)
    /// are the defaults a new folder used to get before it was placed, so they don't count.
    /// </summary>
    private static bool HasRestoredPosition(FolderDef f) => !(f.X == 0 && f.Y == 0) && !(f.X == 100 && f.Y == 100);

    private void CreateTile(FolderDef folder, int x, int y)
    {
        var metrics = MetricsFor(folder.Size);
        var (tileW, tileH) = WindowPx(metrics);

        // Register with multi-monitor displacement manager
        var (relX, relY) = PositionMath.ToRelative(x, y, _monitorService.PrimaryMonitor.WorkArea, tileW, tileH);
        _displacementManager.RegisterFolder(folder.Id, _monitorService.PrimaryMonitor.Key, relX, relY, _monitorService.Monitors, tileW, tileH);

        var tile = new FolderTile(folder.Id);
        tile.Apply(folder, metrics, _settings.ShowItemCount);

        var surface = _host.CreateSurface(folder.Id, tile, x, y);
        tile.GetScreenRect = () => surface.ScreenRect;
        tile.Clicked += OnTileClicked;
        tile.DragMoved += (t, nx, ny) => _host.Move(_tiles[t.Id].Surface, nx, ny);
        tile.DragEnded += OnTileDragEnded;

        tile.FilesDropped += (t, files) =>
        {
            try
            {
                foreach (string file in files)
                {
                    var item = _resolver.Resolve(file);
                    _folderManager.AddItem(t.Id, item);
                }
                DiagnosticService.Instance.LogInfo($"Dropped {files.Length} files into folder '{_folderManager.GetFolder(t.Id)?.Name}'");
            }
            catch (Exception ex)
            {
                DiagnosticService.Instance.LogError($"Failed to add dropped files to folder '{_folderManager.GetFolder(t.Id)?.Name}'", ex);
            }
        };

        // Built each time it opens, so ticks and names are current.
        tile.ContextMenu = new ContextMenu();
        tile.ContextMenuOpening += (_, _) => FillMenu(tile.ContextMenu, folder.Id, tile);

        surface.Lost += s =>
        {
            if (_exiting || _recoveryService.IsPaused) return;
            DiagnosticService.Instance.LogWarn($"Surface lost for folder: {s.Id}");
            _recoveryService.RecoverNow();
        };

        _tiles[folder.Id] = (tile, surface);
        RefreshBackdrop(folder.Id);
    }

    private void OnTileDragEnded(FolderTile t, int nx, int ny)
    {
        if (!_tiles.TryGetValue(t.Id, out var pair)) return;

        if (_settings.SnapToGrid)
        {
            // Snap to the nearest free cell of the icon grid on the monitor the folder was dropped on.
            var box = BoxAt(nx, ny, t.Metrics);
            int cx = box.Left + box.Width / 2, cy = box.Top + box.Height / 2;
            var monitor = _monitorService.Monitors.FirstOrDefault(m => m.Bounds.Contains(cx, cy)) ?? _monitorService.PrimaryMonitor;
            var grid = ReadGrid(monitor.WorkArea, out var icons);
            var (columns, rows) = grid.SpanFor(box.Width, box.Height);
            if (grid.Columns >= columns && grid.Rows >= rows)
            {
                var block = grid.NearestFreeBlock(cx, cy, columns, rows, Obstacles(icons, except: t.Id));
                (nx, ny) = PositionFor(block, t.Metrics);
                _host.Move(pair.Surface, nx, ny);
            }
        }

        var (tileW, tileH) = WindowPx(t.Metrics);
        _displacementManager.SetPosition(t.Id, nx, ny, _monitorService.Monitors, tileW, tileH);
        SavePosition(t.Id, nx, ny);
        RefreshBackdrop(t.Id);
        DiagnosticService.Instance.LogDebug($"Tile moved: {t.Id} -> ({nx}, {ny})");
        WriteState("MOVED");
    }

    /// <summary>Redraws a folder from its settings. A new size keeps the folder's centre where it was.</summary>
    private void RefreshTile(string id)
    {
        if (!_tiles.TryGetValue(id, out var pair)) return;
        var folder = _folderManager.GetFolder(id);
        if (folder is null) return;

        var before = pair.Tile.Metrics;
        var after = MetricsFor(folder.Size);
        pair.Tile.Apply(folder, after, _settings.ShowItemCount);
        if (before == after) return;

        var r = pair.Surface.ScreenRect;
        var (oldW, oldH) = WindowPx(before);
        var (newW, newH) = WindowPx(after);
        int x = r.Left + (oldW - newW) / 2, y = r.Top + (oldH - newH) / 2;
        _host.Move(pair.Surface, x, y);
        _displacementManager.SetPosition(id, x, y, _monitorService.Monitors, newW, newH);
        _layout.Set(id, x, y);
        _store.Save(_layout);
        RefreshBackdrop(id);
    }

    // ----- Glass behind the folders -----

    /// <summary>The frosted plate on screen, for a window whose top-left is at (x, y).</summary>
    private static RectInt PlateAt(int x, int y, FolderTileMetrics m)
    {
        var (ww, wh) = WindowPx(m);
        int pw = (int)Math.Round(m.PlateWidth * Scale), ph = (int)Math.Round(m.PlateHeight * Scale);
        return new RectInt(x + (ww - pw) / 2, y + (wh - ph) / 2, pw, ph);
    }

    private void RefreshBackdrop(string id)
    {
        if (!_tiles.TryGetValue(id, out var pair) || !pair.Surface.IsAlive) return;
        var m = pair.Tile.Metrics;
        double scale = Scale;
        var r = pair.Surface.ScreenRect;
        var plate = PlateAt(r.Left, r.Top, m);

        var box = BoxAt(r.Left, r.Top, m);
        int line = (int)Math.Round(m.LabelLineHeight * scale);
        var label = new RectInt(box.Left, box.Bottom - (int)Math.Round(FolderTileMetrics.BoxBottomPadding * scale) - line, box.Width, line);

        var backdrop = _wallpaper.Measure(plate, label);
        pair.Tile.SetBackdrop(backdrop.Plate, backdrop.LabelLuminance);
    }

    private void RefreshAllBackdrops()
    {
        foreach (var id in _tiles.Keys.ToList()) RefreshBackdrop(id);
        _memoryTrimTimer.Stop();
        _memoryTrimTimer.Start();
    }

    // ----- Folder menu (brief §6.1) -----

    private void FillMenu(ContextMenu menu, string id, FolderTile tile)
    {
        menu.Items.Clear();
        var folder = _folderManager.GetFolder(id);
        if (folder is null) return;

        var open = new MenuItem { Header = "Open" };
        open.Click += (_, _) => OnTileClicked(tile);
        menu.Items.Add(open);

        var rename = new MenuItem { Header = "Rename..." };
        rename.Click += (_, _) =>
        {
            string? newName = FolderPopup.PromptRename(folder.Name, "Rename Folder");
            if (!string.IsNullOrWhiteSpace(newName) && newName != folder.Name)
            {
                _folderManager.RenameFolder(folder.Id, newName.Trim());
                DiagnosticService.Instance.LogInfo($"Renamed folder '{folder.Id}' to '{newName.Trim()}'");
            }
        };
        menu.Items.Add(rename);

        menu.Items.Add(BuildChangeIconMenu(folder));

        var addApp = new MenuItem { Header = "Add Application..." };
        addApp.Click += (_, _) => AddFiles(folder.Id, isApplication: true);
        menu.Items.Add(addApp);

        var addFiles = new MenuItem { Header = "Add File or Folder" };
        var files = new MenuItem { Header = "Files..." };
        files.Click += (_, _) => AddFiles(folder.Id, isApplication: false);
        var folderItem = new MenuItem { Header = "Folder..." };
        folderItem.Click += (_, _) => AddFolder(folder.Id);
        addFiles.Items.Add(files);
        addFiles.Items.Add(folderItem);
        menu.Items.Add(addFiles);

        var appearance = new MenuItem { Header = "Appearance..." };
        appearance.Click += (_, _) => EditAppearance(folder.Id);
        menu.Items.Add(appearance);

        var size = new MenuItem { Header = "Size" };
        foreach (var s in new[] { FolderSize.Small, FolderSize.Medium, FolderSize.Large })
        {
            var item = new MenuItem { Header = s.ToString(), IsCheckable = true, IsChecked = folder.Size == s };
            item.Click += (_, _) => _folderManager.SetFolderSize(folder.Id, s);
            size.Items.Add(item);
        }
        menu.Items.Add(size);

        menu.Items.Add(BuildMoveToMonitorMenu(folder));

        var delete = new MenuItem { Header = "Delete Folder" };
        delete.Click += (_, _) => DeleteFolder(folder.Id);
        menu.Items.Add(delete);

        menu.Items.Add(new Separator());

        var settings = new MenuItem { Header = "Settings..." };
        settings.Click += (_, _) => OpenSettings();
        menu.Items.Add(settings);

        var exit = new MenuItem { Header = "Exit GlassFolders" };
        exit.Click += (_, _) => Application.Current.Shutdown();
        menu.Items.Add(exit);
    }

    private MenuItem BuildChangeIconMenu(FolderDef folder)
    {
        var change = new MenuItem { Header = "Change Icon" };
        bool hasPicture = !string.IsNullOrEmpty(folder.CustomIconPath);
        foreach (string glyph in Glyphs)
        {
            var preview = new FolderIconView { Width = 20, Height = 17 };
            preview.Show(new FolderDef { ColorA = folder.ColorA, ColorB = folder.ColorB, Style = folder.Style, Glyph = glyph });
            var item = new MenuItem
            {
                Header = glyph == "Folder" ? "Plain folder" : glyph,
                Icon = preview,
                IsCheckable = true,
                IsChecked = !hasPicture && folder.Glyph == glyph,
            };
            item.Click += (_, _) =>
            {
                string? old = _folderManager.GetFolder(folder.Id)?.CustomIconPath;
                _folderManager.SetFolderGlyph(folder.Id, glyph);
                DeletePictureCopy(old);
            };
            change.Items.Add(item);
        }

        change.Items.Add(new Separator());
        var choose = new MenuItem { Header = "Choose Picture..." };
        choose.Click += (_, _) => ChooseFolderPicture(folder.Id);
        change.Items.Add(choose);

        var remove = new MenuItem { Header = "Remove Picture", IsEnabled = hasPicture };
        remove.Click += (_, _) =>
        {
            string? old = _folderManager.GetFolder(folder.Id)?.CustomIconPath;
            _folderManager.SetCustomIcon(folder.Id, null);
            DeletePictureCopy(old);
        };
        change.Items.Add(remove);
        return change;
    }

    private MenuItem BuildMoveToMonitorMenu(FolderDef folder)
    {
        var moveSub = new MenuItem { Header = "Move to Monitor" };
        foreach (var mon in _monitorService.Monitors)
        {
            var mItem = new MenuItem
            {
                Header = mon.Name,
                IsChecked = string.Equals(_displacementManager.GetPlacement(folder.Id)?.HomeMonitorKey, mon.Key, StringComparison.OrdinalIgnoreCase)
            };
            mItem.Click += (_, _) =>
            {
                if (!_tiles.TryGetValue(folder.Id, out var pair)) return;
                var (tileW, tileH) = WindowPx(pair.Tile.Metrics);
                if (_displacementManager.MoveToMonitor(folder.Id, mon.Key, _monitorService.Monitors, tileW, tileH))
                {
                    var p = _displacementManager.GetPlacement(folder.Id);
                    if (p != null)
                    {
                        _host.Move(pair.Surface, p.AbsoluteX, p.AbsoluteY);
                        SavePosition(folder.Id, p.AbsoluteX, p.AbsoluteY);
                        RefreshBackdrop(folder.Id);
                        DiagnosticService.Instance.LogInfo($"Moved folder '{folder.Name}' to monitor '{mon.Name}' at ({p.AbsoluteX}, {p.AbsoluteY})");
                    }
                }
            };
            moveSub.Items.Add(mItem);
        }
        return moveSub;
    }

    private void AddFiles(string id, bool isApplication)
    {
        var folder = _folderManager.GetFolder(id);
        if (folder is null) return;
        try
        {
            var ofd = new OpenFileDialog
            {
                Title = isApplication ? $"Add applications to {folder.Name}" : $"Add files to {folder.Name}",
                Filter = isApplication
                    ? "Applications and shortcuts (*.lnk;*.exe;*.appref-ms;*.url)|*.lnk;*.exe;*.appref-ms;*.url|All files (*.*)|*.*"
                    : "All files (*.*)|*.*",
                Multiselect = true,
                // Keep shortcuts as shortcuts, so their own icon, arguments and start folder are kept.
                DereferenceLinks = false,
            };
            if (isApplication)
                ofd.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms);

            if (ofd.ShowDialog() != true) return;
            foreach (string file in ofd.FileNames)
                _folderManager.AddItem(id, _resolver.Resolve(file));
            DiagnosticService.Instance.LogInfo($"Added {ofd.FileNames.Length} items to '{folder.Name}'");
        }
        catch (Exception ex)
        {
            DiagnosticService.Instance.LogError($"Error adding files to folder '{folder.Name}'", ex);
            GlassMessageBox.Show(null, "Couldn't add the files", $"Some files could not be added.\n\n{ex.Message}", kind: GlassMessageKind.Error);
        }
    }

    private void AddFolder(string id)
    {
        var folder = _folderManager.GetFolder(id);
        if (folder is null) return;
        try
        {
            var dialog = new OpenFolderDialog { Title = $"Add folders to {folder.Name}", Multiselect = true };
            if (dialog.ShowDialog() != true) return;
            foreach (string path in dialog.FolderNames)
                _folderManager.AddItem(id, _resolver.Resolve(path));
            DiagnosticService.Instance.LogInfo($"Added {dialog.FolderNames.Length} folders to '{folder.Name}'");
        }
        catch (Exception ex)
        {
            DiagnosticService.Instance.LogError($"Error adding a folder to '{folder.Name}'", ex);
            GlassMessageBox.Show(null, "Couldn't add the folder", $"The folder could not be added.\n\n{ex.Message}", kind: GlassMessageKind.Error);
        }
    }

    private void EditAppearance(string id)
    {
        var current = _folderManager.GetFolder(id);
        if (current is null) return;
        var dialog = new FolderEditorDialog(current, _folderManager.Folders.Count);
        if (dialog.ShowDialog() != true) return;

        var res = dialog.ResultFolder;
        _folderManager.RenameFolder(id, res.Name);
        _folderManager.SetFolderAppearance(id, res.ColorA, res.ColorB, res.Glyph);
        _folderManager.SetFolderStyle(id, res.Style);
        _folderManager.SetFolderSize(id, res.Size);
    }

    /// <summary>
    /// Change Icon → Choose Picture: copies an image the user picks into the app's own folder and shows it in
    /// place of the drawn folder. The file is only decoded as a picture; nothing is ever run.
    /// </summary>
    private void ChooseFolderPicture(string id)
    {
        var folder = _folderManager.GetFolder(id);
        if (folder is null) return;

        var ofd = new OpenFileDialog
        {
            Title = $"Choose a picture for {folder.Name}",
            Filter = "Pictures (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.ico;*.tif;*.tiff)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.ico;*.tif;*.tiff",
        };
        if (ofd.ShowDialog() != true) return;

        try
        {
            var info = new FileInfo(ofd.FileName);
            if (info.Length > MaxPictureBytes)
                throw new InvalidDataException($"The picture is {info.Length / (1024 * 1024)} MB; the limit is {MaxPictureBytes / (1024 * 1024)} MB.");

            using (var stream = info.OpenRead())
            {
                var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
                if (frame.PixelWidth <= 0 || frame.PixelHeight <= 0) throw new InvalidDataException("The file has no picture in it.");
            }

            string dir = Path.Combine(AppPaths.BaseAppDataDir, "folder-icons");
            Directory.CreateDirectory(dir);
            string copy = Path.Combine(dir, $"{id}-{DateTime.UtcNow.Ticks}{info.Extension.ToLowerInvariant()}");
            File.Copy(info.FullName, copy);

            string? old = folder.CustomIconPath;
            _folderManager.SetCustomIcon(id, copy);
            DeletePictureCopy(old);
            DiagnosticService.Instance.LogInfo($"Folder '{folder.Name}' now shows a picture ({info.Name})");
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException or InvalidDataException or ArgumentException or FileFormatException)
        {
            DiagnosticService.Instance.LogWarn($"Picture for folder '{folder.Name}' rejected: {ex.Message}");
            GlassMessageBox.Show(null, "Change icon", $"That picture can't be used.\n\n{ex.Message}", kind: GlassMessageKind.Warning);
        }
    }

    /// <summary>Removes the app's copy of a folder picture. Only files in the app's own folder are touched.</summary>
    private static void DeletePictureCopy(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        string dir = Path.GetFullPath(Path.Combine(AppPaths.BaseAppDataDir, "folder-icons")) + Path.DirectorySeparatorChar;
        try
        {
            string full = Path.GetFullPath(path);
            if (full.StartsWith(dir, StringComparison.OrdinalIgnoreCase) && File.Exists(full)) File.Delete(full);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            DiagnosticService.Instance.LogWarn($"Old folder picture could not be removed: {ex.Message}");
        }
    }

    private void DeleteFolder(string id)
    {
        var folder = _folderManager.GetFolder(id);
        if (folder is null) return;
        var res = GlassMessageBox.Show(null, "Delete folder",
            $"Remove \"{folder.Name}\" from GlassFolders?\n\nYour files and applications are not deleted.",
            GlassMessageButtons.YesNo, GlassMessageKind.Warning, yesLabel: "Delete", noLabel: "Cancel", dangerYes: true);
        if (res != GlassMessageResult.Yes) return;

        string? picture = folder.CustomIconPath;
        if (_tiles.TryGetValue(id, out var pair))
        {
            pair.Surface.Dispose();
            _tiles.Remove(id);
        }
        _folderManager.DeleteFolder(id);
        DeletePictureCopy(picture);
    }

    private void OnTileClicked(FolderTile tile)
    {
        DiagnosticService.Instance.LogDebug($"Clicked folder tile: {tile.Id}");
        bool sameFolder = _popupFor == tile.Id && _popup is not null;
        bool justDeactivated = _lastClosedPopupId == tile.Id && Stopwatch.GetElapsedTime(_lastClosedPopupTimestamp).TotalMilliseconds < 350;
        _popup?.Close();
        if (sameFolder || justDeactivated)
        {
            _lastClosedPopupId = null;
            return;
        }

        var folder = _folderManager.GetFolder(tile.Id);
        if (folder is null) return;

        // The popup measures its distance from the plate, the part that lights up, as in the reference.
        var window = tile.GetScreenRect?.Invoke() ?? default;
        var plate = PlateAt(window.Left, window.Top, tile.Metrics);
        var anchor = new RECT(plate.Left, plate.Top, plate.Right, plate.Bottom);
        _popup = new FolderPopup(folder, _folderManager, _iconCache, _resolver, anchor, PopupGrid.From(_settings), _settings.CheckMissingTargets);
        _popupFor = tile.Id;
        tile.SetOpen(true);

        _popup.Closed += (_, _) =>
        {
            tile.SetOpen(false);
            _lastClosedPopupId = tile.Id;
            _lastClosedPopupTimestamp = Stopwatch.GetTimestamp();
            _popup = null;
            _popupFor = null;
            WriteState("POPUP-CLOSED");
            _memoryTrimTimer.Stop();
            _memoryTrimTimer.Start();
        };

        _popup.Show();
        WriteState("POPUP-OPEN");
    }

    private bool DoReattach()
    {
        if (_exiting || _recoveryService.IsPaused) return true;

        DiagnosticService.Instance.LogInfo("Executing shell reattach sequence...");
        if (!_host.RefreshDesktop())
        {
            DiagnosticService.Instance.LogWarn("Desktop refresh failed; Progman not available yet");
            return false;
        }

        var folders = _folderManager.Folders.Where(f => f.IsVisible).ToList();
        int kept = 0, recreated = 0;

        foreach (var f in folders)
        {
            if (_tiles.TryGetValue(f.Id, out var t) && _host.Reattach(t.Surface))
            {
                kept++;
                continue;
            }

            RECT last = default;
            if (_tiles.TryGetValue(f.Id, out var dead))
            {
                if (dead.Surface.IsAlive) last = dead.Surface.ScreenRect;
                dead.Surface.Dispose();
                _tiles.Remove(f.Id);
            }

            var saved = _layout.Find(f.Id);
            if (saved is not null) CreateTile(f, saved.X, saved.Y);
            else if (last.Width > 0) CreateTile(f, last.Left, last.Top);
            else PlaceAndCreateTile(f);
            recreated++;
        }

        if (_host.Desktop != null)
        {
            _watcher.Watch(_host.Desktop.Progman);
            DiagnosticService.Instance.LogInfo($"Reattach complete: kept={kept}, recreated={recreated}, progman=0x{_host.Desktop.Progman:X}");
        }
        else
        {
            DiagnosticService.Instance.LogInfo($"Reattach complete: kept={kept}, recreated={recreated}, progman=(none)");
        }

        WriteState("REATTACHED");
        return true;
    }

    private bool VerifyDesktopWindows()
    {
        if (_exiting || _recoveryService.IsPaused) return true;

        if (_host.Desktop?.Progman == 0 || !IsWindow(_host.Desktop!.Progman))
        {
            DiagnosticService.Instance.LogWarn("Verification failed: Progman HWND is invalid or destroyed");
            return false;
        }

        var visibleFolders = _folderManager.Folders.Where(f => f.IsVisible).ToList();
        foreach (var folder in visibleFolders)
        {
            if (!_tiles.TryGetValue(folder.Id, out var pair))
            {
                DiagnosticService.Instance.LogWarn($"Verification failed: missing tile surface for folder '{folder.Name}'");
                return false;
            }

            if (!pair.Surface.IsAlive || !_host.IsAttached(pair.Surface))
            {
                DiagnosticService.Instance.LogWarn($"Verification failed: surface for folder '{folder.Name}' is not attached to desktop");
                return false;
            }
        }

        return true;
    }

    private void WriteState(string reason)
    {
        var state = new
        {
            reason,
            pid = Environment.ProcessId,
            host = _host.Name,
            progman = (long)(_host.Desktop?.Progman ?? 0),
            reattachCount = _recoveryService.ReattachCount,
            recoveryState = _recoveryService.State.ToString(),
            popupOpen = _popupFor,
            monitors = _monitorService.Monitors.Select(m => new { key = m.Key, name = m.Name, workArea = m.WorkArea.ToString() }).ToArray(),
            tiles = _tiles.Values.Select(v =>
            {
                var r = v.Surface.ScreenRect;
                var placement = _displacementManager.GetPlacement(v.Tile.Id);
                return new
                {
                    id = v.Tile.Id,
                    hwnd = (long)v.Surface.Hwnd,
                    left = r.Left,
                    top = r.Top,
                    right = r.Right,
                    bottom = r.Bottom,
                    homeMonitor = placement?.HomeMonitorKey,
                    isDisplaced = placement?.IsDisplaced ?? false,
                    attached = _host.IsAttached(v.Surface)
                };
            }).ToArray(),
            utc = DateTime.UtcNow,
        };
        try { File.WriteAllText(Path.Combine(_dataDir, "state.json"), JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true })); }
        catch (IOException) { }
    }

    private static RectInt ToRectInt(RECT r) => new(r.Left, r.Top, r.Width, r.Height);

    public void Dispose()
    {
        if (_exiting) return;
        _exiting = true;

        DiagnosticService.Instance.LogInfo("Beginning clean ordered shutdown of GlassFolders...");
        _memoryTrimTimer.Stop();
        _backdropTimer.Stop();
        _recoveryService.IsShuttingDown = true;
        _recoveryService.Dispose();

        _tray.Dispose();
        _monitorService.Dispose();
        _popup?.Close();
        _activeSettingsWindow?.Close();

        foreach (var t in _tiles.Values)
        {
            t.Surface.Dispose();
        }
        _tiles.Clear();

        _watcher.Dispose();

        try
        {
            _store.Save(_layout);
        }
        catch { }

        DiagnosticService.Instance.LogInfo("GlassFolders cleanly exited.");
    }
}
