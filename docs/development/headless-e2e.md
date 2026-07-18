# Headless end-to-end simulation

[Back to compatibility and testing](testing.md) · [Runtime UAT protocol](runtime-validation.md) · [Performance architecture](../plans/performance-suite.md)

## Purpose

Headless E2E tests run the real mod engine, scheduler, candidate index, reserve policy, and queue-planning behavior against a deterministic simulation of the native game boundary. They require neither Unity nor computer control and are suitable for local development and CI.

The simulation is intentionally smaller than Orb of Creation. It models only contracts the mod consumes:

- shared native action-queue capacity, admission, completion, and manual occupancy;
- Structures and Upgrades with stable UUID/type identity and replaceable native object identity;
- authoritative availability, costs, resources, finite levels, and queued levels;
- save/load-style lifecycle invalidation;
- native rejection and ambiguous post-mutation failure;
- deterministic CPU-work observations and operation counters.

Production code remains responsible for all scheduling and purchase decisions. The simulation does not copy `AutoBuyEngine` logic or predict an independent economy.

## Test layers and ownership

| Layer | Runs | Proves | Does not prove |
|---|---|---|---|
| Unit/component | Portable test doubles and fixtures | Individual policies, reflection ambiguity handling, lifecycle transitions, and scheduler rules | A complete automation session |
| Headless integration | Production native adapters against focused game API stubs | Adapter selection and translation, including shared queue versus native Auto Buy queue | Installed assembly compatibility or Unity behavior |
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

Inspect performance targets that are recorded but not yet release gates:

```powershell
dotnet test tests/OrbModding.Tests/OrbModding.Tests.csproj -p:UseGameStubs=true --filter "Category=PerformanceTarget"
```

A skipped target is a documented engineering backlog item, not passing evidence. Remove `Skip` only when the production engine meets the assertions without weakening their workload or budgets.

## Determinism and performance budgets

Performance simulations assert deterministic work rather than wall-clock duration. Useful metrics include:

- candidate evaluations and maximum evaluations in one simulated frame;
- queue high-water mark and depth after saturation;
- frames with usable queue room but no purchase despite affordable work;
- frames required to reach 90% of usable queue capacity;
- purchase count and distinct-candidate handoff order.

The harness injects observation costs into production CPU-slicing seams. This makes the same run reproducible on a desktop, Steam Deck, and CI runner. Real elapsed time may still be reported diagnostically, but it must not be the sole pass/fail criterion.

## Scenario design rules

- Exercise the production engine through public or existing internal seams; do not duplicate its decision algorithm in the simulator.
- Keep the simulated native world authoritative for availability, resource quantity, cost, queue room, and mutation acceptance.
- Recreate native object identities on lifecycle reload while retaining stable UUID and expected type.
- Include manual queue entries when testing shared-capacity behavior.
- Model unexpected native results and verify that the engine fails closed.
- Use several small focused journeys plus bounded stress scenarios. Avoid one enormous test that makes failures hard to diagnose.
- Reduce every UAT-only defect into a deterministic headless regression when the relevant contract can be represented safely.

## UAT handoff

Headless E2E passing is required before real-game UAT, but it does not replace UAT. Use the [runtime validation protocol](runtime-validation.md) for the installed DLL, disposable-save, queue, UI, rollback, and player-control gates. Computer control may accelerate those observations, but the result remains UAT evidence and should record the game build, mod build, save, settings, and visible outcome.
