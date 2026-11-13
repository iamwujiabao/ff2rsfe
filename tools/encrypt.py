"""encrypt.py IN.dec OUT.BIN [--no-fix]  -> recompute checksums (0x50, 0x70), AES-CBC encrypt, back up OUT if it exists, re-read and verify."""
import sys, os, shutil, time; sys.path.insert(0, os.path.dirname(__file__))
import keys, ff2save as F
if __name__ == "__main__":
    src, dst = sys.argv[1:3]; F.guard_output(dst); cfg = keys.load()
    d = open(src, "rb").read(); hdr, pt = d[:F.HDR], d[F.HDR:]
    if "--no-fix" not in sys.argv: hdr = F.fix_checksums(hdr, pt, cfg)
    out = F.encrypt(hdr, pt, cfg)
    if os.path.exists(dst): b = f"{dst}.bak-{time.strftime('%Y%m%d-%H%M%S')}"; shutil.copy2(dst, b); print("backup:", b)
    open(dst, "wb").write(out)
    back = open(dst, "rb").read(); errs = F.verify(back, cfg); assert back == out
    print(f"wrote {dst} ({len(out)} B); re-read verification: {errs or 'OK'}")
