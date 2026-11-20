param([string]$MakeNsis = "makensis", [Guid]$MicrosoftClientId = [Guid]::Empty)
$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $true
Push-Location $PSScriptRoot
try {
    # The redistributed NSIS notice is pinned to this tool version.
    $nsisVersion = (& $MakeNsis /VERSION | Out-String).Trim()
    if ($nsisVersion -ne "v3.11") { throw "Use official NSIS 3.11; re-audit the license before changing the build tool version." }
    $auth = @()
    if ($MicrosoftClientId -ne [Guid]::Empty) { $auth += "-p:XGMicrosoftClientId=$MicrosoftClientId" }
    dotnet publish src/XGLauncher -c Release -r win-x64 --self-contained true -p:XgWindowsPublish=true -p:RestoreLockedMode=true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true -p:DebugType=none -p:DebugSymbols=false `
        -o dist/win-x64 @auth
    dotnet publish src/XG.Updater -c Release -r win-x64 --self-contained true -p:IncludeNativeLibrariesForSelfExtract=true -p:XgWindowsPublish=true -p:RestoreLockedMode=true `
        -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -p:DebugSymbols=false -o dist/win-x64/tools
    Copy-Item LICENSE dist/win-x64
    Copy-Item licenses dist/win-x64 -Recurse -Force
    New-Item -ItemType Directory -Force dist/win-x64/licenses/project-docs | Out-Null
    Copy-Item README.md,LICENSE.md,NOTICE.md,TRADEMARKS.md,DISCLAIMER.md,AI-DEVELOPMENT.md,PUBLICATION_STATUS.md,THIRD-PARTY-NOTICES.md,THIRD_PARTY_NOTICES.md,RELEASE_NOTES.md,BRAND-REVIEW.md dist/win-x64/licenses/project-docs
    Copy-Item docs dist/win-x64/licenses/project-docs -Recurse -Force
    Copy-Item licensing dist/win-x64/licenses -Recurse -Force
    Copy-Item third-party dist/win-x64/licenses -Recurse -Force
    & $MakeNsis installer/XGLauncher.nsi
    Compress-Archive dist/win-x64/* dist/XGLauncher-portable-win-x64.zip -Force
    Get-FileHash dist/win-x64/XGLauncher.exe,dist/XGLauncher-Setup-win-x64.exe,dist/XGLauncher-portable-win-x64.zip -Algorithm SHA256
} finally { Pop-Location }
