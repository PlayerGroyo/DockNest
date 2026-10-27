[CmdletBinding()]
param([Parameter(Mandatory)][string]$Tag)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($Tag -cnotmatch '^v(?<base>(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*))-beta\.(?<number>[1-9][0-9]*)$') {
    throw 'Use a beta tag such as v0.1.7-beta.1 (no leading zeroes; beta number starts at 1).'
}
$baseVersion = $Matches['base']
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
[xml]$project = Get-Content -LiteralPath (Join-Path $repoRoot 'DockNest.csproj') -Raw
$projectVersion = [string]$project.Project.PropertyGroup.Version
if ($baseVersion -cne $projectVersion) {
    throw "Tag version $baseVersion does not match DockNest.csproj version $projectVersion."
}

$commit = git -C $repoRoot rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve HEAD.' }
$existingTag = git -C $repoRoot tag --list $Tag
if ($LASTEXITCODE -ne 0) { throw 'Cannot check the local tag.' }
if ($existingTag) {
    $tagCommit = git -C $repoRoot rev-parse "$Tag^{commit}"
    if ($LASTEXITCODE -ne 0 -or $tagCommit -cne $commit) { throw "Tag $Tag already points to another commit." }
}

[pscustomobject]@{ Tag = $Tag; Version = $Tag.Substring(1); BaseVersion = $baseVersion; Commit = $commit }
