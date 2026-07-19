# Orb Mentor plan

> **Lifecycle: Beta; extended interactive validation pending.** Equipped-source and highest-only spell policies, capture-time recipient evidence, source-specific mastery ceilings, shared scheduling, and independently enabled artifact/alchemy extensions are released.

[Back to project index](../README.md) · [Project roadmap](roadmap.md)

## Goal

Reduce repetitive mastery work by sharing native XP within a progression domain. For spells, every equipped spell can share native mastery XP with discovered spells below that source's own mastery. `EquippedSpells` is the default source policy; `HighestDiscovered` preserves the original highest-confirmed-mastery behavior. Optional artifact and alchemy domains follow separately audited native contracts. Orb Mentor does not edit saves, change loadouts, or spend leveling resources.

The next-beta [action-family ownership contract](action-family-ownership.md) claims spell, artifact, and alchemy XP grants independently. A conflict cancels only that domain's captured, planned, parked, and pending XP; healthy siblings continue and the root health becomes degraded instead of globally blocked.

Orb Mentor is a separate plugin rather than an Automata module. Automata owns scheduled player actions, while Mentor reacts to progression events and grants bonus progression. The separation lets players install either behavior independently and isolates game-update failures. Both plugins still share Orb Mod Config, common utilities, visual conventions, assembly auditing, and release tooling.

The next-beta [feature-health contract](feature-health-reporting.md) reports the Mentor root and each progression domain separately. Root degradation reflects a failed optional domain only when another configured domain remains operational; it never converts that sibling into a failure or changes pending XP work.

Automatic spell leveling and its native resource spending are explicitly outside Orb Mentor. The beta implements that behavior under Automata's Auto Buy feature.

## Original spell MVP contract

The original public vertical slice was spells-only because artifact and alchemy XP ownership and active-instance rules differ. Those later domains are now released as disabled-by-default beta extensions; see [Mentor artifacts and alchemy](mentor-artifacts-alchemy.md) for their contracts and remaining validation gates.

### Mentor qualification

- With `EquippedSpells` (default), every spell present in the cached native active loadout qualifies as a source for that event.
- With `HighestDiscovered`, every spell tied at the highest confirmed mastery level qualifies as a mentor.
- A qualifying spell creates sharing XP only when the game awards it a positive native mastery-XP event.
- Instant, channelled, toggled, and any other verified native spell-mastery events qualify.
- Orb Mentor never equips, selects, creates, casts, or otherwise activates a spell.
- There is no assumed maximum mastery level; the installed game path has no explicit mastery cap.

### Recipient eligibility

A recipient must:

- be a registered `SpellRecipeSO`;
- be discovered/unlocked;
- not be the source;
- have a strictly lower confirmed mastery level than the capture-time source mastery ceiling (`EquippedSpells`) or highest mentor level (`HighestDiscovered`).

Active/loadout spells remain eligible. A spell that is already ready to confirm mastery also remains eligible and may continue banking XP, matching native behavior. Locked, undiscovered, unresolved, or equal/higher-level spells never receive sharing XP.

### XP source and type progression

Sharing is calculated from the final positive XP passed to `SpellRecipeSO.GainMasteryExp`, after the game's spell, type, player, and other native modifiers have already been applied.

Orb Mentor grants only per-spell mastery XP. It never calls `SpellTypeSO.GainTypeXp` directly. Native spell-type XP continues to be awarded by `SpellRecipeSO.PurchaseLevel()` when the player confirms each recipient's mastery. This means a Firebolt event mentors individual lower-level spells without directly granting Cantrip, Evocation, or other type XP.

### Economy modes

`SharePercent` defaults to 10% and is clamped to the finite range 0–100%.

1. **Shared Pool** — default. For a batch containing final mentor XP `X`, sharing percentage `p`, and `N` eligible recipients, each recipient receives `(X × p) / N`. Total bonus XP is bounded to `X × p`.
2. **Per Recipient** — advanced. Every eligible recipient receives `X × p`. Total bonus XP scales with the number of recipients and is intentionally uncapped; the UI must warn about this.

