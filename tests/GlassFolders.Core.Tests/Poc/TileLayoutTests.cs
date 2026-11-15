using GlassFolders.Core.Poc;

namespace GlassFolders.Core.Tests.Poc;

public class TileLayoutTests
{
    [Fact]
    public void SetAddsThenUpdatesOnePlacement()
    {
        var layout = new TileLayout();

        layout.Set("work", 100, 200);
        layout.Set("work", 300, 400);

        var placement = Assert.Single(layout.Tiles);
        Assert.Equal("work", placement.Id);
        Assert.Equal(300, placement.X);
        Assert.Equal(400, placement.Y);
    }

    [Fact]
    public void RemoveForgetsOnlyTheDeletedFolder()
    {
        var layout = new TileLayout();
        layout.Set("work", 100, 200);
        layout.Set("games", 300, 400);

        Assert.True(layout.Remove("work"));

        Assert.Null(layout.Find("work"));
        Assert.NotNull(layout.Find("games"));
        Assert.False(layout.Remove("work"));
    }
}
