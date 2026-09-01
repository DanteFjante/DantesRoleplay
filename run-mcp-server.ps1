#Requires -Version 5.1
<#
.SYNOPSIS
    Starts the local DantesRoleplay MCP server the way the http launch profile does.

.DESCRIPTION
    Launching the built exe with a bare Start-Process does NOT work: it binds port 5000 with
    Production configuration, and the mcp-remote bridge Claude Desktop uses never reconnects.
    The launch profile in Properties/launchSettings.json supplies both the URL and a set of
    Knowledge__LocalPlayer__* environment variables that OVERRIDE appsettings.json. This script
    reproduces that environment so the server comes up on 6217 with the intended player seat.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\run-mcp-server.ps1

.EXAMPLE
    # Stop whatever is running first (required before any build -- the exe locks its own DLLs)
    powershell -ExecutionPolicy Bypass -File .\run-mcp-server.ps1 -Restart
#>
[CmdletBinding()]
param(
    [switch] $Restart,
    [string] $Role     = 'Actor',
    [string] $ActorId  = 'actor.caldris.ganji',
    [string] $Campaign = 'campaign.caldris.measure-of-mercy'
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$exe  = Join-Path $root 'DantesRoleplay.MCPServer\bin\Debug\net10.0\win-x64\DantesRoleplay.MCPServer.exe'

if (-not (Test-Path $exe)) { throw "Not built yet: $exe. Run: dotnet build DantesRoleplay.slnx" }

if ($Restart) {
    Get-Process DantesRoleplay.MCPServer -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 4
}

if (Get-Process DantesRoleplay.MCPServer -ErrorAction SilentlyContinue) {
    Write-Host 'Already running. Use -Restart to replace it.' -ForegroundColor Yellow
    return
}

$env:ASPNETCORE_ENVIRONMENT              = 'Development'
$env:ASPNETCORE_URLS                     = 'http://localhost:6217'
$env:DANTESROLEPLAY_OLLAMA_COMPLETION    = 'true'
$env:Knowledge__Completion__Enabled      = 'true'
$env:Knowledge__LocalPlayer__Enabled     = 'true'
$env:Knowledge__LocalPlayer__PrincipalId = 'local.player'
$env:Knowledge__LocalPlayer__ApplicationId = 'dnd2024'
$env:Knowledge__LocalPlayer__CampaignId  = $Campaign
$env:Knowledge__LocalPlayer__Role        = $Role
# A GameMaster seat must have NO actor; an Actor seat must have one.
if ($Role -eq 'Actor') { $env:Knowledge__LocalPlayer__ActorId = $ActorId }
else { Remove-Item Env:\Knowledge__LocalPlayer__ActorId -ErrorAction SilentlyContinue }

# Hand the launch to cmd's `start`, which orphans the child. A plain Start-Process leaves the
# server tied to this console, so closing the window that ran this script kills the server --
# which is exactly what happened on 2026-09-01.
$workdir = Join-Path $root 'DantesRoleplay.MCPServer'
# Start-Process validates every item in an argument array. Keep cmd's required empty
# window-title token inside one command string so it is passed literally rather than
# being bound as an empty PowerShell argument.
$cmdArguments = '/c start "" /D "{0}" /B "{1}"' -f $workdir, $exe
Start-Process -FilePath 'cmd.exe' `
    -ArgumentList $cmdArguments `
    -WindowStyle Hidden
Write-Host "Starting as seat Role=$Role ..." -ForegroundColor Cyan

for ($i = 0; $i -lt 40; $i++) {
    Start-Sleep -Seconds 2
    if (Get-NetTCPConnection -LocalPort 6217 -State Listen -ErrorAction SilentlyContinue) {
        Write-Host '    OK   listening on http://localhost:6217' -ForegroundColor Green
        Write-Host '         Safe to close this window; the server keeps running.' -ForegroundColor DarkGray
        return
    }
}
Write-Host '    FAIL never started listening on 6217.' -ForegroundColor Red
