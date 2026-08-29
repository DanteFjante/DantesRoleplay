[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path,
    [string]$MatrixPath = 'ruleset/dnd2024/adoption/evidence/coverage-matrix-1b.json',
    [string]$Feature1Path = 'ruleset/dnd2024/adoption/evidence/feature-1-10-classification-2a.json',
    [string]$LaterPath = 'ruleset/dnd2024/adoption/evidence/later-feature-classification-2b.json',
    [string]$OutputPath = 'ruleset/dnd2024/adoption/evidence/first-cohort-selection-2c.json'
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path $RepositoryRoot).Path
function Full([string]$p) { if ([IO.Path]::IsPathRooted($p)) { [IO.Path]::GetFullPath($p) } else { [IO.Path]::GetFullPath((Join-Path $repo $p)) } }
function Rel([string]$p) { [IO.Path]::GetRelativePath($repo, $p).Replace('\','/') }
$matrixFull=Full $MatrixPath;$featureFull=Full $Feature1Path;$laterFull=Full $LaterPath
$matrix=Get-Content -Raw $matrixFull|ConvertFrom-Json -Depth 100;$featureReport=Get-Content -Raw $featureFull|ConvertFrom-Json -Depth 100;$laterReport=Get-Content -Raw $laterFull|ConvertFrom-Json -Depth 100
$byKey=@{};foreach($row in $matrix.rows){$byKey[$row.capabilityKey]=$row}
$selectedKeys=@('componentdefinition.dnd2024.abilities.v0','mechanic.mechanic.dnd2024.check.ability.v1','procedure.procedure.mechanic.dnd2024.check.ability.v1')
foreach($key in $selectedKeys){if(-not$byKey.ContainsKey($key)){throw "Selected capability does not exist: $key"};if(@($byKey[$key].archiveCandidates).Count-eq0){throw "Selected capability lacks archive evidence: $key"}}
$featureOne=@($featureReport.features|Where-Object feature -eq 1);if($featureOne.Count-ne1){throw 'Feature 1 classification is missing or ambiguous.'}
$archivedEvidence=@($selectedKeys|ForEach-Object{[ordered]@{capabilityKey=$_;archiveCandidates=$byKey[$_].archiveCandidates;historicalTests=$byKey[$_].tests;srd=$byKey[$_].srd}})
$deferred=@(@($featureReport.features|Where-Object feature -ne 1|ForEach-Object feature)+@($laterReport.features|ForEach-Object feature)|Sort-Object -Unique)
$record=[ordered]@{
    format='dnd-code-adoption-first-cohort-selection/v1'
    generatedFrom=[ordered]@{matrix=(Rel $matrixFull);matrixSha256=(Get-FileHash $matrixFull -Algorithm SHA256).Hash.ToUpperInvariant();featureClassification=(Rel $featureFull);featureClassificationSha256=(Get-FileHash $featureFull -Algorithm SHA256).Hash.ToUpperInvariant();laterClassification=(Rel $laterFull);laterClassificationSha256=(Get-FileHash $laterFull -Algorithm SHA256).Hash.ToUpperInvariant()}
    selected=[ordered]@{historicalFeature=1;title='Ability-score fixed-DC check seam';candidateCapabilityKeys=$selectedKeys;archivedPlan=$featureOne[0].archivedPlan;selection='first test-only recovery cohort';futureLeaf='3A operation-view mapping';closedProbeInputs=@('ability score','fixed DC','kernel-owned seeded RNG');forbiddenProbeInputs=@('skill proficiency','character level','conditions','whole donor CampaignState','donor persistence/events/reducers');effects='none';transaction='none'}
    evidence=$archivedEvidence
    blockers=@('Official SRD 5.2.1 locator verification for the selected rule behavior is still required.','The archived check mechanic has broader dependencies than the selected raw-check probe; Leaf 3A must reject undeclared reads.','Current application-kernel projection and sandbox wrapper compatibility are unproven.','No archive artifact is active or approved for direct copy.')
    deferredFeatureNumbers=$deferred
    status='selected-for-test-only-seam-not-ready-or-active'
}
$out=Full $OutputPath;New-Item -ItemType Directory -Force (Split-Path $out)|Out-Null;[IO.File]::WriteAllText($out,($record|ConvertTo-Json -Depth 50)+"`n",[Text.UTF8Encoding]::new($false));Write-Output ([ordered]@{output=(Rel $out);selected=$selectedKeys.Count;deferred=$deferred.Count}|ConvertTo-Json -Compress)
