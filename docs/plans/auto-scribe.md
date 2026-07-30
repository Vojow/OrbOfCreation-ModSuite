# Auto Scribe

> **Lifecycle: Planning.** Product policy, current-build trust, and the first managed-code and
> serialized-asset contract slices are recorded. Native mutation remains blocked on the incomplete
> recipe/output/target relationship audit and one-shot action contracts described below.

[Back to plans](README.md) | [Auto Items plan](auto-items.md) |
[Native evidence](../reverse-engineering/auto-scribe-native-pipeline.md)

## Progress checkpoint

Keep this section current whenever a phase, decision, blocker, or resume point changes. A later
session must be able to resume without reconstructing the design from conversation history.

Last updated: **2026-07-30**

| Phase | Status | Exit condition |
|---|---|---|
| 1. Product model and static native evidence | **In progress** | Every registered Scribe recipe's output Scroll, enchantment, level, and target-selection relationships are proven |
| 2. Current-build compatibility audit | **Complete** | `steam-windows-2026-07-29` accepts the installed assembly pair on `main` |
| 3. Shared world publication | **Planned** | Levelled Scroll stock, structure enchantments, exact Scribe recipes, target eligibility, and Scribe work publish atomically |
| 4. Read-only coverage planner | **Planned** | A native-free planner publishes production demand and Scroll-use directives by opaque role and level without emitting mutations |
| 5. Target-aware Auto Items integration | **Planned** | Auto Items submits a Scroll only when the shared plan and immediate native preflight both find a valid target for its strongest owned level |
| 6. Guarded bounded Scribe mutation | **Planned** | The feature submits at most one revalidated one-shot native Scribe craft at a time and verifies its queue postcondition |
| 7. Configuration and UX | **Planned** | Disabled-by-default controls explain coverage, target level, queue use, dependency state, and external production |
| 8. Lifecycle, diagnostics, and observability | **Planned** | Emergency stop, disable, save/load, reset, NG+, shutdown, and manual edits have explicit terminal behavior and stable diagnostics |
| 9. Automated verification | **Planned** | Focused, complete portable, profiler, source-contract, and installed-contract gates pass |
| 10. Interactive game validation | **Planned** | Disposable-save coverage, upgrade, queue scarcity, manual-race, lifecycle, restart, and cleanup scenarios pass |
| 11. Documentation and cleanup | **Planned** | Behavior docs replace completed plan material and the stacked PR is reviewable |

### Current resume point

- Worktree: `.analysis/worktrees/auto-scribe-plan`
- Branch: `agent/auto-scribe-plan`
- Stacked base: `agent/auto-items-plan` at `9f01300`
- Dependency: draft PR #99, which supplies lifecycle-safe Scroll consumption.
- Current task: finish Phase 1 without authorizing mutation. The serialized
  `ScribeCraftingRecipes` registry is confirmed to contain exactly six recipes: Advancement,
  Development, Echo, Excellence, Learning, and Power. Obtain read-only evidence for every
  recipe-to-Scroll and Scroll-to-enchantment/target edge before adding those native contracts.
- Installed hashes observed on 2026-07-30:
  - `Assembly-CSharp.dll`: `436210E61D9F8B84658609D35E32BC274356170005AC15FE93FA36D4D9F7AA4C`
  - `Assembly-CSharp-firstpass.dll`: `D14D52652591ED3CB5ACF55186478DD3873F3C836871E0F68AA861D1767F480A`
- `origin/main` accepts this exact pair as baseline `steam-windows-2026-07-29` in
  `GameAssemblyAudit` and `data/native-contracts.json`. Build trust is complete; it does not replace
  the still-required feature-specific relationship and mutation contract audit.
- No DLL was installed, no save was opened or edited, and no native mutation was attempted.

## Goal

Fully automate preparing every highest-currently-craftable Scribe Scroll for every structure that
the game considers a valid target for that exact Scroll.

The completed loop is:

```text
native progression unlocks a higher Scribe level or another structure
  -> Auto Scribe observes exact coverage and target-level stock
  -> the shared coverage plan publishes production demand and Scroll-use directives
  -> Auto Scribe submits one missing Scroll to the native Scribe queue
  -> the native one-shot Scribe action creates the Scroll
  -> Auto Items submits the strongest Scroll through native random targeting
  -> the native targeter upgrades the most deficient valid structure
  -> fresh world publication reduces the remaining deficit
  -> Auto Scribe stops queueing when supply covers every deficit
```

