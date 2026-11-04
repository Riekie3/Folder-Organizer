// DesktopProbe: dumps the Explorer desktop window hierarchy so we can see what this Windows build looks like.
// Usage: DesktopProbe [--spawn-workerw]
//   --spawn-workerw  sends Progman message 0x052C (creates the wallpaper WorkerW) before dumping.
using GlassFolders.Native;
using static GlassFolders.Native.NativeMethods;

bool spawn = args.Contains("--spawn-workerw");

Console.WriteLine($"OS: {Environment.OSVersion}  Build: {Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "DisplayVersion", "?")}");
Console.WriteLine($"Virtual screen: {Win32.VirtualScreen()}  Primary work area: {Win32.PrimaryWorkArea()}");
Console.WriteLine($"GetShellWindow: {Win32.Describe(GetShellWindow())}");
Console.WriteLine();

if (spawn)
{
    nint pm = FindWindow("Progman", null);
    SendMessageTimeout(pm, 0x052C, 0xD, 0x1, SMTO_NORMAL, 1000, out _);
    Console.WriteLine("Sent 0x052C to Progman.\n");
}

var d = DesktopWindows.Find();
Console.WriteLine($"Resolved: Progman=0x{d.Progman:X} DefViewHost=0x{d.DefViewHost:X} DefView=0x{d.DefView:X} ListView=0x{d.ListView:X} Tray=0x{d.Tray:X}");
Console.WriteLine();

Console.WriteLine("== Progman subtree ==");
Dump(d.Progman, 0);

Console.WriteLine("\n== Top-level WorkerW windows ==");
foreach (var w in Win32.TopLevelWindows().Where(w => Win32.ClassName(w) == "WorkerW"))
    Dump(w, 0);

Console.WriteLine("\n== Bottom of top-level z-order (last 8) ==");
var all = Win32.TopLevelWindows();
foreach (var w in all.Skip(Math.Max(0, all.Count - 8)))
    Console.WriteLine("  " + Win32.Describe(w));

Console.WriteLine("\n== Windows directly above Progman (up to 6 visible) ==");
int shown = 0;
for (nint w = GetWindow(d.Progman, GW_HWNDPREV); w != 0 && shown < 6; w = GetWindow(w, GW_HWNDPREV))
{
    if (!IsWindowVisible(w)) continue;
    Console.WriteLine("  " + Win32.Describe(w));
    shown++;
}

static void Dump(nint hwnd, int depth)
{
    if (hwnd == 0) return;
    Console.WriteLine(new string(' ', depth * 2) + Win32.Describe(hwnd));
    if (depth > 4) return;
    foreach (var c in Win32.Children(hwnd))
        Dump(c, depth + 1);
}
