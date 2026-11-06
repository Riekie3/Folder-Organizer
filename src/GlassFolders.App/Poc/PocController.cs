using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using GlassFolders.App.Desktop;
using GlassFolders.App.Services;
using GlassFolders.App.UI;
using GlassFolders.Core.Config;
using GlassFolders.Core.Folders;
using GlassFolders.Core.Models;
using GlassFolders.Core.Poc;
using GlassFolders.Native;
using GlassFolders.Native.Shell;
using Microsoft.Win32;
using static GlassFolders.Native.NativeMethods;

namespace GlassFolders.App.Poc;

/// <summary>
/// Drives the desktop organizer: manages folders on the desktop layer,
/// launches applications/shortcuts, handles drag &amp; drop, system tray integration,
/// and automated recovery via ExplorerRecoveryService.
/// </summary>
public sealed class PocController : IDisposable
{
    private readonly IDesktopHost _host;
    private readonly ShellWatcher _watcher;
    private readonly ExplorerRecoveryService _recoveryService;
    private readonly TrayService _tray;
    private readonly TileLayoutStore _store;
    private readonly TileLayout _layout;
    private readonly ConfigurationManager _configManager;
    private readonly FolderManager _folderManager;
    private readonly IconCacheService _iconCache;
    private readonly ShellShortcutResolver _resolver;
    private readonly Dictionary<string, (FolderTile Tile, DesktopSurface Surface)> _tiles = [];
    private readonly string _dataDir;
    private FolderPopup? _popup;
    private string? _popupFor;
    private string? _lastClosedPopupId;
    private long _lastClosedPopupTimestamp;
    private SettingsWindow? _activeSettingsWindow;
    private bool _areFoldersHidden;
    private bool _exiting;

    public PocController(DesktopHostKind kind, bool resetLayout)
    {
        _dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GlassFolders", "poc");
        Directory.CreateDirectory(_dataDir);
        string layoutPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GlassFolders", $"poc-layout-{kind}.json");
        if (resetLayout && File.Exists(layoutPath)) File.Delete(layoutPath);

        _store = new TileLayoutStore(layoutPath);
        _layout = _store.Load();

        _configManager = new ConfigurationManager();
        _folderManager = new FolderManager(_configManager);
        _iconCache = new IconCacheService();
        _resolver = new ShellShortcutResolver();

        _host = DesktopHostFactory.Create(kind);
        _watcher = new ShellWatcher();
        _recoveryService = new ExplorerRecoveryService(Dispatcher.CurrentDispatcher);
        _tray = new TrayService();

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

        // Wire system tray actions
        _tray.NewFolderRequested += OpenNewFolderDialog;
        _tray.ToggleHideAllRequested += ToggleHideAll;
        _tray.TogglePauseRequested += () => SetPause(!_recoveryService.IsPaused);
        _tray.SettingsRequested += () => OpenSettings();
        _tray.RecoverNowRequested += () => _recoveryService.RecoverNow();
        _tray.AboutRequested += () => OpenSettings("About");
        _tray.ExitRequested += () => Application.Current.Shutdown();
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
        _watcher.Watch(_host.Desktop!.Progman);
        WriteState("READY");
        _recoveryService.RecordEvent("Startup complete, desktop ready");
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

        _activeSettingsWindow = new SettingsWindow(_configManager, _folderManager, _recoveryService);
        _activeSettingsWindow.FolderCreated += newFolder =>
        {
            var work = Win32.PrimaryWorkArea();
            double scale = GetDpiForSystem() / 96.0;
            int tileW = (int)(104 * scale), tileH = (int)(112 * scale);
            int count = _tiles.Count;
            int defX = work.Right - tileW - (int)(24 * scale);
            int defY = work.Top + (int)(24 * scale) + count * (tileH + (int)(12 * scale));
            CreateTile(newFolder, defX, defY);
        };

        _activeSettingsWindow.Closed += (_, _) => _activeSettingsWindow = null;
        if (initialPage != null) _activeSettingsWindow.ShowPage(initialPage);
        _activeSettingsWindow.Show();
        _activeSettingsWindow.Activate();
    }

