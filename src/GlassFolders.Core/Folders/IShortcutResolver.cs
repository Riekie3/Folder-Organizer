using GlassFolders.Core.Models;

namespace GlassFolders.Core.Folders;

/// <summary>
/// Abstraction for resolving local paths, shortcuts (.lnk), internet shortcuts (.url), and apps into FolderItem.
/// </summary>
public interface IShortcutResolver
{
    FolderItem Resolve(string path);
    bool TargetExists(FolderItem item);
}
