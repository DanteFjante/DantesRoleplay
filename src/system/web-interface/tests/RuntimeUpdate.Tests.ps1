#Requires -Version 5.1
# No Pester install required. Every write is confined to a fresh disposable fixture.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\scripts\RuntimeUpdate.ps1')

$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ('roleplay-runtime-update-tests-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($fixtureRoot) | Out-Null
$script:passed = 0

function Check([string] $Name, [scriptblock] $Test) {
    & $Test
    $script:passed++
    Write-Host "PASS $Name"
}

function Reject([scriptblock] $Test, [string] $Pattern) {
    $failure = $null
    try { & $Test } catch { $failure = $_.Exception.Message }
    if (-not $failure -or $failure -notmatch $Pattern) {
        throw "Expected failure '$Pattern'; got '$failure'."
    }
}

function Clone($Value) { return $Value | ConvertTo-Json -Depth 40 | ConvertFrom-Json }

function New-ExpectedPins([string] $ApplicationId = 'sample') {
    $checks = [ordered]@{}
    foreach ($name in @('database', 'application-registration', 'active-catalog-snapshot', 'catalog-materialization',
        'extension-resolution', 'query-callability', 'web-page-release', 'audience-binding')) {
        $checks[$name] = @{ code = $name; revision = '1'; fingerprint = ('A' * 64) }
    }
    return @{
        checks = $checks
        audience = @{ status = 'bound'; applicationId = $ApplicationId; stateSpaceId = "$ApplicationId-main"
            campaignId = "campaign.$ApplicationId"; role = 'game-master'; policyRevision = ('B' * 64)
            bindingRevision = ('C' * 64) }
    }
}

function New-ReleaseFixture([string] $Name, [string] $Marker) {
    $root = Join-Path $fixtureRoot $Name
    $hostRoot = Join-Path $root 'host'
    $sourceRoot = Join-Path $root 'source'
    $blobRoot = Join-Path $root 'blobs'
    foreach ($path in @($hostRoot, $blobRoot, (Join-Path $sourceRoot 'catalog'))) {
        [IO.Directory]::CreateDirectory($path) | Out-Null
    }
    $database = Join-Path $root 'database.db'
    [IO.File]::WriteAllText((Join-Path $hostRoot 'DantesRoleplay.MCPServer.exe'), "host-$Marker")
    [IO.File]::WriteAllText((Join-Path $sourceRoot 'catalog\fixture.md'), "source-$Marker")
    [IO.File]::WriteAllText($database, "database-$Marker")
    return [pscustomobject]@{ Root = $root; HostRoot = $hostRoot; SourceRoot = $sourceRoot
        BlobRoot = $blobRoot; Database = $database }
}

function Save-FixtureProfile([string] $Path, $Release, [string] $Marker) {
    $expected = New-ExpectedPins
    $targets = @(
        @{ origin = 'http://127.0.0.1:6217'; expected = $expected },
        @{ origin = 'https://play.example.test'; expected = $expected }
    )
    Save-RuntimeLaunchProfile -Path $Path -HostRoot $Release.HostRoot -SourceRoot $Release.SourceRoot `
        -Database $Release.Database -BlobRoot $Release.BlobRoot -ApplicationId 'sample' `
        -ListenUrl 'http://0.0.0.0:6217' -Targets $targets -Environment @{
            'WebRemoteAccess__Enabled' = 'true'
            'WebRemoteAccess__AllowedOrigins__0' = 'https://play.example.test'
            'Fixture__Marker' = $Marker
        }
}

try {
    Check 'missing profile update fails without creating runtime state' {
        $root = Join-Path $fixtureRoot 'missing-update'
        $profile = Join-Path $root 'DantesRoleplay.MCPServer\data\runtime-launch.json'
        Reject { Update-InstalledRuntime -RepositoryRoot $root -ProfilePath $profile } 'No saved installation'
        if (Test-Path -LiteralPath $root) { throw 'Missing-profile update wrote to the fixture.' }
    }

    Check 'launcher rejects Update with Check before first-run setup without writes' {
        $root = Join-Path $fixtureRoot 'update-check'
        $profile = Join-Path $root 'missing\runtime-launch.json'
        $launcher = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..\..\run-mcp-server.ps1'))
        $previousPreference = $ErrorActionPreference
        try {
            $ErrorActionPreference = 'Continue'
            $output = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $launcher -Update -Check -Profile $profile 2>&1
            $exit = $LASTEXITCODE
        } finally { $ErrorActionPreference = $previousPreference }
        if ($exit -eq 0 -or ($output -join "`n") -notmatch 'cannot be combined') {
            throw "Launcher did not reject -Update -Check: $($output -join ' ')"
        }
        if (Test-Path -LiteralPath $root) { throw 'Rejected launcher arguments wrote to the fixture.' }
    }

    Check 'probe selection clones settings and forces private verification without mutating selection' {
        $release = New-ReleaseFixture 'probe' 'old'
        $profilePath = Join-Path $release.Root 'runtime-launch.json'
        Save-FixtureProfile $profilePath $release 'old'
        $selection = Get-Content -LiteralPath $profilePath -Raw | ConvertFrom-Json
        $before = $selection | ConvertTo-Json -Depth 40 -Compress
        $probe = New-RuntimeUpdateProbeSelection $selection
        if ($probe.listenUrl -cne 'http://127.0.0.1:6217' -or
            $probe.environment.'Runtime:VerificationOnly' -cne 'true' -or
            $probe.environment.'WebRemoteAccess:Enabled' -cne 'true' -or
            $probe.environment.'WebRemoteAccess:AllowedOrigins:0' -cne 'https://play.example.test' -or
            $probe.targets[1].origin -cne 'https://play.example.test') {
            throw 'Probe did not retain settings and authorization while selecting loopback verification.'
        }
        if (($selection | ConvertTo-Json -Depth 40 -Compress) -cne $before -or
            $selection.listenUrl -cne 'http://0.0.0.0:6217' -or
            $selection.environment.WebRemoteAccess__Enabled -cne 'true' -or
            $selection.PSObject.Properties.Name -contains 'Runtime:VerificationOnly') {
            throw 'Probe construction mutated the saved selection.'
        }
    }

    Check 'candidate environment restoration preserves values and removes previously absent flags' {
        $suffix = [Guid]::NewGuid().ToString('N')
        $absent = "DANTESROLEPLAY_UPDATE_ABSENT_$suffix"
        $present = "DANTESROLEPLAY_UPDATE_PRESENT_$suffix"
        [Environment]::SetEnvironmentVariable($absent, [NullString]::Value, [EnvironmentVariableTarget]::Process)
        [Environment]::SetEnvironmentVariable($present, 'preserved-value', [EnvironmentVariableTarget]::Process)
        try {
            $previous = @{
                $absent = [Environment]::GetEnvironmentVariable($absent, [EnvironmentVariableTarget]::Process)
                $present = [Environment]::GetEnvironmentVariable($present, [EnvironmentVariableTarget]::Process)
            }
            [Environment]::SetEnvironmentVariable($absent, 'true', [EnvironmentVariableTarget]::Process)
            [Environment]::SetEnvironmentVariable($present, 'temporary-value', [EnvironmentVariableTarget]::Process)
            Restore-RuntimeUpdateEnvironment $previous
            if ($null -ne [Environment]::GetEnvironmentVariable($absent, [EnvironmentVariableTarget]::Process)) {
                throw 'A previously absent verification flag remained in the process environment.'
            }
            if ([Environment]::GetEnvironmentVariable($present, [EnvironmentVariableTarget]::Process) -cne 'preserved-value') {
                throw 'A pre-existing process environment value was not restored exactly.'
            }
        } finally {
            [Environment]::SetEnvironmentVariable($absent, [NullString]::Value, [EnvironmentVariableTarget]::Process)
            [Environment]::SetEnvironmentVariable($present, [NullString]::Value, [EnvironmentVariableTarget]::Process)
        }
    }

    Check 'atomic selection preserves exact old bytes and selects exact candidate bytes' {
        $old = New-ReleaseFixture 'atomic-old' 'old'
        $candidate = New-ReleaseFixture 'atomic-new' 'new'
        $profilePath = Join-Path $fixtureRoot 'atomic-profile.json'
        $candidatePath = Join-Path $fixtureRoot 'atomic-candidate.json'
        $previousPath = Join-Path $fixtureRoot 'atomic-previous.json'
        Save-FixtureProfile $profilePath $old 'old'
        Save-FixtureProfile $candidatePath $candidate 'new'
        $oldBytes = [IO.File]::ReadAllBytes($profilePath)
        $candidateBytes = [IO.File]::ReadAllBytes($candidatePath)
        $fingerprint = (Get-FileHash -LiteralPath $profilePath -Algorithm SHA256).Hash
        Select-RuntimeUpdate $profilePath $fingerprint $candidatePath $previousPath
        if (-not [Linq.Enumerable]::SequenceEqual([byte[]]$oldBytes, [byte[]][IO.File]::ReadAllBytes($previousPath)) -or
            -not [Linq.Enumerable]::SequenceEqual([byte[]]$candidateBytes, [byte[]][IO.File]::ReadAllBytes($profilePath)) -or
            (Test-Path -LiteralPath $candidatePath)) {
            throw 'Atomic selection did not preserve and replace the exact profile bytes.'
        }
        if (Test-RuntimeUpdateCanResumePrevious $profilePath $fingerprint) {
            throw 'A selected candidate was incorrectly eligible for automatic old-data resumption.'
        }
    }

    Check 'stale expected fingerprint cannot replace the current selection' {
        $old = New-ReleaseFixture 'stale-old' 'old'
        $candidate = New-ReleaseFixture 'stale-new' 'new'
        $profilePath = Join-Path $fixtureRoot 'stale-profile.json'
        $candidatePath = Join-Path $fixtureRoot 'stale-candidate.json'
        $previousPath = Join-Path $fixtureRoot 'stale-previous.json'
        Save-FixtureProfile $profilePath $old 'old'
        Save-FixtureProfile $candidatePath $candidate 'new'
        $oldBytes = [IO.File]::ReadAllBytes($profilePath)
        Reject { Select-RuntimeUpdate $profilePath ('D' * 64) $candidatePath $previousPath } 'changed during update'
        if (-not [Linq.Enumerable]::SequenceEqual([byte[]]$oldBytes, [byte[]][IO.File]::ReadAllBytes($profilePath)) -or
            -not (Test-Path -LiteralPath $candidatePath) -or (Test-Path -LiteralPath $previousPath)) {
            throw 'Stale selection guard changed profile or recovery files.'
        }
    }

    Check 'invalid candidate inventory fails while preserving the old selection' {
        $old = New-ReleaseFixture 'invalid-old' 'old'
        $candidate = New-ReleaseFixture 'invalid-new' 'new'
        $profilePath = Join-Path $fixtureRoot 'invalid-profile.json'
        $candidatePath = Join-Path $fixtureRoot 'invalid-candidate.json'
        $previousPath = Join-Path $fixtureRoot 'invalid-previous.json'
        Save-FixtureProfile $profilePath $old 'old'
        Save-FixtureProfile $candidatePath $candidate 'new'
        $oldBytes = [IO.File]::ReadAllBytes($profilePath)
        $fingerprint = (Get-FileHash -LiteralPath $profilePath -Algorithm SHA256).Hash
        [IO.File]::WriteAllText((Join-Path $candidate.HostRoot 'DantesRoleplay.MCPServer.exe'), 'tampered-candidate')
        Reject { Select-RuntimeUpdate $profilePath $fingerprint $candidatePath $previousPath } 'bytes changed'
        if (-not [Linq.Enumerable]::SequenceEqual([byte[]]$oldBytes, [byte[]][IO.File]::ReadAllBytes($profilePath)) -or
            -not (Test-Path -LiteralPath $candidatePath) -or (Test-Path -LiteralPath $previousPath)) {
            throw 'Invalid candidate changed the selected or recovery profile.'
        }
    }

    Check 'output cannot overlap the old release or recursively copy its own blobs' {
        $old = New-ReleaseFixture 'overlap-old' 'old'
        foreach ($protected in @($old.HostRoot, $old.SourceRoot, $old.BlobRoot)) {
            Reject { Assert-RuntimeUpdateOutput $protected $old } 'must be separate'
            Reject { Assert-RuntimeUpdateOutput (Join-Path $protected 'update-candidate') $old } 'must be separate'
            Reject { Assert-RuntimeUpdateOutput ([IO.Path]::GetDirectoryName($protected)) $old } 'must be separate'
        }
        Assert-RuntimeUpdateOutput (Join-Path $fixtureRoot 'independent-update') $old
    }

    Write-Host "$script:passed runtime update tests passed."
} finally {
    $resolvedFixture = [IO.Path]::GetFullPath($fixtureRoot)
    $tempPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (-not $resolvedFixture.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($resolvedFixture) -notlike 'roleplay-runtime-update-tests-*') {
        throw 'Unsafe fixture cleanup target.'
    }
    Remove-Item -LiteralPath $resolvedFixture -Recurse -Force
}