Auto Scribe prepares Scrolls. Auto Items consumes them. Neither service writes an enchantment,
chooses a target structure, changes toxicity, edits a save, or reproduces an effect directly.

## Product model

The screenshot's Advancement, Power, and Learning rows are a visible progression slice, not the
catalog boundary. The checked-in identity extraction contains at least eight Scroll-named
`ConsumableSO`/`EnchantmentSO` pairs and six Scroll-named `CraftingRecipeSO` candidates:

| Candidate role | Recipe UUID candidate | Scroll UUID | Enchantment UUID |
|---|---|---|---|
| Advancement | `a4a02a8f-6573-411c-a30c-6d9bcee12605` | `5f6aa08d-7da6-4c7a-89c9-aabcfe48e886` | `0796ee25-e1f6-4c5c-abba-aad46e02318b` |
| Development | `b15690ab-828c-42b9-ad69-70f169a45961` | `09d6101a-460d-4ce9-b7d4-46c4abaeadb7` | `cb354ece-fd8c-4ffc-a67e-b24cc3fe5fa5` |
| Echoing | `008ccaa9-da26-4b55-95a5-5bc5df9c62f0` | `164dbfa9-8b9f-4976-9d17-ad3ad6b07a62` | `d854b177-865f-45ee-97a3-23d904df1ba1` |
| Excellence | `6c5c36ea-4736-46d2-b961-6227d4cce5d3` | `49057abe-fe54-481e-99bc-2b82c3995c6b` | `7b17670e-b3b6-401f-83f7-9c0e6d157852` |
| Investment | None identified | `da5eab6d-ab4c-4b32-aca1-2e83b6d3a64b` | `f75cea6e-5d21-439f-bce4-79199b22434d` |
| Learning | `49da8d21-0f6a-492e-bd9a-15531b1737d5` | `ec14ee5d-66a3-4b28-a271-25dca2414387` | `b74c2058-4113-4b6c-b11e-1c97304d236c` |
| Power | `9c0a2b96-45fa-4aca-83ba-8efad8895608` | `4bb8af50-fc7d-44a7-b1fc-937c390f8aec` | `b9d5f0f7-43fd-4bad-a8e2-8a73f2f1d1d6` |
| Speed | None identified | `b2232a7d-5c97-44c9-9520-686e99fa8293` | `9f068bad-f3a0-47de-84f4-407e67622fe1` |

These rows are identity candidates, not accepted relationships. Names made the missing scope
visible, but names never authorize automation.

The serialized `ScribeCraftingRecipes` registry contains exactly six entries, resolving to
`CraftScrollAdvancement`, `CraftScrollDevelopment`, `CraftScrollEcho`,
`CraftScrollExcellence`, `CraftScrollLearning`, and `CraftScrollPower`. This proves the current
registry boundary, but not yet the output and target edges in each graph.

The production catalog is reconstructed from accepted native relationships on every lifecycle:

1. enumerate recipes registered to the exact Scribe recipe type;
2. require an audited levelled-recipe shape;
3. require exactly one output `ConsumableSO` belonging to the exact Scroll family;
4. require an audited structure-target graph with an unambiguous enchantment identity;
5. publish the resulting stable recipe/Scroll/enchantment role.

An owned Scroll with no accepted Scribe recipe remains eligible for target-aware Auto Items use, but
Auto Scribe reports it as coverage-only and does not invent a production path. A role contributes
to Auto Scribe's completion goal only when its exact native Scribe recipe is proven.

The exact Scribe recipe type is `ee001474-8209-4238-9566-84899a877226`
(`CraftingRecipeTypeSO`, `ScribeCrafting`). Its native `maxStartingLevel` is the target level.
The player's selected `startingLevel` is a UI preference and is not changed by the feature.

For one Scroll role at target level `L`:

```text
demand = native-valid structures with no matching enchantment at level >= L
supply = owned Scroll counts at level >= L + matching queued crafts and pending Scroll uses
deficit = max(demand - supply, 0)
```

One bounded craft is eligible only while `deficit > 0`. A fresh snapshot recomputes demand after
each craft, use, structure unlock, level unlock, save/load, reset, or NG+ transition.

## Identity facade

UUIDs remain mandatory at the native boundary: the suite invariant is stable UUID plus expected
native type. They are not part of Auto Scribe policy, configuration, or normal UX.