    public void OpenNewFolderDialog()
    {
        var dialog = new FolderEditorDialog();
        if (dialog.ShowDialog() == true)
        {
            var res = dialog.ResultFolder;
            var work = Win32.PrimaryWorkArea();
            double scale = GetDpiForSystem() / 96.0;
            int tileW = (int)(104 * scale), tileH = (int)(112 * scale);
            int count = _tiles.Count;
            int defX = work.Right - tileW - (int)(24 * scale);
            int defY = work.Top + (int)(24 * scale) + count * (tileH + (int)(12 * scale));

            var created = _folderManager.CreateFolder(res.Name, res.ColorA, res.ColorB, res.Glyph, defX, defY);
            CreateTile(created, defX, defY);
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
            DiagnosticService.Instance.LogInfo("GlassFolders paused: desktop surfaces hidden, idle resources minimized");
        }
        else
        {
            DiagnosticService.Instance.LogInfo("GlassFolders resumed: verifying desktop surfaces");
            _recoveryService.VerifyOrRecover();
        }
    }

    private void CreateAllTiles()
    {
        var work = Win32.PrimaryWorkArea();
        double scale = GetDpiForSystem() / 96.0;
        int tileW = (int)(104 * scale), tileH = (int)(112 * scale);

        var folders = _folderManager.Folders.Where(f => f.IsVisible).ToList();
        for (int i = 0; i < folders.Count; i++)
        {
            var f = folders[i];
            var saved = _layout.Find(f.Id);
            int defX = work.Right - tileW - (int)(24 * scale);
            int defY = work.Top + (int)(24 * scale) + i * (tileH + (int)(12 * scale));
            int x = saved?.X ?? (f.X > 0 ? f.X : defX);
            int y = saved?.Y ?? (f.Y > 0 ? f.Y : defY);
            CreateTile(f, x, y);
        }
    }

    private void CreateTile(FolderDef folder, int x, int y)
    {
        Color colorA = ParseColor(folder.ColorA, Color.FromRgb(0x7F, 0xB2, 0xFF));
        Color colorB = ParseColor(folder.ColorB, Color.FromRgb(0x5B, 0x5C, 0xF2));

        var tile = new FolderTile(folder.Id, folder.Name, colorA, colorB);
        tile.SetGlyph(folder.Glyph);
        tile.SetItemCount(folder.Items.Count);

        var surface = _host.CreateSurface(folder.Id, tile, x, y);
        tile.GetScreenRect = () => surface.ScreenRect;
        tile.Clicked += OnTileClicked;
        tile.DragMoved += (t, nx, ny) => _host.Move(_tiles[t.Id].Surface, nx, ny);
        tile.DragEnded += (t, nx, ny) =>
        {
            _layout.Set(t.Id, nx, ny);
            _store.Save(_layout);
            _folderManager.SetFolderPosition(t.Id, nx, ny);
            DiagnosticService.Instance.LogDebug($"Tile moved: {t.Id} -> ({nx}, {ny})");
            WriteState("MOVED");
        };

        tile.FilesDropped += (t, files) =>
        {
            foreach (string file in files)
            {
                var item = _resolver.Resolve(file);
                _folderManager.AddItem(t.Id, item);
            }
            var f = _folderManager.GetFolder(t.Id);
            if (f is not null) tile.SetItemCount(f.Items.Count);
            DiagnosticService.Instance.LogInfo($"Dropped {files.Length} files into folder '{folder.Name}'");
        };

        tile.ContextMenu = BuildMenu(folder, tile);
        surface.Lost += s =>
        {
            if (_exiting || _recoveryService.IsPaused) return;
            DiagnosticService.Instance.LogWarn($"Surface lost for folder: {s.Id}");
            _recoveryService.RecoverNow();
        };

        _tiles[folder.Id] = (tile, surface);
    }

