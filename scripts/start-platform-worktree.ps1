#Requires -Version 5.1
<##
.SYNOPSIS
    Starts the MCP/web host with disposable storage owned by this worktree.
.DESCRIPTION
    The host receives every storage and listener setting on its command line. This keeps
    appsettings.Local.json and inherited machine settings from selecting live state. The
    generated directory is local runtime data under the repository's ignored .tmp directory.
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\scripts\start-platform-worktree.ps1 -ValidateOnly
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\scripts\start-platform-worktree.ps1
#>
[CmdletBinding()]
param(
    [ValidateRange(1024, 65535)]
    [int] $Port,
    [switch] $ValidateOnly,
    [string] $RuntimeRoot
)

$ErrorActionPreference = 'Stop'

function Resolve-WorktreeRoot {
    $root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
    if (-not (Test-Path -LiteralPath (Join-Path $root 'AGENTS.md') -PathType Leaf) -or
        -not (Test-Path -LiteralPath (Join-Path $root 'DantesRoleplay.MCPServer\DantesRoleplay.MCPServer.csproj') -PathType Leaf)) {
        throw "The script must run inside a DantesRoleplay worktree: $root"
    }
    return $root
}

function Get-FreeLoopbackPort {
    param([int] $Requested)
    if ($Requested) {
        $probe = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, $Requested)
        try { $probe.Start(); return $Requested } finally { $probe.Stop() }
    }
    $probe = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    try { $probe.Start(); return ([Net.IPEndPoint]$probe.LocalEndpoint).Port } finally { $probe.Stop() }
}

$root = Resolve-WorktreeRoot
$runtimePrefix = ([IO.Path]::GetFullPath((Join-Path $root '.tmp\platform-runtime'))).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
$runtimeBase = if ($RuntimeRoot) {
    [IO.Path]::GetFullPath($RuntimeRoot)
} else {
    Join-Path $root ('.tmp\platform-runtime\' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ') + '-' + [Guid]::NewGuid().ToString('N'))
}
if (-not $runtimeBase.StartsWith($runtimePrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "RuntimeRoot must be inside $root\.tmp\platform-runtime."
}
if (Test-Path -LiteralPath $runtimeBase) { throw "RuntimeRoot already exists: $runtimeBase" }
$worktreePrefix = ([IO.Path]::GetFullPath($root)).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
$cursor = [IO.DirectoryInfo]([IO.Path]::GetDirectoryName($runtimeBase))
while ($cursor) {
    $insideWorktree = $cursor.FullName.Equals($root, [StringComparison]::OrdinalIgnoreCase) -or $cursor.FullName.StartsWith($worktreePrefix, [StringComparison]::OrdinalIgnoreCase)
    if (-not $insideWorktree) { break }
    if ($cursor.Attributes -band [IO.FileAttributes]::ReparsePoint) {
        throw "Runtime path contains a reparse point: $($cursor.FullName)"
    }
    $cursor = $cursor.Parent
}
$database = Join-Path $runtimeBase 'data\dantesroleplay.db'
$blobs = Join-Path $runtimeBase 'blobs'
$derived = Join-Path $runtimeBase 'derived'
$output = Join-Path $runtimeBase 'output'
$codexExecutable = Join-Path $runtimeBase 'codex-disabled.exe'
$port = Get-FreeLoopbackPort $Port
$listenUrl = "http://127.0.0.1:$port"

Write-Host "Worktree: $root"
Write-Host "Runtime:  $runtimeBase"
Write-Host "Database: $database"
Write-Host "Blobs:    $blobs"
Write-Host "Derived:  $derived"
Write-Host "Output:   $output"
Write-Host "Listener: $listenUrl"
Write-Host 'Providers: embedding, remote planning, local completion, local outer provider, and Codex bridge disabled.'

if ($ValidateOnly) { return }

foreach ($path in @((Split-Path $database), $blobs, $derived, $output)) {
    [IO.Directory]::CreateDirectory($path) | Out-Null
}

$project = Join-Path $root 'DantesRoleplay.MCPServer\DantesRoleplay.MCPServer.csproj'
$arguments = @(
    'run', '--project', $project, '--no-launch-profile', '--',
    "--environment=Development",
    "--urls=$listenUrl",
    "--ConnectionStrings:Kernel=$database",
    "--BlobStorage:Root=$blobs",
    "--Retrieval:DerivedDataDirectory=$derived",
    '--Retrieval:Embedding:Enabled=false',
    '--InteractionPlanning:Remote:Enabled=false',
    '--Knowledge:Completion:Enabled=false',
    '--InteractionOuter:Local:Enabled=false',
    "--Sources:AllowedRoots:repository=$root",
    "--Codex:RepositoryRoot=$root",
    "--Codex:ExecutablePath=$codexExecutable"
)
$environmentNames = @('ASPNETCORE_ENVIRONMENT', 'ASPNETCORE_URLS', 'URLS')
$previous = @{}
foreach ($name in $environmentNames) { $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
try {
    [Environment]::SetEnvironmentVariable('ASPNETCORE_ENVIRONMENT', 'Development', 'Process')
    [Environment]::SetEnvironmentVariable('ASPNETCORE_URLS', $listenUrl, 'Process')
    [Environment]::SetEnvironmentVariable('URLS', $listenUrl, 'Process')
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "The platform host exited with code $LASTEXITCODE." }
}
finally {
    foreach ($name in $environmentNames) { [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process') }
}
