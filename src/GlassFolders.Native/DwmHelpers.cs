using System.Runtime.InteropServices;

namespace GlassFolders.Native;

public static partial class DwmHelpers
{
    [LibraryImport("dwmapi.dll")]
    private static partial int DwmFlush();

    /// <summary>Blocks until the compositor finishes its next frame, so what's on screen is up to date.</summary>
    public static void WaitForNextFrame()
    {
        try { DwmFlush(); } catch (DllNotFoundException) { }
    }
}
