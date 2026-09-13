#Requires -Version 5.1
. (Join-Path $PSScriptRoot 'RuntimeLaunch.ps1')

function Assert-FirstRunDestination {
    param([string] $RepositoryRoot, [string] $ProfilePath)
    $expected = [IO.Path]::GetFullPath((Join-Path $RepositoryRoot 'DantesRoleplay.MCPServer/data/runtime-launch.json'))
    if ([IO.Path]::GetFullPath($ProfilePath) -ine $expected) { throw 'First-run setup requires the default profile path.' }
    if ($expected -match '["%!?^&|<>\r\n]') { throw 'Move the checkout to a path without shell metacharacters before setup. Spaces are supported.' }
    $directory = [IO.Path]::GetDirectoryName($expected)
    # No inference from an existing database, abandoned release, or unrecognized local data.
    foreach ($path in @($RepositoryRoot, (Join-Path $RepositoryRoot 'DantesRoleplay.MCPServer'), $directory)) {
        if (Test-Path -LiteralPath $path) {
            $item = Get-Item -LiteralPath $path -Force
            if (-not $item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
                throw "First-run destination must be an ordinary directory: $path"
            }
        }
    }
    if ((Test-Path -LiteralPath $directory) -and @(Get-ChildItem -LiteralPath $directory -Force).Count) {
        throw "Existing runtime data found at $directory but no saved release was selected. Restore its runtime-launch.json or use -Profile with that installation's saved profile. Nothing was imported or replaced."
    }
}

function Initialize-FirstRunIfNeeded {
    param([string] $RepositoryRoot, [string] $ProfilePath, [switch] $ExplicitProfile, [switch] $Check,
        [scriptblock] $Initialize = { param($root, $profile) Initialize-FirstRuntime $root $profile })
    if (Test-Path -LiteralPath $ProfilePath -PathType Leaf) { return }
    if ($ExplicitProfile) { throw "The explicitly selected runtime profile does not exist: $ProfilePath" }
    if ($Check) { throw 'This checkout has not been set up. Run run-mcp-server.cmd without -Check to initialize it. No files were changed.' }
    Assert-FirstRunDestination $RepositoryRoot $ProfilePath
    & $Initialize $RepositoryRoot $ProfilePath
}

function Invoke-FirstRunVersion {
    param([string] $Executable, [string[]] $Arguments)
    $result = & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Unable to check $Executable." }
    return $result
}

function Get-FirstRunToolchain {
    $dotnet = Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $dotnet) { throw 'Install the .NET 10 SDK from https://dotnet.microsoft.com/download/dotnet/10.0, reopen your terminal, and run again.' }
    $sdks = @(Invoke-FirstRunVersion $dotnet.Source @('--list-sdks'))
    if (-not @($sdks | Where-Object { $_ -match '^10\.\d+\.\d+\s' }).Count) {
        throw 'The .NET 10 SDK is required (the runtime alone is insufficient). Install it from https://dotnet.microsoft.com/download/dotnet/10.0 and run again.'
    }
    $node = Get-Command node -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    $npm = Get-Command npm.cmd -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $node -or -not $npm) { throw 'Install Node.js 22.13 or newer, including npm, from https://nodejs.org/ and run again.' }
    $version = ([string](Invoke-FirstRunVersion $node.Source @('--version'))).Trim().TrimStart('v')
    if ($version -notmatch '^\d+\.\d+\.\d+$' -or [version]$version -lt [version]'22.13.0') {
        throw "Node.js 22.13 or newer is required; found $version. Install a current LTS from https://nodejs.org/ and run again."
    }
    return @{ dotnet = $dotnet.Source; node = $node.Source; npm = $npm.Source }
}

function Invoke-FirstRunCommand {
    param([string] $Executable, [string[]] $Arguments, [string] $WorkingDirectory)
    Push-Location -LiteralPath $WorkingDirectory
    try {
        & $Executable @Arguments | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "Setup command failed ($LASTEXITCODE): $Executable $($Arguments -join ' ')" }
    } finally { Pop-Location }
}

function Copy-FirstRunCatalog {
    param([string] $RepositoryRoot, [string] $SourceRoot)
    $catalog = Join-Path $RepositoryRoot 'catalog'
    $inventory = @(Get-RuntimeFileInventory $catalog)
    $destination = Join-Path $SourceRoot 'catalog'
    [IO.Directory]::CreateDirectory($destination) | Out-Null
    foreach ($file in $inventory) {
        $from = Resolve-RuntimeChildPath $catalog $file.path
        $to = Resolve-RuntimeChildPath $destination $file.path
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($to)) | Out-Null
        [IO.File]::Copy($from, $to, $false)
        if ((Get-FileHash -LiteralPath $to -Algorithm SHA256).Hash -cne $file.sha256) {
            throw 'Catalog changed while preparing first-run setup. Run again after editing finishes.'
        }
    }
}

