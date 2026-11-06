using GlassFolders.Core.Models;
using GlassFolders.Native.Shell;

namespace GlassFolders.Core.Tests;

public sealed class ShellShortcutResolverTests
{
    private readonly ShellShortcutResolver _resolver = new();

    [Fact]
    public void Resolve_empty_path_throws_argument_exception()
    {
        Assert.Throws<ArgumentException>(() => _resolver.Resolve(""));
        Assert.Throws<ArgumentException>(() => _resolver.Resolve("   "));
    }

    [Fact]
    public void Resolve_http_url_returns_url_item()
    {
        var item = _resolver.Resolve("https://github.com/Riekie3/Folder-Organizer");
        Assert.Equal(FolderItemType.Url, item.Type);
        Assert.Equal("github.com", item.DisplayName);
        Assert.Equal("https://github.com/Riekie3/Folder-Organizer", item.TargetPath);
    }

    [Fact]
    public void Resolve_directory_returns_folder_item()
    {
        string sysDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var item = _resolver.Resolve(sysDir);

        Assert.Equal(FolderItemType.Folder, item.Type);
        Assert.Equal("System32", item.DisplayName, ignoreCase: true);
        Assert.Equal(sysDir, item.TargetPath, ignoreCase: true);
    }

    [Fact]
    public void Resolve_executable_returns_executable_item()
    {
        string notepad = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "notepad.exe");
        if (!File.Exists(notepad))
            notepad = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");

        var item = _resolver.Resolve(notepad);
        Assert.Equal(FolderItemType.Executable, item.Type);
        Assert.Equal(Path.GetFileNameWithoutExtension(notepad), item.DisplayName, ignoreCase: true);
        Assert.Equal(notepad, item.TargetPath, ignoreCase: true);
        Assert.Equal(Path.GetDirectoryName(notepad), item.WorkingDirectory, ignoreCase: true);
    }

    [Fact]
    public void Resolve_store_app_returns_store_app_item()
    {
        var item = _resolver.Resolve(@"shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App");
        Assert.Equal(FolderItemType.StoreApp, item.Type);
        Assert.Equal("App", item.DisplayName);
        Assert.Equal("Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", item.Aumid);
    }

    [Fact]
    public void TargetExists_returns_true_for_system_binaries_in_path()
    {
        var explorer = new FolderItem { DisplayName = "Explorer", TargetPath = "explorer.exe", Type = FolderItemType.Executable };
        Assert.True(_resolver.TargetExists(explorer));

        var notepad = new FolderItem { DisplayName = "Notepad", TargetPath = "notepad.exe", Type = FolderItemType.Executable };
        Assert.True(_resolver.TargetExists(notepad));
    }

    [Fact]
    public void TargetExists_returns_false_for_nonexistent_files()
    {
        var missing = new FolderItem { DisplayName = "Fake", TargetPath = @"C:\NonExistentDirectory\FakeApp12345.exe", Type = FolderItemType.Executable };
        Assert.False(_resolver.TargetExists(missing));
    }

    [Fact]
    public void Resolve_real_desktop_or_startmenu_lnk_does_not_crash_and_returns_valid_item()
    {
        string startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Start Menu\Programs");
        string? lnk = Directory.GetFiles(startMenu, "*.lnk", SearchOption.AllDirectories).FirstOrDefault();

        if (lnk != null)
        {
            var item = _resolver.Resolve(lnk);
            Assert.NotNull(item);
            Assert.False(string.IsNullOrWhiteSpace(item.DisplayName));
            Assert.False(string.IsNullOrWhiteSpace(item.TargetPath));
            Assert.Equal(lnk, item.ShortcutPath, ignoreCase: true);
        }
    }
}
