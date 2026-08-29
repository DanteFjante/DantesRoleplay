[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path,
    [string]$MatrixPath = 'ruleset/dnd2024/adoption/evidence/coverage-matrix-1b.json',
    [string]$OutputPath = 'ruleset/dnd2024/adoption/evidence/later-feature-classification-2b.json'
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path $RepositoryRoot).Path
function Full([string]$p) { if ([IO.Path]::IsPathRooted($p)) { [IO.Path]::GetFullPath($p) } else { [IO.Path]::GetFullPath((Join-Path $repo $p)) } }
function Rel([string]$p) { [IO.Path]::GetRelativePath($repo, $p).Replace('\','/') }
$matrixFull = Full $MatrixPath
$matrix = Get-Content -Raw $matrixFull | ConvertFrom-Json -Depth 100
$eligible = @(
    @{ number=11; evidence='verified'; note='turn and round lifecycle' }, @{ number=12; evidence='verified'; note='action economy' }, @{ number=13; evidence='verified'; note='conditions' }, @{ number=14; evidence='verified'; note='exhaustion' }, @{ number=15; evidence='verified'; note='damage mitigation' }, @{ number=16; evidence='verified'; note='temporary HP and healing' }, @{ number=17; evidence='partial-verified'; note='dying slices 1-3 only' }, @{ number=20; evidence='partial-verified'; note='movement slices 1-5 only' }, @{ number=21; evidence='partial-verified'; note='static Shortbow range only'; manualTitles=@('weapon.dnd2024.shortbow') }, @{ number=22; evidence='partial-verified'; note='unarmed strike evidence only' }, @{ number=23; evidence='accepted'; note='equipment and inventory' }, @{ number=24; evidence='accepted'; note='armor and AC aggregation' }, @{ number=25; evidence='partial-verified'; note='static weapon property/mastery facts only' }, @{ number=26; evidence='partial-verified'; note='static species profiles only' }, @{ number=27; evidence='partial-verified'; note='Fighter 1-2 progression reader only' }, @{ number=28; evidence='accepted'; note='origin foundation in accepted scope' }, @{ number=29; evidence='partial-verified'; note='static magic-item profiles only' }, @{ number=31; evidence='partial-verified'; note='static spell identities only' }, @{ number=32; evidence='partial-verified'; note='static spell resolution profiles only' }, @{ number=33; evidence='accepted'; note='rest policy and bounded episode' }, @{ number=36; evidence='implemented-not-accepted'; note='XP state and next-level eligibility only' }, @{ number=39; evidence='accepted'; note='Heroic Inspiration presence/grant recorder only' }
)
$plannedOnly = @(18,19,30,34,35,37,38)
$byKey = @{}; foreach ($row in $matrix.rows) { $byKey[$row.capabilityKey] = $row }
$testRoot = Full 'old-dnd/DantesRoleplay.Tests'
$features = [Collections.Generic.List[object]]::new()
foreach ($feature in $eligible) {
    $folder = Full ('old-dnd/ruleset/dnd2024/feature-{0:D2}' -f $feature.number)
    $plans = @(Get-ChildItem $folder -File -ErrorAction SilentlyContinue | Where-Object { $_.Name -like '*DEPENDENCY-PLAN.md' -or $_.Name -like '*RECEIPT.md' -or $_.Name -eq 'IMPLEMENTATION-STATUS.md' } | ForEach-Object { Rel $_.FullName } | Sort-Object)
    if ($plans.Count -eq 0) { throw "Missing archived plan or receipt evidence for Feature $($feature.number)" }
    $tests = @(Get-ChildItem $testRoot -Filter ('CatalogFeature{0}*.cs' -f $feature.number) -File -ErrorAction SilentlyContinue)
    $texts = @{}; foreach ($test in $tests) { $texts[$test.FullName] = Get-Content -Raw $test.FullName }
    $direct = [Collections.Generic.List[object]]::new()
    foreach ($row in $matrix.rows) {
        if ($row.alignment -ne 'dnd2024-compatible' -or @($row.archiveCandidates).Count -eq 0) { continue }
        $matched = $false
        foreach ($text in $texts.Values) { if ($text.Contains($row.title)) { $matched = $true; break } }
        if (-not $matched -and $feature.manualTitles) { $matched = @($feature.manualTitles | Where-Object { $row.title -eq $_ }).Count -gt 0 }
        if ($matched) { $direct.Add($row) }
    }
    $direct = @($direct | Sort-Object capabilityKey -Unique)
    if ($direct.Count -eq 0) { throw "No direct archive evidence mapped for Feature $($feature.number)" }
    $closure = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal); $pending = [Collections.Generic.Queue[string]]::new()
    foreach ($row in $direct) { if ($closure.Add($row.capabilityKey)) { $pending.Enqueue($row.capabilityKey) } }
    while ($pending.Count -gt 0) { $key=$pending.Dequeue(); foreach ($dependency in $byKey[$key].dependencies) { if ($byKey.ContainsKey($dependency) -and $byKey[$dependency].alignment -eq 'dnd2024-compatible' -and @($byKey[$dependency].archiveCandidates).Count -gt 0 -and $closure.Add($dependency)) { $pending.Enqueue($dependency) } } }
    $closureRows = @($closure | ForEach-Object { $byKey[$_] } | Sort-Object capabilityKey)
    $blockers = [Collections.Generic.List[string]]::new()
    if ($feature.evidence -ne 'accepted' -and $feature.evidence -ne 'verified') { $blockers.Add('Historical evidence is explicitly partial; unimplemented behavior remains outside recovery scope.') }
    if ($tests.Count -eq 0) { $blockers.Add('No feature-specific historical test file was found; direct evidence is manually constrained.') }
    if (@($closureRows | Where-Object { $null -eq $_.srd.locator }).Count -gt 0) { $blockers.Add('Some closure rows lack archived SRD locator evidence; official source review is required.') }
    $blockers.Add('Current application-kernel projection, effect, transaction, replay, and source-registration compatibility is unproven.')
    $features.Add([ordered]@{ feature=$feature.number; historicalEvidence=$feature.evidence; scopeNote=$feature.note; archivedEvidence=$plans; historicalTests=@($tests | ForEach-Object { Rel $_.FullName }); directCapabilityKeys=@($direct | ForEach-Object capabilityKey); dependencyClosureCapabilityKeys=@($closureRows | ForEach-Object capabilityKey); classification='recover-archive-candidate'; readiness='blocked'; blockers=@($blockers | Sort-Object -Unique) })
}
$allKeys = @($features | ForEach-Object dependencyClosureCapabilityKeys | Sort-Object -Unique)
$shared = @($features | ForEach-Object dependencyClosureCapabilityKeys | Group-Object | Where-Object Count -gt 1 | ForEach-Object Name | Sort-Object)
$report = [ordered]@{ format='dnd-code-adoption-later-feature-classification/v1'; generatedFrom=[ordered]@{ matrix=(Rel $matrixFull); matrixSha256=(Get-FileHash $matrixFull -Algorithm SHA256).Hash.ToUpperInvariant(); inventoryCommit=$matrix.inventoryCommit }; scope=[ordered]@{ classifiedFeatures=$features.Count; plannedOnlyExcludedFeatures=$plannedOnly; directCapabilityKeys=@($features | ForEach-Object directCapabilityKeys | Sort-Object -Unique).Count; dependencyClosureCapabilityKeys=$allKeys.Count; sharedCapabilityKeys=$shared.Count }; features=@($features); sharedCapabilityKeys=$shared; status='classification-complete-no-cohort-selected' }
$out=Full $OutputPath;New-Item -ItemType Directory -Force (Split-Path $out)|Out-Null;[IO.File]::WriteAllText($out,($report|ConvertTo-Json -Depth 50)+"`n",[Text.UTF8Encoding]::new($false));Write-Output ([ordered]@{output=(Rel $out);features=$features.Count;capabilities=$allKeys.Count;shared=$shared.Count}|ConvertTo-Json -Compress)
