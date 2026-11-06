using System.IO.Compression;
using System.Text;
using System.Text.Json;
using GlassFolders.Core.Config;
using GlassFolders.Core.Folders;
using GlassFolders.Core.Models;

namespace GlassFolders.Core.Backup;

/// <summary>
/// Domain service responsible for portable ZIP backup creation, checksum verification,
/// automated rolling backups, and archive restoration.
/// </summary>
public sealed class BackupService
{
    private const int MaxAutoBackups = 10;
    private readonly string _backupsDirectory;

    public string BackupsDirectory => _backupsDirectory;

    public BackupService(string? baseDir = null)
    {
        string root = baseDir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GlassFolders");
        _backupsDirectory = Path.Combine(root, "Backups");
        try { Directory.CreateDirectory(_backupsDirectory); } catch { }
    }

    /// <summary>
    /// Exports current configuration, folder definitions, and manifest into a portable ZIP archive.
    /// </summary>
    public void Export(
        string zipFilePath,
        ConfigurationManager configManager,
        IReadOnlyList<string>? monitorKeys = null,
        string? iconCacheDir = null)
    {
        var foldersConfig = configManager.LoadFolders();
        var settings = configManager.LoadSettings();

        string foldersJson = JsonSerializer.Serialize(foldersConfig, ConfigurationJsonContext.Default.FoldersConfiguration);
        string settingsJson = JsonSerializer.Serialize(settings, ConfigurationJsonContext.Default.AppSettings);

        byte[] foldersBytes = Encoding.UTF8.GetBytes(foldersJson);
        byte[] settingsBytes = Encoding.UTF8.GetBytes(settingsJson);

        var manifest = new BackupManifest
        {
            AppVersion = "0.6.0",
            SchemaVersion = 1,
            CreatedAt = DateTime.UtcNow,
            MachineName = Environment.MachineName,
            Monitors = monitorKeys?.ToList() ?? []
        };

        manifest.FileChecksums["folders.json"] = ZipSafety.ComputeSha256(foldersBytes);
        manifest.FileChecksums["settings.json"] = ZipSafety.ComputeSha256(settingsBytes);

        // Delete target if already exists
        if (File.Exists(zipFilePath)) File.Delete(zipFilePath);

        using var zipStream = File.Create(zipFilePath);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Create);

        // 1. Write folders.json
        var fEntry = archive.CreateEntry("folders.json", CompressionLevel.Optimal);
        using (var s = fEntry.Open()) s.Write(foldersBytes);

        // 2. Write settings.json
        var sEntry = archive.CreateEntry("settings.json", CompressionLevel.Optimal);
        using (var s = sEntry.Open()) s.Write(settingsBytes);

        // 3. Write icons if present
        if (!string.IsNullOrEmpty(iconCacheDir) && Directory.Exists(iconCacheDir))
        {
            foreach (string file in Directory.GetFiles(iconCacheDir, "*.png"))
            {
                byte[] iconBytes = File.ReadAllBytes(file);
                string entryName = "icons/" + Path.GetFileName(file);
                manifest.FileChecksums[entryName] = ZipSafety.ComputeSha256(iconBytes);

                var iEntry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
                using var s = iEntry.Open();
                s.Write(iconBytes);
            }
        }

        // 4. Write manifest.json
        string manifestJson = JsonSerializer.Serialize(manifest, ConfigurationJsonContext.Default.BackupManifest);
        byte[] manifestBytes = Encoding.UTF8.GetBytes(manifestJson);
        var mEntry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
        using (var s = mEntry.Open()) s.Write(manifestBytes);
    }

    /// <summary>
    /// Creates an automatic pre-import/pre-reset backup and prunes old backups beyond the 10 most recent.
    /// </summary>
    public string CreateAutoBackup(ConfigurationManager configManager, string reason = "auto")
    {
        string timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        string fileName = $"pre-{reason}-{timestamp}.zip";
        string targetPath = Path.Combine(_backupsDirectory, fileName);

        Export(targetPath, configManager);
        PruneOldAutoBackups();
        return targetPath;
    }

    private void PruneOldAutoBackups()
    {
        try
        {
            var files = Directory.GetFiles(_backupsDirectory, "pre-*.zip")
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.CreationTimeUtc)
                .ToList();

            for (int i = MaxAutoBackups; i < files.Count; i++)
            {
                files[i].Delete();
            }
        }
        catch { }
    }

    /// <summary>
    /// Applies an import plan using either Replace or Merge mode.
    /// Always creates an automatic backup before modifying configuration.
    /// </summary>
    public void ApplyImport(
        ImportPlan plan,
        ImportMode mode,
        ConfigurationManager configManager,
        FolderManager folderManager)
    {
        // 1. Safety auto-backup before modifying state
        CreateAutoBackup(configManager, mode == ImportMode.Replace ? "replace" : "merge");

        if (mode == ImportMode.Replace)
        {
            configManager.SaveFolders(plan.FoldersConfig);
            configManager.SaveSettings(plan.Settings);
            folderManager.Reload();
            return;
        }

        // 2. Merge mode: union folders and unique items
        var currentConfig = configManager.LoadFolders();
        foreach (var importedFolder in plan.FoldersConfig.Folders)
        {
            var existing = currentConfig.Folders.FirstOrDefault(f => f.Id == importedFolder.Id);
            if (existing != null)
            {
                if (string.Equals(existing.Name, importedFolder.Name, StringComparison.OrdinalIgnoreCase))
                {
                    // Same ID and same name: merge items
                    foreach (var item in importedFolder.Items)
                    {
                        if (!existing.Items.Any(i => string.Equals(i.TargetPath, item.TargetPath, StringComparison.OrdinalIgnoreCase)))
                        {
                            existing.Items.Add(item);
                        }
                    }
                }
                else
                {
                    // Same ID but different name: regenerate ID
                    importedFolder.Id = Guid.NewGuid().ToString("N")[..8];
                    currentConfig.Folders.Add(importedFolder);
                }
            }
            else
            {
                currentConfig.Folders.Add(importedFolder);
            }
        }

        configManager.SaveFolders(currentConfig);
        folderManager.Reload();
    }
}
