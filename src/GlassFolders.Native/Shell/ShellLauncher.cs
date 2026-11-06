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
        string? candidate = null;
        if (!string.IsNullOrEmpty(item.ShortcutPath) && File.Exists(item.ShortcutPath))
            candidate = item.ShortcutPath;
        else if (File.Exists(item.TargetPath) || Directory.Exists(item.TargetPath))
            candidate = item.TargetPath;

        if (string.IsNullOrEmpty(candidate)) return false;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{Path.GetFullPath(candidate)}\"",
                UseShellExecute = true
            });
            return true;
        }
        catch
        {
            return false;
        }
    }
}
