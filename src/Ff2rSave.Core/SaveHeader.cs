using System.Buffers.Binary;
using System.Text;

namespace Ff2rSave.Core;

/// <summary>256-byte container header (see docs/save_format.md). Wraps raw bytes so unknown fields round-trip untouched.</summary>
public sealed class SaveHeader
{
    public const int Size = 0x100;
    public byte[] Raw { get; }
    public SaveHeader(ReadOnlySpan<byte> raw)
    {
        if (raw.Length < Size) throw new SaveFormatException($"File too small for a header ({raw.Length} bytes).");
        Raw = raw[..Size].ToArray();
        if (!IsUser && !IsSystem) throw new SaveFormatException($"Bad magic '{Encoding.ASCII.GetString(Raw, 0, 8).TrimEnd('\0')}'; not a Fatal Frame II save.");
    }
    public bool IsUser => Raw.AsSpan(0, 8).SequenceEqual("WLNUSR\0\0"u8);
    public bool IsSystem => Raw.AsSpan(0, 8).SequenceEqual("WLNSYS\0\0"u8);
    public ulong Version { get => U64(0x08); set => P64(0x08, value); }
    public uint UserId { get => U32(0x10); set => P32(0x10, value); }
    public uint HeaderSize => U32(0x14);
    public uint PayloadLength { get => U32(0x18); set => P32(0x18, value); }
    public uint Chapter { get => U32(0x20); set => P32(0x20, value); }
    public uint Checkpoint { get => U32(0x24); set => P32(0x24, value); }
    public ulong PlayTime { get => U64(0x28); set => P64(0x28, value); }
    public DateTimeOffset Timestamp { get => DateTimeOffset.FromUnixTimeSeconds((long)U64(0x30)); set => P64(0x30, (ulong)value.ToUnixTimeSeconds()); }
    public byte EncryptedFlag => Raw[0x4C];

    public ulong GetUInt(int offset, int width) => width switch { 1 => Raw[offset], 4 => U32(offset), 8 => U64(offset), _ => throw new ArgumentException("width") };
    public void SetUInt(int offset, int width, ulong v)
    {
        if (width == 1) Raw[offset] = checked((byte)v); else if (width == 4) P32(offset, checked((uint)v)); else P64(offset, v);
    }
    uint U32(int o) => BinaryPrimitives.ReadUInt32LittleEndian(Raw.AsSpan(o));
    ulong U64(int o) => BinaryPrimitives.ReadUInt64LittleEndian(Raw.AsSpan(o));
    void P32(int o, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(Raw.AsSpan(o), v);
    void P64(int o, ulong v) => BinaryPrimitives.WriteUInt64LittleEndian(Raw.AsSpan(o), v);
}
