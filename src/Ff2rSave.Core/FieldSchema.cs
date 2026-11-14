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
    public bool IsUnverified => Confidence != "confirmed";
    public int HeaderWidth => Type switch { "u8" => 1, "u32" => 4, "u64" => 8, _ => throw new SaveFormatException($"Bad header type {Type} in {Id}") };
}

/// <summary>Field list loaded at runtime from docs/fields.json so new fields need no C# change.</summary>
public sealed class FieldSchema
{
    public IReadOnlyList<FieldDef> Fields { get; }
    FieldSchema(List<FieldDef> f) => Fields = f;
    public static FieldSchema Load(string path)
    {
        if (!File.Exists(path)) throw new SaveFormatException($"fields.json not found at {path}. It ships next to the program: copy the whole program folder, not just the .exe.");
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var list = doc.RootElement.GetProperty("fields").Deserialize<List<FieldDef>>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
            return new FieldSchema(list);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        { throw new SaveFormatException($"fields.json at {path} is not valid ({ex.Message}). Re-copy it from the release folder.", ex); }
    }
    public FieldDef this[string id] => Fields.FirstOrDefault(f => f.Id == id) ?? throw new KeyNotFoundException($"Unknown field '{id}'");
}
