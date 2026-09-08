#Requires -Version 5.1
<#
.SYNOPSIS
    Starts or checks the saved, byte-verified runtime release.
.DESCRIPTION
    Ordinary use needs no database, source-root, or role arguments. Local administration
    settings select the compatible host, frozen catalog, live database/blobs, and probe origins.
    -Restart replaces only the instance started by this launcher, never every server.
    -Profile selects a reviewed alternative for development or recovery.
    -Check verifies saved files without starting, stopping, or changing anything.
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\run-mcp-server.ps1
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\run-mcp-server.ps1 -Restart
#>
[CmdletBinding()]
param([switch] $Restart, [string] $Profile, [switch] $Check)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'src\system\web-interface\scripts\RuntimeLaunch.ps1')
if (-not $Profile) { $Profile = Join-Path $PSScriptRoot 'DantesRoleplay.MCPServer\data\runtime-launch.json' }
$Profile = [IO.Path]::GetFullPath($Profile)
if (-not (Test-Path -LiteralPath $Profile -PathType Leaf)) {
    throw "No saved runtime release at $Profile. Configure a reviewed release with Save-RuntimeLaunchProfile; startup will not guess from an editable checkout."
}
$fingerprint = (Get-FileHash -LiteralPath $Profile -Algorithm SHA256).Hash
$selection = Get-Content -LiteralPath $Profile -Raw | ConvertFrom-Json
Assert-RuntimeLaunchProfile $selection
$exe = Resolve-RuntimeChildPath $selection.hostRoot $selection.executable
if ((Get-FileHash -LiteralPath $Profile -Algorithm SHA256).Hash -cne $fingerprint) { throw 'Release selection changed during validation. Retry with the reviewed profile.' }
Write-Host "Saved release: $fingerprint"
Write-Host "    Host:     $exe"
Write-Host "    Database: $($selection.database)"
Write-Host "    Blobs:    $($selection.blobRoot)"
Write-Host "    Sources:  $($selection.sourceRoot)"
if ($Check) { Write-Host 'Release files verified; no runtime changes.'; return }

