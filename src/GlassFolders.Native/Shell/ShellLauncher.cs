using System.Diagnostics;
using GlassFolders.Core.Models;

namespace GlassFolders.Native.Shell;

/// <summary>
/// Handles launching applications, shortcuts, files, folders, and URLs.
/// Follows PLAN.md §8.2 rules: launches original .lnk/.url when available,
/// falls back to TargetPath, and handles Store apps via shell:AppsFolder.
/// </summary>
public static class ShellLauncher
{
    public static bool Launch(FolderItem item)
    {
        try
        {
            // 1. If created from a shortcut that still exists, launch that to preserve args, icon, and elevation
            if (!string.IsNullOrEmpty(item.ShortcutPath) && File.Exists(item.ShortcutPath))
            {
                var psi = new ProcessStartInfo
                {
                    FileName = item.ShortcutPath,
                    UseShellExecute = true
                };
                Process.Start(psi);
                return true;
            }

            // 2. Store App (AUMID)
            if (item.Type == FolderItemType.StoreApp || !string.IsNullOrEmpty(item.Aumid))
            {
                string aumid = item.Aumid ?? item.TargetPath.Replace("shell:AppsFolder\\", "");
                var psi = new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"shell:AppsFolder\\{aumid}",
                    UseShellExecute = true
                };
                Process.Start(psi);
                return true;
            }

            // 3. URL
            if (item.Type == FolderItemType.Url || item.TargetPath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || item.TargetPath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                var psi = new ProcessStartInfo
                {
                    FileName = item.TargetPath,
                    UseShellExecute = true
                };
                Process.Start(psi);
                return true;
            }

            // 4. Regular Executable, File or Folder
            var startInfo = new ProcessStartInfo
            {
                FileName = item.TargetPath,
                UseShellExecute = true
            };

            if (!string.IsNullOrEmpty(item.Arguments))
                startInfo.Arguments = item.Arguments;

            if (!string.IsNullOrEmpty(item.WorkingDirectory) && Directory.Exists(item.WorkingDirectory))
                startInfo.WorkingDirectory = item.WorkingDirectory;

            Process.Start(startInfo);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to launch '{item.DisplayName}' ({item.TargetPath}): {ex.Message}");
            return false;
        }
    }

    public static bool OpenLocation(FolderItem item)
    {
        string? candidate = ResolveLocationCandidate(item);
        if (string.IsNullOrEmpty(candidate)) return false;

        try
        {
            if (Directory.Exists(candidate))
            {
                // For folders, open the folder itself
                Process.Start(new ProcessStartInfo
                {
                    FileName = Path.GetFullPath(candidate),
                    UseShellExecute = true
                });
                return true;
            }

            if (File.Exists(candidate))
            {
                // For files, select the file in its containing folder
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{Path.GetFullPath(candidate)}\"",
                    UseShellExecute = true
                });
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to open location for '{item.DisplayName}': {ex.Message}");
            return false;
        }
    }

    public static string? ResolveLocationCandidate(FolderItem item)
    {
        // 1. If TargetPath exists on disk, prefer the actual target binary or folder
        if (!string.IsNullOrWhiteSpace(item.TargetPath))
        {
            if (File.Exists(item.TargetPath) || Directory.Exists(item.TargetPath))
                return Path.GetFullPath(item.TargetPath);

            // Check Windows directory, System32, or PATH for standalone names like notepad.exe, explorer.exe
            string? found = FindInPathOrSystem(item.TargetPath);
            if (found != null && (File.Exists(found) || Directory.Exists(found)))
                return Path.GetFullPath(found);
        }

        // 2. If target is not a local file on disk, fall back to the shortcut (.lnk) if available
        if (!string.IsNullOrWhiteSpace(item.ShortcutPath) && (File.Exists(item.ShortcutPath) || Directory.Exists(item.ShortcutPath)))
            return Path.GetFullPath(item.ShortcutPath);

        return null;
    }

    private static string? FindInPathOrSystem(string target)
    {
        try
        {
            string fileName = Path.GetFileName(target);
            if (string.IsNullOrEmpty(fileName)) return null;

            // Check Windows directory (e.g. explorer.exe)
            string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string inWin = Path.Combine(winDir, fileName);
            if (File.Exists(inWin)) return inWin;

            // Check System32 (e.g. notepad.exe, cmd.exe, calc.exe)
            string sysDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
            string inSys = Path.Combine(sysDir, fileName);
            if (File.Exists(inSys)) return inSys;

            // Check PATH
            string? envPath = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(envPath))
            {
                foreach (string p in envPath.Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    string candidate = Path.Combine(p.Trim(), fileName);
                    if (File.Exists(candidate)) return candidate;
                    if (!fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(candidate + ".exe"))
                        return candidate + ".exe";
                }
            }
        }
        catch { }

        return null;
    }
}
