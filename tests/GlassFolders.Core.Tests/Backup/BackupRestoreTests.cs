using System.IO.Compression;
using System.Text;
using GlassFolders.Core.Backup;
using GlassFolders.Core.Config;
using GlassFolders.Core.Folders;
using GlassFolders.Core.Models;

namespace GlassFolders.Core.Tests.Backup;

public sealed class BackupRestoreTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _backupsDir;
    private readonly ConfigurationManager _configManager;
    private readonly FolderManager _folderManager;
    private readonly BackupService _backupService;

    public BackupRestoreTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "gf-backup-tests-" + Guid.NewGuid().ToString("N"));
        _backupsDir = Path.Combine(_testDir, "Backups");
        Directory.CreateDirectory(_testDir);
        Directory.CreateDirectory(_backupsDir);

        _configManager = new ConfigurationManager(_testDir);
        _folderManager = new FolderManager(_configManager);
        _backupService = new BackupService(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, recursive: true);
    }

    [Fact]
    public void Export_and_Import_round_trip_restores_configuration()
    {
        // Setup initial configuration with a custom folder
        _folderManager.CreateFolder("Development", "#123456", "#654321", "Code", 250, 350);
        var folder = _folderManager.Folders.First(f => f.Name == "Development");
        _folderManager.AddItem(folder.Id, new FolderItem { DisplayName = "VS Code", TargetPath = "C:\\Tools\\code.exe" });

        string zipPath = Path.Combine(_testDir, "test-backup.zip");
        _backupService.Export(zipPath, _configManager, ["DISPLAY1"]);

        Assert.True(File.Exists(zipPath));

        // Create a new blank configuration manager
        string freshDir = Path.Combine(Path.GetTempPath(), "gf-fresh-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(freshDir);
        try
        {
            var freshConfig = new ConfigurationManager(freshDir);
            var freshFolderManager = new FolderManager(freshConfig);

            var plan = ImportPlanner.CreatePlan(zipPath, freshFolderManager);
            Assert.NotNull(plan);
            Assert.Contains(plan.FoldersConfig.Folders, f => f.Name == "Development");

            _backupService.ApplyImport(plan, ImportMode.Replace, freshConfig, freshFolderManager);

            Assert.Contains(freshFolderManager.Folders, f => f.Name == "Development");
            var restoredFolder = freshFolderManager.Folders.First(f => f.Name == "Development");
            Assert.Single(restoredFolder.Items);
            Assert.Equal("VS Code", restoredFolder.Items[0].DisplayName);
        }
        finally
        {
            if (Directory.Exists(freshDir))
                Directory.Delete(freshDir, recursive: true);
        }
    }

    [Fact]
    public void ZipSafety_rejects_path_traversal()
    {
        string maliciousZip = Path.Combine(_testDir, "traversal.zip");
        using (var archive = ZipFile.Open(maliciousZip, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("../evil.txt");
            using var s = entry.Open();
            s.Write(Encoding.UTF8.GetBytes("malicious payload"));
        }

        Assert.Throws<InvalidOperationException>(() =>
        {
            using var readArchive = ZipFile.OpenRead(maliciousZip);
            ZipSafety.ValidateArchive(readArchive);
        });
    }

    [Fact]
    public void ZipSafety_rejects_executable_files()
    {
        string maliciousZip = Path.Combine(_testDir, "malware.zip");
        using (var archive = ZipFile.Open(maliciousZip, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("payload.exe");
            using var s = entry.Open();
            s.Write(Encoding.UTF8.GetBytes("binary payload"));
        }

        Assert.Throws<InvalidOperationException>(() =>
        {
            using var readArchive = ZipFile.OpenRead(maliciousZip);
            ZipSafety.ValidateArchive(readArchive);
        });
    }

    [Fact]
    public void AutoBackup_rotation_keeps_max_10_backups()
    {
        for (int i = 0; i < 15; i++)
        {
            _backupService.CreateAutoBackup(_configManager, $"test-{i}");
            Thread.Sleep(20); // ensure distinct timestamps
        }

        var backupFiles = Directory.GetFiles(_backupsDir, "pre-*.zip");
        Assert.True(backupFiles.Length <= 10, $"Expected <= 10 backups, found {backupFiles.Length}");
    }

    [Fact]
    public void Merge_mode_unions_items_and_resolves_collisions()
    {
        var plan = new ImportPlan
        {
            FoldersConfig = new FoldersConfiguration
            {
                Folders =
                [
                    new FolderDef
                    {
                        Id = "work",
                        Name = "Work",
                        Items =
                        [
                            new FolderItem { DisplayName = "New Work App", TargetPath = "C:\\newapp.exe" }
                        ]
                    },
                    new FolderDef
                    {
                        Id = "games",
                        Name = "Retro Gaming", // Different name for same ID
                        Items =
                        [
                            new FolderItem { DisplayName = "Emulator", TargetPath = "C:\\emu.exe" }
                        ]
                    }
                ]
            }
        };

        _backupService.ApplyImport(plan, ImportMode.Merge, _configManager, _folderManager);

        var work = _folderManager.GetFolder("work");
        Assert.NotNull(work);
        Assert.Contains(work.Items, i => i.DisplayName == "New Work App");

        // The colliding 'games' id with different name should have been added as a separate folder with regenerated ID
        Assert.Contains(_folderManager.Folders, f => f.Name == "Retro Gaming");
    }

    [Fact]
    public void PathRelinker_suggests_environment_variable_expansion()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string unresolved = "%LOCALAPPDATA%";

        string? relinked = PathRelinker.TryRelink(unresolved);
        Assert.NotNull(relinked);
        Assert.Equal(localAppData, relinked);
    }
}
