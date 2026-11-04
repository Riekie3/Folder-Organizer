using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using GlassFolders.Native;
using static GlassFolders.Native.NativeMethods;

namespace GlassFolders.App.Poc;

/// <summary>
/// Minimal stand-in for the folder popup: proves a normal window can be opened from a desktop-layer
/// surface, placed next to it inside the work area, and closed by Esc / clicking outside / ✕.
/// The real Liquid Glass popup is built in Phases 2–3.
/// </summary>
public sealed class PocPopup : Window
{
    private readonly RECT _anchor;
    private bool _closing;

    public PocPopup(string title, RECT anchor)
    {
        _anchor = anchor;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        ShowActivated = true;
        Title = "GlassFolders – " + title;

        var close = new Button { Content = "✕", Width = 32, Height = 32, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = Brushes.White, FontSize = 14, Cursor = Cursors.Hand };
        close.Click += (_, _) => Close();
        var header = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);
        header.Children.Add(new TextBlock { Text = title, FontSize = 24, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center });

        Content = new Border
        {
            Width = 420,
            Padding = new Thickness(24, 16, 16, 24),
            CornerRadius = new CornerRadius(24),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0xA6, 0xFF, 0xFF, 0xFF)),
            Background = new LinearGradientBrush(Color.FromArgb(0xE6, 0x5D, 0x8C, 0xF5), Color.FromArgb(0xE6, 0x3A, 0x55, 0xD8), 45),
            Effect = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 8, Opacity = 0.35, Color = Color.FromRgb(0x10, 0x20, 0x70) },
            Margin = new Thickness(24),
            Child = new StackPanel
            {
                Children =
                {
                    header,
                    new TextBlock { Text = "Proof-of-concept popup. Press Esc, click outside, or ✕ to close.", Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, FontSize = 14, Opacity = 0.9 },
                },
            },
        };

        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Deactivated += (_, _) => { if (!_closing) Close(); };
        Closing += (_, _) => _closing = true;
        SourceInitialized += (_, _) => PositionNearAnchor();
        ContentRendered += (_, _) => { PositionNearAnchor(); Activate(); };
    }

    private void PositionNearAnchor()
    {
        nint hwnd = new WindowInteropHelper(this).Handle;
        var size = Win32.Rect(hwnd);
        var work = Win32.MonitorInfo(MonitorFromPoint(_anchor.Center, MONITOR_DEFAULTTONEAREST)).rcWork;
        const int gap = 8;

        // Prefer the right of the folder, else the left; then clamp inside the work area (never under the taskbar).
        int x = _anchor.Right + gap + size.Width <= work.Right ? _anchor.Right + gap : _anchor.Left - gap - size.Width;
        int y = _anchor.Top;
        x = Math.Clamp(x, work.Left, Math.Max(work.Left, work.Right - size.Width));
        y = Math.Clamp(y, work.Top, Math.Max(work.Top, work.Bottom - size.Height));
        SetWindowPos(hwnd, 0, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER);
    }
}
