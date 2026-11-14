"""correlate.py : candidate fields vs progress / uploader set / lap / difficulty / endings. Groups: anonymous source + uploader's set tag (hints, not proof)."""
import sys, os, collections, math; sys.path.insert(0, os.path.dirname(__file__))
import corpus
recs = corpus.load(); G = collections.OrderedDict()
for r in recs: G.setdefault(corpus.group(r), []).append(r)
print("groups:", {g: len(v) for g, v in G.items()})
L = lambda r, p: r["leaves"].get(p)
def by_group(label, getter):
    print(f"\n{label}")
    for g, rs in G.items():
        c = collections.Counter(getter(r) for r in rs); print(f"   {g:22} n={len(rs):2}  " + ", ".join(f"{k}: {v}/{len(rs)}" for k, v in sorted(c.items(), key=lambda kv: str(kv[0]))))
print("\n== equality / identities across all", len(recs), "saves")
print(" senki == sen in every save:", all(L(r, "/PlayerData/senki") == L(r, "/PlayerData/sen") for r in recs))
print(" hdr lap == lap_num == last_selected_lap in every save:", all(r["hdr"]["lap"] == L(r, "/MissionData/lap_num") == L(r, "/MissionData/last_selected_lap_num_") for r in recs))
for name, p in (("lap_num", "/MissionData/lap_num"),):
    by_group(name, lambda r: L(r, p))
by_group("record.current_end (endings?)", lambda r: L(r, "/PlayRecordData/current_end_"))
by_group("record.ngp_tutorial", lambda r: L(r, "/PlayRecordData/is_new_game_plus_tutorial_compl"))
for i in range(4): by_group(f"difficulty_clear_flag_[{i}]", lambda r, i=i: L(r, f"/PlayRecordData/difficulty_clear_flag_/{i}"))
by_group("amulet slots unlocked", lambda r: L(r, "/PlayerData/unlock_amulet_slot_num"))
by_group("current_film_slot", lambda r: L(r, "/PlayerData/current_film_slot"))
by_group("hdr 0x38", lambda r: r["hdr"]["u38"]); by_group("hdr 0x48", lambda r: r["hdr"]["u48"])
by_group("is_chapter_select_notice_complete", lambda r: L(r, "/PlayRecordData/is_chapter_select_notice_comple"))
by_group("vengeance_unlocked_", lambda r: L(r, "/MissionData/vengeance_unlocked_"))
print("\n== play time (hours) by group: min / median / max")
for g, rs in G.items():
    v = sorted(r["hdr"]["play"] / 3.6e9 for r in rs); print(f"   {g:22} {v[0]:6.1f} / {v[len(v)//2]:6.1f} / {v[-1]:6.1f}")
print("\n== header fields as functions of progress (does the same progress always give the same value?)")
byp = collections.defaultdict(list)
for r in recs: byp[r["hdr"]["progress"]].append(r)
for k in ("u3c", "u38", "u40", "u48", "chapter"):
    conflicts = [p for p, rs in byp.items() if len({r["hdr"][k] for r in rs}) > 1]; multi = sum(1 for rs in byp.values() if len(rs) > 1)
    print(f"   0x{ {'u3c':'3C','u38':'38','u40':'40','u48':'48','chapter':'20'}[k] }: progress values with >1 save: {multi}; of those, with differing {k}: {len(conflicts)}", conflicts[:4])
# generic: what distinguishes the two same-player practice sets?
def scalar_paths(rs): 
    ps = set(rs[0]["leaves"]); [ps.intersection_update(r["leaves"]) for r in rs]; return ps
A, B = G["player B/nm-practice"], G["player B/ngp-practice"]
common = scalar_paths(A) & scalar_paths(B); sep = []
for p in common:
    va = {r["leaves"][p] for r in A}; vb = {r["leaves"][p] for r in B}
    if len(va) <= 2 and len(vb) <= 2 and not (va & vb) and not any(isinstance(x, float) for x in va | vb): sep.append((p, sorted(map(str, va)), sorted(map(str, vb))))
print(f"\n== paths whose values perfectly separate player B's two sets (<=2 distinct values each, no overlap): {len(sep)}")
agg = collections.Counter("/".join(p.split("/")[:3]) for p, *_ in sep)
print("   by area:", dict(agg.most_common(10)))
for p, va, vb in sorted(sep)[:25]: print(f"   {p[:72]:72} nm={va} ngp={vb}")
# progress correlation within player B's nightmare set (one player, chapters 1..9)
def rank(v):
    s = sorted(range(len(v)), key=lambda i: v[i]); r = [0.0] * len(v); i = 0
    while i < len(s):
        j = i
        while j + 1 < len(s) and v[s[j + 1]] == v[s[i]]: j += 1
        for k in range(i, j + 1): r[s[k]] = (i + j) / 2 + 1
        i = j + 1
    return r
def spearman(x, y):
    rx, ry = rank(x), rank(y); mx, my = sum(rx) / len(rx), sum(ry) / len(ry); num = sum((a - mx) * (b - my) for a, b in zip(rx, ry))
    den = math.sqrt(sum((a - mx) ** 2 for a in rx) * sum((b - my) ** 2 for b in ry)); return num / den if den else 0.0
prog = [r["hdr"]["progress"] for r in A]; cand = []
for p in scalar_paths(A):
    v = [r["leaves"][p] for r in A]
    if all(isinstance(x, (int, float)) and not isinstance(x, bool) for x in v) and len(set(v)) >= 6: cand.append((abs(spearman(prog, v)), spearman(prog, v), p, min(v), max(v)))
cand.sort(reverse=True)
print(f"\n== numeric paths most correlated with progress inside player B's nightmare-labelled set (n={len(A)}, Spearman): {len(cand)} candidates with >=6 distinct values")
for _, rho, p, mn, mx in cand[:18]: print(f"   rho={rho:+.3f}  {p[:70]:70} {mn}..{mx}")
print("   number with |rho| >= 0.95:", sum(1 for c in cand if c[0] >= 0.95), "| by area:", dict(collections.Counter('/'.join(c[2].split('/')[:3]) for c in cand if c[0] >= 0.95).most_common(6)))
