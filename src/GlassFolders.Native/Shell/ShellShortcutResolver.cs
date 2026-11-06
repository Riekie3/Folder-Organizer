using System.Runtime.InteropServices;
using System.Text;
using GlassFolders.Core.Folders;
using GlassFolders.Core.Models;

namespace GlassFolders.Native.Shell;

/// <summary>
/// Resolves paths, shortcuts (.lnk), internet shortcuts (.url), and folders into FolderItem models.
/// </summary>
public sealed class ShellShortcutResolver : IShortcutResolver
{
    public FolderItem Resolve(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path cannot be empty.", nameof(path));

        path = path.Trim('\"');

        // Check for URL
        if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return new FolderItem
            {
                Type = FolderItemType.Url,
                DisplayName = GetUrlDisplayName(path),
                TargetPath = path
            };
        }

        string ext = Path.GetExtension(path).ToLowerInvariant();

        if (ext == ".lnk" && File.Exists(path))
        {
            return ResolveLnk(path);
        }

        if (ext == ".url" && File.Exists(path))
        {
            return ResolveUrl(path);
        }

        if (Directory.Exists(path))
        {
            return new FolderItem
            {
                Type = FolderItemType.Folder,
                DisplayName = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
                TargetPath = Path.GetFullPath(path)
            };
        }

