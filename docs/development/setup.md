# Development setup

[Back to documentation](../README.md) · [Contributing](../../CONTRIBUTING.md)

Portable tests require only the .NET SDK. Production builds also require a local Orb of Creation installation with BepInEx 5 because game and framework binaries are not committed.

```powershell
$env:OOC_GAME_DIR='C:\Program Files (x86)\Steam\steamapps\common\Orb of Creation'
dotnet build src/OrbAutomata/OrbAutomata.csproj -c Release
dotnet build src/OrbMentor/OrbMentor.csproj -c Release
dotnet build src/OrbModConfig/OrbModConfig.csproj -c Release
```

Expected external references are documented in the [source layout](../../src/README.md). Run portable tests with:

```powershell
dotnet test tests/OrbModding.Tests/OrbModding.Tests.csproj -p:UseGameStubs=true
```

Stub-linked outputs are isolated under `bin-stubs/` and `obj-stubs/`. Never deploy them to BepInEx. For installed-game checks, continue with the [testing hub](../testing/README.md) and the [runtime validation protocol](../testing/runtime-validation.md).

Orb Chronomancer and Orb Achievement Resonance are not tracked on this supported branch. Switch deliberately to `codex/experimental-chronomancer-resonance` for that work; never copy its DLLs into a supported-suite rehearsal.
