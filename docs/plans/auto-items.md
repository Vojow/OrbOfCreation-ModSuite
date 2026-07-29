# Auto Items

> **Lifecycle: Active.** Phases 1-10 and 12 are implemented; Phase 11 automated validation is
> complete. Installed-game interactive validation remains open.

[Back to plans](README.md) | [Runtime architecture](../runtime-architecture/README.md)

## Progress checkpoint

Keep this section current whenever a phase, decision, blocker, or resume point changes. A future
session should be able to continue from this section without reconstructing earlier work.

Last updated: **2026-07-29**

| Phase | Status | Exit condition |
|---|---|---|
| 1. Evidence and exact native contracts | **Static slice complete** | Read-only taxonomy, toxicity/recovery/rest, queue, and use contracts are documented; live mutation probes remain mandatory in Phases 4–5 |
| 2. Publication, taxonomy, and toxicity facts | **Complete** | Complete native-free facts publish through the shared world snapshot with portable tests |
| 3. Read-only service, configuration, and diagnostics | **Complete** | Disabled-by-default service, bounded evaluator, status, configuration, and journal projection are registered |
| 4. Scroll implementation | **Implemented; interactive gate open** | Scrolls use native random targeting through a guarded one-item submission and verified stock/queue postcondition |
| 5. Relic-first implementation | **Implemented; interactive gate open** | Relics receive first priority whenever native readiness and toxicity headroom admit them |
| 6. Combined validation and hardening | **Automated gates complete; interactive gate open** | Portable and installed-contract gates pass together; live manual-race, recovery, save/load, reset, and NG+ checks remain |
| 7. Temporary-effect evidence and exact contracts | **Complete for implementation; interactive effect audit open** | Exact usage ownership, pending/engaged state, duration, expiry, save hydration, queue, and toxicity contracts are proven; serialized benefit graphs are not used as automated policy inputs |
| 8. Active temporary-effect publication | **Complete** | Complete lifecycle-safe pending/active/remaining-duration facts publish atomically through the shared world snapshot |
| 9. Conservative temporary-item policy | **Complete** | Separate opt-in Fruit/Potion controls, an exact allowlist, Relic priority, no refresh, fill-first recovery latching, and diagnostics are portable-tested |
| 10. Guarded Fruit/Potion submission | **Complete** | Exact allowed items use the existing one-item native boundary and confirm both immediate pending-usage creation and later effect activation |
| 11. Temporary-item validation and tuning | **Automated gates complete; interactive gate open** | Portable, profiler, source-contract, and installed-assembly gates pass; live expiry/recovery/lifecycle checks remain |
| 12. Temporary-item picker UX | **Implemented; interactive layout gate open** | Mods configuration discovers visible Fruit/Potion items, shows names/stock/toxicity/duration, filters and toggles exact UUID selections, preserves unavailable selections, and retains a raw editor |

### Current resume point

- Worktree: `.analysis/worktrees/auto-items-plan`
- Branch: `agent/auto-items-plan`
- Base: `main` at `7f07c16`
- Current task: interactive Phase 11 behavior validation and Phase 12 layout validation for the
  conservative Fruit/Potion extension.
  Exercise one explicitly allowlisted Fruit and Potion on a disposable save and record pending,
  engagement, expiry, fill-to-saturation, partial and complete toxicity recovery, Relic admission
  at both zero and nonzero toxicity, save/load, reset, NG+, emergency-stop, and manual-race results.
  In the same session, verify picker scrolling, all five filters, long item names, toxicity/duration
  labels, unavailable selections, raw editing, staging, apply, discard, and reopen behavior.
- Static evidence recorded: `docs/reverse-engineering/auto-items-native-pipeline.md` now identifies
  exact family and toxicity UUIDs, the asynchronous native queue, rest/recovery mechanics,
  player-equivalent submission method, global multi-buy interaction, and the remaining live probes.
