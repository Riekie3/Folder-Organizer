namespace GlassFolders.Core.Backup;

public sealed class BackupManifest
{
    /// <summary>The newest schema this version reads. A backup is written as version 1 unless it holds item icons (version 2),
    /// so a backup without them still opens in earlier releases.</summary>
    public const int CurrentSchemaVersion = 2;

    /// <summary>Schema of a backup that carries item icons.</summary>
    public const int ItemIconsSchemaVersion = 2;

    public string AppVersion { get; set; } = "0.6.0";
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string MachineName { get; set; } = Environment.MachineName;
    public List<string> Monitors { get; set; } = [];
    public Dictionary<string, string> FileChecksums { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Folder id → archive entry holding the picture the folder shows. Empty for drawn folders.</summary>
    public Dictionary<string, string> FolderIcons { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Item id → archive entry holding the icon the user chose for that item. Empty when no item has one.</summary>
    public Dictionary<string, string> ItemIcons { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
