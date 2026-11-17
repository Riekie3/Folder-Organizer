using System.IO.Compression;
using System.Text;
using System.Text.Json;
using GlassFolders.Core.Config;
using GlassFolders.Core.Folders;
using GlassFolders.Core.Models;

namespace GlassFolders.Core.Backup;

public enum ImportMode
{
    Replace,
    Merge
}

public sealed class ImportPlan
{
    public BackupManifest Manifest { get; set; } = new();
    public FoldersConfiguration FoldersConfig { get; set; } = new();
    public AppSettings Settings { get; set; } = new();
    public int TotalFolders => FoldersConfig.Folders.Count;
    public int TotalItems => FoldersConfig.Folders.Sum(f => f.Items.Count);
    public int MissingTargetsCount { get; set; }
    public int CollidingFoldersCount { get; set; }
    public List<string> CollidingFolderNames { get; set; } = [];

    /// <summary>Folder id → picture bytes read from the archive. Applied by <see cref="BackupService.ApplyImport"/>.</summary>
    public Dictionary<string, FolderPicture> FolderPictures { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Item id → icon bytes read from the archive. Applied by <see cref="BackupService.ApplyImport"/>.</summary>
    public Dictionary<string, FolderPicture> ItemPictures { get; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>A folder picture from a backup, with the image extension it was stored under.</summary>
public sealed record FolderPicture(byte[] Bytes, string Extension);

public static class ImportPlanner
{
    public static ImportPlan CreatePlan(string zipPath, FolderManager currentFolders)
    {
        using var stream = File.OpenRead(zipPath);
        return CreatePlan(stream, currentFolders);
    }

    public static ImportPlan CreatePlan(Stream zipStream, FolderManager currentFolders)
    {
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: true);
        ZipSafety.ValidateArchive(archive);

        // 1. Read manifest.json
        var manifestEntry = archive.GetEntry("manifest.json")
            ?? throw new InvalidOperationException("Backup archive is missing 'manifest.json'.");

        BackupManifest? manifest;
        string manifestJson = Encoding.UTF8.GetString(ZipSafety.ReadAllBytes(manifestEntry, ZipSafety.MaxTotalSizeBytes));
        manifest = JsonSerializer.Deserialize(manifestJson, ConfigurationJsonContext.Default.BackupManifest)
            ?? throw new InvalidOperationException("Failed to parse 'manifest.json' from backup archive.");

        if (manifest.SchemaVersion < 1 || manifest.SchemaVersion > BackupManifest.CurrentSchemaVersion)
        {
            throw new InvalidOperationException(
                $"Backup schema version {manifest.SchemaVersion} is not supported by this version of GlassFolders.");
        }

        // 2. Read folders.json
        var foldersEntry = archive.GetEntry("folders.json")
            ?? throw new InvalidOperationException("Backup archive is missing 'folders.json'.");

        FoldersConfiguration? foldersConfig;
        string foldersJson = Encoding.UTF8.GetString(ZipSafety.ReadAllBytes(foldersEntry, ZipSafety.MaxTotalSizeBytes));
        foldersConfig = JsonSerializer.Deserialize(foldersJson, ConfigurationJsonContext.Default.FoldersConfiguration)
            ?? throw new InvalidOperationException("Failed to parse 'folders.json' from backup archive.");

        // 3. Read settings.json
        var settingsEntry = archive.GetEntry("settings.json");
        AppSettings settings = new();
        if (settingsEntry != null)
        {
            string settingsJson = Encoding.UTF8.GetString(ZipSafety.ReadAllBytes(settingsEntry, ZipSafety.MaxTotalSizeBytes));
            settings = JsonSerializer.Deserialize(settingsJson, ConfigurationJsonContext.Default.AppSettings) ?? new();
        }

        // 4. Verify file checksums from manifest
        foreach (var (fileName, expectedHash) in manifest.FileChecksums)
        {
            var entry = archive.GetEntry(fileName);
            if (entry != null)
            {
                using var entryStream = entry.Open();
                string actualHash = ZipSafety.ComputeSha256(entryStream);
                if (!string.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Checksum mismatch for entry '{fileName}': expected {expectedHash}, got {actualHash}. Archive may be corrupted or tampered.");
                }
            }
        }

        // 5. Read folder pictures. Their paths are remapped when the plan is applied, so a backup
        // restores the pictures even when the app folder is on another PC.
        var folderPictures = new Dictionary<string, FolderPicture>(StringComparer.OrdinalIgnoreCase);
        foreach (var (folderId, entryName) in manifest.FolderIcons)
        {
            var entry = archive.GetEntry(entryName);
            if (entry is null) continue;

            string extension = Path.GetExtension(entryName).ToLowerInvariant();
            if (!ZipSafety.IsPictureExtension(extension)) continue;
            if (entry.Length <= 0 || entry.Length > ZipSafety.MaxPictureBytes) continue;

            byte[] bytes = ZipSafety.ReadAllBytes(entry, ZipSafety.MaxPictureBytes);
            if (bytes.Length == 0) continue;
            folderPictures[folderId] = new FolderPicture(bytes, extension);
        }

        // A CustomIconPath from the exporting PC means nothing here. Only a picture that actually came with
        // the archive can be restored; the rest fall back to the drawn folder (or, in Merge, keep theirs).
        foreach (var folder in foldersConfig.Folders)
        {
            if (!folderPictures.ContainsKey(folder.Id)) folder.CustomIconPath = null;
        }

        // Item icons work the same way, with the smaller limit that applies to them.
        var itemPictures = new Dictionary<string, FolderPicture>(StringComparer.OrdinalIgnoreCase);
        foreach (var (itemId, entryName) in manifest.ItemIcons)
        {
            var entry = archive.GetEntry(entryName);
            if (entry is null) continue;

            string extension = Path.GetExtension(entryName).ToLowerInvariant();
            if (!ZipSafety.IsPictureExtension(extension)) continue;
            if (entry.Length <= 0 || entry.Length > Icons.ItemIconFiles.MaxBytes) continue;

            byte[] bytes = ZipSafety.ReadAllBytes(entry, Icons.ItemIconFiles.MaxBytes);
            if (bytes.Length == 0) continue;
            itemPictures[itemId] = new FolderPicture(bytes, extension);
        }

        foreach (var item in foldersConfig.Folders.SelectMany(f => f.Items))
        {
            if (!itemPictures.ContainsKey(item.Id)) item.CustomIconPath = null;
        }

        // 6. Evaluate missing targets & ID collisions
        int missingCount = 0;
        foreach (var folder in foldersConfig.Folders)
        {
            foreach (var item in folder.Items)
            {
                if (!File.Exists(item.TargetPath) && !Directory.Exists(item.TargetPath))
                {
                    string? relinked = PathRelinker.TryRelink(item.TargetPath);
                    if (relinked == null) missingCount++;
                }
            }
        }

        var collidingNames = new List<string>();
        foreach (var folder in foldersConfig.Folders)
        {
            var existing = currentFolders.GetFolder(folder.Id);
            if (existing != null)
            {
                collidingNames.Add(folder.Name);
            }
        }

        var plan = new ImportPlan
        {
            Manifest = manifest,
            FoldersConfig = foldersConfig,
            Settings = settings,
            MissingTargetsCount = missingCount,
            CollidingFoldersCount = collidingNames.Count,
            CollidingFolderNames = collidingNames
        };
        foreach (var (folderId, picture) in folderPictures)
            plan.FolderPictures[folderId] = picture;
        foreach (var (itemId, picture) in itemPictures)
            plan.ItemPictures[itemId] = picture;
        return plan;
    }
}