An `IAutoScribeIdentityCatalog` facade selects an `AutoScribeIdentityProfile` by the accepted
`GameAssemblyBaseline.Id`. A profile is the single feature-owned mapping from semantic,
version-stable `ScrollRoleKey` values to exact recipe, Scroll, enchantment, recipe-type, list, and
capacity identities:

```text
ScrollRoleKey ("scribe.advancement")
  -> baseline identity profile
  -> recipe UUID + CraftingRecipeSO
  -> Scroll UUID + ConsumableSO
  -> enchantment UUID + EnchantmentSO
```

The collector and action boundary resolve and type-check native identities. The worker sees only
opaque role keys and native-free descriptors. A future game update that replaces UUIDs therefore
changes one audited baseline profile and its contracts, not the evaluator, scheduler,
configuration, or Auto Items integration. An absent, ambiguous, or mismatched profile fails closed.

## Locked decisions

1. **Separate stacked feature.** Auto Scribe is an ordinary ServiceCycle service and a separate
   reviewable PR stacked on Auto Items. It does not enlarge Auto Items into a crafting service.
2. **Highest currently craftable level.** The target is the exact native
   `ScribeCrafting.maxStartingLevel`, not the player's selected starting level and not a guessed
   level from an upgrade name.
3. **Complete native catalog, not a screen list.** Every audited recipe registered to the exact
   Scribe type is considered. A role is supported only when its recipe, Scroll family, structure
   target, and enchantment relationship form one accepted graph. Names are diagnostics only.
4. **Native-valid coverage.** "All structures" means every structure the exact Scroll target
   selection accepts at the target scaling. Visibility alone is not enough.
5. **Higher replaces lower.** A structure is covered when the matching enchantment level is at
   least the target. Lower enchantments create one unit of demand; they are not removed directly.
6. **Supply prevents overproduction.** Same-or-higher owned counts and in-flight work reserve one
   deficit each. Lower-level inventory does not satisfy higher-level demand.
7. **Replaceable identity facade.** Policy and persisted selections use `ScrollRoleKey`; exact UUID
   plus expected-type tuples live only in an audited, baseline-specific identity profile.
8. **Auto Items remains authoritative for use.** Scrolls are consumed only through the existing
   guarded native-random path. Auto Scribe never invokes `ConsumableSO.SelectAndFire()`.
9. **Shared target-aware admission.** The read-only coverage planner publishes a per-role,
   per-level `ScrollUseDirective`. Auto Items blocks the strongest owned Scroll when the directive
   says there are zero candidates, unblocks it on a later generation with candidates, and fails
   closed on unknown evidence. Immediate native target preflight remains authoritative.
10. **No disposal policy.** The suite never discards lower or surplus Scrolls. They remain player
    inventory and are ignored when no native upgrade target exists.
11. **Bounded autonomous play.** `AutoScribe.Active` authorizes one-shot native Scribe queue
    submissions, not ownership of persistent native Auto Scribe instances. Pre-existing manual and
    automatic work remains game-authoritative capacity and supply pressure and is never edited.
12. **Fail closed on dependency loss.** Auto Scribe pauses production when Auto Items Scroll use is
    disabled, unhealthy, lifecycle-retired, or lacks action-family ownership. It does not build an
    unbounded stockpile while consumption is unavailable.
13. **Disabled by default.** The feature requires explicit activation and exposes why a role is
    producing, covered, queued, waiting, externally automated, or blocked.

## Open design rulings

These are implementation questions, not permission to guess:

1. **Queue fairness.** When several roles are deficient, choose and test deterministic fair
   scheduling. The preferred policy is largest uncovered deficit, then oldest served role, then
   stable `ScrollRoleKey`.
2. **External automation accounting.** Determine whether a player-owned automation instance can be
   treated as eventual supply or only as a reason to avoid competing. The first safe behavior is
   to avoid queueing the same role and report external production.
3. **In-flight level evidence.** Confirm that queued Scribe work and pending Scroll usage preserve
   their scaling strongly enough to reserve the correct level.
4. **Canonical one-shot action.** Prove the non-UI native queue contract underlying
   `UICraftingPage.QueueCraft`, including availability, cost, active-queue capacity, construction,
   initiation, list insertion, and postcondition. Do not require the Scribe page to be open.
5. **Completion wake.** Identify the narrow native invalidation signal for craft completion,
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
- native active Scribe queue capacity and each queued instance's role, level, and lifecycle;
- native Auto Scribe capacity and live automatic instances as external production pressure;
- completeness evidence naming every unavailable contract.

