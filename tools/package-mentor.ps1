[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$GameRoot,
    [string]$OutputDirectory,
    [switch]$IncludeSupportedSuite
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'portable-zip.ps1')
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = Join-Path $root 'artifacts/releases' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$artifacts = [IO.Path]::GetFullPath((Join-Path $root 'artifacts'))
if (-not $OutputDirectory.StartsWith($artifacts, [StringComparison]::OrdinalIgnoreCase)) { throw "OutputDirectory must remain under $artifacts" }
& (Join-Path $PSScriptRoot 'test-modsuite.ps1') -GameRoot $GameRoot
if ($LASTEXITCODE -ne 0) { throw 'Validation failed.' }
[xml]$project = Get-Content -Raw (Join-Path $root 'src/OrbMentor/OrbMentor.csproj')
$version = @($project.Project.PropertyGroup.Version | Where-Object { $_ })[0]
[xml]$buildProps = Get-Content -Raw (Join-Path $root 'Directory.Build.props')
$suiteVersion = @($buildProps.Project.PropertyGroup.SuiteVersion | Where-Object { $_ })[0]
if ($IncludeSupportedSuite -and [string]::IsNullOrWhiteSpace($suiteVersion)) { throw 'SuiteVersion is missing from Directory.Build.props.' }
$name = if ($IncludeSupportedSuite) { "OrbOfCreation-ModSuite-$suiteVersion" } else { "OrbMentor-$version" }
$stage = Join-Path $OutputDirectory $name
$zip = "$stage.zip"
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
if (Test-Path $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
if (Test-Path $zip) { Remove-Item -LiteralPath $zip -Force }
$plugins = Join-Path $stage 'BepInEx/plugins/OrbMentor'
New-Item -ItemType Directory -Force $plugins | Out-Null
$files = @('OrbMentor.dll','OrbModding.Common.dll')
foreach ($file in $files) { Copy-Item (Join-Path $root "src/OrbMentor/bin/Release/netstandard2.1/$file") $plugins }
if ($IncludeSupportedSuite) {
    # Release allowlist: experimental projects must be promoted and explicitly
    # added here before they can enter a public package.
    foreach ($item in @(@('OrbAutomata','OrbAutomata.dll'),@('OrbModConfig','OrbModConfig.dll'))) {
        $dir = Join-Path $stage "BepInEx/plugins/$($item[0])"; New-Item -ItemType Directory -Force $dir | Out-Null
        Copy-Item (Join-Path $root "src/$($item[0])/bin/Release/netstandard2.1/$($item[1])") $dir
    }
}
$forbiddenReleaseDlls = @('OrbAchievementResonance.dll', 'OrbChronomancer.dll')
$unexpected = Get-ChildItem -Recurse -File $stage | Where-Object { $_.Name -in $forbiddenReleaseDlls }
if ($unexpected) { throw "Experimental plugin entered the public package: $($unexpected.Name -join ', ')" }
foreach ($file in @('README.md','CHANGELOG.md','LICENSE','THIRD_PARTY_NOTICES.md')) { Copy-Item (Join-Path $root $file) $stage }
New-PortableZip -SourceDirectory $stage -DestinationPath $zip
$hashes = Get-ChildItem -Recurse -File $stage | ForEach-Object { $h = Get-FileHash -Algorithm SHA256 $_.FullName; "$($h.Hash.ToLowerInvariant())  $($_.Name)" }
$hashes += "$( (Get-FileHash -Algorithm SHA256 $zip).Hash.ToLowerInvariant())  $(Split-Path $zip -Leaf)"
$hashes | Set-Content (Join-Path $OutputDirectory "$name-SHA256SUMS.txt") -Encoding utf8
Write-Host "Created $zip"