function Stop-FirstRunProbe {
    param($Receipt)
    if (-not $Receipt) { return }
    $owned = Get-OwnedRuntimeProcess $Receipt @(Get-CimInstance Win32_Process -Filter "ProcessId = $($Receipt.processId)")
    if ($owned) {
        Stop-Process -Id $owned.ProcessId -ErrorAction Stop
        Wait-Process -Id $owned.ProcessId -Timeout 20 -ErrorAction SilentlyContinue
        if (Get-OwnedRuntimeProcess $Receipt @(Get-CimInstance Win32_Process -Filter "ProcessId = $($Receipt.processId)")) {
            throw 'The first-run verification process did not exit. Its setup files were preserved.'
        }
    }
}

function Remove-FirstRunAttempt {
    param([string] $Attempt, [string] $DataRoot)
    $full = [IO.Path]::GetFullPath($Attempt)
    if ([IO.Path]::GetDirectoryName($full) -ine [IO.Path]::GetFullPath($DataRoot) -or
        [IO.Path]::GetFileName($full) -cnotmatch '^install-[0-9a-f]{32}$') {
        throw 'Unsafe first-run cleanup target.'
    }
    # Only the unique directory created by this invocation is eligible. Refuse links
    # or a remaining process before removing a failed, never-selected installation.
    Get-RuntimeFileInventory $full | Out-Null
    $prefix = $full.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    $running = @(Get-CimInstance Win32_Process -Filter "Name = 'DantesRoleplay.MCPServer.exe'" | Where-Object {
        $_.ExecutablePath -and $_.ExecutablePath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)
    })
    if ($running.Count) { throw 'A first-run process is still using its files; they were preserved.' }
    Remove-Item -LiteralPath $full -Recurse -Force
}

