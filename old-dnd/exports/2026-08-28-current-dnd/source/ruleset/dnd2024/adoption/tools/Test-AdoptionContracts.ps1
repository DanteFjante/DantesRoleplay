[CmdletBinding()]
param(
    [string]$ContractsPath = (Join-Path $PSScriptRoot "..\contracts"),
    [string]$NpxCommand = "npx"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Set-JsonPointer {
    param(
        [Parameter(Mandatory = $true)]$Root,
        [Parameter(Mandatory = $true)][string]$Pointer,
        $Value
    )

    $segments = @(
        $Pointer.TrimStart("/").Split("/") |
            ForEach-Object { $_.Replace("~1", "/").Replace("~0", "~") }
    )
    if ($segments.Count -eq 0) {
        throw "A negative-case mutation cannot replace the document root."
    }

    $cursor = $Root
    for ($index = 0; $index -lt $segments.Count - 1; $index++) {
        $segment = $segments[$index]
        if ($cursor -is [System.Array]) {
            $cursor = $cursor[[int]$segment]
        }
        else {
            $property = $cursor.PSObject.Properties[$segment]
            if ($null -eq $property) {
                throw "JSON Pointer segment does not exist: $segment"
            }
            $cursor = $property.Value
        }
    }

    $last = $segments[-1]
    if ($cursor -is [System.Array]) {
        $cursor[[int]$last] = $Value
    }
    else {
        $property = $cursor.PSObject.Properties[$last]
        if ($null -eq $property) {
            throw "JSON Pointer target does not exist: $last"
        }
        $property.Value = $Value
    }
}

function Invoke-Ajv {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)
    $output = @(& $NpxCommand --yes ajv-cli@5.0.0 @Arguments 2>&1 | ForEach-Object { "$_" })
    [pscustomobject]@{
        exitCode = $LASTEXITCODE
        output = $output
    }
}

$contracts = (Resolve-Path -LiteralPath $ContractsPath).Path
$jsonFiles = @(Get-ChildItem -LiteralPath $contracts -Filter "*.json" -File)
if ($jsonFiles.Count -ne 6) {
    throw "Expected exactly six Slice 0B contract/schema/example JSON files; found $($jsonFiles.Count)."
}
foreach ($file in $jsonFiles) {
    Get-Content -Raw -LiteralPath $file.FullName | ConvertFrom-Json -Depth 100 | Out-Null
}

$pairs = @(
    [pscustomobject]@{
        schema = "provenance-ledger.schema.json"
        valid = "provenance-ledger.valid.example.json"
        invalid = "provenance-ledger.invalid-cases.json"
    },
    [pscustomobject]@{
        schema = "coverage-matrix.schema.json"
        valid = "coverage-matrix.valid.example.json"
        invalid = "coverage-matrix.invalid-cases.json"
    }
)

$positiveCount = 0
$negativeCount = 0
$details = @()
foreach ($pair in $pairs) {
    $schemaPath = Join-Path $contracts $pair.schema
    $compile = Invoke-Ajv -Arguments @("compile", "--spec=draft2020", "--strict=false", "-s", $schemaPath)
    if ($compile.exitCode -ne 0) {
        throw "Schema compilation failed for $($pair.schema): $($compile.output -join [Environment]::NewLine)"
    }

    $validPath = Join-Path $contracts $pair.valid
    $positive = Invoke-Ajv -Arguments @("validate", "--spec=draft2020", "--strict=false", "-s", $schemaPath, "-d", $validPath)
    if ($positive.exitCode -ne 0) {
        throw "Positive example failed for $($pair.schema): $($positive.output -join [Environment]::NewLine)"
    }
    $positiveCount++

    $manifest = Get-Content -Raw -LiteralPath (Join-Path $contracts $pair.invalid) | ConvertFrom-Json -Depth 100
    if ($manifest.schema -ne $pair.schema -or $manifest.base -ne $pair.valid) {
        throw "Negative-case manifest does not match its schema/example pair: $($pair.invalid)"
    }
    foreach ($case in $manifest.cases) {
        $document = Get-Content -Raw -LiteralPath $validPath | ConvertFrom-Json -Depth 100
        foreach ($mutation in $case.mutations) {
            Set-JsonPointer -Root $document -Pointer $mutation.pointer -Value $mutation.value
        }

        $temporaryFile = [System.IO.Path]::GetTempFileName()
        try {
            [System.IO.File]::WriteAllText(
                $temporaryFile,
                ($document | ConvertTo-Json -Depth 100),
                [System.Text.UTF8Encoding]::new($false))
            $negative = Invoke-Ajv -Arguments @("validate", "--spec=draft2020", "--strict=false", "-s", $schemaPath, "-d", $temporaryFile)
            if ($negative.exitCode -eq 0) {
                throw "Negative case unexpectedly validated: $($case.name)"
            }
            $negativeCount++
            $details += [pscustomobject]@{
                schema = $pair.schema
                case = "$($case.name)"
                rejected = $true
            }
        }
        finally {
            if (Test-Path -LiteralPath $temporaryFile -PathType Leaf) {
                Remove-Item -LiteralPath $temporaryFile -Force
            }
        }
    }
}

[pscustomobject]@{
    format = "dnd-code-adoption-contract-validation/v1"
    parsedJsonFiles = $jsonFiles.Count
    compiledSchemas = $pairs.Count
    positiveExamplesAccepted = $positiveCount
    negativeExamplesRejected = $negativeCount
    details = $details
} | ConvertTo-Json -Depth 10
