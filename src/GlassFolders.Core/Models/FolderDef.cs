namespace GlassFolders.Core.Models;

public enum FolderSize
{
    Small,
    Medium,
    Large
}

/// <summary>How the folder art is drawn.</summary>
public enum FolderStyle
{
    /// <summary>Solid coloured folder with a white glyph (reference 1). The default.</summary>
    Solid,

    /// <summary>Coloured back with a frosted front flap and a soft glow (reference 4).</summary>
    Glass,
}

/// <summary>
/// Represents a folder tile on the desktop.
/// </summary>
public sealed class FolderDef
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New Folder";
    public string ColorA { get; set; } = FolderPalette.Default.Front;
    public string ColorB { get; set; } = FolderPalette.Default.Back;
    public string Glyph { get; set; } = "Folder";
    public FolderSize Size { get; set; } = FolderSize.Medium;
    public FolderStyle Style { get; set; } = FolderStyle.Solid;

    /// <summary>
    /// A picture the user chose to show instead of the drawn folder: the app's own copy of it, never the
    /// original file. Null draws the folder.
    /// </summary>
    public string? CustomIconPath { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public bool IsVisible { get; set; } = true;
    public List<FolderItem> Items { get; set; } = [];

    public FolderItem? FindItem(string itemId) => Items.FirstOrDefault(i => i.Id == itemId);
}