- Implemented: exact known identities and metadata contracts; `ConsumableTypes` and
  `ConsumableCosts` one-to-many world tables; fail-closed collection; portable traversal, sorting,
  and installed-game contract tests; a native-free profiler that requires exactly one supported
  family, a capped inverted toxicity resource, a valid immediate toxicity cost, and preserves
  additional native costs; disabled-by-default configuration; a bounded ServiceCycle evaluator;
  feature health and decision projection; shared action-family ownership; native Scroll and Relic
  submission with live identity, family, visibility, queue, readiness, targeting, toxicity, and
  lifecycle revalidation; exact one-item multi-buy isolation; postcondition verification; and
  lifecycle-scoped quarantine after an ambiguous attempted mutation.
- Temporary extension implemented: every native usage publishes source item, stable usage UUID,
  pending/engaged state, remaining duration, and maximum duration; Fruit/Potion family controls
  default off; an exact UUID allowlist defaults empty; Relics retain first priority;
  temporary items use available native toxicity headroom; one temporary use blocks all further
  item automation until engagement and expiry; later activation
  is confirmed from a fresh world publication; missing or contradictory activation quarantines
  only that exact item for the lifecycle. The Mods page exposes this allowlist as a discovered-item
  picker while persisting only exact stable UUIDs.
- PR cleanup: picker rendering, picker state/selection, catalog parsing, and exact reflection
  binding are separated. One explicit editor-mode state prevents Items/Raw overlap; invariant
  numeric formatting and fail-closed ambiguous-family/duplicate-identity tests are in place.
- Verification: 1,920 ordinary portable tests, 90 profiler tests, the profiler trace-tool build,
  the 5-test native-contract source gate, and all 25 installed-game contract tests pass on the
  current tree. The installed contract run used the local Windows Steam assemblies. Interactive
  game validation has not been performed in this worktree.
- Working tree: all Auto Items work remains uncommitted in the dedicated worktree.
- Next action: run the Phase 11 behavior and Phase 12 layout checklists. This still requires explicit
  install/live-session approval and a disposable save.

### Locked decisions

1. **Fill-first recovery with Relic priority.** Relics have first priority whenever native
   readiness and toxicity headroom admit them; they have no separate exact-zero restriction.
   Scrolls and admitted temporary items use remaining headroom. Once no otherwise-eligible item
   fits, Auto Items latches a recovery wait and submits nothing until toxicity returns to exact
   zero, then starts another fill cycle.
2. **Native-random Scroll targets.** The game chooses the attribute through its audited random path;
   the suite neither chooses nor weights an attribute.
3. **Safe defaults.** Auto Items defaults to `Disabled`; Scroll and Relic allows default on behind
   that master switch.
4. **The original slice kept temporary families out of mutation scope.** Fruit and Potion were
   classified before they could emit actions. Phases 7-10 replace that historical restriction only
   for exact opt-in items under the temporary policy below.
5. **Documentation and validation travel with behavior.** Publication, Scroll mutation, and Relic
   mutation each land with their own tests, native evidence, runtime validation, and behavior
   documentation. Phase 6 is a combined regression gate, not the first documentation pass.
6. **Temporary families are exact-item opt-in.** Broad Fruit/Potion family membership is
   insufficient because effects can differ in target, value, duration, and safety. The player must
   list each accepted stable UUID; both family controls default off. Runtime admission still
   requires the exact family, a finite positive native duration, toxicity-only cost vectors, and
   every ordinary live readiness check. Serialized benefit graphs are not interpreted or ranked.
7. **The first temporary policy is fill-first and non-stacking.** One allowed temporary item may be
   submitted whenever native toxicity headroom covers its cost and no other temporary effect is
   pending or active. It is never refreshed or stacked. After native expiry, eligible temporary
   items and Scrolls may continue filling headroom until the recovery latch engages.
8. **Activation is asynchronous.** Stock, queue, and a new pending usage verify submission only.
   Temporary-item success additionally requires a later native observation tying the expected
   engaged usage to the submitted stable item. Missing or contradictory activation evidence
   quarantines the exact item for the lifecycle.
9. **Picker labels are conveniences, UUIDs are authority.** The Mods picker reads native display
   names, family, stock, immediate toxicity cost, and base duration only when opened on the Unity
   thread. It stores sorted exact UUIDs, preserves selected UUIDs that are temporarily unavailable,
   and retains a raw editor. A name is never used for runtime identity or admission.

## Goal

Add a fail-closed ServiceCycle feature that can use owned items without competing with player
control or guessing at game state.

