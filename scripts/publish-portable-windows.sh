#!/usr/bin/env bash
set -euo pipefail
script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
cd "$script_dir/.."
export AVALONIA_TELEMETRY_OPTOUT=1 DOTNET_CLI_TELEMETRY_OPTOUT=1
output=$(realpath -m "${1:?publish output path}")
bundle="$PWD/src/XGLauncher/obj/single-exe-assets"
python3 scripts/prepare-single-exe.py "$bundle"
flags=(-c Release -r win-x64 --self-contained true -m:1 -nr:false -p:UseSharedCompilation=false -p:XgWindowsPublish=true -p:RestoreLockedMode=true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -p:DebugSymbols=false)
"${XG_DOTNET:-dotnet}" publish src/XG.Updater "${flags[@]}" -o "$bundle/updater"
"${XG_DOTNET:-dotnet}" publish src/XGLauncher "${flags[@]}" -p:XGBundleAssetsDirectory="$bundle" -o "$output/XG-Launcher"
python3 scripts/verify-portable.py "$output/XG-Launcher"
