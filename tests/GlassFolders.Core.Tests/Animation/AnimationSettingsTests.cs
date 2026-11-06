using System.Text.Json;
using GlassFolders.Core.Config;
using GlassFolders.Core.Models;

namespace GlassFolders.Core.Tests.Animation;

public sealed class AnimationSettingsTests
{
    [Fact]
    public void Default_settings_have_all_animations_enabled()
    {
        var anim = new AnimationSettings();
        Assert.True(anim.GlobalEnabled);
        Assert.True(anim.FollowWindows);
        Assert.Equal(1.0, anim.SpeedMultiplier);

        foreach (AnimationCategory cat in Enum.GetValues<AnimationCategory>())
        {
            Assert.True(anim.IsCategoryEnabled(cat));
        }
    }

    [Fact]
    public void Category_toggles_are_honored_when_global_is_enabled()
    {
        var anim = new AnimationSettings
        {
            GlobalEnabled = true,
            TileHover = false,
            Shimmer = false
        };

        Assert.False(anim.IsCategoryEnabled(AnimationCategory.TileHover));
        Assert.False(anim.IsCategoryEnabled(AnimationCategory.Shimmer));
        Assert.True(anim.IsCategoryEnabled(AnimationCategory.FolderOpenClose));
    }

    [Fact]
    public void Serialization_and_deserialization_round_trip()
    {
        var settings = new AppSettings
        {
            Animation = new AnimationSettings
            {
                GlobalEnabled = true,
                SpeedMultiplier = 1.5,
                ItemPress = false,
                DragGhost = false
            }
        };

        string json = JsonSerializer.Serialize(settings, ConfigurationJsonContext.Default.AppSettings);
        var loaded = JsonSerializer.Deserialize(json, ConfigurationJsonContext.Default.AppSettings);

        Assert.NotNull(loaded);
        Assert.True(loaded.Animation.GlobalEnabled);
        Assert.Equal(1.5, loaded.Animation.SpeedMultiplier);
        Assert.False(loaded.Animation.ItemPress);
        Assert.False(loaded.Animation.DragGhost);
        Assert.True(loaded.Animation.FolderOpenClose);
    }
}
