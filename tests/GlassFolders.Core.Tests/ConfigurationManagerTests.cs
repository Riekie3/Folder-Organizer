using GlassFolders.Core.Config;
using GlassFolders.Core.Models;

namespace GlassFolders.Core.Tests;

public sealed class ConfigurationManagerTests : IDisposable
{
    private readonly string _testDir;
    private readonly ConfigurationManager _configManager;

    public ConfigurationManagerTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "gf-cm-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
        _configManager = new ConfigurationManager(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, recursive: true);
    }

    [Fact]
    public void LoadFolders_creates_folders_json_if_missing()
    {
        Assert.False(File.Exists(_configManager.FoldersPath));
        var config = _configManager.LoadFolders();
        Assert.NotNull(config);
        Assert.NotEmpty(config.Folders);
        Assert.True(File.Exists(_configManager.FoldersPath));
    }

    [Fact]
    public void SaveFolders_and_LoadFolders_round_trip()
    {
        var custom = new FoldersConfiguration
        {
            SchemaVersion = 1,
            Folders =
            [
                new FolderDef
                {
                    Id = "custom1",
                    Name = "My Folder",
                    ColorA = "#112233",
                    ColorB = "#445566",
                    Glyph = "Gear",
                    Items = [new FolderItem { DisplayName = "Item 1", TargetPath = "C:\\test.txt" }]
                }
            ]
        };

        _configManager.SaveFolders(custom);
        var loaded = _configManager.LoadFolders();

        Assert.Single(loaded.Folders);
        Assert.Equal("custom1", loaded.Folders[0].Id);
        Assert.Equal("My Folder", loaded.Folders[0].Name);
        Assert.Single(loaded.Folders[0].Items);
        Assert.Equal("Item 1", loaded.Folders[0].Items[0].DisplayName);
    }

    [Fact]
    public void LoadSettings_creates_defaults_if_missing()
    {
        Assert.False(File.Exists(_configManager.SettingsPath));
        var settings = _configManager.LoadSettings();
        Assert.NotNull(settings);
        Assert.Equal(4, settings.PopupColumns);
        Assert.Equal(3, settings.PopupRows);
        Assert.True(File.Exists(_configManager.SettingsPath));
    }

    [Fact]
    public void SaveSettings_and_LoadSettings_round_trip()
    {
        var settings = new AppSettings
        {
            PopupColumns = 5,
            PopupRows = 2,
            SnapToGrid = false,
            GridSize = 32
        };

        _configManager.SaveSettings(settings);
        var loaded = _configManager.LoadSettings();

        Assert.Equal(5, loaded.PopupColumns);
        Assert.Equal(2, loaded.PopupRows);
        Assert.False(loaded.SnapToGrid);
        Assert.Equal(32, loaded.GridSize);
    }

    [Fact]
    public void AppSettings_supports_startup_and_logging_options()
    {
        var settings = new AppSettings
        {
            StartWithWindows = true,
            StartupDelaySeconds = 10,
            LogLevel = "Debug",
            CheckMissingTargets = false
        };

        _configManager.SaveSettings(settings);
        var loaded = _configManager.LoadSettings();

        Assert.True(loaded.StartWithWindows);
        Assert.Equal(10, loaded.StartupDelaySeconds);
        Assert.Equal("Debug", loaded.LogLevel);
        Assert.False(loaded.CheckMissingTargets);
    }

    [Fact]
    public void Corrupted_folders_json_falls_back_to_bak()
    {
        var config1 = ConfigurationManager.CreateDefaultFolders();
        _configManager.SaveFolders(config1);

        var config2 = ConfigurationManager.CreateDefaultFolders();
        config2.Folders[0].Name = "Updated Work";
        _configManager.SaveFolders(config2); // Previous version is now in .bak

        // Corrupt main folders.json
        File.WriteAllText(_configManager.FoldersPath, "{ corrupted json !!!");

        var recovered = _configManager.LoadFolders();
        Assert.NotNull(recovered);
        Assert.Equal("Work", recovered.Folders[0].Name);
    }

    [Fact]
    public void Completely_broken_config_recovers_with_defaults()
    {
        File.WriteAllText(_configManager.FoldersPath, "{ completely broken");
        File.WriteAllText(_configManager.FoldersPath + ".bak", "{ also broken");

        var config = _configManager.LoadFolders();
        Assert.NotNull(config);
        Assert.Equal(3, config.Folders.Count);
        Assert.Contains(config.Folders, f => f.Id == "work");
    }
}
