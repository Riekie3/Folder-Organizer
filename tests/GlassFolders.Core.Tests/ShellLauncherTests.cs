using GlassFolders.Core.Models;
using GlassFolders.Native.Shell;

namespace GlassFolders.Core.Tests;

public sealed class ShellLauncherTests
{
    [Fact]
    public void ResolveLocationCandidate_resolves_system_explorer()
    {
        var item = new FolderItem
        {
            DisplayName = "File Explorer",
            TargetPath = "explorer.exe",
            Type = FolderItemType.Executable
        };

        string? candidate = ShellLauncher.ResolveLocationCandidate(item);
        Assert.NotNull(candidate);
        Assert.True(File.Exists(candidate));
        Assert.EndsWith("explorer.exe", candidate, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveLocationCandidate_resolves_system_notepad()
    {
        var item = new FolderItem
        {
            DisplayName = "Notepad",
            TargetPath = "notepad.exe",
            Type = FolderItemType.Executable
        };

        string? candidate = ShellLauncher.ResolveLocationCandidate(item);
        Assert.NotNull(candidate);
        Assert.True(File.Exists(candidate));
        Assert.EndsWith("notepad.exe", candidate, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveLocationCandidate_resolves_directory_target()
    {
        string sysDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var item = new FolderItem
        {
            DisplayName = "System32",
            TargetPath = sysDir,
            Type = FolderItemType.Folder
        };

        string? candidate = ShellLauncher.ResolveLocationCandidate(item);
        Assert.NotNull(candidate);
        Assert.True(Directory.Exists(candidate));
        Assert.Equal(sysDir, candidate, ignoreCase: true);
    }

    [Fact]
    public void ResolveLocationCandidate_falls_back_to_shortcut_when_target_missing()
    {
        string tempDir = Path.GetTempPath();
        var item = new FolderItem
        {
            DisplayName = "MyShortcut",
            TargetPath = @"C:\NonExistentDirectory\MissingApp.exe",
            ShortcutPath = tempDir,
            Type = FolderItemType.Shortcut
        };

        string? candidate = ShellLauncher.ResolveLocationCandidate(item);
        Assert.NotNull(candidate);
        Assert.Equal(Path.GetFullPath(tempDir), candidate, ignoreCase: true);
    }

    [Fact]
    public void ResolveLocationCandidate_returns_null_when_both_target_and_shortcut_missing()
    {
        var item = new FolderItem
        {
            DisplayName = "Ghost",
            TargetPath = @"C:\Missing1\app.exe",
            ShortcutPath = @"C:\Missing2\app.lnk",
            Type = FolderItemType.Shortcut
        };

        string? candidate = ShellLauncher.ResolveLocationCandidate(item);
        Assert.Null(candidate);
    }
}
