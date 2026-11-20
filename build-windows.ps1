param([string]$MakeNsis = "makensis", [Guid]$MicrosoftClientId = [Guid]::Empty)
$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $true
Push-Location $PSScriptRoot
try {
    $nsisVersion = (& $MakeNsis /VERSION | Out-String).Trim()
    if ($nsisVersion -ne "v3.11") { throw "Use official NSIS 3.11; re-audit the license before changing the build tool version." }
    $bundle = Join-Path $PSScriptRoot 'src/XGLauncher/obj/single-exe-assets'
    $payload = Join-Path $PSScriptRoot 'dist/win-x64/XG-Launcher'
    # Only remove prior build output, never an installation or user directory.
    if (Test-Path $payload) { Remove-Item $payload -Recurse -Force }
    python scripts/prepare-single-exe.py $bundle
    $flags = @('-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-p:XgWindowsPublish=true', '-p:RestoreLockedMode=true',
        '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:EnableCompressionInSingleFile=true', '-p:DebugType=none', '-p:DebugSymbols=false')
    dotnet publish src/XG.Updater @flags -o "$bundle/updater"
    $auth = @()
    if ($MicrosoftClientId -ne [Guid]::Empty) { $auth += "-p:XGMicrosoftClientId=$MicrosoftClientId" }
    dotnet publish src/XGLauncher @flags "-p:XGBundleAssetsDirectory=$bundle" -o $payload @auth
    python scripts/verify-portable.py $payload dist/XGLauncher-portable-win-x64.zip
    & $MakeNsis installer/XGLauncher.nsi
    Get-FileHash "$payload/XGLauncher.exe",dist/XGLauncher-Setup-win-x64.exe,dist/XGLauncher-portable-win-x64.zip -Algorithm SHA256
} finally { Pop-Location }
