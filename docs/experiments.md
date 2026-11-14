# In-game experiments

Each experiment produces a **before/after pair of slot folders**. You run the commands below (or send me the folders and I run them). Record every run in `docs/experiment_log.md`.
`<slot>` means a slot folder such as `C:\ff2r\snap\before\SAVEDATA00`. Commands use `ff2rsave` (the CLI, with your `local_config.json` beside it).

## Ground rules
- **Same slot, nothing else changed.** Close the game, copy the slot folder (**both** files) as `before`; play only the action under test; save in the **same slot**; close the game, copy as `after`. Never edit with the tool during an experiment.
- Work from copies; never point the tool at the live `Savedata` folder.
- Take a screenshot of the in-game number(s) you rely on (points, item count, time).
- **Evidence standard for changing a field's confidence** (nothing is upgraded on a hunch):
  - `guessed -> likely`: one clean pair whose `diff` matches the hypothesis exactly (and the in-game screenshot agrees).
  - `likely -> confirmed`: a **second independent pair** (different slot or a different amount) gives the same result, with no contradicting evidence.
  - A result that contradicts the hypothesis keeps/lowers the confidence and is written in the log as such.
- What I need back for each run: the two slot folders, the screenshots, and the pasted command output.

---

## 1. Money: which of `senki` / `sen` is the currency?
**Hypothesis:** the shop currency is stored in `/PlayerData/senki` and `/PlayerData/sen` (always equal in all samples).
**Do:** note your points; in a shop buy exactly one item whose price you know (or spend an exact amount), leave the shop, save.
**Run:** `ff2rsave diff <before> <after>`
**Confirms:** `[player.senki]` and `[player.sen]` both change by exactly the price (e.g. `5865 -> 5765`) and the in-game number matches. Both are the currency; keep setting both.
**Partly confirms:** only one of them changes by the price -> that one is the real value; the other is a mirror/other meaning. Update the descriptions.
**Rejects:** neither changes by the price. Then look at `[unmapped]` lines whose old/new differ by the price: the currency is elsewhere.
**Updates:** `player.senki`, `player.sen` (and `player.bukun` if it moves).

## 2. One item: what are `key`, `num` and `key_num`?
**Hypothesis:** an owned item is an `ItemObjectData` row identified by the hash `key`; `num` is its quantity. (The `items` listing shows another column, `key_num`, whose meaning is unknown: it varies per row (1, 0, 40, 287, 367, ...) while `num` is 1 for almost every row, so the stack count may live in `key_num` instead.)
**Do:** before: write down the count of a stackable item you know (e.g. a healing item) and note one item you do **not** own yet. Pick up exactly one more of the stackable item (A) and, separately in another pair, pick up the new item (B). Save after each.
**Run:** `ff2rsave diff <before> <after>` and, for context, `ff2rsave items <after>` / `ff2rsave items <before>`.
**Confirms (A):** exactly one `/PossessionItemData/possession_items/N/ItemObjectData/<field>` changes by +1. If it is `num`, quantity = `num` (field `item.num` -> likely); if it is `key_num`, the quantity is `key_num` and `item.num` must be redefined.
**Confirms (B):** a previously empty row (`key == 0`) becomes the new `key` with `num == 1`: that `key` is the hash of the item. Add it to `docs/items.json` with `source: "experiment <date> <slot>"` and `confidence: "likely"` (-> `confirmed` after a second sighting of the same key for the same item in another save).
**Rejects:** several unrelated item rows change, or the row index shifts (the list is re-sorted): record how rows are ordered before editing quantities.
**Updates:** `item.num`, `item.key`, `docs/items.json`.

## 3. Write order: what do `SAVEDATA.BIN` and `BACKUP.BIN` contain after a save?
**Hypotheses:** (H1) each save rotates the old SAVEDATA into BACKUP; (H2) both files are written identically each save; (H3) BACKUP is only refreshed sometimes.
**Do:** snapshot `S0` (slot as it is). Save once in the game (do something visible, e.g. move room) -> `S1`. Wait a minute, change something again, save again -> `S2`. In PowerShell also run `Get-ChildItem <slot> | Select Name, LastWriteTime | Format-Table` (note the seconds) after each save.
**Run:**
- `ff2rsave diff S0\SAVEDATA00 S1\SAVEDATA00` (what changed in each file)
- `ff2rsave diff S1\SAVEDATA00\SAVEDATA.BIN S2\SAVEDATA00\BACKUP.BIN`
- `ff2rsave diff S2\SAVEDATA00\SAVEDATA.BIN S2\SAVEDATA00\BACKUP.BIN`
**Confirms H1:** the 2nd command prints `IDENTICAL` (BACKUP after save 2 = SAVEDATA after save 1) and the 3rd shows differences.
**Confirms H2:** the 3rd command prints `IDENTICAL` after every save.
**H3 / other:** neither pattern holds; report all snapshots. Also note which file has the earlier `LastWriteTime` (weak evidence of which is written first).
**Rejects:** whichever hypothesis the output contradicts. A single pair cannot confirm an order claim: need S0->S1->S2 as above, ideally repeated once.
**Prior uncontrolled evidence:** `docs/observations.md` §1 (33 of 37 slots identical, 4 with BACKUP older, none newer; H2 contradicted unless files were copied; H1 vs H3 undecided).
**Updates:** `docs/save_format.md` (SAVEDATA/BACKUP section) and the default `BackupFilePolicy` (currently `PrimaryOnly`, chosen only because it cannot destroy the older generation).

