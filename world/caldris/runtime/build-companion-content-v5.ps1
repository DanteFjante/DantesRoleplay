$ErrorActionPreference = 'Stop'

$runtime = $PSScriptRoot
$caldris = Split-Path $runtime -Parent
$worldBible = Get-Content -LiteralPath (Join-Path $caldris 'CALDRIS-WORLD-BIBLE.md')
$gazetteer = Get-Content -LiteralPath (Join-Path $caldris 'CALDRIS-GAZETTEER.md')
$tapestry = Get-Content -LiteralPath (Join-Path $caldris 'CALDRIS-CAMPAIGN-TAPESTRY.md')

function Get-Slug([string]$Text) {
    $normal = $Text.Normalize([Text.NormalizationForm]::FormD).ToLowerInvariant()
    $ascii = -join ($normal.ToCharArray() | Where-Object { [Globalization.CharUnicodeInfo]::GetUnicodeCategory($_) -ne 'NonSpacingMark' })
    return (($ascii -replace "['’]", '') -replace '[^a-z0-9]+', '-').Trim('-')
}

function Get-Summary([object[]]$Parts) {
    $text = (($Parts -join ' ') -replace '\*\*', '' -replace '`', '' -replace '\s+', ' ').Trim()
    if ($text.Length -le 995) { return $text }
    return $text.Substring(0, 994).TrimEnd() + '…'
}

$entities = [Collections.Generic.List[object]]::new()
$relationships = [Collections.Generic.List[object]]::new()

function Add-Location([string]$Id, [string]$Name, [string]$Summary, [string]$Container, [string]$Kind = 'site') {
    $script:entities.Add([ordered]@{
        entityId = $Id; name = $Name; expectedRevision = 0
        components = @([ordered]@{
            qualifiedTypeId = 'dnd2024.game.core.world.location'; expectedRevision = 0
            value = [ordered]@{ kind = $Kind; status = 'active'; summary = (Get-Summary @($Summary)); visibility = 'public' }
        })
        containment = [ordered]@{ containerEntityId = $Container; slot = 'location'; expectedRevision = 0 }
    })
}

function Add-Knowledge([string]$Kind, [string]$Id, [string]$Name, [string]$Summary, [string]$Target, [string]$Container, [string]$SubjectKind, [string]$Provenance) {
    $visibility = if ($Kind -eq 'secret') { 'gm' } else { 'public' }
    $sensitivity = if ($Kind -eq 'secret') { 'secret' } else { 'open' }
    $script:entities.Add([ordered]@{
        entityId = $Id; name = $Name; expectedRevision = 0
        components = @(
            [ordered]@{
                qualifiedTypeId = "dnd2024.game.core.world.$Kind"; expectedRevision = 0
                value = [ordered]@{ status = 'active'; summary = (Get-Summary @($Summary)); provenance = $Provenance; visibility = $visibility }
            },
            [ordered]@{
                qualifiedTypeId = 'dnd2024.game.core.world.knowledge.classification'; expectedRevision = 0
                value = [ordered]@{ subjectKind = $SubjectKind; sensitivity = $sensitivity }
            }
        )
        containment = [ordered]@{ containerEntityId = $Container; slot = 'knowledge'; expectedRevision = 0 }
    })
    $script:relationships.Add([ordered]@{ fromEntityId = $Id; toEntityId = 'world.caldris'; qualifiedKind = 'dnd2024.game.core.world.knowledge.in-world'; expectedRevision = 0; value = @{} })
    $script:relationships.Add([ordered]@{ fromEntityId = $Id; toEntityId = $Target; qualifiedKind = 'dnd2024.game.core.world.knowledge.about'; expectedRevision = 0; value = @{} })
}

function Write-Manifest([string]$FileName, [int]$Index, [object[]]$BatchEntities, [object[]]$BatchRelationships) {
    $token = 'ca1d215e5' + $Index.ToString('x').PadLeft(23, '0')
    $manifest = [ordered]@{
        requestToken = $token; applicationId = 'dnd2024'; stateSpaceId = 'dnd2024-main'; rootEntityId = 'world.caldris'
        entities = @($BatchEntities); relationships = @($BatchRelationships)
    }
    $path = Join-Path $runtime $FileName
    $manifest | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $path -Encoding utf8
    return $path
}

