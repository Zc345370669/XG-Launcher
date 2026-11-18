#!/usr/bin/env bash
set -euo pipefail
# Portable publish flags mirror build-windows.ps1. No NSIS build on Linux.
script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
cd "$script_dir/.."
export AVALONIA_TELEMETRY_OPTOUT=1 DOTNET_CLI_TELEMETRY_OPTOUT=1
"${XG_DOTNET:-dotnet}" publish src/XGLauncher -c Release -r win-x64 --self-contained true -m:1 -nr:false -p:UseSharedCompilation=false -p:XgWindowsPublish=true -p:RestoreLockedMode=true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -p:DebugSymbols=false -o "${1:?publish output path}"

# Keep license obligations alongside the single-file executable.
for notice in LICENSE LICENSE.md THIRD-PARTY-NOTICES.md THIRD_PARTY_NOTICES.md TRADEMARKS.md DISCLAIMER.md AI-DEVELOPMENT.md PUBLICATION_STATUS.md RELEASE_NOTES.md; do cp "$script_dir/../$notice" "$1/"; done
cp -a "$script_dir/../licenses" "$script_dir/../licensing" "$script_dir/../third-party" "$1/"
