using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ff2rSave.Core;

/// <summary>A decrypted save. The JSON section is edited as a tree; the binary tail and all unknown bytes are preserved verbatim.</summary>
public sealed class SaveFile
{
    public const int JsonStart = 16, JsonAreaEnd = 0xFA010; // plaintext offsets (docs/save_format.md)
    static readonly JsonSerializerOptions Compact = new() { WriteIndented = false, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    readonly CryptoConfig cfg; readonly byte[] plaintext;
    public SaveHeader Header { get; }
    public JsonNode Root { get; private set; }
    public byte[] OriginalPlaintext { get; }
    public int OriginalJsonLength { get; }
    public bool Dirty { get; private set; }

    /// <summary>Fields whose confidence is not 'confirmed' are locked unless this is turned on (UI setting / CLI --allow-unverified).</summary>
    public bool AllowUnverified { get; set; }
    public List<string> LoadProblems { get; }

    long? origProgress, origPlayTime; readonly byte[] origHeader;

    SaveFile(CryptoConfig cfg, SaveHeader h, byte[] pt, long fileLen)
    {
        this.cfg = cfg; Header = h; origHeader = (byte[])h.Raw.Clone(); plaintext = pt; OriginalPlaintext = (byte[])pt.Clone();
        LoadProblems = new IntegrityService(cfg).Verify(h, pt, fileLen);
        if (h.IsSystem == false && pt.Length < JsonAreaEnd) throw new SaveFormatException("Payload too short for a user save.");
        OriginalJsonLength = FindJsonLength(pt);
        try { Root = JsonNode.Parse(pt.AsSpan(JsonStart, OriginalJsonLength)) ?? throw new SaveFormatException("JSON section is null."); }
        catch (JsonException ex) { throw new SaveFormatException("JSON section is invalid: the file is corrupted, or local_config.json belongs to a different game build.", ex); }
        origProgress = Node("/MissionData/main_mission_progress")?.GetValue<long>(); origPlayTime = Node("/PlayerData/total_play_time")?.GetValue<long>();
    }

    public static SaveFile Open(string path, CryptoConfig cfg) => FromBytes(File.ReadAllBytes(path), cfg);

    public static SaveFile FromBytes(byte[] raw, CryptoConfig cfg)
    {
        var (h, pt) = new SaveCodec(cfg).Decrypt(raw);
        return new SaveFile(cfg, h, pt, raw.Length);
    }

    static int FindJsonLength(byte[] pt)
    {
        var r = new Utf8JsonReader(pt.AsSpan(JsonStart, Math.Min(pt.Length, JsonAreaEnd) - JsonStart), isFinalBlock: true, default);
        try { while (r.Read()) if (r.CurrentDepth == 0 && (r.TokenType == JsonTokenType.EndObject)) return (int)r.BytesConsumed; }
        catch (JsonException ex) { throw new SaveFormatException("Cannot find the JSON section: the file is corrupted, or local_config.json belongs to a different game build.", ex); }
        throw new SaveFormatException("Unterminated JSON section.");
    }

    // ---- typed access ----
    public IEnumerable<string> Expand(string path)
    {
        IEnumerable<(string, JsonNode?)> cur = [("", Root)];
        foreach (var seg in path.Trim('/').Split('/'))
            cur = cur.SelectMany(t => Children(t.Item1, t.Item2, seg)).ToList();
        return cur.Select(t => t.Item1).ToList();
    }
    static IEnumerable<(string, JsonNode?)> Children(string p, JsonNode? n, string seg)
    {
        if (seg == "*") { if (n is JsonArray a) for (int i = 0; i < a.Count; i++) yield return ($"{p}/{i}", a[i]); else if (n is JsonObject o) foreach (var kv in o) yield return ($"{p}/{kv.Key}", kv.Value); }
        else if (n is JsonArray a2 && int.TryParse(seg, out var i2) && i2 < a2.Count) yield return ($"{p}/{seg}", a2[i2]);
        else if (n is JsonObject o2 && o2.TryGetPropertyValue(seg, out var v)) yield return ($"{p}/{seg}", v);
    }
    JsonNode? Node(string path)
    {
        JsonNode? n = Root;
        foreach (var seg in path.Trim('/').Split('/')) n = n is JsonArray a ? (int.TryParse(seg, out var i) && i >= 0 && i < a.Count ? a[i] : null) : n is JsonObject o ? o[seg] : null;
        return n;
    }

    public long GetInt(FieldDef f, string? concretePath = null) => f.Source == "header" ? (long)Header.GetUInt(f.Offset, f.HeaderWidth)
        : (Node(concretePath ?? Single(f)) ?? throw new SaveFormatException($"Path not found: {f.Path}")).GetValue<long>();
    public bool GetBool(FieldDef f, string? concretePath = null) => Node(concretePath ?? Single(f))!.GetValue<bool>();

    public void SetInt(FieldDef f, long value, string? concretePath = null)
    {
        Guard(f, value);
        if (f.Source == "header") { Header.SetUInt(f.Offset, f.HeaderWidth, checked((ulong)value)); return; }
        var path = concretePath ?? Single(f); var segs = path.Trim('/').Split('/'); var parent = Node("/" + string.Join('/', segs[..^1]));
        if (parent is JsonArray a) a[int.Parse(segs[^1])] = value; else if (parent is JsonObject o) o[segs[^1]] = value; else throw new SaveFormatException($"Path not found: {path}");
        Dirty = true;
    }
    public void SetBool(FieldDef f, bool value, string? concretePath = null)
    {
        CheckEditable(f);
        var segs = (concretePath ?? Single(f)).Trim('/').Split('/'); var parent = Node("/" + string.Join('/', segs[..^1]));
        if (parent is JsonArray a) a[int.Parse(segs[^1])] = value; else ((JsonObject)parent!)[segs[^1]] = value; Dirty = true;
    }
    string Single(FieldDef f) { var p = Expand(f.Path).ToList(); return p.Count == 1 ? p[0] : throw new InvalidOperationException($"Field {f.Id} is a collection ({p.Count} matches); pass a concrete path."); }
    void CheckEditable(FieldDef f)
    {
        if (!f.Editable) throw new InvalidOperationException($"Field {f.Id} is read-only.");
        if (f.IsUnverified && !AllowUnverified) throw new InvalidOperationException($"Field {f.Id} is '{f.Confidence}' (unverified, never tested in the game) and locked. Turn on 'Allow editing unverified fields' (CLI: --allow-unverified) to edit it.");
    }
    void Guard(FieldDef f, long v)
    {
        CheckEditable(f);
        if (f.Min is { } lo && v < lo || f.Max is { } hi && v > hi) throw new ArgumentOutOfRangeException(nameof(v), $"{f.Id}: {v} outside [{f.Min}, {f.Max}]");
    }

    // ---- serialize ----
    /// <summary>Header copies of progress/play time are kept in sync with the JSON (slot menu shows them).</summary>
    void SyncHeader()
    {
        if (!Header.IsUser) return;
        // Only when the JSON value was edited: the game stamps the header copy slightly later than the JSON value, so a blind sync would alter untouched saves.
        if (Node("/MissionData/main_mission_progress") is { } p && p.GetValue<long>() != origProgress) { var v = p.GetValue<long>(); Header.Checkpoint = (uint)v; Header.Chapter = (uint)(v / 1000); }
        if (Node("/PlayerData/total_play_time") is { } t && t.GetValue<long>() != origPlayTime) Header.PlayTime = (ulong)t.GetValue<long>();
    }

    /// <summary>Returns the encrypted file bytes with all integrity fields recomputed. Untouched JSON is written back byte-for-byte.</summary>
    /// <summary>Current decrypted payload including pending JSON edits (header not included). Used by the hex diff view.</summary>
    public byte[] BuildPlaintext()
    {
        var pt = (byte[])plaintext.Clone();
        if (!Dirty) return pt;
        var json = Encoding.UTF8.GetBytes(Root.ToJsonString(Compact));
        if (JsonStart + json.Length > JsonAreaEnd && Header.IsUser) throw new SaveFormatException($"JSON grew to {json.Length} bytes; the area holds {JsonAreaEnd - JsonStart}.");
        int oldEnd = JsonStart + OriginalJsonLength, newEnd = JsonStart + json.Length;
        if (newEnd > pt.Length) throw new SaveFormatException("JSON no longer fits in the payload.");
        if (newEnd > oldEnd && OriginalPlaintext.AsSpan(oldEnd, newEnd - oldEnd).IndexOfAnyExcept((byte)0) >= 0) throw new SaveFormatException("JSON growth would overwrite non-padding data.");
        json.CopyTo(pt, JsonStart); if (newEnd < oldEnd) Array.Clear(pt, newEnd, oldEnd - newEnd);
        return pt;
    }

    /// <summary>Returns the encrypted file bytes with all integrity fields recomputed. Untouched JSON is written back byte-for-byte.</summary>
    public byte[] ToBytes()
    {
        var pt = BuildPlaintext();
        SyncHeader();
        var h = new SaveHeader(Header.Raw); new IntegrityService(cfg).Fix(h, pt);
        return new SaveCodec(cfg).Encrypt(h, pt);
    }

    /// <summary>JSON text of any node by path (generic read access for the CLI), or null.</summary>
    public string? GetRaw(string path) => Node(path)?.ToJsonString();

    /// <summary>True when any edit is pending (JSON or header).</summary>
    public bool HasChanges => Dirty || !Header.Raw.AsSpan().SequenceEqual(origHeader);

    /// <summary>Display text for a field value (header number, JSON number/bool).</summary>
    public string GetText(FieldDef f, string? concretePath = null)
    {
        if (f.Source == "header") return GetInt(f).ToString();
        var n = Node(concretePath ?? Single(f)); if (n is null) return "";
        return n is JsonValue v && v.TryGetValue<bool>(out var b) ? (b ? "true" : "false") : n is JsonValue ? n.ToJsonString() : $"<{n.GetType().Name}>";
    }

    /// <summary>Parse user text according to the field/node type and apply it with validation. Throws FormatException/ArgumentOutOfRangeException/InvalidOperationException.</summary>
    public void SetText(FieldDef f, string text, string? concretePath = null)
    {
        text = text.Trim();
        bool isBool = f.Type == "bool" || (f.Source == "json" && Node(concretePath ?? Single(f)) is JsonValue jv && jv.TryGetValue<bool>(out _));
        if (isBool)
        {
            bool b = text.ToLowerInvariant() switch { "true" or "1" or "yes" => true, "false" or "0" or "no" => false, _ => throw new FormatException($"'{text}' is not true/false") };
            SetBool(f, b, concretePath);
        }
        else SetInt(f, long.Parse(text, System.Globalization.CultureInfo.InvariantCulture), concretePath);
    }
}
