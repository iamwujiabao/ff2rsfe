# First test on Windows: checklist

Goal: try the editor on your real saves **without any chance of losing them**. Follow the steps in order and stop at the first surprise.
Nothing in this tool has ever been run on Windows or tested in the game yet: treat every step as an experiment.

## A. Prepare (before touching the game)
1. **Close the game** and Steam cloud sync if you can (Steam: Library -> game -> Properties -> General -> turn off cloud saves for the test).
2. **Back up the whole save folder.** Copy `%LOCALAPPDATA%\KoeiTecmo\FatalFrameII\Savedata` to somewhere safe, e.g. `Documents\Savedata_BACKUP_original`. Check that the copy has the same files and sizes (each `SAVEDATA.BIN` is 9,318,960 bytes).
3. On the Mac, build: `scripts/publish-app.sh` (and `scripts/publish.sh` for the CLI). Copy the **whole** `dist-app` folder (and `dist` if you want the CLI) to Windows, e.g. `C:\ff2r\`.
4. Copy **your own** `local_config.json` into `C:\ff2r\dist-app\` (same folder as `Ff2rSaveEditor.exe`). It is never inside the zip/repo. If it is missing, the app tells you what to do; do not continue until it opens normally.
5. Windows may show a SmartScreen warning for an unsigned exe: "More info" -> "Run anyway" (only for your own build).

## B. Look, don't touch
6. Start `Ff2rSaveEditor.exe`. Click **Find default location**. Write down what the status line says (folder found? user-ID folder name?).
7. Click **Open folder…** and open a **copy** of one slot folder (e.g. copy `Savedata\<id>\SAVEDATA00` to `C:\ff2r\work\SAVEDATA00`), not the live one.
8. Check: integrity says "OK" for the slots; user ID shown equals your `<id>` folder name (this confirms the user-ID guess; tell me if it differs).
9. CLI cross-check (optional, from `C:\ff2r\dist`): `ff2rsave verify --roundtrip C:\ff2r\work\SAVEDATA00` must print `ROUNDTRIP OK` for both files. If it prints differing byte ranges, **stop and send me the output**.

## C. No-op export, then load it in the game
10. The app's export button stays disabled until something changes, so make the no-op copy with the CLI: `ff2rsave set C:\ff2r\work\SAVEDATA00 mission.progress <the current value> --out C:\ff2r\export\SAVEDATA00` (same value = no real change; `mission.progress` is a confirmed field so no unlock is needed).
11. Compare: `ff2rsave verify --roundtrip C:\ff2r\export\SAVEDATA00` -> OK.
12. **Test on a spare slot first.** Choose a slot you do not care about (or start a throwaway new game and save it). With the game closed, copy the exported `SAVEDATA.BIN` (and `BACKUP.BIN`) over that slot in `Savedata\<id>\SAVEDATAnn\`.
13. Start the game, open the load menu, check the slot appears with the right chapter/time, **load it**, play a minute, save. Result 1: loads / does not load / error message (screenshot).

## D. One small edit
14. In the app (copy of the slot open): tick **Allow editing unverified fields** only if you want to edit a guessed field; read the warning. A safe first edit is a number you can see in the game, e.g. **Points (senki)** and **Points (sen)** (set both to the same new value). Leave "Overwrite original…" **unticked**.
15. Click **Export edited copy…**, choose `C:\ff2r\export2`. A `SAVEDATA00` sub-folder is created. Original untouched (the status line says so).
16. Copy the exported files over your spare slot (game closed). Start the game, load it, check the points in the shop. Result 2: which number the game shows (senki or sen?), any error.
17. Save in the game once more and keep the files: that save is valuable for me (write order, what the game changes).

## E. Only if everything above worked
18. The "Overwrite original" box is a last resort. If you use it: the original `SAVEDATA.BIN` is copied to `SAVEDATA.BIN.bak-<timestamp>`, the new file is verified, `BACKUP.BIN` is left alone.

## How to restore
- **Spare/any slot:** with the game closed, delete the slot folder's `SAVEDATA.BIN` and `BACKUP.BIN`, then copy the same files back from `Savedata_BACKUP_original`.
- **After "Overwrite original":** copy `SAVEDATA.BIN.bak-<timestamp>` over `SAVEDATA.BIN` (rename it), or restore from your full backup.
- If the game shows the slot as empty/corrupted, **do not save in the game**: restore first.
- Re-enable Steam cloud only after you are sure the saves are good (cloud sync can upload a bad file).

## What to send me if something fails
- The exact text of any error (status line in the app, or console output of the CLI) and a screenshot of the game's message.
- Which step number failed and what you expected.
- The slot folder **before** (from your backup) and **after** (`SAVEDATA.BIN`, `BACKUP.BIN`, and any `.bak-*`). Do not send `local_config.json`.
- Output of `ff2rsave info <slotdir>` and `ff2rsave verify --roundtrip <slotdir>` for both.
- Windows version and whether Steam cloud was on.
