# Auto Scribe

> **Lifecycle: Planning.** Product policy and the first managed-code contract slice are recorded.
> The installed game build changed on 2026-07-29, so native mutation remains blocked until that
> assembly pair is audited and trusted.

[Back to plans](README.md) | [Auto Items plan](auto-items.md) |
[Native evidence](../reverse-engineering/auto-scribe-native-pipeline.md)

## Progress checkpoint

Keep this section current whenever a phase, decision, blocker, or resume point changes. A later
session must be able to resume without reconstructing the design from conversation history.

Last updated: **2026-07-30**

| Phase | Status | Exit condition |
|---|---|---|
| 1. Product model and static native evidence | **In progress** | Exact recipe, scroll, enchantment, level, target-selection, inventory, and native automation relationships are accepted |
| 2. Current-build compatibility audit | **Required before mutation** | The installed assembly pair is differentially verified and either trusted or rejected |
| 3. Shared world publication | **Planned** | Scroll counts by level, structure enchantments, exact Scribe recipes, target level, native target eligibility, and automation instances publish atomically |
| 4. Read-only demand planner | **Planned** | A native-free planner computes target-level deficits and bounded desired automation without emitting mutations |
| 5. Target-aware Auto Items integration | **Planned** | Auto Items submits a Scroll only when its strongest owned level still has a native-valid upgrade target |
| 6. Guarded Auto Scribe mutation | **Planned** | The feature manages only automation instances it owns, revalidates live state, and verifies every add, level change, and removal |
| 7. Configuration and UX | **Planned** | Disabled-by-default controls explain coverage, target level, slot use, dependency state, and ownership conflicts |
| 8. Lifecycle, diagnostics, and observability | **Planned** | Emergency stop, disable, save/load, reset, NG+, shutdown, and manual edits have explicit terminal behavior and stable diagnostics |
| 9. Automated verification | **Planned** | Focused, complete portable, profiler, source-contract, and installed-contract gates pass |
| 10. Interactive game validation | **Planned** | Disposable-save coverage, upgrade, slot scarcity, manual-race, lifecycle, restart, and cleanup scenarios pass |
| 11. Documentation and cleanup | **Planned** | Behavior docs replace completed plan material and the stacked PR is reviewable |

### Current resume point

- Worktree: `.analysis/worktrees/auto-scribe-plan`
- Branch: `agent/auto-scribe-plan`
- Stacked base: `agent/auto-items-plan` at `9f01300`
- Dependency: draft PR #99, which supplies lifecycle-safe Scroll consumption.
- Current task: finish Phase 1 without authorizing mutation. Obtain serialized asset evidence for
  the three recipe-to-scroll and scroll-to-enchantment/target graphs, then audit the new installed
  assembly pair before adding native contracts.
- Installed candidate hashes observed on 2026-07-30:
  - `Assembly-CSharp.dll`: `436210E61D9F8B84658609D35E32BC274356170005AC15FE93FA36D4D9F7AA4C`
  - `Assembly-CSharp-firstpass.dll`: `D14D52652591ED3CB5ACF55186478DD3873F3C836871E0F68AA861D1767F480A`
- The repository's accepted Auto Items baseline uses a different main-assembly hash. The new
  managed-code findings are candidate planning evidence only until the compatibility workflow
  accepts them.
- No DLL was installed, no save was opened or edited, and no native mutation was attempted.

## Goal

Fully automate preparing the highest currently craftable Scroll set for every structure that the
game considers a valid Scroll target.

The completed loop is:

```text
native progression unlocks a higher Scribe level or another structure
  -> Auto Scribe observes exact coverage and target-level stock
  -> Auto Scribe enables only the missing Scroll production
  -> native Scribe automation creates Scrolls
  -> Auto Items submits the strongest Scroll through native random targeting
  -> the native targeter upgrades the most deficient valid structure
  -> fresh world publication reduces the remaining deficit
  -> Auto Scribe stops production when supply covers every deficit
```

Auto Scribe prepares Scrolls. Auto Items consumes them. Neither service writes an enchantment,
chooses a target structure, changes toxicity, edits a save, or reproduces an effect directly.

## Product model

