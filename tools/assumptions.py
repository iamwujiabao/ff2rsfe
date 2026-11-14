"""assumptions.py : do the unique samples fit what the tool assumes? Prints a summary (no raw user IDs)."""
import sys, os, glob, hashlib, json, collections; sys.path.insert(0, os.path.dirname(__file__))
import keys, ff2save as F, savejson as S, inventory as INV

cfg = keys.load(); files = INV.find_files(); seen = {}
for f in files: seen.setdefault(hashlib.sha256(open(f, "rb").read()).hexdigest(), f)
fields = json.load(open(os.path.join(INV.ROOT, "docs", "fields.json")))["fields"]
U = []; SYS = []
for sha, f in seen.items():
    raw = open(f, "rb").read(); hdr, pt = F.decrypt(raw, cfg); _, _, obj, end = S.load(f, cfg)
    (U if hdr[:6] == b"WLNUSR" else SYS).append((f, raw, hdr, pt, obj, end))
u32 = lambda h, o, w=4: int.from_bytes(h[o:o+w], "little")
C = collections.Counter
def vals(seq): return dict(C(seq).most_common(8))
print(f"unique saves: {len(U)} user + {len(SYS)} system")
print("\n== header assumptions (user)")
print(" magic/size:", vals((h[:8], len(r)) for _, r, h, *_ in U))
print(" 0x08 version:", vals(hex(u32(h, 8, 8)) for _, _, h, *_ in U), "| 0x14:", vals(u32(h, 0x14) for _, _, h, *_ in U), "| 0x18==size-0x100:", all(u32(h, 0x18) == len(r) - 256 for _, r, h, *_ in U))
print(" 0x1C:", vals(u32(h, 0x1c) for _, _, h, *_ in U), "| 0x38:", vals(u32(h, 0x38) for _, _, h, *_ in U), "| 0x48:", vals(u32(h, 0x48) for _, _, h, *_ in U))
print(" 0x3C distinct:", sorted({u32(h, 0x3c) for _, _, h, *_ in U}), "\n 0x40 distinct:", vals(u32(h, 0x40, 8) for _, _, h, *_ in U))
print(" 0x4C zero?:", any(h[0x4c] == 0 for _, _, h, *_ in U), "| 0x90..0xFF all zero:", all(not any(h[0x90:]) for _, _, h, *_ in U), "| 0x2C..0x2F high word of 0x28 max:", max(u32(h, 0x2c) for _, _, h, *_ in U))
print(" hdr 0x20 == progress//1000:", all(u32(h, 0x20) == o["MissionData"]["main_mission_progress"] // 1000 for _, _, h, p, o, e in U), "| hdr 0x24 == json progress:", all(u32(h, 0x24) == o["MissionData"]["main_mission_progress"] for _, _, h, p, o, e in U))
d = [u32(h, 0x28, 8) - o["PlayerData"]["total_play_time"] for _, _, h, p, o, e in U]; print(f" hdr 0x28 - json total_play_time (header newer by): min {min(d)}, max {max(d)}  (all >= 0: {all(x >= 0 for x in d)})")
print(" plaintext[0:16] == version+8 zero bytes:", all(p[:16] == h[8:16] + b"\0" * 8 for _, _, h, p, o, e in U))
print(" first non-zero after JSON at pt offset:", vals(next(i for i in range(e, len(p)) if p[i]) for _, _, h, p, o, e in U), "| JSON bytes min/max:", min(e - 16 for *_, e in U), max(e - 16 for *_, e in U), "of cap 1,024,000")
print("\n== JSON structure (user)")
tops = C(tuple(o.keys()) for *_, o, e in U); print(" top-level key sets:", {len(k): v for k, v in tops.items()}, "| all equal to mine's:", len(tops) == 1)
ref = U[0][4]
for sec in ref:
    ks = C(tuple(o[sec].keys()) for *_, o, e in U if sec in o)
    if len(ks) > 1: print(f"  !! section {sec}: {len(ks)} different key sets")
def shape(x): return [len(x)] if isinstance(x, (list, dict)) else []
lens = collections.defaultdict(set)
def walk(n, p):
    if isinstance(n, dict):
        for k, v in n.items(): walk(v, p + "/" + k)
    elif isinstance(n, list):
        lens[p].add(len(n))
        for v in n[:1]: walk(v, p + "/*")
for *_, o, e in U: walk(o, "")
varlen = {p: sorted(v) for p, v in lens.items() if len(v) > 1}
print(" array paths whose LENGTH differs between saves:", len(varlen)); [print("   ", p, v[:6]) for p, v in list(varlen.items())[:8]]
types = collections.defaultdict(set)
def wt(n, p):
    if isinstance(n, dict):
        for k, v in n.items(): wt(v, p + "/" + k)
    elif isinstance(n, list):
        for i, v in enumerate(n[:3]): wt(v, p + "/*")
    else: types[p].add(type(n).__name__)
for *_, o, e in U: wt(o, "")
print(" paths with mixed leaf types across saves:", sum(1 for v in types.values() if len(v) > 1), [ (p, sorted(v)) for p, v in types.items() if len(v) > 1][:5])
def keyset(n, p, acc):
    if isinstance(n, dict):
        for k, v in n.items(): acc.add(p + "/" + k); keyset(v, p + "/" + k, acc)
    elif isinstance(n, list):
        for v in n[:1]: keyset(v, p + "/*", acc)
sets = []
for *_, o, e in U: a = set(); keyset(o, "", a); sets.append(a)
allk = set().union(*sets); inter = set.intersection(*sets)
print(f" distinct key-paths in union: {len(allk)}, in every save: {len(inter)}, only in some: {len(allk - inter)}", sorted(allk - inter)[:8])
# schema paths resolve in every save
sys.path.insert(0, os.path.dirname(__file__))
def res(o, path):
    cur = [o]
    for seg in path.strip("/").split("/"):
        nxt = []
        for c in cur:
            if seg == "*": nxt += list(c) if isinstance(c, list) else list(c.values())
            elif isinstance(c, list): nxt.append(c[int(seg)]) if int(seg) < len(c) else None
            elif isinstance(c, dict) and seg in c: nxt.append(c[seg])
        cur = nxt
        if not cur: return []
    return cur
miss = [(fd["id"], os.path.basename(os.path.dirname(f))) for f, r, h, p, o, e in U for fd in fields if fd["source"] == "json" and not res(o, fd["path"])]
print(" fields.json json paths that do NOT resolve in some save:", miss[:5] or "none")
print("\n== system saves")
print(" magic/size:", vals((h[:8], len(r)) for f, r, h, *_ in SYS), "| 0x18 ok:", all(u32(h, 0x18) == len(r) - 256 for f, r, h, *_ in SYS), "| 0x1C:", vals(u32(h, 0x1c) for f, r, h, *_ in SYS), "| plaintext prefix ok:", all(p[:16] == h[8:16] + b"\0" * 8 for f, r, h, p, o, e in SYS))
print(" top-level keys:", vals(tuple(o.keys()) for *_, o, e in SYS)); print(" JSON bytes:", sorted(e - 16 for *_, e in SYS))
for f, r, h, p, o, e in SYS[:1]:
    for k, v in o.items(): print(f"   {k}: {len(json.dumps(v, separators=(',',':'))):,} bytes, {len(v) if hasattr(v,'__len__') else ''} entries")
