namespace GlassFolders.Core.Backup;

/// <summary>
/// Suggests relinked paths for shortcuts or file items whose original targets are missing
/// (e.g. after restoring onto a different PC or a different Windows user account).
/// </summary>
public static class PathRelinker
{
    public static string? TryRelink(string targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath)) return null;

        // 1. If already exists, no relinking needed
        if (File.Exists(targetPath) || Directory.Exists(targetPath))
            return targetPath;

        // 2. Expand environment variables (e.g. %LOCALAPPDATA%, %APPDATA%, %PROGRAMFILES%)
        string expanded = Environment.ExpandEnvironmentVariables(targetPath);
        if (File.Exists(expanded) || Directory.Exists(expanded))
            return expanded;

        // 3. User profile substitution: C:\Users\<OldUser>\... -> Current %USERPROFILE%
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        int usersIdx = targetPath.IndexOf(@"\Users\", StringComparison.OrdinalIgnoreCase);
        if (usersIdx >= 0)
        {
            int afterUserSlash = targetPath.IndexOf('\\', usersIdx + @"\Users\".Length);
            if (afterUserSlash > 0)
            {
                string relSubPath = targetPath[afterUserSlash..];
                string candidate = userProfile + relSubPath;
                if (File.Exists(candidate) || Directory.Exists(candidate))
                    return candidate;
            }
        }

        // 4. Program Files 32-bit vs 64-bit flip
        string pf64 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string pf32 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        if (!string.IsNullOrEmpty(pf64) && targetPath.StartsWith(pf64, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(pf32))
        {
            string candidate = pf32 + targetPath[pf64.Length..];
            if (File.Exists(candidate) || Directory.Exists(candidate))
                return candidate;
        }
        else if (!string.IsNullOrEmpty(pf32) && targetPath.StartsWith(pf32, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(pf64))
        {
            string candidate = pf64 + targetPath[pf32.Length..];
            if (File.Exists(candidate) || Directory.Exists(candidate))
                return candidate;
        }

        // 5. System32 / Windows tools
        string fileName = Path.GetFileName(targetPath);
        string system32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
        string sysCandidate = Path.Combine(system32, fileName);
        if (File.Exists(sysCandidate))
            return sysCandidate;

        return null;
    }
}
