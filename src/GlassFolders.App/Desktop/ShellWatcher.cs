using System.Windows.Interop;
using GlassFolders.Native;
using static GlassFolders.Native.NativeMethods;

namespace GlassFolders.App.Desktop;

/// <summary>
/// Event-driven Explorer watch (no polling):
///  - "TaskbarCreated" broadcast: Explorer finished (re)creating the shell.
///  - WinEvent EVENT_OBJECT_DESTROY scoped to Explorer's process: the Progman window went away.
///  - WM_WTSSESSION_CHANGE: session lock / unlock detection.
///  - WM_POWERBROADCAST: system sleep / resume detection.
/// A hidden top-level window is used because broadcasts are not delivered to message-only windows.
/// </summary>
public sealed class ShellWatcher : IDisposable
{
    private readonly HwndSource _window;
    private readonly uint _taskbarCreated;
    private readonly WinEventProc _winEventProc; // keep delegate alive for the native hook
    private nint _hook;
    private nint _watchedProgman;
    private bool _sessionRegistered;

    public event Action? ShellLost;
    public event Action? ShellReady;
    public event Action? SystemResumed;

    /// <summary>Messages the hidden window receives that other services may care about (display/settings changes).</summary>
    public event Action<uint>? SystemMessage;

    public ShellWatcher()
    {
        _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
        _winEventProc = OnWinEvent;
        var p = new HwndSourceParameters("GlassFolders.ShellWatcher")
        {
            WindowStyle = unchecked((int)WS_POPUP),
            ExtendedWindowStyle = (int)WS_EX_TOOLWINDOW,
            Width = 0,
            Height = 0,
        };
        _window = new HwndSource(p);
        _window.AddHook(WndProc);

        try
        {
            _sessionRegistered = WTSRegisterSessionNotification(_window.Handle, NOTIFY_FOR_THIS_SESSION);
        }
        catch { }
    }

    public nint Handle => _window.Handle;

    /// <summary>Starts watching the given Progman for destruction. Call again after every reattach.</summary>
    public void Watch(nint progman)
    {
        if (_hook != 0) UnhookWinEvent(_hook);
        _watchedProgman = progman;
        uint pid = Win32.ProcessId(progman);
        _hook = pid == 0 ? 0 : SetWinEventHook(EVENT_OBJECT_DESTROY, EVENT_OBJECT_DESTROY, 0, _winEventProc, pid, 0, WINEVENT_OUTOFCONTEXT);
    }

    private void OnWinEvent(nint hook, uint evt, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        // OBJID_WINDOW == 0, CHILDID_SELF == 0
        if (evt == EVENT_OBJECT_DESTROY && idObject == 0 && idChild == 0 && hwnd == _watchedProgman)
        {
            _watchedProgman = 0;
            ShellLost?.Invoke();
        }
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        uint uMsg = (uint)msg;
        if (uMsg == _taskbarCreated)
        {
            ShellReady?.Invoke();
        }
        else if (uMsg == WM_WTSSESSION_CHANGE)
        {
            if ((uint)wParam == WTS_SESSION_UNLOCK)
            {
                SystemResumed?.Invoke();
            }
        }
        else if (uMsg == WM_POWERBROADCAST)
        {
            uint powerEvent = (uint)wParam;
            if (powerEvent is PBT_APMRESUMEAUTOMATIC or PBT_APMRESUMESUSPEND)
            {
                SystemResumed?.Invoke();
            }
        }
        else if (uMsg is WM_DISPLAYCHANGE or WM_SETTINGCHANGE or WM_DPICHANGED)
        {
            SystemMessage?.Invoke(uMsg);
        }

        return 0;
    }

    public void Dispose()
    {
        if (_sessionRegistered && _window.Handle != 0)
        {
            try { WTSUnRegisterSessionNotification(_window.Handle); } catch { }
            _sessionRegistered = false;
        }

        if (_hook != 0) UnhookWinEvent(_hook);
        _hook = 0;
        _window.Dispose();
    }
}
