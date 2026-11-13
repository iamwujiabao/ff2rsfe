using Ff2rSave.Core;

namespace Ff2rSave.Tests;

/// <summary>Locates repo root, git-ignored config and samples. Tests never write to samples/ (they copy to temp).</summary>
public static class Fixture
{
    public static readonly string Root = FindRoot();
    public static CryptoConfig Cfg => CryptoConfig.Load(Path.Combine(Root, "local_config.json"));
    public static FieldSchema Schema => FieldSchema.Load(Path.Combine(Root, "docs", "fields.json"));
    public static string Samples => Path.Combine(Root, "samples");

    static string FindRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "docs", "fields.json"))) return d.FullName;
        throw new InvalidOperationException("repo root not found");
    }
    public static IEnumerable<string> AllBins() => Directory.Exists(Samples) ? Directory.GetFiles(Samples, "*.BIN", SearchOption.AllDirectories).Order() : [];
    public static string Mine(string slot, string file = "SAVEDATA.BIN") => Path.Combine(Samples, "mine", slot, file);

    public static string TempSlotFrom(string slotDir)
    {
        var t = Path.Combine(Path.GetTempPath(), "ff2r-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(t);
        foreach (var f in Directory.GetFiles(slotDir, "*.BIN")) File.Copy(f, Path.Combine(t, Path.GetFileName(f)));
        return t;
    }
}
