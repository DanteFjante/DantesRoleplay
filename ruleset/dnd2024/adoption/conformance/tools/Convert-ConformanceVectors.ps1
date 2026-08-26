[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$InputPath,
    [Parameter(Mandatory = $true)][string]$ScenarioOutputPath,
    [Parameter(Mandatory = $true)][string]$ObservationOutputPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Get-JsonPointerValue {
    param([Parameter(Mandatory = $true)]$Root, [Parameter(Mandatory = $true)][string]$Pointer)
    if ($Pointer -notmatch '^(?:/(?:[^~/]|~[01])*)+$') { throw "Invalid JSON Pointer: $Pointer" }
    $value = $Root
    foreach ($segment in $Pointer.TrimStart('/').Split('/')) {
        $name = $segment.Replace('~1', '/').Replace('~0', '~')
        if ($value -is [System.Array]) {
            if ($name -notmatch '^0$|^[1-9][0-9]*$' -or [int]$name -ge $value.Count) { throw "Result pointer does not resolve: $Pointer" }
            $value = $value[[int]$name]
        } else {
            $property = $value.PSObject.Properties[$name]
            if ($null -eq $property) { throw "Result pointer does not resolve: $Pointer" }
            $value = $property.Value
        }
    }
    return $value
}

function Write-JsonAtomically {
    param([Parameter(Mandatory = $true)]$Value, [Parameter(Mandatory = $true)][string]$Path)
    $full = [IO.Path]::GetFullPath($Path); $directory = Split-Path -Parent $full
    [IO.Directory]::CreateDirectory($directory) | Out-Null
    $temporary = Join-Path $directory ('.' + [IO.Path]::GetRandomFileName())
    try { [IO.File]::WriteAllText($temporary, ($Value | ConvertTo-Json -Depth 100) + "`n", [Text.UTF8Encoding]::new($false)); [IO.File]::Move($temporary, $full, $true) }
    finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force } }
}

$raw = Get-Content -Raw -LiteralPath $InputPath | ConvertFrom-Json -Depth 100
if ($raw.format -ne 'application-adoption-source-vectors/v1') { throw 'Unsupported source-vector format.' }
if ($null -eq $raw.suite -or [string]::IsNullOrWhiteSpace($raw.suite.id) -or $null -eq $raw.suite.source) { throw 'Source-vector suite identity and provenance are required.' }
if (@($raw.compare).Count -eq 0 -or @($raw.cases).Count -eq 0) { throw 'A source vector requires mappings and cases.' }
$names = @($raw.compare | ForEach-Object name); if (@($names | Sort-Object -Unique).Count -ne $names.Count) { throw 'Comparison field names must be unique.' }
$ids = @($raw.cases | ForEach-Object id); if (@($ids | Sort-Object -Unique).Count -ne $ids.Count) { throw 'Case IDs must be unique.' }

$scenarioCases = @(); $observationCases = @()
foreach ($case in $raw.cases) {
    if ([string]::IsNullOrWhiteSpace($case.id) -or $null -eq $case.context -or $null -eq $case.input -or $null -eq $case.PSObject.Properties['result']) { throw 'Every source-vector case requires id, context, input, seed, and result.' }
    $compare = @($raw.compare | ForEach-Object { [ordered]@{ name = $_.name; pointer = $_.pointer } })
    $scenarioCases += [ordered]@{ id = $case.id; context = $case.context; input = $case.input; seed = $case.seed; compare = $compare }
    $values = [ordered]@{}
    foreach ($field in $raw.compare) { $values[$field.name] = Get-JsonPointerValue -Root $case.result -Pointer $field.pointer }
    $observationCases += [ordered]@{ id = $case.id; values = $values }
}
$scenario = [ordered]@{ format = 'application-adoption-conformance-scenarios/v1'; suite = [ordered]@{ id = $raw.suite.id; subject = $raw.suite.subject; source = $raw.suite.source }; cases = $scenarioCases }
$observation = [ordered]@{ format = 'application-adoption-conformance-observations/v1'; suite = [ordered]@{ id = $raw.suite.id }; source = $raw.suite.source; cases = $observationCases }
Write-JsonAtomically -Value $scenario -Path $ScenarioOutputPath
Write-JsonAtomically -Value $observation -Path $ObservationOutputPath
[ordered]@{ format = 'application-adoption-conversion-report/v1'; suite = $raw.suite.id; source = $raw.suite.source; cases = $scenarioCases.Count; scenario = [IO.Path]::GetFullPath($ScenarioOutputPath); observation = [IO.Path]::GetFullPath($ObservationOutputPath) } | ConvertTo-Json -Depth 20
