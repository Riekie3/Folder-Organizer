using System.IO.Compression;
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
}

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
        using (var reader = new StreamReader(manifestEntry.Open()))
        {
            string json = reader.ReadToEnd();
            manifest = JsonSerializer.Deserialize(json, ConfigurationJsonContext.Default.BackupManifest)
                ?? throw new InvalidOperationException("Failed to parse 'manifest.json' from backup archive.");
        }

        // 2. Read folders.json
        var foldersEntry = archive.GetEntry("folders.json")
            ?? throw new InvalidOperationException("Backup archive is missing 'folders.json'.");

        FoldersConfiguration? foldersConfig;
        using (var reader = new StreamReader(foldersEntry.Open()))
        {
            string json = reader.ReadToEnd();
            foldersConfig = JsonSerializer.Deserialize(json, ConfigurationJsonContext.Default.FoldersConfiguration)
                ?? throw new InvalidOperationException("Failed to parse 'folders.json' from backup archive.");
        }

        // 3. Read settings.json
        var settingsEntry = archive.GetEntry("settings.json");
        AppSettings settings = new();
        if (settingsEntry != null)
        {
            using var reader = new StreamReader(settingsEntry.Open());
            string json = reader.ReadToEnd();
            settings = JsonSerializer.Deserialize(json, ConfigurationJsonContext.Default.AppSettings) ?? new();
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

        // 5. Evaluate missing targets & ID collisions
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

        return new ImportPlan
        {
            Manifest = manifest,
            FoldersConfig = foldersConfig,
            Settings = settings,
            MissingTargetsCount = missingCount,
            CollidingFoldersCount = collidingNames.Count,
            CollidingFolderNames = collidingNames
        };
    }
}
