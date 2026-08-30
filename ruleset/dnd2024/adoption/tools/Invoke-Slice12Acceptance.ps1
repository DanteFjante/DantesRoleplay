[CmdletBinding()]
param(
    [string]$DotnetCommand = 'dotnet',
    [string]$NodeCommand = 'node',
    [string]$NpxCommand = 'npx',
    [string]$PwshCommand = 'pwsh',
    [string]$ReportPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
$adoption = Join-Path $repo 'ruleset/dnd2024/adoption'
$results = [Collections.Generic.List[object]]::new()

function Invoke-Checked {
    param([string]$Name, [string]$Command, [string[]]$Arguments)
    Write-Host "[$Name]"
    $lines = @(& $Command @Arguments 2>&1 | ForEach-Object { "$_" })
    $exitCode = $LASTEXITCODE
    $lines | ForEach-Object { Write-Host $_ }
    if ($exitCode -ne 0) {
        throw "$Name failed with exit code $exitCode."
    }
    if (@($lines | Where-Object { $_ -match '^No test matches the given testcase filter' }).Count -ne 0) {
        throw "$Name matched no tests."
    }
    $summary = @($lines | Where-Object {
        $_ -match 'Passed!|valid records|Catalog validation succeeded|application-adoption-.+-test/v1'
    } | Select-Object -Last 1)
    $results.Add([ordered]@{
        name = $Name
        status = 'passed'
        summary = if ($summary.Count -eq 0) { $null } else { $summary[0].Trim() }
    })
}

$mechanicFiles = @(Get-ChildItem -LiteralPath (Join-Path $repo 'catalog/applications/dnd2024') `
    -Recurse -File -Filter '*.js' | Sort-Object FullName)
if ($mechanicFiles.Count -eq 0) {
    throw 'No active D&D JavaScript mechanics were found.'
}
foreach ($file in $mechanicFiles) {
    Invoke-Checked "javascript:$($file.Name)" $NodeCommand @(
        '-e',
        "const fs=require('fs'); new Function('ctx',fs.readFileSync(process.argv[1],'utf8'));",
        $file.FullName
    )
}

$scriptChecks = @(
    @{ name = 'adoption-contracts'; path = 'tools/Test-AdoptionContracts.ps1'; arguments = @('-NpxCommand', $NpxCommand) },
    @{ name = 'conformance'; path = 'conformance/tools/Test-ConformanceTooling.ps1'; arguments = @('-Stage', '4C', '-NpxCommand', $NpxCommand) },
    @{ name = 'content-transformation'; path = 'transformation/tools/Test-ContentTransformation.ps1'; arguments = @('-NpxCommand', $NpxCommand) },
    @{ name = 'projection-mapping'; path = 'mapping/tools/Test-ProjectionDependencyMapping.ps1'; arguments = @('-NpxCommand', $NpxCommand) },
    @{ name = 'effect-allowlist'; path = 'effects/tools/Test-ResultEffectAllowlist.ps1'; arguments = @('-NpxCommand', $NpxCommand) },
    @{ name = 'impact-replay-rollback'; path = 'impact-proof/tools/Test-ImpactReplayRollbackProof.ps1'; arguments = @('-NpxCommand', $NpxCommand, '-DotnetCommand', $DotnetCommand) }
)
foreach ($check in $scriptChecks) {
    $path = Join-Path $adoption $check.path
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Acceptance dependency is missing: $($check.path)"
    }
    Invoke-Checked $check.name $PwshCommand (@('-NoProfile', '-File', $path) + $check.arguments)
}

Invoke-Checked 'release-build' $DotnetCommand @(
    'build', (Join-Path $repo 'DantesRoleplay.slnx'), '--configuration', 'Release', '--nologo'
)
$catalogTool = Join-Path $repo 'DantesRoleplay.Tools/bin/Release/net10.0/roleplay.dll'
if (-not (Test-Path -LiteralPath $catalogTool -PathType Leaf)) {
    throw "Release catalog tool was not produced: $catalogTool"
}
Invoke-Checked 'catalog-validation' $DotnetCommand @($catalogTool, 'validate', 'catalog')
Invoke-Checked 'shared-suite' $DotnetCommand @(
    'test', (Join-Path $repo 'DantesRoleplay.Tests/DantesRoleplay.Tests.csproj'),
    '--configuration', 'Release', '--no-build', '--no-restore', '--nologo',
    '--logger', 'console;verbosity=minimal'
)
Invoke-Checked 'local-ai-suite' $DotnetCommand @(
    'test', (Join-Path $repo 'src/system/local-ai/DantesRoleplay.LocalAI.Tests/DantesRoleplay.LocalAI.Tests.csproj'),
    '--configuration', 'Release', '--no-build', '--no-restore', '--nologo',
    '--logger', 'console;verbosity=minimal'
)
Invoke-Checked 'protocol-walk' $DotnetCommand @(
    'test', (Join-Path $repo 'DantesRoleplay.Tests/DantesRoleplay.Tests.csproj'),
    '--configuration', 'Release', '--no-restore', '--nologo',
    '-p:IncludeProtocolWalkTests=true',
    '--filter', 'FullyQualifiedName~DantesRoleplay.Tests.ProtocolWalkTests',
    '--logger', 'console;verbosity=minimal'
)

$report = [ordered]@{
    format = 'dnd2024-adoption-slice12-acceptance/v1'
    status = 'passed'
    javascriptFiles = $mechanicFiles.Count
    publicMcpVerbs = @('orient', 'query', 'commit')
    automaticActivation = $false
    writesLiveData = $false
    checks = @($results)
}
$json = ($report | ConvertTo-Json -Depth 10) + "`n"
if ($ReportPath) {
    $fullReportPath = [IO.Path]::GetFullPath($ReportPath, $repo)
    $parent = Split-Path -Parent $fullReportPath
    if (-not (Test-Path -LiteralPath $parent -PathType Container)) {
        [IO.Directory]::CreateDirectory($parent) | Out-Null
    }
    [IO.File]::WriteAllText($fullReportPath, $json, [Text.UTF8Encoding]::new($false))
}
Write-Output $json
