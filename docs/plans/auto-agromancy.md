# Auto Agromancy level balancing plan

> **Lifecycle: Planned.** This document specifies design intent and verified native contracts; it does not describe released behavior.

[Back to plans](README.md) · [Orb Automata plan](automata.md) · [Runtime validation](../development/runtime-validation.md)

## Goal

Let the player choose an Agromancy action while Orb Automata chooses the highest currently sustainable action level. The first slice removes repetitive level clicking without choosing the action for the player, changing saves directly, or allowing the selected action to make any consumed resource decrease over time.

This is an **Auto Agromancy** feature, not the existing planned **Auto Harvest** feature. Auto Agromancy manages the level of a player-selected continuous `HarvestActionInstance`; Auto Harvest concerns ready harvest and plot actions.

## First-slice player contract

- Add an opt-in `AutoAgromancyMode` with `Disabled` and `BalanceOnSelection`; default to `Disabled`.
- Trigger only when the player clicks the add-side Agromancy action control.
- Keep the clicked Harvest Element plus Harvest Action pair authoritative. The module never chooses a different action or element.
- Set that pair to the highest native level from `1` through `GetMaximumInstances()` whose projected continuous costs leave every affected resource at a net rate of at least zero.
- If level 1 is not sustainable, do not add the action and report the limiting resource.
- A later add-side click on the same pair means "rebalance now," not "add the current native multi-buy amount."
- Removal-side clicks remain completely native and continue to use the player's current action multiplier.
- The first slice balances at selection time only. It does not continuously raise or lower the level after unrelated production, drain, mastery, save, or configuration changes.

"No resource drain" refers to resource quantity: after admission, the projected native net rate must be non-negative. The action will still own a native `ResourceDrain` entry because that is how the game represents its continuous cost.

## Verified native model

The following contracts were inspected in the installed `Assembly-CSharp.dll` with SHA-256 `5845797D40E4631517DE9F4D6296F10C7381AAD5DA733128B2C4685E66E8711F` on 2026-07-17:

- `UIHarvestActionList.OnActionClick(HarvestActionInstance)` sends add-side clicks to `HarvestActionInstanceListVariable.AddInstance(instance, GlobalVariables.GetMultiBuy())`. Its removal branch calls `RemoveInstance` separately.
- `HarvestActionInstanceListVariable` identifies an active action by both `HarvestActionSO` and `HarvestElementSO`. `AddInstance` creates or updates the native instance; `RemoveInstance` removes it when its count reaches zero.
- `HarvestActionInstance.ChangeInstance(int)` clamps `instances` to `0..GetMaximumInstances()`, then uses native `Engage()` or `Disengage()` behavior.
- `HarvestActionInstance.GetMaximumInstances()` returns the element's `masteryLevel + 1`.
- `HarvestActionInstance.GetScalingInfo(int)` combines action scaling, element action scaling, instance power/speed/cost scaling, and full-speed drain scaling.
- The action's native cost vector combines its referenced `actionCost` with any positive element-internal-resource cost.
- `ResourceDrain` applies continuous costs to native `ResourceSO.drain` records and derives an output ratio when resources cannot fully support the requested drain.
- `ResourceSO.GetTrueRate()` includes live production, displayed rate, drain, and loss behavior. `GetModdedDrain()` also applies resource quality to raw drain.

These facts establish the candidate and mutation surfaces. They do not yet prove that every serialized Agromancy instance-cost curve is monotonic or that every action effect is neutral toward its own consumed resources; those are implementation gates below.

## Admission model

Evaluate one immutable snapshot on the Unity main thread immediately before mutation.

For each resource `r` consumed by the selected action and candidate level `L`:

```text
baselineWithoutSelected(r) = liveTrueRate(r) + currentSelectedNetDrain(r)
projectedRate(r, L)         = baselineWithoutSelected(r) - prospectiveSelectedNetDrain(r, L)
sustainable(L)              = projectedRate(r, L) >= 0 for every consumed resource
```

The current selected contribution is added back so rebalancing replaces that action's level rather than double-counting it. All other player actions, passive drains, production, quality, loss, and resource effects remain represented by the live native rate.

Candidate drain must be derived from the game's exact cost and scaling objects:

1. Resolve the selected instance by stable action UUID plus expected `HarvestActionSO` type and stable element UUID plus expected `HarvestElementSO` type.
2. Read the complete native base cost vector, including element-internal-resource cost.
3. Evaluate `GetScalingInfo(L)` and its final drain-cost multiplier.
4. Convert the raw drain contribution to the same effective units used by `ResourceSO.GetTrueRate()`, including native quality semantics.
5. Reject the whole candidate if any resource, tuple, scaling value, quality value, current contribution, or live rate is missing, negative where invalid, NaN, infinite, contradictory, or of the wrong native type.

Use `BigDouble` end to end. Do not convert rates or costs to `double` for admission.

An exact projected rate of zero is admissible in the first slice. A configurable headroom margin belongs to a later policy iteration; silently inventing one would conflict with "highest possible level."

## Finding the highest level

The planner must return the exact highest sustainable level under the audited native scaling contract, not a heuristic approximation.

- First inspect every serialized `InstanceScalingSO` referenced by supported Harvest Actions and record whether prospective drain is monotonic over valid integer instance counts.
- If monotonicity is proven, use a bounded binary search between `1` and `GetMaximumInstances()` and recheck the selected result plus its next level.
- If a supported curve is not monotonic, use a bounded exact search. Do not assume that the first rejected level makes all later levels invalid.
- Put an operation and CPU-time bound on the click path. If an exact answer cannot be produced within the validated bound, fail closed and leave the action unchanged rather than freezing the frame or choosing an approximate level.

The maximum-level value itself must be validated before it is used as a search bound. Overflow, a negative mastery-derived limit, or an implausibly large unaudited range blocks the action.

## Native mutation and ownership

- Patch only the add branch of `UIHarvestActionList.OnActionClick`; verify the exact installed IL shape before enabling the patch.
- Keep calculation and mutation on the Unity main thread.
- Invoke `HarvestActionInstanceListVariable.AddInstance` or the existing instance's `ChangeInstance` once with the exact delta needed to reach the target.
- Do not write the `instances` field directly, edit save JSON, construct an unregistered parallel instance, or reproduce `Engage()` effects outside the native path.
- Do not change `GlobalVariables.GetMultiBuy()` or any backing global value. The balanced add path replaces the multiplier only for this one UI action; native removal remains untouched.
- Revalidate list identity, slot availability, visibility, prerequisites, maximum level, cost vector, and resource snapshot immediately before the native call.
- If another mod or lifecycle event changes the target between planning and mutation, abandon the result and leave player state unchanged.

The first slice owns only the level it applies during the intercepted click. It does not claim ongoing ownership of the action. The player can lower or remove it through native controls, and disabling the module stops interception immediately without rewriting active actions.

## Effect feedback safety

Engaging a Harvest Action applies persistent effects as well as its drain. A selected action could theoretically change production or cost inputs used by its own admission calculation.

Before implementation, audit the serialized `actionEffects` for every supported action and classify whether they can affect:

- a consumed resource's production, quality, loss, or drain;
- the selected action's cost or speed;
- another active drain competing for the same resource.

If all relevant effects are neutral or beneficial, the conservative pre-engagement calculation is sufficient. Otherwise the first slice must either reject that action family or perform an immediate same-call-stack native post-engagement validation and downshift before the next resource increment. Unknown effect feedback fails closed.

## Diagnostics and UI

Keep the initial surface small:

- Configuration: `AutoAgromancyMode` only.
- Success diagnostic: selected action/element UUIDs, previous level, chosen level, maximum native level, and limiting projected rate.
- Rejection diagnostic: concise reason such as `level 1 would drain <resource>`, unknown cost tuple, invalid rate, unsupported scaling, lifecycle changed, or no action slot.
- Operational records obey the existing Automata diagnostics setting; adapter and patch failures remain rate-limited warnings.
- Do not add a global toggle button in the first implementation. Orb Mod Config and the BepInEx configuration file are sufficient until the runtime contract is proven.

A later tooltip enhancement can show `Balanced to Lv N` and the limiting resource, but it is not required for the first mutation proof.

## Delivery stages

### G0 - Contract and data audit

