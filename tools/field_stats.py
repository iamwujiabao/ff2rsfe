"""field_stats.py : observed min/max/distinct for every field in docs/fields.json across all unique user saves; flags values outside the schema range."""
import sys, os, json, re, collections; sys.path.insert(0, os.path.dirname(__file__))
import corpus
recs = corpus.load(); fields = json.load(open(os.path.join(corpus.INV.ROOT, "docs", "fields.json")))["fields"]
HDR = {16: None, 24: None, 28: "lap", 32: "chapter", 36: "progress", 40: "play", 48: "ts", 56: "u38", 60: "u3c", 64: "u40", 72: "u48", 76: "flag4c"}
def matcher(path):
    rx = re.compile("^" + "/".join(re.escape(s) if s != "*" else "[^/]+" for s in path.strip("/").split("/")).join(["/", "$"]) ); return rx
def observe(fd):
    if fd["source"] == "header":
        k = HDR.get(fd["offset"]); return [r["hdr"][k] for r in recs] if k else None
    m = matcher(fd["path"]); return [v for r in recs for p, v in r["leaves"].items() if m.match(p)]
rows = []; out = []
for fd in fields:
    v = observe(fd)
    if v is None: rows.append((fd["id"], "header, not stored in the cache (user id / length): skipped")); continue
    nums = [x for x in v if isinstance(x, (int, float)) and not isinstance(x, bool)]; bools = [x for x in v if isinstance(x, bool)]
    d = collections.Counter(v); lo = fd.get("min"); hi = fd.get("max")
    if nums:
        mn, mx = min(nums), max(nums); bad = (lo is not None and mn < lo) or (hi is not None and mx > hi)
        rows.append((fd["id"], f"schema {lo}..{hi} | observed {mn}..{mx} | distinct {len(d)} | n={len(v)} | conf={fd['confidence']}" + ("   <-- OUTSIDE SCHEMA" if bad else "") + (f" | common {dict(d.most_common(5))}" if len(d) <= 12 else "")))
        if bad: out.append(fd["id"])
    else:
        rows.append((fd["id"], f"bool/other values {dict(d)} | n={len(v)} | conf={fd['confidence']}"))
for r in rows: print(f"{r[0]:32} {r[1]}")
print("\nfields with observed values outside the schema min/max:", out or "none")
# anything observed in the narrow description text that data contradicts is reviewed by hand in docs/field_proposals.md
