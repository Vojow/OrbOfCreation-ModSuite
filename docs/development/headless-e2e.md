# Headless end-to-end simulation

[Back to compatibility and testing](testing.md) · [Runtime UAT protocol](runtime-validation.md) · [Performance architecture](../plans/performance-suite.md)

See also [sanitized runtime replay fixtures](runtime-replay.md) for the strict,
versioned event format used to reproduce selected ordering-sensitive journeys.

## Purpose

Headless E2E tests run the real mod engine, scheduler, candidate index, reserve policy, and queue-planning behavior against a deterministic simulation of the native game boundary. They require neither Unity nor computer control and are suitable for local development and CI.

The simulation is intentionally smaller than Orb of Creation. It models only contracts the mod consumes:

- shared native action-queue capacity, admission, completion, and manual occupancy;
- Structures and Upgrades with stable UUID/type identity and replaceable native object identity;
- authoritative availability, costs, resources, finite levels, and queued levels;
- save/load-style lifecycle invalidation;
- native rejection and ambiguous post-mutation failure;
- deterministic CPU-work observations and operation counters.

Native-shaped completion scenarios distinguish one native `CompleteAction`
invocation from the number of queue slots it releases. A Structure completion
may settle several Bulk Development levels and enqueue echo work while producing
one completion callback. The callback is modeled before the outer action queue
entry is removed; the engine still acts later from authoritative live queue room.

Resource scenarios may use several independent resources and normalized
`BigAmount` values. The simulated world remains authoritative for every balance
and applies a mutation only when all resource costs and queue constraints pass.

Production code remains responsible for all scheduling and purchase decisions. The simulation does not copy `AutoBuyEngine` logic or predict an independent economy.

Focused native-shaped journeys complement the queue simulation. The game-stub
assembly deliberately identifies itself as `Assembly-CSharp`, so production
assembly-qualified lookups are exercised rather than bypassed. These fixtures
currently cover:

- Mentor spell, artifact, and alchemy registry reconciliation, capture, native
  XP grants, recursion prevention, domain isolation, and lifecycle cancellation;
- Auto Buy registry reconciliation, recreated native identity, resource
  snapshots, native cost decoding, lifecycle evidence, and verified one-level
  queue mutations;
- automatic spell leveling before and after the native level-all upgrade, plus
  cancellation of a pending mutation during lifecycle invalidation.

### Reusable lifecycle scenarios

`tests/OrbModding.Tests/Scenarios/` provides a deterministic, test-only state
machine for suite journeys that cross several native events. Its
`LifecycleScenarioKernel` owns a simulated frame and elapsed-time clock while
driving the production `GameLifecycleMonitor`, `GameplayInvalidationBus`, and
`SuitePerformanceCoordinator`. Lifecycle-bound delayed callbacks capture the
generation at scheduling time and are recorded, but not executed, after that
generation becomes stale. An explicit unfiltered-delivery mode models a late
native observer so the production invalidation bus, rather than the harness,
must reject its old-generation event.

Production-facing feature drivers keep the scenario layer honest:

- `ScenarioAutoBuyFeature` runs the real `AutoBuyEngine` against the existing
  authoritative `SimulatedAutoBuyWorld` and `SimulatedAutoBuyCatalog`;
- `ScenarioMentorFeature` runs the real `MentorEngine` and
  `MentorCoordinatorWork` mutation path;
- `ScenarioOracles` checks lifecycle order, ignored stale callbacks, unique
  request execution, and the shared one-native-mutation-per-frame invariant.

The focused scenarios cover no-save through load/readiness, progression unlock,
queue submission and completion, reset, old-generation callback rejection,
prepared-work cancellation, disable/re-enable, and recreation of a `Main` scene
with a different native identity. The mixed Automata/Mentor journey also proves
that disabling or locking one feature does not stop an unrelated supported
feature. These fixtures never launch Unity or use computer control.

The fixture members are reduced from installed-game documentation and inspected
assembly contracts. They remain smaller than the game and do not replace the
installed-game contract suite or UAT.

### Structured runtime replay

