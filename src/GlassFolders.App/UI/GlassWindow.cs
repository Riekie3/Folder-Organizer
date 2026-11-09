using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using GlassFolders.App.Services;
using GlassFolders.Native;
using static GlassFolders.Native.NativeMethods;

namespace GlassFolders.App.UI;

/// <summary>
/// Base for the windows that sit on a <see cref="GlassCard"/>: Settings, the folder editor and message boxes.
/// Before it first appears it centres itself on the owner's monitor (or the one holding the pointer) and
/// captures that whole monitor, so the card can be dragged anywhere on it while the glass stays put.
/// It follows the Appearance settings and theme while open.
/// </summary>
public class GlassWindow : Window
{
    /// <summary>
    /// Stops windows from placing themselves and capturing the screen. For the screenshot tools, which show
    /// windows far off screen, give them a picture to sit on and render them to a file.
    /// </summary>
    public static bool SkipAutoPlacementAndCapture { get; set; }

    private GlassCard? _card;
    private bool _chrome = true;

    public GlassWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;

        SourceInitialized += (_, _) => PrepareGlass();
        Closed += (_, _) =>
        {
            AppearanceService.Instance.Changed -= OnAppearanceChanged;
            _card?.SetBackdrop(null);
        };
    }

    /// <summary>Connects the card this window draws on. Call from the constructor once the card exists.</summary>
    protected void UseGlass(GlassCard card, bool chromeRadius = true)
    {
        _card = card;
        _chrome = chromeRadius;
        card.ApplyAppearance(AppearanceService.Instance.Glass, AppearanceService.Instance.IsLight, _chrome);
        AppearanceService.Instance.Changed += OnAppearanceChanged;
    }

    /// <summary>Called after the theme or glass settings changed and the card was updated.</summary>
    protected virtual void OnAppearanceApplied() { }

    private void OnAppearanceChanged()
    {
        if (_card is null) return;

        var appearance = AppearanceService.Instance.Glass;
        if (_card.Backdrop is { } current && current.BlurRadius != appearance.BlurRadius)
        {
            // Window can't capture itself while open, so the unblurred copy kept at open is filtered again.
            var blurred = current.WithBlur(appearance.BlurRadius);
            if (blurred is not null) _card.SetBackdrop(blurred);
        }
        _card.ApplyAppearance(appearance, AppearanceService.Instance.IsLight, _chrome);
        OnAppearanceApplied();
    }

    private void PrepareGlass()
    {
        if (SkipAutoPlacementAndCapture) return;
        nint hwnd = new WindowInteropHelper(this).EnsureHandle();
        nint monitor = TargetMonitor();
        double scale = ScreenCapture.MonitorScale(monitor);

        // The window's own size is known, so it can be centred before it shows. Otherwise Windows picks the spot
        // after the snapshot was taken and the glass would be taken from the wrong place.
        Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double width = double.IsNaN(Width) ? DesiredSize.Width : Width;
        double height = double.IsNaN(Height) ? DesiredSize.Height : Height;
        var work = Win32.MonitorInfo(monitor).rcWork;
        int x = work.Left + (int)Math.Round((work.Width - width * scale) / 2);
        int y = work.Top + (int)Math.Round((work.Height - height * scale) / 2);
        SetWindowPos(hwnd, 0, Math.Max(work.Left, x), Math.Max(work.Top, y), 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);

        if (_card is null || !BackdropSnapshot.IsGlassAllowed()) return;

        var screen = Win32.MonitorInfo(monitor).rcMonitor;
        var appearance = AppearanceService.Instance.Glass;
        var snapshot = BackdropSnapshot.Capture(screen, scale, appearance.BlurRadius, allowReblur: true);
        if (snapshot is not null)
            DiagnosticService.Instance.LogDebug($"Window backdrop snapshot took {snapshot.ElapsedMilliseconds:F1} ms ({Title})");
        _card.SetBackdrop(snapshot);
        _card.ApplyAppearance(appearance, AppearanceService.Instance.IsLight, _chrome);
    }

    private nint TargetMonitor()
    {
        if (Owner is not null)
        {
            nint owner = new WindowInteropHelper(Owner).Handle;
            if (owner != 0) return MonitorFromWindow(owner, MONITOR_DEFAULTTONEAREST);
        }
        return GetCursorPos(out POINT cursor)
            ? MonitorFromPoint(cursor, MONITOR_DEFAULTTONEAREST)
            : MonitorFromPoint(new POINT(0, 0), MONITOR_DEFAULTTOPRIMARY);
    }
}
