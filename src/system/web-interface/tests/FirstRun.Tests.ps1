#Requires -Version 5.1
# No Pester install required. Every write is confined to a fresh disposable fixture.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\scripts\FirstRun.ps1')

$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ('roleplay-first-run-tests-' + [Guid]::NewGuid().ToString('N'))
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

function New-RepositoryFixture([string] $Name) {
    $root = Join-Path $fixtureRoot $Name
    [IO.Directory]::CreateDirectory($root) | Out-Null
    return [pscustomobject]@{
        Root = $root
        Data = Join-Path $root 'DantesRoleplay.MCPServer\data'
        Profile = Join-Path $root 'DantesRoleplay.MCPServer\data\runtime-launch.json'
    }
}

function Write-FakeCommand([string] $Directory, [string] $Name, [string] $Output) {
    [IO.Directory]::CreateDirectory($Directory) | Out-Null
    [IO.File]::WriteAllText((Join-Path $Directory ($Name + '.cmd')), "@echo off`r`necho $Output`r`n")
}

try {
    Check 'clean default destination invokes the initializer with exact paths without launcher writes' {
        $fixture = New-RepositoryFixture 'clean'
        $script:invocations = @()
        Initialize-FirstRunIfNeeded -RepositoryRoot $fixture.Root -ProfilePath $fixture.Profile -Initialize {
            param($repositoryRoot, $profilePath)
            $script:invocations += [pscustomobject]@{ Root = $repositoryRoot; Profile = $profilePath }
        }
        if ($script:invocations.Count -ne 1 -or
            $script:invocations[0].Root -cne [IO.Path]::GetFullPath($fixture.Root) -or
            $script:invocations[0].Profile -cne [IO.Path]::GetFullPath($fixture.Profile)) {
            throw 'Initializer did not receive the exact repository and profile paths.'
        }
        if (Test-Path -LiteralPath $fixture.Data) { throw 'The first-run dispatcher wrote runtime data.' }
    }

    Check 'an existing profile is reused byte-for-byte without invoking initialization' {
        $fixture = New-RepositoryFixture 'existing-profile'
        [IO.Directory]::CreateDirectory($fixture.Data) | Out-Null
        [IO.File]::WriteAllText($fixture.Profile, '{"saved":true}')
        $before = (Get-FileHash -LiteralPath $fixture.Profile -Algorithm SHA256).Hash
        Initialize-FirstRunIfNeeded -RepositoryRoot $fixture.Root -ProfilePath $fixture.Profile -Initialize {
            throw 'Initializer must not run for a saved profile.'
        }
        if ((Get-FileHash -LiteralPath $fixture.Profile -Algorithm SHA256).Hash -cne $before) {
            throw 'Saved profile bytes changed.'
        }
    }

    Check 'explicit missing profile and Check remain read-only errors' {
        foreach ($case in @(
            @{ Name = 'explicit'; Arguments = @{ ExplicitProfile = $true } },
            @{ Name = 'check'; Arguments = @{ Check = $true } }
        )) {
            $fixture = New-RepositoryFixture $case.Name
            $script:called = $false
            $arguments = $case.Arguments
            Reject {
                Initialize-FirstRunIfNeeded -RepositoryRoot $fixture.Root -ProfilePath $fixture.Profile @arguments -Initialize {
                    $script:called = $true
                }
            } 'profile|saved|missing|exist|Check'
            if ($script:called -or (Test-Path -LiteralPath $fixture.Data)) {
                throw "$($case.Name) changed the missing destination."
            }
        }
    }

    Check 'existing database blobs or logs refuse implicit bootstrap' {
        foreach ($relative in @('dantesroleplay.db', 'blobs\existing.bin', 'previous.startup.log')) {
            $fixture = New-RepositoryFixture ('occupied-' + [Guid]::NewGuid().ToString('N'))
            $occupied = Join-Path $fixture.Data $relative
            [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($occupied)) | Out-Null
            [IO.File]::WriteAllText($occupied, 'existing runtime state')
            $script:called = $false
            Reject {
                Initialize-FirstRunIfNeeded -RepositoryRoot $fixture.Root -ProfilePath $fixture.Profile -Initialize {
                    $script:called = $true
                }
            } 'existing|empty|runtime|data|refus'
            if ($script:called) { throw "Initializer ran over $relative." }
        }
    }

    Check 'first-run setup cannot target a profile outside the default data path' {
        $fixture = New-RepositoryFixture 'wrong-destination'
        $wrongProfile = Join-Path $fixture.Root 'alternate\runtime-launch.json'
        $script:called = $false
        Reject {
            Initialize-FirstRunIfNeeded -RepositoryRoot $fixture.Root -ProfilePath $wrongProfile -Initialize {
                $script:called = $true
            }
        } 'default profile'
        if ($script:called -or (Test-Path -LiteralPath (Split-Path -Parent $wrongProfile))) {
            throw 'Non-default setup destination was changed.'
        }
    }

    Check 'initializer failure propagates and cannot select a profile' {
        $fixture = New-RepositoryFixture 'failed-initializer'
        Reject {
            Initialize-FirstRunIfNeeded -RepositoryRoot $fixture.Root -ProfilePath $fixture.Profile -Initialize {
                throw 'injected provisioning failure'
            }
        } 'injected provisioning failure'
        if (Test-Path -LiteralPath $fixture.Profile) { throw 'Failed initialization selected a profile.' }
    }

    Check 'failed-attempt cleanup removes only its owned directory and preserves unrelated files' {
        $fixture = New-RepositoryFixture 'cleanup'
        [IO.Directory]::CreateDirectory($fixture.Data) | Out-Null
        $existing = Join-Path $fixture.Data 'existing.db'
        [IO.File]::WriteAllText($existing, 'preserve this installation')
        $attempt = Join-Path $fixture.Data ('install-' + [Guid]::NewGuid().ToString('N'))
        [IO.Directory]::CreateDirectory($attempt) | Out-Null
        [IO.File]::WriteAllText((Join-Path $attempt 'build.log'), 'failed build')
        Reject { Remove-FirstRunAttempt $fixture.Data $fixture.Data } 'Unsafe'
        Remove-FirstRunAttempt $attempt $fixture.Data
        if ((Test-Path -LiteralPath $attempt) -or [IO.File]::ReadAllText($existing) -cne 'preserve this installation') {
            throw 'Cleanup removed unrelated state or retained its failed attempt.'
        }
    }

    Check 'toolchain accepts the minimum supported SDK and Node with npm' {
        $tools = Join-Path $fixtureRoot 'supported-tools'
        Write-FakeCommand $tools 'dotnet' '10.0.100 [fixture]'
        Write-FakeCommand $tools 'node' 'v22.13.0'
        Write-FakeCommand $tools 'npm' '10.9.0'
        $oldPath = $env:PATH
        try {
            $env:PATH = $tools
            $result = Get-FirstRunToolchain
            if (-not $result) { throw 'Supported toolchain was not returned.' }
        } finally { $env:PATH = $oldPath }
    }

    Check 'toolchain rejects old SDK old Node and missing npm before provisioning' {
        foreach ($case in @(
            @{ Name = 'missing-sdk'; Dotnet = $null; Node = 'v22.13.0'; Npm = '10.9.0'; Pattern = '\.NET 10' },
            @{ Name = 'old-sdk'; Dotnet = '9.0.100 [fixture]'; Node = 'v22.13.0'; Npm = '10.9.0'; Pattern = '10' },
            @{ Name = 'old-node'; Dotnet = '10.0.100 [fixture]'; Node = 'v22.12.0'; Npm = '10.9.0'; Pattern = '22.13|Node' },
            @{ Name = 'missing-npm'; Dotnet = '10.0.100 [fixture]'; Node = 'v22.13.0'; Npm = $null; Pattern = 'npm' }
        )) {
            $tools = Join-Path $fixtureRoot $case.Name
            [IO.Directory]::CreateDirectory($tools) | Out-Null
            if ($case.Dotnet) { Write-FakeCommand $tools 'dotnet' $case.Dotnet }
            Write-FakeCommand $tools 'node' $case.Node
            if ($case.Npm) { Write-FakeCommand $tools 'npm' $case.Npm }
            $oldPath = $env:PATH
            try {
                $env:PATH = $tools
                Reject { Get-FirstRunToolchain } $case.Pattern
            } finally { $env:PATH = $oldPath }
        }
    }

    Write-Host "$script:passed first-run tests passed."
} finally {
    $resolvedFixture = [IO.Path]::GetFullPath($fixtureRoot)
    $tempPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (-not $resolvedFixture.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($resolvedFixture) -notlike 'roleplay-first-run-tests-*') {
        throw 'Unsafe fixture cleanup target.'
    }
    Remove-Item -LiteralPath $resolvedFixture -Recurse -Force
}
