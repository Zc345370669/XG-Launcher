#!/usr/bin/env bash
set -euo pipefail
script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
cd "$script_dir/.."
export AVALONIA_TELEMETRY_OPTOUT=1 DOTNET_CLI_TELEMETRY_OPTOUT=1
output="${1:?publish output path}"
bundle="$PWD/src/XGLauncher/obj/single-exe-assets"
python3 scripts/prepare-single-exe.py "$bundle"
"${XG_DOTNET:-dotnet}" publish src/XG.Updater -c Release -r win-x64 --self-contained true -m:1 -nr:false -p:UseSharedCompilation=false -p:XgWindowsPublish=true -p:RestoreLockedMode=true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -p:DebugSymbols=false -o "$bundle/updater"
"${XG_DOTNET:-dotnet}" publish src/XGLauncher -c Release -r win-x64 --self-contained true -m:1 -nr:false -p:UseSharedCompilation=false -p:XgWindowsPublish=true -p:RestoreLockedMode=true -p:XGBundleAssetsDirectory="$bundle" -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -p:DebugSymbols=false -o "$output/XG-Launcher"
