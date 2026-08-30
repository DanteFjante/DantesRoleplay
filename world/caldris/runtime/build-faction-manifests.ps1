param([string]$Atlas = (Join-Path $PSScriptRoot '..\CALDRIS-CAST-AND-FACTIONS.md'))
$ErrorActionPreference='Stop'

function Slug([string]$Value){($Value.ToLowerInvariant() -replace "[’']",'' -replace '[^a-z0-9]+','-').Trim('-')}
function Rel([string]$From,[string]$To,[string]$Kind){[ordered]@{fromEntityId=$From;toEntityId=$To;qualifiedKind=$Kind;expectedRevision=0;value=[ordered]@{}}}
function Territory([string]$Reach){
    switch -Regex ($Reach){
        '^Alderwick' {'location.caldris.alderwick'}
        '^Bramblebridge' {'location.caldris.bramblebridge'}
        '^Dunmarrow' {'location.caldris.dunmarrow'}
        '^Carrowmere' {'location.caldris.carrowmere'}
        '^Bellafont' {'location.caldris.bellafont'}
        '^Lorn' {'location.caldris.lornesse'}
        '^Veyr' {'location.caldris.veyr-marches'}
        '^Ordelain' {'location.caldris.ordelain'}
        '^Namarra' {'location.caldris.namarra'}
        '^Tessa' {'location.caldris.tessarane'}
        '^Selucia' {'location.caldris.selucia'}
        '^Kethria' {'location.caldris.kethria'}
        '^Seven Lamps' {'location.caldris.seven-lamps-league'}
        default {'location.caldris.atlas'}
    }
}
function Manifest([string]$Token,[object[]]$Entities,[object[]]$Relationships){[ordered]@{requestToken=$Token;applicationId='dnd2024';stateSpaceId='dnd2024-main';rootEntityId='world.caldris';entities=$Entities;relationships=$Relationships}}

$existing=@('Alderwick Crown Council','Honest Weights Guild','Bramblebridge Watch')
$entities=[System.Collections.Generic.List[object]]::new();$relationships=[System.Collections.Generic.List[object]]::new()
$section=$false
foreach($line in Get-Content -LiteralPath $Atlas){
    if($line -eq '## Faction atlas'){$section=$true;continue}
    if($section -and $line -match '^## '){break}
    if(-not $section -or $line -notmatch '^\| ' -or $line -match '^\| (Faction|---)'){continue}
    $cells=$line.Trim('|').Split('|')|ForEach-Object{$_.Trim()}
    if($existing -contains $cells[0]){continue}
    $id="faction.caldris.$(Slug $cells[0])"
    $entities.Add([ordered]@{
        entityId=$id;name=$cells[0];expectedRevision=0
        components=@([ordered]@{qualifiedTypeId='dnd2024.game.core.world.faction';expectedRevision=0;value=[ordered]@{
            status='active';summary="$($cells[0]) operates across $($cells[1]). $($cells[2])";visibility='public'
            goals=@($cells[2]);methods=@($cells[3]);assets=@("Reach: $($cells[1])");agenda=[ordered]@{state='ready';summary=$cells[4]}
        }})
        containment=[ordered]@{containerEntityId=(Territory $cells[1]);slot='factions';expectedRevision=0}
    })
    $relationships.Add((Rel $id 'world.caldris' 'dnd2024.game.core.world.faction.in-world'))
    $relationships.Add((Rel $id (Territory $cells[1]) 'dnd2024.game.core.world.faction.territory-controls'))
}
if($entities.Count -ne 31){throw "Expected 31 new factions; found $($entities.Count)."}
$packages=@(
    @{Name='caldris-live-factions-v3o.json';Value=(Manifest 'ca1d215e000000000000000000000011' @($entities|Select-Object -First 16) @($relationships|Select-Object -First 32))},
    @{Name='caldris-live-factions-v3p.json';Value=(Manifest 'ca1d215e000000000000000000000012' @($entities|Select-Object -Skip 16 -First 5) @($relationships|Select-Object -Skip 32 -First 10))},
    @{Name='caldris-live-factions-v3q.json';Value=(Manifest 'ca1d215e000000000000000000000013' @($entities|Select-Object -Skip 21 -First 5) @($relationships|Select-Object -Skip 42 -First 10))},
    @{Name='caldris-live-factions-v3r.json';Value=(Manifest 'ca1d215e000000000000000000000014' @($entities|Select-Object -Skip 26) @($relationships|Select-Object -Skip 52))}
)
foreach($package in $packages){$package.Value|ConvertTo-Json -Depth 12|Set-Content -LiteralPath (Join-Path $PSScriptRoot $package.Name) -Encoding utf8;Write-Output "$($package.Name): $($package.Value.entities.Count) factions, $($package.Value.relationships.Count) relationships"}
