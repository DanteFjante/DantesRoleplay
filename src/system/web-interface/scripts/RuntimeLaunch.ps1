#Requires -Version 5.1
# Local release selection is administration configuration, never game-state authority.

function Resolve-RuntimeChildPath {
    param([string] $Root, [string] $RelativePath)
    if ([string]::IsNullOrWhiteSpace($RelativePath) -or [IO.Path]::IsPathRooted($RelativePath) -or
        $RelativePath -match '(^|[\\/])\.\.([\\/]|$)|[:\x00-\x1f]') {
        throw "Invalid release-relative path: $RelativePath"
    }
    $prefix = [IO.Path]::GetFullPath($Root).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    $path = [IO.Path]::GetFullPath((Join-Path $prefix $RelativePath))
    if (-not $path.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Release path escapes its root.' }
    return $path
}

function Get-RuntimeFileInventory {
    param([string] $Root)
    $directory = Get-Item -LiteralPath $Root -ErrorAction Stop
    if (-not $directory.PSIsContainer -or ($directory.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "Release root must be an ordinary directory: $Root"
    }
    $prefix = $directory.FullName.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    function Visit-RuntimeDirectory([string] $Path) {
        foreach ($entry in Get-ChildItem -LiteralPath $Path -Force) {
            if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Release contains a filesystem link: $($entry.FullName)" }
            if ($entry.PSIsContainer) { Visit-RuntimeDirectory $entry.FullName }
            else {
                [pscustomobject]@{ path = $entry.FullName.Substring($prefix.Length).Replace('\', '/')
                    length = $entry.Length; sha256 = (Get-FileHash -LiteralPath $entry.FullName -Algorithm SHA256).Hash }
            }
        }
    }
    return @(Visit-RuntimeDirectory $directory.FullName | Sort-Object path)
}

function Write-RuntimeJson {
    param([string] $Path, $Value, [switch] $Replace)
    $path = [IO.Path]::GetFullPath($Path)
    $temporary = $path + '.' + [Guid]::NewGuid().ToString('N') + '.pending'
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path)) | Out-Null
    try {
        [IO.File]::WriteAllText($temporary, ($Value | ConvertTo-Json -Depth 40), (New-Object Text.UTF8Encoding $false))
        if ($Replace -and [IO.File]::Exists($path)) {
            [IO.File]::Replace($temporary, $path, ($path + '.' + [Guid]::NewGuid().ToString('N') + '.previous'))
        } else { [IO.File]::Move($temporary, $path) }
    } finally {
        if ([IO.File]::Exists($temporary)) { [IO.File]::Delete($temporary) }
    }
}

function Save-RuntimeLaunchProfile {
    [CmdletBinding()]
    param([string] $Path, [string] $HostRoot, [string] $SourceRoot, [string] $Database,
        [string] $BlobRoot, [string] $ApplicationId, [string] $ListenUrl,
        [object[]] $Targets, [hashtable] $Environment, [switch] $Replace)
    $profile = [ordered]@{
        schemaVersion = 1; hostRoot = [IO.Path]::GetFullPath($HostRoot); sourceRoot = [IO.Path]::GetFullPath($SourceRoot)
        executable = 'DantesRoleplay.MCPServer.exe'; database = [IO.Path]::GetFullPath($Database)
        blobRoot = [IO.Path]::GetFullPath($BlobRoot); applicationId = $ApplicationId; listenUrl = $ListenUrl
        targets = $Targets; environment = $Environment
        hostFiles = @(Get-RuntimeFileInventory $HostRoot); sourceFiles = @(Get-RuntimeFileInventory $SourceRoot)
    }
    # The caller supplies reviewed runtime pins; failed-server output cannot self-certify a release.
    Assert-RuntimeLaunchProfile ($profile | ConvertTo-Json -Depth 40 | ConvertFrom-Json)
    Write-RuntimeJson -Path $Path -Value $profile -Replace:$Replace
}

function Assert-RuntimePins {
    param($Expected)
    foreach ($name in @('database', 'application-registration', 'active-catalog-snapshot', 'catalog-materialization',
        'extension-resolution', 'query-callability', 'web-page-release', 'audience-binding')) {
        $pin = $Expected.checks.$name
        if (-not $pin.code) { throw "Missing runtime pin: $name" }
        if ($name -ne 'audience-binding' -and $pin.fingerprint -cnotmatch '^[0-9A-F]{64}$') { throw "Missing exact runtime fingerprint: $name" }
    }
    foreach ($name in @('applicationId', 'stateSpaceId', 'campaignId', 'role', 'policyRevision', 'bindingRevision')) {
        if (-not $Expected.audience.$name) { throw "Missing audience pin: $name" }
    }
}

function Assert-RuntimeLaunchProfile {
    param($Profile)
    if ($Profile.schemaVersion -ne 1) { throw 'Unsupported runtime launch profile.' }
    foreach ($name in @('hostRoot', 'sourceRoot', 'database', 'blobRoot')) {
        $value = [string]$Profile.$name
        if (-not [IO.Path]::IsPathRooted($value) -or -not (Test-Path -LiteralPath $value)) { throw "Saved runtime $name is missing or not absolute: $value" }
    }
    if (-not (Test-Path -LiteralPath $Profile.database -PathType Leaf) -or
        -not (Test-Path -LiteralPath $Profile.blobRoot -PathType Container) -or
        -not (Test-Path -LiteralPath (Join-Path $Profile.sourceRoot 'catalog') -PathType Container)) {
        throw 'The selected database, blobs, or catalog source root is invalid.'
    }
    $executable = Resolve-RuntimeChildPath $Profile.hostRoot $Profile.executable
    if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) { throw "Release executable missing: $executable" }
    if ($Profile.applicationId -notmatch '^[a-z0-9][a-z0-9._-]*$') { throw 'Invalid application selection.' }
    $listen = [uri]$Profile.listenUrl
    if ($listen.Scheme -ne 'http' -or $listen.Host -notin @('0.0.0.0', '127.0.0.1', '[::]', '[::1]') -or
        $listen.AbsolutePath -ne '/' -or $listen.UserInfo -or $listen.Query -or $listen.Fragment) { throw 'Select one explicit local HTTP listener.' }
    if (@($Profile.targets).Count -lt 1) { throw 'Reviewed runtime probe targets are required.' }
    $origins = @{}
    foreach ($target in $Profile.targets) {
        $origin = [uri]$target.origin
        if ($origin.Scheme -notin @('http', 'https') -or $origin.UserInfo -or $origin.Query -or
            $origin.Fragment -or $origin.AbsolutePath -ne '/' -or $origins.ContainsKey($origin.GetLeftPart([UriPartial]::Authority))) {
            throw 'Probe targets must be distinct exact HTTP(S) origins without credentials or paths.'
        }
        $origins[$origin.GetLeftPart([UriPartial]::Authority)] = $true
        Assert-RuntimePins $target.expected
    }
    $first = [uri]$Profile.targets[0].origin
    if (-not $first.IsLoopback -or $first.Port -ne $listen.Port -or $first.Scheme -ne $listen.Scheme) { throw 'First target must directly probe the selected local listener.' }
    foreach ($kind in @('host', 'source')) {
        $root = if ($kind -eq 'host') { $Profile.hostRoot } else { $Profile.sourceRoot }
        $files = @($Profile.($kind + 'Files'))
        if ($files.Count -eq 0) { throw "Empty $kind file inventory." }
        $actual = @(Get-RuntimeFileInventory $root)
        if ($actual.Count -ne $files.Count) { throw "Release $kind file membership changed." }
        $seen = @{}; $pins = @{}
        foreach ($file in $files) {
            $path = Resolve-RuntimeChildPath $root $file.path
            if ($seen.ContainsKey($path)) { throw 'Duplicate release file.' }
            $seen[$path] = $true; $pins[$file.path] = $file
            if ($file.sha256 -cnotmatch '^[0-9A-F]{64}$' -or $file.length -lt 0 -or
                -not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Invalid release file: $path" }
        }
        foreach ($file in $actual) {
            $pin = $pins[$file.path]
            if (-not $pin -or $file.length -ne $pin.length -or $file.sha256 -cne $pin.sha256) { throw "Release bytes changed: $kind/$($file.path). Restore reviewed bytes or select a reviewed release." }
        }
    }
}

function Assert-RuntimeResponse {
    param($Expected, $Readiness, $Audience, [string] $ApplicationId)
    Assert-RuntimePins $Expected
    if ($Readiness.status -ne 'ready' -or $Readiness.applicationId -cne $ApplicationId) { throw "Application not ready: $($Readiness | ConvertTo-Json -Depth 12 -Compress)" }
    foreach ($property in $Expected.checks.PSObject.Properties) {
        $matches = @($Readiness.checks | Where-Object name -CEQ $property.Name)
        if ($matches.Count -ne 1 -or $matches[0].status -cne 'ready' -or $matches[0].code -cne $property.Value.code) { throw "Runtime readiness drift: $($property.Name)" }
        foreach ($field in @('revision', 'fingerprint')) {
            if ($matches[0].evidence.$field -cne $property.Value.$field) { throw "Runtime $($property.Name) $field drift. Select a matching reviewed release." }
        }
    }
    if ($Audience.status -cne 'bound') { throw 'Runtime audience is not bound.' }
    foreach ($field in @('applicationId', 'stateSpaceId', 'campaignId', 'role', 'actorId', 'policyRevision', 'bindingRevision', 'participationRevision')) {
        if ($Audience.$field -cne $Expected.audience.$field) { throw "Runtime audience $field drift." }
    }
}

function Test-RuntimeTarget {
    param($Profile, $Target)
    $origin = ([uri]$Target.origin).GetLeftPart([UriPartial]::Authority)
    $readiness = Invoke-RestMethod -Uri "$origin/api/readiness/applications/$($Profile.applicationId)" -TimeoutSec 15 -MaximumRedirection 0
    $audience = Invoke-RestMethod -Uri "$origin/api/audience-context" -TimeoutSec 15 -MaximumRedirection 0
    Assert-RuntimeResponse $Target.expected $readiness $audience $Profile.applicationId
    return $readiness
}

function Get-RuntimeEnvironment {
    param($Profile)
    $values = @{}
    foreach ($property in $Profile.environment.PSObject.Properties) { $values[$property.Name] = $property.Value }
    # Unprefixed URLS is application configuration: appsettings.json can override
    # the ASPNETCORE_URLS host fallback. Pin both to the same reviewed listener.
    $values['URLS'] = $Profile.listenUrl
    $values['ASPNETCORE_URLS'] = $Profile.listenUrl
    $values['ConnectionStrings__Kernel'] = $Profile.database
    $values['BlobStorage__Root'] = $Profile.blobRoot
    $values['Sources__AllowedRoots__repository'] = $Profile.sourceRoot
    return $values
}

function Get-OwnedRuntimeProcess {
    param($Receipt, [object[]] $Processes)
    if (-not $Receipt) { return $null }
    $matches = @($Processes | Where-Object {
        $_.ProcessId -eq $Receipt.processId -and $_.ExecutablePath -eq $Receipt.executable -and
        $_.CreationDate.ToUniversalTime().Ticks -eq ([DateTime]$Receipt.startedAtUtc).ToUniversalTime().Ticks
    })
    if ($matches.Count -eq 1) { return $matches[0] }
    return $null
}

function Assert-RuntimeListenerOwnership {
    param([object[]] $Listeners, $OwnedProcess)
    foreach ($listener in $Listeners) {
        if (-not $OwnedProcess -or $listener.OwningProcess -ne $OwnedProcess.ProcessId) {
            throw "Listener conflict at $($listener.LocalAddress):$($listener.LocalPort), PID $($listener.OwningProcess). No process was stopped. Inspect that instance before replacing it."
        }
    }
}
