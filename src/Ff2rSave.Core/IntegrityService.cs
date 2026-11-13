using System.Buffers.Binary;

namespace Ff2rSave.Core;

/// <summary>The game's custom 32-byte checksum (exe rva 0xc14ea0) and the loader's validation rules (exe rva 0xf3f1a0).</summary>
public sealed class IntegrityService(CryptoConfig cfg)
{
    public byte[] Chk32(ReadOnlySpan<byte> data)
    {
        Span<ulong> m8 = stackalloc ulong[8]; m8.Clear();
        for (int i = 0; i < data.Length; i++) m8[i & 7] += data[i];
        var lanes = new Dictionary<int, ulong> { [0] = m8.ToArray().Aggregate(0UL, (a, b) => a + b), [2] = m8[0] + m8[1] + m8[2] + m8[3], [4] = m8[4] + m8[5] + m8[6] + m8[7] };
        for (int m = 0; m < 4; m++) lanes[6 + 2 * m] = m8[m] + m8[m + 4];
        var s = new byte[32];
        foreach (var (o, v) in lanes) { s[o] = (byte)v; s[o + 1] = (byte)(v >> 8); }
        for (int k = 0; k < 31; k++) s[k + 1] = (byte)(s[k + 1] + s[k] + 2 * k);
        var r = new byte[32];
        for (int j = 0; j < 32; j++) r[j] = (byte)(cfg.ChkA[s[j]] + cfg.ChkB[j]);
        return r;
    }

    /// <summary>Recompute header 0x50 (payload checksum) then 0x70 (header checksum with 0x70..0x8F zeroed). Mutates header.</summary>
    public void Fix(SaveHeader h, ReadOnlySpan<byte> plaintext)
    {
        Chk32(plaintext).CopyTo(h.Raw, 0x50);
        Array.Clear(h.Raw, 0x70, 32);
        Chk32(h.Raw).CopyTo(h.Raw, 0x70);
    }

    /// <summary>Loader checks except the user-id comparison. Empty list == valid.</summary>
    public List<string> Verify(SaveHeader h, ReadOnlySpan<byte> plaintext, long fileLength)
    {
        var e = new List<string>();
        if (h.PayloadLength != fileLength - SaveHeader.Size) e.Add("payload length field != file size - 0x100");
        if (plaintext.Length < 8 || BinaryPrimitives.ReadUInt64LittleEndian(plaintext) != h.Version) e.Add("plaintext[0:8] != header version");
        if (!Chk32(plaintext).AsSpan().SequenceEqual(h.Raw.AsSpan(0x50, 32))) e.Add("payload checksum (0x50) mismatch");
        var t = (byte[])h.Raw.Clone(); Array.Clear(t, 0x70, 32);
        if (!Chk32(t).AsSpan().SequenceEqual(h.Raw.AsSpan(0x70, 32))) e.Add("header checksum (0x70) mismatch");
        return e;
    }
}
