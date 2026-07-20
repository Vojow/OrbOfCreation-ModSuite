# Repository test strategy

[Back to testing hub](README.md) · [Test architecture plan](../plans/testing-architecture.md) · [Headless E2E simulation](headless-e2e.md) · [Runtime UAT protocol](runtime-validation.md)

Ordering-sensitive headless regressions may also use the
[sanitized runtime replay format](runtime-replay.md).

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

The portable tests use a source-only `OrbModding.GameStubs` project to compile the supported plugin seams. They validate Automata, Mentor, Mod Config, shared scheduling/status/ownership controls, policy, lifecycle behavior, safe defaults, timing, reflection fixtures, UUID uniqueness, entity type counts, and known mappings. Experimental Chronomancer and Resonance tests are not present on this branch. Portable tests do not claim game API compatibility; production builds ignore the stubs and require `OOC_GAME_DIR`.

Portable automation has three scopes:

- unit/component tests isolate policies, reflection fixtures, schedulers, and lifecycle transitions;
- headless integration tests connect production native adapters to focused game API stubs;
- headless E2E runs the real mod engine against a deterministic simulated game boundary for complete queue, economy, lifecycle, and failure journeys.

Run the headless E2E and deterministic performance scopes independently with:

```powershell
dotnet test tests/OrbModding.Tests/OrbModding.Tests.csproj -p:UseGameStubs=true --filter "Category=HeadlessIntegration"
dotnet test tests/OrbModding.Tests/OrbModding.Tests.csproj -p:UseGameStubs=true --filter "Category=HeadlessE2E"
dotnet test tests/OrbModding.Tests/OrbModding.Tests.csproj -p:UseGameStubs=true --filter "Category=PerformanceSimulation"
dotnet test tests/OrbModding.Tests/OrbModding.Tests.csproj -p:UseGameStubs=true --filter "FullyQualifiedName~RuntimeReplayTests"
```

### Selectable test lanes

