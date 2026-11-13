# Suggested in-game experiments (send me the slot folders before/after)
Always copy the whole slot folder first. Each experiment = two saves in the SAME slot, nothing else changed.
1. **Money**: in a shop spend exactly 100 points, save. Tells which of `senki`/`sen` the game uses (and whether storage/other fields move).
2. **One item**: pick up exactly one known item (e.g. one Stone Mirror film / healing item), save, then use one, save again. Maps item `key` hash -> name and confirms `num`.
3. **Film types**: buy one film of each type in separate saves; same goal as 2.
4. **Slot write order**: save twice in a row in one slot with a ~1 min gap and send SAVEDATA.BIN+BACKUP.BIN after each save (3 snapshots: before, after 1st, after 2nd). Determines which file is written first and what BACKUP holds.
5. **Lap/NG+**: start a New Game+ and save in the first room; compare `lap_num`, `current_end_`, header 0x1C.
6. **Rebind test** (safe, uses my tool on a copy): put a rebound copy of one slot into your real `Savedata\<your id>\` WITHOUT FF2RFix and see if it loads.
7. **Play time**: note the in-game play time on the save screen; I will check the µs-unit guess.
8. **Costume / amulet slot**: change one costume, save; unlock one amulet slot, save.
