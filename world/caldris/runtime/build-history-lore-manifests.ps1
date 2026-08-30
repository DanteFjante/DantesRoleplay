param(
    [string]$Atlas = (Join-Path $PSScriptRoot '..\CALDRIS-HISTORY-AND-LORE-ATLAS.md'),
    [string]$WorldBible = (Join-Path $PSScriptRoot '..\CALDRIS-WORLD-BIBLE.md')
)
$ErrorActionPreference = 'Stop'

function Slug([string]$Value) {
    ($Value.ToLowerInvariant() -replace "[’']", '' -replace '[^a-z0-9]+', '-').Trim('-')
}
function Manifest([string]$Token, [object[]]$Entities, [object[]]$Relationships) {
    [ordered]@{ requestToken=$Token; applicationId='dnd2024'; stateSpaceId='dnd2024-main'; rootEntityId='world.caldris'; entities=$Entities; relationships=$Relationships }
}
function Relationship([string]$From, [string]$To, [string]$Kind) {
    [ordered]@{ fromEntityId=$From; toEntityId=$To; qualifiedKind=$Kind; expectedRevision=0; value=[ordered]@{} }
}
function KnowledgeEntity([string]$Id, [string]$Name, [string]$Summary, [string]$Type, [string]$Visibility, [string]$Sensitivity, [string]$Subject) {
    [ordered]@{
        entityId=$Id; name=$Name; expectedRevision=0
        components=@(
            [ordered]@{ qualifiedTypeId=$Type; expectedRevision=0; value=[ordered]@{ status='active'; summary=$Summary; provenance='Caldris history and lore atlas'; visibility=$Visibility } },
            [ordered]@{ qualifiedTypeId='dnd2024.game.core.world.knowledge.classification'; expectedRevision=0; value=[ordered]@{ subjectKind='event'; sensitivity=$Sensitivity } }
        )
        containment=[ordered]@{ containerEntityId=$Subject; slot='knowledge'; expectedRevision=0 }
    }
}

$lines=Get-Content -LiteralPath $Atlas
$section=''
$incidents=[System.Collections.Generic.List[object]]::new()
$lores=[System.Collections.Generic.List[object]]::new()
foreach($line in $lines){
    if($line -eq '## Twenty-four historical incidents'){ $section='history'; continue }
    if($line -eq '## Twenty-four living lore, customs, and disputed beliefs'){ $section='lore'; continue }
    if($line -match '^## '){ $section=''; continue }
    if($line -notmatch '^\| (Alderwick|Dunmarrow|Carrowmere|Bellafont|Lornesse|Veyr Marches|Ordelain|Namarra|Tessarane|Selucia|Kethria|Seven Lamps League) \|'){ continue }
    $cells=$line.Trim('|').Split('|') | ForEach-Object {$_.Trim()}
    $title=$cells[1] -replace '^\*\*|\*\*$',''
    if($section -eq 'history'){
        $incidents.Add([pscustomobject]@{Polity=$cells[0];Title=$title;Record=$cells[2];Secret=$cells[3];Consequence=$cells[4]})
    } elseif($section -eq 'lore'){
        $lores.Add([pscustomobject]@{Polity=$cells[0];Title=$title;Practice=$cells[2];Dispute=$cells[3];Use=$cells[4]})
    }
}
if($incidents.Count -ne 24 -or $lores.Count -ne 24){ throw "Expected 24 incidents and 24 lore rows; found $($incidents.Count) and $($lores.Count)." }

