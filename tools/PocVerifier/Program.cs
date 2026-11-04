// PocVerifier: automated checks for the Phase 1 desktop-integration proof of concept.
//
// Usage: PocVerifier <structural|interactive|explorer|all> [--host=owned|child-defview|child-progman] [--app=path]
//   structural   no input injection: attachment, desktop layer, taskbar, z-order vs a new window
//   interactive  moves the mouse and presses Win+D twice (restores your windows at the end):
//                show-desktop survival, click opens popup, Esc / outside-click close it, drag + persistence
//   explorer     kills and restarts Explorer twice; checks re-attachment and no duplicate windows
using System.Diagnostics;
using System.Text.Json;
using GlassFolders.Native;
using static GlassFolders.Native.NativeMethods;

string mode = args.FirstOrDefault(a => !a.StartsWith("--")) ?? "structural";
string host = Arg("host") ?? "owned";
string app = Arg("app") ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\..\src\GlassFolders.App\bin\Debug\net10.0-windows\GlassFolders.exe"));
string dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GlassFolders", "poc");
string statePath = Path.Combine(dataDir, "state.json"), logPath = Path.Combine(dataDir, "poc.log");

var results = new List<(string Name, bool Pass, string Detail)>();
void Check(string name, bool pass, string detail = "") { results.Add((name, pass, detail)); Console.WriteLine($"  [{(pass ? "PASS" : "FAIL")}] {name}{(detail.Length > 0 ? "  — " + detail : "")}"); }

Console.WriteLine($"PocVerifier mode={mode} host={host}\n  app={app}\n");
if (!File.Exists(app)) { Console.WriteLine("App not found; build src/GlassFolders.App first."); return 2; }

StopApp();
var st = StartApp(reset: true);
if (st is null) { Console.WriteLine("App did not report READY."); return 2; }
uint pid = (uint)st.Value.GetProperty("pid").GetInt32();

if (mode is "structural" or "all") Structural();
if (mode is "interactive" or "all") Interactive();
if (mode is "explorer" or "all") ExplorerRestart();

StopApp();
int failed = results.Count(r => !r.Pass);
Console.WriteLine($"\n{results.Count - failed}/{results.Count} passed (host={host})");
return failed == 0 ? 0 : 1;

// ───────────────────────── checks ─────────────────────────

void Structural()
{
    Console.WriteLine("Structural");
    var d = DesktopWindows.Find();
    var tiles = TileHandles(pid);
    Check("three tile windows exist", tiles.Count == 3, $"found {tiles.Count}");

    var trayRect = Win32.Rect(d.Tray);
    var vs = Win32.VirtualScreen();
    foreach (var h in tiles)
    {
        string id = Win32.Text(h).Replace("GlassFolders.Tile.", "");
        var r = Win32.Rect(h);
        Check($"{id}: visible", IsWindowVisible(h));
        Check($"{id}: on screen and clear of taskbar", r.Intersects(vs) && !r.Intersects(trayRect), $"{r} tray={trayRect}");

        if (host == "owned")
        {
            uint ex = Win32.ExStyle(h);
            Check($"{id}: owned by Progman", Win32.Owner(h) == d.Progman);
            Check($"{id}: tool window, never in taskbar/Alt-Tab", (ex & WS_EX_TOOLWINDOW) != 0 && (ex & WS_EX_APPWINDOW) == 0);
            Check($"{id}: non-activating", (ex & WS_EX_NOACTIVATE) != 0);
            Check($"{id}: not topmost", (ex & WS_EX_TOPMOST) == 0);
            var between = VisibleWindowsBetween(h, d.Progman, pid);
            Check($"{id}: sits directly above the desktop (no app window below it)", between is not null && between.Count == 0,
                between is null ? "Progman not found below tile — tile is BELOW the desktop" : string.Join("; ", between.Select(Win32.Describe)));
        }
        else
        {
            Check($"{id}: child of the desktop", (Win32.Style(h) & WS_CHILD) != 0 && GetAncestor(h, GA_ROOT) == d.Progman, $"root=0x{GetAncestor(h, GA_ROOT):X}");
        }
    }

    // A brand-new normal window placed over a tile must cover it.
    var target = Win32.Rect(tiles[0]);
    nint test = CreateWindowEx(0, "STATIC", "GlassFolders verifier test window", WS_OVERLAPPEDWINDOW | WS_VISIBLE,
        target.Left - 100, target.Top - 50, target.Width + 200, target.Height + 100, 0, 0, 0, 0);
    Pump(600);
    // Your own windows may already sit on top (e.g. a maximised browser), so check z-order rather than "who is on top".
    var order = Win32.TopLevelWindows();
    int testIdx = order.IndexOf(test), tileIdx = order.IndexOf(host == "owned" ? tiles[0] : d.Progman);
    var hit = GetAncestor(WindowFromPoint(target.Center), GA_ROOT);
    Check("new app window is above the folder in z-order", testIdx >= 0 && tileIdx > testIdx, $"test #{testIdx}, folder #{tileIdx}");
    Check("whatever is on top at the folder's centre is not the folder", Win32.ProcessId(hit) != pid, $"hit {Win32.Describe(hit)}");
    DestroyWindow(test);
    Pump(200);
}

