namespace GlassFolders.Core.Models;

/// <summary>A folder colour: the front face and the darker back with its tab.</summary>
public readonly record struct FolderColor(string Name, string Front, string Back);

/// <summary>
/// Folder colours. The first four are measured from the reference's Work, Games, Media and Utilities folders.
/// </summary>
public static class FolderPalette
{
    public static readonly FolderColor Blue = new("Blue", "#64D5FD", "#1F9DFB");
    public static readonly FolderColor Purple = new("Purple", "#A364FC", "#771EF0");
    public static readonly FolderColor Coral = new("Coral", "#FC8590", "#F13B45");
    public static readonly FolderColor Green = new("Green", "#90DB4D", "#54B92C");

    public static FolderColor Default => Blue;

    public static IReadOnlyList<FolderColor> All { get; } =
    [
        Blue,
        Purple,
        Coral,
        Green,
        new("Amber", "#FFC94D", "#F59E0B"),
        new("Teal", "#5EE0D6", "#0FA3A3"),
        new("Pink", "#FF8AC8", "#E83E8C"),
        new("Violet", "#7FB2FF", "#5B5CF2"),
    ];

    /// <summary>Colours given to new folders in turn, so a fresh column reads like the reference.</summary>
    public static FolderColor ForNewFolder(int existingFolders) => All[Math.Max(0, existingFolders) % 4];
}
