using static GlassFolders.Native.NativeMethods;

namespace GlassFolders.Native;

/// <summary>
/// Snapshot of the Explorer desktop window handles.
/// Win11 24H2+: Progman → SHELLDLL_DefView → SysListView32, with the wallpaper WorkerW as a child of Progman.
/// Older builds: SHELLDLL_DefView may live inside a top-level WorkerW instead of Progman.
/// This is undocumented shell structure; everything that depends on it goes through this class.
/// </summary>
public sealed record DesktopWindows(nint Progman, nint DefView, nint ListView, nint DefViewHost, nint Tray)
{
    public bool IsValid => Progman != 0 && DefView != 0 && IsWindow(Progman) && IsWindow(DefView);

    public static DesktopWindows Find()
    {
        nint progman = FindWindow("Progman", null);
        nint defView = progman != 0 ? FindWindowEx(progman, 0, "SHELLDLL_DefView", null) : 0;
        nint host = defView != 0 ? progman : 0;

        if (defView == 0)
        {
            // Pre-24H2 layout (or after "0x052C" spawn on old builds): DefView inside a top-level WorkerW.
            foreach (var w in Win32.TopLevelWindows())
            {
                if (Win32.ClassName(w) != "WorkerW") continue;
                nint dv = FindWindowEx(w, 0, "SHELLDLL_DefView", null);
                if (dv != 0) { defView = dv; host = w; break; }
            }
        }

        nint listView = defView != 0 ? FindWindowEx(defView, 0, "SysListView32", null) : 0;
        nint tray = FindWindow("Shell_TrayWnd", null);
        return new DesktopWindows(progman, defView, listView, host, tray);
    }

    /// <summary>Waits (with message pumping supplied by caller if needed) until Explorer's desktop exists.</summary>
    public static DesktopWindows? WaitForDesktop(TimeSpan timeout, Action? pump = null)
    {
        var until = DateTime.UtcNow + timeout;
        int delay = 100;
        while (DateTime.UtcNow < until)
        {
            var d = Find();
            if (d.IsValid && d.ListView != 0) return d;
            pump?.Invoke();
            Thread.Sleep(delay);
            delay = Math.Min(delay * 2, 1000);
        }
        return null;
    }
}
