"""decrypt.py IN.BIN OUT.dec  -> OUT = 256-byte original header + decrypted payload (same length as IN)."""
import sys, os; sys.path.insert(0, os.path.dirname(__file__))
import keys, ff2save as F
if __name__ == "__main__":
    src, dst = sys.argv[1:3]; F.guard_output(dst); cfg = keys.load()
    raw = open(src, "rb").read(); hdr, pt = F.decrypt(raw, cfg)
    open(dst, "wb").write(hdr + pt); print(f"{src} -> {dst} ({len(raw)} B); loader problems: {F.verify(raw, cfg) or 'none'}")