The initial supported set is exact and closed:

| Role | Recipe UUID | Scroll UUID | Enchantment UUID |
|---|---|---|---|
| Advancement | `a4a02a8f-6573-411c-a30c-6d9bcee12605` | `5f6aa08d-7da6-4c7a-89c9-aabcfe48e886` | `0796ee25-e1f6-4c5c-abba-aad46e02318b` |
| Power | `9c0a2b96-45fa-4aca-83ba-8efad8895608` | `4bb8af50-fc7d-44a7-b1fc-937c390f8aec` | `b9d5f0f7-43fd-4bad-a8e2-8a73f2f1d1d6` |
| Learning | `49da8d21-0f6a-492e-bd9a-15531b1737d5` | `ec14ee5d-66a3-4b28-a271-25dca2414387` | `b74c2058-4113-4b6c-b11e-1c97304d236c` |

The exact Scribe recipe type is `ee001474-8209-4238-9566-84899a877226`
(`CraftingRecipeTypeSO`, `ScribeCrafting`). Its native `maxStartingLevel` is the target level.
The player's selected `startingLevel` is a UI preference and is not changed by the feature.

For one Scroll role at target level `L`:

```text
demand = native-valid structures with no matching enchantment at level >= L
supply = owned Scroll counts at level >= L + matching pending/queued Scroll uses
deficit = max(demand - supply, 0)
```

Native automation is desired only while `deficit > 0`. A fresh snapshot recomputes demand after
each craft, use, structure unlock, level unlock, save/load, reset, or NG+ transition.

## Locked decisions

1. **Separate stacked feature.** Auto Scribe is an ordinary ServiceCycle service and a separate
   reviewable PR stacked on Auto Items. It does not enlarge Auto Items into a crafting service.
2. **Highest currently craftable level.** The target is the exact native
   `ScribeCrafting.maxStartingLevel`, not the player's selected starting level and not a guessed
   level from an upgrade name.
3. **Exact three-role set.** Only the accepted Advancement, Power, and Learning recipe/Scroll/
   enchantment identity chains are supported. Names are diagnostics only.
4. **Native-valid coverage.** "All structures" means every structure the exact Scroll target
   selection accepts at the target scaling. Visibility alone is not enough.
5. **Higher replaces lower.** A structure is covered when the matching enchantment level is at
   least the target. Lower enchantments create one unit of demand; they are not removed directly.
6. **Supply prevents overproduction.** Same-or-higher owned counts and in-flight uses reserve one
   deficit each. Lower-level inventory does not satisfy higher-level demand.
7. **Auto Items remains authoritative for use.** Scrolls are consumed only through the existing
   guarded native-random path. Auto Scribe never invokes `ConsumableSO.SelectAndFire()`.
8. **Target-aware Scroll admission.** Auto Items must reject a Scroll when its strongest live count
   has no valid native target. This prevents losing obsolete or surplus Scrolls after full coverage.
9. **No disposal policy.** The suite never discards lower or surplus Scrolls. They remain player
   inventory and are ignored when no native upgrade target exists.
10. **No player automation takeover.** Pre-existing native Scribe automation is observed as supply
    pressure but never edited or removed. Every mutation must address an instance created and still
    owned by this feature.
11. **Fail closed on dependency loss.** Auto Scribe pauses production when Auto Items Scroll use is
    disabled, unhealthy, lifecycle-retired, or lacks action-family ownership. It does not build an
    unbounded stockpile while consumption is unavailable.
12. **Disabled by default.** The feature requires explicit activation and exposes why a role is
    producing, covered, waiting, externally automated, or blocked.

## Open design rulings

These are implementation questions, not permission to guess:

1. **Ownership across save/restart.** Prove how suite-created native automation instance UUIDs are
   serialized and how ownership can survive save/load without claiming a player's pre-existing
   instance. If durable ownership cannot be made unambiguous, use bounded one-shot native crafting
   instead of persistent automation.
2. **Slot scarcity.** When fewer than three native Auto Scribe slots are available, choose and test
   a deterministic fair rotation. The preferred policy is largest uncovered deficit, then oldest
   served role, then stable role UUID.
