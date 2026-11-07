using System.Runtime.InteropServices;

namespace GlassFolders.Native;

/// <summary>
/// One-shot read of screen pixels with GDI. The pixels live in native memory only for the length of the
/// callback, so a capture leaves no full-size copy of the screen behind and nothing is written to disk.
/// </summary>
public static partial class ScreenCapture
{
    /// <summary>Reads top-down BGRA pixels. The alpha channel is undefined.</summary>
    public delegate T PixelReader<T>(ReadOnlySpan<byte> pixels, int width, int height, int stride);

    private const uint SRCCOPY = 0x00CC0020;
    private const uint CAPTUREBLT = 0x40000000; // includes other layered windows, such as the desktop folders

    /// <summary>
    /// Captures an area in physical screen coordinates and hands the pixels to <paramref name="read"/>.
    /// Returns false when Windows refuses, for example on the lock screen or a UAC prompt.
    /// </summary>
    public static unsafe bool TryRead<T>(RECT area, PixelReader<T> read, out T? result)
    {
        result = default;
        int width = area.Width, height = area.Height;
        if (width <= 0 || height <= 0) return false;

        nint screen = GetDC(0);
        if (screen == 0) return false;

        nint memory = 0, bitmap = 0, previous = 0;
        try
        {
            memory = CreateCompatibleDC(screen);
            if (memory == 0) return false;

            var info = new BITMAPINFOHEADER
            {
                biSize = (uint)sizeof(BITMAPINFOHEADER),
                biWidth = width,
                biHeight = -height, // negative means top-down
                biPlanes = 1,
                biBitCount = 32,
            };
            bitmap = CreateDIBSection(screen, in info, 0, out nint bits, 0, 0);
            if (bitmap == 0 || bits == 0) return false;

            previous = SelectObject(memory, bitmap);
            if (!BitBlt(memory, 0, 0, width, height, screen, area.Left, area.Top, SRCCOPY | CAPTUREBLT))
                return false;
            GdiFlush();

            result = read(new ReadOnlySpan<byte>((void*)bits, checked(width * height * 4)), width, height, width * 4);
            return true;
        }
        finally
        {
            if (previous != 0) SelectObject(memory, previous);
            if (bitmap != 0) DeleteObject(bitmap);
            if (memory != 0) DeleteDC(memory);
            ReleaseDC(0, screen);
        }
    }

    /// <summary>Scale of a monitor as a factor of 96 DPI; 1.25 means 125 %.</summary>
    public static double MonitorScale(nint monitor)
    {
        try
        {
            if (GetDpiForMonitor(monitor, 0 /* MDT_EFFECTIVE_DPI */, out uint dpiX, out _) == 0 && dpiX > 0)
                return dpiX / 96.0;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
        }
        return NativeMethods.GetDpiForSystem() / 96.0;
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

    [LibraryImport("user32.dll")]
    private static partial nint GetDC(nint hwnd);

    [LibraryImport("user32.dll")]
    private static partial int ReleaseDC(nint hwnd, nint dc);

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateCompatibleDC(nint dc);

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateDIBSection(nint dc, in BITMAPINFOHEADER info, uint usage, out nint bits, nint section, uint offset);

    [LibraryImport("gdi32.dll")]
    private static partial nint SelectObject(nint dc, nint obj);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool BitBlt(nint dest, int x, int y, int width, int height, nint source, int sourceX, int sourceY, uint rop);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GdiFlush();

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(nint obj);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteDC(nint dc);

    [LibraryImport("shcore.dll")]
    private static partial int GetDpiForMonitor(nint monitor, int dpiType, out uint dpiX, out uint dpiY);
}
