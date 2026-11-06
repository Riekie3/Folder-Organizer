using GlassFolders.Core.Config;
using GlassFolders.Core.Folders;
using GlassFolders.Core.Models;

namespace GlassFolders.Core.Tests;

public sealed class FolderManagerTests : IDisposable
{
    private readonly string _testDir;
    private readonly ConfigurationManager _configManager;
    private readonly FolderManager _folderManager;

    public FolderManagerTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "gf-fm-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
        _configManager = new ConfigurationManager(_testDir);
        _folderManager = new FolderManager(_configManager);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, recursive: true);
    }

    [Fact]
    public void Initial_load_seeds_default_folders()
    {
        var folders = _folderManager.Folders;
        Assert.Equal(3, folders.Count);
        Assert.Contains(folders, f => f.Id == "work" && f.Name == "Work");
        Assert.Contains(folders, f => f.Id == "games" && f.Name == "Games");
        Assert.Contains(folders, f => f.Id == "media" && f.Name == "Media");
    }

    [Fact]
    public void Work_folder_contains_default_items()
    {
        var work = _folderManager.GetFolder("work");
        Assert.NotNull(work);
        Assert.NotEmpty(work.Items);
        Assert.Contains(work.Items, i => i.DisplayName == "File Explorer");
    }

    [Fact]
    public void Create_folder_persists_and_can_be_retrieved()
    {
        var folder = _folderManager.CreateFolder("Development", "#4ADE80", "#16A34A", "Code", 250, 350);
        Assert.NotNull(folder);
        Assert.Equal("Development", folder.Name);

        // Reload fresh from disk via new FolderManager
        var freshManager = new FolderManager(_configManager);
        var loaded = freshManager.GetFolder(folder.Id);
        Assert.NotNull(loaded);
        Assert.Equal("Development", loaded.Name);
        Assert.Equal(250, loaded.X);
        Assert.Equal(350, loaded.Y);
    }

    [Fact]
    public void Rename_folder_updates_name()
    {
        var folder = _folderManager.CreateFolder("OldName");
        bool renamed = _folderManager.RenameFolder(folder.Id, "NewName");
        Assert.True(renamed);
        Assert.Equal("NewName", folder.Name);

        var freshManager = new FolderManager(_configManager);
        Assert.Equal("NewName", freshManager.GetFolder(folder.Id)!.Name);
    }

    [Fact]
    public void Delete_folder_removes_from_list_without_affecting_others()
    {
        var folder = _folderManager.CreateFolder("ToDelete");
        Assert.NotNull(_folderManager.GetFolder(folder.Id));

        bool deleted = _folderManager.DeleteFolder(folder.Id);
        Assert.True(deleted);
        Assert.Null(_folderManager.GetFolder(folder.Id));

        // Verify other folders still exist
        Assert.NotNull(_folderManager.GetFolder("work"));
    }

    [Fact]
    public void Add_and_remove_item_manages_folder_contents()
    {
        var item = new FolderItem
        {
            DisplayName = "Test App",
            TargetPath = "C:\\Windows\\test.exe",
            Type = FolderItemType.Executable
        };

        bool added = _folderManager.AddItem("work", item);
        Assert.True(added);

        var work = _folderManager.GetFolder("work")!;
        Assert.Contains(work.Items, i => i.Id == item.Id);

        bool removed = _folderManager.RemoveItem("work", item.Id);
        Assert.True(removed);
        Assert.DoesNotContain(work.Items, i => i.Id == item.Id);
    }

    [Fact]
    public void Reorder_item_swaps_positions()
    {
        var work = _folderManager.GetFolder("work")!;
        int initialCount = work.Items.Count;
        Assert.True(initialCount >= 2);

        var first = work.Items[0];
        var second = work.Items[1];

        bool reordered = _folderManager.ReorderItem("work", 0, 1);
        Assert.True(reordered);
        Assert.Equal(second.Id, work.Items[0].Id);
        Assert.Equal(first.Id, work.Items[1].Id);
    }

    [Fact]
    public void Set_folder_position_updates_coordinates()
    {
        _folderManager.SetFolderPosition("games", 450, 600);
        var games = _folderManager.GetFolder("games")!;
        Assert.Equal(450, games.X);
        Assert.Equal(600, games.Y);

        var freshManager = new FolderManager(_configManager);
        var loaded = freshManager.GetFolder("games")!;
        Assert.Equal(450, loaded.X);
        Assert.Equal(600, loaded.Y);
    }

    [Fact]
    public void Set_folder_appearance_updates_colors_and_glyph()
    {
        _folderManager.SetFolderAppearance("media", "#123456", "#654321", "Play");
        var media = _folderManager.GetFolder("media")!;
        Assert.Equal("#123456", media.ColorA);
        Assert.Equal("#654321", media.ColorB);
        Assert.Equal("Play", media.Glyph);
    }
}
