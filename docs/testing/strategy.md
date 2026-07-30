# Repository test strategy

[Back to testing hub](README.md) · [Headless E2E simulation](headless-e2e.md) · [Runtime UAT protocol](runtime-validation.md)

## Supported baseline

- Windows 64-bit
- Unity `6000.0.70`
- Mono scripting backend
- BepInEx `5.4.23.x`
- Plugin target: `netstandard2.1`
- Steam Deck through the Windows game under Proton

Native Linux builds, BepInEx 6, and other game versions are unsupported until explicitly tested.

## Automated test layers

Run the complete portable suite without a game installation:

```bash
./script/test
```

The script runs every ordinary partition, then the isolated compile-time profiling test project, under one
hard 60-second deadline. Profiling outputs and intermediates cannot replace ordinary build artifacts.

Concurrency tests synchronize on exact events or observable state and give every thread join a finite
local failure deadline. A sleep interval is not evidence that work did not occur, and a multi-second test
timeout is a failure bound rather than an expected test duration.

The portable tests use a source-only `OrbModding.GameStubs` project to compile the supported plugin seams. They validate Automata, Mentor, Mod Config, shared scheduling/status/ownership controls, policy, lifecycle behavior, safe defaults, timing, reflection fixtures, UUID uniqueness, entity type counts, and known mappings. Portable tests do not claim game API compatibility; production builds ignore the stubs and require `OOC_GAME_DIR`.

Portable automation has three scopes:

- unit/component tests isolate policies, reflection fixtures, schedulers, and lifecycle transitions;
- headless integration tests connect production native adapters to focused game API stubs;
- headless E2E runs the real mod engine against a deterministic simulated game boundary for complete queue, economy, lifecycle, and failure journeys.

The production Common ServiceCycle engine is covered under `tests/OrbModding.Tests/Runtime/ServiceCycle`, with the Auto Harvest adapter and composition contracts under `tests/OrbModding.Tests/Services/AutoHarvest/Runtime/ServiceCycle`. Its portable gates prove ownership, half-duplex frame handoff, fair per-service action turns with fixed registration limits, lifecycle replacement, immediate emergency rejection, diagnostics coherence, bounded semantic tracing, codec corruption handling, and allocation-free steady-state paths. They also exercise real file storage in isolated temporary directories, including restart reconciliation, pruning, collision, stale-temporary cleanup, and ordinal exhaustion. Production path policy and composition have direct portable tests; installed-game behavior remains the separate UAT gate.

Run the headless E2E and deterministic performance scopes independently with:

```powershell
dotnet test tests/OrbModding.Tests/OrbModding.Tests.csproj -p:UseGameStubs=true --filter "Category=HeadlessIntegration"
dotnet test tests/OrbModding.Tests/OrbModding.Tests.csproj -p:UseGameStubs=true --filter "Category=HeadlessE2E"
dotnet test tests/OrbModding.Tests/OrbModding.Tests.csproj -p:UseGameStubs=true --filter "Category=PerformanceSimulation"
```

### Focused and Windows test lanes

