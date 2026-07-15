[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$GameRoot,

    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'portable-zip.ps1')
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repositoryRoot 'artifacts/releases'
}

$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts'))
if (-not $OutputDirectory.StartsWith($artifactsRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "OutputDirectory must remain under the repository artifacts directory: $artifactsRoot"
}

$licensePath = Join-Path $repositoryRoot 'LICENSE'
if (-not (Test-Path -LiteralPath $licensePath)) {
    throw 'LICENSE is required before creating a public release package.'
}

$projectPath = Join-Path $repositoryRoot 'src/OrbAutomata/OrbAutomata.csproj'
[xml]$project = Get-Content -Raw -LiteralPath $projectPath
$version = @($project.Project.PropertyGroup.Version | Where-Object { $_ })[0]
if ([string]::IsNullOrWhiteSpace($version)) {
    throw 'Could not read the Orb Automata version from OrbAutomata.csproj.'
}

& (Join-Path $PSScriptRoot 'test-modsuite.ps1') -GameRoot $GameRoot
if ($LASTEXITCODE -ne 0) {
    throw "The validation pipeline failed with exit code $LASTEXITCODE."
}

$packageName = "OrbAutomata-$version"
$stagingDirectory = Join-Path $OutputDirectory $packageName
$zipPath = Join-Path $OutputDirectory "$packageName.zip"

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
if (Test-Path -LiteralPath $stagingDirectory) {
    Remove-Item -LiteralPath $stagingDirectory -Recurse -Force
}
if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

$pluginDirectory = Join-Path $stagingDirectory 'BepInEx/plugins/OrbAutomata'
New-Item -ItemType Directory -Force -Path $pluginDirectory | Out-Null

$files = @(
    @{ Source = 'src/OrbAutomata/bin/Release/netstandard2.1/OrbAutomata.dll'; Destination = $pluginDirectory },
    @{ Source = 'src/OrbModConfig/bin/Release/netstandard2.1/OrbModConfig.dll'; Destination = $pluginDirectory },
    @{ Source = 'src/OrbAutomata/bin/Release/netstandard2.1/OrbModding.Common.dll'; Destination = $pluginDirectory },
    @{ Source = 'README.md'; Destination = $stagingDirectory },
    @{ Source = 'CHANGELOG.md'; Destination = $stagingDirectory },
    @{ Source = 'LICENSE'; Destination = $stagingDirectory },
    @{ Source = 'THIRD_PARTY_NOTICES.md'; Destination = $stagingDirectory }
)

foreach ($file in $files) {
    $source = Join-Path $repositoryRoot $file.Source
    if (-not (Test-Path -LiteralPath $source)) {
        throw "Required package file is missing: $source"
    }
    Copy-Item -LiteralPath $source -Destination $file.Destination
}

New-PortableZip -SourceDirectory $stagingDirectory -DestinationPath $zipPath

$checksumTargets = @(
    (Join-Path $pluginDirectory 'OrbAutomata.dll'),
    (Join-Path $pluginDirectory 'OrbModConfig.dll'),
    (Join-Path $pluginDirectory 'OrbModding.Common.dll'),
    $zipPath
)
$checksums = foreach ($path in $checksumTargets) {
    $hash = Get-FileHash -Algorithm SHA256 -LiteralPath $path
    '{0}  {1}' -f $hash.Hash.ToLowerInvariant(), (Split-Path $path -Leaf)
}
$checksums | Set-Content -LiteralPath (Join-Path $OutputDirectory 'SHA256SUMS.txt') -Encoding utf8

Write-Host "Created $zipPath"
Write-Host "Created $(Join-Path $OutputDirectory 'SHA256SUMS.txt')"
