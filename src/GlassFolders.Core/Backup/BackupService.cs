using System.IO.Compression;
using System.Text;
using System.Text.Json;
using GlassFolders.Core.Config;
using GlassFolders.Core.Folders;
using GlassFolders.Core.Models;
using GlassFolders.Core.Storage;

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

    /// <summary>Where imported folder pictures are written. The app reads folder pictures from the same folder.</summary>
    public string FolderIconsDirectory { get; }

    public BackupService(string? baseDir = null)
    {
        string root = baseDir ?? AppPaths.BaseAppDataDir;
        _backupsDirectory = Path.Combine(root, "Backups");
        FolderIconsDirectory = Path.Combine(root, "folder-icons");
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
            AppVersion = CurrentAppVersion(),
            SchemaVersion = BackupManifest.CurrentSchemaVersion,
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

        // 4. Write the pictures folders show instead of the drawn folder. The manifest maps folder id → entry,
        // because the original path only makes sense on this PC.
        foreach (var folder in foldersConfig.Folders)
        {
            string? picturePath = folder.CustomIconPath;
            if (string.IsNullOrWhiteSpace(picturePath)) continue;

            try
            {
                var info = new FileInfo(picturePath);
                string extension = info.Extension.ToLowerInvariant();
                if (!info.Exists || info.Length == 0 || info.Length > ZipSafety.MaxPictureBytes) continue;
                if (!ZipSafety.IsPictureExtension(extension)) continue;

                byte[] pictureBytes = File.ReadAllBytes(info.FullName);
                string entryName = "folder-icons/" + PictureKey(folder.Id) + extension;
                if (!manifest.FileChecksums.TryAdd(entryName, ZipSafety.ComputeSha256(pictureBytes))) continue;

                manifest.FolderIcons[folder.Id] = entryName;
                var pEntry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
                using var s = pEntry.Open();
                s.Write(pictureBytes);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A locked or unreadable picture should not fail the whole backup. The folder keeps its
                // drawn look after a restore instead.
            }
        }

        // 5. Write manifest.json
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

        // 2. Restore any folder pictures into the app's own folder and point the imported folders at them.
        // This happens before the configuration is saved, so a failed write leaves the old setup untouched.
        foreach (var folder in plan.FoldersConfig.Folders)
        {
            if (plan.FolderPictures.TryGetValue(folder.Id, out var picture))
                folder.CustomIconPath = SavePicture(folder.Id, picture);
        }

        if (mode == ImportMode.Replace)
        {
            configManager.SaveFolders(plan.FoldersConfig);
            configManager.SaveSettings(plan.Settings);
            folderManager.Reload();
            return;
        }

        // 3. Merge mode: union folders and unique items
        var currentConfig = configManager.LoadFolders();
        foreach (var importedFolder in plan.FoldersConfig.Folders)
        {
            var existing = currentConfig.Folders.FirstOrDefault(f => f.Id == importedFolder.Id);
            if (existing != null)
            {
                // Restore the imported picture too; a folder without one keeps the picture it already shows.
                if (!string.IsNullOrEmpty(importedFolder.CustomIconPath))
                    existing.CustomIconPath = importedFolder.CustomIconPath;

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

    private string SavePicture(string folderId, FolderPicture picture)
    {
        Directory.CreateDirectory(FolderIconsDirectory);
        string extension = ZipSafety.IsPictureExtension(picture.Extension) ? picture.Extension : ".png";
        string path = Path.Combine(FolderIconsDirectory, $"{PictureKey(folderId)}-{DateTime.UtcNow.Ticks}{extension}");
        AtomicFile.WriteAllBytes(path, picture.Bytes);
        return path;
    }

    // A short, filesystem-safe name derived from the folder id. Keeps archive entries independent of the
    // user's own file names and safe even if an imported id contains path characters.
    private static string PictureKey(string folderId) =>
        ZipSafety.ComputeSha256(Encoding.UTF8.GetBytes(folderId))[..16];

    private static string CurrentAppVersion() =>
        typeof(BackupService).Assembly.GetName().Version is { } version
            ? $"{version.Major}.{version.Minor}.{version.Build}"
            : "0.0.0";
}
