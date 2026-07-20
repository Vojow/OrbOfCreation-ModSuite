[CmdletBinding()]
param(
    [ValidateSet(
        'Fast',
        'Reliability',
        'AutoBuyDecision',
        'AutoBuyReliability',
        'AutoBuyPerformance',
        'PerformanceAll',
        'Headless',
        'Replay',
        'ExternalProcess',
        'All')]
    [string]$Lane = 'Fast',

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [switch]$NoBuild,
    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path $PSScriptRoot -Parent
$project = Join-Path $repositoryRoot 'tests/OrbModding.Tests/OrbModding.Tests.csproj'
$resultDirectory = Join-Path $repositoryRoot "artifacts/test-results/$($Lane.ToLowerInvariant())"
$filters = @{
    Fast = 'Category!=PerformanceSimulation&Category!=ExternalProcess'
    Reliability = 'Category=Reliability|Category=AutoBuyReliability'
    AutoBuyDecision = 'Category=AutoBuyDecision'
    AutoBuyReliability = 'Category=AutoBuyReliability'
    AutoBuyPerformance = 'Category=AutoBuyPerformance'
    PerformanceAll = 'Category=PerformanceSimulation'
    Headless = 'Category=HeadlessIntegration|Category=HeadlessE2E'
    Replay = 'FullyQualifiedName~RuntimeReplayTests'
    ExternalProcess = 'Category=ExternalProcess'
    All = $null
}

New-Item -ItemType Directory -Path $resultDirectory -Force | Out-Null
$arguments = @(
    'test',
    $project,
    '--configuration', $Configuration,
    '-p:UseGameStubs=true',
    '--results-directory', $resultDirectory,
    '--logger', "trx;LogFileName=$($Lane.ToLowerInvariant()).trx"
)

if ($NoBuild) {
    $arguments += '--no-build'
}
if ($NoRestore) {
    $arguments += '--no-restore'
}
if (-not [string]::IsNullOrWhiteSpace($filters[$Lane])) {
    $arguments += @('--filter', $filters[$Lane])
}

Push-Location $repositoryRoot
try {
    Write-Host "Running portable test lane '$Lane'..."
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Portable test lane '$Lane' failed with exit code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}
