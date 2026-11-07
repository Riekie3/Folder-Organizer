using System.IO;
using Microsoft.Win32;
using GlassFolders.Native;

namespace GlassFolders.App.Services;

/// <summary>
/// Manages Windows startup registration (HKCU\...\Run) and startup delays.
/// </summary>
public sealed class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "GlassFolders";

    public static bool IsStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
            return key?.GetValue(ValueName) != null;
        }
        catch (Exception ex)
        {
            DiagnosticService.Instance.LogWarn("Failed to read startup registry key", ex);
            return false;
        }
    }

    /// <summary>True only when the running process is the real GlassFolders.exe (not a test or tool host).</summary>
    private static bool IsRealApp(string? exe) =>
        !string.IsNullOrEmpty(exe) &&
        string.Equals(Path.GetFileName(exe), "GlassFolders.exe", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// If startup is enabled but the entry points at some other program (or a moved copy),
    /// rewrite it to the running GlassFolders.exe. Does nothing when startup is off.
    /// </summary>
    public static void RepairStartupPath()
    {
        try
        {
            string? exe = Environment.ProcessPath;
            if (!IsRealApp(exe)) return;
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
            if (key?.GetValue(ValueName) is not string current) return;
            string expected = $"\"{exe}\" --tray";
            if (string.Equals(current, expected, StringComparison.OrdinalIgnoreCase)) return;
            key.SetValue(ValueName, expected);
            DiagnosticService.Instance.LogInfo($"Repaired startup entry (was: {current})");
        }
        catch (Exception ex)
        {
            DiagnosticService.Instance.LogWarn("Failed to repair startup registry key", ex);
        }
    }

    public static void SetStartup(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
            if (key == null) return;

            if (enable)
            {
                string? exe = Environment.ProcessPath;
                if (IsRealApp(exe))
                {
                    key.SetValue(ValueName, $"\"{exe}\" --tray");
                    DiagnosticService.Instance.LogInfo("Registered GlassFolders in HKCU Run key");
                }
            }
            else
            {
                key.DeleteValue(ValueName, false);
                DiagnosticService.Instance.LogInfo("Unregistered GlassFolders from HKCU Run key");
            }
        }
        catch (Exception ex)
        {
            DiagnosticService.Instance.LogWarn("Failed to update startup registry key", ex);
        }
    }

    /// <summary>
    /// Waits until the Windows shell (Progman / ShellWindow) is ready and responsive,
    /// plus any configured extra delay.
    /// </summary>
    public static async Task<bool> WaitForShellReadyAsync(int extraDelaySeconds = 0, CancellationToken ct = default)
    {
        DiagnosticService.Instance.LogInfo("Waiting for Windows Shell to be ready...");

        DateTime start = DateTime.UtcNow;
        TimeSpan timeout = TimeSpan.FromSeconds(30);

        while (DateTime.UtcNow - start < timeout && !ct.IsCancellationRequested)
        {
            nint shell = NativeMethods.GetShellWindow();
            nint progman = NativeMethods.FindWindow("Progman", null);

            if (shell != 0 && progman != 0)
            {
                DiagnosticService.Instance.LogInfo($"Shell ready (Progman=0x{progman:X}).");
                if (extraDelaySeconds > 0)
                {
                    DiagnosticService.Instance.LogInfo($"Applying extra startup delay of {extraDelaySeconds}s...");
                    await Task.Delay(TimeSpan.FromSeconds(extraDelaySeconds), ct);
                }
                return true;
            }

            try
            {
                await Task.Delay(250, ct);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        DiagnosticService.Instance.LogWarn("Timed out waiting for Windows shell window to be ready.");
        return false;
    }
}
