using GlassFolders.Core.Models;

namespace GlassFolders.Core.Config;

/// <summary>
/// Top-level serialized structure for folders storage.
/// </summary>
public sealed class FoldersConfiguration
{
    public int SchemaVersion { get; set; } = 1;
    public List<FolderDef> Folders { get; set; } = [];
}
