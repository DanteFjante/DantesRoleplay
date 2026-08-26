[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path,
    [string]$MatrixPath = 'ruleset/dnd2024/adoption/evidence/coverage-matrix-1b.json',
    [string]$OutputPath = 'ruleset/dnd2024/adoption/evidence/conflict-gap-report-1c.json'
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path $RepositoryRoot).Path
function Full([string]$p) { if ([IO.Path]::IsPathRooted($p)) { [IO.Path]::GetFullPath($p) } else { [IO.Path]::GetFullPath((Join-Path $repo $p)) } }
function Rel([string]$p) { [IO.Path]::GetRelativePath($repo, $p).Replace('\','/') }
$matrixFull = Full $MatrixPath
$matrix = Get-Content -Raw $matrixFull | ConvertFrom-Json -Depth 100
$rows = @($matrix.rows)
$exactOverlaps = @($rows | Where-Object { $_.activeOwner.state -eq 'verified' -and @($_.archiveCandidates | Where-Object status -eq 'compatible').Count -gt 0 })
$conflicts = @($rows | Where-Object { @($_.conflicts).Count -gt 0 -or @($_.archiveCandidates | Where-Object status -eq 'conflicting').Count -gt 0 })
$archiveOnly = @($rows | Where-Object { $_.activeOwner.state -eq 'missing' -and @($_.archiveCandidates).Count -gt 0 })
$activeOnly = @($rows | Where-Object { $_.activeOwner.state -eq 'verified' -and @($_.archiveCandidates).Count -eq 0 })
$unresolved = @($archiveOnly | Where-Object { @($_.tests).Count -eq 0 -or $null -eq $_.srd.locator -or @($_.dependencies).Count -eq 0 })
$byKind = @($archiveOnly | Group-Object { ($_.capabilityKey -split '\.')[0] } | Sort-Object Name | ForEach-Object { [ordered]@{ kind=$_.Name; count=$_.Count } })
$report = [ordered]@{
    format='dnd-code-adoption-conflict-gap-report/v2'
    generatedFrom=[ordered]@{ matrix=(Rel $matrixFull); matrixSha256=(Get-FileHash $matrixFull -Algorithm SHA256).Hash.ToUpperInvariant(); inventoryCommit=$matrix.inventoryCommit }
    reconciliation=[ordered]@{ totalRows=$rows.Count; exactActiveArchiveMatches=$exactOverlaps.Count; activeOnly=$activeOnly.Count; archiveOnly=$archiveOnly.Count; conflicts=$conflicts.Count; rowsReconciled=($exactOverlaps.Count+$activeOnly.Count+$archiveOnly.Count+$conflicts.Count) }
    exactMatches=@($exactOverlaps | ForEach-Object capabilityKey | Sort-Object)
    conflicts=@($conflicts | ForEach-Object { [ordered]@{ capabilityKey=$_.capabilityKey; evidence=@($_.conflicts) } })
    gaps=[ordered]@{ archiveOnlyCapabilities=@($archiveOnly | ForEach-Object capabilityKey | Sort-Object); byKind=$byKind; rowsWithHistoricalTests=@($archiveOnly | Where-Object { @($_.tests).Count -gt 0 }).Count; rowsWithDependencies=@($archiveOnly | Where-Object { @($_.dependencies).Count -gt 0 }).Count; rowsWithSrdLocatorEvidence=@($archiveOnly | Where-Object { $null -ne $_.srd.locator }).Count; rowsWithExactDonorFileCandidates=@($archiveOnly | Where-Object { @($_.donorCandidates).Count -gt 0 }).Count; rowsWithExactFoundryReferences=@($archiveOnly | Where-Object { @($_.foundryReferences).Count -gt 0 }).Count; unresolvedCapabilityKeys=@($unresolved | ForEach-Object capabilityKey | Sort-Object) }
    requiredReviews=@('Verify each archived SRD locator against the official source record before changing alignment to dnd2024-owned.','Review exact donor files at symbol level and record SHA-256/license/provenance before adaptation.','Review exact Foundry files for edge cases only; keep adopted false unless a later per-symbol license and independence review approves reuse.','Classify archive-only rows by feature cohort, schema/effect compatibility, tests, and dependency closure before selecting recovery work.')
    status='review-required-no-cohort-selected'
}
if ($report.reconciliation.rowsReconciled -ne $rows.Count) { throw "Reconciliation failed: $($report.reconciliation.rowsReconciled) of $($rows.Count) rows." }
$out = Full $OutputPath; New-Item -ItemType Directory -Force (Split-Path $out) | Out-Null; [IO.File]::WriteAllText($out, ($report | ConvertTo-Json -Depth 40) + "`n", [Text.UTF8Encoding]::new($false))
Write-Output ([ordered]@{ output=(Rel $out); totalRows=$rows.Count; exactMatches=$exactOverlaps.Count; archiveOnly=$archiveOnly.Count; conflicts=$conflicts.Count } | ConvertTo-Json -Compress)