void Interactive()
{
    Console.WriteLine("Interactive (moves the mouse, presses Win+D twice)");
    GetCursorPos(out var originalCursor);
    var d = DesktopWindows.Find();

    WinD();
    Pump(1200);
    if (Arg("shot") is { } shot) Screenshot(shot);
    var tiles = TileHandles(pid);
    foreach (var h in tiles)
    {
        var hit = WindowFromPoint(Win32.Rect(h).Center);
        Check($"{Win32.Text(h)}: visible and hittable after Show desktop (Win+D)", Win32.ProcessId(hit) == pid, $"hit {Win32.Describe(hit)}");
    }

    // Empty desktop next to the tiles must still be Explorer's icon view (icons stay clickable).
    var t0 = Win32.Rect(tiles[0]);
    var free = new POINT(t0.Left - 60, t0.Top + 10);
    var freeHit = WindowFromPoint(free);
    Check("desktop outside folders is still Explorer's icon view", freeHit == d.ListView, $"hit {Win32.Describe(freeHit)}");

    // Click opens popup.
    long logPos = LogLength();
    Click(Win32.Rect(TileById(pid, "work")).Center);
    Pump(1200);
    string log = LogSince(logPos);
    nint popup = FindWindow(null, "GlassFolders – Work");
    Check("click opens the folder popup", log.Contains("CLICK work") && log.Contains("POPUP-OPEN work") && popup != 0 && IsWindowVisible(popup), log.Trim().Replace(Environment.NewLine, " | "));
    Check("popup is in front and focused", popup != 0 && GetForegroundWindow() == popup, $"fg {Win32.Describe(GetForegroundWindow())}");
    if (popup != 0)
    {
        var pr = Win32.Rect(popup);
        var work = Win32.MonitorInfo(MonitorFromWindow(popup, MONITOR_DEFAULTTONEAREST)).rcWork;
        Check("popup inside the work area", pr.Left >= work.Left && pr.Top >= work.Top && pr.Right <= work.Right && pr.Bottom <= work.Bottom, $"{pr} work={work}");
    }

    // Esc closes it.
    logPos = LogLength();
    Key(0x1B);
    Pump(800);
    Check("Esc closes the popup", LogSince(logPos).Contains("POPUP-CLOSED work") && FindWindow(null, "GlassFolders – Work") == 0);

    // Outside click closes it: pick a spot on the bare desktop that neither the popup nor any folder covers.
    Click(Win32.Rect(TileById(pid, "work")).Center);
    Pump(1000);
    var outside = BareDesktopPoint(pid, d.ListView);
    logPos = LogLength();
    Click(outside);
    Pump(800);
    Check("clicking outside closes the popup", LogSince(logPos).Contains("POPUP-CLOSED work") && FindWindow(null, "GlassFolders – Work") == 0);

    // Drag moves the folder and saves it.
    nint games = TileById(pid, "games");
    var before = Win32.Rect(games);
    var to = new POINT(before.Center.X - 300, before.Center.Y + 40);
    Drag(before.Center, to);
    Pump(800);
    var after = Win32.Rect(games);
    Check("drag moves the folder", Math.Abs(after.Left - (before.Left - 300)) <= 3 && Math.Abs(after.Top - (before.Top + 40)) <= 3, $"{before} → {after}");
    Check("drag did not open a popup", FindWindow(null, "GlassFolders – Games") == 0);

    WinD();
    Pump(800);
    SetCursorPos(originalCursor.X, originalCursor.Y);

    // Persistence across restart.
    StopApp();
    var st2 = StartApp(reset: false);
    pid = (uint)st2!.Value.GetProperty("pid").GetInt32();
    var again = Win32.Rect(TileById(pid, "games"));
    Check("moved position restored after app restart", again.Left == after.Left && again.Top == after.Top, $"{after} vs {again}");
}

