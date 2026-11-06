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
/// Drives the core desktop organizer: manages folders on the desktop layer,
/// launches real applications/shortcuts, handles drag &amp; drop, and recovers after Explorer restarts.
/// </summary>
public sealed class PocController : IDisposable
{
    private readonly IDesktopHost _host;
    private readonly ShellWatcher _watcher;
    private readonly TileLayoutStore _store;
    private readonly TileLayout _layout;
    private readonly ConfigurationManager _configManager;
    private readonly FolderManager _folderManager;
    private readonly IconCacheService _iconCache;
    private readonly ShellShortcutResolver _resolver;
    private readonly Dictionary<string, (FolderTile Tile, DesktopSurface Surface)> _tiles = [];
    private readonly string _dataDir;
    private readonly DispatcherTimer _retryTimer;
    private FolderPopup? _popup;
    private string? _popupFor;
    private string? _lastClosedPopupId;
    private long _lastClosedPopupTimestamp;
    private int _reattachCount, _retryDelayMs = 250;
    private DateTime _retryDeadline;
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
        _watcher.ShellLost += () => { Log("SHELL-LOST"); ScheduleReattach(); };
        _watcher.ShellReady += () => { Log("SHELL-READY (TaskbarCreated)"); Reattach(); };
        _retryTimer = new DispatcherTimer(DispatcherPriority.Background);
        _retryTimer.Tick += (_, _) => { _retryTimer.Stop(); Reattach(); };
    }

    public void Start()
    {
        Log($"START host={_host.Name} pid={Environment.ProcessId}");
        FolderTile.Trace = Log;
        if (!_host.RefreshDesktop())
        {
            Log("Desktop not ready at startup; waiting");
            ScheduleReattach();
            return;
        }
        CreateAllTiles();
        _watcher.Watch(_host.Desktop!.Progman);
        WriteState("READY");
        Log("READY");
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
            Log($"MOVED {t.Id} {nx} {ny}");
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
            Log($"DROPPED-FILES {t.Id} count={files.Length}");
        };

        tile.ContextMenu = BuildMenu(folder, tile);
        surface.Lost += s =>
        {
            if (_exiting) return;
            Log($"SURFACE-LOST {s.Id}");
            ScheduleReattach();
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
        settings.Click += (_, _) =>
        {
            var win = new SettingsWindow(_configManager, _folderManager);
            win.FolderCreated += newFolder =>
            {
                var work = Win32.PrimaryWorkArea();
                double scale = GetDpiForSystem() / 96.0;
                int tileW = (int)(104 * scale), tileH = (int)(112 * scale);
                int count = _tiles.Count;
                int defX = work.Right - tileW - (int)(24 * scale);
                int defY = work.Top + (int)(24 * scale) + count * (tileH + (int)(12 * scale));
                CreateTile(newFolder, defX, defY);
            };
            win.ShowDialog();
        };
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

        var exit = new MenuItem { Header = "Exit GlassFolders PoC" };
        exit.Click += (_, _) => Application.Current.Shutdown();
        menu.Items.Add(exit);

        return menu;
    }

    private void OnTileClicked(FolderTile tile)
    {
        Log($"CLICK {tile.Id}");
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

        var rect = _tiles[tile.Id].Surface.ScreenRect;
        var popup = new FolderPopup(folder, _folderManager, _iconCache, _resolver, rect);
        popup.Closed += (_, _) =>
        {
            tile.SetOpen(false);
            tile.SetItemCount(folder.Items.Count);
            if (_popup == popup)
            {
                _popup = null;
                _popupFor = null;
                _lastClosedPopupId = tile.Id;
                _lastClosedPopupTimestamp = Stopwatch.GetTimestamp();
            }
            Log($"POPUP-CLOSED {tile.Id}");
        };
        _popup = popup;
        _popupFor = tile.Id;
        tile.SetOpen(true);
        popup.Show();
        Log($"POPUP-OPEN {tile.Id}");
        WriteState("POPUP");
    }

    private void ScheduleReattach()
    {
        if (_exiting) return;
        if (!_retryTimer.IsEnabled)
        {
            if (_retryDeadline < DateTime.UtcNow) _retryDeadline = DateTime.UtcNow.AddMinutes(2);
            _retryTimer.Interval = TimeSpan.FromMilliseconds(_retryDelayMs);
            _retryTimer.Start();
        }
    }

    private void Reattach()
    {
        if (_exiting) return;
        _retryTimer.Stop();

        if (!_host.RefreshDesktop() || _host.Desktop!.ListView == 0)
        {
            if (_retryDeadline != default && DateTime.UtcNow > _retryDeadline)
            {
                Log("REATTACH-TIMEOUT desktop still unavailable; will retry on next TaskbarCreated");
                _retryDelayMs = 250;
                _retryDeadline = default;
                return;
            }
            if (_retryDeadline == default)
                _retryDeadline = DateTime.UtcNow.AddMinutes(2);

            _retryDelayMs = Math.Min(_retryDelayMs * 2, 8000);
            Log($"REATTACH-WAIT next in {_retryDelayMs} ms");
            ScheduleReattach();
            return;
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

        _watcher.Watch(_host.Desktop.Progman);
        _reattachCount++;
        _retryDelayMs = 250;
        _retryDeadline = default;
        Log($"REATTACH #{_reattachCount} kept={kept} recreated={recreated} progman=0x{_host.Desktop.Progman:X}");
        WriteState("REATTACHED");
    }

    private void WriteState(string reason)
    {
        var state = new
        {
            reason,
            pid = Environment.ProcessId,
            host = _host.Name,
            progman = (long)(_host.Desktop?.Progman ?? 0),
            reattachCount = _reattachCount,
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

    private void Log(string message)
    {
        string line = $"{DateTime.Now:HH:mm:ss.fff} {message}";
        Debug.WriteLine(line);
        try { File.AppendAllText(Path.Combine(_dataDir, "poc.log"), line + Environment.NewLine); }
        catch (IOException) { }
    }

    private static Color ParseColor(string hex, Color fallback)
    {
        try { return (Color)ColorConverter.ConvertFromString(hex); }
        catch { return fallback; }
    }

    private static string? PromptRename(string currentName)
    {
        var win = new Window
        {
            Title = "Rename Folder",
            Width = 320,
            Height = 140,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            WindowStyle = WindowStyle.ToolWindow,
            ResizeMode = ResizeMode.NoResize,
            Background = new SolidColorBrush(Color.FromRgb(0x20, 0x24, 0x38))
        };

        var sp = new StackPanel { Margin = new Thickness(16) };
        var txt = new TextBox { Text = currentName, Margin = new Thickness(0, 0, 0, 16), FontSize = 14 };
        var btnOk = new Button { Content = "OK", Width = 80, Height = 28, IsDefault = true, HorizontalAlignment = HorizontalAlignment.Right };

        btnOk.Click += (_, _) => win.DialogResult = true;
        sp.Children.Add(txt);
        sp.Children.Add(btnOk);
        win.Content = sp;
        txt.SelectAll();
        txt.Focus();

        return win.ShowDialog() == true ? txt.Text : null;
    }

    public void Dispose()
    {
        _exiting = true;
        _retryTimer.Stop();
        _popup?.Close();
        foreach (var t in _tiles.Values) t.Surface.Dispose();
        _tiles.Clear();
        _watcher.Dispose();
        Log("EXIT");
    }
}
