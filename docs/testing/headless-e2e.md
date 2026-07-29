# Headless end-to-end simulation

[Back to testing hub](README.md) · [Repository strategy](strategy.md) · [Runtime UAT protocol](runtime-validation.md)

## Purpose

Headless E2E tests run supported production feature boundaries against
deterministic simulations of native game contracts. They require neither Unity
nor computer control and are suitable for local development and CI.

Focused native-shaped journeys complement the queue simulation. The game-stub
assembly deliberately identifies itself as `Assembly-CSharp`, so production
assembly-qualified lookups are exercised rather than bypassed. These fixtures
currently cover:

- Mentor spell, artifact, and alchemy world-based evaluation, exact-XP input,
  native grants, recursion prevention, and lifecycle cancellation;
- Auto Buy ServiceCycle frame projection, worker policy, final native revalidation,
  queue-room enforcement, and verified Structure/Upgrade mutations;
- automatic spell leveling before and after the native level-all upgrade, its
  boundary refusals for a locked or unaffordable level, and its refusal of a plan
  collected under a superseded lifecycle.

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

Run the active deterministic performance baseline:

```powershell
dotnet test tests/OrbModding.Tests/OrbModding.Tests.csproj -p:UseGameStubs=true --filter "Category=PerformanceSimulation"
```

Run the focused native multi-buy safety contracts while iterating:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/test-portable.ps1 -Lane AutoBuyReliability
```

## Determinism and performance budgets

Portable performance tests assert deterministic work and bounded allocations.
Auto Buy runtime timing is measured through the ServiceCycle profile build and
sanitized in-game traces. Compare like-for-like trace windows when changing
capture or action-drain behavior; portable operation counts are not a
substitute for Unity main-thread timings.

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
- Keep trace observations inside the strict versioned schema. Do not add opaque
  payload dictionaries, private save fields, or free-text log ingestion.

## UAT handoff

Headless E2E passing is required before real-game UAT, but it does not replace UAT. Use the [runtime validation protocol](runtime-validation.md) for the installed DLL, disposable-save, queue, UI, rollback, and player-control gates. Computer control may accelerate those observations, but the result remains UAT evidence and should record the game build, mod build, save, settings, and visible outcome.
