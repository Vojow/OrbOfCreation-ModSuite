# Compatibility, testing, and releases

[Back to roadmap](../plans/roadmap.md) · [Local runtime validation protocol](runtime-validation.md)

## Supported baseline

- Windows 64-bit
- Unity `6000.0.70`
- Mono scripting backend
- BepInEx `5.4.23.x`
- Plugin target: `netstandard2.1`

Other platforms and game versions are unsupported until explicitly tested.

## Automated test layers

Run the deterministic suite without a game installation:

```bash
dotnet test tests/OrbModding.Tests/OrbModding.Tests.csproj -p:UseGameStubs=true
```

The portable tests use a source-only `OrbModding.GameStubs` project to compile the plugin seams. They validate policy, lifecycle behavior, safe defaults, timing rollback, reflection fixtures, UUID uniqueness, entity type counts, known mappings, and Resonance target/modifier ownership. They do not claim game API compatibility. Production builds ignore the stubs and require `OOC_GAME_DIR`.

On a game computer, run the installed-assembly metadata contracts:

```powershell
$env:OOC_GAME_DIR = 'C:\Program Files (x86)\Steam\steamapps\common\Orb of Creation'
dotnet test tests/OrbModding.GameContractTests/OrbModding.GameContractTests.csproj
```

That suite verifies the audited hashes and exact method, field, inheritance, overload, parameter, and return-type contracts used by all three mods. It reads PE metadata without launching Unity or loading the game assemblies into the test process. If `OOC_GAME_DIR` is absent, all installed-game tests report `SKIP` instead of pretending compatibility passed.

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
| Start/load game | ✓ | ✓ | ✓ | ✓ | ✓ |
| Passive resources | ✓ | ✓ | ✓ | ✓ | ✓ |
| Research/crafting | ✓ | ✓ | ✓ | ✓ | ✓ |
| Alchemy | ✓ | ✓ | ✓ | ✓ | ✓ |
| Combat | ✓ | ✓ | ✓ | ✓ | ✓ |
| Save/reload | ✓ | ✓ | ✓ | ✓ | ✓ |
| Return to title | ✓ | ✓ | ✓ | ✓ | ✓ |
| Extended session | ✓ | ✓ | — | ✓ | ✓ |
| Native tooltips | ✓ | ✓ | ✓ | ✓ | ✓ |
| Toolbox edit/save/reload | ✓ | ✓ | ✓ | ✓ | ✓ |

The table defines required scenarios, not current results. Results should be recorded under `tests/` with date, game version, plugin version, and save used.

Use the [local runtime validation protocol](runtime-validation.md) for the ordered build, static-audit, load-smoke, read-only, active, rollback, combined-mod, and release gates. A computer without the game can run the automated tests, but cannot mark any real-reference or runtime gate as passed.

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

Each plugin release should contain:

```text
PluginName.dll
README.md
CHANGELOG.md
LICENSE
```

Do not include game assemblies, BepInEx assemblies, Harmony, debug symbols unless intentionally published, or local configuration files containing user preferences.

## Versioning and game updates

- Record the tested game build and Unity version in every release.
- Fail softly when expected types or methods are missing.
- Keep Harmony targets small and signature-specific.
- Re-run the timing and save test matrices after every game update.
- Prefer direct public game APIs over transpilers.
