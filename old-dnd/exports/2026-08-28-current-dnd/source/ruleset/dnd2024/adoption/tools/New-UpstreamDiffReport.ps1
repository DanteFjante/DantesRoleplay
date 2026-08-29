[CmdletBinding()]
param(
    [string]$LockPath = (Join-Path $PSScriptRoot '../donor-lock.json'),
    [string]$CandidateMapPath,
    [string]$ReportPath,
    [string]$GitCommand = 'git',
    [ValidateRange(1, 5000)][int]$MaxChangedFiles = 500
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$lockFull = (Resolve-Path -LiteralPath $LockPath).Path
$lockHashBefore = (Get-FileHash -LiteralPath $lockFull -Algorithm SHA256).Hash.ToUpperInvariant()
$lock = Get-Content -Raw -LiteralPath $lockFull | ConvertFrom-Json -Depth 100
if ($lock.format -cne 'dnd-code-adoption-donor-lock/v1' -or
    $lock.policy.automaticActivation -ne $false -or
    $lock.policy.productionDependency -ne $false -or
    $lock.policy.floatingRefsAllowed -ne $false) {
    throw 'The donor lock policy is not safe for review-only comparison.'
}
$sources = @($lock.sources)
if ($sources.Count -eq 0 -or @($sources.key | Sort-Object -Unique).Count -ne $sources.Count) {
    throw 'The donor lock must contain uniquely keyed sources.'
}

$candidateMap = $null
if ($CandidateMapPath) {
    $candidateMap = Get-Content -Raw -LiteralPath (Resolve-Path -LiteralPath $CandidateMapPath) |
        ConvertFrom-Json -Depth 20
    if ($candidateMap.format -cne 'dnd2024-upstream-candidates/v1') {
        throw 'The candidate map format is unsupported.'
    }
}

function Invoke-GitResult {
    param([string[]]$Arguments)
    $lines = @(& $GitCommand @Arguments 2>&1 | ForEach-Object { "$_" })
    [pscustomobject]@{ ExitCode = $LASTEXITCODE; Lines = $lines }
}
function Invoke-GitRequired {
    param([string[]]$Arguments, [string]$Failure)
    $result = Invoke-GitResult $Arguments
    if ($result.ExitCode -ne 0) {
        throw "${Failure}: $($result.Lines -join [Environment]::NewLine)"
    }
    return @($result.Lines)
}
function Resolve-Candidate {
    param($Source)
    if ($null -ne $candidateMap) {
        $property = $candidateMap.candidates.PSObject.Properties[$Source.key]
        if ($null -eq $property) { throw "Candidate map is missing '$($Source.key)'." }
        return "$($property.Value)"
    }
    if ("$($Source.branchEvidence)" -notmatch '^[A-Za-z0-9][A-Za-z0-9._/-]{0,199}$' -or
        "$($Source.branchEvidence)" -match '(^|/)\.\.(/|$)') {
        throw "Unsafe branch evidence for '$($Source.key)'."
    }
    $ref = "refs/heads/$($Source.branchEvidence)"
    $lines = Invoke-GitRequired @('ls-remote', '--exit-code', "$($Source.repository)", $ref) `
        "Could not resolve $ref for '$($Source.key)'"
    $matches = @($lines | Where-Object { $_ -match '^([0-9a-f]{40})\s+refs/heads/' })
    if ($matches.Count -ne 1) { throw "Remote ref '$ref' was ambiguous for '$($Source.key)'." }
    return ($matches[0] -split '\s+')[0]
}
function Get-BlobId {
    param([string]$RepositoryPath, [string]$Commit, [string]$Path, [bool]$Required)
    $result = Invoke-GitResult @('-C', $RepositoryPath, 'rev-parse', "$Commit`:$Path")
    if ($result.ExitCode -ne 0) {
        if ($Required) { throw "Required fingerprint is missing at $Commit`: $Path" }
        return $null
    }
    $value = "$($result.Lines[0])".Trim()
    if ($value -notmatch '^[0-9a-f]{40}$') { throw "Invalid blob identity for '$Path'." }
    return $value
}

$osTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd(
    [IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
$temporary = Join-Path $osTemp ('dantesroleplay-dnd-upstream-diff-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($temporary) | Out-Null
$sourceReports = [Collections.Generic.List[object]]::new()
try {
    foreach ($source in $sources) {
        $pinned = "$($source.commit)"
        $candidate = Resolve-Candidate $source
        if ($pinned -notmatch '^[0-9a-f]{40}$' -or $candidate -notmatch '^[0-9a-f]{40}$') {
            throw "Source '$($source.key)' does not resolve to exact commits."
        }
        $repositoryPath = Join-Path $temporary "$($source.key)"
        [IO.Directory]::CreateDirectory($repositoryPath) | Out-Null
        Invoke-GitRequired @('-C', $repositoryPath, 'init', '--quiet') "Git init failed for '$($source.key)'" | Out-Null
        Invoke-GitRequired @('-C', $repositoryPath, 'remote', 'add', 'origin', "$($source.repository)") "Git remote failed for '$($source.key)'" | Out-Null
        Invoke-GitRequired @('-C', $repositoryPath, 'fetch', '--quiet', '--no-tags', '--depth=1', 'origin', $pinned) "Pinned fetch failed for '$($source.key)'" | Out-Null
        if ($candidate -cne $pinned) {
            Invoke-GitRequired @('-C', $repositoryPath, 'fetch', '--quiet', '--no-tags', '--depth=1', 'origin', $candidate) "Candidate fetch failed for '$($source.key)'" | Out-Null
        }
        Invoke-GitRequired @('-C', $repositoryPath, 'cat-file', '-e', "$pinned^{commit}") "Pinned object is not a commit for '$($source.key)'" | Out-Null
        Invoke-GitRequired @('-C', $repositoryPath, 'cat-file', '-e', "$candidate^{commit}") "Candidate object is not a commit for '$($source.key)'" | Out-Null
        $pinnedTree = (@(Invoke-GitRequired @('-C', $repositoryPath, 'rev-parse', "$pinned^{tree}") 'Pinned tree lookup failed'))[0].Trim()
        $candidateTree = (@(Invoke-GitRequired @('-C', $repositoryPath, 'rev-parse', "$candidate^{tree}") 'Candidate tree lookup failed'))[0].Trim()
        $changed = @()
        if ($pinnedTree -cne $candidateTree) {
            $changed = @(Invoke-GitRequired @('-C', $repositoryPath, 'diff', '--name-status', '--no-renames', $pinned, $candidate, '--') 'Tree diff failed' |
                ForEach-Object {
                    $parts = $_ -split "`t", 2
                    if ($parts.Count -ne 2) { throw "Unexpected Git diff row: $_" }
                    [ordered]@{ status = $parts[0]; path = $parts[1] }
                } | Sort-Object path, status)
        }
        $fingerprints = @($source.fingerprints | ForEach-Object {
            $pinnedBlob = Get-BlobId $repositoryPath $pinned "$($_.path)" ([bool]$_.required)
            $candidateBlob = Get-BlobId $repositoryPath $candidate "$($_.path)" ([bool]$_.required)
            [ordered]@{
                path = "$($_.path)"
                required = [bool]$_.required
                pinnedBlob = $pinnedBlob
                candidateBlob = $candidateBlob
                status = if ($pinnedBlob -ceq $candidateBlob) { 'unchanged' } else { 'changed' }
            }
        } | Sort-Object path)
        $sourceReports.Add([ordered]@{
            key = "$($source.key)"
            role = "$($source.role)"
            repository = "$($source.repository)"
            branchEvidence = "$($source.branchEvidence)"
            pinnedCommit = $pinned
            candidateCommit = $candidate
            pinnedTree = $pinnedTree
            candidateTree = $candidateTree
            status = if ($pinnedTree -ceq $candidateTree) { 'unchanged' } else { 'review-required' }
            changedFileCount = $changed.Count
            changedFilesTruncated = $changed.Count -gt $MaxChangedFiles
            changedFiles = @($changed | Select-Object -First $MaxChangedFiles)
            fingerprints = $fingerprints
        })
    }
    if ((Get-FileHash -LiteralPath $lockFull -Algorithm SHA256).Hash.ToUpperInvariant() -cne $lockHashBefore) {
        throw 'The donor lock changed during comparison.'
    }
    $report = [ordered]@{
        format = 'dnd2024-upstream-diff-report/v1'
        lockSha256 = $lockHashBefore
        reviewRequired = @($sourceReports | Where-Object status -eq 'review-required').Count -gt 0
        automaticActivation = $false
        lockChanged = $false
        runtimeWrites = 'none'
        sources = @($sourceReports)
    }
    $json = ($report | ConvertTo-Json -Depth 20) + "`n"
    if ($ReportPath) {
        $outputFull = [IO.Path]::GetFullPath($ReportPath, (Get-Location).Path)
        if ($outputFull -ieq $lockFull) { throw 'The report path cannot be the donor lock.' }
        $parent = Split-Path -Parent $outputFull
        if (-not (Test-Path -LiteralPath $parent -PathType Container)) {
            [IO.Directory]::CreateDirectory($parent) | Out-Null
        }
        [IO.File]::WriteAllText($outputFull, $json, [Text.UTF8Encoding]::new($false))
    }
    Write-Output $json
}
finally {
    if (Test-Path -LiteralPath $temporary) {
        $resolved = [IO.Path]::GetFullPath($temporary)
        if (-not $resolved.StartsWith($osTemp + [IO.Path]::DirectorySeparatorChar,
                [StringComparison]::OrdinalIgnoreCase) -or
            [IO.Path]::GetFileName($resolved) -notlike 'dantesroleplay-dnd-upstream-diff-*') {
            throw "Refusing unsafe temporary cleanup: $resolved"
        }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
