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

Set `OOC_PERFORMANCE_REPORT` to an absolute JSON path to retain the deterministic
Auto Buy measurements, then run
`tools/check-performance-report.ps1 -ReportPath <path>` to compare them with the
reviewed beta history. CI performs this comparison, adds its table to the job
summary, and retains the raw report for 90 days. See
[headless E2E simulation](headless-e2e.md#historical-reports) for metric
definitions and the baseline-update policy.

For a source-level A/B comparison with the last pre-beta `main` engine, run
`tools/compare-autobuy-performance.ps1`. Its compatibility project compiles the
same deterministic workload separately against the untouched reference and
current production sources; it does not copy beta engine changes into the
reference. See [main versus beta compatibility run](headless-e2e.md#main-versus-beta-compatibility-run).

Collect portable production coverage with the checked-in assembly allowlist:

```powershell
dotnet test tests/OrbModding.Tests/OrbModding.Tests.csproj -p:UseGameStubs=true --collect:"XPlat Code Coverage" --settings tests/coverage.runsettings
```

Do not rely on the collector's implicit module selection. It can omit production
plugins when they are loaded through the source-only game-stub graph. The
runsettings file explicitly includes the four supported production assemblies
and excludes the test and game-stub assemblies from the product baseline.

See [headless E2E simulation](headless-e2e.md) for the modeled contracts, metrics, scenario rules, and non-goals.

## Test strategy and merge policy

The suite uses a risk-based pyramid rather than treating every covered line as
equally valuable:

1. Unit and component tests own deterministic policy, arithmetic, state
   transitions, configuration transactions, and failure containment.
2. Headless integration tests execute production reflection and native-adapter
   code against deliberately small `Assembly-CSharp`-shaped fixtures.
3. Headless E2E and performance simulations execute complete engine journeys
   against authoritative queue, economy, lifecycle, and scheduling boundaries.
4. Installed-game contracts prove the audited assembly hashes and exact native
   metadata without launching Unity.
5. UAT proves the remaining Unity wiring, Harmony application, save behavior,
   visual state, player control, and subjective responsiveness.

A test-only PR may merge after its portable suites, coverage floors, repository
hygiene, installed contracts, and real-reference builds pass. It does not need
new active-save UAT when it changes no packaged runtime behavior. A runtime PR
must also pass the proportional UAT gate in
[runtime validation](runtime-validation.md); a release candidate must complete
the full V0-V7 sequence from its exact archive.

### Coverage policy

CI collects the four supported production assemblies in Release and rejects a
regression below these current floors:

| Scope | Enforced line floor | Current Release line coverage |
|---|---:|---:|
| Overall supported production code | 65% | 70.93% |
| Orb Automata | 70% | 74.50% |
| Orb Mentor | 64% | 69.85% |
| Orb Mod Config | 24% | 24.33% |
| Orb Modding Common | 83% | 86.00% |

These are regression floors, not completion targets. New or materially changed
core engines and controllers should aim for at least 80% line and 70% branch
coverage; reflection/native adapters should aim for at least 70% line and 60%
branch coverage. Do not add low-value tests solely to cover Unity view assembly,
plugin bootstrap, or defensive logging. Those paths require focused component
seams, installed contracts, or UAT evidence instead.

`tools/check-coverage.ps1` owns the enforced floors. Raising a floor requires a
passing Release coverage run on the current tree. Lowering one requires an
explicitly documented rationale in the PR.

### Automated scope through P1

The current P0/P1 headless scope covers:

- Auto Buy registry reconciliation, locked-to-available transitions, recreated
  native identity, shared queue capacity, resource snapshots, cost decoding,
  completion settlement, and deterministic queue-filling performance;
- Auto Cast loadout discovery, costs, readiness, charge hold, native firing,
  manual interruption, targeting guards, and Harmony fire-scope separation;
- Auto Concept assignment, lifecycle recovery, resource safety, ownership, and
  exact Harmony targets;
- automatic spell leveling in Locked, Single, and native level-all capability
  states, including lifecycle cancellation;
- Mentor spell, artifact, and alchemy capture-to-native-grant journeys,
  recursion prevention, domain isolation, registry identity, and lifecycle
  cancellation;
- Mod Config staged dependencies, atomic apply/rollback, external refresh,
  UI-work scheduling, listener cleanup, and shell repair policy;
- shared scheduler fairness, mutation admission, failure containment, and
  deterministic performance budgets.

P2 remains runtime-focused: actual Unity layout and navigation, real save/cloud
behavior, a combined-suite soak, real frame-time profiling, and Steam Deck or
Proton validation. Keep these as UAT; reduce any reproducible defect to a
headless regression afterward.

### Time-bounded validation profiles

Routine PR validation is fully automated: portable tests, the tagged headless
and performance jobs, coverage floors, repository hygiene, installed contracts
when a game installation is available, and real-reference builds.

When weekend time is limited, use a 25-35 minute UAT smoke on a disposable save:

1. Install the exact packaged DLLs and confirm one clean load in the BepInEx log.
2. Exercise multi-candidate and single-candidate Auto Buy queue filling, then add
   a manual queue action and trigger emergency disable.
3. Submit one Auto Cast action, one spell-level action, and one Mentor spell XP
   grant while unrelated automation is disabled.
4. Save, reload, return to title, quit normally, restore the backup while the
   game is closed, and verify its checksum.

The time-bounded smoke supports beta iteration but does not authorize a stable
release. The full release profile repeats the matrix at 1x, 2x, 4x, and 8x;
checks new-game and NG+ progression; exercises Auto Concept, all Mentor domains,
and Mod Config; removes each plugin; runs the combined-suite soak; rehearses the
package; and records the result with `tests/runtime/report-template.md`.

On a game computer, run the installed-assembly metadata contracts:

```powershell
$env:OOC_GAME_DIR = 'C:\Program Files (x86)\Steam\steamapps\common\Orb of Creation'
dotnet test tests/OrbModding.GameContractTests/OrbModding.GameContractTests.csproj
```

That suite verifies the audited hashes and exact method, field, inheritance, overload, parameter, and return-type contracts used by Automata, Mentor, Mod Config, and their shared library. It reads PE metadata without launching Unity or loading the game assemblies into the test process. If `OOC_GAME_DIR` is absent, all installed-game tests report `SKIP` instead of pretending compatibility passed.

The same project always runs portable validation of [`data/native-contracts.json`](../../data/native-contracts.json) and the bounded native-source audit. CI therefore fails when a supported gameplay reflection or Harmony source introduces an undeclared literal target, even though GitHub runners do not have the proprietary game assemblies. Framework-only and UI-plumbing reflection is exempted by exact source path with a reason in the manifest. See the [native contract manifest workflow](native-contract-manifest.md).

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
