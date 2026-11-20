param([Parameter(Mandatory=$true)][string]$ExpectedCommit)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$tag = 'v0.1.1-alpha'
if ($env:GH_REPO -ne 'Zc345370669/XG-Launcher' -or $ExpectedCommit -notmatch '^[0-9a-f]{40}$') { throw 'Unexpected release repository or commit.' }
[xml]$project = Get-Content -Raw Directory.Build.props
if ($project.Project.PropertyGroup.Version -ne '0.1.1-alpha') { throw 'Release version mismatch.' }
$asset = Resolve-Path 'release-files/XGLauncher-portable-win-x64.zip'
$hash = (Get-FileHash $asset -Algorithm SHA256).Hash.ToLowerInvariant()
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead($asset)
try {
    if ($archive.Entries.Count -ne 1 -or $archive.Entries[0].FullName -ne 'XG-Launcher/XGLauncher.exe') { throw 'Unexpected release ZIP layout.' }
} finally { $archive.Dispose() }
# No clobber flag: an existing release/tag is never silently overwritten.
gh release create $tag $asset --repo $env:GH_REPO --target $ExpectedCommit --title 'XG Launcher v0.1.1-alpha' --prerelease --latest=false --notes-file docs/releases/v0.1.1-alpha.md
$metadata = gh api "repos/$env:GH_REPO/releases/tags/$tag" | ConvertFrom-Json
$uploaded = @($metadata.assets | Where-Object name -eq 'XGLauncher-portable-win-x64.zip')
if ($metadata.draft -or !$metadata.prerelease -or $uploaded.Count -ne 1 -or $uploaded[0].digest -ne "sha256:$hash" -or $uploaded[0].size -ne (Get-Item $asset).Length) { throw 'Published release metadata/digest differs from verified artifact.' }
$reference = gh api "repos/$env:GH_REPO/git/ref/tags/$tag" | ConvertFrom-Json
if ($reference.object.sha -ne $ExpectedCommit) { throw 'Release tag does not point to tested main commit.' }
Write-Output "PASS published $tag; official ZIP digest/size/tag match tested artifact and commit."
Write-Output $metadata.html_url
