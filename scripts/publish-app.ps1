# Single-file, self-contained Windows x64 Avalonia app -> dist-app\Ff2rSaveEditor.exe. No keys bundled.
$ErrorActionPreference = "Stop"
Set-Location (Join-Path $PSScriptRoot "..")
dotnet publish src/Ff2rSave.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o dist-app
Write-Host "Built dist-app\Ff2rSaveEditor.exe. Put your own local_config.json (and fields.json) next to it (see README)."