The game has four item families:

| Family | Effect lifetime | Current automation policy |
|---|---|---|
| Scroll | Semi-permanent for the run; buffs an attribute | Use through the game's random-attribute path |
| Relic | Semi-permanent for the run; grants a global improvement | Use first whenever native toxicity headroom permits |
| Fruit | Temporary buff | Exact-item opt-in; use with sufficient headroom and no temporary use pending or active |
| Potion | Temporary buff | Exact-item opt-in; use with sufficient headroom and no temporary use pending or active |

Every use adds toxicity. Toxicity has a cap and a passive recovery rate, and recovery is boosted
during the game's rest period after toxicity has stopped increasing. Auto Items must observe those
native rules; it must not write toxicity, accelerate recovery, edit a save, or emulate rest.

## Original product slice

The first playable Scroll/Relic slice was intentionally narrow:

1. The feature is disabled by default and runs as one ordinary ServiceCycle service when enabled.
2. It considers only positively identified Scrolls and Relics with owned stock.
3. A Scroll is eligible only when the native random-target mode is available and selected for that
   use. The game chooses the attribute; the suite does not reproduce or bias the random selection.
4. A Relic is eligible whenever the game's freshly revalidated readiness and toxicity-headroom
   predicates admit it.
5. Every use must also pass the game's live availability, quantity, cooldown, toxicity-cap, and use
   predicates immediately before mutation.
6. Fruit and Potion facts may be published and diagnosed, but the evaluator emits no action for
   them.
7. At most one item is submitted per bounded feature turn. Relics have priority under the
   recovery-preserving policy above; candidates within one family are ordered by stable UUID until
   benefit-aware policy exists.

"Random" applies to the Scroll's attribute target, not to choosing an inventory item. This avoids
silently turning a native targeting policy into nondeterministic scheduler behavior.

## Safety and ownership

Auto Items inherits the suite's runtime invariants:

- Unity objects, native reads, and native use calls stay on the Unity main thread.
- Worker policy sees immutable, native-free facts only.
- Identity is stable UUID plus the exact expected native type; a display name is diagnostic only.
- Scene, save-load, reset, registry rebuild, and NG+ transitions invalidate all native references
  and prepared actions.
- The game remains authoritative for visibility, progression, stock, cooldown, toxicity, toxicity
  headroom, target eligibility, and the mutation result.
- Emergency stop, configuration disable, lifecycle retirement, or loss of action-family ownership
  cancels prepared work before another use.

Item use owns `ConsumableUse`. The suite claims `NativeMultiBuyOverride` once and shares that
synchronous scoped lease between Auto Buy upgrades and Auto Items, so those suite features do not
conflict with one another. A known external owner revokes the shared gate and blocks both affected
paths without disabling unrelated automation.

Every native use follows capture, execute, capture, verify. The accepted immediate submission
postcondition is one unit leaving stock and one prepared usage entering the native queue while that
queue was idle. This verifies submission, not later effect completion. A no-op, partial change,
thrown call, or unobservable result quarantines Auto Items for the current lifecycle.

## Required discovery

Before mutation code is written, inspect the installed game and record:

1. The exact runtime taxonomy and stable identity evidence that separates Scroll, Relic, Fruit, and
   Potion. `ConsumableSO` facts already exist in the shared world snapshot, but they do not publish
   this four-way classification.
2. Current toxicity, cap, ordinary recovery rate, boosted recovery rate, and the authoritative
   signal for the rest period. Establish their numeric types and zero/cap comparison semantics.
3. The toxicity cost of each item and whether it varies by level, modifiers, family, or current
   state.
4. The native readiness and use entry points for each supported family, including exact overloads,
   return types, cooldown or queue behavior, and thread affinity.
5. The native random-attribute path for Scrolls, including its eligible-target rules and an
   observable record of the selected target.
6. The Relic global-effect state and a reliable postcondition for one successful use.
7. Which semi-permanent effects survive save/load, reset, and NG+, and which lifecycle event removes
   them.
8. Whether manual and automated use can race within one frame, and what live predicate closes that
   race.

Document the accepted findings under `docs/reverse-engineering/` and add installed-game metadata
contracts for every reflected or patched member. Missing or contradictory evidence keeps the
affected family unavailable.

