[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$CoveragePath,
    [double]$MinimumOverallLineRate = 0.65
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
$packageMinimums = [ordered]@{
    'OrbAutomata' = 0.70
    'OrbMentor' = 0.64
    'OrbModConfig' = 0.24
    'OrbModding.Common' = 0.83
}

$failures = [Collections.Generic.List[string]]::new()
if ($overall -lt $MinimumOverallLineRate) {
    $failures.Add("overall line rate $($overall.ToString('P2')) is below $($MinimumOverallLineRate.ToString('P2'))")
}

$packages = @($report.coverage.packages.package)
foreach ($entry in $packageMinimums.GetEnumerator()) {
    $package = $packages | Where-Object { $_.name -eq $entry.Key } | Select-Object -First 1
    if ($null -eq $package) {
        $failures.Add("required production package $($entry.Key) is absent from coverage")
        continue
    }

    $rate = [double]::Parse(
        $package.'line-rate',
        [Globalization.CultureInfo]::InvariantCulture)
    Write-Host "$($entry.Key) line coverage: $($rate.ToString('P2')) (minimum $(([double]$entry.Value).ToString('P2')))"
    if ($rate -lt [double]$entry.Value) {
        $failures.Add("$($entry.Key) line rate $($rate.ToString('P2')) is below $(([double]$entry.Value).ToString('P2'))")
    }
}

Write-Host "Overall production line coverage: $($overall.ToString('P2')) (minimum $($MinimumOverallLineRate.ToString('P2')))"
if ($failures.Count -gt 0) {
    throw "Coverage regression:`n - $($failures -join "`n - ")"
}