If no recipient is eligible, no bonus is created or banked. Switching modes or percentages applies live.

## Verified installed-game architecture

Static inspection was repeated against the supported `Assembly-CSharp.dll` baseline:

- SHA-256: `5845797D40E4631517DE9F4D6296F10C7381AAD5DA733128B2C4685E66E8711F`
- ILSpy command-line version used: `10.1.0.8386`

### Native domain-unlock contract

Mentor treats mastery progression and each feature domain as separate native gates. `UITooltip.RenderXpBar()` uses its serialized `masteryEnabledView.IsAvailable()` result to hide ordinary mastery progression, and `ViewSO.IsAvailable()` delegates to the view's native prerequisites. `IdScriptableObject.RuntimeLookup` provides exact UUID-to-native-object resolution after deserialization.

The audited serialized `ViewSO` identities are:

- `MasteriesEnabled`: `07dfae7e-76b9-4b38-bf81-38abc40b9ed7`;
- `MagicSpellbook`: `ca934900-0253-4f71-93e9-733fb91132b7`;
- `WorkshopArtifact`: `668a2a7a-468f-4e0e-b182-979b12a4b0ad`;
- `ScreenAlchemy`: `3ae45ec0-4449-4903-b3d0-b5182e03dca3`.

Names are diagnostics only. The runtime requires exact UUID plus `ViewSO` type and requires both `MasteriesEnabled.IsAvailable()` and the applicable domain view's `IsAvailable()` before that domain can catalog, rank, plan, grant, or inspect domain-specific tooltip data. Missing registry entries remain in a non-error waiting state because native objects can register later. Wrong type, missing accessor, or contradictory return contracts fail closed with a precise permanent reason. Lifecycle invalidation cancels pending work and forces a fresh gate read without rewriting configuration.

### Native event and grant path

```text
Spell action
  → Spell.CalculateExecuteExperience() / held-experience calculation
  → Spell.ModExperience(...)
  → SpellRecipeSO.GainMasteryExp(finalXp)
  → ExperienceContainer.GainExperience(finalXp)
  → masteryExperience saved by SpellRecipeSO
```

Relevant verified behavior:

- `Spell.ModExperience(...)` applies spell level, runtime spell modifiers, spell-type modifiers, `Player.GetSpellExperienceRate()`, and the recipe XP modifier before the recipe receives XP.
- Manual spell execution calls `reference.GainMasteryExp(finalXp × SpellMasteryRate)`.
- Channelled/held casting also calls the same recipe method with already-calculated positive XP.
- `SpellRecipeSO.GainMasteryExp(BigDouble)` stores XP and produces the native mastery-ready notification when the threshold is crossed.
- `SpellRecipeSO.All` is the registered recipe catalog; `IsDiscovered()` is the native availability predicate.
- `SpellRecipeSO.SpellRecipeSaveData` owns discovered state, mastery XP, mastery level, and discovery level. Native save collection therefore persists Mentor grants without custom save data.
- `ExperienceContainer` supports continued XP accumulation beyond the current threshold.
- `SpellRecipeSO.PurchaseLevel()` consumes one native mastery level and then grants each related spell type its normal fixed type XP.

The correct interception boundary is a Harmony postfix on `SpellRecipeSO.GainMasteryExp(BigDouble)`. A postfix observes only successfully completed native grants. Mod-generated recipient calls use the same method under a re-entrancy guard, so they retain native storage and notifications without recursively producing new sharing batches.

## Runtime algorithm

```text
postfix GainMasteryExp(source, finalXp)
  → reject when disabled, guarded, non-finite, zero, or negative
  → use cached source identity, progression evidence, and equipped membership
  → reject unless source satisfies the selected source policy
  → add finalXp to the current-frame mentor accumulator

LateUpdate / bounded worker
  → snapshot recipients strictly below the capture-time source ceiling
  → calculate Shared Pool or Per Recipient amounts
  → consolidate pending XP by stable recipient UUID plus source ceiling
  → process grants within CPU and operation budgets
  → call recipient.GainMasteryExp(amount) inside guard
```

