[CmdletBinding()]
param([Parameter(Mandatory)][string]$Tag)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$metadata = & (Join-Path $PSScriptRoot 'Get-BetaVersion.ps1') -Tag $Tag
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$version = $metadata.Version
$output = Join-Path $repoRoot 'artifacts/beta'
$staging = Join-Path $repoRoot ('artifacts/verification/beta-packages/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $output, $staging -Force | Out-Null
$archives = [Collections.Generic.List[string]]::new()

foreach ($flavor in @('self-contained', 'framework-dependent')) {
    $archive = Join-Path $output "DockNest-$version-win-x64-$flavor.zip"
    if (Test-Path -LiteralPath $archive) { throw "Package already exists: $archive. Use a fresh output directory for a new build." }
}

foreach ($flavor in @('self-contained', 'framework-dependent')) {
    $publish = Join-Path $staging "$flavor/publish"
    $build = Join-Path $staging "$flavor/build/"
    $selfContained = if ($flavor -eq 'self-contained') { 'true' } else { 'false' }
    & dotnet publish (Join-Path $repoRoot 'DockNest.csproj') -c Release -r win-x64 --self-contained $selfContained `
        "-p:Version=$version" '-p:ContinuousIntegrationBuild=true' '-p:DebugType=None' '-p:DebugSymbols=false' `
        '-p:PublishSingleFile=false' '-p:PublishTrimmed=false' "-p:OutputPath=$build" -o $publish
    if ($LASTEXITCODE -ne 0) { throw "Publishing $flavor failed." }

    foreach ($file in @('DockNest.exe', 'DockNest.dll', 'DockNest.deps.json', 'DockNest.runtimeconfig.json', 'README.md', 'README.en.md', 'LICENSE', 'THIRD-PARTY-NOTICES.txt')) {
        if (-not (Test-Path -LiteralPath (Join-Path $publish $file) -PathType Leaf)) { throw "Missing published file: $file" }
    }
    $actualVersion = (Get-Item -LiteralPath (Join-Path $publish 'DockNest.exe')).VersionInfo.ProductVersion
    if ($actualVersion -notlike "$version+*") { throw "Incorrect executable version: $actualVersion" }
    foreach ($readme in @('README.md', 'README.en.md')) {
        $text = Get-Content -LiteralPath (Join-Path $publish $readme) -Raw -Encoding utf8
        foreach ($image in [regex]::Matches($text, '!\[[^\]]*\]\(([^)]+)\)')) {
            $path = $image.Groups[1].Value
            if (-not (Test-Path -LiteralPath (Join-Path $publish $path) -PathType Leaf)) { throw "Missing README image: $path" }
        }
    }
    if ($selfContained -eq 'true') {
        foreach ($runtimeFile in @('coreclr.dll', 'hostfxr.dll', 'PresentationFramework.dll')) {
            if (-not (Test-Path -LiteralPath (Join-Path $publish $runtimeFile))) { throw "Missing bundled runtime: $runtimeFile" }
        }
    }
    elseif (Test-Path -LiteralPath (Join-Path $publish 'coreclr.dll')) { throw 'The framework-dependent package unexpectedly includes the runtime.' }

    $archive = Join-Path $output "DockNest-$version-win-x64-$flavor.zip"
    [IO.Compression.ZipFile]::CreateFromDirectory($publish, $archive, [IO.Compression.CompressionLevel]::Optimal, $false)
    $archives.Add($archive)
}

$checksums = foreach ($archive in $archives) {
    (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($archive)
}
[IO.File]::WriteAllLines((Join-Path $output 'SHA256SUMS.txt'), $checksums, [Text.UTF8Encoding]::new($false))
$notes = @'
## DockNest __VERSION__ · Beta

Windows x64 预发布版本 / Windows x64 pre-release build.

| 下载 / Download | 说明 / Requirements |
| --- | --- |
| `DockNest-__VERSION__-win-x64-self-contained.zip` | 内含 .NET 8 Desktop Runtime，解压即可运行 / Includes the runtime; extract and run |
| `DockNest-__VERSION__-win-x64-framework-dependent.zip` | 体积更小，需要安装 .NET 8 Desktop Runtime x64 / Smaller download; requires .NET 8 Desktop Runtime x64 |

解压整个 ZIP 后运行 `DockNest.exe`。更新前退出旧版本；收藏数据保存在 `%LOCALAPPDATA%\DockNest`，不在程序目录中。

Extract the entire ZIP and run `DockNest.exe`. Exit the previous version before updating. Bookmarks remain in `%LOCALAPPDATA%\DockNest`, separately from the app.

界面语言可在设置中切换。Git 操作需要自行安装 Git。/ Switch the interface language in Settings. Git operations require a local Git installation.

SHA-256 校验值见 `SHA256SUMS.txt`。/ Checksums are provided in `SHA256SUMS.txt`.

CI 覆盖数据、Git、本地化资源和离屏界面渲染；屏幕边缘停靠、托盘和焦点行为需要本机完整检查。

CI covers data, Git, localization resources and offscreen rendering. Edge docking, tray and focus behavior require the full local desktop checks.

Commit: `__COMMIT__`
'@
$notes = $notes.Replace('__VERSION__', $version).Replace('__COMMIT__', $metadata.Commit)
[IO.File]::WriteAllText((Join-Path $output 'release-notes.md'), $notes, [Text.UTF8Encoding]::new($false))
Write-Host "Beta packages: $output"
