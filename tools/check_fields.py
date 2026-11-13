"""Validate docs/fields.json against every sample: JSON paths must resolve, header values must fit their type/bounds."""
import sys, os, glob, json; sys.path.insert(0, os.path.dirname(__file__))
import savejson as S
ROOT = os.path.join(os.path.dirname(__file__), "..")
W = {"u8": 1, "u32": 4, "u64": 8}
def resolve(o, path):
    cur = [o]
    for seg in path.strip("/").split("/"):
        nxt = []
        for c in cur:
            if seg == "*": nxt += list(c) if isinstance(c, list) else list(c.values())
            elif isinstance(c, list): nxt.append(c[int(seg)])
            elif seg in c: nxt.append(c[seg])
        cur = nxt
        if not cur: return []
    return cur
if __name__ == "__main__":
    fields = json.load(open(os.path.join(ROOT, "docs", "fields.json")))["fields"]; bad = 0; files = sorted(glob.glob(os.path.join(ROOT, "samples", "*", "SAVEDATA*", "*.BIN")))
    for f in files:
        h, pt, o, e = S.load(f)
        for fd in fields:
            if fd["source"] == "header":
                v = int.from_bytes(h[fd["offset"]:fd["offset"] + W[fd["type"]]], "little")
                if "min" in fd and not fd["min"] <= v <= fd["max"]: print("RANGE", f, fd["id"], v); bad += 1
            else:
                vals = resolve(o, fd["path"])
                if not vals: print("UNRESOLVED", os.path.basename(os.path.dirname(f)), fd["id"]); bad += 1; continue
                for v in vals:
                    if fd["type"] == "int" and (isinstance(v, bool) or not isinstance(v, int)): print("TYPE", fd["id"], v); bad += 1
                    elif fd["type"] == "bool" and not isinstance(v, bool): print("TYPE", fd["id"], v); bad += 1
                    elif fd["type"] == "int" and "min" in fd and not fd["min"] <= v <= fd["max"]: print("RANGE", fd["id"], v, f); bad += 1
    print(f"{len(fields)} fields x {len(files)} user saves checked; problems: {bad}")