function Initialize-FirstRuntime {
    param([string] $RepositoryRoot, [string] $ProfilePath)
    $RepositoryRoot = [IO.Path]::GetFullPath($RepositoryRoot)
    $ProfilePath = [IO.Path]::GetFullPath($ProfilePath)
    $data = [IO.Path]::GetDirectoryName($ProfilePath)
    $hash = [Security.Cryptography.SHA256]::Create()
    try { $key = [BitConverter]::ToString($hash.ComputeHash([Text.Encoding]::UTF8.GetBytes($data.ToUpperInvariant()))).Replace('-', '') }
    finally { $hash.Dispose() }
    $mutex = New-Object Threading.Mutex($false, "Local\DantesRoleplay.Setup.$key")
    $locked = $false; $attempt = $null; $probeReceipt = $null; $selected = $false
    try {
        try { $locked = $mutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $locked = $true }
        if (-not $locked) { throw 'Another first-run setup is already running for this checkout.' }
        if (Test-Path -LiteralPath $ProfilePath -PathType Leaf) { return }
        Assert-FirstRunDestination $RepositoryRoot $ProfilePath
        $toolchain = Get-FirstRunToolchain
        Assert-RuntimeListenerOwnership @(Get-NetTCPConnection -LocalPort 6217 -State Listen -ErrorAction SilentlyContinue) $null
        $attempt = Join-Path $data ('install-' + [Guid]::NewGuid().ToString('N'))
        [IO.Directory]::CreateDirectory($attempt) | Out-Null
        $hostRoot = Join-Path $attempt 'host'
        $sourceRoot = Join-Path $attempt 'source'
        $packageRoot = Join-Path $attempt 'package'
        $runtimeRoot = Join-Path $attempt 'runtime'
        Write-Host 'First run: building the server and website. Package restore may take a few minutes.'
        Invoke-FirstRunCommand $toolchain.dotnet @('publish', 'DantesRoleplay.MCPServer/DantesRoleplay.MCPServer.csproj',
            '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-p:PublishSingleFile=false',
            '-p:Nullable=annotations', '-p:UseSharedCompilation=false', '-m:1', '-o', $hostRoot, '--nologo') $RepositoryRoot
        $frontend = Join-Path $RepositoryRoot 'src/system/web-interface/dnd2024'
        Invoke-FirstRunCommand $toolchain.npm @('ci', '--no-audit', '--no-fund') $frontend
        Invoke-FirstRunCommand $toolchain.npm @('run', 'build:server') $frontend
        Invoke-FirstRunCommand $toolchain.node @((Join-Path $RepositoryRoot 'src/system/web-interface/scripts/build-installation.mjs'),
            '--repository', $RepositoryRoot, '--output', $packageRoot) $RepositoryRoot
        Copy-FirstRunCatalog $RepositoryRoot $sourceRoot
        $exe = Join-Path $hostRoot 'DantesRoleplay.MCPServer.exe'
        Write-Host 'First run: installing the catalog, starter data, and website into a new database.'
        Invoke-FirstRunCommand $exe @('--Installation:Manifest', (Join-Path $packageRoot 'installation.json'),
            '--Installation:Root', $runtimeRoot, '--Installation:SourceRoot', $sourceRoot) $hostRoot
        $receipt = Get-Content -LiteralPath (Join-Path $runtimeRoot 'installation.json') -Raw | ConvertFrom-Json
        $environment = @{}
        foreach ($property in $receipt.environment.PSObject.Properties) { $environment[$property.Name] = [string]$property.Value }
        $selection = [pscustomobject]@{ database = (Join-Path $runtimeRoot 'database.db'); blobRoot = (Join-Path $runtimeRoot 'blobs')
            sourceRoot = $sourceRoot; listenUrl = 'http://127.0.0.1:6217'; applicationId = $receipt.applicationId
            environment = [pscustomobject]$environment; targets = @([pscustomobject]@{ origin = 'http://127.0.0.1:6217'; expected = $receipt.expected }) }
        Assert-RuntimePins $receipt.expected
        $previous = @{}
        try {
            $values = Get-RuntimeEnvironment $selection
            foreach ($name in $values.Keys) {
                $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
                [Environment]::SetEnvironmentVariable($name, [string]$values[$name], 'Process')
            }
            $process = Start-Process -FilePath $exe -WorkingDirectory $hostRoot -WindowStyle Hidden -PassThru `
                -RedirectStandardOutput (Join-Path $attempt 'verification.log') -RedirectStandardError (Join-Path $attempt 'verification-error.log')
            $observed = Get-CimInstance Win32_Process -Filter "ProcessId = $($process.Id)"
            if (-not $observed -or $observed.ExecutablePath -ine $exe) { throw 'Could not establish ownership of the setup verification process.' }
            $probeReceipt = @{ processId = $process.Id; executable = $exe; startedAtUtc = $observed.CreationDate.ToUniversalTime().ToString('o') }
        } finally {
            foreach ($name in $previous.Keys) { [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process') }
        }
        Write-Host 'First run: checking the installed website and application.'
        $deadline = [DateTime]::UtcNow.AddSeconds(150); $ready = $false
        do {
            $owned = Get-OwnedRuntimeProcess $probeReceipt @(Get-CimInstance Win32_Process -Filter "ProcessId = $($probeReceipt.processId)")
            if (-not $owned) { throw "The setup verification server exited. See $attempt/verification-error.log." }
            Assert-RuntimeListenerOwnership @(Get-NetTCPConnection -LocalPort 6217 -State Listen -ErrorAction SilentlyContinue) $owned
            try { Test-RuntimeTarget $selection $selection.targets[0] | Out-Null; $ready = $true }
            catch { $lastFailure = $_.Exception.Message }
            if (-not $ready) { Start-Sleep -Seconds 2 }
        } while (-not $ready -and [DateTime]::UtcNow -lt $deadline)
        if (-not $ready) { throw "First-run verification failed: $lastFailure" }
        Stop-FirstRunProbe $probeReceipt
        $probeReceipt = $null
        Save-RuntimeLaunchProfile -Path $ProfilePath -HostRoot $hostRoot -SourceRoot $sourceRoot `
            -Database $selection.database -BlobRoot $selection.blobRoot -ApplicationId $selection.applicationId `
            -ListenUrl $selection.listenUrl -Targets $selection.targets -Environment $environment
        $selected = $true
        Write-Host 'First-run setup complete. Future launches will reuse this installation.' -ForegroundColor Green
    } catch {
        $failure = $_
        if ($attempt) {
            $diagnostics = Join-Path $RepositoryRoot '.tmp'
            [IO.Directory]::CreateDirectory($diagnostics) | Out-Null
            $diagnosticPath = Join-Path $diagnostics ([IO.Path]::GetFileName($attempt) + '.failure.log')
            $details = @([string]$failure)
            foreach ($name in @('verification.log', 'verification-error.log')) {
                $log = Join-Path $attempt $name
                if (Test-Path -LiteralPath $log -PathType Leaf) { $details += @(Get-Content -LiteralPath $log -Tail 60) }
            }
            [IO.File]::WriteAllLines($diagnosticPath, [string[]]$details)
            Write-Warning "Setup failed. Diagnostic summary: $diagnosticPath"
        }
        throw
    } finally {
        try {
            Stop-FirstRunProbe $probeReceipt
            if ($attempt -and -not $selected -and -not (Test-Path -LiteralPath $ProfilePath)) {
                try { Remove-FirstRunAttempt $attempt $data }
                catch { Write-Warning "Incomplete setup was preserved at $attempt. $($_.Exception.Message)" }
            }
        } finally {
            if ($locked) { $mutex.ReleaseMutex() }
            $mutex.Dispose()
        }
    }
}