$eraMinutes=@{
    'First Footprints'=-1000000000; 'Hearths and Standing Stones'=-950000000; 'River Thrones'=-850000000
    'Lantern Concord'=-700000000; 'Sundering'=-400000000; 'Crownmaking'=-250000000
    'Three Banner War'=-45000000; 'Long Reprieve'=-20000000
}
$chronologyEntities=[System.Collections.Generic.List[object]]::new()
$chronologyRelationships=[System.Collections.Generic.List[object]]::new()
foreach($incident in $incidents){
    $parts=$incident.Title -split ' — ',2; $era=$parts[0]; $eventTitle=$parts[1]
    $id="chronology.caldris.incident.$((Slug $incident.Polity)).$((Slug $eventTitle))"
    $chronologyEntities.Add([ordered]@{
        entityId=$id; name=$eventTitle; expectedRevision=0
        components=@([ordered]@{qualifiedTypeId='dnd2024.game.core.world.chronology';expectedRevision=0;value=[ordered]@{
            status='active';title=$eventTitle;summary="$($incident.Record) Present consequence: $($incident.Consequence)";calendarId='caldris-common-era';occurredAtMinute=[long]$eraMinutes[$era];precision='era';dateLabel=$era;visibility='public'
        }})
        containment=[ordered]@{containerEntityId="location.caldris.$((Slug $incident.Polity))";slot='history';expectedRevision=0}
    })
    $chronologyRelationships.Add((Relationship $id 'world.caldris' 'dnd2024.game.core.world.chronology.in-world'))
    $chronologyRelationships.Add((Relationship $id "location.caldris.$((Slug $incident.Polity))" 'dnd2024.game.core.world.chronology.about'))
}

$secretEntities=[System.Collections.Generic.List[object]]::new(); $secretRelationships=[System.Collections.Generic.List[object]]::new()
foreach($incident in $incidents){
    $eventTitle=($incident.Title -split ' — ',2)[1]; $id="secret.caldris.history.$((Slug $incident.Polity)).$((Slug $eventTitle))"
    $secretEntities.Add((KnowledgeEntity $id "$eventTitle — concealed layer" $incident.Secret 'dnd2024.game.core.world.secret' 'gm' 'secret' "location.caldris.$((Slug $incident.Polity))"))
    $secretRelationships.Add((Relationship $id 'world.caldris' 'dnd2024.game.core.world.knowledge.in-world'))
    $secretRelationships.Add((Relationship $id "location.caldris.$((Slug $incident.Polity))" 'dnd2024.game.core.world.knowledge.about'))
}

$loreEntities=[System.Collections.Generic.List[object]]::new(); $loreRelationships=[System.Collections.Generic.List[object]]::new()
foreach($lore in $lores){
    $id="fact.caldris.custom.$((Slug $lore.Polity)).$((Slug $lore.Title))"
    $loreEntities.Add((KnowledgeEntity $id $lore.Title "$($lore.Practice) Disagreement: $($lore.Dispute) Story use: $($lore.Use)" 'dnd2024.game.core.world.fact' 'public' 'open' "location.caldris.$((Slug $lore.Polity))"))
    $loreRelationships.Add((Relationship $id 'world.caldris' 'dnd2024.game.core.world.knowledge.in-world'))
    $loreRelationships.Add((Relationship $id "location.caldris.$((Slug $lore.Polity))" 'dnd2024.game.core.world.knowledge.about'))
}

$packages=@(
    @{Name='caldris-live-history-v3f.json';Value=(Manifest 'ca1d215e000000000000000000000008' $chronologyEntities $chronologyRelationships)},
    @{Name='caldris-live-history-secrets-v3g.json';Value=(Manifest 'ca1d215e000000000000000000000009' @($secretEntities | Select-Object -First 12) @($secretRelationships | Select-Object -First 24))},
    @{Name='caldris-live-history-secrets-v3h.json';Value=(Manifest 'ca1d215e00000000000000000000000a' @($secretEntities | Select-Object -Skip 12) @($secretRelationships | Select-Object -Skip 24))},
    @{Name='caldris-live-lore-v3i.json';Value=(Manifest 'ca1d215e00000000000000000000000b' @($loreEntities | Select-Object -First 12) @($loreRelationships | Select-Object -First 24))},
    @{Name='caldris-live-lore-v3j.json';Value=(Manifest 'ca1d215e00000000000000000000000c' @($loreEntities | Select-Object -Skip 12) @($loreRelationships | Select-Object -Skip 24))}
)
foreach($package in $packages){
    $path=Join-Path $PSScriptRoot $package.Name
    $package.Value | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $path -Encoding utf8
    Write-Output "$($package.Name): $($package.Value.entities.Count) entities, $($package.Value.relationships.Count) relationships"
}
