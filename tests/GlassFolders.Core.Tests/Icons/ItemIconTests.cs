using System.IO.Compression;
using System.Text;
using GlassFolders.Core.Backup;
using GlassFolders.Core.Config;
using GlassFolders.Core.Folders;
using GlassFolders.Core.Icons;
using GlassFolders.Core.Models;

namespace GlassFolders.Core.Tests.Icons;

public sealed class ItemIconFilesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "gf-itemicon-files-" + Guid.NewGuid().ToString("N"));

    public ItemIconFilesTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private string Source(string name, byte[] bytes)
    {
        string path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Fact]
    public void Store_copies_the_picture_into_the_directory_and_leaves_the_original()
    {
        string source = Source("logo.PNG", [1, 2, 3, 4]);
        string target = Path.Combine(_dir, "icons");

        string copy = ItemIconFiles.Store(source, "abc123", target);

        Assert.StartsWith(Path.GetFullPath(target), Path.GetFullPath(copy), StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(".png", copy);
        Assert.Contains("abc123", Path.GetFileName(copy));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(copy));
        Assert.True(File.Exists(source));
    }

    [Fact]
    public void Store_gives_each_choice_its_own_file_so_the_cache_can_tell_them_apart()
    {
        string source = Source("a.png", [1, 2, 3]);
        string first = ItemIconFiles.Store(source, "item", Path.Combine(_dir, "icons"));
        Thread.Sleep(5);
        string second = ItemIconFiles.Store(source, "item", Path.Combine(_dir, "icons"));
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Store_keeps_an_odd_item_id_from_escaping_the_directory()
    {
        string source = Source("a.png", [1, 2, 3]);
        string target = Path.Combine(_dir, "icons");

        string copy = ItemIconFiles.Store(source, @"..\..\evil", target);

        Assert.Equal(Path.GetFullPath(target), Path.GetDirectoryName(Path.GetFullPath(copy)));
    }

    [Theory]
    [InlineData("logo.exe")]
    [InlineData("notes.txt")]
    [InlineData("script.ps1")]
    [InlineData("noextension")]
    public void Store_rejects_files_that_are_not_pictures(string name)
    {
        string source = Source(name, [1, 2, 3]);
        Assert.Throws<InvalidDataException>(() => ItemIconFiles.Store(source, "x", Path.Combine(_dir, "icons")));
    }

    [Fact]
    public void Store_rejects_a_missing_empty_or_oversized_file()
    {
        string target = Path.Combine(_dir, "icons");
        Assert.Throws<InvalidDataException>(() => ItemIconFiles.Store(Path.Combine(_dir, "gone.png"), "x", target));
        Assert.Throws<InvalidDataException>(() => ItemIconFiles.Store(Source("empty.png", []), "x", target));
        Assert.Throws<InvalidDataException>(() => ItemIconFiles.Store(Source("big.png", new byte[ItemIconFiles.MaxBytes + 1]), "x", target));
        Assert.False(Directory.Exists(target) && Directory.GetFiles(target).Length > 0);
    }

    [Fact]
    public void Store_accepts_a_file_exactly_at_the_limit()
    {
        string source = Source("limit.png", new byte[ItemIconFiles.MaxBytes]);
        Assert.True(File.Exists(ItemIconFiles.Store(source, "x", Path.Combine(_dir, "icons"))));
    }

    [Fact]
    public void DeleteCopy_removes_only_files_inside_the_directory()
    {
        string target = Path.Combine(_dir, "icons");
        string inside = ItemIconFiles.Store(Source("a.png", [1, 2]), "x", target);
        string outside = Source("keep.png", [9, 9]);

        ItemIconFiles.DeleteCopy(outside, target);
        Assert.True(File.Exists(outside));

        ItemIconFiles.DeleteCopy(inside, target);
        Assert.False(File.Exists(inside));

        ItemIconFiles.DeleteCopy(null, target);
        ItemIconFiles.DeleteCopy("", target);
        ItemIconFiles.DeleteCopy(Path.Combine(target, "..", "keep.png"), target);
        Assert.True(File.Exists(outside));
    }

    [Fact]
    public void The_recommendation_names_the_size()
    {
        Assert.Contains("256 x 256", ItemIconFiles.RecommendationText);
        Assert.Equal(256, ItemIconFiles.RecommendedPixels);
        Assert.Equal(128, ItemIconFiles.MinRecommendedPixels);
    }
}

