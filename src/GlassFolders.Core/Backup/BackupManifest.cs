namespace GlassFolders.Core.Backup;

public sealed class BackupManifest
{
    public const int CurrentSchemaVersion = 1;

    public string AppVersion { get; set; } = "0.6.0";
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string MachineName { get; set; } = Environment.MachineName;
    public List<string> Monitors { get; set; } = [];
    public Dictionary<string, string> FileChecksums { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Folder id → archive entry holding the picture the folder shows. Empty for drawn folders.</summary>
    public Dictionary<string, string> FolderIcons { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
