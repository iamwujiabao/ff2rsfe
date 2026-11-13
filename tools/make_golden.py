"""Writes tests/golden.json: Python-side SHA-256 of decrypted plaintext + checksums per sample (hashes only, no game data) for Python/C# parity tests."""
import sys, os, glob, json, hashlib; sys.path.insert(0, os.path.dirname(__file__))
import keys, ff2save as F
cfg = keys.load(); out = {}
for p in sorted(glob.glob("samples/*/*/*.BIN")):
    raw = open(p, "rb").read(); h, pt = F.decrypt(raw, cfg)
    out[p[len("samples/"):]] = {"size": len(raw), "plaintext_sha256": hashlib.sha256(pt).hexdigest(), "chk_payload": F.chk32(pt, cfg).hex(), "header_chk": h[0x70:0x90].hex()}
json.dump(out, open("tests/golden.json", "w"), indent=1); print(len(out), "entries")