void ExplorerRestart()
{
    Console.WriteLine("Explorer restart (twice)");
    for (int round = 1; round <= 2; round++)
    {
        int before = ReadState()?.GetProperty("reattachCount").GetInt32() ?? 0;
        var sw = Stopwatch.StartNew();
        foreach (var p in Process.GetProcessesByName("explorer")) { try { p.Kill(); } catch { } }
        Pump(3000);
        if (Process.GetProcessesByName("explorer").Length == 0)
            Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });

        bool ok = false;
        JsonElement? s = null;
        for (int i = 0; i < 60 && !ok; i++)
        {
            Pump(500);
            s = ReadState();
            ok = s is not null && s.Value.GetProperty("reattachCount").GetInt32() > before
                 && s.Value.GetProperty("tiles").EnumerateArray().All(t => t.GetProperty("attached").GetBoolean());
        }
        Pump(1500); // let any duplicate events land
        var d = DesktopWindows.Find();
        var tiles = TileHandles(pid);
        Check($"round {round}: folders re-attached after Explorer restart", ok, $"{sw.Elapsed.TotalSeconds:F1}s, reattachCount={s?.GetProperty("reattachCount").GetInt32()}");
        Check($"round {round}: still exactly three folder windows (no duplicates)", tiles.Count == 3, $"found {tiles.Count}");
        Check($"round {round}: attached to the NEW desktop", tiles.All(h => host == "owned" ? Win32.Owner(h) == d.Progman : GetAncestor(h, GA_ROOT) == d.Progman));
        Check($"round {round}: app process still running", Process.GetProcesses().Any(p => p.Id == pid));
    }
}

// ───────────────────────── helpers ─────────────────────────

string? Arg(string name) => args.FirstOrDefault(a => a.StartsWith($"--{name}="))?[(name.Length + 3)..];

void StopApp()
{
    using (var ev = EventWaitHandle.TryOpenExisting(@"Local\GlassFolders.Poc.Exit", out var h) ? h : null) ev?.Set();
    for (int i = 0; i < 40 && Process.GetProcessesByName("GlassFolders").Length > 0; i++) Pump(100);
    foreach (var p in Process.GetProcessesByName("GlassFolders")) { try { p.Kill(); } catch { } }
}

JsonElement? StartApp(bool reset)
{
    try { File.Delete(statePath); } catch { }
    Process.Start(new ProcessStartInfo(app, $"--host={host}{(reset ? " --reset" : "")}") { UseShellExecute = false });
    for (int i = 0; i < 100; i++)
    {
        Pump(100);
        var s = ReadState();
        if (s is not null && s.Value.GetProperty("reason").GetString() == "READY") return s;
    }
    return null;
}

JsonElement? ReadState()
{
    try { return JsonDocument.Parse(File.ReadAllText(statePath)).RootElement.Clone(); }
    catch { return null; }
}

long LogLength() => File.Exists(logPath) ? new FileInfo(logPath).Length : 0;

string LogSince(long pos)
{
    using var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
    fs.Seek(pos, SeekOrigin.Begin);
    return new StreamReader(fs).ReadToEnd();
}

static List<nint> TileHandles(uint pid)
{
    var found = new List<nint>();
    EnumWindows((h, _) =>
    {
        if (Win32.ProcessId(h) == pid && Win32.Text(h).StartsWith("GlassFolders.Tile.")) found.Add(h);
        return true;
    }, 0);
    var d = DesktopWindows.Find();
    foreach (var parent in new[] { d.Progman, d.DefView })
        if (parent != 0)
            foreach (var c in Win32.Children(parent))
                if (Win32.ProcessId(c) == pid && Win32.Text(c).StartsWith("GlassFolders.Tile.")) found.Add(c);
    return found;
}

// A point where Explorer's icon view is what the user would click (scans the primary work area).
static POINT BareDesktopPoint(uint pid, nint listView)
{
    var work = Win32.PrimaryWorkArea();
    for (int fy = 5; fy >= 1; fy--)
        for (int fx = 4; fx <= 6; fx++)
        {
            var p = new POINT(work.Left + work.Width * fx / 10, work.Top + work.Height * fy / 6);
            if (WindowFromPoint(p) == listView) return p;
        }
    return new POINT(work.Left + work.Width / 2, work.Bottom - 40);
}

