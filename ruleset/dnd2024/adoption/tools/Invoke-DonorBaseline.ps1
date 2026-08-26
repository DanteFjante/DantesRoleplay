[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$LockPath,

    [Parameter(Mandatory = $true)]
    [string]$EvidencePath,

    [string]$GitExecutable = "git",
    [string]$NodeExecutable = "node",
    [string]$NpmCommand = "npm",
    [string]$TemporaryParent = [System.IO.Path]::GetTempPath(),
    [switch]$KeepCheckout
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Invoke-Captured {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][string]$WorkingDirectory,
        [int]$TailLines = 80
    )

    $started = [DateTimeOffset]::UtcNow
    Push-Location -LiteralPath $WorkingDirectory
    try {
        $lines = @(& $FilePath @Arguments 2>&1 | ForEach-Object { "$_" })
        $exitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }

    $duration = [DateTimeOffset]::UtcNow - $started
    [pscustomobject]@{
        file = $FilePath
        arguments = $Arguments
        exitCode = $exitCode
        durationSeconds = [Math]::Round($duration.TotalSeconds, 3)
        outputTail = @($lines | Select-Object -Last $TailLines)
    }
}

function Assert-Succeeded {
    param(
        [Parameter(Mandatory = $true)]$Command,
        [Parameter(Mandatory = $true)][string]$Description
    )
    if ($Command.exitCode -ne 0) {
        throw "$Description failed with exit code $($Command.exitCode)."
    }
}

function Invoke-Git {
    param(
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][string]$WorkingDirectory,
        [Parameter(Mandatory = $true)][string]$Description
    )
    $command = Invoke-Captured -FilePath $GitExecutable -Arguments $Arguments -WorkingDirectory $WorkingDirectory
    Assert-Succeeded -Command $command -Description $Description
    $command
}

function Get-Sha256 {
    param([Parameter(Mandatory = $true)][string]$Path)
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}

function Get-SingleLine {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][string]$WorkingDirectory,
        [Parameter(Mandatory = $true)][string]$Description
    )
    $command = Invoke-Captured -FilePath $FilePath -Arguments $Arguments -WorkingDirectory $WorkingDirectory -TailLines 20
    Assert-Succeeded -Command $command -Description $Description
    if ($command.outputTail.Count -eq 0) {
        throw "$Description returned no output."
    }
    "$($command.outputTail[-1])".Trim()
}

function Get-TestSummary {
    param([Parameter(Mandatory = $true)]$Command)

    $plain = (($Command.outputTail -join "`n") -replace "`e\[[0-9;]*[A-Za-z]", "")
    $match = [regex]::Match(
        $plain,
        "(?m)^\s*Tests\s+(?:(?<failed>\d+)\s+failed(?:\s*\|\s*)?)?(?:(?<passed>\d+)\s+passed(?:\s*\|\s*)?)?(?:(?<skipped>\d+)\s+skipped)?")
    if (-not $match.Success -or -not $match.Groups["passed"].Success) {
        throw "The donor test command completed without a parseable Vitest Tests summary."
    }

    $failed = if ($match.Groups["failed"].Success) { [int]$match.Groups["failed"].Value } else { 0 }
    $skipped = if ($match.Groups["skipped"].Success) { [int]$match.Groups["skipped"].Value } else { 0 }
    $failures = @(
        $plain -split "`n" |
            Where-Object { $_ -match "^\s*(FAIL|>)\s+" } |
            ForEach-Object { $_.Trim() } |
            Select-Object -Unique
    )

    [pscustomobject]@{
        passed = [int]$match.Groups["passed"].Value
        failed = $failed
        skipped = $skipped
        failingEvidence = $failures
    }
}

$resolvedLock = (Resolve-Path -LiteralPath $LockPath).Path
$lock = Get-Content -Raw -LiteralPath $resolvedLock | ConvertFrom-Json -Depth 100
if ($lock.format -ne "dnd-code-adoption-donor-lock/v1") {
    throw "Unsupported donor lock format."
}
if ($lock.policy.floatingRefsAllowed -ne $false -or $lock.policy.automaticActivation -ne $false) {
    throw "The donor lock must prohibit floating references and automatic activation."
}
if ($lock.sources.Count -ne 2) {
    throw "The Slice 0A donor lock must contain exactly two approved sources."
}

