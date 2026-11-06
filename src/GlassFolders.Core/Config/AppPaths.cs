using System.IO;

namespace GlassFolders.Core.Config;

/// <summary>
/// Centralized application paths provider.
/// Supports standard user paths (%APPDATA% and %LOCALAPPDATA%) as well as
/// standalone portable mode when 'portable.flag' is placed next to the executable.
/// </summary>
public static class AppPaths
{
    private static readonly Lazy<bool> _isPortable = new(() =>
    {
        try
        {
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            return File.Exists(Path.Combine(exeDir, "portable.flag"));
        }
        catch
        {
            return false;
        }
    });

    /// <summary>
    /// Indicates whether GlassFolders is operating in zero-footprint portable mode.
    /// </summary>
    public static bool IsPortable => _isPortable.Value;

    /// <summary>
    /// Primary configuration root directory (folders, settings, layout files).
    /// </summary>
    public static string BaseAppDataDir =>
        IsPortable
            ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GlassFolders");

    /// <summary>
    /// Local machine root directory (diagnostics logs, thumbnail and icon caches).
    /// </summary>
    public static string BaseLocalAppDataDir =>
        IsPortable
            ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "local")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GlassFolders");

    public static string FoldersPath => Path.Combine(BaseAppDataDir, "folders.json");
    public static string SettingsPath => Path.Combine(BaseAppDataDir, "settings.json");
    public static string BackupsDir => Path.Combine(BaseAppDataDir, "backups");
    public static string LogsDir => Path.Combine(BaseLocalAppDataDir, "logs");
    public static string IconCacheDir => Path.Combine(BaseLocalAppDataDir, "IconCache");
}
