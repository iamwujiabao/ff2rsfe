"""Derive game-specific constants at runtime from the user's own FatalFrameII.exe (nothing is stored in the repo).

Located in the Steam build analysed in Stage 1 (RVAs; verified by self-test in ff2save.py):
  AES-128 key   : .data  rva 0x2c3f158 (16 B, static)       - consumed by key schedule @0xbceb30
  CBC IV        : .rdata rva 0x201c8b0 (16 B)                - copied to 0x70062d8 before encrypt @0xf3ee12
  checksum tabs : immediates built on the stack in function @0xc14ea0 (A: 256 B, B: 32 B)
"""
import sys, os, json, struct, re
sys.path.insert(0, os.path.dirname(__file__))
from pe import load_unpacked, code, ROOT

KEY_RVA, IV_RVA, CHK_FUNC, CHK_END = 0x2c3f158, 0x201c8b0, 0xc14ea0, 0xc15100

def derive(exe=None):
    import capstone
    im = load_unpacked()
    key = im.data[im.rva2off(KEY_RVA):][:16]; iv = im.data[im.rva2off(IV_RVA):][:16]
    md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64); md.detail = True
    mem = {}
    for ins in md.disasm(code(im, CHK_FUNC, CHK_END - CHK_FUNC), CHK_FUNC):
        if ins.mnemonic == "mov" and len(ins.operands) == 2 and ins.operands[0].type == 3 and ins.operands[0].size == 4 and ins.operands[1].type == 2:
            m = ins.operands[0].mem; base = ins.reg_name(m.base) if m.base else None
            if base in ("rsp", "rbp") and not m.index:
                off = m.disp + (0x100 if base == "rbp" else 0)       # rbp == rsp+0x100 in this function
                mem[off] = ins.operands[1].imm & 0xFFFFFFFF
    tab = bytearray(0x120)
    for off, v in mem.items():
        if 0 <= off < 0x120: struct.pack_into("<I", tab, off, v)
    return dict(aes_key=key.hex(), aes_iv=iv.hex(), chk_B=bytes(tab[:0x20]).hex(), chk_A=bytes(tab[0x20:0x120]).hex(), ndwords=len(mem))

def load(path=os.path.join(ROOT, "local_config.json")):
    if os.path.exists(path): return json.load(open(path))
    cfg = derive(); json.dump(cfg, open(path, "w"), indent=1); return cfg   # local_config.json is git-ignored

if __name__ == "__main__":
    c = load(); print({k: (v if k == "ndwords" else v[:16] + "...") for k, v in c.items()})
