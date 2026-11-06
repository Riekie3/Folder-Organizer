using System.Runtime.InteropServices;
using System.Windows.Threading;
using GlassFolders.Core.Layout;
using GlassFolders.Native;
using static GlassFolders.Native.NativeMethods;

namespace GlassFolders.App.Services;

/// <summary>
/// Service providing multi-monitor enumeration, work-area tracking,
/// and debounced reactions to display resolution/orientation changes.
/// </summary>
public sealed class MonitorService : IDisposable
{
    private readonly DispatcherTimer _debounceTimer;
    private List<MonitorDescriptor> _cachedMonitors = [];
    private bool _isDisposed;

    public IReadOnlyList<MonitorDescriptor> Monitors => _cachedMonitors;
    public MonitorDescriptor PrimaryMonitor => _cachedMonitors.FirstOrDefault(m => m.IsPrimary)
        ?? _cachedMonitors.FirstOrDefault()
        ?? new MonitorDescriptor { Key = "PRIMARY", Name = "Primary Display", Bounds = new RectInt(0, 0, 1920, 1080), WorkArea = new RectInt(0, 0, 1920, 1040), IsPrimary = true };

    public event Action<IReadOnlyList<MonitorDescriptor>>? MonitorsChanged;

    public MonitorService()
    {
        _debounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(300)
        };
        _debounceTimer.Tick += (_, _) =>
        {
            _debounceTimer.Stop();
            RefreshMonitors();
        };

        RefreshMonitors();
    }

    /// <summary>
    /// Notifies the service that a WM_DISPLAYCHANGE, WM_DPICHANGED, or WM_SETTINGCHANGE occurred.
    /// Debounces rapid successive notifications by 300ms.
    /// </summary>
    public void NotifyDisplayChanged()
    {
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    public void RefreshMonitors()
    {
        var list = new List<MonitorDescriptor>();

        EnumDisplayMonitors(0, 0, (nint hMon, nint hdc, ref RECT rc, nint data) =>
        {
            var ex = new MONITORINFOEXW { cbSize = Marshal.SizeOf<MONITORINFOEXW>() };
            if (GetMonitorInfoEx(hMon, ref ex))
            {
                string rawDevice = ex.szDevice ?? "";
                string key = rawDevice.StartsWith(@"\\.\") ? rawDevice[4..] : rawDevice;
                if (string.IsNullOrWhiteSpace(key)) key = $"MONITOR_{list.Count + 1}";

                bool isPrimary = (ex.dwFlags & MONITORINFOF_PRIMARY) != 0;
                string name = $"Display {list.Count + 1}" + (isPrimary ? " (Primary)" : "");

                list.Add(new MonitorDescriptor
                {
                    Key = key,
                    Name = name,
                    Bounds = new RectInt(ex.rcMonitor.Left, ex.rcMonitor.Top, ex.rcMonitor.Width, ex.rcMonitor.Height),
                    WorkArea = new RectInt(ex.rcWork.Left, ex.rcWork.Top, ex.rcWork.Width, ex.rcWork.Height),
                    IsPrimary = isPrimary,
                    DpiScale = GetDpiForSystem() / 96.0
                });
            }
            return true;
        }, 0);

        if (list.Count == 0)
        {
            // Fallback to primary work area if enum returned empty
            var work = Win32.PrimaryWorkArea();
            list.Add(new MonitorDescriptor
            {
                Key = "PRIMARY",
                Name = "Primary Display",
                Bounds = new RectInt(work.Left, work.Top, work.Width, work.Height),
                WorkArea = new RectInt(work.Left, work.Top, work.Width, work.Height),
                IsPrimary = true,
                DpiScale = GetDpiForSystem() / 96.0
            });
        }

        _cachedMonitors = list;
        DiagnosticService.Instance.LogInfo($"Monitors refreshed: {list.Count} active monitor(s) detected: [{string.Join(", ", list.Select(m => m.Key))}]");
        MonitorsChanged?.Invoke(_cachedMonitors);
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _debounceTimer.Stop();
    }
}
