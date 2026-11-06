namespace GlassFolders.Core.Models;

public enum FolderSize
{
    Small,
    Medium,
    Large
}

/// <summary>
/// Represents a folder tile on the desktop.
/// </summary>
public sealed class FolderDef
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New Folder";
    public string ColorA { get; set; } = "#7FB2FF";
    public string ColorB { get; set; } = "#5B5CF2";
    public string Glyph { get; set; } = "Folder";
    public FolderSize Size { get; set; } = FolderSize.Medium;
    public int X { get; set; }
    public int Y { get; set; }
    public bool IsVisible { get; set; } = true;
    public List<FolderItem> Items { get; set; } = [];

    public FolderItem? FindItem(string itemId) => Items.FirstOrDefault(i => i.Id == itemId);
}
