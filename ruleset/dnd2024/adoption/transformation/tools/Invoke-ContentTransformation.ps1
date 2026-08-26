[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ManifestPath,
    [Parameter(Mandatory = $true)][string]$SourceRoot,
    [Parameter(Mandatory = $true)][string]$ReportPath,
    [string]$StagingDirectory,
    [switch]$DryRun,
    [string]$ExistingTargetRoot,
    [string]$NpxCommand = 'npx'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$toolRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$contractRoot = Join-Path $toolRoot 'contracts'
$manifestSchema = Join-Path $contractRoot 'content-transform-manifest.schema.json'
$candidateSchema = Join-Path $contractRoot 'staged-content-candidate.schema.json'

function Assert-NoReparsePoint {
    param([Parameter(Mandatory = $true)][string]$Root, [Parameter(Mandatory = $true)][string]$Path)
    $rootFull = [IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    if (Test-Path -LiteralPath $rootFull) {
        if (((Get-Item -Force -LiteralPath $rootFull).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Bounded root may not be a reparse point: $rootFull" }
    }
    $relative = [IO.Path]::GetRelativePath($rootFull, $Path)
    $cursor = $rootFull
    foreach ($segment in @($relative.Split([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar))) {
        if ($segment -eq '.' -or [string]::IsNullOrWhiteSpace($segment)) { continue }
        $cursor = Join-Path $cursor $segment
        if (Test-Path -LiteralPath $cursor) {
            if (((Get-Item -Force -LiteralPath $cursor).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Reparse point is not allowed beneath a bounded root: $cursor" }
        }
    }
}
function Normalize-RelativePath { param([Parameter(Mandatory = $true)][string]$Path) return ((@($Path.Split('/') | Where-Object { $_ -ne '' -and $_ -ne '.' }) -join '/').ToLowerInvariant()) }
function Resolve-ContainedPath {
    param([Parameter(Mandatory = $true)][string]$Root, [Parameter(Mandatory = $true)][string]$RelativePath)
    $rootFull = [IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $full = [IO.Path]::GetFullPath((Join-Path $rootFull $RelativePath))
    if ($full -ne $rootFull -and -not $full.StartsWith($rootFull + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw "Path escapes its root: $RelativePath" }
    Assert-NoReparsePoint -Root $rootFull -Path $full
    return $full
}
function Get-JsonPointerValue {
    param([Parameter(Mandatory = $true)]$Root, [Parameter(Mandatory = $true)][string]$Pointer)
    if ($Pointer -notmatch '^(?:/(?:[^~/]|~[01])*)+$') { throw "Invalid JSON Pointer: $Pointer" }
    $value = $Root
    foreach ($segment in $Pointer.TrimStart('/').Split('/')) {
        $name = $segment.Replace('~1', '/').Replace('~0', '~')
        if ($value -is [System.Array]) {
            if ($name -notmatch '^0$|^[1-9][0-9]*$' -or [int]$name -ge $value.Count) { throw "JSON Pointer does not resolve: $Pointer" }
            $value = $value[[int]$name]
        } else {
            $property = $value.PSObject.Properties[$name]
            if ($null -eq $property) { throw "JSON Pointer does not resolve: $Pointer" }
            $value = $property.Value
        }
    }
    return $value
}
function Invoke-AjvValidation {
    param([Parameter(Mandatory = $true)][string]$SchemaPath, [Parameter(Mandatory = $true)][string]$DocumentPath, [string]$ReferenceSchemaPath)
    if ($ReferenceSchemaPath) {
        $output = @(& $NpxCommand --yes ajv-cli@5.0.0 validate --spec=draft2020 --strict=false -s $SchemaPath -r $ReferenceSchemaPath -d $DocumentPath 2>&1 | ForEach-Object { "$_" })
    } else {
        $output = @(& $NpxCommand --yes ajv-cli@5.0.0 validate --spec=draft2020 --strict=false -s $SchemaPath -d $DocumentPath 2>&1 | ForEach-Object { "$_" })
    }
    if ($LASTEXITCODE -ne 0) { throw "Schema validation failed for ${DocumentPath}: $($output -join [Environment]::NewLine)" }
}
function ConvertTo-CanonicalJson { param($Value) return (($Value | ConvertTo-Json -Depth 100) + "`n") }
function Write-Utf8 { param([string]$Path, [string]$Text) [IO.Directory]::CreateDirectory((Split-Path -Parent $Path)) | Out-Null; [IO.File]::WriteAllText($Path, $Text, [Text.UTF8Encoding]::new($false)) }
function New-JsonTemporaryPath { return (Join-Path ([IO.Path]::GetTempPath()) ([guid]::NewGuid().ToString('N') + '.json')) }

$manifestFull = [IO.Path]::GetFullPath($ManifestPath)
$sourceRootFull = [IO.Path]::GetFullPath($SourceRoot)
if (-not (Test-Path -LiteralPath $sourceRootFull -PathType Container)) { throw "Source root is missing: $SourceRoot" }
Assert-NoReparsePoint -Root $sourceRootFull -Path $sourceRootFull
$reportFull = [IO.Path]::GetFullPath($ReportPath)
$manifest = Get-Content -Raw -LiteralPath $manifestFull | ConvertFrom-Json -Depth 100
Invoke-AjvValidation -SchemaPath $manifestSchema -DocumentPath $manifestFull
$manifestHash = (Get-FileHash -LiteralPath $manifestFull -Algorithm SHA256).Hash.ToUpperInvariant()
$errors = @(); $plans = @(); $seenKeys = @{}; $seenIds = @{}; $seenPaths = @{}

foreach ($entry in @($manifest.entries)) {
    try {
        $normalizedTargetPath = Normalize-RelativePath $entry.target.path
        foreach ($pair in @(@{ name = 'candidate key'; value = $entry.candidateKey; seen = $seenKeys }, @{ name = 'target id'; value = $entry.target.id; seen = $seenIds }, @{ name = 'target path'; value = $normalizedTargetPath; seen = $seenPaths })) {
            if ($pair.seen.ContainsKey($pair.value)) { throw "Duplicate $($pair.name): $($pair.value)" }
            $pair.seen[$pair.value] = $true
        }
        if ($entry.license.review.state -ne 'approved' -or $entry.license.disposition -notin @('first-party-recovery', 'approved-mit-software', 'approved-cc-by-srd-content')) { throw 'Only explicitly reviewed permitted license dispositions may be transformed.' }
        $sourcePath = Resolve-ContainedPath -Root $sourceRootFull -RelativePath $entry.source.path
        $schemaPath = Resolve-ContainedPath -Root $sourceRootFull -RelativePath $entry.target.schemaPath
        if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) { throw "Source file is missing: $($entry.source.path)" }
        if (-not (Test-Path -LiteralPath $schemaPath -PathType Leaf)) { throw "Target schema is missing: $($entry.target.schemaPath)" }
        $actualHash = (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash.ToUpperInvariant()
        if ($actualHash -cne $entry.source.sha256) { throw "Source hash does not match manifest: $($entry.source.path)" }
        $actualSchemaHash = (Get-FileHash -LiteralPath $schemaPath -Algorithm SHA256).Hash.ToUpperInvariant()
        if ($actualSchemaHash -cne $entry.target.schemaSha256) { throw "Target schema hash does not match manifest: $($entry.target.schemaPath)" }
        $actualToolHash = (Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash.ToUpperInvariant()
        if ($actualToolHash -cne $entry.transformation.toolSha256) { throw 'Transformation tool hash does not match manifest.' }
        $mapping = [ordered]@{ recordPointer = $entry.source.recordPointer; payloadPointer = $entry.target.payloadPointer; schemaPath = $entry.target.schemaPath; schemaSha256 = $entry.target.schemaSha256 }
        $mappingBytes = [Text.UTF8Encoding]::new($false).GetBytes((ConvertTo-CanonicalJson $mapping))
        $actualMappingHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($mappingBytes))
        if ($actualMappingHash -cne $entry.transformation.mappingSha256) { throw 'Transformation mapping hash does not match manifest.' }
        $sourceDocument = Get-Content -Raw -LiteralPath $sourcePath | ConvertFrom-Json -Depth 100
        $record = Get-JsonPointerValue -Root $sourceDocument -Pointer $entry.source.recordPointer
        $payload = Get-JsonPointerValue -Root $record -Pointer $entry.target.payloadPointer
        $payloadTemporary = New-JsonTemporaryPath
        try {
            Write-Utf8 -Path $payloadTemporary -Text (ConvertTo-CanonicalJson $payload)
            Invoke-AjvValidation -SchemaPath $schemaPath -DocumentPath $payloadTemporary
        } finally { if (Test-Path -LiteralPath $payloadTemporary) { Remove-Item -LiteralPath $payloadTemporary -Force } }
        $candidate = [ordered]@{ format = 'application-adoption-staged-content-candidate/v1'; candidateKey = $entry.candidateKey; target = [ordered]@{ kind = $entry.target.kind; id = $entry.target.id; path = $entry.target.path }; source = $entry.source; transformation = $entry.transformation; license = $entry.license; ruleset = $entry.ruleset; mapping = [ordered]@{ schemaPath = $entry.target.schemaPath; schemaSha256 = $entry.target.schemaSha256; payloadPointer = $entry.target.payloadPointer }; payload = $payload }
        $candidateTemporary = New-JsonTemporaryPath
        try {
            Write-Utf8 -Path $candidateTemporary -Text (ConvertTo-CanonicalJson $candidate)
            Invoke-AjvValidation -SchemaPath $candidateSchema -ReferenceSchemaPath $manifestSchema -DocumentPath $candidateTemporary
        } finally { if (Test-Path -LiteralPath $candidateTemporary) { Remove-Item -LiteralPath $candidateTemporary -Force } }
        $candidateJson = ConvertTo-CanonicalJson $candidate
        $candidateBytes = [Text.UTF8Encoding]::new($false).GetBytes($candidateJson)
        $candidateHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($candidateBytes))
        if ($ExistingTargetRoot) { $existingPath = Resolve-ContainedPath -Root $ExistingTargetRoot -RelativePath $entry.target.path; if (Test-Path -LiteralPath $existingPath -PathType Leaf) { throw "Existing target collision: $($entry.target.path)" } }
        $plans += [pscustomobject]@{ entry = $entry; candidate = $candidate; json = $candidateJson; sha256 = $candidateHash }
    } catch { $errors += [ordered]@{ candidateKey = $entry.candidateKey; message = $_.Exception.Message } }
}

if ($errors.Count -eq 0 -and -not $DryRun) {
    if ([string]::IsNullOrWhiteSpace($StagingDirectory)) {
        $errors += [ordered]@{ candidateKey = 'batch'; message = 'StagingDirectory is required unless DryRun is specified.' }
    } else {
        $stagingRoot = [IO.Path]::GetFullPath($StagingDirectory)
        try {
            foreach ($plan in $plans) {
                $targetPath = Resolve-ContainedPath -Root $stagingRoot -RelativePath $plan.entry.target.path
                if (Test-Path -LiteralPath $targetPath -PathType Leaf) { throw "Staging target collision: $($plan.entry.target.path)" }
            }
        } catch { $errors += [ordered]@{ candidateKey = 'batch'; message = $_.Exception.Message } }
        if ($errors.Count -eq 0) {
            $written = @()
            try {
                foreach ($plan in $plans) {
                    $targetPath = Resolve-ContainedPath -Root $stagingRoot -RelativePath $plan.entry.target.path
                    Write-Utf8 -Path $targetPath -Text $plan.json
                    $written += $targetPath
                }
            } catch {
                foreach ($path in $written) { if (Test-Path -LiteralPath $path -PathType Leaf) { Remove-Item -LiteralPath $path -Force } }
                $errors += [ordered]@{ candidateKey = 'batch'; message = "Staging write failed: $($_.Exception.Message)" }
            }
        }
    }
}
$status = if ($errors.Count -eq 0) { 'ready' } else { 'rejected' }
$report = [ordered]@{ format = 'application-adoption-content-transform-report/v1'; manifestSha256 = $manifestHash; batch = $manifest.batch; dryRun = [bool]$DryRun; status = $status; candidates = @($plans | ForEach-Object { [ordered]@{ candidateKey = $_.entry.candidateKey; target = $_.entry.target; sha256 = $_.sha256 } }); errors = $errors }
Write-Utf8 -Path $reportFull -Text (ConvertTo-CanonicalJson $report)
Write-Output (ConvertTo-CanonicalJson $report)
if ($errors.Count -gt 0) { exit 2 }
