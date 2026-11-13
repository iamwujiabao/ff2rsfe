"""diff_saves.py A.BIN B.BIN [--max N] : header field diff, JSON path diff, and changed byte ranges in the binary tail."""
import sys, os; sys.path.insert(0, os.path.dirname(__file__))
import struct, json, savejson as S

def flat(o, p=""):
    if isinstance(o, dict):
        for k, v in o.items(): yield from flat(v, f"{p}/{k}")
    elif isinstance(o, list):
        for i, v in enumerate(o): yield from flat(v, f"{p}/{i}")
    else: yield p, o

HDR_FIELDS = [(0x10, "user_id"), (0x18, "payload_len"), (0x1c, "u1c"), (0x20, "chapter"), (0x24, "checkpoint"), (0x28, "u64_28"), (0x30, "timestamp"),
              (0x38, "u38"), (0x3c, "u3c"), (0x40, "u64_40"), (0x48, "u48"), (0x4c, "u8_4c")]

def diff(a, b, maxn=60):
    ha, pa, oa, ea = S.load(a); hb, pb, ob, eb = S.load(b); out = []
    for off, n in HDR_FIELDS:
        w = 8 if n.startswith("u64") else (1 if n.startswith("u8") else 4)
        x = int.from_bytes(ha[off:off+w], "little"); y = int.from_bytes(hb[off:off+w], "little")
        if x != y: out.append(f"hdr  0x{off:02x} {n}: {x} -> {y}")
    fa, fb = dict(flat(oa)), dict(flat(ob)); ch = [(p, fa.get(p, "<absent>"), fb.get(p, "<absent>")) for p in sorted(set(fa) | set(fb)) if fa.get(p, "<absent>") != fb.get(p, "<absent>")]
    out.append(f"json {len(ch)} leaf paths differ (of {len(fa)} / {len(fb)}); json bytes {ea-16} vs {eb-16}")
    for p, x, y in ch[:maxn]: out.append(f"  {p}: {x!r} -> {y!r}")
    if len(ch) > maxn: out.append(f"  ... {len(ch)-maxn} more")
    # binary tail (after JSON region)
    n = min(len(pa), len(pb)); s = max(ea, eb); rng = []; i = s
    while i < n:
        if pa[i] != pb[i]:
            j = i
            while j < n and (pa[j] != pb[j] or (j + 1 < n and pa[j+1] != pb[j+1])): j += 1
            rng.append((i, j)); i = j
        else: i += 1
    out.append(f"tail  {len(rng)} changed ranges after json (pt offset {s}); total {sum(j-i for i,j in rng)} bytes; first: {[(hex(i), j-i) for i,j in rng[:8]]}")
    return "\n".join(out)

if __name__ == "__main__":
    a, b = sys.argv[1:3]; print(diff(a, b, int(sys.argv[sys.argv.index("--max")+1]) if "--max" in sys.argv else 60))