        if (File.Exists(path))
        {
            bool isExe = ext == ".exe";
            return new FolderItem
            {
                Type = isExe ? FolderItemType.Executable : FolderItemType.File,
                DisplayName = Path.GetFileNameWithoutExtension(path),
                TargetPath = Path.GetFullPath(path),
                WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(path))
            };
        }

        // Store App / AUMID (e.g., shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App)
        if (path.StartsWith("shell:AppsFolder\\", StringComparison.OrdinalIgnoreCase))
        {
            string aumid = path["shell:AppsFolder\\".Length..];
            return new FolderItem
            {
                Type = FolderItemType.StoreApp,
                DisplayName = aumid.Contains('!') ? aumid[(aumid.IndexOf('!') + 1)..] : aumid,
                TargetPath = path,
                Aumid = aumid
            };
        }

        // Fallback for missing/arbitrary executable name (e.g. "notepad.exe", "calc.exe")
        return new FolderItem
        {
            Type = ext == ".exe" || !path.Contains(Path.DirectorySeparatorChar) ? FolderItemType.Executable : FolderItemType.File,
            DisplayName = Path.GetFileNameWithoutExtension(path),
            TargetPath = path
        };
    }

    public bool TargetExists(FolderItem item)
    {
        if (item.Type == FolderItemType.Url) return true;
        if (item.Type == FolderItemType.StoreApp) return true;
        if (item.Type == FolderItemType.Folder) return Directory.Exists(item.TargetPath);
        if (File.Exists(item.TargetPath)) return true;
        if (!string.IsNullOrEmpty(item.ShortcutPath) && File.Exists(item.ShortcutPath)) return true;

        // Check in Windows/System32/PATH for standalone exe names
        if (item.Type == FolderItemType.Executable && !Path.IsPathRooted(item.TargetPath))
        {
            string? found = FindInPath(item.TargetPath);
            if (found != null) return true;
        }

        return false;
    }

    private static IShellLinkW CreateShellLink()
    {
        try
        {
            return (IShellLinkW)new ShellLink();
        }
        catch
        {
            var clsid = new Guid("00021401-0000-0000-C000-000000000046");
            var type = Type.GetTypeFromCLSID(clsid, throwOnError: true)!;
            return (IShellLinkW)Activator.CreateInstance(type)!;
        }
    }

    private static FolderItem ResolveLnk(string lnkPath)
    {
        string fullLnk = Path.GetFullPath(lnkPath);
        string name = Path.GetFileNameWithoutExtension(fullLnk);

        try
        {
            var link = CreateShellLink();
            var persist = (IPersistFile)link;
            persist.Load(fullLnk, 0);

            string target = string.Empty;
            try
            {
                var targetSb = new StringBuilder(1024);
                // pfd is NULL (0) so Windows does not write a WIN32_FIND_DATA structure to unallocated stack
                link.GetPath(targetSb, targetSb.Capacity, 0, 0);
                target = targetSb.ToString();
            }
            catch { }

            string args = string.Empty;
            try
            {
                var argsSb = new StringBuilder(1024);
                link.GetArguments(argsSb, argsSb.Capacity);
                args = argsSb.ToString();
            }
            catch { }

            string workDir = string.Empty;
            try
            {
                var workDirSb = new StringBuilder(1024);
                link.GetWorkingDirectory(workDirSb, workDirSb.Capacity);
                workDir = workDirSb.ToString();
            }
            catch { }

            string iconLocation = string.Empty;
            int iconIndex = 0;
            try
            {
                var iconSb = new StringBuilder(1024);
                link.GetIconLocation(iconSb, iconSb.Capacity, out iconIndex);
                iconLocation = iconSb.ToString();
            }
            catch { }

            FolderItemType type = FolderItemType.Shortcut;
            if (!string.IsNullOrWhiteSpace(target))
            {
                if (Directory.Exists(target)) type = FolderItemType.Folder;
                else if (target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) type = FolderItemType.Executable;
            }

            return new FolderItem
            {
                Type = type,
                DisplayName = name,
                TargetPath = string.IsNullOrWhiteSpace(target) ? fullLnk : target,
                ShortcutPath = fullLnk,
                Arguments = string.IsNullOrWhiteSpace(args) ? null : args,
                WorkingDirectory = string.IsNullOrWhiteSpace(workDir) ? null : workDir,
                IconLocation = string.IsNullOrWhiteSpace(iconLocation) ? null : iconLocation,
                IconIndex = iconIndex
            };
        }
        catch
        {
            return new FolderItem
            {
                Type = FolderItemType.Shortcut,
                DisplayName = name,
                TargetPath = fullLnk,
                ShortcutPath = fullLnk
            };
        }
    }

    private static FolderItem ResolveUrl(string urlPath)
    {
        string fullPath = Path.GetFullPath(urlPath);
        string name = Path.GetFileNameWithoutExtension(fullPath);
        string url = fullPath;

        try
        {
            foreach (string line in File.ReadAllLines(fullPath))
            {
                if (line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
                {
                    url = line["URL=".Length..].Trim();
                    break;
                }
            }
        }
        catch { }

        return new FolderItem
        {
            Type = FolderItemType.Url,
            DisplayName = name,
            TargetPath = url,
            ShortcutPath = fullPath
        };
    }

    private static string GetUrlDisplayName(string url)
    {
        try
        {
            var uri = new Uri(url);
            return uri.Host;
        }
        catch
        {
            return url;
        }
    }

    private static string? FindInPath(string fileName)
    {
        try
        {
            string win = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), fileName);
            if (File.Exists(win)) return win;

            string sys = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), fileName);
            if (File.Exists(sys)) return sys;

            string? envPath = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(envPath))
            {
                foreach (string path in envPath.Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    string full = Path.Combine(path.Trim(), fileName);
                    if (File.Exists(full)) return full;
                    if (!fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(full + ".exe"))
                        return full + ".exe";
                }
            }
        }
        catch { }
        return null;
    }
}

[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("000214F9-0000-0000-C000-000000000046")]
internal interface IShellLinkW
{
    void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, nint pfd, uint fFlags);
    void GetIDList(out nint ppidl);
    void SetIDList(nint pidl);
    void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
    void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
    void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
    void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
    void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
    void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
    void GetHotkey(out ushort pwHotkey);
    void SetHotkey(ushort wHotkey);
    void GetShowCmd(out int piShowCmd);
    void SetShowCmd(int iShowCmd);
    void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
    void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
    void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
    void Resolve(nint hwnd, uint fFlags);
    void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
}

[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("0000010b-0000-0000-C000-000000000046")]
internal interface IPersistFile
{
    void GetClassID(out Guid pClassID);
    [PreserveSig] int IsDirty();
    void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
    void Save([MarshalAs(UnmanagedType.LPWStr)] string? pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
    void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
    void GetCurFile([Out, MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
}

[ComImport]
[Guid("00021401-0000-0000-C000-000000000046")]
[ClassInterface(ClassInterfaceType.None)]
internal class ShellLink;