## Runtime shape

### Shared world publication

Extend the existing world collector rather than introducing a feature-owned scan. Publish only the
native-free facts policy needs:

- item classification and stable identity;
- current owned and queued quantity;
- visibility, randomization capability/state, duration class, readiness, and cooldown;
- per-use toxicity cost when the game exposes or authoritatively computes it;
- current toxicity, cap, recovery rate, rest-period state, and boosted recovery rate;
- the minimal active assignment/effect evidence needed to avoid duplicate use and verify results.

Collection completeness must name an unavailable contract. An absent toxicity or classification
fact cannot be replaced with zero, a guessed family, or a permissive default.

### Worker policy

The worker filters to supported families, applies the current exact-item policy, and emits one
advisory action containing stable identity, expected family, expected lifecycle generation, and
the snapshot facts needed for diagnostics. It does not retain native objects or predict recovery.

The toxicity recovery and rest facts are useful for explaining why the feature is waiting, but the
first slice does not schedule a future use from a predicted recovery time. A fresh publication
wakes evaluation when the native state actually changes.

### Main-thread action boundary

Immediately before mutation, resolve stable UUID plus expected type in the current lifecycle and
revalidate:

- feature mode, emergency stop, lifecycle, and mutation-family ownership;
- item classification, visibility, stock, readiness, and cooldown;
- native use eligibility and toxicity headroom;
- random-target mode for a Scroll;
- sufficient native toxicity headroom for the selected item.

Then capture the accepted pre-state, invoke exactly one audited native use, capture the post-state,
and verify the family-specific contract. The adapter must not fall back to a broader overload,
direct field write, save mutation, synthetic attribute choice, or toxicity adjustment.

## Configuration and diagnostics

The initial configuration surface exposed an Auto Items mode and separate Scroll and Relic allows.
Mode defaults to `Disabled`; both supported-family allows default on behind that master switch.

The temporary extension adds separate `UseFruits` and `UsePotions` switches, both defaulting off,
plus an empty-by-default `TemporaryItemAllowlist` of exact stable UUIDs. Enabling a family without
allowlisting an exact item does not make any temporary item eligible.

The Mods page presents that persisted allowlist as a picker. It discovers currently visible
Fruit/Potion items and displays their native name, family, owned count, immediate toxicity cost,
and base duration. The filter cycles through All, Fruit, Potion, Owned, and Selected. Toggling an
item stages a normalized, sorted UUID list. Selected UUIDs not present in the current catalog stay
selected and can be removed explicitly; the Raw editor remains available for diagnostics and
forward compatibility. A picker contract failure disables the discovered list without changing
the staged value.

Feature status and the decision journal should distinguish:

- disabled by configuration;
- progression locked or no owned stock;
- waiting for toxicity headroom;
- waiting for the latched fill-to-recovery cycle;
- Scroll random-target mode unavailable;
- cooldown/native busy;
- lifecycle or ownership unavailable;
- unaudited native contract;
- verified mutation fault.

Do not add a gameplay quick button until the service behavior and configuration path are stable.

## Delivery sequence

1. **Evidence and exact native contracts:** decompile the taxonomy, toxicity/recovery/rest model,
   queue, and native use methods. The static read-only slice completes before publication; the
   type-specific live postcondition probes complete beside the guarded mutation phases they gate.
2. **Publication, taxonomy, and toxicity facts:** extend shared world rows and completeness
   reporting with the minimum accepted facts; add portable binder and publication tests and update
   the world-publication documentation.
3. **Read-only service, configuration, and diagnostics:** register Auto Items in ServiceCycle with
   policy projections, health, and journal decisions, but keep the native adapter unable to submit.
4. **Scroll implementation:** add random-target Scroll use with ownership, live revalidation, exact
   postcondition verification, lifecycle quarantine, native validation, and matching behavior docs.
5. **Relic-first implementation:** give Relics first priority whenever the game's freshly observed
   readiness and toxicity headroom admit them.
6. **Combined validation and hardening:** validate manual races, rest/recovery transitions,
   toxicity cap behavior, save/load, reset, NG+, emergency stop, compatibility quarantine, and the
   complete portable gate.
