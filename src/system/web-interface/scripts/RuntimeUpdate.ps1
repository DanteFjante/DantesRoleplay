#Requires -Version 5.1
. (Join-Path $PSScriptRoot 'FirstRun.ps1')

function Assert-RuntimeUpdatePath {
    param([string] $Path)
    $current = [IO.Path]::GetFullPath($Path)
    while ($current) {
        if (Test-Path -LiteralPath $current) {
            if ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw "Update paths cannot contain filesystem links: $current"
            }
        }
        $current = [IO.Path]::GetDirectoryName($current)
    }
}

function Get-RuntimeUpdateProcess {
    param($Selection, [string] $ProfilePath)
    $receiptPath = $ProfilePath + '.process.json'
    $receipt = if (Test-Path -LiteralPath $receiptPath -PathType Leaf) {
        Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json
    } else { $null }
    $processes = @(Get-CimInstance Win32_Process -Filter "Name = 'DantesRoleplay.MCPServer.exe'")
    $owned = Get-OwnedRuntimeProcess $receipt $processes
    $exe = Resolve-RuntimeChildPath $Selection.hostRoot $Selection.executable
    if ($owned -and $owned.ExecutablePath -ine $exe) { throw 'The process receipt belongs to a different release.' }
    if ($owned -and $receipt.profileFingerprint -cne (Get-FileHash -LiteralPath $ProfilePath -Algorithm SHA256).Hash) {
        throw 'The running process belongs to a different saved selection. Restart the selected installation before updating.'
    }
    $port = ([uri]$Selection.listenUrl).Port
    Assert-RuntimeListenerOwnership @(Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue) $owned
    $unowned = @($processes | Where-Object { $_.ExecutablePath -ieq $exe -and $_.ProcessId -ne $owned.ProcessId })
    if ($unowned.Count) { throw 'The selected release has an untracked process. No server was stopped.' }
    return [pscustomobject]@{ process = $owned; receipt = $receipt }
}

