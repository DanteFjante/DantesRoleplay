[CmdletBinding()]
param([string]$GitCommand = 'git', [string]$PwshCommand = 'pwsh')

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$tool = Join-Path $PSScriptRoot 'New-UpstreamDiffReport.ps1'
$osTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar)
$temporary = Join-Path $osTemp ('dantesroleplay-upstream-diff-test-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($temporary) | Out-Null
function Invoke-FixtureGit([string[]]$Arguments) {
    $output = @(& $GitCommand @Arguments 2>&1 | ForEach-Object { "$_" })
    if ($LASTEXITCODE -ne 0) { throw "Git fixture command failed: $($output -join [Environment]::NewLine)" }
    return @($output)
}
function WriteUtf8([string]$Path, [string]$Value) {
    [IO.File]::WriteAllText($Path, $Value, [Text.UTF8Encoding]::new($false))
}
function ExpectFailure([scriptblock]$Action, [string]$Name) {
    try { & $Action; throw "Expected failure did not occur: $Name" }
    catch { if ($_.Exception.Message -like 'Expected failure did not occur*') { throw } }
}
try {
    $source = Join-Path $temporary 'source'
    [IO.Directory]::CreateDirectory($source) | Out-Null
    Invoke-FixtureGit @('-C', $source, 'init', '--quiet', '--initial-branch=main') | Out-Null
    Invoke-FixtureGit @('-C', $source, 'config', 'user.email', 'slice12@example.invalid') | Out-Null
    Invoke-FixtureGit @('-C', $source, 'config', 'user.name', 'Slice 12') | Out-Null
    WriteUtf8 (Join-Path $source 'LICENSE') "fixture license`n"
    WriteUtf8 (Join-Path $source 'tracked.txt') "one`n"
    Invoke-FixtureGit @('-C', $source, 'add', '--', 'LICENSE', 'tracked.txt') | Out-Null
    Invoke-FixtureGit @('-C', $source, 'commit', '--quiet', '-m', 'pinned') | Out-Null
    $pinned = (@(Invoke-FixtureGit @('-C', $source, 'rev-parse', 'HEAD')))[0].Trim()
    WriteUtf8 (Join-Path $source 'tracked.txt') "two`n"
    WriteUtf8 (Join-Path $source 'added.txt') "added`n"
    Invoke-FixtureGit @('-C', $source, 'add', '--', 'tracked.txt', 'added.txt') | Out-Null
    Invoke-FixtureGit @('-C', $source, 'commit', '--quiet', '-m', 'candidate') | Out-Null
    $candidate = (@(Invoke-FixtureGit @('-C', $source, 'rev-parse', 'HEAD')))[0].Trim()
    $lockPath = Join-Path $temporary 'donor-lock.json'
    $lock = [ordered]@{
        format = 'dnd-code-adoption-donor-lock/v1'
        policy = [ordered]@{ checkoutKind='unique-os-temp-child'; temporaryDirectoryPrefix='fixture-'; automaticActivation=$false; productionDependency=$false; floatingRefsAllowed=$false }
        sources = @([ordered]@{ key='fixture'; role='engineering-reference-only'; repository=$source; branchEvidence='main'; commit=$pinned; initializeSubmodules=$false; executionMode='fingerprint-only'; commands=$null; fingerprints=@([ordered]@{path='LICENSE';required=$true},[ordered]@{path='tracked.txt';required=$true}) })
    }
    WriteUtf8 $lockPath (($lock | ConvertTo-Json -Depth 20) + "`n")
    $lockHash = (Get-FileHash -LiteralPath $lockPath -Algorithm SHA256).Hash
    $candidatePath = Join-Path $temporary 'candidates.json'
    WriteUtf8 $candidatePath (([ordered]@{format='dnd2024-upstream-candidates/v1';candidates=[ordered]@{fixture=$candidate}} | ConvertTo-Json -Depth 5) + "`n")
    $firstPath = Join-Path $temporary 'first.report.json'
    & $tool -LockPath $lockPath -CandidateMapPath $candidatePath -ReportPath $firstPath -GitCommand $GitCommand | Out-Null
    $first = Get-Content -Raw -LiteralPath $firstPath | ConvertFrom-Json -Depth 20
    if ($first.reviewRequired -ne $true -or $first.automaticActivation -ne $false -or $first.lockChanged -ne $false -or
        $first.sources[0].status -cne 'review-required' -or $first.sources[0].changedFileCount -ne 2 -or
        @($first.sources[0].fingerprints | Where-Object status -eq 'changed').Count -ne 1) {
        throw 'Changed-candidate report did not preserve the review-only contract.'
    }
    $secondPath = Join-Path $temporary 'second.report.json'
    & $tool -LockPath $lockPath -CandidateMapPath $candidatePath -ReportPath $secondPath -GitCommand $GitCommand | Out-Null
    if ((Get-FileHash $firstPath -Algorithm SHA256).Hash -cne (Get-FileHash $secondPath -Algorithm SHA256).Hash) {
        throw 'Repeated upstream reports were not deterministic.'
    }
    WriteUtf8 $candidatePath (([ordered]@{format='dnd2024-upstream-candidates/v1';candidates=[ordered]@{fixture=$pinned}} | ConvertTo-Json -Depth 5) + "`n")
    $same = & $tool -LockPath $lockPath -CandidateMapPath $candidatePath -GitCommand $GitCommand | ConvertFrom-Json -Depth 20
    if ($same.reviewRequired -ne $false -or $same.sources[0].status -cne 'unchanged' -or $same.sources[0].changedFileCount -ne 0) {
        throw 'Same-commit comparison did not report unchanged.'
    }
    WriteUtf8 $candidatePath (([ordered]@{format='dnd2024-upstream-candidates/v1';candidates=[ordered]@{fixture='main'}} | ConvertTo-Json -Depth 5) + "`n")
    ExpectFailure { & $tool -LockPath $lockPath -CandidateMapPath $candidatePath -GitCommand $GitCommand | Out-Null } 'floating candidate'
    WriteUtf8 $candidatePath (([ordered]@{format='dnd2024-upstream-candidates/v1';candidates=[ordered]@{fixture=$candidate}} | ConvertTo-Json -Depth 5) + "`n")
    ExpectFailure { & $tool -LockPath $lockPath -CandidateMapPath $candidatePath -ReportPath $lockPath -GitCommand $GitCommand | Out-Null } 'lock overwrite'
    if ((Get-FileHash -LiteralPath $lockPath -Algorithm SHA256).Hash -cne $lockHash) { throw 'Workflow changed the lock.' }
    [ordered]@{ format='dnd2024-upstream-diff-workflow-test/v1'; changedReport='review-required'; unchangedReport='unchanged'; deterministicReports=1; negativeCases=2; lockChanged=$false; automaticActivation=$false } | ConvertTo-Json
}
finally {
    if (Test-Path -LiteralPath $temporary) {
        $resolved = [IO.Path]::GetFullPath($temporary)
        if (-not $resolved.StartsWith($osTemp + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
            [IO.Path]::GetFileName($resolved) -notlike 'dantesroleplay-upstream-diff-test-*') { throw "Unsafe test cleanup: $resolved" }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
