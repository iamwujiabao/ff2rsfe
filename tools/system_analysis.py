"""system_analysis.py : structure + differences of the system saves (SYSTEMSAVEDATA00). Prints anonymous labels only."""
import sys, os, hashlib, json, collections, re, pickle; sys.path.insert(0, os.path.dirname(__file__))
import keys, ff2save as F, savejson as S, inventory as INV
from diff_saves import flat
cfg = keys.load(); files = INV.find_files(); lab = INV.labels_for(files); seen = collections.OrderedDict()
for f in files:
    raw = open(f, "rb").read()
    if raw[:6] != b"WLNSYS": continue
    seen.setdefault(hashlib.sha256(raw).hexdigest(), []).append(f)
def variant(path):   # uploader's descriptive tag on the folder name, e.g. 'SYSTEMSAVEDATA00 - NG+' -> 'NG+'
    b = os.path.basename(os.path.dirname(path)); return b.split(" - ", 1)[1] if " - " in b else ""
recs = []
for sha, paths in seen.items():
    f = paths[0]; raw = open(f, "rb").read(); hdr, pt = F.decrypt(raw, cfg); _, _, obj, end = S.load(f, cfg)
    rel = os.path.relpath(f, INV.SAMPLES); L = lab[rel.split(os.sep)[0]]
    name = f"{L[-1].upper() if L.startswith('player') else L}" + (f"/{variant(f)}" if variant(f) else f"/{INV.set_tag(rel) or '-'}")
    first_nz = next((i for i in range(end, len(pt)) if pt[i]), None); tail = len(pt) - end; nz_after = sum(1 for b in pt[end:] if b)
    recs.append(dict(name=name, sha=sha[:8], also=[lab[os.path.relpath(p, INV.SAMPLES).split(os.sep)[0]] + "/" + (variant(p) or INV.set_tag(os.path.relpath(p, INV.SAMPLES)) or "-") for p in paths[1:]],
                     ts=int.from_bytes(hdr[0x30:0x38], "little"), obj=obj, end=end, first_nz=first_nz, nz_after=nz_after, tail=tail, leaves=dict(flat(obj)), pt_len=len(pt), problems=F.verify(raw, cfg),
                     hdr={o: int.from_bytes(hdr[o:o+w], "little") for o, w in ((0x1c, 4), (0x20, 4), (0x24, 4), (0x28, 8), (0x38, 4), (0x3c, 4), (0x40, 8), (0x48, 4), (0x4c, 1))}))
pickle.dump(recs, open(os.path.join(INV.ROOT, "work", "system_corpus.pkl"), "wb"))
print(f"unique system saves: {len(recs)} (files on disk: {sum(len(v) for v in seen.values())})")
for r in recs: print(f"  {r['name']:22} sha {r['sha']} same as {r['also'] or '-'} | loader problems: {r['problems'] or 'none'} | JSON {r['end']-16:,} B, then {r['tail']:,} B of which {r['nz_after']:,} are non-zero (first at {r['first_nz']}) | leaves {len(r['leaves']):,}")
print("\n== header fields that are non-zero in system saves:", {hex(o): sorted({r['hdr'][o] for r in recs}) for o in (0x1c, 0x20, 0x24, 0x38, 0x3c, 0x40, 0x48)}, "| 0x28 (u64):", sorted({r['hdr'][0x28] for r in recs}), "| 0x4c always non-zero:", all(r["hdr"][0x4c] for r in recs))
print("\n== layout: first non-zero byte after the JSON (pt offset) per save:", [r["first_nz"] for r in recs], "| JSON end:", [r["end"] for r in recs])
print("\n== top-level sections (same for all?)", len({tuple(r['obj']) for r in recs}) == 1, list(recs[0]["obj"]))
for k, v in recs[0]["obj"].items():
    sub = ", ".join(f"{kk}({len(vv) if hasattr(vv,'__len__') and not isinstance(vv,str) else type(vv).__name__})" for kk, vv in list(v.items())[:60]) if isinstance(v, dict) else type(v).__name__
    print(f" {k}: {len(json.dumps(v, separators=(',',':'))):,} B, {len(v)} keys, leaves {sum(1 for p in recs[0]['leaves'] if p.startswith('/'+k+'/')):,}\n    {sub[:900]}")
