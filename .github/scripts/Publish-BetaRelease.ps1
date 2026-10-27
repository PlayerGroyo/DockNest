[CmdletBinding()]
param([Parameter(Mandatory)][string]$Tag)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$metadata = & (Join-Path $PSScriptRoot 'Get-BetaVersion.ps1') -Tag $Tag
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$packages = Join-Path $repoRoot 'artifacts/beta'
$version = $metadata.Version
$assetNames = @("DockNest-$version-win-x64-self-contained.zip", "DockNest-$version-win-x64-framework-dependent.zip")

# Verify the downloaded build artifacts before creating or publishing a release.
$checksumLines = Get-Content -LiteralPath (Join-Path $packages 'SHA256SUMS.txt')
if ($checksumLines.Count -ne $assetNames.Count) { throw 'Unexpected checksum manifest.' }
foreach ($name in $assetNames) {
    $hash = (Get-FileHash -LiteralPath (Join-Path $packages $name) -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($checksumLines -cnotcontains "$hash  $name") { throw "Checksum mismatch: $name" }
}
$notesPath = Join-Path $packages 'release-notes.md'
$notes = Get-Content -LiteralPath $notesPath -Raw -Encoding utf8
if (-not $notes.Contains($metadata.Commit)) { throw 'Release notes do not match the checked-out commit.' }

$existing = & gh release view $Tag --repo $env:GH_REPO --json isDraft,isPrerelease,targetCommitish,url 2>&1
if ($LASTEXITCODE -eq 0) {
    $release = ($existing -join "`n") | ConvertFrom-Json
    if (-not $release.isDraft -or -not $release.isPrerelease) {
        throw "Release $Tag is already published or is not a beta draft. Use a new beta number."
    }
    $existingTag = git -C $repoRoot tag --list $Tag
    if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect the draft tag.' }
    if (-not $existingTag -and $release.targetCommitish -cne $metadata.Commit) {
        throw 'The existing draft targets another commit. Resolve the draft before retrying.'
    }
}
elseif (($existing -join "`n") -notmatch '(?i)release not found|HTTP 404') {
    throw "Cannot inspect existing release: $($existing -join ' ')"
}
else {
    & gh release create $Tag --repo $env:GH_REPO --target $metadata.Commit --draft --prerelease --latest=false `
        --title "DockNest $version" --notes-file $notesPath
    if ($LASTEXITCODE -ne 0) { throw 'Creating the beta draft failed.' }
}

$assets = @($assetNames | ForEach-Object { Join-Path $packages $_ }) + @(Join-Path $packages 'SHA256SUMS.txt')
& gh release upload $Tag @assets --repo $env:GH_REPO --clobber
if ($LASTEXITCODE -ne 0) { throw 'Uploading assets failed; the release remains a draft. Rerun the failed job to retry.' }
& gh release edit $Tag --repo $env:GH_REPO --draft=false --prerelease --latest=false --notes-file $notesPath
if ($LASTEXITCODE -ne 0) { throw 'Publishing the beta draft failed.' }
& gh release view $Tag --repo $env:GH_REPO --json url --jq .url
if ($LASTEXITCODE -ne 0) { throw 'Cannot read the published beta URL.' }
