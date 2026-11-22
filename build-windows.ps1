param([switch]$BuildInstaller, [string]$MakeNsis = "makensis", [Guid]$MicrosoftClientId = [Guid]::Empty)
$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $true
Push-Location $PSScriptRoot
try {
    # The redistributed NSIS notice is pinned to this tool version.
    if ($BuildInstaller) {
    $nsisVersion = (& $MakeNsis /VERSION | Out-String).Trim()
    if ($nsisVersion -ne "v3.11") { throw "Use official NSIS 3.11; re-audit the license before changing the build tool version." }
    }
    $auth = @()
    if ($MicrosoftClientId -ne [Guid]::Empty) { $auth += "-p:XGMicrosoftClientId=$MicrosoftClientId" }
    $bundle = Join-Path $PSScriptRoot 'src/XGLauncher/obj/single-exe-assets'
    python scripts/prepare-single-exe.py $bundle
    dotnet publish src/XG.Updater -c Release -r win-x64 --self-contained true -p:XgWindowsPublish=true -p:RestoreLockedMode=true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -p:DebugSymbols=false -o "$bundle/updater"
    dotnet publish src/XGLauncher -c Release -r win-x64 --self-contained true -p:XgWindowsPublish=true -p:RestoreLockedMode=true `
        -p:XGBundleAssetsDirectory=$bundle -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true -p:DebugType=none -p:DebugSymbols=false -o dist/win-x64/XG-Launcher @auth
    if ($BuildInstaller) { & $MakeNsis installer/XGLauncher.nsi }
    Get-FileHash dist/win-x64/XG-Launcher/XGLauncher.exe -Algorithm SHA256
} finally { Pop-Location }
