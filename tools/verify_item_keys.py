"""verify_item_keys.py : how do item hash keys behave in the sample saves? (counts, num range, duplicates, where large stacks appear)
Names below come from a community cheat table (third-party, UNCONTROLLED evidence); only the key -> name data is used here, no cheat code.
Uses the cache built by tools/corpus.py; prints anonymous group labels only."""
import sys, os, collections; sys.path.insert(0, os.path.dirname(__file__))
import corpus
NAMED = {"Herbal Medicine": 0x766CE17F, "Stone Mirror": 0x179D3D00, "Type-14 Film": 0x132D6400, "Prayer Beads": 0x4F9CE17F, "Type-00 Film": 0x4AFFC0FF,
         "Type-61 Film": 0x0AD73580, "Lunar Incense": 0x282BAA80, "Type-90 Film": 0x18A56180, "Sacred Water": 0x7BDD617F}
SPECIAL = {"(unnamed, special in cheat table)": [0x65E72A7F, 0x7A5EBCFF]}
recs = corpus.load(); N = len(recs)
rows = collections.defaultdict(list); dups = collections.Counter()
for i, r in enumerate(recs):
    per = collections.Counter(e["key"] for s, idx, e in r["items"] if e["key"])
    for k, c in per.items():
        if c > 1: dups[k] += 1
    for s, idx, e in r["items"]:
        if e["key"]: rows[e["key"]].append((i, corpus.group(r), s, e))
def line(name, k):
    rs = rows[k]; nums = [e["num"] for *_, e in rs]
    return f"{name:34} 0x{k:08X} {k:>10} {len({i for i, *_ in rs}):>3}/{N} {len(rs):>4} {dups[k]:>3}  {min(nums, default='-')}-{max(nums, default='-')}  zero rows {sum(1 for n in nums if n == 0)}  common {dict(collections.Counter(nums).most_common(3))}"
print(f"{N} unique user saves\n{'name':34} {'hex':10} {'decimal':>10} saves rows dup  num  ...")
for n, k in NAMED.items(): print(line(n, k))
for n, ks in SPECIAL.items():
    for k in ks: print(line(n, k))
print("\nsaves with >= 2 rows at num >= 99:")
for i, r in enumerate(recs):
    hi = [e for s, idx, e in r["items"] if e["key"] and e["num"] >= 99]
    if len(hi) >= 2: print(f"  {corpus.group(r):22} rows>=99: {len(hi)} (of {sum(1 for s, _, e in r['items'] if e['key'])}), ==99999: {sum(1 for e in hi if e['num'] == 99999)}, named items at exactly 99: {sum(1 for e in hi if e['key'] in NAMED.values() and e['num'] == 99)}")
print("\nrows with num == 99999:", dict(collections.Counter((corpus.group(recs[i]), f"0x{e['key']:08X}") for k in rows for i, g, s, e in rows[k] if e["num"] == 99999)))
print("keys ever above 99:", [f"0x{k:08X}" for k, rs in rows.items() if max(e['num'] for *_, e in rs) > 99])
