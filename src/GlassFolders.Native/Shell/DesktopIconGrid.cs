using System.Runtime.InteropServices;
using GlassFolders.Core.Layout;

namespace GlassFolders.Native.Shell;

/// <summary>
/// What the desktop's own icon view reports, in screen pixels: the grid cell size and the cell each icon fills.
/// </summary>
/// <param name="AutoArrange">Windows re-flows icons by itself when this is on.</param>
/// <param name="IconsHidden">"Show desktop icons" is off. The icons still own their cells.</param>
public sealed record DesktopIconSnapshot(int CellWidth, int CellHeight, IReadOnlyList<RectInt> Cells, bool AutoArrange, bool IconsHidden);

/// <summary>
/// Reads the desktop icon grid through the shell's documented <c>IFolderView</c>: the same view Explorer draws.
/// Read-only: it never moves, selects or changes an icon.
/// </summary>
public static class DesktopIconGrid
{
    private const int SWC_DESKTOP = 8;
    private const int SWFO_NEEDDISPATCH = 1;
    private const uint SVGIO_ALLVIEW = 2;
    private const uint FWF_NOICONS = 0x1000;
    private const int DefaultIconSize = 48;

    private static readonly Guid CLSID_ShellWindows = new("9BA05972-F6A8-11CF-A442-00A0C90A8F39");
    private static readonly Guid SID_STopLevelBrowser = new("4C96BE40-915C-11CF-99D3-00AA004AE837");

    /// <summary>Returns the icon grid, or null when Explorer's desktop isn't available (restarting, or another shell).</summary>
    public static DesktopIconSnapshot? TryRead() => TryRead(out _);

    /// <summary>As <see cref="TryRead()"/>, and says which step failed, for the log.</summary>
    public static DesktopIconSnapshot? TryRead(out string failure)
    {
        failure = "";
        object? windows = null, dispatch = null, browser = null, view = null;
        nint enumPtr = 0;
        try
        {
            Type? type = Type.GetTypeFromCLSID(CLSID_ShellWindows, throwOnError: false);
            if (type is null) { failure = "no ShellWindows class"; return null; }
            windows = Activator.CreateInstance(type);
            if (windows is not IShellWindows shellWindows) { failure = "no IShellWindows"; return null; }

            object loc = 0; // CSIDL_DESKTOP
            object? root = null;
            dispatch = shellWindows.FindWindowSW(ref loc, ref root, SWC_DESKTOP, out _, SWFO_NEEDDISPATCH);
            if (dispatch is not IServiceProvider provider) { failure = "desktop window not found"; return null; }

            Guid sid = SID_STopLevelBrowser, iid = typeof(IShellBrowser).GUID;
            if (provider.QueryService(ref sid, ref iid, out browser) != 0 || browser is not IShellBrowser shellBrowser) { failure = "no shell browser"; return null; }
            if (shellBrowser.QueryActiveShellView(out view) != 0 || view is not IFolderView folderView) { failure = "no folder view"; return null; }

            POINT spacing = default;
            if (folderView.GetSpacing(ref spacing) != 0 || spacing.X <= 0 || spacing.Y <= 0) { failure = "no spacing"; return null; }
            bool autoArrange = folderView.GetAutoArrange() == 0; // S_OK means on, S_FALSE off
            int iconSize = DefaultIconSize;
            uint flags = 0;
            if (view is IFolderView2 view2)
            {
                if (view2.GetViewModeAndIconSize(out _, out int size) == 0 && size > 0) iconSize = size;
                view2.GetCurrentFolderFlags(out flags);
            }

            // Positions are client coordinates of the icon list inside the desktop view.
            var desktop = DesktopWindows.Find();
            nint list = desktop.ListView != 0 ? desktop.ListView : desktop.DefView;
            if (list == 0) { failure = "no desktop list view"; return null; }
            var origin = new POINT(0, 0);
            ClientToScreen(list, ref origin);

            // An item's position is its icon's top-left. The icon is centred in its cell, and its size is in
            // DIPs, so convert it with the desktop's DPI to find the cell's left edge.
            uint dpi = GetDpiForWindow(list);
            int iconPx = (int)Math.Round(iconSize * (dpi == 0 ? 96 : dpi) / 96.0);
            int cellInset = Math.Max(0, (spacing.X - iconPx) / 2);

            var icons = new List<RectInt>();
            Guid enumIid = typeof(IEnumIDList).GUID;
            if (folderView.Items(SVGIO_ALLVIEW, ref enumIid, out enumPtr) == 0 && enumPtr != 0)
            {
                var items = (IEnumIDList)Marshal.GetObjectForIUnknown(enumPtr);
                try
                {
                    while (items.Next(1, out nint pidl, out uint fetched) == 0 && fetched == 1)
                    {
                        try
                        {
                            if (folderView.GetItemPosition(pidl, out POINT p) == 0)
                                icons.Add(new RectInt(origin.X + p.X - cellInset, origin.Y + p.Y, spacing.X, spacing.Y));
                        }
                        finally
                        {
                            Marshal.FreeCoTaskMem(pidl);
                        }
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(items);
                }
            }

            return new DesktopIconSnapshot(spacing.X, spacing.Y, icons, autoArrange, (flags & FWF_NOICONS) != 0);
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or UnauthorizedAccessException)
        {
            failure = ex.GetType().Name + ": " + ex.Message;
            return null;
        }
        finally
        {
            if (enumPtr != 0) Marshal.Release(enumPtr);
            Release(view);
            Release(browser);
            Release(dispatch);
            Release(windows);
        }
    }

    private static void Release(object? com)
    {
        if (com is not null && Marshal.IsComObject(com)) Marshal.ReleaseComObject(com);
    }

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(nint hwnd, ref POINT point);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hwnd);

