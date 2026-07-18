# Compatibility, testing, and releases

[Back to roadmap](../plans/roadmap.md) · [Headless E2E simulation](headless-e2e.md) · [Local runtime UAT protocol](runtime-validation.md)

## Supported baseline

- Windows 64-bit
- Unity `6000.0.70`
- Mono scripting backend
- BepInEx `5.4.23.x`
- Plugin target: `netstandard2.1`
- Steam Deck through the Windows game under Proton

Native Linux builds, BepInEx 6, and other game versions are unsupported until explicitly tested.

## Automated test layers

Run the deterministic suite without a game installation:

```bash
dotnet test tests/OrbModding.Tests/OrbModding.Tests.csproj -p:UseGameStubs=true
```

The portable tests use a source-only `OrbModding.GameStubs` project to compile the supported plugin seams. They validate Automata, Mentor, Mod Config, shared scheduling/status controls, policy, lifecycle behavior, safe defaults, timing, reflection fixtures, UUID uniqueness, entity type counts, and known mappings. Experimental Chronomancer and Resonance tests are not present on this branch. Portable tests do not claim game API compatibility; production builds ignore the stubs and require `OOC_GAME_DIR`.

Portable automation has three scopes:

- unit/component tests isolate policies, reflection fixtures, schedulers, and lifecycle transitions;
- headless integration tests connect production native adapters to focused game API stubs;
- headless E2E runs the real mod engine against a deterministic simulated game boundary for complete queue, economy, lifecycle, and failure journeys.

Run the headless E2E and deterministic performance scopes independently with:

```powershell
dotnet test tests/OrbModding.Tests/OrbModding.Tests.csproj -p:UseGameStubs=true --filter "Category=HeadlessIntegration"
dotnet test tests/OrbModding.Tests/OrbModding.Tests.csproj -p:UseGameStubs=true --filter "Category=HeadlessE2E"
dotnet test tests/OrbModding.Tests/OrbModding.Tests.csproj -p:UseGameStubs=true --filter "Category=PerformanceSimulation"
```

See [headless E2E simulation](headless-e2e.md) for the modeled contracts, metrics, scenario rules, and non-goals.

On a game computer, run the installed-assembly metadata contracts:

```powershell
$env:OOC_GAME_DIR = 'C:\Program Files (x86)\Steam\steamapps\common\Orb of Creation'
dotnet test tests/OrbModding.GameContractTests/OrbModding.GameContractTests.csproj
```

That suite verifies the audited hashes and exact method, field, inheritance, overload, parameter, and return-type contracts used by Automata, Mentor, Mod Config, and their shared library. It reads PE metadata without launching Unity or loading the game assemblies into the test process. If `OOC_GAME_DIR` is absent, all installed-game tests report `SKIP` instead of pretending compatibility passed.

Run both layers and all real-reference plugin builds with:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/test-modsuite.ps1 -GameRoot 'C:\Program Files (x86)\Steam\steamapps\common\Orb of Creation'
```

Every defect in game-independent code should first receive a failing regression test. Reflection code should be exercised with deliberately ambiguous and missing members. Runtime-only discoveries from the game computer should be reduced into a deterministic fixture or policy test whenever possible.

## Compatibility targets

### Clean installation

Every release must work with only BepInEx and the plugin installed.

### Overlapping automation mods

The supported release profile uses one auto-buy mod. Automata does not detect, patch, coordinate with, or yield ownership to AutobuyOrb or other third-party buyers. Concurrent buyers are unsupported because they may race for resources, queue capacity, and temporary global multi-buy state.

### Save compatibility

- Runtime mutations use normal game APIs.
- No concurrent direct writes to `.sav` files.
- Test save/load at every supported speed preset.
- Keep manual backup instructions in release documentation.

## Test matrix

| Area | Clean | 1× | 2× | 4× | 8× |
|---|---:|---:|---:|---:|---:|
| Start/load/return to title | Required | Required | Required | Required | Required |
| Auto Buy queue and reserves | Required | Required | Required | Required | Required |
| Auto Cast and manual interruption | Required | Required | Required | Required | Required |
| Auto Concept rotation/resource safety | Required | Required | Required | Required | Required |
| Auto spell leveling | Required | Required | Required | Required | Required |
| Mentor spells/artifacts/alchemy | Required | Required | Required | Required | Required |
| Mods UI navigation/edit/apply | Required | Required | Required | Required | Required |
| Save/reload and plugin removal | Required | Required | Required | Required | Required |
| Extended combined-suite session | Required | Required | Optional | Required | Required |

The table defines required scenarios, not current results. Results should be recorded under `tests/` with date, game version, plugin version, and save used.

Use the [local runtime validation protocol](runtime-validation.md) for the ordered build, static-audit, load-smoke, read-only, active, rollback, combined-mod, and release gates. Those real-game checks are UAT. Computer control may be used to perform or observe UAT, but it is never a dependency of headless E2E or performance simulation. A computer without the game can run the automated tests, but cannot mark any real-reference or runtime UAT gate as passed.

## Runtime assertions

In debug builds, detect and log:

- Unsupported scene or game version.
- Invalid speed multiplier.
- Timing values not restored on unload.
- Automation action exceeding its time budget.
- Missing runtime object or expected method.
- Queue size becoming negative or inconsistent.
- Resource quantity becoming NaN or infinity unexpectedly.

## Release channels

- `0.x-dev`: local experiments; no stability promise.
- `0.x-alpha`: packaged for testers; configuration may change.
- `0.x-beta`: feature-complete with compatibility testing.
- `1.0`: stable configuration and documented supported build.

## Release package

The supported suite archive follows the explicit package allowlist:

```text
BepInEx/plugins/OrbAutomata/OrbAutomata.dll
BepInEx/plugins/OrbMentor/OrbMentor.dll
BepInEx/plugins/OrbMentor/OrbModding.Common.dll
BepInEx/plugins/OrbModConfig/OrbModConfig.dll
README.md
CHANGELOG.md
LICENSE
THIRD_PARTY_NOTICES.md
```

Do not include experimental DLLs, game assemblies, BepInEx assemblies, Harmony, debug symbols unless intentionally published, or local configuration files containing user preferences.

## Versioning and game updates

- Record the tested game build and Unity version in every release.
- Fail softly when expected types or methods are missing.
- Keep Harmony targets small and signature-specific.
- Re-run the timing and save test matrices after every game update.
- Prefer direct public game APIs over transpilers.
