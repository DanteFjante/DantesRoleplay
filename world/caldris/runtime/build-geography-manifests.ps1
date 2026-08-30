param(
    [string]$WorldBible = (Join-Path $PSScriptRoot '..\CALDRIS-WORLD-BIBLE.md'),
    [string]$Gazetteer = (Join-Path $PSScriptRoot '..\CALDRIS-GAZETTEER.md'),
    [string]$Expanded = (Join-Path $PSScriptRoot '..\CALDRIS-EXPANDED-LOCATIONS.md')
)

$ErrorActionPreference = 'Stop'

function Slug([string]$Value) {
    $normalized = $Value.ToLowerInvariant() -replace "[’']", '' -replace '[^a-z0-9]+', '-'
    return $normalized.Trim('-')
}

function LocationComponent([string]$Kind, [string]$Summary) {
    return [ordered]@{
        qualifiedTypeId = 'dnd2024.game.core.world.location'
        expectedRevision = 0
        value = [ordered]@{ kind = $Kind; status = 'active'; summary = $Summary; visibility = 'public' }
    }
}

function NewLocation([string]$Id, [string]$Name, [string]$Kind, [string]$Summary, [string]$Parent, [string]$Slot) {
    return [ordered]@{
        entityId = $Id
        name = $Name
        expectedRevision = 0
        components = @((LocationComponent $Kind $Summary))
        containment = [ordered]@{ containerEntityId = $Parent; slot = $Slot; expectedRevision = 0 }
    }
}

function Manifest([string]$Token, [object[]]$Entities) {
    return [ordered]@{
        requestToken = $Token
        applicationId = 'dnd2024'
        stateSpaceId = 'dnd2024-main'
        rootEntityId = 'world.caldris'
        entities = $Entities
        relationships = @()
    }
}

$polityRows = Get-Content -LiteralPath $WorldBible | Where-Object { $_ -match '^\| (Eredane|Solasca) \|' }
$polities = foreach ($row in $polityRows) {
    $cells = $row.Trim('|').Split('|') | ForEach-Object { $_.Trim() }
    [pscustomobject]@{
        Continent = $cells[0]
        Name = $cells[1]
        Form = $cells[2]
        Capital = $cells[3]
        OtherCities = $cells[4].Split(',') | ForEach-Object { $_.Trim() }
        Pressure = $cells[5]
    }
}

if ($polities.Count -ne 12) { throw "Expected 12 polity rows, found $($polities.Count)." }
$polityNames = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$polities.Name | ForEach-Object { [void]$polityNames.Add($_) }

$polityEntities = foreach ($polity in $polities) {
    $slug = Slug $polity.Name
    $summary = "$($polity.Name) is a $($polity.Form.ToLowerInvariant()) of $($polity.Continent). Its present pressure: $($polity.Pressure)"
    $entity = NewLocation "location.caldris.$slug" $polity.Name 'region' $summary "location.caldris.$((Slug $polity.Continent))" 'region'
    if ($polity.Name -eq 'Alderwick') {
        $entity.components += [ordered]@{
            qualifiedTypeId = 'dnd2024.game.core.world.map.anchor'
            expectedRevision = 0
            value = [ordered]@{ x = 510; y = 500 }
        }
        $entity.components += [ordered]@{
            qualifiedTypeId = 'dnd2024.game.core.world.map.visual'
            expectedRevision = 0
            value = [ordered]@{
                status = 'active'
                variants = [ordered]@{
                    player = [ordered]@{ assetKey = 'caldris.region.eredane.player'; alt = 'A regional map of Alderwick and the Bramblebridge country.' }
                    dm = [ordered]@{ assetKey = 'caldris.region.eredane.dm'; alt = 'The Game Master map of Alderwick and the Bramblebridge country.' }
                }
            }
        }
    }
    $entity
}

$polityEntities += @(
    [ordered]@{
        entityId = 'location.caldris.bramblebridge'; name = 'Bramblebridge'; expectedRevision = 1; components = @()
        containment = [ordered]@{ containerEntityId = 'location.caldris.alderwick'; slot = 'settlement'; expectedRevision = 1 }
    },
    [ordered]@{
        entityId = 'location.caldris.candlefen'; name = 'Candlefen'; expectedRevision = 1; components = @()
        containment = [ordered]@{ containerEntityId = 'location.caldris.alderwick'; slot = 'settlement'; expectedRevision = 1 }
    },
    [ordered]@{
        entityId = 'location.caldris.wrens-hollow'; name = "Wren's Hollow"; expectedRevision = 1; components = @()
        containment = [ordered]@{ containerEntityId = 'location.caldris.alderwick'; slot = 'settlement'; expectedRevision = 1 }
    }
)

