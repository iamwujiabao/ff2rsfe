# Fatal Frame II: Crimson Butterfly Remake - save editor (PC / Steam)

Editor for **your own** single-player saves: a .NET library, a CLI (`ff2rsave`) and a WPF app (Windows only).

> ## ⚠ BACK UP YOUR SAVES FIRST
> Copy the whole `Savedata` folder somewhere safe before using any tool here. The tools also make a timestamped
> `*.bak-*` copy before every write and re-read/verify afterwards, but **edits to fields marked "unverified" have never
> been tested in the game** and a bad edit can make a save unloadable. This is unofficial software, used at your own risk.

## Supported game version
Developed against the Steam build whose `FatalFrameII.exe` is 51,992,544 bytes (container version `0x24121300`).
Other builds may use different keys; the tool will report integrity/JSON errors rather than guess.

## Saves may be tied to a user ID
Each user save stores a 32-bit user ID in its header (offset 0x10) and the game refuses saves whose ID differs from
the current user's (the folder `%LOCALAPPDATA%\KoeiTecmo\FatalFrameII\Savedata\<digits>\` is that ID). This is separate
from the encryption. The editor can rewrite the ID (field `hdr.user_id`) and recompute the checksums; offline tests pass,
but **rebinding has not been tried in the real game**. Alternatively use the community FF2RFix mod's "Remove Save ID Check".
Saves from other people are for analysis only: do not redistribute them.

## One-time setup: `local_config.json`
The AES key/IV and checksum tables belong to the game and are **not** in this repository. Derive them from your own
copy of the game (Python 3.11+ with `capstone pefile pycryptodome`, analysis only; the C# tools never need Python):

```bash
python3 -m venv tools/.venv && tools/.venv/bin/pip install capstone pefile pycryptodome
# put your FatalFrameII.exe at game/FatalFrameII.exe, then:
tools/.venv/bin/python tools/keys.py          # writes local_config.json (git-ignored)
```
Put `local_config.json` next to the program (or any parent folder). Deriving it inside C# directly from the exe is not implemented.

## CLI
```text
ff2rsave info   <file|slotdir>
ff2rsave verify <file|dir>
ff2rsave dump   <file> --raw out.dec | --json out.json
ff2rsave get    <file> <fieldId>            # or: get <file> --path /MissionData/main_mission_progress
ff2rsave set    <file|slotdir> <fieldId> <value> [--path /concrete/path] [--out new.BIN]
ff2rsave pack   <in.dec> <out.BIN>
ff2rsave fields
```
`set` on a slot directory writes both `SAVEDATA.BIN` and `BACKUP.BIN` with identical content (the game's own write order is
not verified). Field ids and their confidence come from `docs/fields.json`; rows other than `confirmed` are guesses.
Exit codes: 0 ok, 1 integrity problems, 2 usage/other error.

## WPF app (`src/Ff2rSave.App`, Windows)
Open a `Savedata\<id>` folder (or use "Find default location"), pick a slot, edit values in the grid, inspect the hex diff, Save.
It follows the system light/dark theme. **It was compile-checked but never run** (developed on macOS); expect rough edges.

## Build / test / publish
```bash
dotnet test                         # .NET 10 SDK; tests use the git-ignored samples/ and local_config.json
scripts/publish.sh                  # or scripts\publish.ps1 -> dist/ff2rsave.exe (single-file, self-contained, win-x64)
```
Equivalent: `dotnet publish src/Ff2rSave.Cli -c Release -r win-x64 --self-contained -p:PublishSingleFile=true`.

## Known limits
Item/costume IDs are hashes with no name table yet; no adding of new items; system saves can be read/verified but have no field map;
the `SAVEDATA.BIN`/`BACKUP.BIN` write order and many field meanings are unverified. Format details: `docs/save_format.md`;
suggested experiments to close the gaps: `docs/experiments.md`.
