[CmdletBinding()]
param(
    [string]$LockPath = (Join-Path $PSScriptRoot '../donor-lock.json'),
    [string]$NoticesPath = (Join-Path $PSScriptRoot '../THIRD-PARTY-NOTICES.md')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$lockFull = (Resolve-Path -LiteralPath $LockPath).Path
$noticesFull = (Resolve-Path -LiteralPath $NoticesPath).Path
$lock = Get-Content -Raw -LiteralPath $lockFull | ConvertFrom-Json -Depth 100
$notices = Get-Content -Raw -LiteralPath $noticesFull
if ($lock.format -cne 'dnd-code-adoption-donor-lock/v1') { throw 'Unexpected donor lock format.' }
if ($lock.policy.automaticActivation -ne $false -or $lock.policy.productionDependency -ne $false -or
    $lock.policy.floatingRefsAllowed -ne $false -or $lock.policy.checkoutKind -cne 'unique-os-temp-child') {
    throw 'Donor lock safety policy drifted.'
}
$sources = @($lock.sources)
if (@($sources.key | Sort-Object -Unique).Count -ne $sources.Count) { throw 'Duplicate donor source key.' }
foreach ($source in $sources) {
    if ("$($source.commit)" -notmatch '^[0-9a-f]{40}$') { throw "Nonexact donor commit: $($source.key)" }
    if ("$($source.repository)" -notmatch '^https://github\.com/.+\.git$') { throw "Unexpected donor repository: $($source.key)" }
    if ([string]::IsNullOrWhiteSpace("$($source.branchEvidence)")) { throw "Missing branch evidence: $($source.key)" }
}
$engine = $sources | Where-Object key -ceq 'dnd-srd-engine'
$foundry = $sources | Where-Object key -ceq 'foundry-dnd5e'
if (@($engine).Count -ne 1 -or $engine.role -cne 'primary-engineering-donor' -or
    $engine.executionMode -cne 'install-build-test') { throw 'MIT donor lock entry drifted.' }
if (@($foundry).Count -ne 1 -or $foundry.role -cne 'engineering-reference-only' -or
    $foundry.executionMode -cne 'fingerprint-only' -or $null -ne $foundry.commands) {
    throw 'Foundry reference-only boundary drifted.'
}
$requiredNoticeText = @(
    'https://www.dndbeyond.com/srd',
    'https://creativecommons.org/licenses/by/4.0/legalcode',
    "$($engine.commit)",
    'Copyright (c) 2026 Greg Carr',
    'Permission is hereby granted, free of charge',
    'No SRD prose is reproduced',
    'no Foundry code or data is copied'
)
foreach ($required in $requiredNoticeText) {
    if (-not $notices.Contains($required, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Required attribution text is missing: $required"
    }
}

$adoptionRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$provenanceFiles = @(Get-ChildItem -LiteralPath $adoptionRoot -Recurse -File -Filter '*.provenance.json')
function Test-FoundryBoundary {
    param($Value, [string]$Path)
    if ($null -eq $Value) { return }
    if ($Value -is [Management.Automation.PSCustomObject]) {
        $sourceProperty = $Value.PSObject.Properties['source']
        if ($null -ne $sourceProperty -and $null -ne $sourceProperty.Value -and
            "$($sourceProperty.Value.sourceKey)" -ceq 'foundry-dnd5e') {
            if ("$($Value.license.disposition)" -cne 'reference-only' -or "$($Value.status)" -ceq 'accepted') {
                throw "Foundry provenance exceeds reference-only scope: $Path"
            }
        }
        foreach ($property in $Value.PSObject.Properties) { Test-FoundryBoundary $property.Value $Path }
    }
    elseif ($Value -is [Collections.IEnumerable] -and $Value -isnot [string]) {
        foreach ($item in $Value) { Test-FoundryBoundary $item $Path }
    }
}
foreach ($file in $provenanceFiles) {
    Test-FoundryBoundary (Get-Content -Raw -LiteralPath $file.FullName | ConvertFrom-Json -Depth 100) $file.FullName
}

[ordered]@{
    format = 'dnd2024-adoption-attribution-audit/v1'
    status = 'passed'
    lockSha256 = (Get-FileHash -LiteralPath $lockFull -Algorithm SHA256).Hash.ToUpperInvariant()
    sourceCount = $sources.Count
    provenanceFiles = $provenanceFiles.Count
    exactPins = $true
    foundryReferenceOnly = $true
    automaticActivation = $false
} | ConvertTo-Json -Depth 5
