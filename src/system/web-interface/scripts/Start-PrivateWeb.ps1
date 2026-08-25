[CmdletBinding()]
param(
    [ValidateRange(1024, 65535)]
    [int]$Port = 6217
)

$ErrorActionPreference = 'Stop'
$serveCreated = $false
$createdServeStatus = $null
$environmentNames = @(
    'ASPNETCORE_ENVIRONMENT',
    'ASPNETCORE_URLS',
    'WebInterface__RemoteAccess__Enabled',
    'WebInterface__RemoteAccess__TailscaleHost',
    'WebInterface__RemoteAccess__AllowedLogins__0'
)
$previousEnvironment = @{}
foreach ($name in $environmentNames) {
    $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}

try {
    $tailscaleCommand = Get-Command 'tailscale' -ErrorAction Stop
    $status = & $tailscaleCommand.Source status --json | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or $status.BackendState -ne 'Running') {
        throw 'Tailscale must be installed, signed in, and running.'
    }

    $hostName = ([string]$status.Self.DNSName).Trim().TrimEnd('.')
    $userId = [string]$status.Self.UserID
    $user = $status.User.PSObject.Properties |
        Where-Object { $_.Name -eq $userId } |
        Select-Object -First 1 -ExpandProperty Value
    $login = [string]$user.LoginName
    if ([string]::IsNullOrWhiteSpace($hostName) -or
        -not $hostName.EndsWith('.ts.net', [StringComparison]::OrdinalIgnoreCase) -or
        [string]::IsNullOrWhiteSpace($login)) {
        throw 'The signed-in Tailscale hostname or user identity could not be resolved.'
    }

    $serveStatus = & $tailscaleCommand.Source serve status --json | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0) {
        throw 'The current Tailscale Serve configuration could not be read.'
    }
    if (@($serveStatus.PSObject.Properties).Count -ne 0) {
        throw 'Tailscale Serve already has a configuration. It was left unchanged.'
    }

    [Environment]::SetEnvironmentVariable('ASPNETCORE_ENVIRONMENT', 'Production', 'Process')
    [Environment]::SetEnvironmentVariable('ASPNETCORE_URLS', "http://127.0.0.1:$Port", 'Process')
    [Environment]::SetEnvironmentVariable('WebInterface__RemoteAccess__Enabled', 'true', 'Process')
    [Environment]::SetEnvironmentVariable('WebInterface__RemoteAccess__TailscaleHost', $hostName, 'Process')
    [Environment]::SetEnvironmentVariable('WebInterface__RemoteAccess__AllowedLogins__0', $login, 'Process')

    & $tailscaleCommand.Source serve --bg --yes $Port
    if ($LASTEXITCODE -ne 0) {
        throw 'Tailscale Serve could not be enabled.'
    }
    $serveCreated = $true
    $createdServeStatus = ((& $tailscaleCommand.Source serve status --json) -join "`n").Trim()

    $repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..\..')).Path
    $project = Join-Path $repositoryRoot 'DantesRoleplay.MCPServer\DantesRoleplay.MCPServer.csproj'
    Write-Host "Private web access is available at https://$hostName while this process runs."
    Write-Host 'Press Ctrl+C to stop the host and remove its Tailscale Serve mapping.'
    & dotnet run --project $project --no-launch-profile
    if ($LASTEXITCODE -ne 0) {
        throw "The roleplay host exited with code $LASTEXITCODE."
    }
}
finally {
    if ($serveCreated) {
        $currentServeStatus = ((& tailscale serve status --json) -join "`n").Trim()
        if ($currentServeStatus -eq $createdServeStatus) {
            & tailscale serve reset | Out-Null
        }
        else {
            Write-Warning 'Tailscale Serve changed while the host was running, so it was left unchanged.'
        }
    }

    foreach ($name in $environmentNames) {
        [Environment]::SetEnvironmentVariable($name, $previousEnvironment[$name], 'Process')
    }
}
