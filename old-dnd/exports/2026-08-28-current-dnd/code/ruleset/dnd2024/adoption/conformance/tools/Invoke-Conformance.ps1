[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ScenarioPath,
    [Parameter(Mandatory = $true)][string]$ReferenceObservationPath,
    [Parameter(Mandatory = $true)][string]$CandidateObservationPath,
    [string]$IntentionalDifferencePath,
    [Parameter(Mandatory = $true)][string]$ReportPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Test-DeepEqual { param($Left, $Right) return (($Left | ConvertTo-Json -Depth 100 -Compress) -ceq ($Right | ConvertTo-Json -Depth 100 -Compress)) }
function Read-Json { param([string]$Path) return (Get-Content -Raw -LiteralPath $Path | ConvertFrom-Json -Depth 100) }
function Write-Report { param($Value, [string]$Path) $full=[IO.Path]::GetFullPath($Path); [IO.Directory]::CreateDirectory((Split-Path -Parent $full))|Out-Null; [IO.File]::WriteAllText($full,($Value|ConvertTo-Json -Depth 100)+"`n",[Text.UTF8Encoding]::new($false)) }
function Index-ById { param($Items,[string]$Kind) $index=@{}; foreach($item in @($Items)){ if([string]::IsNullOrWhiteSpace($item.id) -or $index.ContainsKey($item.id)){throw "Duplicate or empty $Kind id."};$index[$item.id]=$item }; return $index }

$scenario=Read-Json $ScenarioPath;$reference=Read-Json $ReferenceObservationPath;$candidate=Read-Json $CandidateObservationPath
if($scenario.format -ne 'application-adoption-conformance-scenarios/v1' -or $reference.format -ne 'application-adoption-conformance-observations/v1' -or $candidate.format -ne 'application-adoption-conformance-observations/v1'){throw 'Unsupported conformance document format.'}
$suiteId=$scenario.suite.id;if([string]::IsNullOrWhiteSpace($suiteId) -or $reference.suite.id -ne $suiteId -or $candidate.suite.id -ne $suiteId){throw 'Scenario and observations must have the same suite id.'}
$referenceById=Index-ById $reference.cases 'reference case';$candidateById=Index-ById $candidate.cases 'candidate case';$scenarioById=Index-ById $scenario.cases 'scenario case'
if($referenceById.Count -ne $scenarioById.Count -or $candidateById.Count -ne $scenarioById.Count){throw 'Observations must contain exactly the scenario cases.'}
$declared=@{};if($IntentionalDifferencePath){$manifest=Read-Json $IntentionalDifferencePath;if($manifest.format -ne 'application-adoption-intentional-differences/v1' -or $manifest.suite.id -ne $suiteId){throw 'Intentional-difference manifest does not match the suite.'};foreach($difference in @($manifest.differences)){if(-not $scenarioById.ContainsKey($difference.caseId)){throw "Intentional difference names an unknown case: $($difference.caseId)"};if(@($scenarioById[$difference.caseId].compare|ForEach-Object name) -notcontains $difference.field){throw "Intentional difference names an undeclared field: $($difference.caseId)/$($difference.field)"};if([string]::IsNullOrWhiteSpace($difference.reason) -or [string]::IsNullOrWhiteSpace($difference.evidence)){throw 'Intentional difference requires reason and evidence.'};$key="$($difference.caseId)|$($difference.field)";if($declared.ContainsKey($key)){throw 'Duplicate intentional difference declaration.'};$declared[$key]=$difference}}
$rows=@();$hasBlocking=$false;$mismatches=@{}
foreach($case in $scenario.cases){if(-not $referenceById.ContainsKey($case.id) -or -not $candidateById.ContainsKey($case.id)){throw "Observation case missing: $($case.id)"};$left=$referenceById[$case.id];$right=$candidateById[$case.id];$declaredFields=@($case.compare|ForEach-Object name);foreach($field in $declaredFields){if($null -eq $left.values.PSObject.Properties[$field] -or $null -eq $right.values.PSObject.Properties[$field]){throw "Observation field missing: $($case.id)/$field"}};if((@($left.values.PSObject.Properties.Name|Sort-Object) -join '|') -ne (@($declaredFields|Sort-Object) -join '|') -or (@($right.values.PSObject.Properties.Name|Sort-Object) -join '|') -ne (@($declaredFields|Sort-Object) -join '|')){throw "Observation fields do not exactly match scenario: $($case.id)"};foreach($field in $declaredFields){$same=Test-DeepEqual $left.values.$field $right.values.$field;$key="$($case.id)|$field";$row=[ordered]@{caseId=$case.id;field=$field;status=if($same){'passed'}elseif($declared.ContainsKey($key)){'requires-confirmation'}else{'blocked'};reference=$left.values.$field;candidate=$right.values.$field};if(-not $same){$mismatches[$key]=$true;$hasBlocking=$true;if($declared.ContainsKey($key)){$row.reason=$declared[$key].reason;$row.evidence=$declared[$key].evidence}};$rows+=$row}}
foreach($key in $declared.Keys){if(-not $mismatches.ContainsKey($key)){throw "Intentional difference does not describe an observed mismatch: $key"}}
$report=[ordered]@{format='application-adoption-conformance-report/v1';suite=[ordered]@{id=$suiteId};reference=$reference.source;candidate=$candidate.source;status=if($hasBlocking){'blocked'}else{'passed'};comparisons=$rows};Write-Report $report $ReportPath;Write-Output ($report|ConvertTo-Json -Depth 100);if($hasBlocking){exit 2}
