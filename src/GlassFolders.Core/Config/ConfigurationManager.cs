using System.Text.Json;
using GlassFolders.Core.Models;
using GlassFolders.Core.Storage;

namespace GlassFolders.Core.Config;

/// <summary>
/// Manages loading and atomic saving of settings and folders definitions.
/// Automatically recovers from corrupted files using .bak fallback and default seeding.
/// Supports standard user AppData as well as portable mode.
/// </summary>
public sealed class ConfigurationManager
{
    private readonly string _foldersPath;
    private readonly string _settingsPath;
    private readonly object _lock = new();

    public ConfigurationManager(string? baseDir = null)
    {
        string dir = baseDir ?? AppPaths.BaseAppDataDir;
        Directory.CreateDirectory(dir);
        _foldersPath = Path.Combine(dir, "folders.json");
        _settingsPath = Path.Combine(dir, "settings.json");
    }

    public string FoldersPath => _foldersPath;
    public string SettingsPath => _settingsPath;

    public FoldersConfiguration LoadFolders()
    {
        lock (_lock)
        {
            string? text = AtomicFile.ReadWithFallback(_foldersPath, IsValidFoldersJson);
            if (string.IsNullOrWhiteSpace(text))
            {
                var seeded = CreateDefaultFolders();
                SaveFolders(seeded);
                return seeded;
            }

            try
            {
                var config = JsonSerializer.Deserialize(text, ConfigurationJsonContext.Default.FoldersConfiguration);
                return config is { SchemaVersion: >= 1 } ? config : CreateDefaultFolders();
            }
            catch (JsonException)
            {
                return CreateDefaultFolders();
            }
        }
    }

    public void SaveFolders(FoldersConfiguration config)
    {
        lock (_lock)
        {
            string json = JsonSerializer.Serialize(config, ConfigurationJsonContext.Default.FoldersConfiguration);
            AtomicFile.WriteAllText(_foldersPath, json);
        }
    }

    public AppSettings LoadSettings()
    {
        lock (_lock)
        {
            string? text = AtomicFile.ReadWithFallback(_settingsPath, IsValidSettingsJson);
            if (string.IsNullOrWhiteSpace(text))
            {
                var defaults = new AppSettings();
                SaveSettings(defaults);
                return defaults;
            }

            try
            {
                var settings = JsonSerializer.Deserialize(text, ConfigurationJsonContext.Default.AppSettings);
                return settings is { SchemaVersion: >= 1 } ? settings : new AppSettings();
            }
            catch (JsonException)
            {
                return new AppSettings();
            }
        }
    }

    public void SaveSettings(AppSettings settings)
    {
        lock (_lock)
        {
            string json = JsonSerializer.Serialize(settings, ConfigurationJsonContext.Default.AppSettings);
            AtomicFile.WriteAllText(_settingsPath, json);
        }
    }

    private static bool IsValidFoldersJson(string text)
    {
        try
        {
            var doc = JsonSerializer.Deserialize(text, ConfigurationJsonContext.Default.FoldersConfiguration);
            return doc is { SchemaVersion: >= 1 };
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsValidSettingsJson(string text)
    {
        try
        {
            var doc = JsonSerializer.Deserialize(text, ConfigurationJsonContext.Default.AppSettings);
            return doc is { SchemaVersion: >= 1 };
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static FoldersConfiguration CreateDefaultFolders()
    {
        return new FoldersConfiguration
        {
            SchemaVersion = 1,
            Folders =
            [
                new FolderDef
                {
                    Id = "work",
                    Name = "Work",
                    ColorA = "#7FB2FF",
                    ColorB = "#5B5CF2",
                    Glyph = "Briefcase",
                    Size = FolderSize.Medium,
                    Items =
                    [
                        new FolderItem { DisplayName = "File Explorer", TargetPath = "explorer.exe", Type = FolderItemType.Executable },
                        new FolderItem { DisplayName = "Notepad", TargetPath = "notepad.exe", Type = FolderItemType.Executable },
                        new FolderItem { DisplayName = "Calculator", TargetPath = "calc.exe", Type = FolderItemType.Executable }
                    ]
                },
                new FolderDef
                {
                    Id = "games",
                    Name = "Games",
                    ColorA = "#C38BFF",
                    ColorB = "#7B3FE8",
                    Glyph = "Gamepad",
                    Size = FolderSize.Medium,
                    Items = []
                },
                new FolderDef
                {
                    Id = "media",
                    Name = "Media",
                    ColorA = "#FF9A9A",
                    ColorB = "#E8455A",
                    Glyph = "Play",
                    Size = FolderSize.Medium,
                    Items = []
                }
            ]
        };
    }
}