    [ComImport, Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface IShellWindows
    {
        // Vtable order up to FindWindowSW; the others are never called.
        void Count();
        void Item();
        void NewEnum();
        void Register();
        void RegisterPending();
        void Revoke();
        void OnNavigate();
        void OnActivated();
        [return: MarshalAs(UnmanagedType.IDispatch)]
        object FindWindowSW(ref object loc, ref object? locRoot, int swClass, out int hwnd, int options);
    }

    [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IServiceProvider
    {
        [PreserveSig] int QueryService(ref Guid service, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object? obj);
    }

    [ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellBrowser
    {
        // IOleWindow
        void GetWindow();
        void ContextSensitiveHelp();
        // IShellBrowser, in vtable order up to QueryActiveShellView. The others are never called.
        void InsertMenusSB();
        void SetMenuSB();
        void RemoveMenusSB();
        void SetStatusTextSB();
        void EnableModelessSB();
        void TranslateAcceleratorSB();
        void BrowseObject();
        void GetViewStateStream();
        void GetControlWindow();
        void SendControlMsg();
        [PreserveSig] int QueryActiveShellView([MarshalAs(UnmanagedType.IUnknown)] out object? view);
    }

    [ComImport, Guid("CDE725B0-CCC9-4519-917E-325D72FAB4CE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFolderView
    {
        void GetCurrentViewMode();
        void SetCurrentViewMode();
        void GetFolder();
        void Item();
        void ItemCount();
        [PreserveSig] int Items(uint flags, ref Guid riid, out nint enumerator);
        void GetSelectionMarkedItem();
        void GetFocusedItem();
        [PreserveSig] int GetItemPosition(nint pidl, out POINT point);
        [PreserveSig] int GetSpacing(ref POINT point);
        void GetDefaultSpacing();
        [PreserveSig] int GetAutoArrange();
    }

    [ComImport, Guid("1AF3A467-214F-4298-908E-06B03E0B39F9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFolderView2
    {
        // IFolderView
        void GetCurrentViewMode();
        void SetCurrentViewMode();
        void GetFolder();
        void Item();
        void ItemCount();
        void Items();
        void GetSelectionMarkedItem();
        void GetFocusedItem();
        void GetItemPosition();
        void GetSpacing();
        void GetDefaultSpacing();
        void GetAutoArrange();
        void SelectItem();
        void SelectAndPositionItems();
        // IFolderView2, in vtable order up to GetViewModeAndIconSize
        void SetGroupBy();
        void GetGroupBy();
        void SetViewProperty();
        void GetViewProperty();
        void SetTileViewProperties();
        void SetExtendedTileViewProperties();
        void SetText();
        void SetCurrentFolderFlags();
        [PreserveSig] int GetCurrentFolderFlags(out uint flags);
        void GetSortColumnCount();
        void SetSortColumns();
        void GetSortColumns();
        void GetItem();
        void GetVisibleItem();
        void GetSelectedItem();
        void GetSelection();
        void GetSelectionState();
        void InvokeVerbOnSelection();
        void SetViewModeAndIconSize();
        [PreserveSig] int GetViewModeAndIconSize(out int mode, out int imageSize);
    }

    [ComImport, Guid("000214F2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEnumIDList
    {
        [PreserveSig] int Next(uint count, out nint pidl, out uint fetched);
    }
}
