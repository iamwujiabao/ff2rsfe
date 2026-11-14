# Static check of the item and points code in our exe

**What this is:** a read-only check of `FatalFrameII.exe` (51,992,544 bytes, the build this project targets) after a community cheat table (third-party, uncontrolled, written for another build) described some inventory and points code. The exe's `.text` is encrypted by SteamStub, so it was decrypted **in memory only** with `tools/pe.py` and read with a disassembler; nothing was run, nothing from the table was copied, and the search script is not part of the repo. All addresses below are RVAs in **our** exe; the table's own addresses do not apply to it.

## Does the table fit our build?
Yes for the code, no for the addresses. Eight of the nine instruction sequences the table relies on exist in our exe (7 of them exactly once), at different addresses; one exact byte sequence (a specific quantity read) has no match, its looser variant has 3. So the item and points routines it describes exist here, and the layout facts below were checked in our own code, not taken from the table.

## Established in our exe (from the code itself)
| fact | evidence (our RVAs) |
|---|---|
| An inventory entry has a 32-bit **key at +0** (0 = empty) and a 32-bit **quantity at +8** | the item-use routine at `0x9A16A0` tests `[entry]` for 0, looks the key up, reads `[entry+8]` and stores `[entry+8] - amount`; the "has item" check at `0x9A7610` compares `[entry]` with an item hash and requires `[entry+8] > 0`; a film-count check at `0x9A4050` compares `[entry+8]` with the needed amount |
| Using an item that the item data flags as unlimited resets the quantity to 1 instead of decreasing it | same routine, branch before the subtraction |
| The inventory is an array of **650 slots of 16 bytes** (pointer-sized slot, stride 0x10, span 0x28A0) and a second one of **550 slots** (span 0x2260) | the 0x28A0 span is used at 28 sites and 0x2260 at 17; they match `possession_items` (650) and `storage_items` (550) in the save JSON |
| The save schema for `ItemObjectData` declares `key_num` with default `-1` | schema builder at `0xBA1A60`: this is the 4294967295 seen in every empty slot of the samples |
| The item **hashes and names are not in the exe** | none of the 11 keys occurs as a 32-bit value anywhere in the file and none of the names occurs as ASCII or UTF-16 text: item definitions live in external game data |

## What this does and does not tell us about the save fields
- **`item.num` = quantity:** the code confirms that a live entry's quantity is at +8 and its key at +0. That the saved `num` is that dword is an **inference** from field order (the save lists `key`, `key_num`, `num`, `flag`, i.e. offsets 0, 4, 8, 12), not something the serializer shows: the schema builder gives names and defaults, not offsets. This supports `item.num` = `likely` (already set); it does not make it `confirmed`; experiment 2 still decides.
- **Names:** cannot be checked offline (see above). They stay `likely` at most, from the third-party table.
- **Why some stacks exceed 99** (Prayer Beads 128-420 and 99999, key `0x7A5EBCFF` 99999): not explained by this code. The per-item maximum comes from item data that is not in the exe.

## Points
- **The live points value** is a 64-bit integer at `global + 0x1D0E8`, where `global` is the game-state object at `0x6FCF968` (the same one the save writer reads, e.g. total play time at `+0x1A998`). It is added to at five sites (`0xA0D33A`, `0xA0D44E`, `0xB12285`, `0xB1252F`, `0xDE36B1`) and spent by a saturating subtraction (clamped at 0) in the function at `0xABC990`.
- **The saved `senki` and `sen`** are built from offsets `+0x00` and `+0x10` (32-bit) of the object handed to the PlayerData builder at `0xB99BA0`; its `total_play_time` comes from `+0x1A998`, the same offset the header writer uses, so that object very likely has the layout of the same global.
- **Conclusion:** the table's "Points" (the 64-bit field) is **not shown to be `senki` or `sen`**: they are different fields in the same object. Whether the saved values are copies of the live points, a separate per-run counter, or related to the system save's `shop_point_` (values up to 180 million, which are 64-bit sized) is **unknown**; the only reference to `shop_point_` in the exe writes a constant 0 into the schema default, so it does not tell. Nothing here changes `player.senki` / `player.sen` (still `guessed`; experiment 1 decides which one the game uses).
- The table's separate 4-byte "Points" address belongs to another build and was not located in ours.

## Not checked
The functions that fill `senki`/`sen`/`shop_point_` from live state, how the save loader writes `num` back into entries, and the item data (caps, flags) that live outside the exe.
