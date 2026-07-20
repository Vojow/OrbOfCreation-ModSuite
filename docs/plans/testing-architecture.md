# Test strategy and architecture

**Lifecycle:** Initial lanes and deterministic reliability implemented / comparative performance pending

[Back to plans](README.md) · [Testing hub](../testing/README.md) · [Repository strategy](../testing/strategy.md) · [Headless E2E](../testing/headless-e2e.md) · [Runtime validation](../testing/runtime-validation.md)

## Purpose

The suite keeps its existing risk-based pyramid, but exposes orthogonal test
lanes so developers can answer one question without running every kind of
evidence. Layers describe how close a test is to the game; lanes describe the
risk being investigated.

The architecture must preserve these boundaries:

- portable tests never claim installed-game compatibility;
- deterministic simulations gate operation counts and modeled time, not host
  wall-clock timing;
- installed contracts never claim Unity runtime behavior;
- active-save mutation remains behind the ordered runtime UAT gates;
- faster local selection never removes a test from the complete CI partition.

## Layer and lane model

| Layer | Authority | Typical evidence |
|---|---|---|
| Unit/component | Pure or focused production seams | decisions, arithmetic, state transitions, failure containment |
| Headless integration | Production adapters plus small game-shaped stubs | reflection signatures, queue and mutation boundaries |
| Headless E2E | Production engines plus deterministic simulated game state | queue/economy/lifecycle journeys |
| Installed contract | Audited installed assemblies | exact native metadata and hashes |
| Runtime UAT | Unity and a disposable save | Harmony wiring, native side effects, saves, controls, responsiveness |

| Lane | Question | Merge role |
|---|---|---|
| `Fast` | Did ordinary portable behavior regress? | every local edit and portable CI |
| `Reliability` | Do cross-suite lifecycle, replay, and failure-containment contracts remain safe? | relevant PRs |
| `AutoBuyDecision` | Did candidate, ordering, reserve, grouping, or continuation policy change? | every Auto Buy policy edit |
| `AutoBuyReliability` | Does Auto Buy remain safe through faults, live state changes, and lifecycle replacement? | every Auto Buy PR |
| `AutoBuyPerformance` | Did a progression-shaped workload require more deterministic work or simulated frames? | Auto Buy scheduling/performance PRs |
| `PerformanceAll` | Did any checked deterministic suite performance target regress? | CI |
| `ExternalProcess` | Do repository generators and subprocess contracts reject invalid inputs? | CI and repository/tooling changes |
| `InstalledContract` | Do audited native contracts still match an installed game? | game computer and release |

Tests may belong to more than one positive lane. `Fast` is deliberately a
partition filter: it contains everything except `PerformanceSimulation` and
`ExternalProcess`. CI runs those excluded categories separately, so the three
portable partitions still cover the complete suite. `PerformanceSimulation`
and `ExternalProcess` are mutually exclusive partition markers; a test must not
carry both.

Use
`powershell -NoProfile -ExecutionPolicy Bypass -File tools/test-portable.ps1 -Lane <name>`
rather than copying VSTest filter expressions into local workflows. Every lane
writes a TRX result below `artifacts/test-results/` for failure diagnosis.

## Auto Buy evidence architecture

Auto Buy changes use three complementary contracts:

1. **Decision** owns exact semantic behavior and policy invariants. Tests that
   intentionally preserve old behavior say `Current` in their name so the
   redesign can replace them deliberately rather than silently weakening them.
2. **Reliability** owns safety under live queue, resource, availability,
   lifecycle, and mutation-failure changes. It asserts no overfill, no reserve
   breach, no stale reference reuse, no ambiguous retry, and eventual progress
   for healthy siblings.
3. **Performance** owns deterministic operation density, queue continuity,
   fairness, and frames to a fixed workload. Wall-clock duration is diagnostic
   because host scheduling and process startup are not controlled game inputs.

Synthetic early, mid, late, and endgame stress workloads are checked against
reviewed JSON history before production policy changes. They are deliberately
separate from evidence-backed progression profiles. The same workload
definition must be used on both sides of an A/B comparison. A workload/schema
mismatch is a test infrastructure failure, not a performance result.

## Failure evidence

Portable and coverage CI retain TRX output even when a test step fails.
Deterministic performance CI additionally retains its JSON reports and checked
evaluation. Seeded/state-machine simulations report the seed, first failing
prefix, and a bounded replay-compatible/synthetic event tail. The per-event
assertion also names the triggering action, so the exact deterministic tape can
be replayed without retaining unbounded output.

Do not add automatic retries for deterministic tests. A flaky deterministic
test is a defect in the test, isolation, or production behavior and must remain
visible.

## Coverage policy

Line floors remain enforced regression gates. Branch rates are printed for the
overall supported production set and each production assembly, but are initially
diagnostic. Raise branch coverage in focused engine and adapter seams, record a
reviewed baseline from a current Release run, and only then introduce floors.
Do not create low-value tests solely to increase a percentage.

## Implementation phases

### T0 — Selectable lanes and retained evidence

- Centralize maintained test strategy and module ownership under `docs/testing/`,
  splitting larger modules by feature. **Implemented.**
- Add the portable lane runner and stable category vocabulary. **Implemented.**
- Separate fast, deterministic-performance, and external-process CI partitions
  without dropping complete-suite coverage. **Implemented.**
- Retain portable, performance, contract-audit, and coverage result artifacts.
  **Implemented.**
- Establish an initial selectable reliability corpus and Auto Buy subset from
  existing dirty-resource, multi-buy, replay, and cross-boundary simulations.
  **Implemented.**

### T1 — Coverage quality and test navigation

- Record an initial overall and per-assembly diagnostic branch snapshot.
  **Implemented; rerun before selecting floors.**
- Add branch regression floors where they measure decision and failure paths.
- Split the largest test fixtures by responsibility while retaining shared,
  bounded scenario builders.
- Assign remaining portable classes to stable risk/feature categories.

### T2 — Reliability model expansion

- Add deterministic seeded sequences over queue, affordability, availability,
  completion, configuration, and lifecycle events. **Implemented for Auto Buy.**
- Assert safety invariants after every event, not only at scenario completion.
  **Implemented for Auto Buy.**
- Shrink or serialize a failing sequence into a sanitized runtime replay fixture.
  **First-failing-prefix reduction and bounded event output implemented; fixture
  promotion remains a reviewed manual step because synthetic fault controls are
  not valid runtime events.**
- Grow the reviewed replay corpus from every ordering-sensitive runtime defect.

### T3 — Comparative performance

- Keep runtime-derived scheduler perturbations beside the stable progression
  workloads, with deterministic safety/progress/operation gates. **Implemented
  for Auto Buy low-Bulk, mixed-outage, completion-storm, threshold, heavy-tail,
  and catalog-ramp cases.**
- Run the four progression workloads through an identical harness on the
  pre-change and candidate engines.
- Add allocation evidence in a controlled benchmark process; keep it separate
  from Unity-native elapsed timing.
- Strengthen endgame queue-continuity targets after the new group/continuation
  design establishes a reviewed achievable baseline.
- Record desktop and Steam Deck/Proton runtime profiles through UAT.

## Exit criteria

- A developer can run decision, reliability, or performance evidence directly.
- CI's portable partitions collectively execute every portable test exactly
  once outside the separate instrumented coverage run.
- Deterministic reports reject workload drift and explain operation regressions.
- Reliability failures are reproducible from bounded, sanitized evidence.
- Installed-contract and runtime UAT claims remain separate and explicit.
