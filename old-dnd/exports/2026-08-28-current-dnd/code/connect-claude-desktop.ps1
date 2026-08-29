#Requires -Version 5.1
<#
.SYNOPSIS
    Registers the local DantesRoleplay MCP server with Claude Desktop.

.DESCRIPTION
    Claude Desktop's claude_desktop_config.json only accepts stdio servers -- it has no
    field for a streamable-HTTP URL, and its "Add custom connector" UI resolves URLs from
    Anthropic's cloud, so it cannot reach localhost. The supported bridge is `mcp-remote`:
    a small Node process Claude Desktop launches over stdio, which forwards to
    http://127.0.0.1:6217/mcp. Nothing is exposed off this machine.

    This script is idempotent and merge-safe: it preserves any other mcpServers you
    already have and backs up the existing config before writing.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\connect-claude-desktop.ps1

.EXAMPLE
    # Preview without writing anything
    powershell -ExecutionPolicy Bypass -File .\connect-claude-desktop.ps1 -WhatIfOnly

.EXAMPLE
    # Remove the entry again
    powershell -ExecutionPolicy Bypass -File .\connect-claude-desktop.ps1 -Remove

.EXAMPLE
    # Just report where everything lives and whether it exists
    powershell -ExecutionPolicy Bypass -File .\connect-claude-desktop.ps1 -Diagnose
#>
[CmdletBinding()]
param(
    [string] $ServerName = 'dantesroleplay',
    # 127.0.0.1 rather than localhost on purpose: Node resolves localhost to ::1 first on
    # some Windows configurations, and a server listening only on IPv4 is then unreachable
    # through a bridge that connects fine from a browser.
    [string] $McpUrl     = 'http://127.0.0.1:6217/mcp',
    [string] $ConfigDir,
    [switch] $Remove,
    [switch] $WhatIfOnly,
    [switch] $Diagnose
)

$ErrorActionPreference = 'Stop'

function Write-Step { param($m) Write-Host "==> $m" -ForegroundColor Cyan }
function Write-Ok   { param($m) Write-Host "    OK   $m" -ForegroundColor Green }
function Write-Warn { param($m) Write-Host "    WARN $m" -ForegroundColor Yellow }
function Write-Bad  { param($m) Write-Host "    FAIL $m" -ForegroundColor Red }


# ---------------------------------------------------------------------------
# 1. Locate the Claude Desktop config directory.
#
# There are TWO possible locations, and picking the wrong one fails silently.
#
#   Standard installer  ->  %APPDATA%\Claude\
#   Microsoft Store     ->  %LOCALAPPDATA%\Packages\Claude_<hash>\LocalCache\Roaming\Claude\
#
# The Store build ships as an MSIX package, and MSIX filesystem virtualization redirects
# Electron's app.getPath("userData") into the package container. The app therefore reads
# the LocalCache path, while %APPDATA%\Claude stays empty (or, worse, holds a file that is
# never read). Anthropic tracks this as a bug -- on MSIX, Settings -> Developer -> Edit
# Config opens the NON-virtualized path, so the file you edit by hand and the file the app
# loads are two different files that never sync:
#   https://github.com/anthropics/claude-code/issues/25579
#   https://github.com/anthropics/claude-code/issues/26073
#
# Detection is by probing for the package directory rather than by parsing the exe path,
# so it still works when Claude Desktop is not running. The hash is globbed, not
# hardcoded -- it is stable in practice but not guaranteed.
# ---------------------------------------------------------------------------
Write-Step 'Locating Claude Desktop configuration directory'

if (-not $env:APPDATA) {
    Write-Bad 'APPDATA is not set. Run this from Windows PowerShell, not a POSIX shell.'
    exit 1
}

$standardDir = Join-Path $env:APPDATA 'Claude'

