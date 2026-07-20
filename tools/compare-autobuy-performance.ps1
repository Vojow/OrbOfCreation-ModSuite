param(
    [string]$ReferenceRef = '7f61f21d27c4b1a4996ba9888a303c42bb74e81c',

    [ValidateSet('Auto', 'Legacy', 'Intermediate', 'Current')]
    [string]$ReferenceApi = 'Auto',

    [string]$ReferenceLabel = 'main reference',

    [string]$CurrentLabel = 'beta',

    [string]$Heading = 'Auto Buy main/beta deterministic comparison',

    [string]$OutputDirectory = 'artifacts/performance/ab'
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$projectPath = Join-Path $repositoryRoot 'tests\OrbModding.AutoBuyComparison\OrbModding.AutoBuyComparison.csproj'
$outputPath = if ([System.IO.Path]::IsPathRooted($OutputDirectory)) {
    [System.IO.Path]::GetFullPath($OutputDirectory)
}
else {
    [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputDirectory))
}

if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
    throw "The Auto Buy comparison project is missing: $projectPath"
}

New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
$referenceReportPath = Join-Path $outputPath 'reference.json'
$betaReportPath = Join-Path $outputPath 'current.json'
$comparisonPath = Join-Path $outputPath 'comparison.md'

function Invoke-Git {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)

    & git @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

function Resolve-Commit {
    param([Parameter(Mandatory = $true)][string]$Ref)

    $resolved = & git rev-parse --verify "$Ref^{commit}" 2>$null
    if ($LASTEXITCODE -ne 0) {
        & git fetch --no-tags --depth=1 origin $Ref
        if ($LASTEXITCODE -ne 0) {
            throw "Unable to fetch comparison reference '$Ref'."
        }

        $resolved = & git rev-parse --verify FETCH_HEAD
        if ($LASTEXITCODE -ne 0) {
            throw "Unable to resolve fetched comparison reference '$Ref'."
        }
    }

    return $resolved.Trim()
}

function Invoke-ComparisonRunner {
    param(
        [Parameter(Mandatory = $true)][string]$SourceRoot,
        [Parameter(Mandatory = $true)][string]$ReportPath,
        [Parameter(Mandatory = $true)][string]$SourceLabel,
        [Parameter(Mandatory = $true)][bool]$LegacyApi,
        [Parameter(Mandatory = $true)][bool]$IntermediateQueueApi
    )

    $arguments = @(
        'run',
        '--project', $projectPath,
        '--configuration', 'Release',
        '--no-incremental',
        '-p:UseGameStubs=true',
        "-p:ComparisonSourceRoot=$SourceRoot"
    )
    if ($LegacyApi) {
        $arguments += '-p:LegacyMainApi=true'
    }
    if ($IntermediateQueueApi) {
        $arguments += '-p:IntermediateQueueApi=true'
    }

    $arguments += @('--', '--report', $ReportPath, '--source', $SourceLabel)
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "The $SourceLabel Auto Buy comparison run failed with exit code $LASTEXITCODE."
    }
}

function Get-ReferenceApiProfile {
    param(
        [Parameter(Mandatory = $true)][string]$SourceRoot,
        [Parameter(Mandatory = $true)][string]$Mode
    )

    if ($Mode -eq 'Legacy') {
        return [pscustomobject]@{ Legacy = $true; IntermediateQueue = $false }
    }
    if ($Mode -eq 'Intermediate') {
        return [pscustomobject]@{ Legacy = $true; IntermediateQueue = $true }
    }
    if ($Mode -eq 'Current') {
        return [pscustomobject]@{ Legacy = $false; IntermediateQueue = $false }
    }

    $enginePath = Join-Path $SourceRoot 'src\OrbAutomata\AutoBuyEngine.cs'
    if (-not (Test-Path -LiteralPath $enginePath -PathType Leaf)) {
        throw "Cannot determine the reference API because AutoBuyEngine.cs is missing: $enginePath"
    }

    $engineSource = Get-Content -LiteralPath $enginePath -Raw
    $legacy = $engineSource.IndexOf('ownsActionFamily', [StringComparison]::Ordinal) -lt 0
    $intermediateQueue = $legacy -and
        $engineSource.IndexOf('TryCaptureQueueCapacity', [StringComparison]::Ordinal) -ge 0
    return [pscustomobject]@{
        Legacy = $legacy
        IntermediateQueue = $intermediateQueue
    }
}

