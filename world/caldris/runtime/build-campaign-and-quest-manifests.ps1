param(
    [string]$QuestAtlas = (Join-Path $PSScriptRoot '..\CALDRIS-QUEST-ATLAS.md'),
    [string]$AdditionalQuests = (Join-Path $PSScriptRoot '..\CALDRIS-ADDITIONAL-QUESTS.md')
)
$ErrorActionPreference='Stop'

function Slug([string]$Value){($Value.ToLowerInvariant() -replace "[’']",'' -replace '[^a-z0-9]+','-').Trim('-')}
function Rel([string]$From,[string]$To,[string]$Kind){[ordered]@{fromEntityId=$From;toEntityId=$To;qualifiedKind=$Kind;expectedRevision=0;value=[ordered]@{}}}
function Manifest([string]$Token,[string]$Root,[object[]]$Entities,[object[]]$Relationships){[ordered]@{requestToken=$Token;applicationId='dnd2024';stateSpaceId='dnd2024-main';rootEntityId=$Root;entities=$Entities;relationships=$Relationships}}

$campaign='campaign.caldris.measure-of-mercy'
$arc="$campaign.arc.thirteen-bells"
$chapter="$campaign.chapter.the-thirteenth-bell"
$campaignEntities=@(
    [ordered]@{
        entityId=$arc;name='Volume I — Thirteen Bells';expectedRevision=0
        components=@([ordered]@{qualifiedTypeId='dnd2024.game.core.campaign.arc';expectedRevision=0;value=[ordered]@{
            status='active';title='Volume I — Thirteen Bells';partyStake='Can Bramblebridge gain a fair road without losing the imperfect people and services that keep it alive?';gmContext='Levels 1–2. Toll abuse leads from Fara Dint to Rusk Pettifer and then Provost Halwen Drail. Preserve the value of each victory; the orchard-fungus handoff opens an unrelated Bellafont story.'
        }})
        containment=[ordered]@{containerEntityId=$campaign;slot='arcs';expectedRevision=0}
    },
    [ordered]@{
        entityId=$chapter;name='The Thirteenth Bell';expectedRevision=0
        components=@([ordered]@{qualifiedTypeId='dnd2024.game.core.campaign.chapter';expectedRevision=0;value=[ordered]@{
            status='active';title='The Thirteenth Bell';partyQuestion="Why did Bramblebridge's noon bell ring thirteen times as an empty tax wagon entered the market?";gmContext='Account for wagon, driver, horse, cargo, and bell access. Clues include dry flour under wet canvas, theatre-pulley fibers, a changed whistle, an altered alley map, and barge-signal timing. Failure advances the carriers but leaves clearer tracks.'
        }})
        containment=[ordered]@{containerEntityId=$campaign;slot='chapters';expectedRevision=0}
    }
)
$campaignRelationships=@(
    (Rel $campaign $arc 'dnd2024.game.core.campaign.has-arc'),
    (Rel $campaign $chapter 'dnd2024.game.core.campaign.has-chapter'),
    (Rel $chapter $arc 'dnd2024.game.core.campaign.chapter.in-arc')
)
(Manifest 'ca1d215e000000000000000000000017' $campaign $campaignEntities $campaignRelationships)|ConvertTo-Json -Depth 12|Set-Content -LiteralPath (Join-Path $PSScriptRoot 'caldris-live-campaign-opening-v3u.json') -Encoding utf8

$quests=[System.Collections.Generic.List[object]]::new()
foreach($path in @($QuestAtlas,$AdditionalQuests)){
    $text=Get-Content -LiteralPath $path -Raw
    $matches=[regex]::Matches($text,'(?ms)^### (?<number>Q\d{2}) — (?<title>.+?)\r?\n\r?\n(?<body>.+?)(?=\r?\n### |\r?\n## )')
    foreach($match in $matches){
        $number=$match.Groups['number'].Value;$title=$match.Groups['title'].Value.Trim()
        $body=($match.Groups['body'].Value -replace '\*\*','' -replace '\r?\n',' ' -replace '\s+',' ').Trim()
        if($body.Length -gt 980){$body=$body.Substring(0,977).TrimEnd()+"…"}
        $quests.Add([ordered]@{
            entityId="secret.caldris.quest.$($number.ToLowerInvariant()).$(Slug $title)";name="$number — $title";expectedRevision=0
            components=@(
                [ordered]@{qualifiedTypeId='dnd2024.game.core.world.secret';expectedRevision=0;value=[ordered]@{status='active';summary=$body;provenance='Caldris quest atlas';visibility='gm'}},
                [ordered]@{qualifiedTypeId='dnd2024.game.core.world.knowledge.classification';expectedRevision=0;value=[ordered]@{subjectKind='event';sensitivity='secret'}}
            )
            containment=[ordered]@{containerEntityId='location.caldris.atlas';slot='quest-seeds';expectedRevision=0}
        })
    }
}
if($quests.Count -ne 48){throw "Expected 48 quest packets; found $($quests.Count)."}
$questRelationships=[System.Collections.Generic.List[object]]::new()
foreach($quest in $quests){$questRelationships.Add((Rel $quest.entityId 'world.caldris' 'dnd2024.game.core.world.knowledge.in-world'));$questRelationships.Add((Rel $quest.entityId 'location.caldris.atlas' 'dnd2024.game.core.world.knowledge.about'))}
$packages=@(
    @{Name='caldris-live-quest-seeds-v3v.json';Value=(Manifest 'ca1d215e000000000000000000000018' 'world.caldris' @($quests|Select-Object -First 16) @($questRelationships|Select-Object -First 32))},
    @{Name='caldris-live-quest-seeds-v3w.json';Value=(Manifest 'ca1d215e000000000000000000000019' 'world.caldris' @($quests|Select-Object -Skip 16 -First 16) @($questRelationships|Select-Object -Skip 32 -First 32))},
    @{Name='caldris-live-quest-seeds-v3x.json';Value=(Manifest 'ca1d215e00000000000000000000001a' 'world.caldris' @($quests|Select-Object -Skip 32) @($questRelationships|Select-Object -Skip 64))}
)
foreach($package in $packages){$package.Value|ConvertTo-Json -Depth 12|Set-Content -LiteralPath (Join-Path $PSScriptRoot $package.Name) -Encoding utf8;Write-Output "$($package.Name): $($package.Value.entities.Count) quest packets"}
Write-Output 'caldris-live-campaign-opening-v3u.json: 1 active arc, 1 active chapter'
