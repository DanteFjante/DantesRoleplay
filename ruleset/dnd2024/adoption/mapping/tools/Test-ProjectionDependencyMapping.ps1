[CmdletBinding()]
param([string]$NpxCommand = 'npx')

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..\..')).Path
$root = Join-Path $repo 'ruleset/dnd2024/adoption/mapping'
$schema = Join-Path $root 'contracts/projection-dependency-mapping.schema.json'
$fixture = Join-Path $root 'fixtures/projection-dependency-mapping.valid.json'
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('dantesroleplay-projection-mapping-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($temporary) | Out-Null

function Invoke-Ajv { param([string[]]$Arguments) $output = @(& $NpxCommand --yes ajv-cli@5.0.0 @Arguments 2>&1 | ForEach-Object { "$_" }); [pscustomobject]@{ exitCode = $LASTEXITCODE; output = $output } }
function Require-AjvSuccess { param([string[]]$Arguments, [string]$Message) $result = Invoke-Ajv $Arguments; if ($result.exitCode -ne 0) { throw "${Message}: $($result.output -join [Environment]::NewLine)" } }
function Write-Json { param($Value, [string]$Path) [IO.File]::WriteAllText($Path, (($Value | ConvertTo-Json -Depth 100) + "`n"), [Text.UTF8Encoding]::new($false)) }
function Assert-Unique { param([object[]]$Values, [string]$Name) if (@($Values | Group-Object | Where-Object Count -gt 1).Count -gt 0) { throw "Duplicate $Name declaration." } }
function Projection-Key { param($Reference) return "$($Reference.qualifiedId)@$($Reference.version)" }
function Expected-Root { param($MappingInput)
    if ($MappingInput.kind -eq 'component') { return "component:$($MappingInput.component.qualifiedTypeId)@$($MappingInput.component.typeVersion)#$($MappingInput.sourcePointer)" }
    return "projection:$($MappingInput.projection.qualifiedId)@$($MappingInput.projection.version)"
}
function Assert-SemanticClosure { param($Manifest)
    Assert-Unique @($Manifest.inputs | ForEach-Object id) 'input id'
    Assert-Unique @($Manifest.inputs | ForEach-Object targetPointer) 'target pointer'
    Assert-Unique @($Manifest.impactEvidence | ForEach-Object inputId) 'impact input'
    if (@($Manifest.impactEvidence).Count -ne @($Manifest.inputs).Count) { throw 'Every declared input requires exactly one impact-evidence declaration.' }
    $roles = @{}; foreach ($role in @($Manifest.roles)) { $roles[$role] = $true }
    $inputs = @{}; foreach ($mappingInput in @($Manifest.inputs)) {
        $inputs[$mappingInput.id] = $mappingInput
        if ($mappingInput.kind -eq 'component' -and -not $roles.ContainsKey($mappingInput.entityRole)) { throw "Component input '$($mappingInput.id)' has an undeclared entity role." }
        if ($mappingInput.kind -eq 'projection') {
            if ((Projection-Key $mappingInput.projection) -eq (Projection-Key $Manifest.projection)) { throw "Projection input '$($mappingInput.id)' may not depend on the candidate itself." }
            foreach ($binding in $mappingInput.roleBindings.PSObject.Properties) {
                if (-not $roles.ContainsKey($binding.Name) -or -not $roles.ContainsKey([string]$binding.Value)) { throw "Projection input '$($mappingInput.id)' has an undeclared role binding." }
            }
        }
    }
    foreach ($evidence in @($Manifest.impactEvidence)) {
        if (-not $inputs.ContainsKey($evidence.inputId)) { throw "Impact evidence references an unknown input: $($evidence.inputId)" }
        if ($evidence.canonicalRoot -cne (Expected-Root $inputs[$evidence.inputId])) { throw "Impact root does not exactly identify input '$($evidence.inputId)'." }
        if ((Projection-Key $evidence.dependentProjection) -cne (Projection-Key $Manifest.projection) -or $evidence.dependentProjection.contentHash -cne $Manifest.projection.contentHash) { throw "Impact evidence must name the candidate projection exactly." }
    }
}
function New-Report { param($Manifest, [string]$ManifestPath)
    [ordered]@{ format = 'application-adoption-projection-dependency-mapping-report/v1'; manifestSha256 = (Get-FileHash -LiteralPath $ManifestPath -Algorithm SHA256).Hash.ToUpperInvariant(); candidateKey = $Manifest.candidateKey; projection = [ordered]@{ qualifiedId = $Manifest.projection.qualifiedId; version = $Manifest.projection.version; contentHash = $Manifest.projection.contentHash }; inputs = @($Manifest.inputs | ForEach-Object { [ordered]@{ id = $_.id; kind = $_.kind; targetPointer = $_.targetPointer } }); impactRoots = @($Manifest.impactEvidence | ForEach-Object canonicalRoot) }
}
function Assert-Rejected { param([scriptblock]$Action, [string]$Name) try { & $Action; throw "Expected rejection did not occur: $Name" } catch { if ($_.Exception.Message -like "Expected rejection did not occur*") { throw }; return } }

try {
    Require-AjvSuccess @('compile', '--spec=draft2020', '--strict=false', '-s', $schema) 'Schema compilation failed'
    Require-AjvSuccess @('validate', '--spec=draft2020', '--strict=false', '-s', $schema, '-d', $fixture) 'Valid fixture schema validation failed'
    $valid = Get-Content -Raw $fixture | ConvertFrom-Json -Depth 100
    Assert-SemanticClosure $valid
    $reportPath1 = Join-Path $temporary 'report-1.json'; $reportPath2 = Join-Path $temporary 'report-2.json'
    Write-Json (New-Report $valid $fixture) $reportPath1; Write-Json (New-Report $valid $fixture) $reportPath2
    if ((Get-FileHash $reportPath1 -Algorithm SHA256).Hash -cne (Get-FileHash $reportPath2 -Algorithm SHA256).Hash) { throw 'Mapping report is not deterministic.' }
    $schemaNegative = Get-Content -Raw $fixture | ConvertFrom-Json -Depth 100; $schemaNegative | Add-Member -NotePropertyName unexpected -NotePropertyValue $true
    $schemaNegativePath = Join-Path $temporary 'schema-negative.json'; Write-Json $schemaNegative $schemaNegativePath
    if ((Invoke-Ajv @('validate', '--spec=draft2020', '--strict=false', '-s', $schema, '-d', $schemaNegativePath)).exitCode -eq 0) { throw 'Unknown object property was accepted.' }
    $malformedHash = Get-Content -Raw $fixture | ConvertFrom-Json -Depth 100; $malformedHash.projection.contentHash = 'not-a-sha256'
    $malformedHashPath = Join-Path $temporary 'malformed-hash.json'; Write-Json $malformedHash $malformedHashPath
    if ((Invoke-Ajv @('validate', '--spec=draft2020', '--strict=false', '-s', $schema, '-d', $malformedHashPath)).exitCode -eq 0) { throw 'Malformed projection hash was accepted.' }
    $duplicateInput = Get-Content -Raw $fixture | ConvertFrom-Json -Depth 100; $duplicateInput.inputs[1].id = $duplicateInput.inputs[0].id; Assert-Rejected { Assert-SemanticClosure $duplicateInput } 'duplicate input ID'
    $duplicateTarget = Get-Content -Raw $fixture | ConvertFrom-Json -Depth 100; $duplicateTarget.inputs[1].targetPointer = $duplicateTarget.inputs[0].targetPointer; Assert-Rejected { Assert-SemanticClosure $duplicateTarget } 'duplicate target pointer'
    $unknownRole = Get-Content -Raw $fixture | ConvertFrom-Json -Depth 100; $unknownRole.inputs[0].entityRole = 'missing'; Assert-Rejected { Assert-SemanticClosure $unknownRole } 'unknown component role'
    $unknownInput = Get-Content -Raw $fixture | ConvertFrom-Json -Depth 100; $unknownInput.impactEvidence[0].inputId = 'missing'; Assert-Rejected { Assert-SemanticClosure $unknownInput } 'unknown impact input'
    $missingImpact = Get-Content -Raw $fixture | ConvertFrom-Json -Depth 100; $missingImpact.impactEvidence = @($missingImpact.impactEvidence[0]); Assert-Rejected { Assert-SemanticClosure $missingImpact } 'missing impact evidence'
    $badRoot = Get-Content -Raw $fixture | ConvertFrom-Json -Depth 100; $badRoot.impactEvidence[0].canonicalRoot = 'component:mapping-fixture.ability-state@1#/str'; Assert-Rejected { Assert-SemanticClosure $badRoot } 'mismatched impact root'
    $selfDependency = Get-Content -Raw $fixture | ConvertFrom-Json -Depth 100; $selfDependency.inputs[1].projection.qualifiedId = $selfDependency.projection.qualifiedId; $selfDependency.inputs[1].projection.version = $selfDependency.projection.version; Assert-Rejected { Assert-SemanticClosure $selfDependency } 'self dependency'
    [ordered]@{ format = 'application-adoption-projection-dependency-mapping-test/v1'; schemaCompilations = 1; positiveDocuments = 1; schemaNegativeCases = 2; semanticNegativeCases = 7; deterministicReports = 1; writes = 'none' } | ConvertTo-Json
}
finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Recurse -Force } }
