# Fatal Frame II: Crimson Butterfly Remake (PC/Steam) save format

Status legend: **[C]** confirmed (reproduced on all samples) · **[L]** likely · **[G]** guessed.
Analysed build: Steam `FatalFrameII.exe` (SteamStub 3.1 x64). Offsets below are RVAs in that build.
Samples: 12 user saves (3 mine, 2 NG+, 1 100%; SAVEDATA+BACKUP) and 2 system saves.

## Files and folders
`%LOCALAPPDATA%\KoeiTecmo\FatalFrameII\Savedata\<id>\SAVEDATA00..nn\{SAVEDATA.BIN,BACKUP.BIN}` and `SYSTEMSAVEDATA00\SAVEDATA.BIN`.
Path format in the exe: `%s\%d\%s\%s` (root, user id as signed int32, slot dir, file) [C]. `<id>` = header 0x10 of the saves bound to that user [C].
Whether `<id>` is the Steam AccountID is [G] (the value in my own saves is a 10-digit number, which fits; the origin of the field was not traced).

## Container [C]
| Off | Size | Meaning |
|---|---|---|
| 0x00 | 8 | magic `WLNUSR\0\0` (user, 9,318,960 B) or `WLNSYS\0\0` (system, 61,851,984 B) |
| 0x08 | 8 | version qword 0x24121300; must equal plaintext[0:8] |
| 0x10 | 4 | user id (see above) |
| 0x14 | 4 | header size 0x100 |
| 0x18 | 4 | payload length (file size - 0x100); 0x1C u32 = 1 (user) / 0 (system) |
| 0x20 | 4 | chapter = main_mission_progress // 1000 [C] |
| 0x24 | 4 | main_mission_progress [C] |
| 0x28 | 8 | total_play_time copy [C]; unit µs [G] |
| 0x30 | 8 | Unix time [C] |
| 0x38,0x3C,0x40 | | game-state copies, meaning unknown [G] (see fields.json) |
| 0x48 | 4 | completion code [L]: `current_end_` + 10 x `difficulty_clear_flag_[3]` + 100 x `is_new_game_plus_tutorial_compl` (42/42 saves; uncontrolled) |
| 0x4C | 1 | non-zero => payload encrypted [L] (random-looking byte) |
| 0x50 | 32 | checksum of the **plaintext payload** [C] |
| 0x70 | 32 | checksum of the 0x100 header with 0x70..0x8F zeroed [C] |
| 0x90 | 112 | zero |

Only the user-id (0x10), magic, lengths, version qword and the two checksums are validated by the loader (exe 0xf3f1a0). All other header fields are slot-menu metadata copied from game state; keep them consistent with the JSON anyway.

## Payload encryption [C]
AES-128-CBC, no padding, **fixed key and IV for every file and every player** (user and system). Key = 16 bytes at `.data` rva 0x2c3f158, IV = 16 bytes at `.rdata` rva 0x201c8b0 of the exe. Neither is stored in this repo: `tools/keys.py` derives them from the user's own exe into git-ignored `local_config.json`. The `.text` of the Steam exe is encrypted by SteamStub; unpacking is done in memory only.

## Integrity checksum [C]
Function at exe rva 0xc14ea0, custom 32-byte keyed checksum, reimplemented in `tools/ff2save.py::chk32`:
state S[32]=0; for each byte `b` at index `i`: add `b` (mod 2^16, little-endian lanes) to lane at S[0], S[2+2*((i>>2)&1)], S[6+2*(i&3)]; then for k in 0..30: `S[k+1] += S[k] + 2k (mod 256)`; output[j] = `(A[S[j]] + B[j]) mod 256` with A (256 B) and B (32 B) immediates in the function (extracted at runtime). Hash order on write: payload first (0x50), then header (0x70).

