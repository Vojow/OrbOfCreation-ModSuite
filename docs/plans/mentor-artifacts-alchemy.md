# Orb Mentor: artifacts and alchemy design

> **Lifecycle: Beta; extended interactive validation pending.** Independent artifact and alchemy domains are released, default disabled, and still require the interactive gates below before stable promotion.

[Back to plan index](README.md) · [Orb Mentor plan](mentor.md) · [Project roadmap](roadmap.md)

## Purpose

This document records the design and current beta implementation that extended Orb Mentor beyond spells without changing the spell contract. Each progression domain remains independently configurable and fails closed. Shared Pool remains the default economy, fresh artifact and alchemy switches start disabled, and all grants use or faithfully complete the domain's native persistence and level-up path.

The game calls artifacts `EquipmentSO`; this document uses **artifact** for player-facing text and **equipment** for native API names.

## Common product model

Each enabled domain ranks its own discovered definitions by current mastery level. Every definition tied at the highest level is a mentor. A positive native XP event earned by a mentor creates sharing XP for discovered definitions at a strictly lower mastery level.

Domains never share across boundaries: spell XP mentors spells, artifact XP mentors artifacts, and alchemy XP mentors alchemy recipes. Source XP is never changed. Mod-generated grants are guarded against recursion.

The existing economy formulas remain unchanged:

- **Shared Pool:** each eligible recipient receives `(source XP × share percent) / recipient count`.
- **Per Recipient:** each eligible recipient receives `source XP × share percent`.

Configuration provides independent `Spells`, `Artifacts`, and `Alchemy` enable switches and percentages. The compact Mentor button remains the plugin-wide Disabled/Active control. Its tooltip summarizes each domain and identifies any domain-specific blocked reason.

Each configured domain also follows native progression availability independently. The global `MasteriesEnabled` `ViewSO` and the domain's exact `WorkshopArtifact` or `ScreenAlchemy` `ViewSO` must both report `IsAvailable()` before Mentor starts any catalog, relationship, planning, or grant work for that domain. A locked domain reports waiting rather than an error, preserves its saved switch and percentage, and can unlock later in the same session. Lifecycle resets cancel pending domain work and re-evaluate the views.

## Alchemy vertical slice

### Verified native ownership

- Source registry: `AlchemyRecipeSO.All`; the Mentor catalog retains only recipes classified as ordinary alchemy by the shared UUID-and-native-type domain classifier. Scholar concepts are excluded.
- Stable identity: inherited GUID.
- Eligibility: `AlchemyRecipeSO.IsAvailable()`. This covers both manually discovered recipes and natural/prerequisite recipes whose native `discovered` field may remain false.
- XP and confirmed mastery owner: `AlchemyRecipeSO.masteryXp` and `masteryLevel`.
- Native grant path: `AlchemyRecipeSO.GainMasteryXp(BigDouble)`.
- Persistence: `AlchemyRecipeSO.AlchemyRecipeSaveData`.
- Type progression: `GainMasteryXp` increases mastery immediately and `ApplyMastery()` adds the resulting level to every related `AlchemyTypeSO`.
- XP producers include continuous recipe activity through `AlchemyRecipeSO.Increment(float)` and completion XP through `AlchemyInstance.CompleteRecipe()`.

### Implemented contract

- Hook a postfix on `AlchemyRecipeSO.GainMasteryXp(BigDouble)` and observe the final positive argument.
- Ignore XP callbacks from verified Scholar concepts. Unknown or contradictory domain evidence blocks Alchemy sharing for the lifecycle instead of guessing.
- Include all successful native alchemy XP events, whether continuous or completion-based.
- Rank all available ordinary-alchemy recipes globally by `masteryLevel`; do not require a recipe to remain active after it has been discovered. A higher-level Scholar concept therefore cannot displace the ordinary-alchemy mentor tier.
- Grant through the same `GainMasteryXp` method inside the domain recursion guard.
- Revalidate shared-classifier ordinary-alchemy evidence immediately before the native grant.
- Accept native immediate recipient mastery level-ups and related `AlchemyTypeSO` progression. Never call `ApplyMastery`, `IncrementLevel`, or type methods separately.
- Re-evaluate recipient eligibility immediately before delayed grants because a recipe may level automatically while work is queued.
- Aggregate continuous events per frame before applying the existing operation and CPU budgets.

### Alchemy verification gate

- Continuous and completion events each produce one observed final-XP event.
- A completion that represents multiple extracted completions shares the single final multiplied amount exactly once.
- Shared Pool conservation and Per Recipient multiplication hold across discovered recipes.
- Recipient mastery and every related alchemy type advance exactly as they do after a native grant.
- Locked recipes, equal/higher mastery recipes, and the source never receive XP.
- Active instance creation, quantity, selection, recipe time, advancement, costs, and completion effects are untouched.
- Save/load and reset invalidate pending recipe references without losing native source progress.
- Save/load, scene, reset, and NG+ lifecycle signals also invalidate the classifier snapshot; disabled Alchemy sharing does not initialize it.

## Artifact vertical slice

### Verified native ownership