$receiptPath = $Profile + '.process.json'
$port = ([uri]$selection.listenUrl).Port
$mutex = New-Object Threading.Mutex($false, "Local\DantesRoleplay.Runtime.Port.$port")
$locked = $false
try {
    try { $locked = $mutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $locked = $true }
    if (-not $locked) { throw "Another launcher is already managing port $port." }
    if ((Get-FileHash -LiteralPath $Profile -Algorithm SHA256).Hash -cne $fingerprint) { throw 'Release selection changed before launch.' }
    $receipt = if (Test-Path -LiteralPath $receiptPath) { Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json } else { $null }
    $processes = @(Get-CimInstance Win32_Process -Filter "Name = 'DantesRoleplay.MCPServer.exe'")
    $owned = Get-OwnedRuntimeProcess $receipt $processes
    $listeners = @(Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue)
    Assert-RuntimeListenerOwnership $listeners $owned
    $unowned = @($processes | Where-Object { $_.ExecutablePath -eq $exe -and $_.ProcessId -ne $owned.ProcessId })
    if ($unowned.Count) { throw "The selected executable has an untracked instance: PID $($unowned.ProcessId -join ', '). It was left unchanged." }
    if ($owned -and $Restart) {
        # Re-resolve immediately before stopping: a reused PID is not ownership.
        $current = Get-OwnedRuntimeProcess $receipt @(Get-CimInstance Win32_Process -Filter "ProcessId = $($owned.ProcessId)")
        if (-not $current) { throw 'The recorded process changed while checking restart ownership.' }
        Stop-Process -Id $current.ProcessId -ErrorAction Stop
        $stopDeadline = [DateTime]::UtcNow.AddSeconds(20)
        do {
            $remaining = Get-OwnedRuntimeProcess $receipt @(Get-CimInstance Win32_Process -Filter "ProcessId = $($current.ProcessId)")
            if ($remaining) { Start-Sleep -Milliseconds 250 }
        } while ($remaining -and [DateTime]::UtcNow -lt $stopDeadline)
        if ($remaining) { throw 'The selected process did not exit.' }
        $owned = $null
    }
    if ($owned -and $receipt.profileFingerprint -cne $fingerprint) { throw 'Saved release changed. Use -Restart to replace the recorded instance.' }
    if (-not $owned) {
        Assert-RuntimeListenerOwnership @(Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue) $null
        # cmd start detaches the child from the calling console. These are paths, not commands.
        $logPath = $Profile + '.' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ') + '.startup.log'
        if ($exe -match '["%!?^&|<>\r\n]' -or $selection.hostRoot -match '["%!?^&|<>\r\n]' -or
            $logPath -match '["%!?^&|<>\r\n]') { throw 'Host/profile path contains unsupported shell metacharacters.' }
        $environment = Get-RuntimeEnvironment $selection
        $previous = @{}
        $started = [DateTime]::UtcNow
        try {
            foreach ($name in $environment.Keys) {
                $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
                [Environment]::SetEnvironmentVariable($name, $environment[$name], 'Process')
            }
            $arguments = '/c start "" /D "{0}" /B "{1}" > "{2}" 2>&1' -f $selection.hostRoot, $exe, $logPath
            Start-Process -FilePath cmd.exe -ArgumentList $arguments -WindowStyle Hidden
        } finally {
            foreach ($name in $previous.Keys) { [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process') }
        }
        for ($attempt = 0; $attempt -lt 20 -and -not $owned; $attempt++) {
            $candidates = @(Get-CimInstance Win32_Process -Filter "Name = 'DantesRoleplay.MCPServer.exe'" | Where-Object {
                $_.ExecutablePath -eq $exe -and $_.CreationDate.ToUniversalTime() -ge $started
            })
            if ($candidates.Count -gt 1) { throw 'Multiple matching new processes; none will be stopped automatically.' }
            if ($candidates.Count -eq 1) { $owned = $candidates[0] } else { Start-Sleep -Milliseconds 250 }
        }
        if (-not $owned) { throw "The selected server did not start. Log: $logPath" }
        $receipt = @{ processId = $owned.ProcessId; executable = $exe; startedAtUtc = $owned.CreationDate.ToUniversalTime().ToString('o'); profileFingerprint = $fingerprint; logPath = $logPath }
        Write-RuntimeJson $receiptPath $receipt -Replace
    }
    $deadline = [DateTime]::UtcNow.AddSeconds(150)
    $ready = $false
    do {
        if (-not (Get-OwnedRuntimeProcess $receipt @(Get-CimInstance Win32_Process -Filter "ProcessId = $($owned.ProcessId)"))) {
            throw "The selected server exited before readiness; no other listener can satisfy this launch. Log: $($receipt.logPath)"
        }
        $listeners = @(Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue)
        Assert-RuntimeListenerOwnership $listeners $owned
        try {
            if (-not $listeners.Count) { throw 'Waiting for the selected listener.' }
            Test-RuntimeTarget $selection $selection.targets[0] | Out-Null
            $ready = $true
        } catch { $lastFailure = $_.Exception.Message }
        if (-not $ready) { Start-Sleep -Seconds 2 }
    } while (-not $ready -and [DateTime]::UtcNow -lt $deadline)
    if (-not $ready) { throw "The selected server is not ready: $lastFailure" }
    foreach ($target in $selection.targets) {
        Test-RuntimeTarget $selection $target | Out-Null
        Write-Host "    Verified ready: $($target.origin)" -ForegroundColor Green
    }
    if ((Get-FileHash -LiteralPath $Profile -Algorithm SHA256).Hash -cne $fingerprint -or
        -not (Get-OwnedRuntimeProcess $receipt @(Get-CimInstance Win32_Process -Filter "ProcessId = $($owned.ProcessId)"))) {
        throw 'Release selection or process changed during readiness verification.'
    }
    $listeners = @(Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue)
    if (-not $listeners.Count) { throw 'The selected listener disappeared during readiness verification.' }
    Assert-RuntimeListenerOwnership $listeners $owned
    foreach ($listener in $listeners) { Write-Host "    Listening: $($listener.LocalAddress):$($listener.LocalPort), PID $($listener.OwningProcess)" }
    Write-Host 'Safe to close this window; the server keeps running.'
} finally {
    if ($locked) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
}
