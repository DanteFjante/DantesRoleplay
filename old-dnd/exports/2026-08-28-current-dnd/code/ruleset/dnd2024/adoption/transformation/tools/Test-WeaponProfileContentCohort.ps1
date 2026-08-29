[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../../../..')).Path,
    [string]$ManifestPath = (Join-Path $PSScriptRoot '../fixtures/slice-10b3a-weapon-profile-transform.json')
)
$ErrorActionPreference = 'Stop'; Set-StrictMode -Version Latest
function Names($v) { @($v.PSObject.Properties.Name | Sort-Object) }
function Assert-Names($v,[string[]]$e,[string]$s) { $a=Names $v; $w=@($e|Sort-Object); if(($a-join ',')-cne($w-join ',')){throw "$s has unexpected shape: $($a-join ', ')"} }
function Json($v) { $v | ConvertTo-Json -Depth 30 -Compress }
function Contained([string]$root,[string]$relative) { $r=[IO.Path]::GetFullPath($root).TrimEnd([IO.Path]::DirectorySeparatorChar); $p=[IO.Path]::GetFullPath((Join-Path $r $relative)); if(-not $p.StartsWith($r+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw "Path escapes root: $relative"}; $p }
$root=[IO.Path]::GetFullPath($RepositoryRoot); $m=Get-Content -Raw -LiteralPath ([IO.Path]::GetFullPath($ManifestPath))|ConvertFrom-Json -Depth 40
Assert-Names $m @('format','cohort','sourceRevision','officialSource','license','entries') 'Manifest'
if($m.format-cne'dnd2024-static-content-transform/v1'-or$m.cohort-cne'slice-10b3a-weapon-profiles'){throw 'Unexpected weapon manifest.'}
if($m.officialSource.sourceId-cne'source.dnd2024.srd-5.2.1'-or$m.officialSource.locator-cne'Equipment > Weapons'){throw 'Weapon source binding drift.'}
if($m.license.classification-cne'CC-BY-4.0'-or[string]::IsNullOrWhiteSpace($m.license.attribution)-or[string]::IsNullOrWhiteSpace($m.license.changes)){throw 'Attribution/change indication required.'}
$ids=@('weapon.dnd2024.battleaxe','weapon.dnd2024.dagger','weapon.dnd2024.flail','weapon.dnd2024.greatsword','weapon.dnd2024.javelin','weapon.dnd2024.shortbow')
if(@($m.entries).Count-ne6-or(@($m.entries.id|Sort-Object)-join',')-cne(($ids|Sort-Object)-join',')){throw 'Weapon cohort is incomplete.'}
$seen=@{};$paths=@{};$results=@();$keep=@('category','kind','attackAbilities','damage','sourceRef')
foreach($e in @($m.entries)){
  Assert-Names $e @('id','name','sourcePath','sourceSha256','discardedKeys','targetPath') $e.id
  if($seen[$e.id]-or$paths[$e.targetPath]){throw "Duplicate weapon ID/path: $($e.id)"};$seen[$e.id]=1;$paths[$e.targetPath]=1
  $sp=Contained $root $e.sourcePath;$tp=Contained $root $e.targetPath
  if((Get-FileHash $sp -Algorithm SHA256).Hash.ToUpperInvariant()-cne$e.sourceSha256){throw "Source hash drift: $($e.sourcePath)"}
  $s=Get-Content -Raw $sp|ConvertFrom-Json -Depth 30; Assert-Names $s @('id','name','components') $e.sourcePath; Assert-Names $s.components @('dnd2024.weapon-profile') "$($e.id) components"
  if($s.id-cne$e.id-or$s.name-cne$e.name){throw "Source identity drift: $($e.id)"};$p=$s.components.'dnd2024.weapon-profile'
  $discard=@((Names $p)|Where-Object{$keep-cnotcontains$_}|Sort-Object);if(($discard-join',')-cne(@($e.discardedKeys|Sort-Object)-join',')){throw "Discarded-key drift: $($e.id)"}
  $reduced=[ordered]@{category=$p.category;kind=$p.kind;attackAbilities=$p.attackAbilities;damage=$p.damage;sourceRef=$p.sourceRef}
  $generated=[ordered]@{id=$e.id;name=$e.name;components=[ordered]@{'dnd2024.weapon-profile'=$reduced}}
  $target=Get-Content -Raw $tp|ConvertFrom-Json -Depth 30;if((Json $target)-cne(Json $generated)){throw "Target drift: $($e.targetPath)"}
  $results += [ordered]@{id=$e.id;targetSha256=(Get-FileHash $tp -Algorithm SHA256).Hash.ToUpperInvariant()}
}
[ordered]@{format='dnd2024-static-content-transform-report/v1';cohort=$m.cohort;status='verified';candidateCount=$results.Count;candidates=$results}|ConvertTo-Json -Depth 10
