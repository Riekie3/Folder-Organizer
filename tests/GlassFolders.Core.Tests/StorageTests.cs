using GlassFolders.Core.Poc;
using GlassFolders.Core.Storage;

namespace GlassFolders.Core.Tests;

public sealed class StorageTests : IDisposable
{
    private readonly string _dir;
    private string P(string name) => Path.Combine(_dir, name);

    public StorageTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "gf-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Write_creates_folder_and_file()
    {
        AtomicFile.WriteAllText(P("a.json"), "one");
        Assert.Equal("one", File.ReadAllText(P("a.json")));
        Assert.False(File.Exists(P("a.json.tmp")));
    }

    [Fact]
    public void Overwrite_keeps_previous_version_as_bak()
    {
        AtomicFile.WriteAllText(P("a.json"), "one");
        AtomicFile.WriteAllText(P("a.json"), "two");
        Assert.Equal("two", File.ReadAllText(P("a.json")));
        Assert.Equal("one", File.ReadAllText(P("a.json.bak")));
    }

    [Fact]
    public void Fallback_reads_bak_when_main_is_invalid()
    {
        AtomicFile.WriteAllText(P("a.json"), "good");
        AtomicFile.WriteAllText(P("a.json"), "good2");
        File.WriteAllText(P("a.json"), "corrupt");
        Assert.Equal("good", AtomicFile.ReadWithFallback(P("a.json"), t => t.StartsWith("good")));
    }

    [Fact]
    public void Fallback_returns_null_when_nothing_valid()
    {
        Assert.Null(AtomicFile.ReadWithFallback(P("missing.json"), _ => true));
    }

    [Fact]
    public void Fallback_returns_null_when_both_main_and_bak_are_corrupt()
    {
        AtomicFile.WriteAllText(P("a.json"), "bad1");
        AtomicFile.WriteAllText(P("a.json"), "bad2");
        Assert.Null(AtomicFile.ReadWithFallback(P("a.json"), t => t.StartsWith("good")));
    }

    [Fact]
    public void Layout_round_trips()
    {
        var store = new TileLayoutStore(P("layout.json"));
        var layout = new TileLayout();
        layout.Set("work", 100, 200);
        layout.Set("games", -1500, 40);
        layout.Set("work", 110, 210);
        store.Save(layout);

        var loaded = store.Load();
        Assert.Equal(2, loaded.Tiles.Count);
        Assert.Equal((110, 210), (loaded.Find("work")!.X, loaded.Find("work")!.Y));
        Assert.Equal(-1500, loaded.Find("games")!.X);
    }

    [Fact]
    public void Corrupt_layout_recovers_from_backup()
    {
        var store = new TileLayoutStore(P("layout.json"));
        var layout = new TileLayout();
        layout.Set("work", 1, 2);
        store.Save(layout);
        layout.Set("work", 3, 4);
        store.Save(layout);
        File.WriteAllText(P("layout.json"), "{ not json");

        Assert.Equal(1, store.Load().Find("work")!.X);
    }

    [Fact]
    public void Corrupt_layout_without_valid_backup_returns_empty_layout()
    {
        var store = new TileLayoutStore(P("layout.json"));
        File.WriteAllText(P("layout.json"), "{ invalid json");
        File.WriteAllText(P("layout.json.bak"), "{ also invalid");

        var loaded = store.Load();
        Assert.NotNull(loaded);
        Assert.Empty(loaded.Tiles);
        Assert.Equal(1, loaded.SchemaVersion);
    }

    [Fact]
    public void Layout_with_zero_schema_version_is_rejected_and_falls_back()
    {
        var store = new TileLayoutStore(P("layout.json"));
        var layout = new TileLayout();
        layout.Set("work", 42, 84);
        store.Save(layout);
        layout.Set("work", 43, 85);
        store.Save(layout); // now .bak has work at (42, 84)

        // Overwrite main with invalid schemaVersion 0
        File.WriteAllText(P("layout.json"), """{"schemaVersion": 0, "tiles": []}""");

        var loaded = store.Load();
        Assert.Single(loaded.Tiles);
        Assert.Equal(42, loaded.Find("work")!.X);
    }

    [Fact]
    public void Missing_layout_gives_empty()
    {
        Assert.Empty(new TileLayoutStore(P("none.json")).Load().Tiles);
    }
}
