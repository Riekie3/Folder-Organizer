using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GlassFolders.Native;
using static GlassFolders.Native.NativeMethods;

namespace GlassFolders.App.Services;

/// <summary>
/// Manages the system notification area (system tray) icon and context menu.
/// Pure Win32 Shell_NotifyIcon implementation; no taskbar icon, zero memory leaks.
/// </summary>
public sealed class TrayService : IDisposable
{
    private const uint WM_TRAY = WM_USER + 100;
    private const uint TrayIconId = 1;

    private readonly HwndSource _msgWindow;
    private nint _hIcon;
    private bool _isPaused;
    private bool _isDisposed;

    public event Action? NewFolderRequested;
    public event Action? ToggleHideAllRequested;
    public event Action? TogglePauseRequested;
    public event Action? SettingsRequested;
    public event Action? RecoverNowRequested;
    public event Action? AboutRequested;
    public event Action? ExitRequested;

    public bool IsPaused
    {
        get => _isPaused;
        set
        {
            if (_isPaused != value)
            {
                _isPaused = value;
                UpdateIcon();
            }
        }
    }

    public bool AreFoldersHidden { get; set; }

    public TrayService()
    {
        var p = new HwndSourceParameters("GlassFolders.TrayHost")
        {
            WindowStyle = unchecked((int)WS_POPUP),
            ExtendedWindowStyle = (int)WS_EX_TOOLWINDOW,
            Width = 0,
            Height = 0,
        };
        _msgWindow = new HwndSource(p);
        _msgWindow.AddHook(WndProc);

        CreateAndAddIcon();
    }

    private void CreateAndAddIcon()
    {
        _hIcon = GenerateTrayIconHandle(_isPaused);

        var nid = new NOTIFYICONDATAW
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATAW>(),
            hWnd = _msgWindow.Handle,
            uID = TrayIconId,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_TRAY,
            hIcon = _hIcon,
            szTip = _isPaused ? "GlassFolders (Paused)" : "GlassFolders"
        };