## 4. Play time: unit of `total_play_time` (and header 0x28)
**Hypothesis:** `/PlayerData/total_play_time` counts microseconds of play (header 0x28 is a copy ~50-100 ms later).
**Do:** at save A write down the in-game play time shown on the save/load screen (hh:mm:ss). Play for a measured stretch with a stopwatch (e.g. exactly 10 minutes of gameplay, no menus/pause), save as B, write down the displayed time again.
**Run:** `ff2rsave get A\SAVEDATA00\SAVEDATA.BIN player.total_play_time` and the same for B; also `ff2rsave info <slot>` (shows `play time` and the header copy).
**Confirms:** `value / 1,000,000` equals the displayed time within about 1 s at both saves, **and** `(B - A) / 1,000,000` equals the stopwatch/displayed difference within 2 %.
**Rejects:** a constant factor off (e.g. value/1000 matches -> milliseconds; value/1e7 matches -> 100 ns ticks): record the factor.
**Updates:** `player.total_play_time`, `hdr.play_time` descriptions and the app's play-time display.

## 5. Lap / New Game+
**Hypothesis:** `/MissionData/lap_num` (and header 0x1C `hdr.lap`) is the NG+ lap counter. *Caution:* every sample, including the two Nexus "NG+" saves, shows `1`, so either those are first-lap saves or the field is not the counter.
**Do:** from a **cleared** game, start New Game+, save in the first room (`NGP`). Keep a save of the clear (`CLEAR`) for comparison.
**Run:** for each: `ff2rsave get <slot>\SAVEDATA.BIN mission.lap_num`, `... mission.last_lap`, `... hdr.lap`, `... record.current_end`, `... record.ngp_tutorial`; and `ff2rsave diff CLEAR NGP --max 30`.
**Confirms:** `mission.lap_num` differs between the first-run save and the NG+ save (record the exact numbers, e.g. 1 -> 2 or 0 -> 1) and `hdr.lap` follows it.
**Rejects:** `lap_num` is identical in both: it is not the lap counter; look at the `[unmapped]` list and `[record.*]` lines for what changed instead.
**Updates:** `mission.lap_num`, `mission.last_lap`, `hdr.lap`, `record.current_end`, `record.ngp_tutorial`.

## 6. Rebind to another user ID
**Hypothesis:** the loader refuses a save whose header user ID (`hdr.user_id`) differs from the current user's ID, and rewriting that ID (checksums recomputed) makes it load. **Never tested in the game.**
**Prior uncontrolled evidence and extra steps:** `docs/observations.md` §2 (one uploader's readme number equals their header ID; a short 4-digit ID is shared by two uploaders; the readmes mention a "steam file" in the ID folder). Also list the names and sizes of the files in your `Savedata\<id>` folder, compare folder name / `info` user id / your Steam AccountID (friend code), and try Steam offline.
**Prerequisites:** FF2RFix ("Remove Save ID Check") must **not** be installed for this test (remove `FF2RFix.asi` / `dinput8.dll` from the game folder, and put them back after). Use a spare slot.
**Do:**
 1. `ff2rsave info <one of your own slots>` -> note `user id`. **Compare it with your `Savedata\<digits>` folder name.** If they are not equal, stop and report (the "folder = user ID" claim is then wrong).
 2. Take a save from another player (e.g. a Nexus save, analysis only) into your slot folder **unchanged**; try to load it in the game.
 3. `ff2rsave set <that save's slot> hdr.user_id <your id> --out <dir>`; copy the result into the spare slot; load it.
**Confirms:** step 1 equal; step 2 refused/not listed (shows the check exists without the mod); step 3 loads. Then `hdr.user_id` is confirmed *and* the rebind claim is confirmed.
**Rejects:** step 3 still refused -> something else is bound (record the exact message). Step 2 loads -> the check is not active in your setup (is the mod still installed?).
**Updates:** `hdr.user_id` description, `docs/save_format.md` ("User-ID binding"), README.

## 7. Costume and amulet slots
**Hypotheses:** the current costumes are stored as hashes in `/PlayerData/costume_mio` and `/PlayerData/costume_mayu` (not yet in `fields.json`); `/PlayerData/unlock_amulet_slot_num` counts unlocked amulet slots.
**Do (costume):** change exactly one character's costume, save. **Do (amulet):** unlock exactly one more amulet slot (however the game allows it), save, in another pair.
**Run:** `ff2rsave diff <before> <after>`.
**Confirms (costume):** exactly one of `costume_mio` / `costume_mayu` changes to a new hash (and `/PlayRecordData/checked_costume_list_/N` may flip). Add the paths as fields; record the hashes in the log (names unknown).
**Confirms (amulet):** `[player.unlock_amulet_slot_num]` changes by exactly +1 and the in-game slot count matches (-> `likely`; second pair -> `confirmed`).
**Rejects:** other paths change instead (`[unmapped]`); the hypothesised path stays constant.
**Updates:** new costume fields in `fields.json`, `player.unlock_amulet_slot_num`.