function Write-KnowledgeBatches([string]$Prefix, [int]$StartIndex, [object[]]$KnowledgeEntities, [object[]]$KnowledgeRelationships) {
    $paths = @()
    for ($offset = 0; $offset -lt $KnowledgeEntities.Count; $offset += 18) {
        $end = [Math]::Min($offset + 17, $KnowledgeEntities.Count - 1)
        $batch = @($KnowledgeEntities[$offset..$end])
        $ids = @($batch.entityId)
        $links = @($KnowledgeRelationships | Where-Object { $_.fromEntityId -in $ids })
        $number = [int]($offset / 18) + 1
        $paths += Write-Manifest "$Prefix-$($number.ToString('00')).json" ($StartIndex + $number - 1) $batch $links
    }
    return $paths
}

# Forty missing playable and first-ring places.
$inBrambleSites = $false
$inAdditional = $false
$placeContainers = @{
    'Alderwick'='location.caldris.alderwick'; 'Dunmarrow'='location.caldris.dunmarrow'; 'Carrowmere'='location.caldris.carrowmere'
    'Bellafont'='location.caldris.bellafont'; 'Lornesse'='location.caldris.lornesse'; 'Veyr'='location.caldris.veyr-marches'
    'Ordelain'='location.caldris.ordelain'; 'Namarra'='location.caldris.namarra'; 'Tessarane'='location.caldris.tessarane'
    'Selucia'='location.caldris.selucia'; 'Kethria'='location.caldris.kethria'; 'League'='location.caldris.seven-lamps-league'
    'Lantern Sea'='location.caldris.atlas'; 'central Eredane'='location.caldris.eredane'; 'eastern Eredane'='location.caldris.eredane'
}
foreach ($line in $gazetteer) {
    if ($line -eq '### Bramblebridge playable sites') { $inBrambleSites = $true; continue }
    if ($inBrambleSites -and $line -match '^### ') { $inBrambleSites = $false }
    if ($line -eq '## Additional named places') { $inAdditional = $true; continue }
    if ($inAdditional -and $line -match '^## ' -and $line -ne '## Additional named places') { $inAdditional = $false }
    if ($inBrambleSites -and $line -match '^\| ([^|-][^|]+?) \| ([^|]+?) \| ([^|]+?) \|$') {
        $name=$matches[1].Trim(); if ($name -in @('Place','The Gilded Kettle','North Bell Tower')) { continue }
        Add-Location "location.caldris.bramblebridge.$(Get-Slug $name)" $name "Function and texture: $($matches[2].Trim()) Current pressure: $($matches[3].Trim())" 'location.caldris.bramblebridge'
    }
    if ($inAdditional -and $line -match '^\| ([^|-][^|]+?) \| ([^|]+?) \| ([^|]+?) \| ([^|]+?) \|$') {
        $name=$matches[1].Trim(); $region=$matches[2].Trim(); $type=$matches[3].Trim(); $identity=$matches[4].Trim()
        if ($name -eq 'Place') { continue }
        $container=$placeContainers[$region]; if (-not $container) { throw "No container for $region" }
        Add-Location "location.caldris.$(Get-Slug $region).$(Get-Slug $name)" $name "$type in $region. $identity" $container
    }
}
$locationEntities = @($entities); $locationRelationships = @($relationships)
$manifestPaths = @((Write-Manifest 'caldris-companion-content-v5-01-locations.json' 1 $locationEntities $locationRelationships))
$entities.Clear(); $relationships.Clear()

# Public World reference: geography, waterways, months, eras, polity dossiers, faith, magic, and tensions.
$polityTargets = @{
    'Alderwick'='location.caldris.alderwick'; 'Dunmarrow'='location.caldris.dunmarrow'; 'Carrowmere'='location.caldris.carrowmere'
    'Bellafont'='location.caldris.bellafont'; 'Lornesse'='location.caldris.lornesse'; 'Veyr Marches'='location.caldris.veyr-marches'
    'Ordelain'='location.caldris.ordelain'; 'Namarra'='location.caldris.namarra'; 'Tessarane'='location.caldris.tessarane'
    'Selucia'='location.caldris.selucia'; 'Kethria'='location.caldris.kethria'; 'Seven Lamps League'='location.caldris.seven-lamps-league'
}

