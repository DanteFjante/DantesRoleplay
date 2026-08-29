[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../../../..')).Path,
    [string]$ManifestPath = (Join-Path $PSScriptRoot '../fixtures/slice-10a-currency-transform.json')
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
    return ($Value | ConvertTo-Json -Depth 20 -Compress)
}

$root = [IO.Path]::GetFullPath($RepositoryRoot)
$manifestFull = [IO.Path]::GetFullPath($ManifestPath)
$manifest = Get-Content -Raw -LiteralPath $manifestFull | ConvertFrom-Json -Depth 30
Assert-Names $manifest @('format','cohort','sourceRevision','officialSource','license','entries') 'Manifest'
if ($manifest.format -cne 'dnd2024-static-content-transform/v1' -or
    $manifest.cohort -cne 'slice-10a-currency') { throw 'Unexpected currency transform manifest.' }
if ($manifest.officialSource.sourceId -cne 'source.dnd2024.srd-5.2.1' -or
    $manifest.officialSource.locator -cne 'Equipment > Coins > Coin Values (PDF p. 89)') {
    throw 'The official currency source binding drifted.'
}
if ($manifest.license.classification -cne 'CC-BY-4.0' -or
    [string]::IsNullOrWhiteSpace($manifest.license.attribution) -or
    [string]::IsNullOrWhiteSpace($manifest.license.changes)) {
    throw 'CC BY attribution and change indication are required.'
}

$expected = [ordered]@{ cp = 1; sp = 10; ep = 50; gp = 100; pp = 1000 }
$seenIds = @{}; $seenPaths = @{}; $results = @()
foreach ($entry in @($manifest.entries)) {
    Assert-Names $entry @('id','name','denomination','copperValue','sourcePath','sourceSha256','targetPath') $entry.id
    if ($seenIds.ContainsKey($entry.id)) { throw "Duplicate target ID: $($entry.id)" }
    if ($seenPaths.ContainsKey($entry.targetPath)) { throw "Duplicate target path: $($entry.targetPath)" }
    $seenIds[$entry.id] = $true; $seenPaths[$entry.targetPath] = $true
    if (-not $expected.Contains($entry.denomination) -or
        $expected[$entry.denomination] -ne $entry.copperValue) {
        throw "Unexpected denomination/value mapping: $($entry.id)"
    }

    $sourcePath = Resolve-ContainedPath $root $entry.sourcePath
    $targetPath = Resolve-ContainedPath $root $entry.targetPath
    $sourceHash = (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash.ToUpperInvariant()
    if ($sourceHash -cne $entry.sourceSha256) { throw "Source hash drift: $($entry.sourcePath)" }
    if (-not (Test-Path -LiteralPath $targetPath -PathType Leaf)) { throw "Target is missing: $($entry.targetPath)" }

    $source = Get-Content -Raw -LiteralPath $sourcePath | ConvertFrom-Json -Depth 20
    Assert-Names $source @('id','name','components') $entry.sourcePath
    Assert-Names $source.components @('dnd2024.item-definition') "$($entry.id) components"
    $definition = $source.components.'dnd2024.item-definition'
    Assert-Names $definition @('definitionVersion','kind','stackPolicy','massPounds','currency','sourceRef') "$($entry.id) definition"
    if ($source.id -cne $entry.id -or $source.name -cne $entry.name -or
        $definition.definitionVersion -ne 1 -or $definition.kind -cne 'currency' -or
        $definition.stackPolicy -cne 'fungible' -or $definition.massPounds.numerator -ne 1 -or
        $definition.massPounds.denominator -ne 50 -or $definition.currency.denomination -cne $entry.denomination -or
        $definition.currency.copperValue -ne $entry.copperValue -or $definition.currency.coinsPerPound -ne 50 -or
        $definition.sourceRef.sourceId -cne 'source.dnd2024.srd-5.2.1' -or
        $definition.sourceRef.locator -cne 'Equipment > Currency') {
        throw "Archived source meaning drift: $($entry.sourcePath)"
    }

    $generated = [ordered]@{
        id = $entry.id
        name = $entry.name
        components = [ordered]@{
            'dnd2024.item-definition' = [ordered]@{
                definitionVersion = 1
                kind = 'currency'
                stackPolicy = 'fungible'
                massPounds = [ordered]@{ numerator = 1; denominator = 50 }
                currency = [ordered]@{
                    denomination = $entry.denomination
                    copperValue = $entry.copperValue
                    coinsPerPound = 50
                }
                sourceRef = [ordered]@{
                    sourceId = $manifest.officialSource.sourceId
                    locator = $manifest.officialSource.locator
                }
            }
        }
    }
    $target = Get-Content -Raw -LiteralPath $targetPath | ConvertFrom-Json -Depth 20
    if ((Canonical-Json $target) -cne (Canonical-Json $generated)) {
        throw "Deterministic target drift: $($entry.targetPath)"
    }
    $results += [ordered]@{
        id = $entry.id
        targetPath = $entry.targetPath
        targetSha256 = (Get-FileHash -LiteralPath $targetPath -Algorithm SHA256).Hash.ToUpperInvariant()
    }
}

if ($seenIds.Count -ne 5 -or @($expected.Keys | Where-Object { -not ($manifest.entries.denomination -ccontains $_) }).Count -ne 0) {
    throw 'The currency cohort must contain exactly CP, SP, EP, GP, and PP.'
}

[ordered]@{
    format = 'dnd2024-static-content-transform-report/v1'
    cohort = $manifest.cohort
    status = 'verified'
    candidateCount = $results.Count
    candidates = $results
} | ConvertTo-Json -Depth 10