$resolvedTemporaryParent = [System.IO.Path]::GetFullPath($TemporaryParent).TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar)
$osTemporary = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar)
if (-not [string]::Equals($resolvedTemporaryParent, $osTemporary, [StringComparison]::OrdinalIgnoreCase)) {
    throw "TemporaryParent must resolve to the operating-system temporary directory."
}

$prefix = "$($lock.policy.temporaryDirectoryPrefix)"
if ([string]::IsNullOrWhiteSpace($prefix) -or $prefix.IndexOfAny([System.IO.Path]::GetInvalidFileNameChars()) -ge 0) {
    throw "The temporary directory prefix is invalid."
}
$checkoutRoot = Join-Path $resolvedTemporaryParent ($prefix + [Guid]::NewGuid().ToString("N"))
$resolvedEvidence = [System.IO.Path]::GetFullPath($EvidencePath)
$evidenceParent = Split-Path -Parent $resolvedEvidence
if ([string]::IsNullOrWhiteSpace($evidenceParent) -or -not (Test-Path -LiteralPath $evidenceParent -PathType Container)) {
    throw "EvidencePath must have an existing parent directory."
}
if ($resolvedEvidence.StartsWith($checkoutRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw "EvidencePath must be outside the disposable checkout."
}

$result = [ordered]@{
    format = "dnd-code-adoption-donor-baseline/v1"
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    lockSha256 = Get-Sha256 -Path $resolvedLock
    tools = [ordered]@{}
    sources = @()
    baselineStatus = "incomplete"
    cleanup = [ordered]@{
        kept = [bool]$KeepCheckout
        deleted = $false
    }
}

try {
    New-Item -ItemType Directory -Path $checkoutRoot -ErrorAction Stop | Out-Null
    $result.tools.git = Get-SingleLine -FilePath $GitExecutable -Arguments @("--version") -WorkingDirectory $checkoutRoot -Description "git version"
    $result.tools.node = Get-SingleLine -FilePath $NodeExecutable -Arguments @("--version") -WorkingDirectory $checkoutRoot -Description "node version"
    $result.tools.npm = Get-SingleLine -FilePath $NpmCommand -Arguments @("--version") -WorkingDirectory $checkoutRoot -Description "npm version"

    foreach ($source in $lock.sources) {
        if ("$($source.commit)" -notmatch "^[0-9a-f]{40}$") {
            throw "Source $($source.key) does not have an exact lowercase 40-character commit."
        }
        $sourcePath = Join-Path $checkoutRoot "$($source.key)"
        New-Item -ItemType Directory -Path $sourcePath -ErrorAction Stop | Out-Null
        Invoke-Git -Arguments @("init", "--quiet") -WorkingDirectory $sourcePath -Description "git init for $($source.key)" | Out-Null
        Invoke-Git -Arguments @("remote", "add", "origin", "$($source.repository)") -WorkingDirectory $sourcePath -Description "git remote for $($source.key)" | Out-Null
        Invoke-Git -Arguments @("fetch", "--quiet", "--depth", "1", "origin", "$($source.commit)") -WorkingDirectory $sourcePath -Description "git fetch for $($source.key)" | Out-Null
        Invoke-Git -Arguments @("checkout", "--quiet", "--detach", "FETCH_HEAD") -WorkingDirectory $sourcePath -Description "git checkout for $($source.key)" | Out-Null

        $head = Get-SingleLine -FilePath $GitExecutable -Arguments @("rev-parse", "HEAD") -WorkingDirectory $sourcePath -Description "HEAD for $($source.key)"
        if ($head -ne "$($source.commit)") {
            throw "Source $($source.key) checked out $head instead of $($source.commit)."
        }
        $tree = Get-SingleLine -FilePath $GitExecutable -Arguments @("rev-parse", "HEAD^{tree}") -WorkingDirectory $sourcePath -Description "tree for $($source.key)"

        $submodules = @()
        if ($source.initializeSubmodules -eq $true) {
            Invoke-Git -Arguments @("submodule", "update", "--init", "--recursive", "--depth", "1") -WorkingDirectory $sourcePath -Description "submodules for $($source.key)" | Out-Null
            $submoduleCommand = Invoke-Captured -FilePath $GitExecutable -Arguments @("submodule", "status", "--recursive") -WorkingDirectory $sourcePath -TailLines 100
            Assert-Succeeded -Command $submoduleCommand -Description "submodule status for $($source.key)"
            $submodules = @($submoduleCommand.outputTail)
        }

        $fingerprints = @()
        foreach ($entry in $source.fingerprints) {
            $relative = "$($entry.path)"
            $fullPath = [System.IO.Path]::GetFullPath((Join-Path $sourcePath $relative))
            if (-not $fullPath.StartsWith($sourcePath + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
                throw "Fingerprint path escapes source root: $relative"
            }
            if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
                if ($entry.required -eq $true) {
                    throw "Required fingerprint file is missing for $($source.key): $relative"
                }
                continue
            }
            $fingerprints += [pscustomobject]@{
                path = $relative.Replace("\", "/")
                sha256 = Get-Sha256 -Path $fullPath
                length = (Get-Item -LiteralPath $fullPath).Length
            }
        }

        $packageVersion = $null
        $packagePath = Join-Path $sourcePath "package.json"
        if (Test-Path -LiteralPath $packagePath -PathType Leaf) {
            $package = Get-Content -Raw -LiteralPath $packagePath | ConvertFrom-Json -Depth 100
            if ($null -ne $package.PSObject.Properties["version"]) {
                $packageVersion = "$($package.version)"
            }
        }

        $execution = [ordered]@{
            mode = "$($source.executionMode)"
            install = $null
            build = $null
            test = $null
            testSummary = $null
        }
        if ($source.executionMode -eq "install-build-test") {
            $execution.install = Invoke-Captured -FilePath $NpmCommand -Arguments @($source.commands.install) -WorkingDirectory $sourcePath -TailLines 100
            Assert-Succeeded -Command $execution.install -Description "npm install for $($source.key)"
            $execution.build = Invoke-Captured -FilePath $NpmCommand -Arguments @($source.commands.build) -WorkingDirectory $sourcePath -TailLines 120
            Assert-Succeeded -Command $execution.build -Description "npm build for $($source.key)"
            $execution.test = Invoke-Captured -FilePath $NpmCommand -Arguments @($source.commands.test) -WorkingDirectory $sourcePath -TailLines 320
            $execution.testSummary = Get-TestSummary -Command $execution.test
        }
        elseif ($source.executionMode -ne "fingerprint-only") {
            throw "Unsupported execution mode for $($source.key): $($source.executionMode)"
        }

        $result.sources += [pscustomobject]@{
            key = "$($source.key)"
            role = "$($source.role)"
            repository = "$($source.repository)"
            branchEvidence = "$($source.branchEvidence)"
            commit = $head
            tree = $tree
            packageVersion = $packageVersion
            submodules = $submodules
            fingerprints = $fingerprints
            execution = $execution
        }
    }

    $donor = @($result.sources | Where-Object { $_.key -eq "dnd-srd-engine" })
    if ($donor.Count -ne 1 -or $null -eq $donor[0].execution.testSummary) {
        throw "The primary donor did not produce exactly one test summary."
    }
    $result.baselineStatus = if ($donor[0].execution.testSummary.failed -eq 0) {
        "reproduced-green"
    }
    else {
        "reproduced-with-test-failures"
    }
}
finally {
    if (-not $KeepCheckout -and (Test-Path -LiteralPath $checkoutRoot)) {
        $resolvedCheckout = (Resolve-Path -LiteralPath $checkoutRoot).Path
        $expectedParent = Split-Path -Parent $resolvedCheckout
        $leaf = Split-Path -Leaf $resolvedCheckout
        if (-not [string]::Equals($expectedParent, $resolvedTemporaryParent, [StringComparison]::OrdinalIgnoreCase) -or
            -not $leaf.StartsWith($prefix, [StringComparison]::Ordinal)) {
            throw "Refusing to delete an unverified checkout path: $resolvedCheckout"
        }
        Remove-Item -LiteralPath $resolvedCheckout -Recurse -Force
        $result.cleanup.deleted = -not (Test-Path -LiteralPath $resolvedCheckout)
    }
}

$json = $result | ConvertTo-Json -Depth 100
[System.IO.File]::WriteAllText($resolvedEvidence, $json + [Environment]::NewLine, [System.Text.UTF8Encoding]::new($false))
$json
