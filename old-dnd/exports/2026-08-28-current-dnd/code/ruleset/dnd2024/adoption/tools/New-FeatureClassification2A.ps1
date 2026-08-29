[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path,
    [string]$MatrixPath = 'ruleset/dnd2024/adoption/evidence/coverage-matrix-1b.json',
    [string]$OutputPath = 'ruleset/dnd2024/adoption/evidence/feature-1-10-classification-2a.json'
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path $RepositoryRoot).Path
function Full([string]$p) { if ([IO.Path]::IsPathRooted($p)) { [IO.Path]::GetFullPath($p) } else { [IO.Path]::GetFullPath((Join-Path $repo $p)) } }
function Rel([string]$p) { [IO.Path]::GetRelativePath($repo, $p).Replace('\','/') }
$matrixFull = Full $MatrixPath
$matrix = Get-Content -Raw $matrixFull | ConvertFrom-Json -Depth 100
$features = @(
    @{ number=1; title='Ability scores and fixed-DC ability checks'; tokens=@('dnd2024.abilities','check.ability','dnd2024.dice'); plan='old-dnd/ruleset/dnd2024/feature-01/FEATURE-1-RUNBOOK.md'; depends=@() },
    @{ number=2; title='Level, proficiency bonus, and skill checks'; tokens=@('character-level','skill-proficiencies'); plan='old-dnd/ruleset/dnd2024/feature-02/FEATURE-2-DEPENDENCY-PLAN.md'; depends=@(1) },
    @{ number=3; title='Advantage/Disadvantage and D20 Test convention'; tokens=@('d20-test.state-effects'); plan='old-dnd/ruleset/dnd2024/feature-03/FEATURE-3-DEPENDENCY-PLAN.md'; depends=@(1,2) },
    @{ number=4; title='Saving throws'; tokens=@('saving-throw'); plan='old-dnd/ruleset/dnd2024/feature-04/FEATURE-4-DEPENDENCY-PLAN.md'; depends=@(3) },
    @{ number=5; title='Initiative and encounter ordering'; tokens=@('initiative'); plan='old-dnd/ruleset/dnd2024/feature-05/FEATURE-5-DEPENDENCY-PLAN.md'; depends=@(3) },
    @{ number=6; title='Armor Class and Hit Points'; tokens=@('armor-class','hit-points'); plan='old-dnd/ruleset/dnd2024/feature-06/FEATURE-6-DEPENDENCY-PLAN.md'; depends=@() },
    @{ number=7; title='Weapon profiles and proficiency'; tokens=@('weapon-profile','weapon-proficiencies','weapon.dnd2024.'); plan='old-dnd/ruleset/dnd2024/feature-07/FEATURE-7-DEPENDENCY-PLAN.md'; depends=@(2) },
    @{ number=8; title='Weapon attack rolls'; tokens=@('weapon-attack'); plan='old-dnd/ruleset/dnd2024/feature-08/FEATURE-8-DEPENDENCY-PLAN.md'; depends=@(3,6,7) },
    @{ number=9; title='Weapon damage and Hit Point loss'; tokens=@('weapon-damage'); plan='old-dnd/ruleset/dnd2024/feature-09/FEATURE-9-DEPENDENCY-PLAN.md'; depends=@(6,7,8) },
    @{ number=10; title='Reproducible vertical test session'; tokens=@('feature-10'); plan='old-dnd/ruleset/dnd2024/feature-10/FEATURE-10-DEPENDENCY-PLAN.md'; depends=@(1,2,3,4,5,6,7,8,9) }
)
$byKey = @{}; foreach ($row in $matrix.rows) { $byKey[$row.capabilityKey] = $row }
$classified = [Collections.Generic.List[object]]::new()
foreach ($feature in $features) {
    if (-not (Test-Path (Full $feature.plan))) { throw "Missing archived feature evidence: $($feature.plan)" }
    $rows = [Collections.Generic.List[object]]::new()
    foreach ($row in $matrix.rows) {
        if ($row.disposition -ne 'recover-archive') { continue }
        $matched = $false
        foreach ($token in $feature.tokens) { if ($row.title.Contains($token)) { $matched = $true; break } }
        if ($matched) { $rows.Add($row) }
    }
    $rows = @($rows | Sort-Object capabilityKey -Unique)
    if ($rows.Count -eq 0) { throw "No archive capabilities classified for Feature $($feature.number)" }
    $closure = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $pending = [Collections.Generic.Queue[string]]::new()
    foreach ($row in $rows) { if ($closure.Add($row.capabilityKey)) { $pending.Enqueue($row.capabilityKey) } }
    while ($pending.Count -gt 0) {
        $key = $pending.Dequeue()
        foreach ($dependency in $byKey[$key].dependencies) {
            if ($byKey.ContainsKey($dependency) -and $byKey[$dependency].alignment -eq 'dnd2024-compatible' -and $byKey[$dependency].archiveCandidates.Count -gt 0 -and $closure.Add($dependency)) { $pending.Enqueue($dependency) }
        }
    }
    $closureRows = @($closure | ForEach-Object { $byKey[$_] } | Sort-Object capabilityKey)
    $blockers = [Collections.Generic.List[string]]::new()
    if (@($closureRows | Where-Object { $null -eq $_.srd.locator }).Count -gt 0) { $blockers.Add('Some selected rows lack archived SRD locator evidence; official source review is required.') }
    if (@($closureRows | Where-Object { @($_.tests).Count -eq 0 }).Count -gt 0) { $blockers.Add('Some selected rows lack exact historical test references.') }
    $blockers.Add('Current application-kernel projection, result/effect, replay, and transaction compatibility is unproven.')
    $blockers.Add('No archived artifact is active; a later slice must revalidate exact source, schema, and effect contracts.')
    if ($feature.number -eq 10) { $blockers.Add('Feature 10 is acceptance fixtures/transcript evidence; it cannot replace Features 1–9 mechanics.') }
    $classified.Add([ordered]@{
        feature=$feature.number; title=$feature.title; archivedPlan=$feature.plan; dependsOnFeatures=@($feature.depends)
        directCapabilityKeys=@($rows | ForEach-Object capabilityKey); dependencyClosureCapabilityKeys=@($closureRows | ForEach-Object capabilityKey); historicalTests=@($closureRows | ForEach-Object tests | ForEach-Object path | Sort-Object -Unique)
        archivedSrdLocatorEvidenceRows=@($closureRows | Where-Object { $null -ne $_.srd.locator }).Count; exactDonorCandidateRows=@($closureRows | Where-Object { @($_.donorCandidates).Count -gt 0 }).Count; exactFoundryReferenceRows=@($closureRows | Where-Object { @($_.foundryReferences).Count -gt 0 }).Count
        classification='recover-archive-candidate'; readiness='blocked'; blockers=@($blockers | Sort-Object -Unique)
    })
}
$allKeys = @($classified | ForEach-Object dependencyClosureCapabilityKeys | Sort-Object -Unique)
$shared = @($classified | ForEach-Object dependencyClosureCapabilityKeys | Group-Object | Where-Object Count -gt 1 | ForEach-Object Name | Sort-Object)
$report = [ordered]@{
    format='dnd-code-adoption-feature-classification/v1'; generatedFrom=[ordered]@{ matrix=(Rel $matrixFull); matrixSha256=(Get-FileHash $matrixFull -Algorithm SHA256).Hash.ToUpperInvariant(); inventoryCommit=$matrix.inventoryCommit }
    scope=[ordered]@{ features=@(1..10); classifiedFeatures=$classified.Count; selectedCapabilityKeys=$allKeys.Count; sharedCapabilityKeys=$shared.Count }
    features=@($classified); sharedCapabilityKeys=$shared; status='classification-complete-no-cohort-selected'
}
$out = Full $OutputPath; New-Item -ItemType Directory -Force (Split-Path $out) | Out-Null; [IO.File]::WriteAllText($out, ($report | ConvertTo-Json -Depth 40) + "`n", [Text.UTF8Encoding]::new($false))
Write-Output ([ordered]@{ output=(Rel $out); features=$classified.Count; capabilities=$allKeys.Count; shared=$shared.Count } | ConvertTo-Json -Compress)
