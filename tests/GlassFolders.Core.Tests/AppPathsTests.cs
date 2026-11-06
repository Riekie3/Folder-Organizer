using GlassFolders.Core.Config;

namespace GlassFolders.Core.Tests;

public sealed class AppPathsTests
{
    [Fact]
    public void AppPaths_defaults_to_valid_non_empty_paths()
    {
        Assert.False(string.IsNullOrWhiteSpace(AppPaths.BaseAppDataDir));
        Assert.False(string.IsNullOrWhiteSpace(AppPaths.BaseLocalAppDataDir));
        Assert.False(string.IsNullOrWhiteSpace(AppPaths.FoldersPath));
        Assert.False(string.IsNullOrWhiteSpace(AppPaths.SettingsPath));
        Assert.False(string.IsNullOrWhiteSpace(AppPaths.BackupsDir));
        Assert.False(string.IsNullOrWhiteSpace(AppPaths.LogsDir));
        Assert.False(string.IsNullOrWhiteSpace(AppPaths.IconCacheDir));
    }

    [Fact]
    public void Configuration_paths_are_rooted_in_base_app_data_dir()
    {
        Assert.StartsWith(AppPaths.BaseAppDataDir, AppPaths.FoldersPath, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(AppPaths.BaseAppDataDir, AppPaths.SettingsPath, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(AppPaths.BaseAppDataDir, AppPaths.BackupsDir, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Local_cache_paths_are_rooted_in_base_local_app_data_dir()
    {
        Assert.StartsWith(AppPaths.BaseLocalAppDataDir, AppPaths.LogsDir, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(AppPaths.BaseLocalAppDataDir, AppPaths.IconCacheDir, StringComparison.OrdinalIgnoreCase);
    }
}
