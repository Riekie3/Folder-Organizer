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

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name);
        using var stream = entry.Open();
        stream.Write(Encoding.UTF8.GetBytes(content));
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
    public void ZipSafety_rejects_unexpected_file_types()
    {
        string archivePath = Path.Combine(_testDir, "notes.zip");
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("readme.txt");
            using var s = entry.Open();
            s.Write(Encoding.UTF8.GetBytes("not part of a backup"));
        }

        Assert.Throws<InvalidOperationException>(() =>
        {
            using var readArchive = ZipFile.OpenRead(archivePath);
            ZipSafety.ValidateArchive(readArchive);
        });
    }

    [Fact]
    public void ZipSafety_rejects_a_decompression_bomb()
    {
        string archivePath = Path.Combine(_testDir, "bomb.zip");
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            // A highly compressible "picture" that expands far beyond the allowed ratio.
            var entry = archive.CreateEntry("folder-icons/ratio.png", CompressionLevel.Optimal);
            using var s = entry.Open();
            s.Write(new byte[1024 * 1024]);
        }

        Assert.Throws<InvalidOperationException>(() =>
        {
            using var readArchive = ZipFile.OpenRead(archivePath);
            ZipSafety.ValidateArchive(readArchive);
        });
    }

    [Fact]
    public void Folder_picture_is_exported_and_restored_into_the_new_app_folder()
    {
        var folder = _folderManager.CreateFolder("Pictures", "#123456", "#654321", "Palette", 250, 350);
        byte[] original = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        string picturePath = Path.Combine(_testDir, "folder-picture.png");
        File.WriteAllBytes(picturePath, original);
        _folderManager.SetCustomIcon(folder.Id, picturePath);

        string zipPath = Path.Combine(_testDir, "with-picture.zip");
        _backupService.Export(zipPath, _configManager);

        string freshDir = Path.Combine(Path.GetTempPath(), "gf-pictures-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(freshDir);
        try
        {
            var freshConfig = new ConfigurationManager(freshDir);
            var freshFolderManager = new FolderManager(freshConfig);
            var importService = new BackupService(freshDir);

            var plan = ImportPlanner.CreatePlan(zipPath, freshFolderManager);
            Assert.True(plan.FolderPictures.ContainsKey(folder.Id));
            Assert.True(plan.Manifest.FolderIcons.ContainsKey(folder.Id));

            importService.ApplyImport(plan, ImportMode.Replace, freshConfig, freshFolderManager);

            var restored = freshFolderManager.Folders.First(f => f.Id == folder.Id);
            Assert.NotNull(restored.CustomIconPath);
            Assert.StartsWith(Path.Combine(freshDir, "folder-icons"), restored.CustomIconPath, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(restored.CustomIconPath));
            Assert.Equal(original, File.ReadAllBytes(restored.CustomIconPath));
        }
        finally
        {
            if (Directory.Exists(freshDir))
                Directory.Delete(freshDir, recursive: true);
        }
    }

    [Fact]
    public void Folder_without_a_picture_still_restores_drawn()
    {
        string zipPath = Path.Combine(_testDir, "no-picture.zip");
        _backupService.Export(zipPath, _configManager);

        var plan = ImportPlanner.CreatePlan(zipPath, _folderManager);
        Assert.Empty(plan.FolderPictures);
        Assert.All(plan.FoldersConfig.Folders, f => Assert.Null(f.CustomIconPath));
    }

    [Fact]
    public void A_missing_picture_is_not_imported_as_a_dead_path()
    {
        var folder = _folderManager.CreateFolder("Pictures", "#123456", "#654321", "Palette", 250, 350);
        string picturePath = Path.Combine(_testDir, "folder-picture.png");
        File.WriteAllBytes(picturePath, [1, 2, 3, 4]);
        _folderManager.SetCustomIcon(folder.Id, picturePath);

        string? copied = _folderManager.GetFolder(folder.Id)?.CustomIconPath;
        Assert.NotNull(copied);
        File.Delete(copied!);

        string zipPath = Path.Combine(_testDir, "missing-picture.zip");
        _backupService.Export(zipPath, _configManager);

        var plan = ImportPlanner.CreatePlan(zipPath, _folderManager);
        Assert.Empty(plan.FolderPictures);
        Assert.Null(plan.FoldersConfig.Folders.Single(f => f.Id == folder.Id).CustomIconPath);
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
    public void Import_rejects_a_newer_schema_version()
    {
        string archivePath = Path.Combine(_testDir, "newer-schema.zip");
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            WriteEntry(archive, "manifest.json",
                """{"appVersion":"9.0.0","schemaVersion":99,"machineName":"TEST","monitors":[],"fileChecksums":{},"folderIcons":{}}""");
            WriteEntry(archive, "folders.json", """{"schemaVersion":2,"folders":[]}""");
        }

        var error = Assert.Throws<InvalidOperationException>(() =>
            ImportPlanner.CreatePlan(archivePath, _folderManager));
        Assert.Contains("not supported", error.Message);
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
