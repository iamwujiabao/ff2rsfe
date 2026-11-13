#!/usr/bin/env bash
# Single-file, self-contained Windows x64 Avalonia app -> dist-app/Ff2rSaveEditor.exe (works from macOS). No keys bundled.
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet publish src/Ff2rSave.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o dist-app
echo "Built dist-app/Ff2rSaveEditor.exe. Put your own local_config.json (and fields.json) next to it (see README)."
