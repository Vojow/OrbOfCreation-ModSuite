# Auto Harvest plan

> **Lifecycle: H2 native adapter implemented; coordinated H3 runtime validation in progress.** Auto Harvest is present behind a disabled-by-default configuration gate. The initial installed-build smoke test and normal combined fruit/treasure operation passed on 2026-07-19; the controlled H3 matrix remains pending.

[Back to plans](README.md) · [Orb Automata plan](automata.md) · [Runtime validation](../development/runtime-validation.md)

## Goal

Automatically submit selected ready fruit-tree and treasure-tree collection actions through the game's native plot-action queue without planting, replanting, replacing, or destroying a plot. Unknown readiness, cost, identity, lifecycle, or preservation evidence must fail closed.

Auto Harvest is separate from Auto Agromancy. Auto Harvest operates `PlotNodeSO`, `PlotNodeActionSO`, and `PlotNodeActionInstance`; Auto Agromancy balances continuous `HarvestActionInstance` levels chosen by the player.

## First slice

- Support only the two explicitly selected collection pairs below.
- Require the exact stable UUID and expected native type for both plot and action.
- Require an existing visible plot, an available action on that exact plot, a native-ready phase, known cost semantics, a free native `ActivePlotNodeActions` slot, and no matching queued or running action.
- Submit one collection through the audited native action path and verify an authoritative native postcondition.
- Preserve the plant. The module does not choose seeds, plant, replant, replace, enrich, force growth, or redesign plot layouts.
- Admit only the audited native phase-cycle contract: reserve one idle tree, complete through the native action timer, award through the expected treasure pool, and move that same tree to `Resting`. Any unresolved resource drain, destructive transition, or completion effect rejects the candidate.
- Keep manual collection available. Disabling the feature stops new submissions and never removes existing native work.

## Stable candidate identities

| Role | UUID | Expected type |
|---|---|---|
| Fruit tree plot | `6782dd13-e229-4385-a1aa-8ed86e6ea1ed` | `PlotNodeSO` |
| Fruit tree collect | `60ea60a2-44e9-41c2-86d6-3935fae0b647` | `PlotNodeActionSO` |
| Treasure tree plot | `2d41cfc1-bffa-43b5-b3a8-5e4d5ad85434` | `PlotNodeSO` |
| Treasure tree collect | `3eb68f6f-c2f2-405a-88d2-e5c80345aeb4` | `PlotNodeActionSO` |
| Active plot actions | `70871e86-100b-4ae0-ba9b-fc96e09b7e1f` | `PlotNodeActionInstanceListVariable` |

Names are diagnostic only. The canonical mapping is [`data/entity-mappings.tsv`](../../data/entity-mappings.tsv).

## Verified assembly surface

Metadata inspection of `Assembly-CSharp.dll` SHA-256 `5845797D40E4631517DE9F4D6296F10C7381AAD5DA733128B2C4685E66E8711F` establishes these candidate contracts without loading Unity or mutating the game:

- `PlotNodeSO` owns the stable plot catalog, available actions, phase instances, action instances, exact phase quantities, remaining quantity, visibility, and total usage cost.
- `PlotNodeActionSO` owns prerequisites, `Destroy` versus `ExitPhase` element-cost semantics, exit phase, quantity cost, native drain cost, persistent action effects, parallel-action behavior, completion effects, and scaling.
- `PlotNodeActionInstance` exposes exact plot/action identity, native resource cost, readiness-related quantity bounds, engagement state, and current queued quantity.
- `PlotNodeActionInstanceListVariable` exposes native identity matching and `AddInstance`. Its inherited live `value` list and `HasEmptySpot` contract are authoritative; its `GetAll()` override returns a new empty list and must not be used for live duplicate or slot inspection. The first slice has no reason to call `RemoveInstance`.
- `PlotNodeSO.PlotNodePhaseInstance` exposes native phase quantity and expiry state.