## Plaintext layout [C]
`[0:8]` version qword 0x24121300, `[8:16]` zero, `[16 : 16+n]` compact JSON (`separators=(",",":")`, UTF-8, byte-identical re-serialization verified), zero padding up to plaintext offset 0xFA010 (JSON area = 1,024,000 B; samples use 0.778-0.801 MB of it (42 unique user saves)), then a binary tail (~8.3 MB, changes heavily between saves; likely photo data referenced by `PHOTO_SLOT`) [G]. System save: same 16-byte prefix, JSON 0.39-0.42 MB, then padding and binary data (see the system-save section).
Editing rule: parse JSON, change values, re-serialize compactly, zero-pad to the same area, leave the tail untouched, then recompute 0x50 and 0x70.

## JSON sections (user save)
`PlayerData` (39 keys: money `senki`/`sen`, equipment, gestures, `total_play_time`, costumes, amulets, `item_get_info`...), `TempRecordData`, `UIData`, `PossessionItemData` (`possession_items` x650, `storage_items` x550, entries `ItemObjectData{key,key_num,num,flag,entry_number,...}`), `MissionData` (`main_mission_progress`, `lap_num`, suspended data), `WorldData`, `PlayRecordData`, `PHOTO_SLOT`. System save: `SystemData`, `SystemUiData`, `SystemPlayerRecordData`, `PHOTO_SYSTEM`, `PHOTO_GHOST_LIST`. These match the exe's RTTI `savedata::*` names.
Many identifiers (item keys, costumes, tutorial flags) are 32-bit hashes (low byte often 00 or FF); the hash->name table is **unknown** [G].

## User-ID binding [C]
Loader compares header 0x10 with the current user's id (obj+0xb0); mismatch -> error 8 (exe 0xf3f5f4). FF2RFix "Remove Save ID Check" patches this branch (its AOB `74 ?? C7 ?? ?? ?? ?? ?? 08 00 00 00 83 ?? ?? ?? ?? ?? 00` matches once, at 0xf3f5fd). The id is **not** in the payload or payload checksum, so rebinding = write new id to 0x10 and recompute 0x70 (offline test: all other loader checks pass) [C]. Not yet tested in the real game. Saves from two other players carry the same short 4-digit id [C]; why is unknown.

## SAVEDATA.BIN vs BACKUP.BIN [G]
Either identical or BACKUP is an older generation of the same slot (e.g. mine/SAVEDATA00: progress 9015 vs 9030). The write order is not determined. Editor policy until tested: an in-place save writes only `SAVEDATA.BIN` and leaves `BACKUP.BIN` (the older generation, a state the game itself produces) untouched as the way back; writing both is an explicit option (`--both`). Whether the game prefers one file, or compares them, is still unknown (see experiment 4).

## System save (SYSTEMSAVEDATA00) - read-only
Observed in 6 unique system saves of other players (uncontrolled); details and per-save tables in `docs/system_observations.md`.
- **Known [C for the container, nothing else]:** magic `WLNSYS`, 61,851,984 bytes, same AES-128-CBC key/IV and the same two checksums as user saves (6/6 unique files verify and round-trip byte-exactly). All game-state header fields are 0. The JSON (0.39-0.42 MB) has 5 sections (`SystemData`, `SystemUiData`, `SystemPlayerRecordData`, `PHOTO_SYSTEM`, `PHOTO_GHOST_LIST`), 21,853 leaf values (ints and bools), identical paths in every save. It holds no user ID.
- **Guessed [G]:** `docs/system_fields.json` (108 field patterns, all `guessed`, all read-only): settings, unlock lists (zero-padded lists of hashed ids in unlock order: compare as sets), ending flags, bit masks, shop points, played-scene lists, photo metadata.
- **Unknown:** what the hashed ids are, what the binary data after the JSON is (0 to 41 MB of non-zero bytes; first non-zero at plaintext offset 1,024,016 or 17,613,392), what `shop_point_` counts, `record_save_data`, and whether the game accepts an edited system save. **The tools never edit or write system saves** (Core refuses; CLI `set`/export and the app's save/export are blocked).
- Cost: loading takes about 0.1 s and 256 MB of memory (a user save: 97 MB); no special loading scheme is needed.