3. **External automation accounting.** Determine whether a player-owned automation instance can be
   treated as eventual supply or only as a reason to avoid competing. The first safe behavior is
   to leave that role untouched and report external ownership.
4. **In-flight level evidence.** Confirm that pending Scroll usage preserves the consumed
   `ConsumableCount` scaling strongly enough to reserve the correct level.
5. **Completion wake.** Identify the narrow native invalidation signal for automation completion,
   enchantment replacement, recipe-level unlock, and structure discovery. Polling remains a bounded
   fallback, not an excuse for Harmony work in an unbounded path.

## Required publication

Extend the shared world collector rather than introducing a feature-owned scan. Publish only
native-free facts needed by policy:

- exact Scribe recipe identity, visibility, recipe type, level behavior, and target level;
- exact recipe-to-output Scroll identity;
- Scroll inventory counts by level and pending-use level;
- structure identity, visibility, and matching enchantment identity plus level;
- native target eligibility for each exact role at the current target scaling;
- native Auto Scribe capacity and each live automation instance's stable identity, recipe, level,
  and lifecycle;
- completeness evidence naming every unavailable contract.

The worker receives one immutable world generation. It must not retain Unity objects, walk
serialized effect graphs, call target selectors, or infer relationships from names.

## Native boundary

Every add, level change, or removal must run on the Unity main thread and revalidate:

- feature configuration, emergency stop, lifecycle, dependency health, and action-family ownership;
- exact recipe, recipe type, output Scroll, and target-level identity;
- native automation list and slot capacity;
- current demand, same-or-higher stock, and in-flight use;
- exact instance ownership and unchanged expected pre-state;
- player-owned automation absence for the affected recipe;
- exact postcondition after one mutation.

An unknown type, overload, list, slot count, instance owner, relationship, or postcondition rejects
the action. An ambiguous attempted mutation quarantines Auto Scribe for the lifecycle.

## Configuration and UX

The proposed surface is intentionally small:

- `AutoScribe.Mode`: `Disabled` by default, or `Active`;
- `AutoScribe.Roles`: Advancement, Power, and Learning enabled by default behind the master switch;
- `AutoScribe.EvaluationIntervalSeconds`: bounded planning cadence.

The Mods page should show a read-only coverage summary per role:

```text
Power: target Lv 12 · 28/41 covered · 2 supplied · producing
```

It should also explain dependency and safety states: Auto Items Scrolls disabled, target evidence
unavailable, no automation slot, player automation present, current build unaudited, lifecycle
retired, or mutation quarantined. It must not expose UUID entry as the normal workflow because the
supported set is exact and closed.

## Delivery sequence

1. Finish serialized asset evidence and current-build compatibility audit.
2. Add exact known entities and installed metadata contracts.
3. Publish levelled Scroll counts, enchantment coverage, Scribe recipes, target eligibility, and
   automation instances through the shared world snapshot.
4. Implement and portable-test the pure demand/supply planner with no action adapter.
5. Tighten Auto Items Scroll eligibility and live preflight to require a valid upgrade target.
6. Add disabled-by-default configuration, diagnostics, ownership, and service registration.
7. Implement guarded native Auto Scribe instance management after the ownership ruling is proven.
8. Add configuration UX and coverage summaries.
9. Run focused and complete automated gates on the current tree.
10. Perform interactive disposable-save validation and replace completed plan material with
    maintained behavior documentation.

## Interactive validation outline

The final UAT must include:

- one, two, and three available native automation slots;
- empty coverage, mixed lower levels, complete coverage, and a newly unlocked target level;
- a structure becoming visible while active;
- stock already covering demand and surplus lower-level stock;
- a player-owned Scribe automation before activation and a manual edit while active;
- Auto Items disabled, unhealthy, blocked by ownership, and re-enabled;
- toxicity wait while Scroll supply exists;
- emergency stop during production and immediately before a proposed mutation;
- save/load, reset, NG+, scene replacement, shutdown, and restart;
- no valid Scroll targets, canceled native targeting, and an observed postcondition mismatch.

No release claim is permitted until the native UI visibly agrees with published coverage and every
suite-created automation instance is either safely resumed or safely relinquished across those
boundaries.