- Catalog: `EquipmentSO.All`.
- Stable identity: inherited GUID.
- Eligibility: `EquipmentSO.IsCreated()` / `IsDiscovered()`.
- XP and mastery owner: `EquipmentSO.masteryXp` and `masteryLevel`.
- Persistence: `EquipmentSO.EquipmentSaveData`.
- Native XP production occurs only for equipped, fully attuned equipment inside private `IncrementActive(double)`.
- `IncrementActive` grants directly to a private `ExperienceContainer`, checks gained levels, invokes private `GainMasteryLevels(int)`, and synchronizes `masteryXp`.
- Artifact mastery levels automatically apply to the player's total equipment mastery. There is no public `EquipmentSO.GainMasteryXp` equivalent.

### Safety consequence

Artifact grants must not ship by writing `masteryXp`, changing `masteryLevel`, or invoking only `ExperienceContainer.GainExperience`. Any of those partial paths can desynchronize the container, saved field, native level-up effects, or total equipment mastery.

### Development adapter design

I0 adds an installed-game contract fixture and a development-only adapter probe that resolves:

1. the recipient's initialized `ExperienceContainer`;
2. its native `GainExperience`, gained-level, and current-experience operations;
3. `EquipmentSO.GainMasteryLevels(int)`;
4. synchronization of `EquipmentSO.masteryXp` after the container operation.

The adapter executes the same ordered state transition as native `IncrementActive`: gain container XP, calculate gained levels, apply those levels through the native equipment method, then copy the container's current experience to the saved field. It must fail closed before mutation if every member is not resolved against the supported assembly.

The beta adapter is allowed to operate only after its complete reflected contract resolves. Automated differential tests cover identical starting-state and XP transitions, but stable promotion still requires interactive evidence that XP, mastery level, total-equipment-mastery modifier, notifications, and serialized save values match native equipped progression. If the runtime contract is incomplete, Artifacts remains unavailable/blocked before mutation.

### Artifact event capture

- Patch private `EquipmentSO.IncrementActive(double)` with prefix/postfix state capture.
- Derive the final positive native XP earned from the experience state delta, including mastery threshold crossings, rather than recomputing the game's rate formula.
- Reject mod-guarded calls and non-positive or invalid deltas.
- Rank created artifacts globally by current `masteryLevel`.
- Created but unequipped artifacts are eligible recipients once the adapter gate passes; Mentor never equips or attunes them.
- Revalidate creation and lower mastery immediately before each delayed grant.

### Artifact verification gate

- No event while unequipped, attuning, paused, or otherwise not receiving native XP.
- Exactly one event for each positive equipped progression delta at normal and accelerated speeds.
- Threshold-crossing deltas are measured correctly even though mastery auto-levels during the source call.
- Adapter output is differential-equivalent to native progression for zero, one, and multiple gained levels.
- Native sounds, logs, total equipment mastery, saved XP, and saved level remain consistent.
- Equipping, quantities, usage costs, attunement, effects, type slots, and creation state are untouched.
- Reset/load cancels pending work and invalidates cached container/member references.

## Implementation order

### E0 — Domain-neutral engine

Extract the existing recipe-specific eligibility data into a domain-neutral mastery entry while preserving deterministic UUID ordering, economy formulas, consolidation, and cancellation. Keep spells tests as regression coverage.

### A1 — Alchemy contracts and engine tests

Add installed-game metadata contracts and stubs for the alchemy catalog, discovery, fields, save data, native grant path, related types, and completion caller. Add pure tests for independent domain settings and alchemy automatic-level revalidation.

### A2 — Alchemy runtime

Add the guarded postfix, catalog resolver, per-frame accumulator, native grants, lifecycle cancellation, tooltip/config reporting, and detailed diagnostics. Validate continuous and completion XP in game.

### R1 — Artifact contracts and probe

Add contracts for the equipment catalog, creation state, container, private native progression sequence, save fields, and total mastery effect. Build a development-only event/differential adapter probe. Do not expose artifact grants publicly at this stage.

### R2 — Artifact runtime

Only after R1 differential evidence passes, enable the guarded adapter, scheduler integration, tooltip/config reporting, and runtime validation matrix. Otherwise leave the domain blocked with a precise reason.

### H1 — Hardening

Test all three domains independently and together at normal and accelerated game speeds, with and without supported sibling plugins. Validate clean install, upgrade, save/load, reset, disable, removal, and reinstall. Public configuration remains Disabled/Active only.

## Decisions intentionally deferred to evidence

- Whether artifact sharing can safely include created but never-equipped recipients depends on adapter initialization evidence.
- Whether continuous alchemy/artifact event volume needs time-window aggregation beyond the existing per-frame accumulator depends on runtime profiling.
- Domain percentages may initially share the spells default of 10%; changing that default requires balance evidence, not API evidence.

## Implementation status

The beta includes independent Alchemy and Artifacts switches, guarded hooks, per-domain aggregation and queues, round-robin budgeting, alchemy native grants, and the artifact native-sequence adapter. Continuous artifact and alchemy events reuse one catalog snapshot per frame and distribute consolidated XP at most four times per second to avoid reflection churn and native-notification storms. Both domains default disabled. Automated contract and adapter tests pass on the current supported tree; the interactive gates above remain mandatory before either domain is described as stable.
