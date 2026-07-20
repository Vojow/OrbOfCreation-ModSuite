param(
    [Parameter(Mandatory = $true)]
    [string]$ReportPath,

    [string]$BaselinePath,

    [ValidateRange(0, 100)]
    [double]$MaxRegressionPercent = 10.0
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($BaselinePath)) {
    $BaselinePath = Join-Path $PSScriptRoot '..\data\autobuy-performance-baseline.json'
}

function Read-Report {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$Label
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Label report does not exist: $Path"
    }

    $resolved = (Resolve-Path -LiteralPath $Path).Path
    try {
        return Get-Content -LiteralPath $resolved -Raw | ConvertFrom-Json
    }
    catch {
        throw "$Label report is not valid JSON: $resolved`n$($_.Exception.Message)"
    }
}

function Get-RequiredProperty {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Object,

        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string]$Context
    )

    if ($Object.PSObject.Properties.Name -notcontains $Name) {
        throw "$Context is missing required property '$Name'."
    }

    return $Object.$Name
}

$report = Read-Report -Path $ReportPath -Label 'Current'
$baseline = Read-Report -Path $BaselinePath -Label 'Baseline'

if ($report.schemaVersion -ne 1 -or $baseline.schemaVersion -ne 1) {
    throw 'Performance report schemaVersion must be 1.'
}

if ($report.suite -ne $baseline.suite) {
    throw "Suite mismatch: current '$($report.suite)', baseline '$($baseline.suite)'."
}

$workloadProperties = @(
    'gameStage',
    'candidateCount',
    'structureCount',
    'upgradeCount',
    'targetStructureLevels',
    'queueCapacity',
    'reservedQueueSlots',
    'frameCount',
    'completionStartFrame',
    'completionEveryFrames',
    'theoreticalMinimumSubmissionFrames'
)

$metricRules = @(
    [pscustomobject]@{ Name = 'totalSubmitted'; Direction = 'higher' },
    [pscustomobject]@{ Name = 'queueHighWater'; Direction = 'higher' },
    [pscustomobject]@{ Name = 'finalQueueDepth'; Direction = 'higher' },
    [pscustomobject]@{ Name = 'minimumQueueAfterSaturation'; Direction = 'higher' },
    [pscustomobject]@{ Name = 'framesToNinetyPercentQueue'; Direction = 'lower' },
    [pscustomobject]@{ Name = 'framesToAllSubmissions'; Direction = 'lower' },
    [pscustomobject]@{ Name = 'framesToAllCompletions'; Direction = 'lower' },
    [pscustomobject]@{ Name = 'submissionOverheadFrames'; Direction = 'lower' },
    [pscustomobject]@{ Name = 'submissionEfficiencyPercent'; Direction = 'higher' },
    [pscustomobject]@{ Name = 'idleFramesWithPurchasableWork'; Direction = 'lower' },
    [pscustomobject]@{ Name = 'evaluationOnlyFramesWithPurchasableWork'; Direction = 'lower' },
    [pscustomobject]@{ Name = 'deferredFramesWithPurchasableWork'; Direction = 'lower' },
    [pscustomobject]@{ Name = 'maximumEvaluationsInFrame'; Direction = 'lower' },
    [pscustomobject]@{ Name = 'distinctCandidatesSubmitted'; Direction = 'higher' },
    [pscustomobject]@{ Name = 'structuresSubmittedMultipleTimes'; Direction = 'higher' },
    [pscustomobject]@{ Name = 'structuresMeetingTarget'; Direction = 'higher' },
    [pscustomobject]@{ Name = 'minimumStructureSubmissions'; Direction = 'higher' },
    [pscustomobject]@{ Name = 'maximumStructureSubmissions'; Direction = 'lower' },
    [pscustomobject]@{ Name = 'totalCandidateEvaluations'; Direction = 'lower' },
    [pscustomobject]@{ Name = 'totalCostReads'; Direction = 'lower' },
    [pscustomobject]@{ Name = 'totalLifecycleReads'; Direction = 'lower' },
    [pscustomobject]@{ Name = 'queueCapacityReads'; Direction = 'lower' },
    [pscustomobject]@{ Name = 'evaluationBatches'; Direction = 'lower' },
    [pscustomobject]@{ Name = 'candidateEvaluationsPerPurchase'; Direction = 'lower' },
    [pscustomobject]@{ Name = 'observedOperationsPerPurchase'; Direction = 'lower' }
)

$baselineScenarios = @{}
foreach ($scenario in @($baseline.scenarios)) {
    if ($baselineScenarios.ContainsKey($scenario.name)) {
        throw "Baseline contains duplicate scenario '$($scenario.name)'."
    }

    $baselineScenarios[$scenario.name] = $scenario
}