The worker receives one immutable world generation. It must not retain Unity objects, walk
serialized effect graphs, call target selectors, or infer relationships from names.

## Auto Scribe and Auto Items coordination

The background-safe `ScrollCoveragePlanner` produces one immutable `ScrollCoveragePlan` from the
same world generation used by both services. For every role and relevant level it publishes:

- valid native target count and uncovered deficit;
- covered, production-needed, or evidence-unknown state;
- `AllowUse`, `BlockNoCandidate`, or `BlockUnknown` for Auto Items;
- whether one bounded Scribe craft is currently useful.

This is the communication contract between the features. Its read-only planning slice remains
available even when Auto Scribe's master switch is disabled, so Auto Items can safely consume an
already-owned Scroll without requiring Auto Scribe production. Auto Scribe consumes only the
production directive; Auto Items consumes only the use directive. Neither service mutates the
other's configuration.

A pinned plan can become stale between evaluation and execution. Stale permission is closed by
re-running the exact native target check immediately before `SelectAndFire`; a stale block merely
delays use until the next world generation. The shared plan can tighten Auto Items admission but
can never override its lifecycle, toxicity, inventory, action-family, or native preflight gates.

## Native boundary

Every one-shot queue submission must run on the Unity main thread and revalidate:

- feature configuration, emergency stop, lifecycle, dependency health, and action-family ownership;
- exact recipe, recipe type, output Scroll, target-level identity, and current world generation;
- active Scribe queue room, native availability, computed cost, and required resources;
- current demand, same-or-higher stock, and in-flight work;
- player-owned automatic production absence for the affected role;
- exact active-list postcondition after adding one native craft.

An unknown type, overload, list, slot count, relationship, or postcondition rejects the action. An
ambiguous attempted mutation quarantines Auto Scribe for the lifecycle.

## Configuration and UX

The proposed surface is intentionally small:

- `AutoScribe.Mode`: `Disabled` by default, or `Active`;
- `AutoScribe.Roles`: a discovered-role picker persisted as stable `ScrollRoleKey` values, with all
  audited producible roles enabled by default behind the master switch;
- `AutoScribe.EvaluationIntervalSeconds`: bounded planning cadence.

The Mods page should show a read-only coverage summary per role:

```text
Development: target Lv 12 - 28/41 covered - 2 supplied - queued
```

It should also explain dependency and safety states: Auto Items Scrolls disabled, coverage-only
Scroll with no Scribe recipe, target evidence unavailable, no queue room, player automation
present, identity profile unavailable, lifecycle retired, or mutation quarantined. Exact UUIDs
remain diagnostic native authority but are not persisted policy or normal user-facing labels.

## Delivery sequence

1. Finish serialized recipe/output/target relationship evidence. Current-build trust is complete.
2. Add the baseline-specific identity profile, exact Scribe type and Scroll family identities, and
   installed metadata contracts; discover individual role graphs from native relationships rather
   than a fixed screen list.
3. Publish levelled Scroll counts, enchantment coverage, Scribe recipes, target eligibility, and
   active/automatic instances through the shared world snapshot.
4. Implement and portable-test the pure coverage plan, use directives, and fair one-shot scheduler
   with no action adapter.
5. Tighten Auto Items Scroll eligibility and live preflight to consume the use directive and
   require a valid upgrade target.
6. Add disabled-by-default configuration, diagnostics, and service registration.
7. Implement guarded one-shot native Scribe queue submission after its exact action contract is
   proven.
8. Add configuration UX and coverage summaries.
9. Run focused and complete automated gates on the current tree.
10. Perform interactive disposable-save validation and replace completed plan material with
    maintained behavior documentation.

## Interactive validation outline

The final UAT must include:

- zero, one, and several active Scribe queue slots with multiple deficient producible roles;
- empty coverage, mixed lower levels, complete coverage, and a newly unlocked target level;
- a structure becoming visible while active;
- stock already covering demand and surplus lower-level stock;
- a player-owned Scribe automation before activation and manual queue work while active;
- Auto Items disabled, unhealthy, blocked by ownership, and re-enabled;
- toxicity wait while Scroll supply exists;
- emergency stop during production and immediately before a proposed mutation;
- save/load, reset, NG+, scene replacement, shutdown, and restart;
- no valid Scroll targets, canceled native targeting, and an observed postcondition mismatch.

No release claim is permitted until the native UI visibly agrees with published coverage, queued
work, and Scroll-use blocks across those boundaries.
