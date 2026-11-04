using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using GlassFolders.App.Desktop;
using GlassFolders.Core.Poc;
using GlassFolders.Native;
using static GlassFolders.Native.NativeMethods;

namespace GlassFolders.App.Poc;

/// <summary>
/// Drives the Phase 1 proof of concept: three folder tiles on the desktop, click to open a popup,
/// drag to move, positions saved, and re-attachment after Explorer restarts.
/// Writes a state file and a log that tools/PocVerifier reads.
/// </summary>
public sealed class PocController : IDisposable
{
    private static readonly (string Id, string Label, Color A, Color B)[] Folders =
    [
        ("work", "Work", Color.FromRgb(0x7F, 0xB2, 0xFF), Color.FromRgb(0x5B, 0x5C, 0xF2)),
        ("games", "Games", Color.FromRgb(0xC3, 0x8B, 0xFF), Color.FromRgb(0x7B, 0x3F, 0xE8)),
        ("media", "Media", Color.FromRgb(0xFF, 0x9A, 0x9A), Color.FromRgb(0xE8, 0x45, 0x5A)),
    ];

    private readonly IDesktopHost _host;
    private readonly ShellWatcher _watcher;
    private readonly TileLayoutStore _store;
    private readonly TileLayout _layout;
    private readonly Dictionary<string, (FolderTile Tile, DesktopSurface Surface)> _tiles = [];
    private readonly string _dataDir;
    private readonly DispatcherTimer _retryTimer;
    private PocPopup? _popup;
    private string? _popupFor;
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
        if (!_host.RefreshDesktop())
        {
            Log("Desktop not ready at startup; waiting");
            ScheduleReattach();
            return;
        }
        CreateAllTiles();
        _watcher.Watch(_host.Desktop!.Progman);
        WriteState("READY");
    }

    private void CreateAllTiles()
    {
        var work = Win32.PrimaryWorkArea();
        double scale = GetDpiForSystem() / 96.0;
        int tileW = (int)(104 * scale), tileH = (int)(112 * scale);

        for (int i = 0; i < Folders.Length; i++)
        {
            var f = Folders[i];
            var saved = _layout.Find(f.Id);
            int x = saved?.X ?? work.Right - tileW - (int)(24 * scale);
            int y = saved?.Y ?? work.Top + (int)(24 * scale) + i * (tileH + (int)(12 * scale));
            CreateTile(f.Id, f.Label, f.A, f.B, x, y);
        }
    }

    private void CreateTile(string id, string label, Color a, Color b, int x, int y)
    {
        var tile = new FolderTile(id, label, a, b);
        var surface = _host.CreateSurface(id, tile, x, y);
        tile.GetScreenRect = () => surface.ScreenRect;
        tile.Clicked += OnTileClicked;
        tile.DragMoved += (t, nx, ny) => _host.Move(_tiles[t.Id].Surface, nx, ny);
        tile.DragEnded += (t, nx, ny) =>
        {
            _layout.Set(t.Id, nx, ny);
            _store.Save(_layout);
            Log($"MOVED {t.Id} {nx} {ny}");
            WriteState("MOVED");
        };
        tile.ContextMenu = BuildMenu();
        surface.Lost += s =>
        {
            if (_exiting) return;
            Log($"SURFACE-LOST {s.Id}");
            ScheduleReattach();
        };
        _tiles[id] = (tile, surface);
    }

    private ContextMenu BuildMenu()
    {
        var menu = new ContextMenu();
        var exit = new MenuItem { Header = "Exit GlassFolders PoC" };
        exit.Click += (_, _) => Application.Current.Shutdown();
        menu.Items.Add(exit);
        return menu;
    }

    private void OnTileClicked(FolderTile tile)
    {
        Log($"CLICK {tile.Id}");
        bool sameFolder = _popupFor == tile.Id && _popup is not null;
        _popup?.Close();
        if (sameFolder) return;

        var rect = _tiles[tile.Id].Surface.ScreenRect;
        var popup = new PocPopup(tile.Id switch { "work" => "Work", "games" => "Games", _ => "Media" }, rect);
        popup.Closed += (_, _) =>
        {
            tile.SetOpen(false);
            if (_popup == popup) { _popup = null; _popupFor = null; }
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

    /// <summary>Idempotent: keeps live surfaces (re-owning/re-parenting them), recreates dead ones, never duplicates.</summary>
    private void Reattach()
    {
        if (_exiting) return;
        _retryTimer.Stop();

        if (!_host.RefreshDesktop() || _host.Desktop!.ListView == 0)
        {
            if (DateTime.UtcNow > _retryDeadline)
            {
                Log("REATTACH-TIMEOUT desktop still unavailable; will retry on next TaskbarCreated");
                _retryDelayMs = 250;
                return;
            }
            _retryDelayMs = Math.Min(_retryDelayMs * 2, 8000);
            Log($"REATTACH-WAIT next in {_retryDelayMs} ms");
            ScheduleReattach();
            return;
        }

        int kept = 0, recreated = 0;
        foreach (var f in Folders)
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
            CreateTile(f.Id, f.Label, f.A, f.B, saved?.X ?? last.Left, saved?.Y ?? last.Top);
            recreated++;
        }

        if (_tiles.Count == 0) CreateAllTiles();
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
