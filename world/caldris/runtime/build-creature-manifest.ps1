param(
    [string]$WorldBible = (Join-Path $PSScriptRoot '..\CALDRIS-WORLD-BIBLE.md')
)
$ErrorActionPreference = 'Stop'

function Slug([string]$Value) {
    ($Value.ToLowerInvariant() -replace "[’']", '' -replace '[^a-z0-9]+', '-').Trim('-')
}
function Creature([string]$Id, [string]$Name, [string]$Summary, [string]$ContainerId, [string]$Visibility = 'public') {
    [ordered]@{
        entityId=$Id; name=$Name; expectedRevision=0
        components=@([ordered]@{
            qualifiedTypeId='dnd2024.game.core.world.motive'; expectedRevision=0
            value=[ordered]@{ status='active'; summary=$Summary; visibility=$Visibility }
        })
        containment=[ordered]@{ containerEntityId=$ContainerId; slot='creatures'; expectedRevision=0 }
    }
}

$dragonHomes=[ordered]@{
    'Ilyr Vaust, the Brass Archivist'='location.caldris.atlas'
    'Mourenne Under-Mere'='location.caldris.alderwick'
    'Karsh Veyru, the Copper Eater'='location.caldris.kethria'
    'Sable Annek of the Reed Graves'='location.caldris.namarra'
    'Oriselle Cloud-Turner'='location.caldris.lornesse'
    'Tavaros of the Empty Nets'='location.caldris.selucia'
}
$dragonEntities=[System.Collections.Generic.List[object]]::new()
$text=Get-Content -LiteralPath $WorldBible -Raw
foreach($name in $dragonHomes.Keys){
    $escaped=[regex]::Escape($name)
    $match=[regex]::Match($text,"(?ms)^### $escaped\r?\n\r?\n(.+?)(?=\r?\n### |\r?\n## Mythical creature traditions)")
    if(-not $match.Success){ throw "Dragon section not found: $name" }
    $summary=($match.Groups[1].Value -replace '\r?\n',' ' -replace '\s+',' ').Trim()
    $visibility=if($name -like 'Sable Annek*'){'gm'}else{'public'}
    $dragonEntities.Add((Creature "creature.caldris.dragon.$(Slug (($name -split ',')[0]))" $name $summary $dragonHomes[$name] $visibility))
}

$traditionHomes=[ordered]@{
    'Bell fox'='location.caldris.veyr-marches'; 'Lantern heron'='location.caldris.namarra'
    'Mossback'='location.caldris.alderwick'; 'Ash stag'='location.caldris.bellafont'
    'Mere drake'='location.caldris.alderwick'; 'Mirror eel'='location.caldris.namarra'
    'Mourning moth'='location.caldris.ordelain'; 'Roof gobbler'='location.caldris.alderwick'
    'Grey orchard wife'='location.caldris.bellafont'; 'Stone sleeper'='location.caldris.lornesse'
    'Salt widow'='location.caldris.carrowmere'; 'Hearthling'='location.caldris.atlas'
    'Road saint'='location.caldris.atlas'; 'Cloud ram'='location.caldris.lornesse'
    'Reed knight'='location.caldris.namarra'; 'Glass locust'='location.caldris.tessarane'
    'Kethrian embercat'='location.caldris.kethria'; 'Selucian tidehorse'='location.caldris.selucia'
    'Ink crow'='location.caldris.seven-lamps-league'; 'Door mouse'='location.caldris.atlas'
}
$traditionEntities=[System.Collections.Generic.List[object]]::new()
$inTable=$false
foreach($line in Get-Content -LiteralPath $WorldBible){
    if($line -eq '## Mythical creature traditions'){ $inTable=$true; continue }
    if($inTable -and $line -match '^## '){ break }
    if(-not $inTable -or $line -notmatch '^\| ' -or $line -match '^\| (Creature or tradition|---)'){ continue }
    $cells=$line.Trim('|').Split('|') | ForEach-Object {$_.Trim()}
    $name=$cells[0]
    if(-not $traditionHomes.Contains($name)){ throw "Creature home not mapped: $name" }
    $traditionEntities.Add((Creature "creature.caldris.tradition.$(Slug $name)" $name "$($cells[1]) Story use: $($cells[2])" $traditionHomes[$name]))
}
if($dragonEntities.Count -ne 6 -or $traditionEntities.Count -ne 20){ throw "Expected 6 dragons and 20 traditions; found $($dragonEntities.Count) and $($traditionEntities.Count)." }

$manifest=[ordered]@{
    requestToken='ca1d215e00000000000000000000000d'; applicationId='dnd2024'; stateSpaceId='dnd2024-main'
    rootEntityId='world.caldris'; entities=@($dragonEntities)+@($traditionEntities); relationships=@()
}
$path=Join-Path $PSScriptRoot 'caldris-live-creatures-v3k.json'
$manifest | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $path -Encoding utf8
Write-Output "caldris-live-creatures-v3k.json: $($manifest.entities.Count) entities"
