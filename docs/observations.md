# Observations from other players' saves (uncontrolled)

Source: the anonymized sample set in `docs/sample_inventory.md` (players A-D plus the repo owner `me`). Reproduce with `tools/slot_relations.py` and `tools/id_analysis.py` (they print groups and counts only).
**These are not controlled experiments.** Nobody recorded what they did between saves, files may have been copied, renamed or trimmed by the uploaders, and slot content may have been shared between people. This data can reject or narrow a hypothesis and show which values occur. It **cannot** confirm anything and does **not** replace experiment #3 (write order) or #6 (user ID) in `docs/experiments.md`. The default `BackupFilePolicy` is unchanged.

## 1. SAVEDATA.BIN vs BACKUP.BIN

Unique user slots (same content under two sources counted once): **38**.

| state | slots |
|---|---|
| both files present and byte-identical | 33 |
| both present, contents differ | 4 |
| `BACKUP.BIN` missing | 1 |
| both present, `BACKUP.BIN` newer than `SAVEDATA.BIN` | **0** |

All 7 system-save files (6 unique) have no `BACKUP.BIN` (7 of 7).

The 4 slots that differ (A-D = players, `me` = repo owner; "set" tags are the uploaders' hints):

| source / slot | BACKUP progress -> SAVEDATA progress | header time gap | play-time gap | JSON values that differ | main areas |
|---|---|---|---|---|---|
| me / 00 | 9015 -> 9030 | 34.0 min | 34.0 min | 2,621 | world + mission state |
| B (nm) / 14 | 7005 -> 7010 | 2.4 min | 2.4 min | 1,476 | world + mission state |
| B (ngp) / 19 | 9030 -> 9030 | 29.3 h | 4.3 h | 11 | player act, equipment |
| C / 00 | 7004 -> 9030 | 39.3 h | 2.9 h | 4,979 | world + mission state |

- In **all 4**, `BACKUP.BIN` is older on every clock at once: header timestamp, play time (`total_play_time`) and progress (never behind-and-ahead). It is **never** newer.
- The BACKUP does not have to be "just before": the gap ranges from 2 minutes of play to two chapters.
- Two of the four pairs (me/00 and B-nm/14) were evidently saved within one play session: the play-time gap equals the wall-clock gap (ratio 0.9995 and 0.9875). That is consistent with the play-time unit being microseconds (see experiment 4); it is two pairs, uncontrolled.
- Almost all slots in the practice sets are identical, which is what you would expect if each slot was saved once, or if both files are written together at each save.

### What this says about H1-H3 (experiment #3)
| hypothesis | observation | verdict |
|---|---|---|
| **H1** every save rotates the old SAVEDATA into BACKUP | A slot saved twice or more would differ. 33 of 37 slots with both files are identical, which fits only if those slots were saved once. The 4 differing slots all fit (BACKUP older, plausible previous save). | **Not contradicted, not confirmed.** |
| **H2** both files are written identically at every save | Four slots where BACKUP is older contradict it as an exceptionless rule, unless the uploader copied files separately. | **Contradicted by 4 slots** (uncontrolled; copying cannot be ruled out). |
| **H3** BACKUP is only refreshed sometimes | Compatible with everything seen, because it predicts nothing specific. | **Not testable with this data.** |

Other points: the one user slot without a BACKUP is not the first save of its set (it sits at chapter 3 with a lot of play time), and system saves never have one in any sample.
Conclusion: the observations argue against "always write both identically" and never show a newer BACKUP, which does not hurt the current default (`PrimaryOnly` leaves `BACKUP.BIN` as the older generation). They cannot tell H1 from H3. **Experiment #3 is still needed.**

## 2. User IDs (header 0x10)

IDs are shown as groups only: equal letters = equal value.

| source | ID group | digits | user saves | system saves |
|---|---|---|---|---|
| me | c | 10 | 4 | 0 |
| player A | a | 4 | 1 | 1 |
| player B | b | 10 | 33 | 4 |
| player C | a | 4 | 3 | 1 |
| player D | d | 9 | 1 | 0 |

(counts are unique files.) Facts:
- Every value fits in 32 bits.
- **Group a (4 digits) is shared by two different uploaders** (A and C), in both their user and system saves. It is not a 9-10 digit account-like number.
- Each of the other players has one 9-10 digit ID used for **all** their user and system saves.
- **The ID is not stored in the decrypted payload**: for the 9-10 digit IDs it appears in 0 of 42 files, as little-endian bytes, decimal text or hex text. (The 4-digit value turns up as bytes in 5 of 6 files, which is meaningless for such a small number.) No JSON key name refers to a user, account or Steam ID (the only hits are `steam_deck_*` UI settings).
- **Folder = header ID, one data point:** player D's readme says the user-ID folder is a 9-digit number "in my case"; that number **equals** player D's header ID (checked by script; the number is not recorded here). Player B's readme says the folder is "a 10 DIGIT NUMBER", consistent with their 10-digit IDs. This is one uploader's statement, uncontrolled.
- The readmes also mention a **"steam file"** inside the user-ID folder that must be deleted along with the save folders. No sample contains it. **We know nothing about it** (name, size, content).
- Player B's readme tells users to copy a system save into the ID folder, and the other uploaders say the mod is needed to use someone else's save. This fits the loader code, which compares the header ID for both user and system saves.

### Does the 4-digit ID contradict "the folder name equals the header ID"?
Not by itself. We do not know the folder names of players A and C. What it contradicts is the assumption that a header ID is always a 9-10 digit, Steam-account-like number. Two possible readings, **neither tested**:
1. The game wrote a fallback/default value when it had no real account ID (for example no Steam login or an emulated Steam). Then those players' folders may be named after the same short number.
2. Someone normalised the value when packaging the saves. Less likely, since two uploaders share it in both user and system saves, but not excluded.

**Where the exe gets the ID is not established.** The exe holds a Steam `ISteamUser023` accessor, but a bounded static search found no store to the manager's ID field in the save code, so I could not link them. "The ID is the Steam AccountID" remains a guess.

### Proposed tests on your Windows PC (not yet run)
1. **List your user-ID folder**: file and folder names and sizes inside `Savedata\<id>\` (to learn what the "steam file" is). Do not send file contents.
2. **Compare three numbers locally** and tell me only whether they match: the folder name, the "user id" printed by `ff2rsave info` on a slot from it, and your Steam AccountID (the "friend code" shown in Steam > Add a friend; it is the AccountID). If all three match, the ID is the Steam AccountID.
3. **Steam offline**: with Steam in offline mode (and, separately, closed if the game starts), see which folder name the game uses for a new save, and whether a short-number folder appears. This tests the fallback reading. Work on a throwaway profile or copy; do not touch saves you care about.
4. **Experiment #6** (rebind) with FF2RFix removed, as written.

| if | then |
|---|---|
| folder = header ID = AccountID | the ID is the Steam AccountID; rebinding means writing your own AccountID |
| folder = header ID but not the AccountID | the ID comes from somewhere else (check what the "steam file" holds) |
| a short-number folder appears when Steam is offline | the short ID is a fallback value |
| folder differs from the header ID | the "folder = ID" statement in the README/docs is wrong; fix it |

## 3. What the data says about the docs
- `docs/save_format.md` ("Files and folders") treats `<id>` = header 0x10 as confirmed. The only direct evidence is one uploader's readme plus the exe's path format; I propose wording it as "likely (one uploader's statement + exe path format)" until test 2 is done.
- The README sentence that the folder "is that ID" has the same status.
- Nothing seen contradicts the container, crypto or checksum findings.