for ($i=0; $i -lt $worldBible.Count; $i++) {
    if ($i -ge 194 -and $i -lt 364 -and $worldBible[$i] -match '^### (.+?) — (.+)$') {
        $name=$matches[1]; $subtitle=$matches[2]; $parts=@("$name — $subtitle.")
        for ($j=$i+1; $j -lt $worldBible.Count -and $worldBible[$j] -notmatch '^### '; $j++) { if ($worldBible[$j] -match '^- \*\*(.+?):\*\* (.+)$') { $parts += "$($matches[1]): $($matches[2])" } }
        $target=$polityTargets[$name]; Add-Knowledge 'fact' "fact.caldris.dossier.$(Get-Slug $name)" "Polity dossier — $name" (Get-Summary $parts) $target $target 'state' 'Caldris world bible — polity dossiers'
    }
}

$tableMode=''
foreach ($line in $worldBible) {
    if ($line -eq '### Waters and crossings') { $tableMode='waters'; continue }
    if ($line -eq '## Calendar and everyday time') { $tableMode='months'; continue }
    if ($line -eq '## The seven eras') { $tableMode=''; continue }
    if ($line -eq '## Faith and religious life') { $tableMode='faith'; continue }
    if ($line -eq '## Low-magic institutions and impact audit') { $tableMode=''; continue }
    if ($line -eq '### Why ordinary systems remain ordinary') { $tableMode='magic'; continue }
    if ($line -match '^## ' -or ($line -match '^### ' -and $line -ne '### Why ordinary systems remain ordinary')) { if ($tableMode -notin @('months')) { $tableMode='' } }
    if ($line -match '^\| ([^|-][^|]+?) \| ([^|]+?) \|$') {
        $name=$matches[1].Trim(); $detail=$matches[2].Trim()
        if ($name -in @('Feature','Month','Magical possibility')) { continue }
        if ($tableMode -eq 'waters') { Add-Knowledge 'fact' "fact.caldris.water.$(Get-Slug $name)" $name $detail 'world.caldris' 'location.caldris.atlas' 'location' 'Caldris world bible — waters and crossings' }
        elseif ($tableMode -eq 'months') { Add-Knowledge 'fact' "fact.caldris.month.$(Get-Slug $name)" "Month — $name" $detail 'world.caldris' 'location.caldris.atlas' 'event' 'Caldris world bible — common calendar' }
    }
    elseif ($line -match '^\| ([^|-][^|]+?) \| ([^|]+?) \| ([^|]+?) \|$') {
        $name=$matches[1].Trim(); $concerns=$matches[2].Trim(); $practice=$matches[3].Trim()
        if ($name -eq 'Lamp') { continue }
        if ($tableMode -eq 'faith') { Add-Knowledge 'fact' "fact.caldris.faith.$(Get-Slug $name)" $name "Concerns: $concerns. Everyday practice: $practice." 'world.caldris' 'location.caldris.atlas' 'rule' 'Caldris world bible — Nine Lamps Covenant' }
        elseif ($tableMode -eq 'magic') { Add-Knowledge 'fact' "fact.caldris.magic-impact.$(Get-Slug $name)" "Low-magic impact — $name" $practice 'world.caldris' 'location.caldris.atlas' 'rule' 'Caldris world bible — low-magic impact audit' }
    }
}

