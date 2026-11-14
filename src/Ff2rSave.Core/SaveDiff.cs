using System.Text.Json.Nodes;

namespace Ff2rSave.Core;

public sealed record DiffEntry(string Location, string? Old, string? New, string? FieldId);

public sealed record DiffResult(List<DiffEntry> Header, List<DiffEntry> Json, int JsonBytesA, int JsonBytesB, int TailChangedBytes, int TailRanges)
{
    public IEnumerable<DiffEntry> Mapped => Header.Concat(Json).Where(e => e.FieldId != null);
    public IEnumerable<DiffEntry> Unmapped => Header.Concat(Json).Where(e => e.FieldId == null);
}

/// <summary>Differences between two saves: header fields, JSON leaf values (mapped to fields.json ids where a mapping exists), and the binary tail.</summary>
public static class SaveDiff
{
    public static DiffResult Compare(SaveFile a, SaveFile b, FieldSchema schema)
    {
        var header = HeaderDiff(a.Header, b.Header, schema);
        var fa = Flatten(a.Root); var fb = Flatten(b.Root);
        var patterns = schema.Fields.Where(f => f.Source == "json").Select(f => (segs: f.Path.Trim('/').Split('/'), f.Id)).ToList();
        string? Map(string path) { var s = path.Trim('/').Split('/'); return patterns.FirstOrDefault(p => p.segs.Length == s.Length && p.segs.Zip(s).All(t => t.First == "*" || t.First == t.Second)).Id; }
        var json = fa.Keys.Union(fb.Keys).Order(StringComparer.Ordinal)
            .Where(k => fa.GetValueOrDefault(k) != fb.GetValueOrDefault(k))
            .Select(k => new DiffEntry(k, fa.GetValueOrDefault(k), fb.GetValueOrDefault(k), Map(k))).ToList();

        // binary tail: everything after the longer JSON section
        int start = SaveFile.JsonStart + Math.Max(a.OriginalJsonLength, b.OriginalJsonLength); var pa = a.OriginalPlaintext; var pb = b.OriginalPlaintext;
        int n = Math.Min(pa.Length, pb.Length), changed = 0, ranges = 0; bool inRange = false;
        for (int i = start; i < n; i++) { bool d = pa[i] != pb[i]; if (d) { changed++; if (!inRange) ranges++; } inRange = d; }
        return new DiffResult(header, json, a.OriginalJsonLength, b.OriginalJsonLength, changed, ranges);
    }

    static List<DiffEntry> HeaderDiff(SaveHeader a, SaveHeader b, FieldSchema schema)
    {
        var r = new List<DiffEntry>(); var covered = new bool[SaveHeader.Size];
        foreach (var f in schema.Fields.Where(f => f.Source == "header"))
        {
            for (int i = 0; i < f.HeaderWidth; i++) covered[f.Offset + i] = true;
            ulong x = a.GetUInt(f.Offset, f.HeaderWidth), y = b.GetUInt(f.Offset, f.HeaderWidth);
            if (x != y) r.Add(new DiffEntry($"header 0x{f.Offset:X2}", x.ToString(), y.ToString(), f.Id));
        }
        for (int i = 0; i < SaveHeader.Size; i++) // bytes outside known fields; 0x50..0x8F are derived checksums, ignored
        {
            if (covered[i] || i is >= 0x50 and < 0x90 || a.Raw[i] == b.Raw[i]) continue;
            int s = i; while (i + 1 < SaveHeader.Size && !covered[i + 1] && !(i + 1 is >= 0x50 and < 0x90) && a.Raw[i + 1] != b.Raw[i + 1]) i++;
            r.Add(new DiffEntry($"header 0x{s:X2}+{i - s + 1}", Convert.ToHexString(a.Raw, s, i - s + 1), Convert.ToHexString(b.Raw, s, i - s + 1), null));
        }
        return r;
    }

    public static Dictionary<string, string> Flatten(JsonNode? root)
    {
        var d = new Dictionary<string, string>();
        void Walk(JsonNode? n, string p)
        {
            switch (n)
            {
                case JsonObject o: foreach (var kv in o) Walk(kv.Value, $"{p}/{kv.Key}"); break;
                case JsonArray a: for (int i = 0; i < a.Count; i++) Walk(a[i], $"{p}/{i}"); break;
                default: d[p] = n?.ToJsonString() ?? "null"; break;
            }
        }
        Walk(root, ""); return d;
    }
}
