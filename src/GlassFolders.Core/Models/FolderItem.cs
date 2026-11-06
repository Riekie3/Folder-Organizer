namespace GlassFolders.Core.Models;

public enum FolderItemType
{
    Shortcut,
    Executable,
    StoreApp,
    File,
    Folder,
    Url
}

/// <summary>
/// Represents one item (app, shortcut, file, folder, URL) stored inside a GlassFolders folder.
/// Removing an item from a folder never deletes the underlying file or application.
/// </summary>
public sealed class FolderItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public FolderItemType Type { get; set; } = FolderItemType.Shortcut;
    public string DisplayName { get; set; } = "";
    public string TargetPath { get; set; } = "";
    public string? ShortcutPath { get; set; }
    public string? Arguments { get; set; }
    public string? WorkingDirectory { get; set; }
    public string? IconLocation { get; set; }
    public int IconIndex { get; set; }
    public string? Aumid { get; set; }
}
