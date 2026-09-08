#Requires -Version 5.1
# No Pester install required. Every write is confined to a fresh disposable fixture.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\scripts\RuntimeLaunch.ps1')
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ('roleplay-runtime-tests-' + [Guid]::NewGuid().ToString('N'))
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
    if (-not $failure -or $failure -notmatch $Pattern) { throw "Expected failure '$Pattern'; got '$failure'." }
}
function Clone($Value) { return $Value | ConvertTo-Json -Depth 40 | ConvertFrom-Json }
try {
    $hostRoot = Join-Path $fixtureRoot 'host'
    $sourceRoot = Join-Path $fixtureRoot 'release'
    $blobRoot = Join-Path $fixtureRoot 'blobs'
    foreach ($path in @($hostRoot, $blobRoot, (Join-Path $sourceRoot 'catalog'))) { [IO.Directory]::CreateDirectory($path) | Out-Null }
    $database = Join-Path $fixtureRoot 'game.db'
    $executable = Join-Path $hostRoot 'DantesRoleplay.MCPServer.exe'
    $source = Join-Path $sourceRoot 'catalog/fixture.md'
    [IO.File]::WriteAllText($database, 'disposable database placeholder')
    [IO.File]::WriteAllText($executable, 'disposable executable placeholder')
    [IO.File]::WriteAllText($source, "exact`r`nbytes`r`n")
    $checks = [ordered]@{}
    foreach ($name in @('database', 'application-registration', 'active-catalog-snapshot', 'catalog-materialization',
        'extension-resolution', 'query-callability', 'web-page-release', 'audience-binding')) {
        $checks[$name] = @{ code = $name; revision = '1'; fingerprint = ('A' * 64) }
    }
    $audience = @{ status = 'bound'; applicationId = 'sample'; stateSpaceId = 'sample-main'; campaignId = 'campaign.sample';
        role = 'game-master'; policyRevision = ('A' * 64); bindingRevision = ('B' * 64) }
    $expected = @{ checks = $checks; audience = $audience }
    $targets = @(@{origin = 'http://127.0.0.1:6217'; expected = $expected}, @{origin = 'http://192.0.2.7'; expected = $expected})
    $profilePath = Join-Path $fixtureRoot 'runtime-launch.json'
    Check 'save and reload exact host, sources, database, blobs and per-origin pins' {
        Save-RuntimeLaunchProfile $profilePath $hostRoot $sourceRoot $database $blobRoot 'sample' 'http://0.0.0.0:6217' $targets @{}
        $script:profile = Get-Content -LiteralPath $profilePath -Raw | ConvertFrom-Json
        Assert-RuntimeLaunchProfile $profile
    }
    Check 'saving cannot silently replace an existing selection' {
        Reject { Save-RuntimeLaunchProfile $profilePath $hostRoot $sourceRoot $database $blobRoot 'sample' 'http://0.0.0.0:6217' $targets @{} } 'exist'
    }
    Check 'reviewed replacement retains a recoverable prior profile' {
        Save-RuntimeLaunchProfile $profilePath $hostRoot $sourceRoot $database $blobRoot 'sample' 'http://0.0.0.0:6217' $targets @{} -Replace
        if (@(Get-ChildItem -LiteralPath $fixtureRoot -Filter '*.previous').Count -ne 1) { throw 'Missing previous selection.' }
    }
    Check 'development edits do not change frozen release validation' {
        [IO.File]::WriteAllText((Join-Path $fixtureRoot 'development.md'), 'unrelated development edit')
        Assert-RuntimeLaunchProfile $profile
    }
    Check 'saved listener overrides both host defaults and application Urls settings' {
        $values = Get-RuntimeEnvironment $profile
        if ($values['URLS'] -cne $profile.listenUrl -or $values['ASPNETCORE_URLS'] -cne $profile.listenUrl -or
            $values['Sources__AllowedRoots__repository'] -cne $profile.sourceRoot -or
            $values['ConnectionStrings__Kernel'] -cne $profile.database) { throw 'Launch settings were not pinned.' }
    }
    Check 'MSBuild excludes runtime state while retaining deployable host settings' {
        $project = Join-Path $PSScriptRoot '..\..\..\..\DantesRoleplay.MCPServer\DantesRoleplay.MCPServer.csproj'
        $output = & dotnet msbuild $project -nologo -getItem:Content,None
        if ($LASTEXITCODE -ne 0) { throw 'MSBuild item evaluation failed.' }
        $items = ($output -join '') | ConvertFrom-Json
        $runtimeItems = @(@($items.Items.Content) + @($items.Items.None) | Where-Object { $_.Identity -match '^data[\\/]' })
        if ($runtimeItems.Count -ne 0 -or @($items.Items.Content | Where-Object Identity -eq appsettings.json).Count -ne 1) {
            throw 'Build content includes runtime state or omits host settings.'
        }
    }
    Check 'line ending changes fail byte verification' {
        [IO.File]::WriteAllText($source, "exact`nbytes`n")
        Reject { Assert-RuntimeLaunchProfile $profile } 'bytes changed'
        [IO.File]::WriteAllText($source, "exact`r`nbytes`r`n")
    }
    Check 'same-length tampering of host files fails verification' {
        [IO.File]::WriteAllText($executable, 'DisposablE executable placeholder')
        Reject { Assert-RuntimeLaunchProfile $profile } 'bytes changed'
        [IO.File]::WriteAllText($executable, 'disposable executable placeholder')
    }
    Check 'unexpected host file is rejected' {
        $extra = Join-Path $hostRoot 'unexpected.json'
        [IO.File]::WriteAllText($extra, '{}')
        Reject { Assert-RuntimeLaunchProfile $profile } 'membership changed'
        [IO.File]::Delete($extra)
    }
    Check 'missing sources and database cannot become a fresh empty game' {
        foreach ($field in @('sourceRoot', 'database', 'blobRoot')) {
            $bad = Clone $profile; $bad.$field = Join-Path $fixtureRoot 'missing'
            Reject { Assert-RuntimeLaunchProfile $bad } 'missing'
        }
    }
    Check 'source-root substitution cannot reuse the old file pins' {
        $wrong = Join-Path $fixtureRoot 'wrong'; [IO.Directory]::CreateDirectory((Join-Path $wrong 'catalog')) | Out-Null
        [IO.File]::WriteAllText((Join-Path $wrong 'catalog/fixture.md'), "exact`nbytes`n")
        $bad = Clone $profile; $bad.sourceRoot = $wrong
        Reject { Assert-RuntimeLaunchProfile $bad } 'bytes changed'
    }
    Check 'path traversal and duplicate pins fail' {
        foreach ($path in @('../escape', 'C:\escape', 'catalog/../../escape', 'catalog/fixture.md:stream')) {
            Reject { Resolve-RuntimeChildPath $sourceRoot $path } 'Invalid|escapes'
        }
        $bad = Clone $profile; $bad.sourceFiles[0].path = '../escape'
        Reject { Assert-RuntimeLaunchProfile $bad } 'Invalid'
    }
    Check 'first target must probe the selected local listener' {
        $bad = Clone $profile; $bad.targets[0].origin = 'http://127.0.0.1:9999'
        Reject { Assert-RuntimeLaunchProfile $bad } 'First target'
    }
    Check 'origin identity includes the scheme and rejects duplicate complete origins' {
        $valid = Clone $profile
        $valid.targets += Clone $valid.targets[1]
        $valid.targets[2].origin = 'https://192.0.2.7'
        Assert-RuntimeLaunchProfile $valid
        $valid.targets[2].origin = $valid.targets[1].origin
        Reject { Assert-RuntimeLaunchProfile $valid } 'distinct exact'
    }
    Check 'malformed and unpinned target selection fails before launch' {
        $bad = Clone $profile; $bad.targets[1].origin = 'http://192.0.2.7/other'
        Reject { Assert-RuntimeLaunchProfile $bad } 'exact HTTP'
        $bad = Clone $profile; $bad.targets[0].expected.checks.database.fingerprint = $null
        Reject { Assert-RuntimeLaunchProfile $bad } 'fingerprint'
    }
    $runtime = Clone $expected
    $readiness = [pscustomobject]@{status = 'ready'; applicationId = 'sample'; checks = @($runtime.checks.PSObject.Properties | ForEach-Object {
        [pscustomobject]@{name = $_.Name; status = 'ready'; code = $_.Value.code; evidence = @{revision = $_.Value.revision; fingerprint = $_.Value.fingerprint}}
    })}
    Check 'matching runtime and audience are accepted' { Assert-RuntimeResponse $runtime $readiness $runtime.audience 'sample' }
    Check 'every readiness owner is checked for byte/revision drift' {
        foreach ($row in $readiness.checks) {
            foreach ($field in @('revision', 'fingerprint')) {
                $bad = Clone $readiness
                ($bad.checks | Where-Object name -eq $row.name).evidence.$field = 'changed'
                Reject { Assert-RuntimeResponse $runtime $bad $runtime.audience 'sample' } 'drift'
            }
        }
    }
    Check 'public target cannot borrow local audience policy evidence' {
        $otherAudience = Clone $runtime.audience; $otherAudience.policyRevision = 'C' * 64
        Reject { Assert-RuntimeResponse $runtime $readiness $otherAudience 'sample' } 'policyRevision drift'
    }
    Check 'failed, missing or duplicate readiness owners cannot pass' {
        $bad = Clone $readiness; $bad.status = 'failed'
        Reject { Assert-RuntimeResponse $runtime $bad $runtime.audience 'sample' } 'not ready'
        $bad = Clone $readiness; $bad.checks = @()
        Reject { Assert-RuntimeResponse $runtime $bad $runtime.audience 'sample' } 'drift'
        $bad = Clone $readiness; $bad.checks += $bad.checks[0]
        Reject { Assert-RuntimeResponse $runtime $bad $runtime.audience 'sample' } 'drift'
    }
    $process = [pscustomobject]@{ProcessId = 1234; ExecutablePath = $executable; CreationDate = [DateTime]::UtcNow}
    $receipt = Clone @{processId = 1234; executable = $executable; startedAtUtc = $process.CreationDate.ToString('o')}
    Check 'ownership survives persisted timestamps but not PID reuse or executable changes' {
        if (-not (Get-OwnedRuntimeProcess $receipt @($process))) { throw 'Owned process was not found.' }
        $other = [pscustomobject]@{ProcessId = 1234; ExecutablePath = $executable; CreationDate = $process.CreationDate.AddSeconds(1)}
        if (Get-OwnedRuntimeProcess $receipt @($other)) { throw 'PID reuse accepted.' }
        $other.CreationDate = $process.CreationDate; $other.ExecutablePath = 'another.exe'
        if (Get-OwnedRuntimeProcess $receipt @($other)) { throw 'Wrong executable accepted.' }
    }
    Check 'untracked and split IPv4/IPv6 listeners cannot satisfy or be stopped by launcher' {
        $listeners = @([pscustomobject]@{LocalAddress = '0.0.0.0'; LocalPort = 6217; OwningProcess = 1234})
        Assert-RuntimeListenerOwnership $listeners $process
        Reject { Assert-RuntimeListenerOwnership $listeners $null } 'Listener conflict'
        $listeners += [pscustomobject]@{LocalAddress = '::1'; LocalPort = 6217; OwningProcess = 9999}
        Reject { Assert-RuntimeListenerOwnership $listeners $process } 'PID 9999'
    }
    Write-Host "$script:passed runtime launch tests passed."
} finally {
    $resolvedFixture = [IO.Path]::GetFullPath($fixtureRoot)
    $tempPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (-not $resolvedFixture.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($resolvedFixture) -notlike 'roleplay-runtime-tests-*') { throw 'Unsafe fixture cleanup target.' }
    Remove-Item -LiteralPath $resolvedFixture -Recurse -Force
}
