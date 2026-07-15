[CmdletBinding()]
param(
    [string]$GameRoot = $env:OOC_GAME_DIR,
    [switch]$SkipRealBuild
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path $PSScriptRoot -Parent

function Invoke-DotNet {
    param([Parameter(Mandatory)][string[]]$Arguments)

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

function Assert-PluginOutput {
    param(
        [Parameter(Mandatory)][string]$ProjectName,
        [Parameter(Mandatory)][string]$PluginAssembly
    )

    $outputDirectory = Join-Path $repositoryRoot "src/$ProjectName/bin/Release/netstandard2.1"
    $names = @(Get-ChildItem -LiteralPath $outputDirectory -File | Select-Object -ExpandProperty Name)
    foreach ($required in @($PluginAssembly, 'OrbModding.Common.dll')) {
        if ($required -notin $names) {
            throw "Required build artifact is missing from ${ProjectName}: $required"
        }
    }

    $forbiddenPatterns = @(
        'Assembly-CSharp*.dll',
        'BepInEx.dll',
        '0Harmony.dll',
        'UnityEngine*.dll',
        'OrbModding.GameStubs.dll'
    )
    foreach ($pattern in $forbiddenPatterns) {
        $forbidden = @($names | Where-Object { $_ -like $pattern })
        if ($forbidden.Count -gt 0) {
            throw "Game/runtime assemblies leaked into ${ProjectName} output: $($forbidden -join ', ')"
        }
    }
}

Push-Location $repositoryRoot
try {
    Write-Host 'Running portable behavior and knowledge-map tests...'
    Invoke-DotNet @(
        'test',
        'tests/OrbModding.Tests/OrbModding.Tests.csproj',
        '-p:UseGameStubs=true'
    )

    if ([string]::IsNullOrWhiteSpace($GameRoot)) {
        Remove-Item Env:OOC_GAME_DIR -ErrorAction SilentlyContinue
        Write-Host 'OOC_GAME_DIR is not set; verifying that installed-game contracts skip cleanly...'
        Invoke-DotNet @(
            'test',
            'tests/OrbModding.GameContractTests/OrbModding.GameContractTests.csproj'
        )
        Write-Warning 'Installed-game contract tests and real-reference builds were skipped. Pass -GameRoot on a game computer.'
        return
    }

    $GameRoot = [System.IO.Path]::GetFullPath($GameRoot)
    $assemblyCSharp = Join-Path $GameRoot 'Orb Of Creation_Data/Managed/Assembly-CSharp.dll'
    if (-not (Test-Path -LiteralPath $assemblyCSharp)) {
        throw "Game assembly not found under GameRoot: $assemblyCSharp"
    }

    $env:OOC_GAME_DIR = $GameRoot
    Write-Host "Running installed-game contracts against $GameRoot..."
    Invoke-DotNet @(
        'test',
        'tests/OrbModding.GameContractTests/OrbModding.GameContractTests.csproj'
    )

    if (-not $SkipRealBuild) {
        Write-Host 'Building every plugin against the installed game references...'
        $builds = @(
            [pscustomobject]@{ Project = 'src/OrbChronomancer/OrbChronomancer.csproj'; Name = 'OrbChronomancer'; Assembly = 'OrbChronomancer.dll' }
            [pscustomobject]@{ Project = 'src/OrbAutomata/OrbAutomata.csproj'; Name = 'OrbAutomata'; Assembly = 'OrbAutomata.dll' }
            [pscustomobject]@{ Project = 'src/OrbAchievementResonance/OrbAchievementResonance.csproj'; Name = 'OrbAchievementResonance'; Assembly = 'OrbAchievementResonance.dll' }
            [pscustomobject]@{ Project = 'src/OrbModConfig/OrbModConfig.csproj'; Name = 'OrbModConfig'; Assembly = 'OrbModConfig.dll' }
        )
        foreach ($build in $builds) {
            Invoke-DotNet @('build', $build.Project, '-c', 'Release')
            Assert-PluginOutput -ProjectName $build.Name -PluginAssembly $build.Assembly
        }
    }
}
finally {
    Pop-Location
}