public sealed class ItemIconFolderManagerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "gf-itemicon-fm-" + Guid.NewGuid().ToString("N"));
    private readonly string _iconsDir;
    private readonly ConfigurationManager _config;
    private readonly FolderManager _manager;
    private readonly FolderDef _folder;
    private readonly FolderItem _item;

    public ItemIconFolderManagerTests()
    {
        Directory.CreateDirectory(_root);
        _iconsDir = Path.Combine(_root, "item-icons");
        _config = new ConfigurationManager(_root);
        _manager = new FolderManager(_config) { ItemIconsDirectory = _iconsDir };
        _folder = _manager.CreateFolder("Tools");
        _item = new FolderItem { DisplayName = "Editor", TargetPath = @"C:\Tools\edit.exe" };
        _manager.AddItem(_folder.Id, _item);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private string NewCopy(string name = "pic.png", byte[]? bytes = null)
    {
        string source = Path.Combine(_root, name);
        File.WriteAllBytes(source, bytes ?? [1, 2, 3, 4]);
        return ItemIconFiles.Store(source, _item.Id, _iconsDir);
    }

    [Fact]
    public void SetItemIcon_stores_the_path_and_it_survives_a_reload()
    {
        string copy = NewCopy();
        Assert.True(_manager.SetItemIcon(_folder.Id, _item.Id, copy));

        var reloaded = new FolderManager(_config);
        Assert.Equal(copy, reloaded.GetFolder(_folder.Id)!.FindItem(_item.Id)!.CustomIconPath);
    }

    [Fact]
    public void Items_without_a_chosen_icon_have_none()
    {
        Assert.Null(_manager.GetFolder(_folder.Id)!.FindItem(_item.Id)!.CustomIconPath);
    }

    [Fact]
    public void Choosing_another_icon_removes_the_copy_it_replaces()
    {
        string first = NewCopy("a.png");
        _manager.SetItemIcon(_folder.Id, _item.Id, first);
        Thread.Sleep(5);
        string second = NewCopy("b.png");

        _manager.SetItemIcon(_folder.Id, _item.Id, second);

        Assert.False(File.Exists(first));
        Assert.True(File.Exists(second));
        Assert.Equal(second, _item.CustomIconPath);
    }

    [Fact]
    public void Reset_goes_back_to_the_windows_icon_and_removes_the_copy()
    {
        string copy = NewCopy();
        _manager.SetItemIcon(_folder.Id, _item.Id, copy);

        Assert.True(_manager.SetItemIcon(_folder.Id, _item.Id, null));

        Assert.Null(_item.CustomIconPath);
        Assert.False(File.Exists(copy));
        Assert.Null(new FolderManager(_config).GetFolder(_folder.Id)!.FindItem(_item.Id)!.CustomIconPath);
    }

    [Fact]
    public void Removing_the_item_removes_its_copy_but_never_the_original_picture()
    {
        string original = Path.Combine(_root, "original.png");
        File.WriteAllBytes(original, [5, 5, 5]);
        string copy = ItemIconFiles.Store(original, _item.Id, _iconsDir);
        _manager.SetItemIcon(_folder.Id, _item.Id, copy);

        _manager.RemoveItem(_folder.Id, _item.Id);

        Assert.False(File.Exists(copy));
        Assert.True(File.Exists(original));
    }

    [Fact]
    public void Deleting_the_folder_removes_the_copies_of_all_its_items()
    {
        var second = new FolderItem { DisplayName = "Second", TargetPath = @"C:\Tools\two.exe" };
        _manager.AddItem(_folder.Id, second);
        string c1 = NewCopy("one.png");
        string source2 = Path.Combine(_root, "two.png");
        File.WriteAllBytes(source2, [7, 7]);
        string c2 = ItemIconFiles.Store(source2, second.Id, _iconsDir);
        _manager.SetItemIcon(_folder.Id, _item.Id, c1);
        _manager.SetItemIcon(_folder.Id, second.Id, c2);

        _manager.DeleteFolder(_folder.Id);

        Assert.False(File.Exists(c1));
        Assert.False(File.Exists(c2));
    }

    [Fact]
    public void Renaming_keeps_the_chosen_icon()
    {
        string copy = NewCopy();
        _manager.SetItemIcon(_folder.Id, _item.Id, copy);

        _manager.RenameItem(_folder.Id, _item.Id, "Renamed");

        Assert.Equal(copy, _item.CustomIconPath);
        Assert.Equal("Renamed", _item.DisplayName);
    }

    [Fact]
    public void Unknown_ids_change_nothing()
    {
        Assert.False(_manager.SetItemIcon("nope", _item.Id, "x.png"));
        Assert.False(_manager.SetItemIcon(_folder.Id, "nope", "x.png"));
    }

    [Fact]
    public void Setting_the_same_icon_again_does_not_delete_it()
    {
        string copy = NewCopy();
        _manager.SetItemIcon(_folder.Id, _item.Id, copy);
        _manager.SetItemIcon(_folder.Id, _item.Id, copy);
        Assert.True(File.Exists(copy));
    }
}

