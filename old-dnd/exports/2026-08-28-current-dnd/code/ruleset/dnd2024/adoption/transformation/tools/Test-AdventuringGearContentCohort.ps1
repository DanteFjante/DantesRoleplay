[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../../../..')).Path,
    [string]$ManifestPath = (Join-Path $PSScriptRoot '../fixtures/slice-10b1a-adventuring-gear-transform.json')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Resolve-ContainedPath {
    param([string]$Root, [string]$RelativePath)
    $rootFull = [IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $full = [IO.Path]::GetFullPath((Join-Path $rootFull $RelativePath))
    if (-not $full.StartsWith($rootFull + [IO.Path]::DirectorySeparatorChar,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "Path escapes the repository root: $RelativePath"
    }
    return $full
}

function Property-Names {
    param($Value)
    return @($Value.PSObject.Properties.Name | Sort-Object)
}

function Assert-Names {
    param($Value, [string[]]$Expected, [string]$Subject)
    $actual = @(Property-Names $Value)
    $wanted = @($Expected | Sort-Object)
    if (($actual -join ',') -cne ($wanted -join ',')) {
        throw "$Subject has an unexpected shape: $($actual -join ', ')"
    }
}

function Canonical-Json {
    param($Value)
    return ($Value | ConvertTo-Json -Depth 30 -Compress)
}

function Clone-Json {
    param($Value)
    return (Canonical-Json $Value) | ConvertFrom-Json -Depth 30
}

$root = [IO.Path]::GetFullPath($RepositoryRoot)
$manifestFull = [IO.Path]::GetFullPath($ManifestPath)
$manifest = Get-Content -Raw -LiteralPath $manifestFull | ConvertFrom-Json -Depth 40
Assert-Names $manifest @('format','cohort','sourceRevision','officialSource','license','quarantine','entries') 'Manifest'
if ($manifest.format -cne 'dnd2024-static-content-transform/v1' -or
    $manifest.cohort -cne 'slice-10b1a-adventuring-gear') {
    throw 'Unexpected adventuring-gear transform manifest.'
}
Assert-Names $manifest.officialSource @('sourceId','url','locatorPolicy') 'Official source'
if ($manifest.officialSource.sourceId -cne 'source.dnd2024.srd-5.2.1' -or
    $manifest.officialSource.url -cne 'https://media.dndbeyond.com/compendium-images/srd/5.2/SRD_CC_v5.2.1.pdf' -or
    -not $manifest.officialSource.locatorPolicy.StartsWith('Equipment > Adventuring Gear > ',
        [StringComparison]::Ordinal)) {
    throw 'The official adventuring-gear source binding drifted.'
}
Assert-Names $manifest.license @('classification','attribution','changes') 'License'
if ($manifest.license.classification -cne 'CC-BY-4.0' -or
    [string]::IsNullOrWhiteSpace($manifest.license.attribution) -or
    [string]::IsNullOrWhiteSpace($manifest.license.changes)) {
    throw 'CC BY attribution and change indication are required.'
}

$expectedQuarantine = @(
    'item.dnd2024.hempen-rope-50-foot.v1',
    'item.dnd2024.quiver.v1'
)
if (@($manifest.quarantine).Count -ne 2 -or
    (@($manifest.quarantine.id | Sort-Object) -join ',') -cne (($expectedQuarantine | Sort-Object) -join ',')) {
    throw 'The Rope and Quiver representation gaps must remain explicitly quarantined.'
}
foreach ($blocked in @($manifest.quarantine)) {
    Assert-Names $blocked @('id','reason') "Quarantine $($blocked.id)"
    if ([string]::IsNullOrWhiteSpace($blocked.reason)) { throw "Missing quarantine reason: $($blocked.id)" }
}

$expectedIds = @(
    'item.dnd2024.backpack.v1',
    'item.dnd2024.caltrops-bag.v1',
    'item.dnd2024.crowbar.v1',
    'item.dnd2024.oil-flask.v1',
    'item.dnd2024.pouch.v1',
    'item.dnd2024.rations-one-day.v1',
    'item.dnd2024.tinderbox.v1',
    'item.dnd2024.torch.v1',
    'item.dnd2024.waterskin.v1'
)
if (@($manifest.entries).Count -ne 9 -or
    (@($manifest.entries.id | Sort-Object) -join ',') -cne (($expectedIds | Sort-Object) -join ',')) {
    throw 'The adventuring-gear cohort must contain exactly the nine approved IDs.'
}

$seenIds = @{}; $seenPaths = @{}; $results = @()
foreach ($entry in @($manifest.entries)) {
    Assert-Names $entry @('id','sourceName','targetName','sourcePath','sourceSha256','targetPath','expectedDefinition') $entry.id
    if ($seenIds.ContainsKey($entry.id)) { throw "Duplicate target ID: $($entry.id)" }
    if ($seenPaths.ContainsKey($entry.targetPath)) { throw "Duplicate target path: $($entry.targetPath)" }
    $seenIds[$entry.id] = $true; $seenPaths[$entry.targetPath] = $true

    $sourcePath = Resolve-ContainedPath $root $entry.sourcePath
    $targetPath = Resolve-ContainedPath $root $entry.targetPath
    $sourceHash = (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash.ToUpperInvariant()
    if ($sourceHash -cne $entry.sourceSha256) { throw "Source hash drift: $($entry.sourcePath)" }
    if (-not (Test-Path -LiteralPath $targetPath -PathType Leaf)) { throw "Target is missing: $($entry.targetPath)" }

    $source = Get-Content -Raw -LiteralPath $sourcePath | ConvertFrom-Json -Depth 30
    Assert-Names $source @('id','name','components') $entry.sourcePath
    Assert-Names $source.components @('dnd2024.item-definition') "$($entry.id) components"
    if ($source.id -cne $entry.id -or $source.name -cne $entry.sourceName) {
        throw "Archived source identity drift: $($entry.sourcePath)"
    }
    $sourceDefinition = Clone-Json $source.components.'dnd2024.item-definition'
    if ($sourceDefinition.sourceRef.sourceId -cne 'source.dnd2024.srd-5.2.1' -or
        $sourceDefinition.sourceRef.locator -cne 'Equipment > Adventuring Gear') {
        throw "Archived source binding drift: $($entry.sourcePath)"
    }
    $sourceDefinition.sourceRef.locator = $entry.expectedDefinition.sourceRef.locator
    if ((Canonical-Json $sourceDefinition) -cne (Canonical-Json $entry.expectedDefinition)) {
        throw "Archived static meaning differs from the reviewed mapping: $($entry.sourcePath)"
    }

    $generated = [ordered]@{
        id = $entry.id
        name = $entry.targetName
        components = [ordered]@{
            'dnd2024.item-definition' = $entry.expectedDefinition
        }
    }
    $target = Get-Content -Raw -LiteralPath $targetPath | ConvertFrom-Json -Depth 30
    Assert-Names $target @('id','name','components') $entry.targetPath
    Assert-Names $target.components @('dnd2024.item-definition') "$($entry.id) target components"
    if ((Canonical-Json $target) -cne (Canonical-Json $generated)) {
        throw "Deterministic target drift: $($entry.targetPath)"
    }
    $results += [ordered]@{
        id = $entry.id
        targetPath = $entry.targetPath
        targetSha256 = (Get-FileHash -LiteralPath $targetPath -Algorithm SHA256).Hash.ToUpperInvariant()
    }
}

[ordered]@{
    format = 'dnd2024-static-content-transform-report/v1'
    cohort = $manifest.cohort
    status = 'verified'
    candidateCount = $results.Count
    quarantinedCount = @($manifest.quarantine).Count
    candidates = $results
} | ConvertTo-Json -Depth 10