$pkgDirs = @()
if ($env:LOCALAPPDATA) {
    $packagesRoot = Join-Path $env:LOCALAPPDATA 'Packages'
    if (Test-Path -LiteralPath $packagesRoot) {
        $pkgDirs = @(Get-ChildItem -LiteralPath $packagesRoot -Filter 'Claude_*' -Directory `
                        -Force -ErrorAction SilentlyContinue)
    }
}

# Score candidates so an installed-but-never-launched package does not beat a package that
# already holds a real config.
$msixDir = $null
if ($pkgDirs.Count -gt 0) {
    $scored = foreach ($p in $pkgDirs) {
        $d = Join-Path $p.FullName 'LocalCache\Roaming\Claude'
        [pscustomobject]@{
            Dir   = $d
            Score = (2 * [int](Test-Path -LiteralPath (Join-Path $d 'claude_desktop_config.json'))) +
                    [int](Test-Path -LiteralPath $d)
        }
    }
    $msixDir = ($scored | Sort-Object Score -Descending | Select-Object -First 1).Dir
}

if ($ConfigDir) {
    $configDir = $ConfigDir
    $installKind = 'explicit -ConfigDir override'
} elseif ($msixDir) {
    $configDir = $msixDir
    $installKind = 'Microsoft Store (MSIX, virtualized path)'
} else {
    $configDir = $standardDir
    $installKind = 'standard installer'
}
$configPath = Join-Path $configDir 'claude_desktop_config.json'

Write-Host ''
Write-Host "    Install type : $installKind" -ForegroundColor White
Write-Host "    Config file  : $configPath" -ForegroundColor White
Write-Host ''

# Report the surrounding landscape. "I don't have that folder" has two causes, and both
# look identical from Explorer: AppData carries the Hidden attribute and is invisible
# unless View -> Show -> Hidden items is on, AND on MSIX the documented folder genuinely
# does not exist because the real one lives under Packages.
$landscape = [ordered]@{
    'AppData\Roaming'             = $env:APPDATA
    'Documented dir (installer)'  = $standardDir
}
if ($msixDir) {
    $landscape['MSIX dir (REAL, in use)'] = $msixDir
    $landscape['MSIX logs']               = (Join-Path $msixDir 'logs')
} elseif ($env:LOCALAPPDATA) {
    $landscape['Installer app dir']       = (Join-Path $env:LOCALAPPDATA 'AnthropicClaude')
}
$landscape['claude_desktop_config'] = $configPath

foreach ($k in $landscape.Keys) {
    $p = $landscape[$k]
    if (-not $p) { continue }
    if (Test-Path -LiteralPath $p) {
        Write-Ok    ("{0,-28} exists   {1}" -f $k, $p)
    } else {
        Write-Warn  ("{0,-28} MISSING  {1}" -f $k, $p)
    }
}

# Is Claude Desktop actually installed? If the process is running we get the exact exe.
$claudeProc = Get-Process -Name 'Claude' -ErrorAction SilentlyContinue |
              Select-Object -First 1
$installerDir = if ($env:LOCALAPPDATA) { Join-Path $env:LOCALAPPDATA 'AnthropicClaude' } else { $null }
# Path is checked, not just the process name: "claude" also matches the Claude Code CLI,
# which would produce a confident and wrong "Desktop is running".
if ($claudeProc -and $claudeProc.Path -and
    $claudeProc.Path -match 'WindowsApps|AnthropicClaude|Claude\.exe') {
    Write-Ok "Claude Desktop is running: $($claudeProc.Path)"
} elseif ($msixDir -or ($installerDir -and (Test-Path -LiteralPath $installerDir))) {
    Write-Ok 'Claude Desktop is installed (not currently running).'
} else {
    Write-Warn 'Could not confirm Claude Desktop is installed on this machine.'
}

# On MSIX the documented path is the classic trap: it is exactly where the docs and the
# app's own "Edit Config" button send you, and a config placed there is never loaded.
if ($msixDir -and (Test-Path -LiteralPath (Join-Path $standardDir 'claude_desktop_config.json'))) {
    Write-Warn 'A config exists at the DOCUMENTED path, which this MSIX build never reads:'
    Write-Warn "    $(Join-Path $standardDir 'claude_desktop_config.json')"
    Write-Warn 'It is being ignored. This script writes to the MSIX path shown above.'
}

if (-not (Test-Path -LiteralPath $configPath)) {
    Write-Host ''
    Write-Ok 'No config file yet. That is normal -- Claude Desktop does not create one'
    Write-Ok 'until you use Settings -> Developer -> Edit Config. This script creates it.'
}

if ($Diagnose) {
    Write-Host ''
    Write-Step 'Diagnose only -- nothing written'
    Write-Host "    Open the folder with:  explorer.exe `"$configDir`"" -ForegroundColor DarkGray
    exit 0
}


# ---------------------------------------------------------------------------
# 2. Check prerequisites: Node/npx (mcp-remote runs under npx).
# ---------------------------------------------------------------------------
Write-Step 'Checking prerequisites'

$npxCmd = Get-Command npx -ErrorAction SilentlyContinue
if ($npxCmd) {
    $nodeVersion = (& node --version 2>$null)
    Write-Ok "node $nodeVersion / npx found at $($npxCmd.Source)"
} else {
    Write-Bad 'npx was not found on PATH. Install Node.js LTS from https://nodejs.org/'
    Write-Bad 'mcp-remote cannot run without it. Aborting.'
    exit 1
}

# Claude Desktop launches the server with a bare process, NOT through your shell, so a
# .cmd shim on PATH is resolved differently than it is here. Record the absolute path and
# use "npx.cmd" explicitly -- a plain "npx" command string is the single most common
# cause of a server that silently fails to start on Windows.
$npxExe = if ($npxCmd.Source -like '*.cmd') { $npxCmd.Source } else { 'npx.cmd' }
Write-Ok "Will launch via: $npxExe"


# ---------------------------------------------------------------------------
# 3. Is the API actually up? Not fatal -- you can start it later -- but catching a
#    stopped API now saves a confusing "server disconnected" in Claude Desktop.
# ---------------------------------------------------------------------------
Write-Step "Probing $McpUrl"
try {
    # A bare GET on the MCP route is expected to be rejected (405/406/400) by the
    # streamable-HTTP handler. Any HTTP response at all proves the API is listening.
    $null = Invoke-WebRequest -Uri $McpUrl -Method Get -TimeoutSec 5 -UseBasicParsing
    Write-Ok 'API responded.'
} catch [System.Net.WebException] {
    if ($_.Exception.Response) {
        $code = [int]$_.Exception.Response.StatusCode
        Write-Ok "API is listening (HTTP $code on a bare GET is expected for MCP)."
    } else {
        Write-Warn 'Could not reach the server. Run `dotnet run --project DantesRoleplay.MCPServer` first.'
    }
} catch {
    if ($_.Exception.Message -match '40\d|Method Not Allowed|Not Acceptable|Bad Request') {
        Write-Ok 'API is listening (rejecting a bare GET is expected for MCP).'
    } else {
        Write-Warn 'Could not reach the server. Run `dotnet run --project DantesRoleplay.MCPServer` first.'
    }
}


# ---------------------------------------------------------------------------
# 4. Load the existing config.
#
# The object graph from ConvertFrom-Json is edited in place via Add-Member rather than
# rebuilt into hashtables. A rebuild is tempting but wrong: in PowerShell every value is
# a PSObject, so a generic "does it have properties?" recursion descends INTO strings and
# rewrites "-y" as {"Length":2} -- silently corrupting the args of every other MCP server
# in the file. Touching only the one property we own avoids the whole class of problem.
# ---------------------------------------------------------------------------
Write-Step 'Reading existing configuration'

$config = $null
if (Test-Path -LiteralPath $configPath) {
    $raw = Get-Content -LiteralPath $configPath -Raw -Encoding UTF8
    if ($raw -and $raw.Trim()) {
        try {
            $config = ConvertFrom-Json $raw
            Write-Ok "Loaded existing config ($($raw.Length) bytes)."
        } catch {
            Write-Bad "Existing config is not valid JSON: $($_.Exception.Message)"
            Write-Bad 'Fix or delete it first; refusing to overwrite it blindly.'
            exit 1
        }
    } else {
        Write-Ok 'Existing config is empty; starting fresh.'
    }
} else {
    Write-Ok 'No config file yet; one will be created.'
}

if ($null -eq $config) { $config = [pscustomobject]@{} }

if (-not $config.PSObject.Properties['mcpServers'] -or $null -eq $config.mcpServers) {
    $config | Add-Member -MemberType NoteProperty -Name 'mcpServers' `
                         -Value ([pscustomobject]@{}) -Force
}
$servers = $config.mcpServers

$existingNames = @($servers.PSObject.Properties | ForEach-Object { $_.Name } |
                   Where-Object { $_ -and $_.Trim() })
if ($existingNames.Count -gt 0) {
    Write-Ok "Preserving existing servers: $($existingNames -join ', ')"
}


# ---------------------------------------------------------------------------
# 5. Apply the change.
#
# --transport http-only: the API registers streamable HTTP (Program.cs -> MapMcp("/mcp")
# with HttpServerSessionMode.Stateless). mcp-remote defaults to trying deprecated SSE
# first, which just costs a failed round-trip here, so pin the transport.
# --allow-http: mcp-remote requires opt-in for non-HTTPS origins.
# ---------------------------------------------------------------------------
if ($Remove) {
    Write-Step "Removing '$ServerName'"
    if ($servers.PSObject.Properties[$ServerName]) {
        $servers.PSObject.Properties.Remove($ServerName)
        Write-Ok 'Entry removed.'
    } else {
        Write-Ok 'Entry was not present; nothing to do.'
    }
} else {
    Write-Step "Registering '$ServerName' -> $McpUrl"
    if ($servers.PSObject.Properties[$ServerName]) {
        Write-Warn "Entry '$ServerName' already existed and will be replaced."
    }
    $entry = [pscustomobject]@{
        command = $npxExe
        args    = @('-y', 'mcp-remote', $McpUrl, '--transport', 'http-only', '--allow-http')
    }
    $servers | Add-Member -MemberType NoteProperty -Name $ServerName -Value $entry -Force
}

$json = $config | ConvertTo-Json -Depth 12

if ($WhatIfOnly) {
    Write-Step 'Preview only -- nothing written'
    Write-Host ''
    Write-Host "Target: $configPath" -ForegroundColor DarkGray
    Write-Host $json
    exit 0
}


# ---------------------------------------------------------------------------
# 6. Back up and write.
# ---------------------------------------------------------------------------
Write-Step 'Writing configuration'

if (-not (Test-Path -LiteralPath $configDir)) {
    New-Item -ItemType Directory -Path $configDir -Force | Out-Null
    Write-Ok "Created $configDir"
}

if (Test-Path -LiteralPath $configPath) {
    $backup = "$configPath.bak-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
    Copy-Item -LiteralPath $configPath -Destination $backup
    Write-Ok "Backed up to $(Split-Path -Leaf $backup)"
}

# Write UTF-8 without BOM. Set-Content -Encoding UTF8 emits a BOM on PS 5.1, and a BOM
# in front of '{' makes some JSON parsers reject the file outright.
[System.IO.File]::WriteAllText($configPath, $json, (New-Object System.Text.UTF8Encoding($false)))
Write-Ok "Wrote $configPath"

Write-Host ''
Write-Host $json -ForegroundColor DarkGray
Write-Host ''
Write-Step 'Next steps'
Write-Host @"
    1. Start the server if it is not already running:
          dotnet run --project DantesRoleplay.MCPServer
    2. Fully quit Claude Desktop -- close the window AND right-click the system-tray
       icon and choose Quit. Restarting the window alone does not reload this file.
    3. Reopen Claude Desktop and start a NORMAL chat (not a Cowork task -- Cowork runs
       sandboxed and cannot reach localhost either way).
    4. The '$ServerName' tools appear under the connectors menu in the message box.
       First run downloads mcp-remote via npx and may take ~20s.

    Try:  "Call orient and tell me what this system is and what state it is in."

    If the server does not appear, check %APPDATA%\Claude\logs\mcp.log and
    mcp-server-$ServerName.log.
"@