public sealed class ItemIconBackupTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "gf-itemicon-bk-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _extra = [];

    private sealed record Setup(string Dir, ConfigurationManager Config, FolderManager Folders, BackupService Backup);

    private Setup NewSetup(string name)
    {
        string dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);
        var config = new ConfigurationManager(dir);
        var folders = new FolderManager(config) { ItemIconsDirectory = Path.Combine(dir, "item-icons") };
        return new Setup(dir, config, folders, new BackupService(dir));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static (FolderDef Folder, FolderItem Item) AddItemWithIcon(Setup s, string folderName, string target, byte[] picture)
    {
        var folder = s.Folders.CreateFolder(folderName);
        var item = new FolderItem { DisplayName = Path.GetFileNameWithoutExtension(target), TargetPath = target };
        s.Folders.AddItem(folder.Id, item);
        string source = Path.Combine(s.Dir, item.Id + ".png");
        File.WriteAllBytes(source, picture);
        s.Folders.SetItemIcon(folder.Id, item.Id, ItemIconFiles.Store(source, item.Id, s.Folders.ItemIconsDirectory));
        return (folder, item);
    }

    [Fact]
    public void Export_writes_item_icons_with_checksums_and_marks_the_backup_schema_2()
    {
        var a = NewSetup("a");
        var (_, item) = AddItemWithIcon(a, "Tools", @"C:\Tools\one.exe", [1, 2, 3, 4, 5]);
        string zip = Path.Combine(a.Dir, "out.zip");

        a.Backup.Export(zip, a.Config);

        using var archive = ZipFile.OpenRead(zip);
        ZipSafety.ValidateArchive(archive);
        var plan = ImportPlanner.CreatePlan(zip, a.Folders);
        Assert.Equal(2, plan.Manifest.SchemaVersion);
        Assert.True(plan.Manifest.ItemIcons.TryGetValue(item.Id, out string? entry));
        Assert.StartsWith("item-icons/", entry);
        Assert.True(plan.Manifest.FileChecksums.ContainsKey(entry!));
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, plan.ItemPictures[item.Id].Bytes);
    }

    [Fact]
    public void A_backup_without_item_icons_stays_schema_1_so_older_releases_still_open_it()
    {
        var a = NewSetup("a");
        string zip = Path.Combine(a.Dir, "plain.zip");

        a.Backup.Export(zip, a.Config);

        var plan = ImportPlanner.CreatePlan(zip, a.Folders);
        Assert.Equal(1, plan.Manifest.SchemaVersion);
        Assert.Empty(plan.Manifest.ItemIcons);
        Assert.Empty(plan.ItemPictures);
    }

    [Fact]
    public void Replace_restores_the_icon_into_the_new_app_folder_and_drops_the_old_pcs_path()
    {
        var a = NewSetup("a");
        var (folder, item) = AddItemWithIcon(a, "Tools", @"C:\Tools\one.exe", [9, 8, 7, 6]);
        string zip = Path.Combine(a.Dir, "out.zip");
        a.Backup.Export(zip, a.Config);

        var b = NewSetup("b");
        var plan = ImportPlanner.CreatePlan(zip, b.Folders);
        b.Backup.ApplyImport(plan, ImportMode.Replace, b.Config, b.Folders);

        var restored = b.Folders.GetFolder(folder.Id)!.FindItem(item.Id)!;
        Assert.NotNull(restored.CustomIconPath);
        Assert.StartsWith(Path.Combine(b.Dir, "item-icons"), restored.CustomIconPath, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(item.CustomIconPath, restored.CustomIconPath);
        Assert.Equal(new byte[] { 9, 8, 7, 6 }, File.ReadAllBytes(restored.CustomIconPath!));
    }

    [Fact]
    public void Merge_adds_a_new_item_with_its_icon()
    {
        var a = NewSetup("a");
        var (folder, item) = AddItemWithIcon(a, "Tools", @"C:\Tools\new.exe", [1, 1, 1]);
        string zip = Path.Combine(a.Dir, "out.zip");
        a.Backup.Export(zip, a.Config);

        var b = NewSetup("b");
        b.Folders.CreateFolder("Tools"); // different id, so it arrives as its own folder
        var plan = ImportPlanner.CreatePlan(zip, b.Folders);
        b.Backup.ApplyImport(plan, ImportMode.Merge, b.Config, b.Folders);

        var imported = b.Folders.Folders.SelectMany(f => f.Items).Single(i => i.Id == item.Id);
        Assert.True(File.Exists(imported.CustomIconPath));
        Assert.StartsWith(Path.Combine(b.Dir, "item-icons"), imported.CustomIconPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Merge_gives_an_existing_item_the_archives_icon_and_removes_the_copy_it_replaces()
    {
        var a = NewSetup("a");
        var (folder, item) = AddItemWithIcon(a, "Tools", @"C:\Tools\same.exe", [4, 4, 4, 4]);
        string zip = Path.Combine(a.Dir, "out.zip");
        a.Backup.Export(zip, a.Config);

        var b = NewSetup("b");
        var local = b.Folders.CreateFolder("Tools");
        // Same folder id and name as the archive's, and an item with the same target that already has an icon.
        b.Folders.DeleteFolder(local.Id);
        var sameFolder = new FolderDef { Id = folder.Id, Name = "Tools" };
        var existing = new FolderItem { DisplayName = "same", TargetPath = @"C:\Tools\same.exe" };
        sameFolder.Items.Add(existing);
        var cfg = b.Config.LoadFolders();
        cfg.Folders.Add(sameFolder);
        b.Config.SaveFolders(cfg);
        b.Folders.Reload();
        string oldSource = Path.Combine(b.Dir, "old.png");
        File.WriteAllBytes(oldSource, [0, 0]);
        string oldCopy = ItemIconFiles.Store(oldSource, existing.Id, b.Folders.ItemIconsDirectory);
        b.Folders.SetItemIcon(folder.Id, existing.Id, oldCopy);

        var plan = ImportPlanner.CreatePlan(zip, b.Folders);
        b.Backup.ApplyImport(plan, ImportMode.Merge, b.Config, b.Folders);

        var after = b.Folders.GetFolder(folder.Id)!.Items.Single();
        Assert.Equal(new byte[] { 4, 4, 4, 4 }, File.ReadAllBytes(after.CustomIconPath!));
        Assert.False(File.Exists(oldCopy));
    }

    [Fact]
    public void Merge_keeps_the_icon_an_existing_item_has_when_the_archive_supplies_none()
    {
        var a = NewSetup("a");
        var folder = a.Folders.CreateFolder("Tools");
        a.Folders.AddItem(folder.Id, new FolderItem { DisplayName = "same", TargetPath = @"C:\Tools\same.exe" });
        string zip = Path.Combine(a.Dir, "out.zip");
        a.Backup.Export(zip, a.Config);

        var b = NewSetup("b");
        var cfg = b.Config.LoadFolders();
        var existing = new FolderItem { DisplayName = "same", TargetPath = @"C:\Tools\same.exe" };
        cfg.Folders.Add(new FolderDef { Id = folder.Id, Name = "Tools", Items = { existing } });
        b.Config.SaveFolders(cfg);
        b.Folders.Reload();
        string source = Path.Combine(b.Dir, "mine.png");
        File.WriteAllBytes(source, [3, 3]);
        string mine = ItemIconFiles.Store(source, existing.Id, b.Folders.ItemIconsDirectory);
        b.Folders.SetItemIcon(folder.Id, existing.Id, mine);

        b.Backup.ApplyImport(ImportPlanner.CreatePlan(zip, b.Folders), ImportMode.Merge, b.Config, b.Folders);

        Assert.Equal(mine, b.Folders.GetFolder(folder.Id)!.Items.Single().CustomIconPath);
        Assert.True(File.Exists(mine));
    }

    [Fact]
    public void An_item_whose_icon_file_is_gone_is_exported_without_a_dead_path()
    {
        var a = NewSetup("a");
        var (folder, item) = AddItemWithIcon(a, "Tools", @"C:\Tools\one.exe", [1, 2, 3]);
        File.Delete(a.Folders.GetFolder(folder.Id)!.FindItem(item.Id)!.CustomIconPath!);
        string zip = Path.Combine(a.Dir, "out.zip");

        a.Backup.Export(zip, a.Config);

        var plan = ImportPlanner.CreatePlan(zip, a.Folders);
        Assert.Empty(plan.ItemPictures);
        Assert.Null(plan.FoldersConfig.Folders.Single(f => f.Id == folder.Id).Items.Single().CustomIconPath);
    }

    [Fact]
    public void ZipSafety_allows_item_icon_pictures_and_refuses_other_files_in_that_folder()
    {
        string good = Path.Combine(_root, "good.zip");
        Directory.CreateDirectory(_root);
        using (var archive = ZipFile.Open(good, ZipArchiveMode.Create))
        {
            var e = archive.CreateEntry("item-icons/abc.png");
            using var s = e.Open();
            s.Write(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        }
        using (var read = ZipFile.OpenRead(good)) ZipSafety.ValidateArchive(read);

        foreach (string bad in new[] { "item-icons/abc.txt", "item-icons/sub/abc.png", "item-icons/abc.exe" })
        {
            string path = Path.Combine(_root, "bad-" + Guid.NewGuid().ToString("N") + ".zip");
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                var e = archive.CreateEntry(bad);
                using var s = e.Open();
                s.Write(new byte[] { 1, 2, 3, 4 });
            }
            Assert.Throws<InvalidOperationException>(() =>
            {
                using var read = ZipFile.OpenRead(path);
                ZipSafety.ValidateArchive(read);
            });
        }
    }

    [Fact]
    public void An_oversized_item_icon_in_an_archive_is_skipped_not_loaded()
    {
        var a = NewSetup("a");
        var (_, item) = AddItemWithIcon(a, "Tools", @"C:\Tools\one.exe", [1, 2, 3]);
        string zip = Path.Combine(a.Dir, "tampered.zip");
        var rnd = new Random(7);
        byte[] big = new byte[ItemIconFiles.MaxBytes + 1024];
        rnd.NextBytes(big);
        string foldersJson = File.ReadAllText(Path.Combine(a.Dir, "folders.json"));
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            void Write(string name, byte[] bytes)
            {
                var e = archive.CreateEntry(name);
                using var s = e.Open();
                s.Write(bytes);
            }
            Write("manifest.json", Encoding.UTF8.GetBytes($$$"""{"appVersion":"1.2.2","schemaVersion":2,"machineName":"T","monitors":[],"fileChecksums":{},"folderIcons":{},"itemIcons":{"{{{item.Id}}}":"item-icons/big.png"}}"""));
            Write("folders.json", Encoding.UTF8.GetBytes(foldersJson));
            Write("item-icons/big.png", big);
        }

        var plan = ImportPlanner.CreatePlan(zip, a.Folders);

        Assert.Empty(plan.ItemPictures);
        Assert.Null(plan.FoldersConfig.Folders.SelectMany(f => f.Items).Single(i => i.Id == item.Id).CustomIconPath);
    }

    [Fact]
    public void A_corrupted_item_icon_fails_the_checksum()
    {
        var a = NewSetup("a");
        AddItemWithIcon(a, "Tools", @"C:\Tools\one.exe", [1, 2, 3, 4, 5, 6]);
        string zip = Path.Combine(a.Dir, "out.zip");
        a.Backup.Export(zip, a.Config);

        string tampered = Path.Combine(a.Dir, "tampered.zip");
        using (var src = ZipFile.OpenRead(zip))
        using (var dst = ZipFile.Open(tampered, ZipArchiveMode.Create))
        {
            foreach (var entry in src.Entries)
            {
                var copy = dst.CreateEntry(entry.FullName);
                using var input = entry.Open();
                using var output = copy.Open();
                if (entry.FullName.StartsWith("item-icons/")) output.Write(new byte[] { 9, 9, 9, 9, 9, 9 });
                else input.CopyTo(output);
            }
        }

        Assert.Throws<InvalidOperationException>(() => ImportPlanner.CreatePlan(tampered, a.Folders));
    }
}
