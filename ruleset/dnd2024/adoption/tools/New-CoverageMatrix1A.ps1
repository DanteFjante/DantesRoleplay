[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path,
    [string]$OutputPath = 'ruleset/dnd2024/adoption/evidence/coverage-matrix-1a.json'
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path $RepositoryRoot).Path
function Full([string]$p) { if ([IO.Path]::IsPathRooted($p)) { [IO.Path]::GetFullPath($p) } else { [IO.Path]::GetFullPath((Join-Path $repo $p)) } }
function Rel([string]$p) { [IO.Path]::GetRelativePath($repo, $p).Replace('\','/') }
function Key($record) { (($record.kind + '.' + $record.id + '.v' + $record.version).ToLowerInvariant() -replace '[^a-z0-9._-]+','-') }
function Evidence([string]$prefix, $record) {
    $primary = Full ($prefix + '/' + $record.path)
    if (-not (Test-Path -LiteralPath $primary -PathType Leaf)) {
        $matches = @(Get-ChildItem (Full $prefix) -Recurse -File -Filter ([IO.Path]::GetFileName($record.path)) -ErrorAction SilentlyContinue)
        if ($matches.Count -eq 1) { $primary = $matches[0].FullName }
    }
    $paths = [Collections.Generic.List[string]]::new()
    if (Test-Path -LiteralPath $primary -PathType Leaf) { $paths.Add((Rel $primary)) }
    if ($record.kind -eq 'mechanic') {
        $js = [IO.Path]::ChangeExtension($primary, '.js'); if (Test-Path -LiteralPath $js) { $paths.Add((Rel $js)) }
    }
    if ($record.kind -eq 'componentDefinition') {
        $schema = [IO.Path]::Combine([IO.Path]::GetDirectoryName($primary), [IO.Path]::GetFileNameWithoutExtension($primary) + '.schema.json')
        if (Test-Path -LiteralPath $schema) { $paths.Add((Rel $schema)) }
    }
    if ($paths.Count -eq 0 -and $prefix -eq 'old-dnd/catalog') { $paths.Add('old-dnd/catalog-manifest.pre-archive.json') }
    return @($paths | Sort-Object -Unique)
}
$currentManifest = Get-Content -Raw (Full 'catalog/manifest.json') | ConvertFrom-Json -Depth 40
$archiveManifest = Get-Content -Raw (Full 'old-dnd/catalog-manifest.pre-archive.json') | ConvertFrom-Json -Depth 40
$current = @{}; foreach ($r in $currentManifest.records) { $k = Key $r; if ($current.ContainsKey($k)) { throw "Duplicate current capability: $k" }; $current[$k] = $r }
$archive = @{}; foreach ($r in $archiveManifest.records) { $k = Key $r; if ($archive.ContainsKey($k)) { throw "Duplicate archive capability: $k" }; $archive[$k] = $r }
$keys = @($current.Keys + $archive.Keys | Sort-Object -Unique)
$idToKey = @{}; foreach ($k in $keys) { $r = if ($current.ContainsKey($k)) { $current[$k] } else { $archive[$k] }; if (-not $idToKey.ContainsKey($r.id)) { $idToKey[$r.id] = $k } }
$currentTests = @(Get-ChildItem (Full 'DantesRoleplay.Tests') -Filter '*.cs' -File -ErrorAction SilentlyContinue)
$archiveTests = @(Get-ChildItem (Full 'old-dnd/DantesRoleplay.Tests') -Filter '*.cs' -File -ErrorAction SilentlyContinue)
$testText = @{}; foreach ($f in @($currentTests + $archiveTests)) { $testText[$f.FullName] = Get-Content -Raw $f.FullName }
$rows = [Collections.Generic.List[object]]::new()
foreach ($k in $keys) {
    $activeRecord = if ($current.ContainsKey($k)) { $current[$k] } else { $null }
    $archiveRecord = if ($archive.ContainsKey($k)) { $archive[$k] } else { $null }
    $record = if ($null -ne $activeRecord) { $activeRecord } else { $archiveRecord }
    $activeEvidence = if ($null -ne $activeRecord) { @(Evidence 'catalog' $activeRecord) } else { @() }
    $archiveEvidence = if ($null -ne $archiveRecord) { @(Evidence 'old-dnd/catalog' $archiveRecord) } else { @() }
    if (($null -ne $activeRecord -and $activeEvidence.Count -eq 0) -or ($null -ne $archiveRecord -and $archiveEvidence.Count -eq 0)) { throw "Missing primary evidence for $k" }
    $texts = @(@($activeEvidence) + @($archiveEvidence) | Where-Object { $_ -ne 'old-dnd/catalog-manifest.pre-archive.json' } | Sort-Object -Unique | ForEach-Object { Get-Content -Raw (Full $_) })
    $joined = $texts -join "`n"
    $isDnd = $record.id -like '*dnd2024*' -or $joined -match 'source\.dnd2024\.srd-5\.2\.1|System Reference Document 5\.2\.1'
    $locator = $null
    $matches = [regex]::Matches($joined, "Source:\s*System Reference Document 5\.2\.1,\s*'([^']+)'")
    if ($matches.Count -gt 0) { $locator = @($matches | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)[0] }
    if ($null -eq $locator) {
        $matches = [regex]::Matches($joined, '"sourceId"\s*:\s*"source\.dnd2024\.srd-5\.2\.1"\s*,\s*"locator"\s*:\s*"([^"]+)"')
        if ($matches.Count -gt 0) { $locator = @($matches | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)[0] }
    }
    $tests = [Collections.Generic.List[object]]::new()
    foreach ($f in $testText.Keys | Sort-Object) { if ($testText[$f].Contains($record.id)) { $tests.Add([ordered]@{ path = (Rel $f); result = 'historical-only' }) } }
    $dependencies = [Collections.Generic.List[string]]::new()
    foreach ($id in $idToKey.Keys) { if ($id -ne $record.id -and $joined.Contains($id)) { $dependencies.Add($idToKey[$id]) } }
    $conflicts = @()
    if ($null -ne $activeRecord -and $null -ne $archiveRecord -and $activeRecord.contentHash -ne $archiveRecord.contentHash) { $conflicts = @("Active/archive content hashes differ for $($record.kind) $($record.id) v$($record.version).") }
    $archiveCandidates = @()
    if ($null -ne $archiveRecord) {
        $archivePath = 'old-dnd/catalog/' + $archiveRecord.path
        if (-not (Test-Path (Full $archivePath))) { $archivePath = 'old-dnd/catalog-manifest.pre-archive.json' }
        $archiveCandidates = @([ordered]@{ path = $archivePath; symbol = $archiveRecord.id; evidence = @($archiveEvidence); status = $(if ($null -ne $activeRecord -and $conflicts.Count -eq 0) { 'compatible' } elseif ($conflicts.Count -gt 0) { 'conflicting' } else { 'unclassified' }) })
    }
    $rows.Add([ordered]@{
        capabilityKey = $k; title = $record.id; alignment = $(if ($isDnd) { 'dnd2024-compatible' } else { 'ruleset-neutral' })
        activeOwner = [ordered]@{ state = $(if ($null -ne $activeRecord) { 'verified' } else { 'missing' }); owner = $(if ($null -ne $activeRecord) { $activeRecord.id } else { $null }); evidence = @($activeEvidence) }
        archiveCandidates = $archiveCandidates; donorCandidates = @()
        srd = [ordered]@{ sourceId = $(if ($null -ne $locator) { 'source.dnd2024.srd-5.2.1' } else { $null }); locator = $locator; verified = $false; edition = $(if ($isDnd) { '2024' } else { 'not-applicable' }) }
        foundryReferences = @(); conflicts = $conflicts; dependencies = @($dependencies | Sort-Object -Unique); tests = @($tests)
        disposition = $(if ($null -ne $activeRecord) { 'retain-active' } else { 'recover-archive' })
        assignment = [ordered]@{ model = 'gpt-5.6-luna'; reasoning = 'medium'; reviewModel = $(if ($isDnd -or $conflicts.Count -gt 0) { 'gpt-5.6-sol' } else { 'gpt-5.6-terra' }); reviewReasoning = 'high' }
        status = 'inventory-complete'
    })
}
$manifestInputs = @('catalog/manifest.json','old-dnd/catalog-manifest.pre-archive.json')
$inputLines = foreach ($p in $manifestInputs) { "$p|$((Get-FileHash (Full $p) -Algorithm SHA256).Hash.ToUpperInvariant())" }
$sha = [Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes(($inputLines -join "`n")))
$inputHash = ([BitConverter]::ToString($sha) -replace '-','').ToUpperInvariant()
$document = [ordered]@{ format = 'dnd-code-adoption-coverage-matrix/v1'; inventoryCommit = (git -C $repo rev-parse HEAD).Trim(); inputSha256 = $inputHash; rows = @($rows) }
$out = Full $OutputPath; New-Item -ItemType Directory -Force (Split-Path $out) | Out-Null; [IO.File]::WriteAllText($out, ($document | ConvertTo-Json -Depth 30) + "`n", [Text.UTF8Encoding]::new($false))
Write-Output ([ordered]@{ output = (Rel $out); rows = $rows.Count; active = $current.Count; archive = $archive.Count; inputSha256 = $inputHash } | ConvertTo-Json -Compress)
