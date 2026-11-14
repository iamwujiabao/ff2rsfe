"""Writes tests/golden.json: Python-side results per sample file, KEYED BY THE FILE'S SHA-256 (no paths, no uploader names), for Python/C# parity tests.
Values are hashes/checksums only: no game or save data."""
import sys, os, hashlib, json; sys.path.insert(0, os.path.dirname(__file__))
import keys, ff2save as F, inventory as INV
cfg = keys.load(); out = {}
for p in INV.find_files():
    raw = open(p, "rb").read(); key = hashlib.sha256(raw).hexdigest()
    if key in out: continue
    h, pt = F.decrypt(raw, cfg)
    out[key] = {"size": len(raw), "plaintext_sha256": hashlib.sha256(pt).hexdigest(), "chk_payload": F.chk32(pt, cfg).hex(), "header_chk": h[0x70:0x90].hex()}
json.dump(out, open(os.path.join(os.path.dirname(__file__), "..", "tests", "golden.json"), "w"), indent=1, sort_keys=True); print(len(out), "entries")
