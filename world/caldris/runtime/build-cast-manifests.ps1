param(
    [string]$Atlas = (Join-Path $PSScriptRoot '..\CALDRIS-CAST-AND-FACTIONS.md'),
    [string]$Additional = (Join-Path $PSScriptRoot '..\CALDRIS-ADDITIONAL-CAST.md')
)
$ErrorActionPreference = 'Stop'

function Slug([string]$Value) {
    ($Value.ToLowerInvariant() -replace '[“”]', '' -replace "[’']", '' -replace '[^a-z0-9]+', '-').Trim('-')
}
function Actor([string]$Name, [string]$Summary, [string]$ContainerId, [string]$Visibility = 'public') {
    [ordered]@{
        entityId="actor.caldris.$(Slug $Name)"; name=$Name; expectedRevision=0
        components=@([ordered]@{
            qualifiedTypeId='dnd2024.game.core.world.motive'; expectedRevision=0
            value=[ordered]@{status='active';summary=$Summary;visibility=$Visibility}
        })
        containment=[ordered]@{containerEntityId=$ContainerId;slot='people';expectedRevision=0}
    }
}
function PolityId([string]$Name) {
    switch($Name){
        'Veyr' {'veyr-marches'}
        'Seven Lamps' {'seven-lamps-league'}
        default { Slug $Name }
    }
}
function Manifest([string]$Token,[object[]]$Entities){
    [ordered]@{requestToken=$Token;applicationId='dnd2024';stateSpaceId='dnd2024-main';rootEntityId='world.caldris';entities=$Entities;relationships=@()}
}

$all=[System.Collections.Generic.List[object]]::new()
$atlasText=Get-Content -LiteralPath $Atlas -Raw
$existing=@('Magistrate Elowen Pike','Nessa Quill','Tibb Fallow','Brother Odo Mallow','Merrit Vale','Della Crookshaw','Fenna Dorr','Fara Dint')
$startingMatches=[regex]::Matches($atlasText,'(?ms)^### (?<heading>.+?) — (?<trait>.+?)\r?\n\r?\n(?<body>.+?)(?=\r?\n### |\r?\n## Wider polity anchors)')
foreach($match in $startingMatches){
    $name=$match.Groups['heading'].Value.Trim()
    if($existing -contains $name -or $name -in @('Provost Halwen Drail','Chancellor Sera Vane','Auditor Celyn Marr','Ilyr Vaust')){ continue }
    $trait=$match.Groups['trait'].Value.Trim()
    $body=$match.Groups['body'].Value
    $role=[regex]::Match($body,'(?m)^- \*\*Role:\*\* (?<value>.+?(?:\r?\n  .+?)*)$').Groups['value'].Value -replace '\r?\n\s+',' '
    $want=[regex]::Match($body,'(?m)^- \*\*Immediate want:\*\* (?<value>.+?(?:\r?\n  .+?)*)$').Groups['value'].Value -replace '\r?\n\s+',' '
    $depth=[regex]::Match($body,'(?m)^- \*\*Knows/suspects/hides:\*\* (?<value>.+?(?:\r?\n  .+?)*)$').Groups['value'].Value -replace '\r?\n\s+',' '
    $all.Add((Actor $name "$trait. Role: $role Immediate want: $want Hidden depth: $depth" 'location.caldris.bramblebridge' 'party'))
}

$section=''
foreach($line in Get-Content -LiteralPath $Atlas){
    if($line -eq '## Wider polity anchors — forty-four NPCs'){ $section='wider'; continue }
    if($section -eq 'wider' -and $line -match '^## '){ $section=''; continue }
    if($section -ne 'wider' -or $line -notmatch '^\| (Dunmarrow|Carrowmere|Bellafont|Lornesse|Veyr|Ordelain|Namarra|Tessarane|Selucia|Kethria|Seven Lamps) \|'){ continue }
    $cells=$line.Trim('|').Split('|') | ForEach-Object {$_.Trim()}
    $summary="$($cells[2]). Immediate want: $($cells[3]). Hidden depth: $($cells[4])."
    $all.Add((Actor $cells[1] $summary "location.caldris.$(PolityId $cells[0])" 'party'))
}

$polity=''
foreach($line in Get-Content -LiteralPath $Additional){
    if($line -match '^### (Alderwick|Dunmarrow|Carrowmere|Bellafont|Lornesse|Veyr Marches|Ordelain|Namarra|Tessarane|Selucia|Kethria|Seven Lamps League)$'){ $polity=$Matches[1]; continue }
    if($line -notmatch '^\| \*\*(.+?)\*\* \|'){ continue }
    $cells=$line.Trim('|').Split('|') | ForEach-Object {$_.Trim()}
    $name=$cells[0] -replace '^\*\*|\*\*$',''
    $summary="$($cells[1]) $($cells[2]) $($cells[3])"
    $all.Add((Actor $name $summary "location.caldris.$(Slug $polity)" 'party'))
}

foreach($name in @('Provost Halwen Drail','Chancellor Sera Vane','Auditor Celyn Marr')){
    $escaped=[regex]::Escape($name)
    $match=[regex]::Match($atlasText,"(?ms)^### $escaped — (?<trait>.+?)\r?\n\r?\n(?<body>.+?)(?=\r?\n### |\r?\n## Cast use rules)")
    if(-not $match.Success){throw "Campaign-spine section not found: $name"}
    $summary=($match.Groups['trait'].Value+'. '+$match.Groups['body'].Value -replace '\r?\n',' ' -replace '\s+',' ').Trim()
    $all.Add((Actor $name $summary 'location.caldris.alderwick' 'gm'))
}

if($all.Count -ne 90){throw "Expected 90 new actors; found $($all.Count)."}
if(($all.entityId|Sort-Object -Unique).Count -ne $all.Count){throw 'Duplicate actor IDs were generated.'}
$packages=@(
    @{Name='caldris-live-cast-v3l.json';Value=(Manifest 'ca1d215e00000000000000000000000e' @($all|Select-Object -First 30))},
    @{Name='caldris-live-cast-v3m.json';Value=(Manifest 'ca1d215e00000000000000000000000f' @($all|Select-Object -Skip 30 -First 30))},
    @{Name='caldris-live-cast-v3n.json';Value=(Manifest 'ca1d215e000000000000000000000010' @($all|Select-Object -Skip 60))}
)
foreach($package in $packages){
    $package.Value|ConvertTo-Json -Depth 12|Set-Content -LiteralPath (Join-Path $PSScriptRoot $package.Name) -Encoding utf8
    Write-Output "$($package.Name): $($package.Value.entities.Count) actors"
}