for ($i=0; $i -lt $worldBible.Count; $i++) {
    if ($i -ge 96 -and $i -lt 177 -and $worldBible[$i] -match '^### ([IVX]+)\. (.+?) — (.+)$') {
        $era=$matches[2]; $date=$matches[3]; $parts=@("$date.")
        for ($j=$i+1; $j -lt $worldBible.Count -and $worldBible[$j] -notmatch '^### '; $j++) { if ($worldBible[$j].Trim()) { $parts += $worldBible[$j].Trim() } }
        Add-Knowledge 'fact' "fact.caldris.era.$(Get-Slug $era)" "Era — $era" (Get-Summary $parts) 'world.caldris' 'location.caldris.atlas' 'event' 'Caldris world bible — seven eras'
    }
    if ($i -ge 484 -and $i -lt 500 -and $worldBible[$i] -match '^\d+\. (.+)$') {
        $detail=$matches[1]; Add-Knowledge 'fact' "fact.caldris.tension.$(Get-Slug (($detail -split '[.;]')[0]))" 'Present tension' $detail 'world.caldris' 'location.caldris.atlas' 'state' 'Caldris world bible — current tensions'
    }
}

$distribution = @(
    @('village','Village magic','A village may know a healer, diviner, hedge practitioner, retired adventurer, or unusually gifted priest, but reliable spellcasting is never assumed.'),
    @('town','Town magic','A large town may have one to three known practitioners with narrow reputations and competing obligations.'),
    @('city','City magic','A major city may contain several practitioners and one or two institutions, still far too few to replace ordinary medicine, transport, craft, policing, or agriculture.'),
    @('court','Court magic','Courts value magical capability but distrust dependence on a single exceptional person; corroboration, patronage, and political obligation remain decisive.')
)
foreach($row in $distribution){ Add-Knowledge 'fact' "fact.caldris.magic-distribution.$($row[0])" $row[1] $row[2] 'world.caldris' 'location.caldris.atlas' 'quantity' 'Caldris world bible — practitioner distribution' }

$worldKnowledgeEntities=@($entities); $worldKnowledgeRelationships=@($relationships)
$manifestPaths += Write-KnowledgeBatches 'caldris-companion-content-v5-02-world-reference' 20 $worldKnowledgeEntities $worldKnowledgeRelationships
$entities.Clear(); $relationships.Clear()

# GM campaign reference extracted from the tapestry.
for ($i=0; $i -lt $tapestry.Count; $i++) {
    $line=$tapestry[$i]
    if ($i -ge 24 -and $i -lt 84 -and $line -match '^### (Hearth|Crown|Road|Deep history) — (.+)$') {
        $name=$matches[1]; $parts=@($line.TrimStart('# '))
        for($j=$i+1;$j -lt $tapestry.Count -and $tapestry[$j] -notmatch '^### ';$j++){if($tapestry[$j].Trim()){ $parts += $tapestry[$j].Trim() }}
        Add-Knowledge 'secret' "secret.caldris.campaign.thread.$(Get-Slug $name)" "Campaign thread — $name" (Get-Summary $parts) 'location.caldris.atlas' 'location.caldris.atlas' 'intention' 'Caldris campaign tapestry — enduring threads'
    }
    if ($i -ge 84 -and $i -lt 176 -and $line -match '^### (Volume [IVX]+) — (.+)$') {
        $volume=$matches[1]; $title=$matches[2]; $parts=@("$volume — $title")
        for($j=$i+1;$j -lt $tapestry.Count -and $tapestry[$j] -notmatch '^### ';$j++){if($tapestry[$j].Trim()){ $parts += $tapestry[$j].Trim() }}
        Add-Knowledge 'secret' "secret.caldris.campaign.volume.$(Get-Slug $volume)" "$volume — $title" (Get-Summary $parts) 'location.caldris.atlas' 'location.caldris.atlas' 'intention' 'Caldris campaign tapestry — six-volume shape'
    }
    if ($i -ge 176 -and $i -lt 238 -and $line -match '^### Ladder ([A-D]) — (.+)$') {
        $letter=$matches[1]; $title=$matches[2]; $parts=@("Ladder $letter — $title")
        for($j=$i+1;$j -lt $tapestry.Count -and $tapestry[$j] -notmatch '^### ';$j++){if($tapestry[$j].Trim()){ $parts += $tapestry[$j].Trim() }}
        Add-Knowledge 'secret' "secret.caldris.campaign.ladder.$($letter.ToLowerInvariant())" "Boss ladder — $title" (Get-Summary $parts) 'location.caldris.atlas' 'location.caldris.atlas' 'relationship' 'Caldris campaign tapestry — apparent-boss ladders'
    }
}