function Read-JsonReport {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Comparison report was not produced: $Path"
    }

    return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
}

$referenceCommit = Resolve-Commit -Ref $ReferenceRef
$betaCommit = (& git rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) {
    throw 'Unable to resolve the beta working-tree commit.'
}

$betaDirty = -not [string]::IsNullOrWhiteSpace((& git status --porcelain --untracked-files=no | Out-String))
$betaSourceLabel = "$CurrentLabel@$betaCommit" + $(if ($betaDirty) { '+working-tree' } else { '' })
$referenceSourceLabel = "$ReferenceLabel@$referenceCommit"
$worktreePath = Join-Path ([System.IO.Path]::GetTempPath()) ('ooc-autobuy-ab-' + [guid]::NewGuid().ToString('N'))
$worktreeAdded = $false

try {
    Invoke-Git worktree add --detach $worktreePath $referenceCommit
    $worktreeAdded = $true
    $referenceApiProfile = Get-ReferenceApiProfile -SourceRoot $worktreePath -Mode $ReferenceApi

    Invoke-ComparisonRunner `
        -SourceRoot $worktreePath `
        -ReportPath $referenceReportPath `
        -SourceLabel $referenceSourceLabel `
        -LegacyApi $referenceApiProfile.Legacy `
        -IntermediateQueueApi $referenceApiProfile.IntermediateQueue
    Invoke-ComparisonRunner `
        -SourceRoot $repositoryRoot `
        -ReportPath $betaReportPath `
        -SourceLabel $betaSourceLabel `
        -LegacyApi $false `
        -IntermediateQueueApi $false
}
finally {
    if ($worktreeAdded) {
        $resolvedWorktree = [System.IO.Path]::GetFullPath($worktreePath)
        $temporaryRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
        if (-not $resolvedWorktree.StartsWith($temporaryRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove a comparison worktree outside the temporary directory: $resolvedWorktree"
        }

        Invoke-Git worktree remove --force $resolvedWorktree
    }
}

$reference = Read-JsonReport -Path $referenceReportPath
$beta = Read-JsonReport -Path $betaReportPath
if ($reference.schemaVersion -ne $beta.schemaVersion -or $reference.suite -ne $beta.suite) {
    throw 'The reference and beta reports do not use the same schema and suite.'
}

$metricDefinitions = @(
    [pscustomobject]@{ Name = 'totalSubmitted'; Label = 'Submitted purchases'; Direction = 'higher' },
    [pscustomobject]@{ Name = 'queueHighWater'; Label = 'Queue high-water'; Direction = 'higher' },
    [pscustomobject]@{ Name = 'finalQueueDepth'; Label = 'Final queue depth'; Direction = 'higher' },
    [pscustomobject]@{ Name = 'minimumQueueAfterSaturation'; Label = 'Minimum queue after saturation'; Direction = 'higher' },
    [pscustomobject]@{ Name = 'distinctCandidatesSubmitted'; Label = 'Distinct candidates'; Direction = 'higher' },
    [pscustomobject]@{ Name = 'idleFramesWithPurchasableWork'; Label = 'Idle purchasable frames'; Direction = 'lower' },
    [pscustomobject]@{ Name = 'framesToNinetyPercentQueue'; Label = 'Frames to 90% queue'; Direction = 'lower' },
    [pscustomobject]@{ Name = 'maximumEvaluationsInFrame'; Label = 'Maximum evaluations/frame'; Direction = 'lower' },
    [pscustomobject]@{ Name = 'maximumPurchasesInFrame'; Label = 'Maximum purchases/frame'; Direction = 'higher' },
    [pscustomobject]@{ Name = 'totalCandidateEvaluations'; Label = 'Candidate evaluations'; Direction = 'diagnostic' },
    [pscustomobject]@{ Name = 'totalLifecycleReads'; Label = 'Lifecycle reads'; Direction = 'diagnostic' },
    [pscustomobject]@{ Name = 'queueCapacityReads'; Label = 'Queue-capacity reads'; Direction = 'diagnostic' },
    [pscustomobject]@{ Name = 'observedOperationsPerPurchase'; Label = 'Observed operations/purchase'; Direction = 'diagnostic' }
)

$referenceScenarios = @{}
foreach ($scenario in @($reference.scenarios)) {
    $referenceScenarios[$scenario.name] = $scenario
}

$betaScenarios = @{}
foreach ($scenario in @($beta.scenarios)) {
    $betaScenarios[$scenario.name] = $scenario
}

$referenceNames = @($referenceScenarios.Keys | Sort-Object)
$betaNames = @($betaScenarios.Keys | Sort-Object)
if (($referenceNames -join "`n") -ne ($betaNames -join "`n")) {
    throw 'The reference and beta reports do not contain the same scenarios.'
}

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add("# $Heading")
$lines.Add('')
$lines.Add("- $($ReferenceLabel): ``$($reference.sourceCommit)``")
$lines.Add("- $($CurrentLabel): ``$($beta.sourceCommit)``")
$lines.Add('- Workload: 166 mixed candidates, 304 native queue slots, 900 deterministic frames; stable 1.1 ms scenarios plus a 0.05 ms/eight-completion burst scenario')
$lines.Add('- Diagnostic operation counts are not scored because an engine can appear cheaper by evaluating or serving fewer candidates.')
$lines.Add('')
$lines.Add("| Scenario | Metric | $ReferenceLabel | $CurrentLabel | Delta | Reading |")
$lines.Add('|---|---|---:|---:|---:|---|')

foreach ($scenarioName in $referenceNames) {
    $referenceScenario = $referenceScenarios[$scenarioName]
    $betaScenario = $betaScenarios[$scenarioName]
    if (($referenceScenario.workload | ConvertTo-Json -Compress) -ne
        ($betaScenario.workload | ConvertTo-Json -Compress)) {
        throw "Scenario workload differs for '$scenarioName'."
    }

    foreach ($definition in $metricDefinitions) {
        $referenceValue = [double]$referenceScenario.metrics.($definition.Name)
        $betaValue = [double]$betaScenario.metrics.($definition.Name)
        $deltaValue = $betaValue - $referenceValue
        $deltaText = if ($referenceValue -eq 0.0) {
            $deltaValue.ToString('+0.######;-0.######;0', [Globalization.CultureInfo]::InvariantCulture)
        }
        else {
            (($deltaValue / [Math]::Abs($referenceValue))).ToString(
                '+0.00%;-0.00%;0.00%',
                [Globalization.CultureInfo]::InvariantCulture)
        }

        $reading = if ($definition.Direction -eq 'diagnostic') {
            'diagnostic'
        }
        elseif ($betaValue -eq $referenceValue) {
            'same'
        }
        elseif (($definition.Direction -eq 'higher' -and $betaValue -gt $referenceValue) -or
                ($definition.Direction -eq 'lower' -and $betaValue -lt $referenceValue)) {
            "$CurrentLabel better"
        }
        else {
            "$ReferenceLabel better"
        }

        $lines.Add(
            "| $scenarioName | $($definition.Label) | $referenceValue | $betaValue | $deltaText | $reading |")
    }
}

$lines.Add('')
$lines.Add('Interpret idle purchasable frames together with minimum queue depth: a batched refill can produce more no-purchase frames while keeping substantially more work queued. Native elapsed-time and allocation profiling remain separate UAT evidence.')

Set-Content -LiteralPath $comparisonPath -Value $lines -Encoding utf8
if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_STEP_SUMMARY)) {
    Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Value $lines
}

$lines | ForEach-Object { Write-Host $_ }
Write-Host "A/B reports written under $outputPath"