Normal POSIX development uses `./script/test`. Focused raw `dotnet test --filter`
commands are diagnostic aids. On Windows, the portable lane runner owns stable
filter expressions and writes a TRX result under `artifacts/test-results/`:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/test-portable.ps1 -Lane Fast
powershell -NoProfile -ExecutionPolicy Bypass -File tools/test-portable.ps1 -Lane Reliability
powershell -NoProfile -ExecutionPolicy Bypass -File tools/test-portable.ps1 -Lane AutoBuyReliability
powershell -NoProfile -ExecutionPolicy Bypass -File tools/test-portable.ps1 -Lane AutoConceptReliability
powershell -NoProfile -ExecutionPolicy Bypass -File tools/test-portable.ps1 -Lane All
```

`Fast` runs every portable test except deterministic performance simulations
and tests that launch an external process. CI runs those two exclusions as
separate `PerformanceAll` and `ExternalProcess` partitions, so the union still
executes the complete portable suite. `AutoBuyReliability` selects the focused
native multi-buy safety contracts. `AutoConceptReliability` selects the receipt,
settlement, publication-deferral, depth, and timed-rotation journeys.

Run the versioned configuration-schema scope independently with:

```powershell
dotnet test tests/OrbModding.Tests/OrbModding.Tests.csproj -p:UseGameStubs=true --filter "FullyQualifiedName~ConfigurationSchema"
```

This scope owns schema plan validation; missing/current/malformed/negative/future markers; ordered migration and typed-bind sequencing; Automata's exact mode, interval precedence, invariant conversion, clamping, and obsolete-key diagnostics; Mentor's schema-4 retirement of operations-per-frame and CPU-budget keys; marker-only Mod Config adoption; first-free non-overwriting backup suffixes and race collisions; all-or-nothing partial-write/flush cleanup with byte/length/hash verification; exact-byte and new-file rollback after bind/save plus first and repeated reload faults; `SaveOnConfigSet` restoration; subscriber fault isolation; sanitized failure reasons; atomic worker-thread-to-Unity-tick dirty handoff; exact-GUID status-only catalog entries; and schema projection. A path or serialized value appearing in published status is a test failure.

### ServiceCycle performance evidence

Every automation feature is a ServiceCycle service. Portable simulations prove
bounded action turns, worker handoff, allocation contracts, and local queue
draining deterministically. Debug profile builds record pump and feature-stage
durations; full traces and the dashboard join those timings to exact service,
cycle, projection, and action evidence.

There is no separate coordinator profile or evidence checker. Compare equivalent
trace windows from the exact tested build when changing capture, evaluation, or
action-drain behavior. Desktop and Steam Deck/Proton timings remain UAT evidence:
retain both captures with the suite/game versions and scenario before closing a
runtime performance gate.

Collect portable production coverage with the checked-in assembly allowlist:

```powershell
dotnet test tests/OrbModding.Tests/OrbModding.Tests.csproj -p:UseGameStubs=true --collect:"XPlat Code Coverage" --settings tests/coverage.runsettings
```

Do not rely on the collector's implicit module selection. It can omit production
code when it is loaded through the source-only game-stub graph. The runsettings
file explicitly includes the one production assembly, `[OrbModSuite]*`, and so
excludes the test and game-stub assemblies from the product baseline. It also
drops compiler-generated, generated-code, and explicitly excluded members by
attribute.

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

A test-only PR may merge after its portable suites, the coverage floor, repository
hygiene, installed contracts, and real-reference builds pass. It does not need
new active-save UAT when it changes no packaged runtime behavior. A runtime PR
must also pass the proportional UAT gate in
[runtime validation](runtime-validation.md); a release candidate must complete
the full V0-V7 sequence from its exact archive.

### Coverage policy

The suite ships as one assembly, so there is one coverage package and one floor:
**76.5% line coverage**, enforced by `tools/check-coverage.ps1`. CI runs the
Release collection, excluding only the `ExternalProcess` category, and hands the
resulting Cobertura report to that script.

The script checks the same floor twice, because with a single package the two
numbers are the same measurement: once against the report's overall line rate,
and once against the `OrbModSuite` package's own rate. A missing `OrbModSuite`
package is itself a failure, so a run that collected nothing cannot pass by
reporting nothing. Any failure throws and fails the job.

A second floor is impossible to state here rather than merely absent: one
assembly means one package, so a per-assembly number could only ever disagree
with itself. The floor is set two points under the rate measured when the suite
merged into one DLL.

Branch coverage is printed — overall and for the package — as diagnostic evidence
only. No branch floor is enforced.

This is a regression floor, not a completion target. New or materially changed
core engines and controllers should aim for at least 80% line and 70% branch
coverage; reflection/native adapters should aim for at least 70% line and 60%
branch coverage. Do not add low-value tests solely to cover Unity view code,
plugin bootstrap, or defensive logging. Those paths require focused component
seams, installed contracts, or UAT evidence instead.

Raising the floor requires a passing Release coverage run on the current tree.
Lowering it requires an explicitly documented rationale in the PR.

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
  states, including refusal of a plan collected under a superseded lifecycle;
- Mentor spell, artifact, and alchemy capture-to-native-grant journeys,
- atomic action-family claims, exact known conflicts, final mutation gates, lifecycle/configuration release, and independent Mentor-domain cancellation,
  recursion prevention, domain isolation, registry identity, and lifecycle
  cancellation;
- Mod Config staged dependencies, atomic apply/rollback, external refresh,
  UI-work scheduling, listener cleanup, and shell repair policy;
- shared versioned configuration transactions over the suite's one configuration
  file, ordered migration steps against fixture plans, marker-only adoption of an
  unversioned file, backup collision handling,
  verified all-or-nothing backup creation, exact rollback, reload-failure
  containment, future-version refusal, reason privacy, atomic UI handoff, and
  exact-GUID editable or status-only schema projection;
- shared scheduler fairness, mutation admission, failure containment, and
  deterministic performance budgets;
- exact per-registration performance observations for same-subsystem work,
  same-frame versus distinct-frame deferrals, fixed denial-reason counters,
  consecutive deferred-frame runs, pending-wait reset and starvation episodes,
  sparse and backward Unity frame identities, wait closure on admission/disable/
  disposal, exact overrun attribution,
  admitted no-ops, multiple native outcomes per lease, attempted versus
  postcondition-verified mutations, and disable/re-enable/disposal lifecycle;
- production reflection outcomes proving preflight rejection performs zero
  native calls, verifier execution/postcondition failures are attempted but
  uncommitted, unverified charge hold never claims a commit, exception cleanup
  retains observed outcomes, and rejected actions report zero committed mutations;
- reusable lifecycle state-machine journeys spanning no-save, load, registry
  readiness, unlock, action, completion, reset, stale callbacks, disable and
  re-enable, same-name scene recreation, and mixed Automata/Mentor isolation.
P2 remains runtime-focused: actual Unity layout and navigation, real save/cloud
behavior, a combined-suite soak, real frame-time profiling, and Steam Deck or
Proton validation. Keep these as UAT; reduce any reproducible defect to a
headless regression afterward.

### Time-bounded validation profiles

Routine PR validation is fully automated: portable tests, the tagged headless
and performance jobs, the coverage floor, repository hygiene, installed contracts
when a game installation is available, and real-reference builds.

When a full UAT pass is not affordable, use a 25-35 minute smoke on a disposable save:

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

The same project always runs portable validation of [`data/native-contracts.json`](../../data/native-contracts.json) and the bounded native-source audit. CI therefore fails when a supported gameplay reflection or Harmony source introduces an undeclared literal target, even though GitHub runners do not have the proprietary game assemblies. Framework-only and UI-plumbing reflection is exempted by exact source path with a reason in the manifest. See the [native contract manifest workflow](native-contracts.md).

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
BepInEx/plugins/OrbModSuite/OrbModSuite.dll
README.md
CHANGELOG.md
LICENSE
THIRD_PARTY_NOTICES.md
```

The suite ships as one assembly. `tools/package-supported-suite.sh` asserts this exact entry list, so it
is the authority if this section drifts from it.

Do not include experimental DLLs, game assemblies, BepInEx assemblies, Harmony, debug symbols unless intentionally published, or local configuration files containing user preferences.

## Versioning and game updates

- Record the tested game build and Unity version in every release.
- Fail softly when expected types or methods are missing.
- Keep Harmony targets small and signature-specific.
- Re-run the timing and save test matrices after every game update.
- Prefer direct public game APIs over transpilers.
