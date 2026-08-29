[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../../../..')).Path,
    [string]$ManifestPath = (Join-Path $PSScriptRoot '../fixtures/slice-10b2a-light-armor-transform.json')
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

function Property-Names { param($Value); return @($Value.PSObject.Properties.Name | Sort-Object) }
function Assert-Names {
    param($Value, [string[]]$Expected, [string]$Subject)
    $actual = @(Property-Names $Value); $wanted = @($Expected | Sort-Object)
    if (($actual -join ',') -cne ($wanted -join ',')) {
        throw "$Subject has an unexpected shape: $($actual -join ', ')"
    }
}
function Canonical-Json { param($Value); return ($Value | ConvertTo-Json -Depth 30 -Compress) }
function Clone-Json { param($Value); return (Canonical-Json $Value) | ConvertFrom-Json -Depth 30 }

$categoryIds = @{
    light = @(
        'item.dnd2024.leather-armor.v1',
        'item.dnd2024.padded-armor.v1',
        'item.dnd2024.studded-leather-armor.v1'
    )
    medium = @(
        'item.dnd2024.breastplate.v1',
        'item.dnd2024.chain-shirt.v1',
        'item.dnd2024.half-plate-armor.v1',
        'item.dnd2024.hide-armor.v1',
        'item.dnd2024.scale-mail.v1'
    )
    heavy = @(
        'item.dnd2024.chain-mail.v1',
        'item.dnd2024.plate-armor.v1',
        'item.dnd2024.ring-mail.v1',
        'item.dnd2024.splint-armor.v1'
    )
    shield = @('item.dnd2024.shield.v1')
}

$root = [IO.Path]::GetFullPath($RepositoryRoot)
$manifest = Get-Content -Raw -LiteralPath ([IO.Path]::GetFullPath($ManifestPath)) |
    ConvertFrom-Json -Depth 40
Assert-Names $manifest @('format','cohort','category','sourceRevision','officialSource','license','entries') 'Manifest'
if ($manifest.format -cne 'dnd2024-static-content-transform/v1' -or
    -not $manifest.cohort.StartsWith('slice-10b2', [StringComparison]::Ordinal) -or
    -not $categoryIds.ContainsKey($manifest.category)) {
    throw 'Unexpected armor transform manifest.'
}
Assert-Names $manifest.officialSource @('sourceId','url','locator') 'Official source'
if ($manifest.officialSource.sourceId -cne 'source.dnd2024.srd-5.2.1' -or
    $manifest.officialSource.url -cne 'https://media.dndbeyond.com/compendium-images/srd/5.2/SRD_CC_v5.2.1.pdf' -or
    $manifest.officialSource.locator -cne 'Equipment > Armor > Armor table (PDF p. 92)') {
    throw 'The official Armor table source binding drifted.'
}
Assert-Names $manifest.license @('classification','attribution','changes') 'License'
if ($manifest.license.classification -cne 'CC-BY-4.0' -or
    [string]::IsNullOrWhiteSpace($manifest.license.attribution) -or
    [string]::IsNullOrWhiteSpace($manifest.license.changes)) {
    throw 'CC BY attribution and change indication are required.'
}

$expectedIds = @($categoryIds[$manifest.category])
if (@($manifest.entries).Count -ne $expectedIds.Count -or
    (@($manifest.entries.id | Sort-Object) -join ',') -cne (($expectedIds | Sort-Object) -join ',')) {
    throw "The $($manifest.category) armor cohort is incomplete or contains an unexpected ID."
}

$seenIds = @{}; $seenPaths = @{}; $results = @()
foreach ($entry in @($manifest.entries)) {
    Assert-Names $entry @('id','name','sourcePath','sourceSha256','targetPath') $entry.id
    if ($seenIds.ContainsKey($entry.id)) { throw "Duplicate target ID: $($entry.id)" }
    if ($seenPaths.ContainsKey($entry.targetPath)) { throw "Duplicate target path: $($entry.targetPath)" }
    $seenIds[$entry.id] = $true; $seenPaths[$entry.targetPath] = $true

    $sourcePath = Resolve-ContainedPath $root $entry.sourcePath
    $targetPath = Resolve-ContainedPath $root $entry.targetPath
    if ((Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash.ToUpperInvariant() -cne
        $entry.sourceSha256) { throw "Source hash drift: $($entry.sourcePath)" }
    if (-not (Test-Path -LiteralPath $targetPath -PathType Leaf)) {
        throw "Target is missing: $($entry.targetPath)"
    }

    $source = Get-Content -Raw -LiteralPath $sourcePath | ConvertFrom-Json -Depth 30
    Assert-Names $source @('id','name','components') $entry.sourcePath
    Assert-Names $source.components @('dnd2024.item-definition') "$($entry.id) components"
    if ($source.id -cne $entry.id -or $source.name -cne $entry.name) {
        throw "Archived source identity drift: $($entry.sourcePath)"
    }
    $definition = Clone-Json $source.components.'dnd2024.item-definition'
    $expectedKind = if ($manifest.category -ceq 'shield') { 'shield' } else { 'armor' }
    if ($definition.kind -cne $expectedKind -or
        $definition.armorProfile.category -cne $manifest.category -or
        $definition.sourceRef.sourceId -cne 'source.dnd2024.srd-5.2.1' -or
        $definition.sourceRef.locator -cne 'Equipment > Armor') {
        throw "Archived armor meaning drift: $($entry.sourcePath)"
    }
    $definition.sourceRef.locator = $manifest.officialSource.locator
    $generated = [ordered]@{
        id = $entry.id
        name = $entry.name
        components = [ordered]@{ 'dnd2024.item-definition' = $definition }
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
    category = $manifest.category
    status = 'verified'
    candidateCount = $results.Count
    candidates = $results
} | ConvertTo-Json -Depth 10
