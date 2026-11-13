"""Stage 1: parse and validate the 256-byte save header across all samples."""
import struct, sys, glob, os, math, hashlib, datetime, collections

ROOT = os.path.join(os.path.dirname(__file__), "..")

def files():
    return sorted(glob.glob(os.path.join(ROOT, "samples", "*", "*", "*.BIN")))

def entropy(b):
    c = collections.Counter(b); n = len(b)
    return -sum(v/n*math.log2(v/n) for v in c.values())

def parse(path):
    with open(path, "rb") as f:
        d = f.read()
    h = d[:0x100]
    u = lambda o: struct.unpack_from("<I", h, o)[0]
    return dict(path=os.path.relpath(path, ROOT), size=len(d), magic=h[:8], ver=u(8), hdrsize=u(0xC),
                u10=u(0x10), u14=u(0x14), plen=struct.unpack_from("<Q", h, 0x18)[0], u20=u(0x20), u24=u(0x24),
                u28=u(0x28), u2c=u(0x2C), ts=u(0x30), u34=u(0x34), u38=u(0x38), u3c=u(0x3C),
                u40=u(0x40), u44=u(0x44), u48=u(0x48), u4c=u(0x4C),
                blob=h[0x50:0x90], tail_zero=not any(h[0x90:]), plen_ok=None, d=d)

if __name__ == "__main__":
    for p in files():
        r = parse(p)
        r["plen_ok"] = (r["plen"] == r["size"] - 0x100) and (r["size"] - 0x100) % 16 == 0
        ts = datetime.datetime.fromtimestamp(r["ts"], datetime.UTC).isoformat() if r["ts"] else "-"
        print(f'{r["path"]}\n  magic={r["magic"]} ver={r["ver"]:#x} hdr={r["hdrsize"]:#x} plen={r["plen"]:#x} ok={r["plen_ok"]} tail0={r["tail_zero"]}')
        print(f'  10={r["u10"]:#x} 14={r["u14"]:#x} 20={r["u20"]} 24={r["u24"]} 28={r["u28"]:#010x} 2c={r["u2c"]:#x} 30={r["ts"]} ({ts}) 34={r["u34"]:#x} 38={r["u38"]:#x} 3c={r["u3c"]}')
        print(f'  40={r["u40"]:#010x} 44={r["u44"]} 48={r["u48"]:#x} 4c={r["u4c"]:#x} blob={r["blob"][:8].hex()}.. payloadH={entropy(r["d"][0x100:0x100+1<<20]):.4f}')
