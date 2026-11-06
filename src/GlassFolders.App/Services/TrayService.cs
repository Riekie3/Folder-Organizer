using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
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

    private static unsafe nint GenerateTrayIconHandle(bool paused)
    {
        const int w = 16, h = 16;
        uint[] pixels = new uint[w * h];

        // Primary and secondary colors for the glossy glass folder
        uint plateColor = paused ? 0xFF888888 : 0xFF3B6FE0;
        uint flapColor = paused ? 0xCCAAAAAA : 0xCCE0ECFF;
        uint rimColor = 0xFFFFFFFF;
        uint pauseBadgeColor = 0xFFFF9900;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                uint c = 0; // transparent

                // Tab at top left
                if (y >= 2 && y <= 4 && x >= 1 && x <= 6)
                {
                    c = plateColor;
                }
                // Back plate
                else if (y >= 4 && y <= 13 && x >= 1 && x <= 14)
                {
                    c = plateColor;
                }

                // Translucent front flap
                if (y >= 7 && y <= 13 && x >= 1 && x <= 14)
                {
                    c = y == 7 ? rimColor : flapColor;
                }

                // If paused, add an amber pause indicator in the bottom-right corner
                if (paused && y >= 9 && y <= 14 && x >= 10 && x <= 14)
                {
                    if (x == 11 || x == 13)
                        c = pauseBadgeColor;
                }

                pixels[y * w + x] = c;
            }
        }

        fixed (uint* ptr = pixels)
        {
            nint hbmColor = CreateBitmap(w, h, 1, 32, (nint)ptr);
            nint hbmMask = CreateBitmap(w, h, 1, 1, nint.Zero);

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

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        RemoveIcon();
        _msgWindow.Dispose();
    }
}
