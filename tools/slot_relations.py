"""slot_relations.py : SAVEDATA vs BACKUP across all sample slots (observational). No raw IDs are printed."""
import sys, os, hashlib, json, collections, re; sys.path.insert(0, os.path.dirname(__file__))
import keys, ff2save as F, savejson as S, inventory as INV
from diff_saves import flat

cfg = keys.load(); files = INV.find_files(); lab = INV.labels_for(files)
dirs = collections.defaultdict(dict)
for f in files: dirs[os.path.dirname(f)][os.path.basename(f)] = f
u = lambda h, o, w=4: int.from_bytes(h[o:o+w], "little")
slots = []; seen = set()
for d, fs in sorted(dirs.items()):
    if "SAVEDATA.BIN" not in fs: continue
    key = tuple(hashlib.sha256(open(fs[n], "rb").read()).hexdigest() if n in fs else None for n in ("SAVEDATA.BIN", "BACKUP.BIN"))
    if key in seen: continue               # same slot content seen under another source (the 100% save)
    seen.add(key)
    rel = os.path.relpath(d, INV.SAMPLES); L = lab[rel.split(os.sep)[0]]; tag = INV.set_tag(rel)
    slots.append(dict(label=L, set=tag, slot=INV.slot_of(fs["SAVEDATA.BIN"]), dir=d, files=fs, ident=(key[0] == key[1]) if key[1] else None, has_backup=key[1] is not None))
print(f"slot directories (unique content): {len(slots)}")
C = collections.Counter("no BACKUP" if not s["has_backup"] else ("identical" if s["ident"] else "differ") for s in slots); print(" ", dict(C))
print(" by source:", {k: dict(collections.Counter(("no BACKUP" if not s["has_backup"] else "identical" if s["ident"] else "differ") for s in slots if s["label"] == k)) for k in sorted({s["label"] for s in slots})})

print("\n== slots where SAVEDATA and BACKUP differ")
rows = []
for s in slots:
    if s["has_backup"] and not s["ident"]:
        sa = S.load(s["files"]["SAVEDATA.BIN"], cfg); sb = S.load(s["files"]["BACKUP.BIN"], cfg)
        (hA, _, oA, _), (hB, _, oB, _) = sa, sb
        dts = u(hA, 0x30, 8) - u(hB, 0x30, 8); dpt = (u(hA, 0x28, 8) - u(hB, 0x28, 8)) / 6e7; dprog = u(hA, 0x24) - u(hB, 0x24)
        fa, fb = dict(flat(oA)), dict(flat(oB)); nd = sum(1 for k in set(fa) | set(fb) if fa.get(k) != fb.get(k))
        sect = collections.Counter("/".join(k.split("/")[:3]) for k in set(fa) | set(fb) if fa.get(k) != fb.get(k))
        rows.append((s["label"], s["set"], s["slot"], dprog, dts, dpt, nd, sect.most_common(3), u(hB, 0x24), u(hA, 0x24)))
        print(f" {s['label']:9} {s['set']:13} {s['slot']:10}  BACKUP progress {u(hB,0x24)} -> SAVEDATA {u(hA,0x24)} (+{dprog}) | header time diff {dts:,} s ({dts/3600:.2f} h) | play-time diff {dpt:.1f} min | {nd} JSON values differ | top areas {sect.most_common(2)}")
        print(f"            BACKUP older on all three clocks (timestamp, play time, progress >=): {dts>0 and dpt>0 and dprog>=0}")
print("\n== slots without BACKUP.BIN:", [(s["label"], s["set"], s["slot"]) for s in slots if not s["has_backup"]])
print("   (system saves: no BACKUP.BIN exists for any of the", sum(1 for f in files if os.path.getsize(f) == 61851984), "system files)")

print("\n== identical slots: is SAVEDATA's own timestamp consistent with play time ordering within a source/set? (info only)")
idn = [s for s in slots if s["ident"]]; print(" identical slots:", len(idn), "of", sum(1 for s in slots if s["has_backup"]), "slots that have a BACKUP")

print("\n== hypothesis check, per slot that has both files (uncontrolled)")
n_both = sum(1 for s in slots if s["has_backup"]); n_ident = len(idn); n_diff = n_both - n_ident
print(f" slots with both files: {n_both}; identical: {n_ident}; differ: {n_diff}; BACKUP newer than SAVEDATA: 0 of {n_diff}")
