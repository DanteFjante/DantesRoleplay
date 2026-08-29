[CmdletBinding()]
param([string]$NpxCommand = 'npx', [string]$DotnetCommand = 'dotnet')

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..\..')).Path
$adoptionRoot = Join-Path $repo 'ruleset/dnd2024/adoption'
$root = Join-Path $adoptionRoot 'impact-proof'
$schema = Join-Path $root 'contracts/impact-replay-rollback-proof.schema.json'
$fixture = Join-Path $root 'fixtures/impact-replay-rollback-proof.valid.json'
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('dantesroleplay-impact-proof-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($temporary) | Out-Null

function Invoke-Ajv { param([string[]]$Arguments) $output = @(& $NpxCommand --yes ajv-cli@5.0.0 @Arguments 2>&1 | ForEach-Object { "$_" }); [pscustomobject]@{ exitCode = $LASTEXITCODE; output = $output } }
function Require-AjvSuccess { param([string[]]$Arguments, [string]$Message) $result = Invoke-Ajv $Arguments; if ($result.exitCode -ne 0) { throw "${Message}: $($result.output -join [Environment]::NewLine)" } }
function Write-Json { param($Value, [string]$Path) [IO.File]::WriteAllText($Path, (($Value | ConvertTo-Json -Depth 100) + "`n"), [Text.UTF8Encoding]::new($false)) }
function Assert-Rejected { param([scriptblock]$Action, [string]$Name) try { & $Action; throw "Expected rejection did not occur: $Name" } catch { if ($_.Exception.Message -like 'Expected rejection did not occur*') { throw }; return } }
function Resolve-ContainedPath { param([string]$Root, [string]$RelativePath)
    $rootFull = [IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $full = [IO.Path]::GetFullPath((Join-Path $rootFull $RelativePath))
    if ($full -ne $rootFull -and -not $full.StartsWith($rootFull + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw "Path escapes the adoption root: $RelativePath" }
    return $full
}
function Read-PinnedArtifact { param($Artifact)
    $path = Resolve-ContainedPath $adoptionRoot $Artifact.path
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Pinned artifact is missing: $($Artifact.path)" }
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToUpperInvariant() -cne $Artifact.sha256) { throw "Pinned artifact hash does not match: $($Artifact.path)" }
    return [pscustomobject]@{ path = $path; value = (Get-Content -Raw -LiteralPath $path | ConvertFrom-Json -Depth 100) }
}
function Assert-Chain { param($Proof)
    $mapping = Read-PinnedArtifact $Proof.mapping
    $allowlist = Read-PinnedArtifact $Proof.allowlist
    $result = Read-PinnedArtifact $Proof.result
    if ($mapping.value.candidateKey -cne $Proof.candidateKey) { throw 'Mapping candidate key does not match the proof.' }
    if ($allowlist.value.candidateKey -cne $Proof.candidateKey) { throw 'Allowlist candidate key does not match the proof.' }
    if ($allowlist.value.projectionMapping.candidateKey -cne $mapping.value.candidateKey -or $allowlist.value.projectionMapping.sha256 -cne $Proof.mapping.sha256 -or $allowlist.value.projectionMapping.manifestPath -cne $Proof.mapping.path) { throw 'Allowlist mapping reference does not match the pinned mapping.' }
    $roots = @($mapping.value.impactEvidence | ForEach-Object canonicalRoot)
    if (@($roots).Count -ne @($Proof.expected.impactRoots).Count -or @($roots | Where-Object { $Proof.expected.impactRoots -cnotcontains $_ }).Count -ne 0) { throw 'Pinned mapping impact roots do not match the proof.' }
    if (@($Proof.expected.focusedTests | Sort-Object -Unique).Count -ne 3) { throw 'The proof must require impact, replay, and rollback tests.' }
    return [pscustomobject]@{ mapping = $mapping; allowlist = $allowlist; result = $result; roots = $roots }
}
function Invoke-FocusedProofTests {
    $filter = 'FullyQualifiedName~ApplicationAdoptionProbeTests|FullyQualifiedName~ApplicationEcsEffectApplierTests'
    $output = @(& $DotnetCommand test 'DantesRoleplay.Tests/DantesRoleplay.Tests.csproj' --no-build --no-restore --nologo --filter $filter '--logger' 'console;verbosity=minimal' 2>&1 | ForEach-Object { "$_" })
    if ($LASTEXITCODE -ne 0) { throw "Focused generic proof tests failed: $($output -join [Environment]::NewLine)" }
    return 1
}

try {
    Require-AjvSuccess @('compile', '--spec=draft2020', '--strict=false', '-s', $schema) 'Proof schema compilation failed'
    Require-AjvSuccess @('validate', '--spec=draft2020', '--strict=false', '-s', $schema, '-d', $fixture) 'Valid proof fixture schema validation failed'
    $proof = Get-Content -Raw $fixture | ConvertFrom-Json -Depth 100
    $chain = Assert-Chain $proof
    $unknown = Get-Content -Raw $fixture | ConvertFrom-Json -Depth 100; $unknown | Add-Member -NotePropertyName unexpected -NotePropertyValue $true; $unknownPath = Join-Path $temporary 'unknown.json'; Write-Json $unknown $unknownPath
    if ((Invoke-Ajv @('validate', '--spec=draft2020', '--strict=false', '-s', $schema, '-d', $unknownPath)).exitCode -eq 0) { throw 'Unknown proof property was accepted.' }
    $stale = Get-Content -Raw $fixture | ConvertFrom-Json -Depth 100; $stale.mapping.sha256 = ('0' * 64); Assert-Rejected { Assert-Chain $stale } 'stale mapping hash'
    $mismatch = Get-Content -Raw $fixture | ConvertFrom-Json -Depth 100; $mismatch.expected.impactRoots[0] = 'component:mapping-fixture.ability-state@1#/str'; Assert-Rejected { Assert-Chain $mismatch } 'mismatched impact root'
    $focused = Invoke-FocusedProofTests
    $report = [ordered]@{ format = 'application-adoption-impact-replay-rollback-proof-report/v1'; candidateKey = $proof.candidateKey; mappingSha256 = $proof.mapping.sha256; allowlistSha256 = $proof.allowlist.sha256; resultSha256 = $proof.result.sha256; impactRoots = @($chain.roots); focusedProofTests = $focused; writes = 'none' }
    $first = ($report | ConvertTo-Json -Depth 100) + "`n"; $second = ($report | ConvertTo-Json -Depth 100) + "`n"; if ($first -cne $second) { throw 'Proof report is not deterministic.' }
    Write-Output $first
}
finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Recurse -Force } }