Stable UUIDs are used for deterministic ordering and diagnostics, never display names. If multiple tied mentors earn XP in one frame, their final XP is combined before distribution. No amount is lost when a large batch crosses a frame boundary.

Pending work is discarded when Mentor is disabled, emergency-blocked, the gameplay manager resets, a scene/load invalidates object identity, or the recipient can no longer be resolved. Native source XP is never changed.

## Player controls and configuration

Fresh installations start disabled.

### General

- `Enabled`: plugin-wide enable switch.
- `Mode`: `Disabled` or `Active`; no public DryRun mode.
- `ToggleShortcut`: configurable BepInEx shortcut, default `Alt+M`.
- `EmergencyDisable`: immediately rejects new events and clears pending sharing work.

### Sharing

- `EconomyMode`: `SharedPool` or `PerRecipient`.
- `SpellSourcePolicy`: `EquippedSpells` (default) or `HighestDiscovered`.
- `SharePercent`: 0–100, default 10.

### Performance

- bounded recipient-grant operations per frame;
- a small unscaled CPU-time budget;
- consolidated per-recipient pending XP so delayed work does not create repeated calls.

Performance limits delay grants but never silently reduce calculated amounts. Conservative defaults should handle the full discovered spell catalog without a visible frame stall.

### Diagnostics

- normal logs contain startup, enable/disable transitions, blocked state, warnings, and errors;
- detailed source, mentor, batch, recipient, and amount logs are opt-in;
- logging-only probes are development builds/settings and are not exposed as a public operating mode.

## In-game control

Orb Mentor has a compact queue/status-area button beside the existing Auto Cast control. It remains independently usable when Automata is not installed.

- Click toggles Active/Disabled.
- `Alt+M` performs the same action.
- Visual states: `ON`, `OFF`, `WAITING`, and `BLOCKED`.
- Tooltip: economy mode, percentage, current tied mentor names/count, eligible-recipient count, and the reason for a blocked state.
- The compact surface uses an appropriate native spell/mastery icon; detailed text stays in the tooltip.

Changing mode, percentage, shortcut, or enable state through Orb Mod Config applies live. Orb Mentor remains functional through its BepInEx configuration when Orb Mod Config is absent.

## Safety invariants

- Never modify or subtract source XP.
- Never directly set XP fields, levels, or save JSON when the native grant path is available.
- Never call spell-type XP methods from a sharing event.
- Never share XP produced by Orb Mentor itself.
- Never grant to a non-discovered, unresolved, equal-level, or higher-level recipe.
- Never identify a recipe by display name alone.
- Reject negative, NaN, infinite, overflowing, or otherwise invalid values.
- Preserve native mastery-ready sounds, popups, and logs in the MVP, even when several recipients cross thresholds together.
- Revalidate recipients immediately before a delayed grant.
- Fail closed and show `BLOCKED` when the hook, catalog, lifecycle, or numeric contract is unavailable.
- Show `WAITING` without catalog or loadout discovery while the native mastery or domain view remains locked; unlock promptly when both audited views become available.
- Do not store progression state outside native objects. Plugin removal leaves an ordinary game save containing only XP the game already knows how to serialize.

## Implementation iterations

### I0 — Contract fixtures and development probe

- Add installed-game contract tests for the exact `GainMasteryExp`, catalog, discovery, mastery-level, save, and purchase/type-XP surfaces.
- Add game-stub equivalents without copying game implementation.
- Build a logging-only development probe for source recipe UUID/name, final XP, confirmed mastery, ready state, event frequency, and lifecycle transitions.
- Validate instant, channelled, and toggled spells at normal and accelerated game speed.

Exit gate: every visible native mastery gain produces one expected event with the same final XP shown by native progression; reset/load does not leave stale objects.

### I1 — Pure Mentor engine