        Shell_NotifyIcon(NIM_ADD, ref nid);
    }

    public void RecreateIcon()
    {
        RemoveIcon();
        CreateAndAddIcon();
    }

    private void UpdateIcon()
    {
        if (_hIcon != 0)
        {
            DestroyIcon(_hIcon);
            _hIcon = 0;
        }

        _hIcon = GenerateTrayIconHandle(_isPaused);

        var nid = new NOTIFYICONDATAW
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATAW>(),
            hWnd = _msgWindow.Handle,
            uID = TrayIconId,
            uFlags = NIF_ICON | NIF_TIP,
            hIcon = _hIcon,
            szTip = _isPaused ? "GlassFolders (Paused)" : "GlassFolders"
        };

        Shell_NotifyIcon(NIM_MODIFY, ref nid);
    }

    private void RemoveIcon()
    {
        var nid = new NOTIFYICONDATAW
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATAW>(),
            hWnd = _msgWindow.Handle,
            uID = TrayIconId
        };
        Shell_NotifyIcon(NIM_DELETE, ref nid);

        if (_hIcon != 0)
        {
            DestroyIcon(_hIcon);
            _hIcon = 0;
        }
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if ((uint)msg == WM_TRAY)
        {
            uint mouseMsg = (uint)lParam;
            if (mouseMsg == WM_RBUTTONUP)
            {
                ShowContextMenu();
                handled = true;
            }
            else if (mouseMsg == WM_LBUTTONUP || mouseMsg == WM_LBUTTONDBLCLK)
            {
                SettingsRequested?.Invoke();
                handled = true;
            }
        }
        return 0;
    }

    private void ShowContextMenu()
    {
        SetForegroundWindow(_msgWindow.Handle);

        var menu = new ContextMenu();
        menu.Style = Application.Current.TryFindResource("GlassContextMenuStyle") as Style;

        // 1. New Folder...
        var itemNewFolder = new MenuItem { Header = "New Folder..." };
        itemNewFolder.Click += (_, _) => NewFolderRequested?.Invoke();
        menu.Items.Add(itemNewFolder);

        // 2. Show / Hide Desktop Folders
        var itemHideAll = new MenuItem
        {
            Header = AreFoldersHidden ? "Show Desktop Folders" : "Hide Desktop Folders",
            IsChecked = AreFoldersHidden
        };
        itemHideAll.Click += (_, _) =>
        {
            AreFoldersHidden = !AreFoldersHidden;
            ToggleHideAllRequested?.Invoke();
        };
        menu.Items.Add(itemHideAll);

        // 3. Pause GlassFolders
        var itemPause = new MenuItem
        {
            Header = _isPaused ? "Resume GlassFolders" : "Pause GlassFolders",
            IsChecked = _isPaused
        };
        itemPause.Click += (_, _) =>
        {
            IsPaused = !IsPaused;
            TogglePauseRequested?.Invoke();
        };
        menu.Items.Add(itemPause);

        menu.Items.Add(new Separator());

        // 4. Settings...
        var itemSettings = new MenuItem { Header = "Settings..." };
        itemSettings.Click += (_, _) => SettingsRequested?.Invoke();
        menu.Items.Add(itemSettings);

        // 5. Explorer Recovery
        var itemRecovery = new MenuItem { Header = "Explorer Recovery" };
        var itemRecoverNow = new MenuItem { Header = "Recover Now (Reattach)" };
        itemRecoverNow.Click += (_, _) => RecoverNowRequested?.Invoke();
        itemRecovery.Items.Add(itemRecoverNow);
        menu.Items.Add(itemRecovery);

        menu.Items.Add(new Separator());

        // 6. About
        var itemAbout = new MenuItem { Header = "About GlassFolders" };
        itemAbout.Click += (_, _) => AboutRequested?.Invoke();
        menu.Items.Add(itemAbout);

        // 7. Exit
        var itemExit = new MenuItem { Header = "Exit GlassFolders" };
        itemExit.Click += (_, _) => ExitRequested?.Invoke();
        menu.Items.Add(itemExit);

        menu.IsOpen = true;
    }

    private const int SM_CXSMICON = 49;

    /// <summary>
    /// Makes the tray icon from the app icon's own small frame, at the size the tray uses at this display
    /// scale (16 px at 100 %, 20 at 125 %, 24 at 150 %). Paused shows it in grey.
    /// </summary>
    private static unsafe nint GenerateTrayIconHandle(bool paused)
    {
        int size = Math.Max(16, GetSystemMetricsForDpi(SM_CXSMICON, GetDpiForSystem()));
        byte[] pixels = LoadIconFrame(size);

        if (paused)
        {
            for (int i = 0; i < pixels.Length; i += 4)
            {
                byte grey = (byte)Math.Round(0.114 * pixels[i] + 0.587 * pixels[i + 1] + 0.299 * pixels[i + 2]);
                pixels[i] = pixels[i + 1] = pixels[i + 2] = grey;
                pixels[i + 3] = (byte)(pixels[i + 3] * 3 / 4);
            }
        }

        fixed (byte* ptr = pixels)
        {
            nint hbmColor = CreateBitmap(size, size, 1, 32, (nint)ptr);
            nint hbmMask = CreateBitmap(size, size, 1, 1, nint.Zero);

            var info = new ICONINFO
            {
                fIcon = 1,
                xHotspot = 0,
                yHotspot = 0,
                hbmMask = hbmMask,
                hbmColor = hbmColor
            };

            nint hIcon = CreateIconIndirect(ref info);

            DeleteObject(hbmColor);
            DeleteObject(hbmMask);

            return hIcon;
        }
    }

    /// <summary>The app icon frame closest to <paramref name="size"/>, scaled to it if needed, as straight BGRA.</summary>
    private static byte[] LoadIconFrame(int size)
    {
        var info = Application.GetResourceStream(new Uri("pack://application:,,,/GlassFolders;component/assets/app.ico"));
        using var stream = info.Stream;
        var decoder = new IconBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        BitmapSource frame = decoder.Frames.OrderBy(f => f.PixelWidth).FirstOrDefault(f => f.PixelWidth >= size)
                             ?? decoder.Frames.OrderBy(f => f.PixelWidth).Last();
        if (frame.PixelWidth != size)
            frame = new TransformedBitmap(frame, new ScaleTransform(size / (double)frame.PixelWidth, size / (double)frame.PixelHeight));

        var bgra = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[size * size * 4];
        bgra.CopyPixels(new Int32Rect(0, 0, size, size), pixels, size * 4, 0);
        return pixels;
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int index, uint dpi);

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        RemoveIcon();
        _msgWindow.Dispose();
    }
}