$mode=''
foreach($line in $tapestry){
    if($line -eq '## Independent adventure bridges'){ $mode='bridges'; continue }
    if($line -eq '## Consequence web'){ $mode='consequences'; continue }
    if($line -eq '## Cozy return structure'){ $mode='cozy'; continue }
    if($line -match '^## '){ if($line -notin @('## Independent adventure bridges','## Consequence web','## Cozy return structure')){$mode=''} }
    if($mode -eq 'bridges' -and $line -match '^\| ([^|-][^|]+?) \| ([^|]+?) \|$'){
        $from=$matches[1].Trim();$handoff=$matches[2].Trim(); if($from -eq 'Completed adventure'){ continue }; Add-Knowledge 'secret' "secret.caldris.campaign.bridge.$(Get-Slug $from)" "Adventure bridge — $from" $handoff 'location.caldris.atlas' 'location.caldris.atlas' 'intention' 'Caldris campaign tapestry — independent adventure bridges'
    }
    elseif($mode -in @('consequences','cozy') -and $line -match '^- (.+)$'){
        $detail=$matches[1]; $prefix=if($mode -eq 'cozy'){'Cozy return'}else{'Consequence guidance'}
        Add-Knowledge 'secret' "secret.caldris.campaign.$mode.$(Get-Slug (($detail -split '[.;]')[0]))" $prefix $detail 'location.caldris.atlas' 'location.caldris.atlas' 'intention' "Caldris campaign tapestry — $mode"
    }
}
$campaignEntities=@($entities);$campaignRelationships=@($relationships)
$manifestPaths += Write-KnowledgeBatches 'caldris-companion-content-v5-03-campaign-reference' 40 $campaignEntities $campaignRelationships
$entities.Clear();$relationships.Clear()

# Three missing broad era chronology anchors.
$eras = @(
    @('first-footprints','The First Footprints','The oldest remembered migrations followed immense animals between ice and warming river valleys. Migration songs, standing stones, passage rights, and borderless burial mounds remain.','before c. 3100 BS',-990000000),
    @('hearths-and-standing-stones','Hearths and Standing Stones','Permanent villages, managed woods, barrows, river shrines, and seasonal moots established many surviving rights of commons, guest refuge, and sanctuary.','c. 3100–1900 BS',-900000000),
    @('river-thrones','The River Thrones','Irrigation, metalwork, walled river towns, toll writing, and dynastic temples created the first durable states around the Mere, Aur, and Namar waters.','c. 1900–840 BS',-800000000)
)
foreach($era in $eras){
    $id="chronology.caldris.$($era[0])"
    $entities.Add([ordered]@{
        entityId=$id;name=$era[1];expectedRevision=0
        components=@([ordered]@{qualifiedTypeId='dnd2024.game.core.world.chronology';expectedRevision=0;value=[ordered]@{status='active';title=$era[1];summary=$era[2];calendarId='caldris-common-era';occurredAtMinute=$era[4];precision='era';dateLabel=$era[3];visibility='public'}})
        containment=[ordered]@{containerEntityId='location.caldris.atlas';slot='history';expectedRevision=0}
    })
    $relationships.Add([ordered]@{fromEntityId=$id;toEntityId='world.caldris';qualifiedKind='dnd2024.game.core.world.chronology.in-world';expectedRevision=0;value=@{}})
}
$manifestPaths += Write-Manifest 'caldris-companion-content-v5-04-era-chronology.json' 60 @($entities) @($relationships)

$result = [ordered]@{
    manifests = @($manifestPaths | ForEach-Object { Resolve-Path $_ | Select-Object -ExpandProperty Path })
    locationCount = $locationEntities.Count
    worldKnowledgeCount = $worldKnowledgeEntities.Count
    campaignKnowledgeCount = $campaignEntities.Count
    chronologyCount = $eras.Count
}
$result | ConvertTo-Json -Depth 5