- Add installed-game metadata contracts for all fields and methods used by the adapter and UI hook.
- Verify the exact add/remove branch IL and fail-closed patch behavior.
- Inventory Harvest Action cost vectors, instance-scaling assets, and persistent action effects from the installed build.
- Prove monotonicity where binary search will be used; otherwise define the bounded exact-search limit.
- Record lifecycle invalidation points for title return, save load, reset, and NG+.

Exit: the planner can obtain exact prospective drain vectors without mutating the game, and every supported action family has a documented scaling/effect classification.

### G1 - Pure level planner

- Implement a pure `BigDouble` planner over maximum level, current selected contribution, per-level cost vectors, live net-rate snapshots, and exact work bounds.
- Return the chosen target plus the limiting resource and rejection reason.
- Cover zero production, exact-zero headroom, multiple resources, existing selected drain, unrelated aggregate drain, quality conversion, non-monotonic curves, invalid numbers, and changed snapshots.

Exit: deterministic portable tests prove that the planner returns the highest admissible level or fails closed.

### G2 - Native adapter and click integration

- Add a lifecycle-bound native adapter for action/element identity, active-instance lookup, cost extraction, scaling, resource rates, and native mutation.
- Intercept only add-side player clicks when `BalanceOnSelection` is active.
- Preserve the native success presentation where practical and provide a clear rejection diagnostic when no positive level is sustainable.
- Ensure disabling or an adapter failure leaves the original native controls usable.

Exit: a click selects exactly the player's chosen pair at the computed level, while removal and all unrelated Agromancy controls remain native.

### G3 - Interactive validation

- Use a disposable save and test one action with one resource, one action with multiple resources, an already active pair, a full action list, and a level-1 rejection.
- Compare planner cost vectors with native tooltips and the applied `ResourceDrain` at levels `1`, a middle level, the chosen level, and the next rejected level.
- Observe affected resource quantities and rates long enough to cover native 0.2-second drain-ratio checks; no admitted action may make a resource quantity trend downward.
- Change mastery, production, quality, and competing drains, then click again and confirm a fresh target.
- Verify manual removal, emergency disable, scene/save transitions, 1x/2x/4x/8x Chronomancer operation, save/reload, and plugin removal.
- Run portable tests and the installed-game contract suite against the current working tree.

Exit: selection-time balancing is exact for supported actions, resources remain non-decreasing after admission, and the feature fails closed without trapping player control.

## First-slice exclusions

- Choosing an Agromancy action or Harvest Element automatically.
- Continuous background upshifting or downshifting after the selection event.
- Reallocating or lowering other active actions to make room for the new selection.
- Reserve percentages, absolute rate headroom, time-to-empty targets, priorities, or per-action level caps.
- Plot layout, planting, destructive harvests, replanting, and Auto Harvest strategy.
- Predicting future mastery levels or production upgrades.
- Coexistence with another mod that automates the same Agromancy action list.

## Later QoL sequence

1. **Dirty-event rebalance:** reevaluate owned selections after relevant rate, quality, mastery, or action-list changes, with hysteresis and bounded work.
2. **Headroom policies:** optional minimum net rate, percentage reserve, and per-action cap.
3. **Multi-action allocation:** distribute shared resource headroom across several player-approved actions with deterministic priorities.
4. **Explanation UI:** show the chosen level, limiting resource, next-level deficit, and next evaluation reason in the Agromancy tooltip.
5. **Auto Harvest integration:** keep plot/harvest execution a separate policy even if both features share resource snapshots and diagnostics.

## Definition of done for the first implementation

- The setting defaults to Disabled and does nothing when disabled.
- An add-side click preserves the player's exact action and element choice.
- The selected instance reaches the exact highest audited sustainable native level.
- Every affected resource has a projected and observed non-negative net rate after admission.
- An unsustainable level 1 is not activated.
- Existing active drains are included, but unrelated actions are never reduced or removed.
- Removal clicks, action slots, mastery caps, persistent effects, and saves remain game-authoritative.
- No global multi-buy state is changed.
- Unknown identity, cost, scaling, effect feedback, lifecycle, or resource state rejects mutation with a bounded diagnostic.
- Disabled and steady-state operation perform no catalog scans or background balancing work.