$gazetteerLines = Get-Content -LiteralPath $Gazetteer
$cities = [System.Collections.Generic.List[object]]::new()
$currentPolity = $null
for ($i = 0; $i -lt $gazetteerLines.Count; $i++) {
    $line = $gazetteerLines[$i]
    if ($line -match '^### (.+)$') {
        $candidate = $Matches[1].Trim()
        $currentPolity = if ($polityNames.Contains($candidate)) { $candidate } else { $null }
        continue
    }
    if ($currentPolity -and $line -match '^#### (.+)$') {
        $name = ($Matches[1] -split ' — ')[0].Trim()
        $parts = [System.Collections.Generic.List[string]]::new()
        $j = $i + 1
        while ($j -lt $gazetteerLines.Count) {
            $next = $gazetteerLines[$j]
            if ($next -match '^#{1,4} ' -or $next -match '^- \*\*' -or $next -match '^\|') { break }
            if (-not [string]::IsNullOrWhiteSpace($next)) { $parts.Add($next.Trim()) }
            $j++
        }
        $summary = ($parts -join ' ')
        if (-not $summary) { throw "No summary found for city $name." }
        $cities.Add([pscustomobject]@{ Polity = $currentPolity; Name = $name; Summary = $summary })
    }
}
if ($cities.Count -ne 36) { throw "Expected 36 primary cities, found $($cities.Count)." }

$cityEntities = foreach ($city in $cities) {
    $politySlug = Slug $city.Polity
    $citySlug = Slug $city.Name
    NewLocation "location.caldris.$politySlug.$citySlug" $city.Name 'settlement' $city.Summary "location.caldris.$politySlug" 'settlement'
}

$expandedLines = Get-Content -LiteralPath $Expanded
$secondRing = [System.Collections.Generic.List[object]]::new()
$currentPolity = $null
foreach ($line in $expandedLines) {
    if ($line -match '^### (.+)$') {
        $candidate = $Matches[1].Trim()
        $currentPolity = if ($polityNames.Contains($candidate)) { $candidate } else { $null }
        continue
    }
    if ($currentPolity -and $line -match '^\| \*\*(.+?)\*\* \|') {
        $cells = $line.Trim('|').Split('|') | ForEach-Object { $_.Trim() }
        $name = $cells[0] -replace '^\*\*|\*\*$', ''
        $summary = "$($cells[1]) Present pressure: $($cells[2])"
        $secondRing.Add([pscustomobject]@{ Polity = $currentPolity; Name = $name; Summary = $summary })
    }
}
if ($secondRing.Count -ne 48) { throw "Expected 48 second-ring places, found $($secondRing.Count)." }

$secondRingEntities = foreach ($place in $secondRing) {
    $politySlug = Slug $place.Polity
    $placeSlug = Slug $place.Name
    NewLocation "location.caldris.$politySlug.$placeSlug" $place.Name 'site' $place.Summary "location.caldris.$politySlug" 'site'
}

$packages = @(
    @{ Name = 'caldris-live-geography-v3a.json'; Value = (Manifest 'ca1d215e000000000000000000000003' $polityEntities) },
    @{ Name = 'caldris-live-geography-v3b.json'; Value = (Manifest 'ca1d215e000000000000000000000004' @($cityEntities | Select-Object -First 18)) },
    @{ Name = 'caldris-live-geography-v3c.json'; Value = (Manifest 'ca1d215e000000000000000000000005' @($cityEntities | Select-Object -Skip 18)) },
    @{ Name = 'caldris-live-geography-v3d.json'; Value = (Manifest 'ca1d215e000000000000000000000006' @($secondRingEntities | Select-Object -First 24)) },
    @{ Name = 'caldris-live-geography-v3e.json'; Value = (Manifest 'ca1d215e000000000000000000000007' @($secondRingEntities | Select-Object -Skip 24)) }
)

foreach ($package in $packages) {
    $path = Join-Path $PSScriptRoot $package.Name
    $package.Value | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $path -Encoding utf8
    Write-Output "$($package.Name): $($package.Value.entities.Count) entities"
}
