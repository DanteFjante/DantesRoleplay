[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path,
    [string]$InputPath = 'ruleset/dnd2024/adoption/evidence/coverage-matrix-1a.json',
    [string]$OutputPath = 'ruleset/dnd2024/adoption/evidence/coverage-matrix-1b.json',
    [string]$InventoryPath = 'ruleset/dnd2024/adoption/evidence/slice-1b-source-inventory.json'
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path $RepositoryRoot).Path
function Full([string]$p) { if ([IO.Path]::IsPathRooted($p)) { [IO.Path]::GetFullPath($p) } else { [IO.Path]::GetFullPath((Join-Path $repo $p)) } }
function Rel([string]$p) { [IO.Path]::GetRelativePath($repo, $p).Replace('\','/') }
$inputFull = Full $InputPath
$matrix = Get-Content -Raw $inputFull | ConvertFrom-Json -Depth 100
$donor = @(
    @{ token='check.ability'; path='src/derive/ability-check.ts'; blob='bce546d51233258a8cf991c9fb3b33b255e3d3f5' },
    @{ token='abilities'; path='src/derive/ability.ts'; blob='b18fed9555c670e41045b3c8f3f9c791d62f821f' },
    @{ token='armor-class'; path='src/derive/ac.ts'; blob='d6b386a6c301dea43f7b8056ba84d2f924124df0' },
    @{ token='carrying-capacity'; path='src/derive/carrying-capacity.ts'; blob='8d3fd05cfab1d9ccee92b2cfaf0c25e544d776a3' },
    @{ token='creature-size'; path='src/derive/creature-size.ts'; blob='9c53a2dbef9c2521fa6050948e81f147b41ea15b' },
    @{ token='damage-mitigation'; path='src/derive/damage-mitigation.ts'; blob='322cfcf7499c824682ab6d675f83258c9b15e6ee' },
    @{ token='saving-throw'; path='src/derive/save.ts'; blob='1dd543078c4652c154326a467d3eef6b64742cd1' },
    @{ token='speed'; path='src/derive/speed.ts'; blob='71ad8a8e5f90026fed2bf23db0760d63d68d792d' },
    @{ token='.dice'; path='src/rng/dice.ts'; blob='db5dd5e8bd83a512c2d45430d6bc6afa73f6a834' },
    @{ token='weapon-mastery'; path='src/derive/weapon-mastery.ts'; blob='f38eb7b507adee44ab8ed69c2b9e759ee15a8327' }
)
$foundry = @(
    @{ token='check.ability'; path='module/dice/d20-roll.mjs'; blob='33d1551d5ed8fcc1aaac6a28d1238101d71b2035' },
    @{ token='d20-test'; path='module/dice/d20-roll.mjs'; blob='33d1551d5ed8fcc1aaac6a28d1238101d71b2035' },
    @{ token='.dice'; path='module/dice/basic-roll.mjs'; blob='9cd2529326f5e30411cb3dc68e823633b6ce700c' },
    @{ token='damage'; path='module/dice/damage-roll.mjs'; blob='8b8807b983ba6311131319fb5faa535201d6cfad' }
)
$donorMatches = 0; $foundryMatches = 0
foreach ($row in $matrix.rows) {
    $name = $row.title.ToLowerInvariant()
    $d = @($donor | Where-Object { $name.Contains($_.token) } | Sort-Object path -Unique)
    $row.donorCandidates = @($d | ForEach-Object { [ordered]@{ path=$_.path; symbol=$null; evidence=@('ruleset/dnd2024/adoption/donor-lock.json','ruleset/dnd2024/adoption/evidence/donor-baseline-2026-08-25.json','ruleset/dnd2024/adoption/evidence/slice-1b-source-inventory.json'); status='unclassified' } })
    $f = @($foundry | Where-Object { $name.Contains($_.token) } | Sort-Object path -Unique)
    $row.foundryReferences = @($f | ForEach-Object { [ordered]@{ path=$_.path; behavior='Exact pinned Foundry implementation file for later edge-case review; no bytes adopted by Slice 1.'; adopted=$false } })
    $donorMatches += $d.Count; $foundryMatches += $f.Count
}
$out = Full $OutputPath; New-Item -ItemType Directory -Force (Split-Path $out) | Out-Null; [IO.File]::WriteAllText($out, ($matrix | ConvertTo-Json -Depth 100) + "`n", [Text.UTF8Encoding]::new($false))
$inventory = [ordered]@{
    format='dnd-code-adoption-slice-1b-source-inventory/v2'; generatedFrom=[ordered]@{ matrix=(Rel $inputFull); matrixSha256=(Get-FileHash $inputFull -Algorithm SHA256).Hash.ToUpperInvariant() }
    donor=[ordered]@{ repository='https://github.com/greghcarr/dnd-srd-engine.git'; commit='ead852b19b9e45f54f43e193caf4f10aad91a91b'; tree='3ba1b25ed10231799a3d0f5d752d5aedc4b21aff'; exactFiles=@($donor | Sort-Object path | ForEach-Object { [ordered]@{ path=$_.path; gitBlobSha1=$_.blob; matchToken=$_.token } }); matchedReferences=$donorMatches }
    foundry=[ordered]@{ repository='https://github.com/foundryvtt/dnd5e.git'; commit='275bed0be4ccfa15e6b3347acccb8da8784726d9'; tree='df5ae6627620ad68e5b5d7a1851a8d9a885c15e0'; role='reference-only; no assets'; exactFiles=@($foundry | Sort-Object path,token | ForEach-Object { [ordered]@{ path=$_.path; gitBlobSha1=$_.blob; matchToken=$_.token } }); matchedReferences=$foundryMatches }
    srd=[ordered]@{ sourceId='source.dnd2024.srd-5.2.1'; rowsWithArchivedLocator=@($matrix.rows | Where-Object { $null -ne $_.srd.locator }).Count; verifiedRows=@($matrix.rows | Where-Object { $_.srd.verified }).Count; policy='Archived locators are evidence only; official verification remains required before rule implementation.' }
    unmatchedArchiveCapabilityKeys=@($matrix.rows | Where-Object { $_.disposition -eq 'recover-archive' -and @($_.donorCandidates).Count -eq 0 -and @($_.foundryReferences).Count -eq 0 } | ForEach-Object capabilityKey | Sort-Object)
}
$inv = Full $InventoryPath; [IO.File]::WriteAllText($inv, ($inventory | ConvertTo-Json -Depth 30) + "`n", [Text.UTF8Encoding]::new($false))
Write-Output ([ordered]@{ output=(Rel $out); inventory=(Rel $inv); donorReferences=$donorMatches; foundryReferences=$foundryMatches; srdLocatorRows=$inventory.srd.rowsWithArchivedLocator } | ConvertTo-Json -Compress)