$currentScenarios = @{}
foreach ($scenario in @($report.scenarios)) {
    if ($currentScenarios.ContainsKey($scenario.name)) {
        throw "Current report contains duplicate scenario '$($scenario.name)'."
    }

    $currentScenarios[$scenario.name] = $scenario
}

$baselineNames = @($baselineScenarios.Keys | Sort-Object)
$currentNames = @($currentScenarios.Keys | Sort-Object)
if (($baselineNames -join "`n") -ne ($currentNames -join "`n")) {
    throw "Scenario set differs from baseline. Baseline: $($baselineNames -join ', '). Current: $($currentNames -join ', ')."
}

$allowedFraction = $MaxRegressionPercent / 100.0
$rows = New-Object System.Collections.Generic.List[object]
$failures = New-Object System.Collections.Generic.List[string]

foreach ($scenarioName in $baselineNames) {
    $baselineScenario = $baselineScenarios[$scenarioName]
    $currentScenario = $currentScenarios[$scenarioName]

    foreach ($property in $workloadProperties) {
        $baselineValue = Get-RequiredProperty -Object $baselineScenario.workload -Name $property -Context "Baseline workload '$scenarioName'"
        $currentValue = Get-RequiredProperty -Object $currentScenario.workload -Name $property -Context "Current workload '$scenarioName'"
        if ($baselineValue -ne $currentValue) {
            throw "Workload '$scenarioName.$property' changed from '$baselineValue' to '$currentValue'. Update the scenario and baseline intentionally."
        }
    }

    foreach ($rule in $metricRules) {
        $baselineProperty = Get-RequiredProperty -Object $baselineScenario.metrics -Name $rule.Name -Context "Baseline metrics '$scenarioName'"
        $currentProperty = Get-RequiredProperty -Object $currentScenario.metrics -Name $rule.Name -Context "Current metrics '$scenarioName'"
        if ($null -eq $baselineProperty -or $null -eq $currentProperty) {
            if ($null -ne $baselineProperty -or $null -ne $currentProperty) {
                throw "Metric '$scenarioName.$($rule.Name)' changed between measured and unmeasured."
            }

            continue
        }

        $baselineValue = [double]$baselineProperty
        $currentValue = [double]$currentProperty
        $absoluteAllowance = if ($rule.Direction -eq 'lower' -and $baselineValue -eq 0.0) {
            1.0
        }
        else {
            [Math]::Abs($baselineValue) * $allowedFraction
        }

        $passed = if ($rule.Direction -eq 'higher') {
            $currentValue -ge ($baselineValue - $absoluteAllowance)
        }
        else {
            $currentValue -le ($baselineValue + $absoluteAllowance)
        }

        $delta = if ($baselineValue -eq 0.0) {
            'n/a'
        }
        else {
            '{0:+0.00;-0.00;0.00}%' -f ((($currentValue - $baselineValue) / [Math]::Abs($baselineValue)) * 100.0)
        }

        $rows.Add([pscustomobject]@{
            Scenario = $scenarioName
            Metric = $rule.Name
            Baseline = $baselineValue
            Current = $currentValue
            Delta = $delta
            Direction = $rule.Direction
            Result = if ($passed) { 'PASS' } else { 'FAIL' }
        })

        if (-not $passed) {
            $failures.Add("$scenarioName.$($rule.Name): baseline $baselineValue, current $currentValue, direction $($rule.Direction)")
        }
    }
}

$tableLines = New-Object System.Collections.Generic.List[string]
$tableLines.Add('| Scenario | Metric | Baseline | Current | Delta | Better | Result |')
$tableLines.Add('|---|---|---:|---:|---:|---|---|')
foreach ($row in $rows) {
    $tableLines.Add("| $($row.Scenario) | $($row.Metric) | $($row.Baseline) | $($row.Current) | $($row.Delta) | $($row.Direction) | $($row.Result) |")
}

$tableLines | ForEach-Object { Write-Host $_ }

if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_STEP_SUMMARY)) {
    $summary = @(
        '## Auto Buy deterministic performance history',
        '',
        "Baseline: ``$BaselinePath``  ",
        "Allowed regression: $MaxRegressionPercent%",
        ''
    ) + $tableLines
    Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Value $summary
}

if ($failures.Count -gt 0) {
    throw "Deterministic performance regressions exceeded $MaxRegressionPercent%:`n - $($failures -join "`n - ")"
}

Write-Host "Performance report matches the checked-in history within $MaxRegressionPercent%."
