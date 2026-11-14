"""id_analysis.py : header user-ID facts across samples. Prints groups/digit-counts/booleans only (never raw IDs)."""
import sys, os, hashlib, re, struct, collections; sys.path.insert(0, os.path.dirname(__file__))
import keys, ff2save as F, savejson as S, inventory as INV
cfg = keys.load(); files = INV.find_files(); lab = INV.labels_for(files); seen = {}
for f in files: seen.setdefault(hashlib.sha256(open(f, "rb").read()).hexdigest(), f)
uid = lambda h: struct.unpack_from("<I", h, 0x10)[0]
rows = []
for sha, f in seen.items():
    raw = open(f, "rb").read(); hdr, pt = F.decrypt(raw, cfg); L = lab[os.path.relpath(f, INV.SAMPLES).split(os.sep)[0]]
    rows.append((L, "system" if hdr[:6] == b"WLNSYS" else "user", uid(hdr), pt, f))
grp = {}
for L, k, u, pt, f in rows: grp.setdefault(u, f"ID-{chr(ord('a') + len(grp))}")
print("== distribution of header user IDs (unique files): source -> ID group (digits), counts by file kind")
dist = collections.defaultdict(collections.Counter)
for L, k, u, pt, f in rows: dist[(L, grp[u], len(str(u)))][k] += 1
for (L, g, nd), c in sorted(dist.items()): print(f"  {L:9} {g} ({nd} digits)  user saves {c['user']:3}  system saves {c['system']:2}")
print("  groups: ", {g: sum(1 for r in rows if grp[r[2]] == g) for g in grp.values()}, "| groups shared by more than one source:", {g: sorted({r[0] for r in rows if grp[r[2]] == g}) for g in grp.values() if len({r[0] for r in rows if grp[r[2]] == g}) > 1})
short = [g for u, g in grp.items() if len(str(u)) <= 5]; print("  IDs with <= 5 digits:", short, "| as a 32-bit value all <= 2^32-1:", all(u <= 0xFFFFFFFF for L, k, u, pt, f in rows))
print("\n== readme claims vs headers (booleans only)")
def readme_number(label):   # read the number from the uploader's own readme at runtime; never stored in the repo
    for dp, dn, fn in os.walk(INV.SAMPLES):
        for f in fn:
            if f.endswith(".txt") and lab.get(os.path.relpath(os.path.join(dp, f), INV.SAMPLES).split(os.sep)[0]) == label:
                m = re.search(r"in my case it'?s\s+(\d{6,})", open(os.path.join(dp, f), errors="replace").read())
                if m: return m.group(1)
txt = readme_number("player D")
print("  player D readme names a folder number:", txt is not None, "| equals header ID of player D:", any(str(u) == txt for L, k, u, pt, f in rows if L == "player D"), "| its digit count matches the header's:", {len(str(u)) for L, k, u, pt, f in rows if L == "player D"} == {len(txt or "")})
print("  player B readme says '10 digit number'; player B header digits:", {len(str(u)) for L, k, u, pt, f in rows if L == "player B"})
print("\n== is the header ID stored anywhere in the decrypted plaintext? (LE u32 bytes / decimal text / hex text)")
for L, k, u, pt, f in rows[:0]: pass
res = collections.defaultdict(lambda: [0, 0, 0, 0])
for L, k, u, pt, f in rows:
    r = res[(grp[u], len(str(u)))]; r[0] += 1; r[1] += struct.pack("<I", u) in pt; r[2] += str(u).encode() in pt; r[3] += (f"{u:08x}".encode() in pt.lower())
for (g, nd), (n, b, d, hx) in sorted(res.items()): print(f"  {g} ({nd} digits): files {n}; LE bytes found in {b}; decimal text in {d}; hex text in {hx}")
print("\n== JSON key names mentioning user/steam/account/id (any save)")
names = collections.Counter()
for L, k, u, pt, f in rows:
    o = S.load(f, cfg)[2]
    def walk(n, p):
        if isinstance(n, dict):
            for key, v in n.items():
                if re.search(r"user|steam|account|player_?id|uid|owner", key, re.I): names[p + "/" + key] += 1
                walk(v, p + "/" + key)
        elif isinstance(n, list):
            for v in n[:1]: walk(v, p + "/*")
    walk(o, "")
print("  ", dict(names) or "none")
