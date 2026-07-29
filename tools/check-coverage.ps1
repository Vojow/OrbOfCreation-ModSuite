[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$CoveragePath,
    # One assembly, one coverlet package, one floor: the single package's rate is the overall rate,
    # so a second number here could only ever disagree with itself. Set two points under the rate
    # measured after the legacy runtime and its coordinator retired.
    [double]$MinimumOverallLineRate = 0.734
)

$ErrorActionPreference = 'Stop'
$resolvedCoverage = [IO.Path]::GetFullPath($CoveragePath)
if (-not (Test-Path -LiteralPath $resolvedCoverage -PathType Leaf)) {
    throw "Coverage report was not found: $resolvedCoverage"
}

[xml]$report = Get-Content -LiteralPath $resolvedCoverage -Raw
$overall = [double]::Parse(
    $report.coverage.'line-rate',
    [Globalization.CultureInfo]::InvariantCulture)
$overallBranch = [double]::Parse(
    $report.coverage.'branch-rate',
    [Globalization.CultureInfo]::InvariantCulture)
$productionPackage = 'OrbModSuite'

$failures = [Collections.Generic.List[string]]::new()
if ($overall -lt $MinimumOverallLineRate) {
    $failures.Add("overall line rate $($overall.ToString('P2')) is below $($MinimumOverallLineRate.ToString('P2'))")
}

$packages = @($report.coverage.packages.package)
$package = $packages | Where-Object { $_.name -eq $productionPackage } | Select-Object -First 1
if ($null -eq $package) {
    $failures.Add("required production package $productionPackage is absent from coverage")
}
else {
    $rate = [double]::Parse(
        $package.'line-rate',
        [Globalization.CultureInfo]::InvariantCulture)
    $branchRate = [double]::Parse(
        $package.'branch-rate',
        [Globalization.CultureInfo]::InvariantCulture)
    Write-Host "$productionPackage line coverage: $($rate.ToString('P2')) (minimum $($MinimumOverallLineRate.ToString('P2'))); branch coverage: $($branchRate.ToString('P2')) (diagnostic)"
    if ($rate -lt $MinimumOverallLineRate) {
        $failures.Add("$productionPackage line rate $($rate.ToString('P2')) is below $($MinimumOverallLineRate.ToString('P2'))")
    }
}

Write-Host "Overall production line coverage: $($overall.ToString('P2')) (minimum $($MinimumOverallLineRate.ToString('P2')))"
Write-Host "Overall production branch coverage: $($overallBranch.ToString('P2')) (diagnostic)"
if ($failures.Count -gt 0) {
    throw "Coverage regression:`n - $($failures -join "`n - ")"
}
