"""corpus.py : load every unique user save (dedupe by SHA-256) into a git-ignored pickle cache (work/corpus.pkl).
Each record: label (anonymous), set tag, slot, kind, header ints, flat JSON leaves {path: value}, raw item rows. No raw IDs are kept (header 0x10 is replaced by a group letter)."""
import sys, os, hashlib, pickle, struct; sys.path.insert(0, os.path.dirname(__file__))
import keys, ff2save as F, savejson as S, inventory as INV
from diff_saves import flat
CACHE = os.path.join(INV.ROOT, "work", "corpus.pkl")

def build():
    cfg = keys.load(); files = INV.find_files(); lab = INV.labels_for(files); seen = {}; recs = []; groups = {}
    for f in files:
        h = hashlib.sha256(open(f, "rb").read()).hexdigest()
        if h in seen: continue
        seen[h] = f; hdr, pt = F.decrypt(open(f, "rb").read(), cfg)
        if hdr[:6] != b"WLNUSR": continue
        _, _, obj, _ = S.load(f, cfg); rel = os.path.relpath(f, INV.SAMPLES)
        u = lambda o, w=4: int.from_bytes(hdr[o:o+w], "little")
        gid = groups.setdefault(u(0x10), "ID-" + chr(97 + len(groups)))
        recs.append(dict(sha=h[:8], label=lab[rel.split(os.sep)[0]], set=INV.set_tag(rel), slot=INV.slot_of(f), file=os.path.basename(f), idgrp=gid,
                         hdr={k: u(o, w) for k, o, w in (("lap", 0x1c, 4), ("chapter", 0x20, 4), ("progress", 0x24, 4), ("play", 0x28, 8), ("ts", 0x30, 8), ("u38", 0x38, 4), ("u3c", 0x3c, 4), ("u40", 0x40, 8), ("u48", 0x48, 4), ("flag4c", 0x4c, 1))},
                         leaves=dict(flat(obj)), items=[(sec, i, {k: v for k, v in e["ItemObjectData"].items() if k != "seed_info"}) for sec in ("possession_items", "storage_items") for i, e in enumerate(obj["PossessionItemData"][sec])]))
    os.makedirs(os.path.dirname(CACHE), exist_ok=True); pickle.dump(recs, open(CACHE, "wb")); return recs

def load():
    return pickle.load(open(CACHE, "rb")) if os.path.exists(CACHE) else build()

def group(r):
    """Analysis group: anonymous source + the uploader's set tag (a hint, not proof)."""
    return f"{r['label']}" + (f"/{r['set']}" if r["set"] else "")

if __name__ == "__main__":
    recs = build(); import collections
    print(len(recs), "unique user saves cached; groups:", dict(collections.Counter(group(r) for r in recs)))
