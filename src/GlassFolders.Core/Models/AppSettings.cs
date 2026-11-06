namespace GlassFolders.Core.Models;

/// <summary>
/// User settings for GlassFolders.
/// </summary>
public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 1;
    public int PopupColumns { get; set; } = 4;
    public int PopupRows { get; set; } = 3;
    public bool SnapToGrid { get; set; } = true;
    public int GridSize { get; set; } = 20;
    public bool StartWithWindows { get; set; } = true;
    public int StartupDelaySeconds { get; set; } = 0;
    public string LogLevel { get; set; } = "Info";
    public bool CheckMissingTargets { get; set; } = true;
}
