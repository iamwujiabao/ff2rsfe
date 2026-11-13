"""Load a save's JSON section: returns (hdr, pt, obj, json_end_offset_in_pt). pt offset of JSON start is 16."""
import sys, os, json; sys.path.insert(0, os.path.dirname(__file__))
import keys, ff2save as F
JSON_START = 16
def load(path, cfg=None):
    cfg = cfg or keys.load(); raw = open(path, "rb").read(); hdr, pt = F.decrypt(raw, cfg); body = pt[JSON_START:]
    try: body.decode("utf-8"); txt = body.rstrip(b"\0").decode()
    except UnicodeDecodeError as e: txt = body[:e.start].decode()
    obj, end = json.JSONDecoder().raw_decode(txt)
    return hdr, pt, obj, JSON_START + len(txt[:end].encode())