`tests/OrbModding.RuntimeReplay/` defines a dependency-free V1 model and strict
canonical JSON codec for lifecycle, resource, queue, progression, inventory,
configuration, and completion observations. The scenario dispatcher reuses
`LifecycleScenarioKernel` and `ScenarioAutoBuyFeature`; it does not duplicate
their lifecycle clock, scheduler, invalidation bus, or engine simulation.

The two checked-in replay fixtures cover completion-driven queue refill and
chained progression invalidation. Repeated runs assert identical frames,
integer-microsecond timestamps, lifecycle generations, mutation requests, and
queue outcomes. Old-generation invalidation is rejected by the production bus.
The separate converter accepts only a reviewed typed setup plus sanitized JSONL
events and publishes atomically after complete validation. See the
[schema and conversion workflow](runtime-replay.md).

## Test layers and ownership

| Layer | Runs | Proves | Does not prove |
|---|---|---|---|
| Unit/component | Portable test doubles and fixtures | Individual policies, reflection ambiguity handling, lifecycle transitions, and scheduler rules | A complete automation session |
| Headless integration | Production native adapters against focused game API stubs | Assembly-qualified discovery and adapter translation, including Mentor, resources, spell leveling, and shared queue versus native Auto Buy queue | Installed assembly compatibility or Unity behavior |
| Headless E2E | Real mod engine through a simulated native boundary | Queue filling, candidate handoff, resource depletion, lifecycle recovery, failure containment, and deterministic performance budgets | Unity wiring, installed assembly compatibility, visual behavior, or the real save format |
| Installed-game contracts | PE metadata from the installed game | Audited type/member signatures and assembly hashes | Runtime behavior inside Unity |
| UAT | Real game, disposable saves, observation, and optional computer control | Harmony/reflection wiring, visible queue behavior, save/load, UI, player control, and subjective responsiveness | Broad deterministic regression coverage |

Computer control belongs only to UAT. No automated E2E or performance-simulation gate may depend on it.

## Commands

Run the complete portable suite:

```powershell
dotnet test tests/OrbModding.Tests/OrbModding.Tests.csproj -p:UseGameStubs=true
```

Run only headless behavioral journeys:

```powershell
dotnet test tests/OrbModding.Tests/OrbModding.Tests.csproj -p:UseGameStubs=true --filter "Category=HeadlessIntegration"
dotnet test tests/OrbModding.Tests/OrbModding.Tests.csproj -p:UseGameStubs=true --filter "Category=HeadlessE2E"
```

Run only the reusable lifecycle state-machine scenarios while iterating on the
kernel or a feature driver:

```powershell
dotnet test tests/OrbModding.Tests/OrbModding.Tests.csproj -p:UseGameStubs=true --filter "FullyQualifiedName~LifecycleStateMachineScenarioTests"
```

Run the structured runtime replay fixtures and codec/converter contracts:

```powershell
dotnet test tests/OrbModding.Tests/OrbModding.Tests.csproj -p:UseGameStubs=true --filter "FullyQualifiedName~RuntimeReplayTests"
```

Run the active deterministic performance baseline:

```powershell
dotnet test tests/OrbModding.Tests/OrbModding.Tests.csproj -p:UseGameStubs=true --filter "Category=PerformanceSimulation"
```

Capture a machine-readable report and compare it with the checked-in beta
history:

```powershell
$env:OOC_PERFORMANCE_REPORT = Join-Path $PWD 'artifacts/performance/autobuy-current.json'
dotnet test tests/OrbModding.Tests/OrbModding.Tests.csproj --configuration Release -p:UseGameStubs=true --filter "Category=PerformanceSimulation"
tools/check-performance-report.ps1 -ReportPath artifacts/performance/autobuy-current.json
```

The environment variable is optional. Without it, the tests enforce their hard
budgets but do not create an artifact. Use an absolute path when setting it
because the test host runs from its build-output directory.

Performance targets remain skipped only while they document engineering backlog rather than released behavior. Promote a target to `PerformanceSimulation` only when the production engine meets its assertions without weakening the workload or budgets.

## Determinism and performance budgets

Performance simulations assert deterministic work rather than wall-clock duration. Useful metrics include:

- candidate evaluations and maximum evaluations in one simulated frame;
- queue high-water mark and depth after saturation;
- frames with usable queue room but no purchase despite affordable work;
- frames required to reach 90% of usable queue capacity;
- purchase count and distinct-candidate handoff order.

The harness injects observation costs into production CPU-slicing seams. This makes the same run reproducible on a desktop, Steam Deck, and CI runner. Real elapsed time may still be reported diagnostically, but it must not be the sole pass/fail criterion.

### Historical reports

[`data/autobuy-performance-baseline.json`](../../data/autobuy-performance-baseline.json)
is the reviewed beta reference. Each report records the exact workload, source
commit, queue output, refill latency, candidate and catalog reads, mutation
attempts, scheduler callbacks, and normalized evaluations and total observed
operations per successful submission. The operation total is diagnostic: it is
the sum of modeled native reads, native mutation attempts, and scheduler
callbacks. It is not a claim about instructions, allocations, or real elapsed
CPU time.

`tools/check-performance-report.ps1` rejects a changed workload or scenario set
and allows at most a 10% regression by default. Queue depth, submissions, and
candidate diversity are higher-is-better; reads, callbacks, refill latency,
idle frames, and normalized operation density are lower-is-better. Existing
scenario assertions remain the stricter correctness and safety gates.

CI prints the comparison table in the job summary and retains the raw current
report for 90 days. The checked-in reference supplies the long-lived anchor;
retained artifacts provide per-run evidence for investigations. Update the
reference only in the same reviewed change that intentionally changes the
workload or improves/accepts the engine result. Run the full portable suite
first, record the previous and new values in the PR, and never refresh the file
merely to make a regression check pass.

### Main versus beta compatibility run

Run the same simulation against the last pre-beta `main` engine and the current
working tree with:

```powershell
tools/compare-autobuy-performance.ps1
```

The script checks out commit `7f61f21` into a disposable temporary worktree,
compiles a legacy catalog adapter against that unmodified production source,
then compiles the current adapter against the current production source. Both
execute the same 166-candidate, 304-slot, 900-frame periodic-completion and
completion-storm workloads. It writes `main-reference.json`,
`beta-current.json`, and `comparison.md` under
`artifacts/performance/ab` and removes only the temporary worktree it created.

The compatibility adapter accounts only for intentional API differences: the
change from remaining queue room to the full queue-capacity snapshot, candidate
evaluation evidence, and completion settlement/revalidation. It does not
backport beta engine behavior into the reference build. Use
`-ReferenceRef <commit>` to compare another legacy-compatible commit.

Output and responsiveness metrics are interpreted directly: submissions,
queue depth, distinct candidates, saturation time, and idle purchasable frames.
Raw reads and modeled operations remain diagnostic in this cross-version view.
An older engine can appear to perform less work simply because it does not
revalidate or serve as many candidates, so operation density is a regression
gate only between reports with equivalent engine semantics.

## Scenario design rules

- Exercise the production engine through public or existing internal seams; do not duplicate its decision algorithm in the simulator.
- Keep the simulated native world authoritative for availability, resource quantity, cost, queue room, and mutation acceptance.
- Recreate native object identities on lifecycle reload while retaining stable UUID and expected type.
- Include manual queue entries when testing shared-capacity behavior.
- Model unexpected native results and verify that the engine fails closed.
- Use several small focused journeys plus bounded stress scenarios. Avoid one enormous test that makes failures hard to diagnose.
- Reduce every UAT-only defect into a deterministic headless regression when the relevant contract can be represented safely.
- Schedule native-shaped delayed callbacks through the lifecycle kernel so an
  old generation is rejected explicitly rather than relying on timing luck.
- Give every simulated mutation request a stable request identity and run the
  mutation uniqueness oracle in mixed-feature journeys.
- Keep replay observations inside the strict versioned schema. Do not add opaque
  payload dictionaries, private save fields, or free-text log ingestion.

## UAT handoff

Headless E2E passing is required before real-game UAT, but it does not replace UAT. Use the [runtime validation protocol](runtime-validation.md) for the installed DLL, disposable-save, queue, UI, rollback, and player-control gates. Computer control may accelerate those observations, but the result remains UAT evidence and should record the game build, mod build, save, settings, and visible outcome.
