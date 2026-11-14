using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ff2rSave.Core;

/// <summary>An item hash key. Name is null for keys that are known to matter but have no name yet (the Note then says why).</summary>
public sealed record ItemName(long Key, string? Name, string Source, string Confidence, string? Note = null);

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
                long key = e.GetProperty("key").GetInt64();
                string? Opt(string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String && v.GetString()!.Trim() != "" ? v.GetString() : null;
                string? name = Opt("name"), note = Opt("note");
                if (key == 0 || (name == null && note == null)) throw new JsonException("each item needs a non-zero key and either a name or a note explaining why it has none");
                map.Add(key, new ItemName(key, name, Opt("source") ?? "", Opt("confidence") ?? "guessed", note));
            }
            return new(map);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or ArgumentException or InvalidOperationException or FormatException)
        { throw new SaveFormatException($"items.json at {path} is not valid ({ex.Message}). Each entry needs a unique non-zero numeric key and a name (or a note if it is deliberately unnamed).", ex); }
    }
}

/// <summary>One owned-item row (PossessionItemData/possession_items or storage_items).</summary>
public sealed record ItemEntry(string Section, int Index, long Key, long KeyNum, long Num, long Flag, string? Name, string? NameConfidence, string? Note = null)
{
    /// <summary>The name with its confidence when known, otherwise 'unknown (0x...)'. Never claims more than the catalog says.</summary>
    public string Display => Name != null ? (NameConfidence == "confirmed" ? Name : $"{Name} ({NameConfidence})") : $"unknown (0x{Key:X8})";
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
                    rows.Add(new ItemEntry(sec, i, key, L("key_num"), num, L("flag"), n?.Name, n?.Name != null ? n.Confidence : null, n?.Note));
                }
        return rows;
    }
}