    private ContextMenu BuildMenu(FolderDef folder, FolderTile tile)
    {
        var menu = new ContextMenu();

        var open = new MenuItem { Header = "Open Folder" };
        open.Click += (_, _) => OnTileClicked(tile);
        menu.Items.Add(open);

        var add = new MenuItem { Header = "Add Shortcuts / Files..." };
        add.Click += (_, _) =>
        {
            var ofd = new OpenFileDialog
            {
                Title = $"Add to {folder.Name}",
                Filter = "All Files (*.*)|*.*|Shortcuts (*.lnk)|*.lnk|Applications (*.exe)|*.exe",
                Multiselect = true
            };
            if (ofd.ShowDialog() == true)
            {
                foreach (string file in ofd.FileNames)
                {
                    var item = _resolver.Resolve(file);
                    _folderManager.AddItem(folder.Id, item);
                }
                tile.SetItemCount(folder.Items.Count);
            }
        };
        menu.Items.Add(add);

        var edit = new MenuItem { Header = "Edit Appearance..." };
        edit.Click += (_, _) =>
        {
            var dialog = new FolderEditorDialog(folder);
            if (dialog.ShowDialog() == true)
            {
                var res = dialog.ResultFolder;
                _folderManager.RenameFolder(folder.Id, res.Name);
                _folderManager.SetFolderAppearance(folder.Id, res.ColorA, res.ColorB, res.Glyph);
                tile.SetLabel(res.Name);
                tile.SetGlyph(res.Glyph);
                tile.SetColors(ParseColor(res.ColorA, Colors.Blue), ParseColor(res.ColorB, Colors.Navy));
            }
        };
        menu.Items.Add(edit);

        var settings = new MenuItem { Header = "Settings..." };
        settings.Click += (_, _) => OpenSettings();
        menu.Items.Add(settings);

        menu.Items.Add(new Separator());

        var delete = new MenuItem { Header = "Delete Folder" };
        delete.Click += (_, _) =>
        {
            var res = MessageBox.Show(
                $"Remove folder \"{folder.Name}\" from GlassFolders?\n\nYour actual files and applications will NOT be deleted.",
                "Delete Folder", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (res == MessageBoxResult.Yes)
            {
                _folderManager.DeleteFolder(folder.Id);
                if (_tiles.TryGetValue(folder.Id, out var pair))
                {
                    pair.Surface.Dispose();
                    _tiles.Remove(folder.Id);
                }
            }
        };
        menu.Items.Add(delete);

        var exit = new MenuItem { Header = "Exit GlassFolders" };
        exit.Click += (_, _) => Application.Current.Shutdown();
        menu.Items.Add(exit);

        return menu;
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

        var anchor = tile.GetScreenRect?.Invoke() ?? default;
        _popup = new FolderPopup(folder, _folderManager, _iconCache, _resolver, anchor);
        _popupFor = tile.Id;

        _popup.Closed += (_, _) =>
        {
            _lastClosedPopupId = tile.Id;
            _lastClosedPopupTimestamp = Stopwatch.GetTimestamp();
            _popup = null;
            _popupFor = null;
            WriteState("POPUP-CLOSED");
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

        var work = Win32.PrimaryWorkArea();
        double scale = GetDpiForSystem() / 96.0;
        int tileW = (int)(104 * scale), tileH = (int)(112 * scale);

        var folders = _folderManager.Folders.Where(f => f.IsVisible).ToList();
        int kept = 0, recreated = 0;

        for (int i = 0; i < folders.Count; i++)
        {
            var f = folders[i];
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
            int defX = work.Right - tileW - (int)(24 * scale);
            int defY = work.Top + (int)(24 * scale) + i * (tileH + (int)(12 * scale));
            int targetX = saved?.X ?? (last.Width > 0 ? last.Left : defX);
            int targetY = saved?.Y ?? (last.Height > 0 ? last.Top : defY);
            CreateTile(f, targetX, targetY);
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
            tiles = _tiles.Values.Select(v =>
            {
                var r = v.Surface.ScreenRect;
                return new { id = v.Tile.Id, hwnd = (long)v.Surface.Hwnd, left = r.Left, top = r.Top, right = r.Right, bottom = r.Bottom, attached = _host.IsAttached(v.Surface) };
            }).ToArray(),
            utc = DateTime.UtcNow,
        };
        try { File.WriteAllText(Path.Combine(_dataDir, "state.json"), JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true })); }
        catch (IOException) { }
    }

    private static Color ParseColor(string hex, Color fallback)
    {
        try { return (Color)ColorConverter.ConvertFromString(hex); }
        catch { return fallback; }
    }

    public void Dispose()
    {
        if (_exiting) return;
        _exiting = true;

        DiagnosticService.Instance.LogInfo("Beginning clean ordered shutdown of GlassFolders...");
        _recoveryService.IsShuttingDown = true;
        _recoveryService.Dispose();

        _tray.Dispose();
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
