using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ff2rSave.Core;

public sealed record ItemName(long Key, string Name, string Source, string Confidence);

/// <summary>docs/items.json: item hash key -> name. A missing file is fine (names are then shown as unknown).</summary>
public sealed class ItemCatalog
{
    readonly Dictionary<long, ItemName> map;
    ItemCatalog(Dictionary<long, ItemName> m) => map = m;
    public static ItemCatalog Empty { get; } = new([]);
    public int Count => map.Count;
    public ItemName? Get(long key) => map.GetValueOrDefault(key);

    public static ItemCatalog Load(string? path)
    {
        if (path == null || !File.Exists(path)) return Empty;
        try
        {
            var map = new Dictionary<long, ItemName>();
            foreach (var e in JsonDocument.Parse(File.ReadAllText(path)).RootElement.GetProperty("items").EnumerateArray())
            {
                // a missing key must not silently become key 0 (that is every empty slot)
                long key = e.GetProperty("key").GetInt64(); string name = e.GetProperty("name").GetString() ?? "";
                if (key == 0 || name.Trim() == "") throw new JsonException("each item needs a non-zero key and a non-empty name");
                map.Add(key, new ItemName(key, name, e.TryGetProperty("source", out var so) ? so.GetString() ?? "" : "", e.TryGetProperty("confidence", out var co) ? co.GetString() ?? "guessed" : "guessed"));
            }
            return new(map);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or ArgumentException or InvalidOperationException or FormatException)
        { throw new SaveFormatException($"items.json at {path} is not valid ({ex.Message}). Each entry needs a unique non-zero numeric key and a name.", ex); }
    }
}

/// <summary>One owned-item row (PossessionItemData/possession_items or storage_items).</summary>
public sealed record ItemEntry(string Section, int Index, long Key, long KeyNum, long Num, long Flag, string? Name, string? NameConfidence)
{
    public string Path => $"/PossessionItemData/{Section}/{Index}/ItemObjectData";
}

public static class ItemListing
{
    public static List<ItemEntry> Rows(SaveFile f, ItemCatalog names, bool all = false)
    {
        var rows = new List<ItemEntry>();
        if (f.Root["PossessionItemData"] is not JsonObject p) return rows;
        foreach (var sec in new[] { "possession_items", "storage_items" })
            if (p[sec] is JsonArray a)
                for (int i = 0; i < a.Count; i++)
                {
                    if (a[i]?["ItemObjectData"] is not JsonObject o) continue;
                    long L(string k) => o[k] is JsonValue v && v.TryGetValue<long>(out var n) ? n : 0;
                    long key = L("key"), num = L("num");
                    if (!all && key == 0 && num == 0) continue; // empty slot
                    var n = names.Get(key);
                    rows.Add(new ItemEntry(sec, i, key, L("key_num"), num, L("flag"), n?.Name, n?.Confidence));
                }
        return rows;
    }
}
