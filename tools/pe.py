"""PE helpers for FatalFrameII.exe analysis (reads only; never writes the exe)."""
import os, re, struct, pefile
ROOT = os.path.join(os.path.dirname(__file__), "..")
EXE = os.path.join(ROOT, "game", "FatalFrameII.exe")

class Img:
    def __init__(self, path=EXE):
        self.data = open(path, "rb").read()
        self.pe = pefile.PE(data=self.data, fast_load=True)
        self.base = self.pe.OPTIONAL_HEADER.ImageBase
        self.secs = [(s.Name.rstrip(b"\0").decode(), s.VirtualAddress, s.Misc_VirtualSize, s.PointerToRawData, s.SizeOfRawData) for s in self.pe.sections]
    def rva2off(self, rva):
        for n, va, vs, po, rs in self.secs:
            if va <= rva < va + max(vs, rs): return po + rva - va
    def off2rva(self, off):
        for n, va, vs, po, rs in self.secs:
            if po <= off < po + rs: return va + off - po
    def read_va(self, va, n): o = self.rva2off(va - self.base); return self.data[o:o+n]
    def sec(self, name): return next(s for s in self.secs if s[0] == name)
    def lea_xrefs(self, target_rva):
        """offsets of RIP-relative lea/mov/cmp etc whose disp32 resolves to target_rva (text scan, heuristic)."""
        n, va, vs, po, rs = self.sec(".text"); t = self.text if hasattr(self, "text") else self.data[po:po+rs]; out = []
        for m in re.finditer(rb"[\x40-\x4f][\x8d\x8b\x89\x3b\x39\x63\x03][\x05\x0d\x15\x1d\x25\x2d\x35\x3d]", t, re.S):
            i = m.start(); disp = struct.unpack_from("<i", t, i + 3)[0]
            if va + i + 7 + disp == target_rva: out.append(va + i)
        return out


def steamstub_header(im):
    """Decode the SteamStub v3.1 x64 header sitting before the entry point (rolling-XOR)."""
    ep = im.pe.OPTIONAL_HEADER.AddressOfEntryPoint; o = im.rva2off(ep) - 0xF0
    raw = bytearray(im.data[o:o+0xF0]); key = 0
    for i in range(0, 0xF0, 4):
        v = struct.unpack_from("<I", raw, i)[0]; struct.pack_into("<I", raw, i, v ^ key); key = v
    assert struct.unpack_from("<I", raw, 4)[0] == 0xC0DEC0DF, "not SteamStub 3.1"
    u32 = lambda i: struct.unpack_from("<I", raw, i)[0]; u64 = lambda i: struct.unpack_from("<Q", raw, i)[0]
    return dict(oep=u64(0x20), code_va=u64(0x48), code_size=u64(0x50), aes_key=bytes(raw[0x58:0x78]),
                aes_iv=bytes(raw[0x78:0x88]), stolen=bytes(raw[0x88:0x98]), raw=bytes(raw))

def decrypt_text(im, iv_mode):
    """In-memory only. iv_mode: 'raw' | 'ecb_enc' | 'ecb_dec' (how the stub derives the CBC IV)."""
    from Crypto.Cipher import AES
    h = steamstub_header(im); k = h["aes_key"]; iv = h["aes_iv"]
    if iv_mode == "ecb_enc": iv = AES.new(k, AES.MODE_ECB).encrypt(iv)
    if iv_mode == "ecb_dec": iv = AES.new(k, AES.MODE_ECB).decrypt(iv)
    o = im.rva2off(h["code_va"]); ct = im.data[o:o+h["code_size"]]
    return AES.new(k, AES.MODE_CBC, iv).decrypt(ct), h


def load_unpacked():
    """Img with .text decrypted in memory (analysis only; nothing is written to disk)."""
    im = Img(); pt, h = decrypt_text(im, "raw"); im.text = (h["stolen"] + pt)[:len(pt)]; im.stub = h  # empirical: stolen bytes are PREPENDED (validated by call-target hit rate)
    n, va, vs, po, rs = im.sec(".text"); assert h["code_va"] == va and len(im.text) == rs
    return im

def code(im, rva, n):
    va0 = im.sec(".text")[1]; return im.text[rva - va0: rva - va0 + n]


def functions(im):
    """RUNTIME_FUNCTION table -> sorted [(begin_rva, end_rva)]."""
    d = im.pe.OPTIONAL_HEADER.DATA_DIRECTORY[3]; o = im.rva2off(d.VirtualAddress); out = []
    for i in range(d.Size // 12):
        b, e, _ = struct.unpack_from("<III", im.data, o + 12 * i); out.append((b, e))
    return sorted(out)

def func_at(im, rva):
    import bisect
    fs = getattr(im, "_fs", None) or functions(im); im._fs = fs
    i = bisect.bisect_right([b for b, e in fs], rva) - 1
    return fs[i] if fs[i][0] <= rva < fs[i][1] else None

def disasm(im, begin, end, annotate=True):
    import capstone
    md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64); md.detail = False
    lines = []
    for ins in md.disasm(code(im, begin, end - begin), im.base + begin):
        note = ""
        if annotate and ins.op_str.startswith(("rip", "qword ptr [rip", "dword ptr [rip")) or "[rip" in ins.op_str:
            m = re.search(r"rip ([+-]) (0x[0-9a-f]+)", ins.op_str)
            if m:
                t = ins.address + ins.size + (int(m.group(2), 16) * (1 if m.group(1) == "+" else -1)) - im.base
                o = im.rva2off(t)
                if o is not None:
                    s = re.match(rb"[ -~]{4,40}", im.data[o:o+48]); w = im.data[o:o+80]
                    ws = re.match(rb"(?:[ -~]\0){4,30}", w)
                    note = "  ; ->%#x %s" % (t, repr(s.group(0)) if s else (repr(ws.group(0).decode("utf-16le")) if ws else ""))
        lines.append("%08x  %-6s %s%s" % (ins.address - im.base, ins.mnemonic, ins.op_str, note))
    return "\n".join(lines)


def find_calls(im, target_rva):
    """All `call rel32` / `jmp rel32` sites whose target is target_rva (text scan; heuristic)."""
    va0 = im.sec(".text")[1]; t = im.text; out = []
    for m in re.finditer(rb"[\xe8\xe9]", t):
        i = m.start()
        if i + 5 <= len(t) and va0 + i + 5 + struct.unpack_from("<i", t, i + 1)[0] == target_rva: out.append((va0 + i, t[i]))
    return out