function Assert-RuntimeUpdateOutput {
    param([string] $OutputPath, $Selection)
    $output = [IO.Path]::GetFullPath($OutputPath).TrimEnd('\', '/')
    foreach ($root in @($Selection.hostRoot, $Selection.sourceRoot, $Selection.blobRoot)) {
        $protected = [IO.Path]::GetFullPath($root).TrimEnd('\', '/')
        if ($output.Equals($protected, [StringComparison]::OrdinalIgnoreCase) -or
            $output.StartsWith($protected + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
            $protected.StartsWith($output + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Update output must be separate from the previous host, source, and blob directories.'
        }
    }
}

function New-RuntimeUpdateProbeSelection {
    param($Selection)
    $probe = $Selection | ConvertTo-Json -Depth 40 | ConvertFrom-Json
    $probe.listenUrl = $probe.targets[0].origin
    if (-not ([uri]$probe.listenUrl).IsLoopback) { throw 'Update verification requires a direct loopback target.' }
    $environment = @{}
    foreach ($property in $probe.environment.PSObject.Properties) {
        $environment[$property.Name.Replace('__', ':')] = [string]$property.Value
    }
    $environment['Runtime:VerificationOnly'] = 'true'
    $probe.environment = [pscustomobject]$environment
    return $probe
}

function Restore-RuntimeUpdateEnvironment {
    param([hashtable] $Previous)
    foreach ($name in $Previous.Keys) {
        if ($null -eq $Previous[$name]) {
            # PowerShell 7 can bind a null argument to string.Empty here. NullString keeps the
            # .NET deletion contract explicit on both Windows PowerShell 5.1 and PowerShell 7.
            [Environment]::SetEnvironmentVariable(
                $name, [NullString]::Value, [EnvironmentVariableTarget]::Process)
        } else {
            [Environment]::SetEnvironmentVariable(
                $name, [string]$Previous[$name], [EnvironmentVariableTarget]::Process)
        }
    }
}

function Test-RuntimeUpdateCandidate {
    param($Selection, [string] $LogDirectory)
    $probe = New-RuntimeUpdateProbeSelection $Selection
    $exe = Resolve-RuntimeChildPath $Selection.hostRoot $Selection.executable
    $receipt = $null
    $previous = @{}
    try {
        try {
            $environment = Get-RuntimeEnvironment $probe
            foreach ($name in $environment.Keys) {
                $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
                [Environment]::SetEnvironmentVariable($name, [string]$environment[$name], 'Process')
            }
            $process = Start-Process -FilePath $exe -WorkingDirectory $Selection.hostRoot -WindowStyle Hidden -PassThru `
                -RedirectStandardOutput (Join-Path $LogDirectory 'verification.log') `
                -RedirectStandardError (Join-Path $LogDirectory 'verification-error.log')
            $observed = Get-CimInstance Win32_Process -Filter "ProcessId = $($process.Id)"
            if (-not $observed -or $observed.ExecutablePath -ine $exe) { throw 'Could not establish ownership of the update verification process.' }
            $receipt = @{ processId = $process.Id; executable = $exe; startedAtUtc = $observed.CreationDate.ToUniversalTime().ToString('o') }
        } finally { Restore-RuntimeUpdateEnvironment $previous }
        $deadline = [DateTime]::UtcNow.AddSeconds(150)
        $ready = $false
        do {
            $owned = Get-OwnedRuntimeProcess $receipt @(Get-CimInstance Win32_Process -Filter "ProcessId = $($receipt.processId)")
            if (-not $owned) { throw "The update verification server exited. See $LogDirectory/verification-error.log." }
            Assert-RuntimeListenerOwnership @(Get-NetTCPConnection -LocalPort ([uri]$probe.listenUrl).Port -State Listen -ErrorAction SilentlyContinue) $owned
            try {
                Test-RuntimeTarget $probe $probe.targets[0] | Out-Null
                $response = Invoke-WebRequest -Uri ($probe.listenUrl.TrimEnd('/') + '/') -UseBasicParsing -TimeoutSec 15 -MaximumRedirection 0
                if ($response.StatusCode -ne 200) { throw 'The candidate website is unavailable.' }
                $ready = $true
            } catch { $lastFailure = $_.Exception.Message }
            if (-not $ready) { Start-Sleep -Seconds 2 }
        } while (-not $ready -and [DateTime]::UtcNow -lt $deadline)
        if (-not $ready) { throw "Update verification failed: $lastFailure" }
    } finally { Stop-FirstRunProbe $receipt }
}

function Select-RuntimeUpdate {
    param([string] $ProfilePath, [string] $ExpectedFingerprint,
        [string] $CandidateProfilePath, [string] $PreviousProfilePath)
    foreach ($path in @($ProfilePath, $CandidateProfilePath, $PreviousProfilePath)) { Assert-RuntimeUpdatePath $path }
    if ((Get-FileHash -LiteralPath $ProfilePath -Algorithm SHA256).Hash -cne $ExpectedFingerprint) {
        throw 'The selected installation changed during update. No selection was replaced.'
    }
    if (Test-Path -LiteralPath $PreviousProfilePath) { throw 'The update recovery profile already exists.' }
    $candidate = Get-Content -LiteralPath $CandidateProfilePath -Raw | ConvertFrom-Json
    Assert-RuntimeLaunchProfile $candidate
    if ((Get-FileHash -LiteralPath $ProfilePath -Algorithm SHA256).Hash -cne $ExpectedFingerprint) {
        throw 'The selected installation changed during candidate verification.'
    }
    [IO.File]::Replace($CandidateProfilePath, $ProfilePath, $PreviousProfilePath)
}

function Test-RuntimeUpdateCanResumePrevious {
    param([string] $ProfilePath, [string] $PreviousFingerprint)
    # After selection the new database may already have accepted gameplay. Restoring
    # the old database automatically at that point could silently discard those writes.
    return (Test-Path -LiteralPath $ProfilePath -PathType Leaf) -and
        (Get-FileHash -LiteralPath $ProfilePath -Algorithm SHA256).Hash -ceq $PreviousFingerprint
}

function Update-InstalledRuntime {
    param([string] $RepositoryRoot, [string] $ProfilePath)
    $RepositoryRoot = [IO.Path]::GetFullPath($RepositoryRoot)
    $ProfilePath = [IO.Path]::GetFullPath($ProfilePath)
    Assert-RuntimeUpdatePath $ProfilePath
    if (-not (Test-Path -LiteralPath $ProfilePath -PathType Leaf)) {
        throw 'No saved installation was found. Run run-mcp-server.cmd without -Update for first-time setup, or select an existing -Profile.'
    }
    $originalBytes = [IO.File]::ReadAllBytes($ProfilePath)
    $profileHasher = [Security.Cryptography.SHA256]::Create()
    try { $fingerprint = [BitConverter]::ToString($profileHasher.ComputeHash($originalBytes)).Replace('-', '') }
    finally { $profileHasher.Dispose() }
    $original = [Text.Encoding]::UTF8.GetString($originalBytes) | ConvertFrom-Json
    Assert-RuntimeLaunchProfile $original
    foreach ($path in @($original.hostRoot, $original.sourceRoot, $original.database, $original.blobRoot)) { Assert-RuntimeUpdatePath $path }
    if ($RepositoryRoot -match '["%!?^&|<>\r\n]' -or $ProfilePath -match '["%!?^&|<>\r\n]') {
        throw 'Update paths contain unsupported shell metacharacters. Spaces are supported.'
    }
    $port = ([uri]$original.listenUrl).Port
    $updateMutex = New-Object Threading.Mutex($false, "Local\DantesRoleplay.Runtime.Port.$port")
    $locked = $false; $wasRunning = $false; $stopped = $false; $attempt = $null
    $launcher = Join-Path $RepositoryRoot 'run-mcp-server.ps1'
    try {
        try { $locked = $updateMutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $locked = $true }
        if (-not $locked) { throw 'Another launcher or update is already managing this installation.' }
        if (-not (Test-RuntimeUpdateCanResumePrevious $ProfilePath $fingerprint)) { throw 'The selected installation changed before the update began.' }
        Get-RuntimeUpdateProcess $original $ProfilePath | Out-Null
        $toolchain = Get-FirstRunToolchain
        $attempt = Join-Path ([IO.Path]::GetDirectoryName($ProfilePath)) ('update-' + [Guid]::NewGuid().ToString('N'))
        Assert-RuntimeUpdateOutput $attempt $original
        [IO.Directory]::CreateDirectory($attempt) | Out-Null
        Write-Host 'Update: preparing the new server and website while the existing installation stays available.'
        $release = New-RuntimeRelease $RepositoryRoot $attempt $toolchain
        if (-not (Test-RuntimeUpdateCanResumePrevious $ProfilePath $fingerprint)) { throw 'The selected installation changed while building the update.' }
        Assert-RuntimeLaunchProfile $original
        $running = Get-RuntimeUpdateProcess $original $ProfilePath
        $wasRunning = $null -ne $running.process
        $previousProfile = Join-Path $attempt 'previous-profile.json'
        [IO.File]::WriteAllBytes($previousProfile, $originalBytes)
        Write-Host 'Update: pausing the selected server and preparing a preserved copy of its game data.'
        if ($wasRunning) {
            $stopped = $true
            Stop-FirstRunProbe $running.receipt
        }
        Assert-RuntimeListenerOwnership @(Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue) $null
        $runtimeRoot = Join-Path $attempt 'runtime'
        $exe = Join-Path $release.hostRoot 'DantesRoleplay.MCPServer.exe'
        Invoke-FirstRunCommand $exe @('--RuntimeUpdate:Manifest', (Join-Path $release.packageRoot 'installation.json'),
            '--RuntimeUpdate:Profile', $previousProfile, '--RuntimeUpdate:Root', $runtimeRoot,
            '--RuntimeUpdate:SourceRoot', $release.sourceRoot) $release.hostRoot
        $receipt = Get-Content -LiteralPath (Join-Path $runtimeRoot 'update.json') -Raw | ConvertFrom-Json
        if ($receipt.format -cne 'dantesroleplay.runtime-update/1' -or $receipt.applicationId -cne $original.applicationId) {
            throw 'The updater returned an incompatible installation receipt.'
        }
        $environment = @{}
        foreach ($property in $receipt.environment.PSObject.Properties) { $environment[$property.Name] = [string]$property.Value }
        $candidateProfile = Join-Path $attempt 'candidate-profile.json'
        Save-RuntimeLaunchProfile -Path $candidateProfile -HostRoot $release.hostRoot -SourceRoot $release.sourceRoot `
            -Database (Join-Path $runtimeRoot 'database.db') -BlobRoot (Join-Path $runtimeRoot 'blobs') `
            -ApplicationId $original.applicationId -ListenUrl $original.listenUrl -Targets @($receipt.targets) -Environment $environment
        $candidate = Get-Content -LiteralPath $candidateProfile -Raw | ConvertFrom-Json
        Write-Host 'Update: verifying the candidate privately with background work and writes disabled.'
        Test-RuntimeUpdateCandidate $candidate $attempt
        $recoveryProfile = Join-Path $attempt 'prior-selection.json'
        Select-RuntimeUpdate $ProfilePath $fingerprint $candidateProfile $recoveryProfile
        Write-Host "Update selected. Previous installation retained; recovery profile: $recoveryProfile"
        & $launcher -Profile $ProfilePath
        Write-Host 'Update complete. Your existing game data and configuration have been retained.' -ForegroundColor Green
    } catch {
        $failure = $_
        if ($attempt) { Write-Warning "Update files are retained at $attempt." }
        if ($stopped -and $wasRunning -and (Test-RuntimeUpdateCanResumePrevious $ProfilePath $fingerprint)) {
            try {
                Write-Host 'Update was not selected. Resuming the previous installation.'
                & $launcher -Profile $ProfilePath
            } catch { Write-Warning "The previous installation is preserved, but could not resume: $($_.Exception.Message)" }
        } elseif (-not (Test-RuntimeUpdateCanResumePrevious $ProfilePath $fingerprint)) {
            Write-Warning 'The selected profile is retained to protect any gameplay written after selection. Resolve startup with the normal launcher; do not restore an older database over later writes.'
        }
        throw $failure
    } finally {
        if ($locked) { $updateMutex.ReleaseMutex() }
        $updateMutex.Dispose()
    }
}
