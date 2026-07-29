# Auto Agromancy migration

Status: **Active — reusable policy/native core adapted; runtime integration pending**

## Goal

Bring Auto Agromancy's native level-balancing behavior onto the current ModSuite
architecture without restoring the retired monolithic Automata service registry.
The feature stays disabled and unreachable until its ServiceCycle integration,
configuration, diagnostics, and installed-game validation are complete.

## Preserved behavior contract

- Search every native level from 1 through `GetMaximumInstances()` and select
  the highest level sustainable against live true resource rates.
- Add the selected pair's observed drain contribution back before evaluating a
  rebalance.
- Include `actionCost` plus a positive element/internal-resource cost.
- Apply the native level drain modifier and `ResourceSO.GetTrueSpend`.
- Accept an exact zero remaining rate.
- Support non-monotonic native costs with a bounded full search.
- Mutate only through native `AddInstance` or `ChangeInstance`, after lifecycle
  and action-family revalidation, and verify exact identity and level.
- Fail closed on missing, invalid, changing, or unaudited native facts.
- Cap an exact search at 4,096 levels and 25 ms.

## Adapted on top of current `main`

- `src/AutoAgromancy/Policy/AutoAgromancyLevelPlanner.cs` is the native-free,
  bounded planner.
- `src/AutoAgromancy/Native/AutoAgromancyNativeAdapter.cs` retains the audited
  reflection contract and verified main-thread mutation boundary.
- Portable planner and native-adapter tests were carried forward under
  `tests/OrbModding.Tests/Services/AutoAgromancy/`.
- The sources keep the existing `OrbAutomata` namespace, matching the other
  feature folders after the suite source split.

The former `AutoAgromancyController`, legacy `IAutomataService` registration,
old `AutomataConfiguration`, and PR-96-era plugin wiring were deliberately not
carried forward. Those types no longer exist on `main`, and reviving them would
create a second scheduler/configuration path.

## Remaining implementation

1. Define the immutable Auto Agromancy configuration inside
   `SuiteRuntimeConfiguration`, bind it through the suite configuration, and add
   the next `SuiteConfigurationSchema` migration. Decide explicitly whether
   ready-plot collection remains the separate Auto Harvest control or becomes
   one Auto Agromancy master; the old branch assumed unification, while current
   `main` exposes Auto Harvest independently.
2. Add a typed `IAutomataServiceCycleFeature` with native-free state, worker
   evaluation, bounded action records, owner-thread capture, and owner-thread
   execution. Do not run the old controller from `Plugin.Update`.
3. Replace the old controller's native-object-bearing
   `AutoAgromancyActiveLevel.Selected` handoff with stable UUID plus expected
   native type across the ServiceCycle boundary. Resolve and revalidate the
   native selection only on the Unity main thread immediately before mutation.
4. Add the exact `PlotNodeActionInstanceListVariable.AddInstance` Harmony signal
   to the suite's explicit patch list. Verify the authoritative
   `ActivePlotNodeActions` UUID and a matching quantity increase before waking
   Auto Agromancy.
5. Publish verified Auto Harvest completion as a native-free wake/invalidation
   for Auto Agromancy without giving Auto Harvest ownership of its runtime.
6. Add feature status projection, decision-journal state, lifecycle teardown,
   action-family ownership, emergency-stop behavior, and deterministic
   registration-order tests.
7. Update user/configuration/testing documentation only when the feature is
   reachable, then complete installed-game validation on a save with an active,
   sustainable Druidry action/element pair.

## Installed-game evidence retained from the earlier branch

Interactive validation on 2026-07-25 established that a native Plant Sapling
click reached the exact concrete plot-action `AddInstance` signal. The test save
had no active Druidry pair and no available Druidry resource, so it did not prove
a visible nonzero-level mutation. That remains a release-blocking validation
item.

The earlier audit also found that Harmony interception of the Druidry UI
callback and inherited harvest-list add/change methods was bypassed by the
Unity/Mono runtime. All Agromancy entity buttons converged on the concrete,
non-generic plot-action list method, which is why the remaining integration
must use that exact target.

## Deferred opportunities

Reserve-aware sustainability, stored-resource affordability, per-action caps,
cross-action optimization, extra resource-rate wake sources, and a dedicated
gameplay control are future features. They are not migration requirements.