- Implement mentor qualification, recipient eligibility, both economy formulas, stable ordering, consolidation, invalid-number rejection, and pending-work cancellation as game-independent code.
- Cover tied mentors, no recipients, all-equal levels, ready recipients with banked XP, live config changes, and large `BigDouble` values.
- Define the operation and CPU budgets without Unity dependencies.

Exit gate: deterministic unit tests prove conservation in Shared Pool mode and exact per-recipient multiplication in Per Recipient mode.

### I2 — Native spell vertical slice

- Add the guarded Harmony postfix and lifecycle-owned recipe resolver.
- Begin with a development-only constrained runtime fixture, then enable the complete discovered catalog.
- Grant through `SpellRecipeSO.GainMasteryExp` and prove re-entrancy suppression.
- Verify native save/reload, mastery-ready alerts, continued banking, manual/bulk confirmation, and type XP awarded only on confirmation.

Exit gate: recipients gain exactly the planned XP, source XP is unchanged, no recursive batch appears, and type XP does not change until native mastery confirmation.

### I3 — Scheduling and controls

- Add per-frame aggregation, per-recipient consolidation, budgeted cross-frame processing, `Alt+M`, and pending-work cancellation.
- Add the queue/status-area button with ON/OFF/BLOCKED states and diagnostic tooltip.
- Integrate the typed settings into Orb Mod Config with Shared Pool first and a clear Per Recipient warning.

Exit gate: large recipient sets and channelled spells remain smooth; toggling off discards pending bonus work immediately; UI survives native tab and scene transitions.

### I4 — Public beta hardening

- Run extended sessions at normal and accelerated game speeds.
- Validate with and without Automata and Orb Mod Config.
- Confirm normal logs remain quiet and detailed logs are sufficient to diagnose discrepancies.
- Package a standalone Orb Mentor archive and an optional complete ModSuite archive.
- Publish only Disabled/Active behavior; retain probe instrumentation for development builds.

Exit gate: clean install, upgrade, save/load, reset, disable, plugin removal, and reinstall all pass without custom save repair.

## Verification matrix

Required automated and runtime scenarios:

- one mentor, several tied mentors, mentor changes, and no discovered mentor;
- recipients at lower, equal, and higher confirmed mastery;
- discovered versus locked recipes;
- active and inactive recipients;
- ready-to-level recipients banking multiple future levels;
- instant, channelled, toggled, rapid, and large batched XP events;
- Shared Pool at 0%, 10%, and 100%, including conservation tolerance;
- Per Recipient at 0%, 10%, and 100%, including collection-size scaling;
- enable/disable, `Alt+M`, button toggle, emergency disable, and live configuration changes;
- pending work spanning frames and invalidated during reset/load;
- source and recipient crossing thresholds during the same frame;
- native mastery confirmation and spell-type XP occurring exactly once per confirmed recipient level;
- save/load, scene changes, reset/prestige boundaries, and plugin removal;
- normal and accelerated game speeds;
- native final-XP modifiers;
- proof that mod-generated XP cannot recursively produce additional sharing.

## Release and ownership

Current identity:

- Display name: `Orb Mentor`
- Assembly/project: `OrbMentor`
- Plugin GUID: `dev.vojow.orbofcreation.mentor`
- Current beta version: `0.3.0`

Runtime dependency: `OrbModding.Common`. Orb Mod Config is optional. Automata is a compatible sibling plugin, not a dependency.

## Deferred work

- Aggregated replacement for native mastery-ready popup spam: only if runtime testing shows it is necessary.
- Per-type mentors: after the global spell model is stable and native multi-type membership is fully tested.
- Production promotion for artifact and alchemy mentoring: after their interactive native-progression, lifecycle, save, and performance gates pass.

## Remaining probe questions

These are implementation-audit tasks, not unresolved product decisions:

1. Does any non-casting native system call `SpellRecipeSO.GainMasteryExp`, and should its positive XP be included under the agreed “all native events” rule?
2. Which lifecycle callback most reliably invalidates pending recipe references during reset/load?
3. What operation count and CPU budget keep the largest discovered catalogs across all enabled domains smooth at the supported game-speed range?
