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

## 2. One item: names, quantity (`num`) and the other columns
**Background:** `docs/items.json` now holds nine item names taken from a community Cheat Engine table. That is **third-party, uncontrolled evidence from another game build**: it can make a name `likely` at most. **This experiment is what can make a name `confirmed`**, and it also tests whether `num` is the quantity. Prior sample evidence is in `docs/items_observed.md` (all nine keys occur in 31-41 of 42 saves; rows stay at 0 after use; a few stacks go above 99).
**Hypotheses:** (a) an owned item is an `ItemObjectData` row identified by the hash `key`; (b) `num` is its quantity (the table reads the quantity at +8 of a 16-byte entry; our save order is key, key_num, num, flag); (c) `key_num` is a per-instance id, not a count; (d) the names in `items.json` are right.
**Do (names + quantity, repeat for at least 3 different named items, ideally one film, one healing item and Prayer Beads):** before: write down the in-game **name and count** of the item (screenshot of the item menu). Pick up exactly one more (or use exactly one), save in the same slot, screenshot again.
**Run:** `ff2rsave diff <before> <after>` and `ff2rsave items <after>` (look at the row of the key in the table below).
| item (table name) | key |
|---|---|
| Herbal Medicine | `0x766CE17F` |
| Stone Mirror | `0x179D3D00` |
| Type-14 Film | `0x132D6400` |
| Prayer Beads | `0x4F9CE17F` |
| Type-00 Film | `0x4AFFC0FF` |
| Type-61 Film | `0x0AD73580` |
| Lunar Incense | `0x282BAA80` |
| Type-90 Film | `0x18A56180` |
| Sacred Water | `0x7BDD617F` |
| *(unnamed)* | `0x65E72A7F`, `0x7A5EBCFF` |
**Confirms the quantity field (b):** exactly one `/PossessionItemData/possession_items/N/ItemObjectData/num` changes, by the same amount you picked up or used, and the in-game count matches. If instead `key_num` changes, the quantity is `key_num` and `item.num` must be redefined.
**Confirms a name (d):** the row that changes has the key listed above **and** the in-game item name equals the table name. Record each name separately in `docs/experiment_log.md`; one clean pair makes it `likely` (it already is), a second independent pair of the same item (other slot or other amount) makes it `confirmed`.
**Rejects a name:** the row that changes has a different key than the table lists for that in-game name. Record the correct key and lower the entry in `items.json`.
**Cap checks (optional but valuable):** with an item you can afford to farm or buy, see how far its count goes. The table's "max stack" routine writes 99, but in the samples Prayer Beads reaches 128-420 (and 99999 in 3 saves) and the unnamed `0x7A5EBCFF` is 50 in 10 saves and 99999 in 3, so the real per-item limits are unknown. Do not edit saves to test caps; only observe what the game lets you hold.
**Identify the unnamed keys:** pick up the item that makes `0x65E72A7F` or `0x7A5EBCFF` appear/change and record its in-game name.
**Rejects hypothesis (b):** several unrelated item rows change, the changed row is not the one with the expected key, or the count shown in-game differs from `num`.
**Updates:** `item.num` (see the proposal in `docs/field_proposals.md` section 5), `docs/items.json` (confidence, new names, the two unnamed keys).

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
**Hypothesis:** `/MissionData/lap_num` (and header 0x1C `hdr.lap`) is the NG+ lap counter. *Caution:* all 42 unique sample saves (including the ones other players label NG+) show `1`, so either those are first-lap saves or the field is not the counter; the example of finding `lap_num` = 2 in NG+ saves does **not** occur in that data (`docs/field_proposals.md`).
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
