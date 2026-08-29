[CmdletBinding()]
param([string]$NpxCommand = 'npx')

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..\..')).Path
$adoptionRoot = Join-Path $repo 'ruleset/dnd2024/adoption'
$root = Join-Path $repo 'ruleset/dnd2024/adoption/effects'
$schema = Join-Path $root 'contracts/result-effect-allowlist.schema.json'
$fixture = Join-Path $root 'fixtures/result-effect-allowlist.valid.json'
$resultFixture = Join-Path $root 'fixtures/result-effect-allowlist.result.json'
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('dantesroleplay-result-effect-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($temporary) | Out-Null

function Invoke-Ajv { param([string[]]$Arguments) $output = @(& $NpxCommand --yes ajv-cli@5.0.0 @Arguments 2>&1 | ForEach-Object { "$_" }); [pscustomobject]@{ exitCode = $LASTEXITCODE; output = $output } }
function Require-AjvSuccess { param([string[]]$Arguments, [string]$Message) $result = Invoke-Ajv $Arguments; if ($result.exitCode -ne 0) { throw "${Message}: $($result.output -join [Environment]::NewLine)" } }
function Write-Json { param($Value, [string]$Path) [IO.File]::WriteAllText($Path, (($Value | ConvertTo-Json -Depth 100) + "`n"), [Text.UTF8Encoding]::new($false)) }
function Assert-Unique { param([object[]]$Values, [string]$Name) if (@($Values | Group-Object | Where-Object Count -gt 1).Count -gt 0) { throw "Duplicate $Name declaration." } }
function Assert-Rejected { param([scriptblock]$Action, [string]$Name) try { & $Action; throw "Expected rejection did not occur: $Name" } catch { if ($_.Exception.Message -like 'Expected rejection did not occur*') { throw }; return } }
function Get-JsonPointer { param($Value, [string]$Pointer)
    if ($Pointer -eq '') { return $Value }
    $cursor = $Value
    foreach ($segment in $Pointer.TrimStart('/').Split('/')) {
        $name = $segment.Replace('~1', '/').Replace('~0', '~')
        if ($cursor -is [System.Array]) { if ($name -notmatch '^0$|^[1-9][0-9]*$' -or [int]$name -ge $cursor.Count) { throw "JSON Pointer does not resolve: $Pointer" }; $cursor = $cursor[[int]$name] }
        else { $property = $cursor.PSObject.Properties[$name]; if ($null -eq $property) { throw "JSON Pointer does not resolve: $Pointer" }; $cursor = $property.Value }
    }
    return $cursor
}
function Resolve-ContainedPath { param([string]$Root, [string]$RelativePath)
    $rootFull = [IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $full = [IO.Path]::GetFullPath((Join-Path $rootFull $RelativePath))
    if ($full -ne $rootFull -and -not $full.StartsWith($rootFull + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw "Path escapes the adoption root: $RelativePath" }
    return $full
}
function Assert-TemplateFields { param($Template, [string[]]$Required, [string[]]$Forbidden)
    foreach ($field in $Required) { if ($null -eq $Template.PSObject.Properties[$field] -or [string]::IsNullOrWhiteSpace([string]$Template.$field)) { throw "Template '$($Template.proposalKind)' requires '$field'." } }
    foreach ($field in $Forbidden) { if ($null -ne $Template.PSObject.Properties[$field] -and -not [string]::IsNullOrEmpty([string]$Template.$field)) { throw "Template '$($Template.proposalKind)' cannot carry '$field'." } }
}
function Assert-SemanticClosure { param($Manifest)
    if ($Manifest.projectionMapping.candidateKey -cne $Manifest.candidateKey) { throw 'Projection mapping must identify the same candidate key.' }
    Assert-Unique @($Manifest.allowlist | ForEach-Object proposalKind) 'proposal kind'
    $roles = @($Manifest.roles)
    foreach ($template in @($Manifest.allowlist)) {
        foreach ($roleField in @('entityRole', 'targetRole')) { if ($null -ne $template.PSObject.Properties[$roleField] -and -not ($roles -ccontains [string]$template.$roleField)) { throw "Template '$($template.proposalKind)' uses an undeclared $roleField." } }
        switch ($template.effectType) {
            { $_ -in @('component.add', 'component.set', 'component.merge') } { Assert-TemplateFields $template @('entityRole', 'component', 'dataPointer') @('targetRole', 'relationshipKind', 'entityIdPointer', 'namePointer', 'slotPointer'); break }
            'component.remove' { Assert-TemplateFields $template @('entityRole', 'component') @('targetRole', 'relationshipKind', 'dataPointer', 'entityIdPointer', 'namePointer', 'slotPointer'); break }
            'entity.create' { Assert-TemplateFields $template @('entityIdPointer', 'namePointer') @('entityRole', 'targetRole', 'component', 'relationshipKind', 'dataPointer', 'slotPointer'); break }
            'entity.delete' { Assert-TemplateFields $template @('entityRole') @('targetRole', 'component', 'relationshipKind', 'dataPointer', 'entityIdPointer', 'namePointer', 'slotPointer'); break }
            'containment.move' { Assert-TemplateFields $template @('entityRole', 'targetRole') @('component', 'relationshipKind', 'dataPointer', 'entityIdPointer', 'namePointer'); break }
            'relationship.create' { Assert-TemplateFields $template @('entityRole', 'targetRole', 'relationshipKind', 'dataPointer') @('component', 'entityIdPointer', 'namePointer', 'slotPointer'); break }
            'relationship.remove' { Assert-TemplateFields $template @('entityRole', 'targetRole', 'relationshipKind') @('component', 'dataPointer', 'entityIdPointer', 'namePointer', 'slotPointer'); break }
            default { throw "Unsupported generic effect type: $($template.effectType)" }
        }
    }
}
function Assert-ProjectionMappingReference { param($Manifest)
    $mappingPath = Resolve-ContainedPath $adoptionRoot $Manifest.projectionMapping.manifestPath
    if (-not (Test-Path -LiteralPath $mappingPath -PathType Leaf)) { throw 'Declared projection mapping manifest is missing.' }
    if ((Get-FileHash -LiteralPath $mappingPath -Algorithm SHA256).Hash.ToUpperInvariant() -cne $Manifest.projectionMapping.sha256) { throw 'Declared projection mapping hash does not match.' }
    $mapping = Get-Content -Raw -LiteralPath $mappingPath | ConvertFrom-Json -Depth 100
    if ($mapping.candidateKey -cne $Manifest.candidateKey) { throw 'Declared projection mapping does not identify the same candidate key.' }
}
function Convert-CandidateResult { param($Manifest, $Result)
    $proposals = @(Get-JsonPointer $Result $Manifest.result.proposalsPointer)
    foreach ($proposal in $proposals) { if ($proposal -isnot [psobject] -or [string]::IsNullOrWhiteSpace([string]$proposal.kind)) { throw 'Each candidate proposal requires a kind.' } }
    $plans = @(); foreach ($proposal in $proposals) {
        $matches = @($Manifest.allowlist | Where-Object { $_.proposalKind -ceq $proposal.kind })
        if ($matches.Count -ne 1) { throw "Candidate proposal kind is not allowlisted: $($proposal.kind)" }
        $template = $matches[0]
        $optional = @{}
        foreach ($field in @('entityRole', 'targetRole', 'component', 'relationshipKind')) { $optional[$field] = if ($null -ne $template.PSObject.Properties[$field]) { $template.$field } else { $null } }
        $plan = [ordered]@{ proposalKind = $template.proposalKind; effectType = $template.effectType; entityRole = $optional['entityRole']; targetRole = $optional['targetRole']; component = $optional['component']; relationshipKind = $optional['relationshipKind'] }
        foreach ($pair in @(@{ source = 'dataPointer'; target = 'data' }, @{ source = 'entityIdPointer'; target = 'entityId' }, @{ source = 'namePointer'; target = 'name' }, @{ source = 'slotPointer'; target = 'slot' })) { if ($null -ne $template.PSObject.Properties[$pair.source]) { $plan[$pair.target] = Get-JsonPointer $proposal $template.$($pair.source) } }
        $plans += [pscustomobject]$plan
    }
    return @($plans)
}

try {
    Require-AjvSuccess @('compile', '--spec=draft2020', '--strict=false', '-s', $schema) 'Schema compilation failed'
    Require-AjvSuccess @('validate', '--spec=draft2020', '--strict=false', '-s', $schema, '-d', $fixture) 'Valid fixture schema validation failed'
    $manifest = Get-Content -Raw $fixture | ConvertFrom-Json -Depth 100; $result = Get-Content -Raw $resultFixture | ConvertFrom-Json -Depth 100
    $resultSchema = Resolve-ContainedPath $root $manifest.result.schemaPath
    if (-not (Test-Path -LiteralPath $resultSchema -PathType Leaf)) { throw 'Declared result schema is missing.' }
    if ((Get-FileHash -LiteralPath $resultSchema -Algorithm SHA256).Hash.ToUpperInvariant() -cne $manifest.result.schemaHash) { throw 'Declared result schema hash does not match.' }
    Require-AjvSuccess @('compile', '--spec=draft2020', '--strict=false', '-s', $resultSchema) 'Candidate result schema compilation failed'
    Require-AjvSuccess @('validate', '--spec=draft2020', '--strict=false', '-s', $resultSchema, '-d', $resultFixture) 'Candidate result schema validation failed'
    Assert-SemanticClosure $manifest
    Assert-ProjectionMappingReference $manifest
    $plans1 = Convert-CandidateResult $manifest $result; $plans2 = Convert-CandidateResult $manifest $result
    if (@($plans1).Count -ne 1 -or $plans1[0].effectType -ne 'component.set' -or $plans1[0].entityRole -ne 'subject' -or $plans1[0].component.qualifiedTypeId -ne 'mapping-fixture.subject-state' -or $plans1[0].data.value -ne 11) { throw 'Valid candidate result did not map to the declared component-set plan.' }
    $planPath1 = Join-Path $temporary 'plan-1.json'; $planPath2 = Join-Path $temporary 'plan-2.json'; Write-Json $plans1 $planPath1; Write-Json $plans2 $planPath2
    if ((Get-FileHash $planPath1 -Algorithm SHA256).Hash -cne (Get-FileHash $planPath2 -Algorithm SHA256).Hash) { throw 'Candidate effect plans are not deterministic.' }
    $unknownProperty = Get-Content -Raw $fixture | ConvertFrom-Json -Depth 100; $unknownProperty | Add-Member -NotePropertyName unexpected -NotePropertyValue $true; $unknownPropertyPath = Join-Path $temporary 'unknown-property.json'; Write-Json $unknownProperty $unknownPropertyPath
    if ((Invoke-Ajv @('validate', '--spec=draft2020', '--strict=false', '-s', $schema, '-d', $unknownPropertyPath)).exitCode -eq 0) { throw 'Unknown allowlist property was accepted.' }
    $malformedHash = Get-Content -Raw $fixture | ConvertFrom-Json -Depth 100; $malformedHash.projectionMapping.sha256 = 'not-a-sha256'; $malformedHashPath = Join-Path $temporary 'malformed-hash.json'; Write-Json $malformedHash $malformedHashPath
    if ((Invoke-Ajv @('validate', '--spec=draft2020', '--strict=false', '-s', $schema, '-d', $malformedHashPath)).exitCode -eq 0) { throw 'Malformed mapping hash was accepted.' }
    $unknownResultProperty = Get-Content -Raw $resultFixture | ConvertFrom-Json -Depth 100; $unknownResultProperty.proposals[0] | Add-Member -NotePropertyName unexpected -NotePropertyValue $true; $unknownResultPath = Join-Path $temporary 'unknown-result-property.json'; Write-Json $unknownResultProperty $unknownResultPath
    if ((Invoke-Ajv @('validate', '--spec=draft2020', '--strict=false', '-s', $resultSchema, '-d', $unknownResultPath)).exitCode -eq 0) { throw 'Unknown candidate-result property was accepted.' }
    $duplicateKind = Get-Content -Raw $fixture | ConvertFrom-Json -Depth 100; $duplicateKind.allowlist += $duplicateKind.allowlist[0]; Assert-Rejected { Assert-SemanticClosure $duplicateKind } 'duplicate proposal kind'
    $unknownRole = Get-Content -Raw $fixture | ConvertFrom-Json -Depth 100; $unknownRole.allowlist[0].entityRole = 'missing'; Assert-Rejected { Assert-SemanticClosure $unknownRole } 'unknown role'
    $wrongCandidate = Get-Content -Raw $fixture | ConvertFrom-Json -Depth 100; $wrongCandidate.projectionMapping.candidateKey = 'other-candidate'; Assert-Rejected { Assert-SemanticClosure $wrongCandidate } 'projection mapping candidate mismatch'
    $staleMapping = Get-Content -Raw $fixture | ConvertFrom-Json -Depth 100; $staleMapping.projectionMapping.sha256 = ('0' * 64); Assert-Rejected { Assert-ProjectionMappingReference $staleMapping } 'stale projection mapping hash'
    $unknownProposal = Get-Content -Raw $resultFixture | ConvertFrom-Json -Depth 100; $unknownProposal.proposals[0].kind = 'unapproved'; Assert-Rejected { Convert-CandidateResult $manifest $unknownProposal } 'unknown proposal kind'
    $caseChangedProposal = Get-Content -Raw $resultFixture | ConvertFrom-Json -Depth 100; $caseChangedProposal.proposals[0].kind = 'Set-Subject-State'; Assert-Rejected { Convert-CandidateResult $manifest $caseChangedProposal } 'case-changed proposal kind'
    $missingPayload = Get-Content -Raw $resultFixture | ConvertFrom-Json -Depth 100; $missingPayload.proposals[0].PSObject.Properties.Remove('state'); Assert-Rejected { Convert-CandidateResult $manifest $missingPayload } 'missing data payload'
    [ordered]@{ format = 'application-adoption-result-effect-allowlist-test/v1'; schemaCompilations = 2; positiveDocuments = 2; schemaNegativeCases = 3; semanticNegativeCases = 4; conversionNegativeCases = 3; deterministicPlans = 1; writes = 'none' } | ConvertTo-Json
}
finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Recurse -Force } }