Use the portable lane runner for normal development. It owns the stable filter
expressions and writes a TRX result under `artifacts/test-results/`:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/test-portable.ps1 -Lane Fast
powershell -NoProfile -ExecutionPolicy Bypass -File tools/test-portable.ps1 -Lane Reliability
powershell -NoProfile -ExecutionPolicy Bypass -File tools/test-portable.ps1 -Lane AutoBuyDecision
powershell -NoProfile -ExecutionPolicy Bypass -File tools/test-portable.ps1 -Lane AutoBuyReliability
powershell -NoProfile -ExecutionPolicy Bypass -File tools/test-portable.ps1 -Lane AutoBuyPerformance
powershell -NoProfile -ExecutionPolicy Bypass -File tools/test-portable.ps1 -Lane All
```

`Fast` runs every portable test except deterministic performance simulations
and tests that launch an external process. CI runs those two exclusions as
separate `PerformanceAll` and `ExternalProcess` partitions, so the union still
executes the complete portable suite. `AutoBuyDecision`,
`AutoBuyReliability`, and `AutoBuyPerformance` answer independent policy,
safety, and deterministic-work questions; a test may carry multiple positive
categories when it supplies more than one kind of evidence.

The complete layer/lane ownership model and its implementation phases live in
the [test strategy and architecture plan](../plans/testing-architecture.md).

Run the versioned configuration-schema scope independently with:

```powershell
dotnet test tests/OrbModding.Tests/OrbModding.Tests.csproj -p:UseGameStubs=true --filter "FullyQualifiedName~ConfigurationSchema"
```

This scope owns schema plan validation; missing/current/malformed/negative/future markers; ordered migration and typed-bind sequencing; Automata's exact mode, interval precedence, invariant conversion, clamping, and obsolete-key diagnostics; marker-only Mentor and Mod Config adoption; first-free non-overwriting backup suffixes and race collisions; all-or-nothing partial-write/flush cleanup with byte/length/hash verification; exact-byte and new-file rollback after bind/save plus first and repeated reload faults; `SaveOnConfigSet` restoration; subscriber fault isolation; sanitized failure reasons; atomic worker-thread-to-Unity-tick dirty handoff; exact-GUID status-only catalog entries; and schema projection. A path or serialized value appearing in published status is a test failure.

Set `OOC_PERFORMANCE_REPORT` to an absolute JSON path to retain the deterministic
Auto Buy measurements, then run
`tools/check-performance-report.ps1 -ReportPath <path>` to compare them with the
reviewed beta history. CI performs this comparison, adds its table to the job
summary, and retains the raw report for 90 days. See
[headless E2E simulation](headless-e2e.md#historical-reports) for metric
definitions and the baseline-update policy.

### Suite coordinator performance evidence

The Auto Buy history remains a separate deterministic operation-count report.
Suite-wide runtime timing uses the strict profile in
`data/suite-performance-profile-v1.json` and the separate
`OrbModding.PerformanceEvidence` tool. A capture freezes every exact coordinator
work identity at both the start and end of an explicitly requested measurement
window. The checker evaluates counter deltas; it never attributes a lifetime
maximum that was already present at the start to the current capture.

The V1 profile pins the 0.75 ms soft budget, 1.0 ms hard budget, one
mutation-owning feature admission per frame, twelve supported work identities,
exact 10/12/30-frame wait
limits, and a minimum of 30 samples. Cooperative p95, p99, and maximum targets,
combined active-frame timing, wait limits, starvation, abandonment, and
work/measurement failures are enforced merge evidence. Exceeded or insufficient
required results return CLI exit code 3. Invalid JSON, incompatible policy,
profile-hash drift, missing/unknown work, or contradictory facts return exit code
1. Usage errors return exit code 2. Native timing and native hard-budget overruns
remain literal `observe-only` after a complete uncontaminated sample window until
both Windows desktop and Steam Deck under Proton captures exist; an insufficient
or contaminated native window still makes the capture unusable and returns 3.
The coordinator's combined active-frame distribution is evaluated separately:
p95 uses the 0.75 ms soft target, while p99 and maximum use the 1.0 ms hard
target. A per-work pass therefore cannot hide stacked suite work in one frame.
The twelve identity constants are consumed by the production registration
sites as well as a checked-profile audit, so renaming or reclassifying runtime
work without updating the profile fails portable CI. The same audit compares all
twelve compiled starvation thresholds with both JSON wait fields, preventing the
runtime resolver and checked profile from drifting independently.

CI produces a fixed-clock synthetic start/end capture and runs:

```powershell
dotnet run --project tools/OrbModding.PerformanceEvidence/OrbModding.PerformanceEvidence.csproj --configuration Release -- --profile data/suite-performance-profile-v1.json --evidence artifacts/performance/suite-performance-evidence.json --json-output artifacts/performance/suite-performance-evaluation.json --markdown-output artifacts/performance/suite-performance-evaluation.md
```

The retained JSON contains raw coordinator facts only; classifications belong
to the checker. Rolling percentiles can be `within-target` only when the start
window was empty or the capture added at least the complete rolling capacity.
Otherwise the result is `insufficient-window`, preventing pre-capture samples
from producing a false pass. Recurring isolated deferred frames remain a raw
diagnostic; maximum pending wait and consecutive deferred-frame runs are the
bounded wait gates.
Native observe-only metrics follow the same sample/window qualification and do
not emit comparable timing facts from a contaminated rolling window. Capture
also retains registration ids internally across start/end to reject dispose-and-
recreate churn, while deliberately omitting those process-local ids from JSON.
Markdown reports render every non-alphanumeric character from captured metadata
or metric labels as a numeric entity and normalize line breaks, so evidence text
cannot introduce code spans, links, images, raw HTML, or table cells.

Capture metadata is bounded to capture kind, source commit, suite/game version,
scenario, duration, and UTC time. The production DTO has no host, OS user, save,
or path field and performs no file I/O. Capture/export is a low-frequency
diagnostic action, never a scheduler hot-path call. Real captures are still UAT:
run the same scenario on desktop and Deck, retain both evidence files, and record
the exact game/build versions before closing the runtime performance gate.

For a source-level A/B comparison with the last pre-beta `main` engine, run
`tools/compare-autobuy-performance.ps1`. Its compatibility project compiles the
same deterministic workload separately against the untouched reference and
current production sources; it does not copy beta engine changes into the
reference. Pull-request CI additionally compares the exact target SHA with the
head SHA and updates one target/current performance comment. See
[main versus beta compatibility run](headless-e2e.md#main-versus-beta-compatibility-run)
and [pull-request target comparison comment](headless-e2e.md#pull-request-target-comparison-comment).

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

| Scope | Enforced line floor | Current Release line | Diagnostic branch |
|---|---:|---:|---:|
| Overall supported production code | 65% | 72.79% | 62.97% |
| Orb Automata | 70% | 72.79% | 63.84% |
| Orb Mentor | 64% | 72.11% | 58.20% |
| Orb Mod Config | 24% | 28.96% | 31.59% |
| Orb Modding Common | 83% | 88.64% | 76.35% |

The coverage checker also prints overall and per-assembly branch rates as
diagnostic evidence. Branch floors are not enforced until a reviewed Release
baseline exists for the current tree.

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
- atomic action-family claims, exact known conflicts, final mutation gates, lifecycle/configuration release, and independent Mentor-domain cancellation,
  recursion prevention, domain isolation, registry identity, and lifecycle
  cancellation;
- Mod Config staged dependencies, atomic apply/rollback, external refresh,
  UI-work scheduling, listener cleanup, and shell repair policy;
- shared versioned configuration transactions, exact Automata schema-zero
  mappings, marker-only Mentor/Mod Config adoption, backup collision handling,
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
  retains observed outcomes, and legacy per-lease operation totals remain stable;
- reusable lifecycle state-machine journeys spanning no-save, load, registry
  readiness, unlock, action, completion, reset, stale callbacks, disable and
  re-enable, same-name scene recreation, and mixed Automata/Mentor isolation.
- strict V1 structured replays for deterministic frame/time ordering,
  completion-driven queue refill, chained progression invalidation, canonical
  sanitized fixtures, and converter failure containment.

The reusable lifecycle fixtures live under
`tests/OrbModding.Tests/Scenarios/`. They use the production lifecycle monitor,
gameplay invalidation bus, and shared coordinator, then drive real Auto Buy and
Mentor engine/controller seams through portable native-world simulations. Run
them independently with:

```powershell
dotnet test tests/OrbModding.Tests/OrbModding.Tests.csproj -p:UseGameStubs=true --filter "FullyQualifiedName~LifecycleStateMachineScenarioTests"
```

Runtime replay fixtures reuse that same kernel and production Auto Buy feature
driver. Their schema contains no arbitrary payload or private save data; see
[runtime replay fixtures](runtime-replay.md) for the event allowlist and reviewed
conversion workflow.

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
