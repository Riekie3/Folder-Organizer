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
        Assert.Equal(2, settings.PopupRows);
        Assert.True(File.Exists(_configManager.SettingsPath));
    }

    [Fact]
    public void LoadSettings_moves_the_old_default_of_3_rows_to_the_new_default()
    {
        File.WriteAllText(_configManager.SettingsPath, """{"schemaVersion":1,"popupColumns":5,"popupRows":3}""");

        var settings = _configManager.LoadSettings();

        Assert.Equal(2, settings.PopupRows);
        Assert.Equal(5, settings.PopupColumns);
        Assert.Equal(AppSettings.CurrentSchemaVersion, settings.SchemaVersion);
        Assert.Equal(2, _configManager.LoadSettings().PopupRows);
    }

    [Fact]
    public void LoadSettings_keeps_rows_the_user_chose()
    {
        File.WriteAllText(_configManager.SettingsPath, """{"schemaVersion":1,"popupRows":4}""");
        Assert.Equal(4, _configManager.LoadSettings().PopupRows);

        // From version 2 on, 3 rows is a real choice and stays.
        _configManager.SaveSettings(new AppSettings { PopupRows = 3 });
        Assert.Equal(3, _configManager.LoadSettings().PopupRows);
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
        Assert.Equal(4, config.Folders.Count);
        Assert.Contains(config.Folders, f => f.Id == "work");
    }

    [Fact]
    public void Default_folders_use_the_reference_palette()
    {
        var config = ConfigurationManager.CreateDefaultFolders();
        Assert.Equal(FoldersConfiguration.CurrentSchemaVersion, config.SchemaVersion);
        Assert.Equal(["Work", "Games", "Media", "Utilities"], config.Folders.Select(f => f.Name));
        Assert.Equal(FolderPalette.Blue.Front, config.Folders[0].ColorA);
        Assert.Equal(FolderPalette.Green.Back, config.Folders[3].ColorB);
        Assert.All(config.Folders, f => Assert.Equal(FolderStyle.Solid, f.Style));
    }

    [Fact]
    public void LoadFolders_moves_old_preset_colours_to_the_reference_palette_once()
    {
        File.WriteAllText(_configManager.FoldersPath, """
            {"schemaVersion":1,"folders":[
              {"id":"a","name":"A","colorA":"#7FB2FF","colorB":"#5B5CF2"},
              {"id":"b","name":"B","colorA":"#ff9a9a","colorB":"#e8455a"},
              {"id":"c","name":"C","colorA":"#112233","colorB":"#445566"}]}
            """);

        var config = _configManager.LoadFolders();

        Assert.Equal(FoldersConfiguration.CurrentSchemaVersion, config.SchemaVersion);
        Assert.Equal((FolderPalette.Blue.Front, FolderPalette.Blue.Back), (config.Folders[0].ColorA, config.Folders[0].ColorB));
        Assert.Equal((FolderPalette.Coral.Front, FolderPalette.Coral.Back), (config.Folders[1].ColorA, config.Folders[1].ColorB));
        Assert.Equal(("#112233", "#445566"), (config.Folders[2].ColorA, config.Folders[2].ColorB));

        // Saved, so it runs once: from version 2 on, the old Blue values are the Violet preset and stay.
        Assert.Equal(FoldersConfiguration.CurrentSchemaVersion, _configManager.LoadFolders().SchemaVersion);
        config.Folders[2].ColorA = "#7FB2FF";
        config.Folders[2].ColorB = "#5B5CF2";
        _configManager.SaveFolders(config);
        Assert.Equal("#7FB2FF", _configManager.LoadFolders().Folders[2].ColorA);
    }

    [Fact]
    public void Folder_style_size_and_custom_icon_round_trip()
    {
        var config = ConfigurationManager.CreateDefaultFolders();
        config.Folders[0].Style = FolderStyle.Glass;
        config.Folders[0].Size = FolderSize.Large;
        config.Folders[0].CustomIconPath = @"C:\icons\work.png";
        _configManager.SaveFolders(config);

        var loaded = _configManager.LoadFolders().Folders[0];
        Assert.Equal(FolderStyle.Glass, loaded.Style);
        Assert.Equal(FolderSize.Large, loaded.Size);
        Assert.Equal(@"C:\icons\work.png", loaded.CustomIconPath);
    }

    [Fact]
    public void Item_count_badge_is_off_by_default()
    {
        Assert.False(new AppSettings().ShowItemCount);
        _configManager.SaveSettings(new AppSettings { ShowItemCount = true });
        Assert.True(_configManager.LoadSettings().ShowItemCount);
    }

    [Fact]
    public void Appearance_settings_default_to_the_tuned_look()
    {
        var s = new AppSettings();
        Assert.Equal(AppTheme.System, s.Theme);
        Assert.Equal(GlassAppearance.DefaultTransparency, s.GlassTransparency);
        Assert.Equal(GlassAppearance.DefaultBlur, s.GlassBlur);
        Assert.Equal(GlassAppearance.DefaultBorder, s.GlassBorder);
        Assert.Equal(GlassAppearance.DefaultShadow, s.GlassShadow);
        Assert.Equal(GlassAppearance.DefaultCornerRadius, s.CornerRadius);
        Assert.Equal(PopupIconSize.Medium, s.PopupIconSize);
    }

    [Fact]
    public void Appearance_settings_round_trip()
    {
        _configManager.SaveSettings(new AppSettings
        {
            Theme = AppTheme.Light,
            GlassTransparency = 80,
            GlassBlur = 20,
            GlassBorder = 0,
            GlassShadow = 100,
            CornerRadius = 24,
            PopupIconSize = PopupIconSize.Large,
            LogLevel = "Debug",
        });

        var loaded = _configManager.LoadSettings();
        Assert.Equal(AppTheme.Light, loaded.Theme);
        Assert.Equal(80, loaded.GlassTransparency);
        Assert.Equal(20, loaded.GlassBlur);
        Assert.Equal(0, loaded.GlassBorder);
        Assert.Equal(100, loaded.GlassShadow);
        Assert.Equal(24, loaded.CornerRadius);
        Assert.Equal(PopupIconSize.Large, loaded.PopupIconSize);
        Assert.Equal("Debug", loaded.LogLevel);
    }

    [Fact]
    public void A_settings_file_from_before_the_appearance_options_gets_the_defaults()
    {
        File.WriteAllText(_configManager.SettingsPath, """{"schemaVersion":2,"popupColumns":5,"popupRows":2}""");
        var loaded = _configManager.LoadSettings();
        Assert.Equal(5, loaded.PopupColumns);
        Assert.Equal(AppTheme.System, loaded.Theme);
        Assert.Equal(GlassAppearance.DefaultBlur, loaded.GlassBlur);
        Assert.Equal(GlassAppearance.DefaultCornerRadius, loaded.CornerRadius);
    }
}
