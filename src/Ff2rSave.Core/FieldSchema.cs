using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ff2rSave.Core;

public sealed class FieldDef
{
    public string Id { get; set; } = ""; public string Name { get; set; } = ""; public string Source { get; set; } = "json";
    public int Offset { get; set; } public string Path { get; set; } = ""; public string Type { get; set; } = "int";
    public long? Min { get; set; } public long? Max { get; set; } public bool Editable { get; set; }
    public bool Collection { get; set; } public string Description { get; set; } = ""; public string Confidence { get; set; } = "guessed";
    [JsonPropertyName("derived_from")] public string? DerivedFrom { get; set; }
    /// <summary>Values seen in sample saves (a non-binding hint: other values may or may not be valid).</summary>
    [JsonPropertyName("observed_values")] public long[]? ObservedValues { get; set; }
    /// <summary>"user" (SAVEDATA, fields.json) or "system" (SYSTEMSAVEDATA00, system_fields.json; always read-only).</summary>
    public string Scope { get; set; } = "user";
    public bool IsUnverified => Confidence != "confirmed";
    public int HeaderWidth => Type switch { "u8" => 1, "u32" => 4, "u64" => 8, _ => throw new SaveFormatException($"Bad header type {Type} in {Id}") };
}

/// <summary>Field list loaded at runtime from docs/fields.json so new fields need no C# change.</summary>
public sealed class FieldSchema
{
    public IReadOnlyList<FieldDef> Fields { get; }
    FieldSchema(List<FieldDef> f) => Fields = f;
    /// <summary>Loads fields.json and, if given, system_fields.json (all of whose fields are read-only guesses). Ids must be unique across both.</summary>
    public static FieldSchema Load(string path, string? systemPath = null)
    {
        var fields = LoadFile(path);
        if (systemPath != null) fields.AddRange(LoadFile(systemPath).Select(f => { f.Scope = "system"; return f; }));
        var dup = fields.GroupBy(f => f.Id).FirstOrDefault(g => g.Count() > 1);
        if (dup != null) throw new SaveFormatException($"Field id '{dup.Key}' is defined twice in the field files.");
        return new FieldSchema(fields);
    }

    /// <summary>Fields that apply to a user save (false) or a system save (true).</summary>
    public IEnumerable<FieldDef> For(bool system) => Fields.Where(f => (f.Scope == "system") == system);

    static List<FieldDef> LoadFile(string path)
    {
        if (!File.Exists(path)) throw new SaveFormatException($"{Path.GetFileName(path)} not found at {path}. It ships next to the program: copy the whole program folder, not just the .exe.");
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var list = doc.RootElement.GetProperty("fields").Deserialize<List<FieldDef>>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
            return list;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        { throw new SaveFormatException($"{Path.GetFileName(path)} at {path} is not valid ({ex.Message}). Re-copy it from the release folder.", ex); }
    }
    public FieldDef this[string id] => Fields.FirstOrDefault(f => f.Id == id) ?? throw new KeyNotFoundException($"Unknown field '{id}'");
}
