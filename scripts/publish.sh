#!/usr/bin/env bash
# Single-file, self-contained Windows x64 CLI -> dist/ff2rsave.exe. No keys are bundled: it reads local_config.json at runtime.
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet publish src/Ff2rSave.Cli -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o dist
echo "Built dist/ff2rsave.exe. Place your own local_config.json next to it (see README)."
