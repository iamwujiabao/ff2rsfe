"""make_system_fields.py : generate docs/system_fields.json from the system-save corpus (work/system_corpus.pkl, built by system_analysis.py).
All fields are guessed + read-only. Descriptions only state what was observed in the 6 unique system saves (other players, uncontrolled)."""
import sys, os, re, json, pickle, collections
ROOT = os.path.join(os.path.dirname(__file__), "..")
recs = pickle.load(open(os.path.join(ROOT, "work", "system_corpus.pkl"), "rb")); names = [r["name"] for r in recs]
norm = lambda p: re.sub(r"/\d+(?=/|$)", "/*", p)
pats = collections.OrderedDict()
for p in recs[0]["leaves"]: pats.setdefault(norm(p), []).append(p)
SEC = {"SystemData": "data", "SystemUiData": "ui", "SystemPlayerRecordData": "record", "PHOTO_SYSTEM": "photo", "PHOTO_GHOST_LIST": "ghostphoto"}
def rest_of(pat):
    """path segments after the section, without wildcards and CamelCase struct names"""
    return [x for x in pat.strip("/").split("/")[1:] if x != "*" and not re.match(r"^[A-Z][A-Za-z]+$", x)]
def fid(pat):
    out = [SEC[pat.strip("/").split("/")[0]]]
    for x in rest_of(pat):
        x = x.rstrip("_")
        if out[-1] != x: out.append(x)
    return "sys." + ".".join(out)
def stats(pat):
    vals = [[recs[k]["leaves"][p] for p in pats[pat]] for k in range(len(recs))]
    flat = [v for row in vals for v in row]; isb = all(isinstance(v, bool) for v in flat)
    differing = sum(1 for i in range(len(pats[pat])) if len({str(vals[k][i]) for k in range(len(recs))}) > 1)
    nonempty = [sum(1 for v in row if v not in (0, False)) for row in vals]
    return dict(isbool=isb, n=len(pats[pat]), differing=differing, nonempty=nonempty, mn=min(flat) if not isb else None, mx=max(flat) if not isb else None, distinct=len({str(v) for v in flat}))
NOTE = {
 "shop_point_": "Observed 0 in two saves and 612,244 .. 180,438,954 in four others. Whether this is a balance or a lifetime total is unknown; the per-playthrough points in a user save (senki/sen) are separate fields.",
 "ending_cleared_flag_": "Five booleans, probably one per ending. [0] is True in 5 of 6 saves; [1]..[4] are True only in the two fully unlocked saves (which also have user-save current_end_ 5 in the same sources).",
 "costume_bit_flag_": "Bit mask (unsigned 32-bit) of unlocked costumes? 0 in four saves; identical non-zero value in the two fully unlocked saves.",
 "photo_sticker_bit_flag_": "Bit mask of unlocked photo stickers? 0x7FFFFFFF (31 bits) in the fully unlocked saves, one bit in one save, 0 otherwise.",
 "photo_frame_bit_flag_": "Bit mask of unlocked photo frames? 15 (4 bits) in the fully unlocked saves, 0 otherwise.",
 "tutorial_unlock_data_": "Zero-padded list of unlocked entries as hashed ids, in unlock order (the same ids appear at different positions in different saves), so compare as sets.",
 "futago_doll_unlock_data_": "List of hashed ids with a per-entry value; 'unlock' list.",
 "document_unlock_data_": "Zero-padded list of unlocked documents as hashed ids (set, order varies).",
 "omamori_unlock_data_": "Zero-padded list of unlocked omamori (charms) as hashed ids (set, order varies).",
 "reiseki_unlock_data_": "List of hashed ids with a per-entry value; 'unlock' list.",
 "ghost_list_unlock_data_": "Zero-padded list of ghost-list entries as hashed ids (set, order varies); 375 of 379 filled in the fully unlocked saves.",
 "side_story_unlock_data_": "Zero-padded list of unlocked side stories as hashed ids.",
 "yobimodoshi_unlock_data_": "Per-entry unlock values; every non-zero entry has the same value (a flag).",
 "chibarai_unlock_data_": "Per-entry unlock values; every non-zero entry has the same value (a flag).",
 "is_played_rtm_": "List of hashed ids of played (real-time movie?) scenes, zero-padded; compare as sets.",
 "is_played_prm_": "List of hashed ids of played (pre-rendered movie?) scenes, zero-padded.",
 "played_rtm_data_": "Same ids as SystemData/is_played_rtm_ (42 in the fully unlocked saves); another copy.",
 "played_prm_data_": "Same ids as SystemData/is_played_prm_.",
 "record_save_data": "500 records of (label hash, value, display flag); 62 of them are non-empty in every save and 475 positions are identical in all six saves.",
 "setting_language_": "Language options (language id at [0]); player-specific.", "setting_audio_": "Audio volume options; player-specific.",
 "setting_game_": "Game options; player-specific.", "setting_menu_": "Menu options; player-specific.", "setting_graphic_": "Graphics options; player-specific.", "setting_camera_": "Camera options; player-specific.",
 "setting_hdr_": "HDR options; player-specific.", "setting_controller_base_operate": "Controller base-operation setting; player-specific.",
 "resolution_scaling_setting_": "Resolution scaling options; player-specific.", "save_slot_bookmark_": "Per-slot bookmark flags (1000 entries).",
 "ghost_photo_array_": "Per-ghost photo metadata (379 ghosts). Not analysed.", "photo_info_array_": "Photo metadata array. Not analysed.",
 "is_trophy_unlocked_": "101 trophy flags: identical in all six saves (all False in this data).",
}
fields = []
for pat in pats:
    key = rest_of(pat)[0]
    sec = pat.strip("/").split("/")[0]; st = stats(pat)
    desc = NOTE.get(key, "Name-based guess only.")
    obs = f" Observed in 6 system saves (other players, uncontrolled): {st['n']} value(s) per save, {st['differing']} position(s) differ between saves, {st['distinct']} distinct value(s)"
    obs += f"; range {st['mn']}..{st['mx']}" if not st["isbool"] and st["distinct"] <= 12 else ""
    obs += f"; non-empty per save (" + ", ".join(names) + f"): {', '.join(map(str, st['nonempty']))}." if st["n"] > 1 and st["differing"] else "."
    fields.append(dict(id=fid(pat), name=key.rstrip("_").replace("_", " "), source="json", path=pat, type="bool" if st["isbool"] else "int", editable=False, collection="*" in pat, description=desc + obs, confidence="guessed", scope="system"))
ids = [f["id"] for f in fields]; assert len(ids) == len(set(ids)), [i for i in ids if ids.count(i) > 1]
doc = {"schema": 1, "scope": "system", "note": "System save (WLNSYS, SYSTEMSAVEDATA00) fields. EVERY field is guessed and READ-ONLY: derived from 6 unique system saves of other players (uncontrolled); nothing here was tested in the game and the tools refuse to edit or write system saves.", "fields": fields}
head = {k: v for k, v in doc.items() if k != "fields"}
out = "{\n" + ",\n".join(f"  {json.dumps(k)}: {json.dumps(v)}" for k, v in head.items()) + ',\n  "fields": [\n' + ",\n".join("    " + json.dumps(f, ensure_ascii=False) for f in fields) + "\n  ]\n}\n"
json.loads(out); open(os.path.join(ROOT, "docs", "system_fields.json"), "w").write(out); print(len(fields), "system fields written")
