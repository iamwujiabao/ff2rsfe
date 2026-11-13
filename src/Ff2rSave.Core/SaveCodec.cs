using System.Security.Cryptography;

namespace Ff2rSave.Core;

/// <summary>AES-128-CBC, no padding, fixed key+IV for every save (Stage 1 findings).</summary>
public sealed class SaveCodec(CryptoConfig cfg)
{
    public (SaveHeader Header, byte[] Plaintext) Decrypt(ReadOnlySpan<byte> raw)
    {
        var header = new SaveHeader(raw);
        var ct = raw[SaveHeader.Size..];
        if (ct.Length == 0 || ct.Length % 16 != 0) throw new SaveFormatException($"Payload length {ct.Length} is not a positive multiple of 16 (truncated or corrupted file).");
        using var aes = Aes.Create(); aes.Key = cfg.AesKey;
        return (header, aes.DecryptCbc(ct, cfg.AesIv, PaddingMode.None));
    }

    public byte[] Encrypt(SaveHeader header, ReadOnlySpan<byte> plaintext)
    {
        using var aes = Aes.Create(); aes.Key = cfg.AesKey;
        var ct = aes.EncryptCbc(plaintext, cfg.AesIv, PaddingMode.None);
        var o = new byte[SaveHeader.Size + ct.Length]; header.Raw.CopyTo(o, 0); ct.CopyTo(o, SaveHeader.Size); return o;
    }
}
