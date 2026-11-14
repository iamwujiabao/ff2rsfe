using System.Text.Json;

namespace Ff2rSave.Core;

/// <summary>Game-derived constants. Loaded from git-ignored local_config.json (produced by tools/keys.py from the user's own exe); never embedded.</summary>
public sealed record CryptoConfig(byte[] AesKey, byte[] AesIv, byte[] ChkA, byte[] ChkB)
{
    public const string FileName = "local_config.json";

    const string HowTo = " It holds the game's key tables, is NOT included in this download, and is created once from your own copy of the game: run `python3 tools/keys.py` with game/FatalFrameII.exe in place (see README, 'One-time setup'). Then put the file in the same folder as this program (or any folder above it).";

    /// <summary>Finds local_config.json by walking up from each start folder. Throws a plain-language SaveFormatException listing where it looked.</summary>
    public static string Find(IEnumerable<string> startDirs, string? explicitPath = null)
    {
        if (explicitPath != null) return File.Exists(explicitPath) ? explicitPath : throw new SaveFormatException($"The config file you named does not exist: {explicitPath}.{HowTo}");
        var searched = new List<string>();
        foreach (var start in startDirs)
            for (var d = new DirectoryInfo(start); d != null; d = d.Parent) { var p = Path.Combine(d.FullName, FileName); if (File.Exists(p)) return p; if (!searched.Contains(d.FullName)) searched.Add(d.FullName); }
        throw new SaveFormatException($"{FileName} was not found (looked in {string.Join(", ", searched.Take(4))}…).{HowTo}");
    }

    public static CryptoConfig Load(string path)
    {
        if (!File.Exists(path)) throw new SaveFormatException($"{FileName} not found at {path}.{HowTo}");
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            byte[] Get(string n) => Convert.FromHexString(doc.RootElement.GetProperty(n).GetString()!);
            var c = new CryptoConfig(Get("aes_key"), Get("aes_iv"), Get("chk_A"), Get("chk_B"));
            if (c.AesKey.Length != 16 || c.AesIv.Length != 16 || c.ChkA.Length != 256 || c.ChkB.Length != 32) throw new SaveFormatException($"{path} has the wrong key/table sizes.{HowTo}");
            return c;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or FormatException or InvalidOperationException or ArgumentNullException or IOException)
        { throw new SaveFormatException($"{path} is not a valid {FileName} ({ex.Message}).{HowTo}", ex); }
    }
}

public sealed class SaveFormatException(string message, Exception? inner = null) : Exception(message, inner);
