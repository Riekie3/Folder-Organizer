using System.Text.Json;
using System.Text.Json.Serialization;
using GlassFolders.Core.Storage;

namespace GlassFolders.Core.Poc;

/// <summary>
/// Phase 1 proof-of-concept layout: tile positions in physical screen pixels.
/// Replaced by the per-monitor, DPI-aware layout model in Phase 5.
/// </summary>
public sealed class TileLayout
{
    public int SchemaVersion { get; set; } = 1;
    public List<TilePlacement> Tiles { get; set; } = [];

    public TilePlacement? Find(string id) => Tiles.FirstOrDefault(t => t.Id == id);

    public void Set(string id, int x, int y)
    {
        var t = Find(id);
        if (t is null) Tiles.Add(new TilePlacement { Id = id, X = x, Y = y });
        else { t.X = x; t.Y = y; }
    }
}

public sealed class TilePlacement
{
    public string Id { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
}

[JsonSerializable(typeof(TileLayout))]
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class TileLayoutJson : JsonSerializerContext;

public sealed class TileLayoutStore(string path)
{
    public string Path { get; } = path;

    public TileLayout Load()
    {
        string? text = AtomicFile.ReadWithFallback(Path, IsValid);
        return text is null ? new TileLayout() : JsonSerializer.Deserialize(text, TileLayoutJson.Default.TileLayout)!;
    }

    public void Save(TileLayout layout) =>
        AtomicFile.WriteAllText(Path, JsonSerializer.Serialize(layout, TileLayoutJson.Default.TileLayout));

    private static bool IsValid(string text)
    {
        try { return JsonSerializer.Deserialize(text, TileLayoutJson.Default.TileLayout) is { SchemaVersion: >= 1 }; }
        catch (JsonException) { return false; }
    }
}