static nint TileById(uint pid, string id) => TileHandles(pid).First(h => Win32.Text(h) == "GlassFolders.Tile." + id);

// Returns visible foreign windows between tile and Progman in z-order, or null if Progman isn't below the tile.
static List<nint>? VisibleWindowsBetween(nint tile, nint progman, uint pid)
{
    var list = new List<nint>();
    for (nint w = GetWindow(tile, GW_HWNDNEXT); w != 0; w = GetWindow(w, GW_HWNDNEXT))
    {
        if (w == progman) return list;
        if (!IsWindowVisible(w) || Win32.ProcessId(w) == pid) continue;
        var r = Win32.Rect(w);
        if (r.Width <= 1 || r.Height <= 1) continue; // zero-size helper windows
        if (Win32.ClassName(w) is "EdgeUiInputTopWndClass" or "EdgeUiInputWndClass") continue; // 3-px shell edge strips
        list.Add(w);
    }
    return null;
}

// Full-screen capture through Windows PowerShell (System.Drawing), made DPI-aware so it grabs real pixels.
static void Screenshot(string path)
{
    path = Path.GetFullPath(path);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    string ps = "Add-Type -Name D -Namespace W -MemberDefinition '[DllImport(\"user32.dll\")] public static extern bool SetProcessDPIAware();';" +
                "[W.D]::SetProcessDPIAware() | Out-Null; Add-Type -AssemblyName System.Windows.Forms,System.Drawing;" +
                "$b=[System.Windows.Forms.SystemInformation]::VirtualScreen; $bmp=New-Object System.Drawing.Bitmap $b.Width,$b.Height;" +
                "$g=[System.Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen($b.Left,$b.Top,0,0,$b.Size);" +
                $"$bmp.Save('{path}',[System.Drawing.Imaging.ImageFormat]::Png)";
    Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -Command \"{ps.Replace("\"", "\\\"")}\"") { UseShellExecute = false, CreateNoWindow = true })!.WaitForExit(15000);
}

static void Pump(int ms)
{
    var until = Environment.TickCount64 + ms;
    while (Environment.TickCount64 < until)
    {
        while (PeekMessage(out var m, 0, 0, 0, PM_REMOVE)) { TranslateMessage(m); DispatchMessage(m); }
        Thread.Sleep(15);
    }
}

static unsafe void Send(params INPUT[] inputs)
{
    fixed (INPUT* p = inputs) SendInput((uint)inputs.Length, p, sizeof(INPUT));
}

static INPUT KeyInput(ushort vk, bool up) => new() { type = INPUT_KEYBOARD, u = new INPUTUNION { ki = new KEYBDINPUT { wVk = vk, dwFlags = up ? KEYEVENTF_KEYUP : 0 } } };

static void Key(ushort vk) => Send(KeyInput(vk, false), KeyInput(vk, true));

static void WinD() => Send(KeyInput(VK_LWIN, false), KeyInput(0x44, false), KeyInput(0x44, true), KeyInput(VK_LWIN, true));

static INPUT MouseAt(POINT p, uint flags)
{
    var vs = Win32.VirtualScreen();
    return new INPUT
    {
        type = INPUT_MOUSE,
        u = new INPUTUNION { mi = new MOUSEINPUT {
            dx = (int)((p.X - vs.Left) * 65535L / Math.Max(1, vs.Width - 1)),
            dy = (int)((p.Y - vs.Top) * 65535L / Math.Max(1, vs.Height - 1)),
            dwFlags = flags | MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK } },
    };
}

static void Click(POINT p)
{
    Send(MouseAt(p, 0)); Pump(80);
    Send(MouseAt(p, MOUSEEVENTF_LEFTDOWN)); Pump(60);
    Send(MouseAt(p, MOUSEEVENTF_LEFTUP));
}

static void Drag(POINT from, POINT to)
{
    Send(MouseAt(from, 0)); Pump(80);
    Send(MouseAt(from, MOUSEEVENTF_LEFTDOWN)); Pump(80);
    for (int i = 1; i <= 12; i++)
    {
        Send(MouseAt(new POINT(from.X + (to.X - from.X) * i / 12, from.Y + (to.Y - from.Y) * i / 12), 0));
        Pump(25);
    }
    Pump(100);
    Send(MouseAt(to, MOUSEEVENTF_LEFTUP));
}
