"""Fatal Frame II Remake save codec (Stage 1 findings). Constants come from local_config.json (derived from the user's exe)."""
import struct, sys, os
sys.path.insert(0, os.path.dirname(__file__))
from Crypto.Cipher import AES
import keys

HDR = 0x100

def chk32_slow(data: bytes, cfg) -> bytes:
    """Literal per-byte transcription of exe @0xc14ea0 (reference for testing chk32)."""
    A = bytes.fromhex(cfg["chk_A"]); B = bytes.fromhex(cfg["chk_B"]); S = [0] * 32
    def add16(o, b):
        v = (S[o] | S[o + 1] << 8) + b; S[o] = v & 255; S[o + 1] = (v >> 8) & 255
    for i, b in enumerate(data):
        add16(0, b); add16(2 + 2 * ((i >> 2) & 1), b); add16(6 + 2 * (i & 3), b)
    for k in range(31): S[k + 1] = (S[k + 1] + S[k] + 2 * k) & 255
    return bytes((A[S[j]] + B[j]) & 255 for j in range(32))

def chk32(data: bytes, cfg) -> bytes:
    """Same function in closed form: every 16-bit lane is a sum of bytes over a position class (i%4 / i%8 halves)."""
    A = bytes.fromhex(cfg["chk_A"]); B = bytes.fromhex(cfg["chk_B"]); S = [0] * 32
    m4 = [sum(data[m::4]) for m in range(4)]
    lanes = {0: sum(m4), 2: sum(sum(data[j::8]) for j in range(4)), 4: sum(sum(data[j::8]) for j in range(4, 8))}
    for m in range(4): lanes[6 + 2 * m] = m4[m]
    for o, v in lanes.items(): S[o] = v & 255; S[o + 1] = (v >> 8) & 255
    for k in range(31): S[k + 1] = (S[k + 1] + S[k] + 2 * k) & 255
    return bytes((A[S[j]] + B[j]) & 255 for j in range(32))

def decrypt(raw: bytes, cfg) -> tuple[bytes, bytes]:
    hdr, ct = raw[:HDR], raw[HDR:]
    return hdr, AES.new(bytes.fromhex(cfg["aes_key"]), AES.MODE_CBC, bytes.fromhex(cfg["aes_iv"])).decrypt(ct)

def encrypt(hdr: bytes, pt: bytes, cfg) -> bytes:
    return hdr + AES.new(bytes.fromhex(cfg["aes_key"]), AES.MODE_CBC, bytes.fromhex(cfg["aes_iv"])).encrypt(pt)


def fix_checksums(hdr: bytes, pt: bytes, cfg) -> bytes:
    """Recompute header+0x50 (payload checksum) then +0x70 (header checksum, computed with +0x70..0x8F zeroed)."""
    h = bytearray(hdr); h[0x50:0x70] = chk32(pt, cfg); h[0x70:0x90] = b"\0" * 32
    h[0x70:0x90] = chk32(bytes(h), cfg); return bytes(h)

def verify(raw: bytes, cfg) -> list[str]:
    """Replicates the loader's checks (exe @0xf3f1a0). Returns a list of problems (empty == loader-valid, ignoring the user-ID check)."""
    errs = []; hdr, pt = decrypt(raw, cfg)
    if hdr[:8] not in (b"WLNUSR\0\0", b"WLNSYS\0\0"): errs.append("bad magic")
    if struct.unpack_from("<I", hdr, 0x18)[0] != len(raw) - HDR: errs.append("payload length field != file size - 0x100")
    if pt[:8] != hdr[8:16]: errs.append("plaintext[0:8] != header version qword")
    if chk32(pt, cfg) != hdr[0x50:0x70]: errs.append("payload checksum (0x50) mismatch")
    t = bytearray(hdr); t[0x70:0x90] = b"\0" * 32
    if chk32(bytes(t), cfg) != hdr[0x70:0x90]: errs.append("header checksum (0x70) mismatch")
    return errs

def guard_output(path):
    """Safety rule: never write into samples/ or the real game save folder."""
    p = os.path.abspath(path).lower()
    if os.sep + "samples" + os.sep in p or "koeitecmo" in p: raise SystemExit(f"refusing to write to {path}: samples/ and the real save folder are read-only")
