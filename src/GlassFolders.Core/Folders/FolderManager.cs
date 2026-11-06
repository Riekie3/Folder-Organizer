using GlassFolders.Core.Config;
using GlassFolders.Core.Models;

namespace GlassFolders.Core.Folders;

/// <summary>
/// Domain service for managing desktop folders and items.
/// Modifying or deleting folders/items never deletes the original apps or files.
/// </summary>
public sealed class FolderManager
{
    private readonly ConfigurationManager _configManager;
    private readonly FoldersConfiguration _config;

    public event Action? Changed;

    public FolderManager(ConfigurationManager configManager)
    {
        _configManager = configManager;
        _config = _configManager.LoadFolders();
    }

    public IReadOnlyList<FolderDef> Folders => _config.Folders;

    public FolderDef? GetFolder(string id) => _config.Folders.FirstOrDefault(f => f.Id == id);

    public FolderDef CreateFolder(string name, string colorA = "#7FB2FF", string colorB = "#5B5CF2", string glyph = "Folder", int x = 100, int y = 100)
    {
        var folder = new FolderDef
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            Name = name,
            ColorA = colorA,
            ColorB = colorB,
            Glyph = glyph,
            X = x,
            Y = y,
            IsVisible = true
        };
        _config.Folders.Add(folder);
        Save();
        return folder;
    }

    public bool RenameFolder(string id, string newName)
    {
        var folder = GetFolder(id);
        if (folder is null) return false;
        folder.Name = newName;
        Save();
        return true;
    }

    public bool SetFolderAppearance(string id, string colorA, string colorB, string glyph)
    {
        var folder = GetFolder(id);
        if (folder is null) return false;
        folder.ColorA = colorA;
        folder.ColorB = colorB;
        folder.Glyph = glyph;
        Save();
        return true;
    }

    public bool SetFolderPosition(string id, int x, int y)
    {
        var folder = GetFolder(id);
        if (folder is null) return false;
        folder.X = x;
        folder.Y = y;
        Save();
        return true;
    }

    public bool SetFolderVisibility(string id, bool isVisible)
    {
        var folder = GetFolder(id);
        if (folder is null) return false;
        folder.IsVisible = isVisible;
        Save();
        return true;
    }

    public bool DeleteFolder(string id)
    {
        var folder = GetFolder(id);
        if (folder is null) return false;
        _config.Folders.Remove(folder);
        Save();
        return true;
    }

    public bool AddItem(string folderId, FolderItem item)
    {
        var folder = GetFolder(folderId);
        if (folder is null) return false;
        folder.Items.Add(item);
        Save();
        return true;
    }

    public bool RemoveItem(string folderId, string itemId)
    {
        var folder = GetFolder(folderId);
        if (folder is null) return false;
        var item = folder.FindItem(itemId);
        if (item is null) return false;
        folder.Items.Remove(item);
        Save();
        return true;
    }

    public bool ReorderItem(string folderId, int oldIndex, int newIndex)
    {
        var folder = GetFolder(folderId);
        if (folder is null || oldIndex < 0 || oldIndex >= folder.Items.Count || newIndex < 0 || newIndex >= folder.Items.Count)
            return false;

        var item = folder.Items[oldIndex];
        folder.Items.RemoveAt(oldIndex);
        folder.Items.Insert(newIndex, item);
        Save();
        return true;
    }

    private void Save()
    {
        _configManager.SaveFolders(_config);
        Changed?.Invoke();
    }
}
