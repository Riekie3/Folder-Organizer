using System.Runtime.InteropServices;

namespace GlassFolders.Native.Shell;

/// <summary>
/// Reads large, full-alpha icons through the shell's image factory. This is the only documented route to the
/// 256 px artwork inside apps and shortcuts. Older APIs stop at 32 or 48 px.
/// </summary>
public static partial class ShellImage
{
    private const int SIIGBF_ICONONLY = 0x04;

    /// <summary>
    /// Asks the shell for an icon as top-down BGRA. The image is at most <paramref name="size"/> pixels square.
    /// Whether the pixels are premultiplied depends on the source, so callers check.
    /// </summary>
    public static bool TryGetIcon(string path, int size, out byte[] pixels, out int width, out int height)
    {
        pixels = [];
        width = height = 0;

        nint bitmap = 0;
        try
        {
            Guid iid = typeof(IShellItemImageFactory).GUID;
            int hr = SHCreateItemFromParsingName(path, 0, in iid, out IShellItemImageFactory factory);
            if (hr != 0 || factory is null) return false;

            try
            {
                hr = factory.GetImage(new NativeSize(size, size), SIIGBF_ICONONLY, out bitmap);
                if (hr != 0 || bitmap == 0) return false;
            }
            finally
            {
                Marshal.ReleaseComObject(factory);
            }

            return ReadBitmap(bitmap, out pixels, out width, out height);
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or ArgumentException)
        {
            return false;
        }
        finally
        {
            if (bitmap != 0) DeleteObject(bitmap);
        }
    }

    private static bool ReadBitmap(nint bitmap, out byte[] pixels, out int width, out int height)
    {
        pixels = [];
        width = height = 0;

        if (GetObject(bitmap, Marshal.SizeOf<BITMAP>(), out BITMAP info) == 0) return false;
        width = info.bmWidth;
        height = Math.Abs(info.bmHeight);
        if (width <= 0 || height <= 0) return false;

        var header = new BITMAPINFOHEADER
        {
            biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = width,
            biHeight = -height, // negative means top-down
            biPlanes = 1,
            biBitCount = 32,
        };

        pixels = new byte[width * height * 4];
        nint dc = CreateCompatibleDC(0);
        if (dc == 0) return false;
        try
        {
            return GetDIBits(dc, bitmap, 0, (uint)height, pixels, ref header, 0) == height;
        }
        finally
        {
            DeleteDC(dc);
        }
    }

    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(NativeSize size, int flags, out nint bitmap);
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativeSize(int cx, int cy)
    {
        public readonly int Cx = cx, Cy = cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType, bmWidth, bmHeight, bmWidthBytes;
        public ushort bmPlanes, bmBitsPixel;
        public nint bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth, biHeight;
        public ushort biPlanes, biBitCount;
        public uint biCompression, biSizeImage;
        public int biXPelsPerMeter, biYPelsPerMeter;
        public uint biClrUsed, biClrImportant;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(string path, nint bindContext, in Guid iid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory factory);

    [DllImport("gdi32.dll", EntryPoint = "GetObjectW")]
    private static extern int GetObject(nint obj, int size, out BITMAP info);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(nint dc, nint bitmap, uint start, uint lines, [Out] byte[] bits, ref BITMAPINFOHEADER info, uint usage);

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateCompatibleDC(nint dc);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteDC(nint dc);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(nint obj);
}