The native UI checks `GetMaximumRemInstances() > 0` before adding, but `AddInstance` does not repeat that exact remaining-quantity check when a matching action already exists. The adapter must revalidate immediately before submitting exactly one action. Plot actions use their own fixed `ActivePlotNodeActions` slot list; they do not consume the global `ActionManager` queue used by Auto Buy.

These contracts identify the state and mutation surfaces. The exact serialized values and the native methods that consume them are audited below.

## Verified serialized assets

The repository-local, read-only audit used `sharedassets0.assets` SHA-256 `BBCCDA17BE8B6B26A3BD0F492584425085250D0B38D14A6024D741D9EBD40446` from Unity `6000.0.70f1`. UnityPy `1.25.2` with TypeTreeGeneratorAPI `0.0.10` decoded each selected object with strict byte-count verification against the copied `Assembly-CSharp.dll`. The decoded proprietary asset data and local parser environment remain ignored and are not committed.

Both selected plots contain their exact collect action in `availableActions` and have no native `autoAction`. Their serialized contracts are:

| Contract | Fruit tree | Treasure tree |
|---|---:|---:|
| Collect base time | 3 seconds | 10 seconds |
| Prerequisites | none | none |
| Element cost | 1 idle tree | 1 idle tree |
| Cost transition | `ExitPhase` to `Resting` | `ExitPhase` to `Resting` |
| Resource drain / persistent effects | none / none | none / none |
| Parallel / any-state / size-scaled cost | false / false / false | false / false / false |
| Native plot yield included | yes (`ignoreNodeYield = false`) | yes (`ignoreNodeYield = false`) |
| Completion script | `TreasurePoolInstantEffect` / `EarnTreasure` / value 1 | `TreasurePoolInstantEffect` / `EarnTreasure` / value 1 |
| Reward pool | `FruitTreasurePool` (`b3ab80f0-80c7-41d4-b4c7-f34c3e909104`) | `CoreTreasurePool` (`1a370ff9-fea7-4a2a-bca7-57fdb2862356`) |
| Completion scaling | `SpecialScaling` (`be446180-242f-40d2-910e-91e735fc20ad`) | `SpecialScaling` (`be446180-242f-40d2-910e-91e735fc20ad`) |
| Growing / resting timer | 480 / 340 seconds | 720 / 360 seconds |

The exact completion script calls `TreasurePoolSO.EarnPartialTreasure`; its scaling modifier only selects the audited scaling weight. Neither completion object mutates the plot.

## Verified native phase and queue flow

The audited IL establishes this sequence:

1. Native UI admission requires `GetMaximumRemInstances() > 0`, clamps the requested quantity, and calls `ActivePlotNodeActions.AddInstance`.
2. Because both actions have `useAnyStateForCost = false`, `GetMaximumRemInstances()` derives capacity from unreserved `Idle` quantity. Adding one action immediately reserves one idle tree through `AddActionUsage`.
3. `AddInstance` either increments an exact plot/action match or creates, initializes, and engages a new `PlotNodeActionInstance`. Auto Harvest rejects an existing match and always requests exactly one new instance.
4. On completion, the instance executes the audited treasure reward, removes one `Idle` quantity, and creates one `Resting` quantity. `ExitPhase` keeps total plot quantity constant; it does not take the `Destroy` path.
5. The selected plots naturally transition `Resting` to `Growing`, then `Growing` to `Idle`. This is the ordinary reusable tree cycle.

The synchronous acceptance postcondition is therefore an exact new live-list entry for the selected plot/action pair, engaged with actual quantity one, plus one additional used native plot-action slot. The adapter must capture those values before mutation and require all of those exact deltas immediately afterward. Any mismatch is ambiguous and blocks that pair until lifecycle recovery.

## Implemented runtime boundary

The H2 adapter now:

1. Resolves the eight selected plot, action, active-list, scaling, and reward identities through the shared exact UUID/type registry resolver for the current lifecycle generation.
2. Rechecks serialized phase, cost, prerequisite, drain, effect, completion, and reward-pool contracts on each candidate evaluation and immediately before mutation.
3. Requires positive native readiness, rejects any active supported collect, and requires two empty `ActivePlotNodeActions` slots so one remains available for manual work.
4. Submits quantity one, verifies the exact new engaged entry and slot delta, and blocks an ambiguously attempted pair until lifecycle recovery.
5. Alternates successful fruit and treasure submissions, performs no scans while disabled, and clears retained Unity references and ambiguity blocks across lifecycle transitions.

Any unresolved or contradictory value leaves the candidate mechanically ineligible.

## Delivery stages

### H0 — Contract and data audit (complete for the selected pair)

- Lock the required metadata surface into installed-game tests.
- Relevant IL and exact serialized action data are audited for the current assembly and asset hashes.
- Exact native submission, preservation, duplicate, and synchronous postcondition contracts are recorded above.

Exit: both allowlisted collect actions have complete identity, readiness, cost, preservation, duplicate, queue, and postcondition evidence.

### H1 — Pure policy engine (complete)

- The initial pure decision policy now admits only the two exact allowlisted pairs when every identity, selection, lifecycle, visibility, availability, prerequisite, readiness, preservation, duplicate, and native action-slot fact is positively verified.
- Model immutable candidate snapshots and explicit rejection reasons.
- Select only allowlisted, ready, non-destructive, non-duplicate candidates.
- Reuse shared suite mutation admission without accessing Unity from tests; do not apply the unrelated global action-queue capacity policy.
- Cover unknown identity, wrong type, locked/hidden plot, wrong phase, destructive cost, resource drain, full native plot-action slots, duplicate work, lifecycle changes, and fair ordering.

Exit: portable tests prove that unknown or destructive state cannot produce a submission request.

### H2 — Native adapter, disabled by default (complete)

- A lifecycle-bound adapter uses cached reflected metadata and exact UUID/type checks.
- All authoritative state is revalidated immediately before one native mutation.
- Shared native postcondition verification blocks ambiguous retries until lifecycle recovery.
- The config-only master setting defaults to `Disabled`; both supported pair selectors default true behind it.

Exit: the implementation builds against the audited assembly and cannot mutate outside the two selected collection pairs.

### H3 — Coordinated runtime validation

- Install only after explicit approval while the game is closed.
- Validate on a disposable backed-up save with other plot automation disabled.
- Observe readiness, queue entry, completion, yield, plant preservation, manual collection, save/reload, title return, emergency disable, and plugin removal.

Progress recorded on 2026-07-19:

- Backed up the complete save and BepInEx configuration directories while the game was closed, preserved the previously installed DLLs and log, and verified the backup copies against their sources.
- Installed the exact `OrbAutomata` 0.9.0 and `OrbModding.Common` 0.3.3 Release outputs and verified matching source/destination SHA-256 hashes.
- Launched the supported three-plugin suite against the audited game assemblies. BepInEx loaded Automata 0.9.0 once with Auto Harvest disabled at startup, completed chainloader startup, and recorded no error, exception, Harmony failure, or contract mismatch in the complete startup log.
- Enabled both supported selectors through configuration and observed successful normal fruit-tree and treasure-tree collection in game.
- The isolated single-selector cycles, final-manual-slot reservation, emergency disable, title/save lifecycle, removal, rollback, and repeated fairness checks remain pending. This initial smoke result does not complete H3.

Exit: repeated collection preserves both plot types and player control under the documented runtime matrix.

## First-slice exclusions

- Any ordinary tree-harvest, herb-gathering, mining, destroy, enrich, imbue, peel, force-growth, or planting action.
- Choosing a plot or action that the player did not explicitly enable.
- Replanting or replacing anything.
- Direct field or save-file mutation.
- Guessing serialized behavior from an internal/display name.
- Coexistence with another mod that automates plot actions.
