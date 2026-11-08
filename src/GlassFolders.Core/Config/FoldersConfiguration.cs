using GlassFolders.Core.Models;

namespace GlassFolders.Core.Config;

/// <summary>
/// Top-level serialized structure for folders storage.
/// </summary>
public sealed class FoldersConfiguration
{
    /// <summary>2: the old preset colours were replaced by the reference palette.</summary>
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public List<FolderDef> Folders { get; set; } = [];
}
