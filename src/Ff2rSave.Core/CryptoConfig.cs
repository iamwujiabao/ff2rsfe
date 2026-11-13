using System.Text.Json;

namespace Ff2rSave.Core;

/// <summary>Game-derived constants. Loaded from git-ignored local_config.json (produced by tools/keys.py from the user's own exe); never embedded.</summary>
public sealed record CryptoConfig(byte[] AesKey, byte[] AesIv, byte[] ChkA, byte[] ChkB)
{
    public static CryptoConfig Load(string path)
    {
        if (!File.Exists(path)) throw new SaveFormatException($"Config not found: {path}. Run tools/keys.py against your own FatalFrameII.exe first.");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        byte[] Get(string n) => Convert.FromHexString(doc.RootElement.GetProperty(n).GetString()!);
        var c = new CryptoConfig(Get("aes_key"), Get("aes_iv"), Get("chk_A"), Get("chk_B"));
        if (c.AesKey.Length != 16 || c.AesIv.Length != 16 || c.ChkA.Length != 256 || c.ChkB.Length != 32) throw new SaveFormatException("Config has wrong key/table sizes.");
        return c;
    }
}

public sealed class SaveFormatException(string message, Exception? inner = null) : Exception(message, inner);