7. **Temporary-effect evidence and exact contracts:** audit native usage ownership, pending and
   engaged state, duration, expiry, save hydration, and queue behavior. Do not automate from
   serialized benefit graphs that cannot be proven.
8. **Active temporary-effect publication:** publish source item UUID, stable usage UUID,
   pending/engaged state, remaining duration, and maximum duration as one atomic native-free world
   relation. Skip an incomplete usage set rather than publishing a permissive partial view.
9. **Conservative temporary-item policy:** add disabled-by-default Fruit/Potion switches and an
   exact UUID allowlist. Preserve Relic priority, require sufficient native headroom, forbid
   refresh or stacking while any temporary use is pending or active, and latch recovery only after
   no otherwise-eligible item fits.
10. **Guarded Fruit/Potion submission:** reuse the audited one-item native boundary, revalidate the
    exact family and duration live, reject non-toxicity cost vectors, and verify stock, queue, and
    pending-usage postconditions. Track asynchronous engagement and quarantine only the exact item
    when its activation evidence is missing or contradictory.
11. **Temporary-item validation and tuning:** run portable, profiler, source-contract, and
    installed-assembly gates, then complete disposable-save checks for engagement, expiry,
    recovery, manual races, emergency stop, save/load, reset, and NG+.
12. **Temporary-item picker UX:** replace raw UUID entry on the Mods page with an on-demand,
    fail-closed discovered-item picker. Keep stable UUID persistence and a raw fallback; add
    family/owned/selected filters, item details, unavailable-selection preservation, portable
    tests, metadata contracts, and an interactive layout checklist.

Keep publication, permanent-item use, temporary policy, and temporary mutation reviewable as
separate behavior changes. Update the progress checkpoint before and after each phase.

## Verification gates

Portable tests must cover classification failure, missing toxicity facts, cap admission, Relic
headroom gating and priority, native-random Scroll gating, stable ordering, disabled temporary families, exact
allowlisting, active-use blocking, activation and expiry tracking, emergency stop, ownership loss,
lifecycle cancellation, manual-race revalidation, picker discovery/filtering/UUID normalization,
and every ambiguous postcondition.

Real-reference and installed-game contract tests must validate exact types, members, overloads,
return types, taxonomy evidence, and postcondition signals. Interactive validation must then show:

- a Scroll changes only a target selected by the game's random path;
- no Scroll is used when that path cannot be proven;
- a Relic may be used at zero or nonzero toxicity when native headroom permits, receives first
  priority, and is rejected when the native readiness predicate refuses it;
- no item is used when the cap/readiness predicate rejects it;
- recovery and boosted rest recovery remain entirely game-driven;
- Fruit and Potion stock is never consumed by the first slice;
- a temporary item is consumed only when its exact UUID and family are enabled, native headroom
  covers its cost, and no other temporary usage exists;
- a successful temporary submission first appears as one pending usage, later becomes engaged, and
  blocks all additional temporary use until native expiry;
- saturation latches a recovery wait that remains active through partial recovery and clears only
  at exact-zero displayed toxicity;
- missing or contradictory activation evidence quarantines only the submitted item for the
  lifecycle;
- lifecycle transitions and manual item use cannot execute stale prepared work.
- picker labels and filters match visible native Fruit/Potion items, selection survives Apply and
  reopen by UUID, and unavailable selected UUIDs are not silently discarded.

Run `./script/test` on each implementation change. Passing portable tests alone does not establish a
native item-use contract.

## Remaining validation decisions

1. Confirm interactively that the published inverted-resource capacity boundary matches the
   player-visible exact-zero toxicity boundary after complete recovery and through rest.
2. Scroll/Relic mutation ambiguity quarantines the Auto Items adapter for the lifecycle. Temporary
   activation ambiguity quarantines only the exact submitted item; validate both scopes live.
3. Bounded polling is implemented; identify a reliable native toxicity notification only if live
   profiling shows polling is insufficient.
4. After the conservative temporary slice passes live validation, decide whether benefit-aware
   ordering or refresh rules are useful enough to justify additional audited native effect
   contracts.

These items are release gates, not permission to weaken the implemented fail-closed checks.
