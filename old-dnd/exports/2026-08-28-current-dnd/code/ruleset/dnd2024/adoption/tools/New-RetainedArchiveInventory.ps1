[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path,
    [string]$ArchivePath = 'old-dnd',
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$archiveRelative = $ArchivePath.Replace('\', '/').TrimEnd('/')
if ($archiveRelative -notmatch '^[A-Za-z0-9][A-Za-z0-9._/-]*$' -or
    $archiveRelative -match '(^|/)\.\.(/|$)') {
    throw 'ArchivePath must be a safe repository-relative path.'
}
$archiveFull = [IO.Path]::GetFullPath((Join-Path $repo $archiveRelative))
$repoPrefix = $repo.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $archiveFull.StartsWith($repoPrefix, [StringComparison]::OrdinalIgnoreCase) -or
    -not (Test-Path -LiteralPath $archiveFull -PathType Container)) {
    throw 'The archive root is missing or outside the repository.'
}
$outputFull = $null
$outputRelative = $null
if ($OutputPath) {
    $outputFull = [IO.Path]::GetFullPath($OutputPath, $repo)
    $archivePrefix = $archiveFull.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if ($outputFull -ieq $archiveFull -or $outputFull.StartsWith($archivePrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'The inventory report cannot be written inside the retained archive.'
    }
    if ($outputFull.StartsWith($repoPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        $outputRelative = [IO.Path]::GetRelativePath($repo, $outputFull).Replace('\', '/')
    }
}

function Invoke-GitRequired {
    param([string[]]$Arguments, [string]$Failure)
    $lines = @(& git -C $repo @Arguments 2>&1 | ForEach-Object { "$_" })
    if ($LASTEXITCODE -ne 0) { throw "${Failure}: $($lines -join [Environment]::NewLine)" }
    return @($lines)
}
function Full([string]$RelativePath) {
    $full = [IO.Path]::GetFullPath((Join-Path $repo $RelativePath))
    if (-not $full.StartsWith($repoPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Tracked path escapes the repository: $RelativePath"
    }
    return $full
}
function Get-RetentionClass([string]$Path) {
    if ($Path -ceq "$archiveRelative/README.md" -or $Path -ceq "$archiveRelative/catalog-manifest.pre-archive.json") { return 'archive-metadata' }
    if ($Path.StartsWith("$archiveRelative/catalog/", [StringComparison]::Ordinal)) { return 'historical-catalog' }
    if ($Path.StartsWith("$archiveRelative/DantesRoleplay.Tests/", [StringComparison]::Ordinal)) { return 'historical-tests' }
    if ($Path.StartsWith("$archiveRelative/ruleset/", [StringComparison]::Ordinal)) { return 'historical-plans-and-evidence' }
    if ($Path.StartsWith("$archiveRelative/src/", [StringComparison]::Ordinal)) { return 'historical-compiled-adapter' }
    if ($Path.StartsWith("$archiveRelative/character/", [StringComparison]::Ordinal)) { return 'historical-character-source' }
    return 'historical-root-document'
}
function Get-ConsumerClass([string]$Path) {
    $name = [IO.Path]::GetFileName($Path)
    $extension = [IO.Path]::GetExtension($Path).ToLowerInvariant()
    if ($extension -in @('.sln', '.slnx', '.csproj', '.props', '.targets') -or
        $name -in @('global.json', 'Directory.Build.props', 'Directory.Build.targets')) { return 'runtime-build-configuration' }
    if ($Path.StartsWith('catalog/', [StringComparison]::Ordinal)) { return 'active-catalog' }
    if ($extension -ceq '.cs' -and ($Path -match '(^|/)[^/]*Tests?(/|\.)')) { return 'compiled-test' }
    if ($extension -ceq '.cs') { return 'compiled-production-source' }
    if ($Path.StartsWith('ruleset/dnd2024/adoption/tools/', [StringComparison]::Ordinal) -and $extension -ceq '.ps1') { return 'adoption-tool' }
    if ($Path.StartsWith('ruleset/dnd2024/adoption/evidence/', [StringComparison]::Ordinal)) { return 'durable-evidence' }
    if ($Path.StartsWith('ruleset/dnd2024/adoption/', [StringComparison]::Ordinal) -and $extension -eq '.json') { return 'adoption-fixture-or-contract' }
    if ($extension -eq '.md') { return 'documentation' }
    return 'other-development-material'
}

$tracked = @(Invoke-GitRequired @('ls-files', '--', $archiveRelative) 'Could not enumerate tracked archive files' |
    ForEach-Object { $_.Replace('\', '/') } | Where-Object { $_ -ne '' } | Sort-Object -Unique)
if ($tracked.Count -eq 0) { throw 'The archive contains no tracked files.' }
if (@($tracked | Sort-Object -Unique).Count -ne $tracked.Count) { throw 'The archive contains duplicate tracked paths.' }
$fileRows = [Collections.Generic.List[object]]::new()
$aggregate = [Text.StringBuilder]::new()
[long]$totalBytes = 0
foreach ($path in $tracked) {
    if (-not $path.StartsWith("$archiveRelative/", [StringComparison]::Ordinal)) {
        throw "Unexpected tracked archive path: $path"
    }
    $full = Full $path
    if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { throw "Tracked archive file is missing: $path" }
    $item = Get-Item -LiteralPath $full
    $totalBytes += $item.Length
    $hash = (Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash.ToUpperInvariant()
    [void]$aggregate.Append($path).Append("`0").Append($hash).Append("`0").Append($item.Length).Append("`n")
    $fileRows.Add([ordered]@{
        path = $path
        length = [long]$item.Length
        sha256 = $hash
        extension = [IO.Path]::GetExtension($path).ToLowerInvariant()
        retentionClass = Get-RetentionClass $path
    })
}
$aggregateHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData(
    [Text.Encoding]::UTF8.GetBytes($aggregate.ToString())))

$textExtensions = @('.md', '.json', '.cs', '.js', '.ps1', '.cmd', '.sh', '.xml', '.props',
    '.targets', '.sln', '.slnx', '.csproj', '.toml', '.yml', '.yaml', '.txt')
$repositoryFiles = @(Invoke-GitRequired @('ls-files', '--cached', '--others', '--exclude-standard') 'Could not enumerate repository files' |
    ForEach-Object { $_.Replace('\', '/') } | Sort-Object -Unique)
$consumers = [Collections.Generic.List[object]]::new()
foreach ($path in $repositoryFiles) {
    if ($path.StartsWith("$archiveRelative/", [StringComparison]::Ordinal)) { continue }
    if ($null -ne $outputRelative -and $path -ceq $outputRelative) { continue }
    if ($path -match '^ruleset/dnd2024/adoption/evidence/retained-archive-inventory-13a(?:\.[^.]+)?\.json$') { continue }
    $extension = [IO.Path]::GetExtension($path).ToLowerInvariant()
    if ($extension -notin $textExtensions -and [IO.Path]::GetFileName($path) -notin @('global.json', '.gitignore')) { continue }
    $full = Full $path
    if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { continue }
    $text = Get-Content -Raw -LiteralPath $full -ErrorAction SilentlyContinue
    if ($null -ne $text -and $text.Contains($archiveRelative, [StringComparison]::Ordinal)) {
        $consumers.Add([ordered]@{ path = $path; classification = Get-ConsumerClass $path })
    }
}
$consumers = @($consumers | Sort-Object path)

$transformationSources = [Collections.Generic.List[object]]::new()
$manifestPaths = @($repositoryFiles | Where-Object {
    $_.StartsWith('ruleset/dnd2024/adoption/transformation/fixtures/', [StringComparison]::Ordinal) -and
    $_.EndsWith('.json', [StringComparison]::Ordinal)
})
foreach ($manifestPath in $manifestPaths) {
    $manifest = Get-Content -Raw -LiteralPath (Full $manifestPath) | ConvertFrom-Json -Depth 100
    $formatProperty = $manifest.PSObject.Properties['format']
    if ($null -eq $formatProperty -or "$($formatProperty.Value)" -cne 'dnd2024-static-content-transform/v1') { continue }
    foreach ($entry in @($manifest.entries)) {
        $sourcePath = "$($entry.sourcePath)".Replace('\', '/')
        if (-not $sourcePath.StartsWith("$archiveRelative/", [StringComparison]::Ordinal)) {
            throw "Transformation source is outside the archive: $manifestPath -> $sourcePath"
        }
        if ($tracked -cnotcontains $sourcePath) { throw "Transformation source is not tracked: $sourcePath" }
        $actual = (Get-FileHash -LiteralPath (Full $sourcePath) -Algorithm SHA256).Hash.ToUpperInvariant()
        $expected = "$($entry.sourceSha256)".ToUpperInvariant()
        if ($actual -cne $expected) { throw "Transformation source hash drift: $sourcePath" }
        $transformationSources.Add([ordered]@{
            manifest = $manifestPath
            sourcePath = $sourcePath
            sourceSha256 = $actual
            targetPath = "$($entry.targetPath)".Replace('\', '/')
        })
    }
}
$transformationSources = @($transformationSources | Sort-Object manifest, sourcePath)
$blockingConsumers = @($consumers | Where-Object {
    $_.classification -in @('compiled-test', 'adoption-tool', 'adoption-fixture-or-contract', 'durable-evidence')
})
$runtimeConsumers = @($consumers | Where-Object {
    $_.classification -in @('runtime-build-configuration', 'active-catalog', 'compiled-production-source')
})
$blockers = [Collections.Generic.List[string]]::new()
if ($blockingConsumers.Count -gt 0) { $blockers.Add('Tracked development tests, tools, fixtures, or evidence still consume or cite the archive.') }
if ($transformationSources.Count -gt 0) { $blockers.Add('Accepted transformation manifests still verify exact archive source bytes.') }
$blockers.Add('The explicit archive-retention decision remains in force; no destructive removal was requested.')

$report = [ordered]@{
    format = 'dnd2024-retained-archive-inventory/v1'
    archivePath = $archiveRelative
    disposition = 'retain'
    deletionReady = $false
    archive = [ordered]@{
        trackedFileCount = $fileRows.Count
        totalBytes = $totalBytes
        aggregateSha256 = $aggregateHash
        files = @($fileRows)
        retentionClasses = @($fileRows | Group-Object { $_['retentionClass'] } | Sort-Object Name |
            ForEach-Object { [ordered]@{ name = $_.Name; count = $_.Count } })
        extensions = @($fileRows | Group-Object { $_['extension'] } | Sort-Object Name |
            ForEach-Object { [ordered]@{ name = $_.Name; count = $_.Count } })
    }
    consumers = [ordered]@{
        count = $consumers.Count
        runtimeCount = $runtimeConsumers.Count
        blockingDevelopmentCount = $blockingConsumers.Count
        entries = $consumers
        classifications = @($consumers | Group-Object { $_['classification'] } | Sort-Object Name |
            ForEach-Object { [ordered]@{ name = $_.Name; count = $_.Count } })
    }
    transformationSources = [ordered]@{
        count = $transformationSources.Count
        allHashesMatch = $true
        entries = $transformationSources
    }
    blockers = @($blockers)
    archiveWrites = 'none'
}
$json = ($report | ConvertTo-Json -Depth 20) + "`n"
if ($OutputPath) {
    $parent = Split-Path -Parent $outputFull
    if (-not (Test-Path -LiteralPath $parent -PathType Container)) { [IO.Directory]::CreateDirectory($parent) | Out-Null }
    [IO.File]::WriteAllText($outputFull, $json, [Text.UTF8Encoding]::new($false))
}
[ordered]@{
    format = $report.format
    disposition = $report.disposition
    deletionReady = $report.deletionReady
    trackedFiles = $report.archive.trackedFileCount
    aggregateSha256 = $report.archive.aggregateSha256
    consumers = $report.consumers.count
    runtimeConsumers = $report.consumers.runtimeCount
    blockingDevelopmentConsumers = $report.consumers.blockingDevelopmentCount
    transformationSources = $report.transformationSources.count
    archiveWrites = $report.archiveWrites
} | ConvertTo-Json
