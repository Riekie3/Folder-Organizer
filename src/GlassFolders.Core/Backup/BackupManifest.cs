namespace GlassFolders.Core.Backup;

public sealed class BackupManifest
{
    public string AppVersion { get; set; } = "0.6.0";
    public int SchemaVersion { get; set; } = 1;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string MachineName { get; set; } = Environment.MachineName;
    public List<string> Monitors { get; set; } = [];
    public Dictionary<string, string> FileChecksums { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
