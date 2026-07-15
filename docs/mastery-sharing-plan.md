# Orb Mastery Sharing plan

[Back to project index](../README.md) · [Project roadmap](roadmap.md)

## Goal

Reduce the repetitive work of leveling large collections of spells, artifacts, and alchemy entities by sharing a controlled portion of experience earned through normal play with unlocked entities that are not currently being used.

The working plugin name is **Orb Mastery Sharing**. The final name can change without affecting its configuration identity.

The mod is a catch-up system, not an automatic level setter. It must grant experience through each domain's native progression path wherever possible so the game remains responsible for thresholds, level-ups, caps, observables, effects, and save data.

## Intended behavior

When an eligible source earns experience, the mod calculates a configurable sharing pool for the same domain and distributes it among eligible recipients.

```text
native XP event
  → identify source and domain
  → calculate sharing pool
  → select unlocked, non-maxed, underused recipients
  → distribute with caps and deterministic ordering
  → call native XP/progression path
```

Domains remain separate by default:

- spell experience helps other spells;
- artifact experience helps other artifacts;
- alchemy experience helps other compatible alchemy recipes or entities.

Cross-domain sharing is excluded from the MVP because the domains may use different XP scales and progression meanings.

## Default balance model

The recommended default is **bonus catch-up**: normal source XP is unchanged and an additional pool equal to 10% of the verified native XP gain is distributed to recipients. This reduces loadout rotation without penalizing the item the player chose to use.

The alternative **split** mode preserves the total XP budget by taking the configured share from the source and distributing that amount. It should remain available for players who want stricter balance.

Initial defaults:

- disabled until the domain's native XP hook is verified;
- 10% share in bonus mode once enabled;
- same-domain recipients only;
- unlocked and discovered recipients only;
- exclude the source, active/equipped entities, capped entities, and explicitly blocked UUIDs;
- prioritize the lowest mastery/level, with stable UUID ordering as the tie-breaker;
- distribute to at most five recipients per event;
- apply a configurable per-recipient and per-event cap;
- never grant offline/backfilled XP in the MVP.

These defaults are provisional until representative XP curves are measured. A percentage alone is not sufficient protection if one domain produces unusually large batched XP events.

## Recipient policies

The MVP should support three selection policies:

1. **Lowest first** — concentrate catch-up on the lowest-level eligible entities. Recommended default.
2. **Even spread** — divide the pool among all selected recipients.
3. **Round robin** — rotate through eligible recipients deterministically to avoid permanently favoring the same UUIDs.

Optional per-domain allowlists and blocklists use stable UUIDs, with display names shown only for readability. Undiscovered, locked, invalid, or incompatible entities are never eligible even if their UUID appears in an allowlist.

“Unused” needs a domain-specific definition:

- **Spells:** not the source and not present in an active spell loadout.
- **Artifacts:** not the source and not currently equipped in an active equipment loadout.
- **Alchemy:** not the source recipe/entity and not currently selected or running in an active alchemy instance.

The discovery probe must confirm these definitions against live list variables and loadout state. If active-state detection fails, sharing for that domain fails closed.

## Configuration

Each domain has independent settings:

- enabled;
- bonus or split mode;
- share percentage;
- recipient policy and maximum recipients;
- minimum and maximum recipient level;
- per-event and per-recipient cap;
- include/exclude active entities;
- UUID allowlist and blocklist;
- diagnostic logging.

Global safety settings include an emergency disable, maximum grants per frame, maximum processing time per frame, duplicate-event suppression, and dry-run mode.

The future in-game [mod configuration UI](mod-config-ui-plan.md) should present these settings as one mod page with General, Spells, Artifacts, Alchemy, Eligibility, and Diagnostics categories. The mastery-sharing plugin must remain fully functional without that UI plugin.

## Known static surfaces

The current entity catalog contains promising progression surfaces, but their exact ownership and mutation contracts are not yet verified:

| Domain | Candidate surfaces |
|---|---|
| Spells | `MasteryExperience`, `MasteryLevel`, `SpellTypeXp`, `SpellMasteryRate`, `SpellExperienceRate`, `SpellMasteryXpScaling` |
| Artifacts/equipment | `ArtifactExperience`, `ArtifactLevel`, `EquipmentExperienceRate`, `TotalEquipmentMastery` |
| Alchemy | `AlchemyMasteryXp`, `AlchemyMasteryLevel`, `AlchemyLevel`, `AlchemyXpRate`, `AlchemyLeveling`, `AlchemyXpReq` |

These names prove that related assets and modifiers exist, not that directly mutating them is safe. Implementation begins with an IL and logging-only runtime probe of actual XP gain call sites.

## Safety invariants

- Never directly write a level when a native XP grant path exists.
- Never share XP generated by this mod; tagged/re-entrant grants must not recursively produce more sharing.
- Never grant to locked, undiscovered, capped, destroyed, or unresolved entities.
- Never identify an entity by display name alone.
- Never modify source XP in bonus mode.
- In split mode, preserve the measured source-plus-shared total within numeric tolerance.
- Clamp invalid configuration and reject negative, NaN, infinite, or overflowing grants.
- Do not write save files directly; rely on registered runtime objects and normal saving.
- Disable only the affected domain after a hook, catalog, or native grant failure.

## Delivery stages

### S0 — Experience-system audit

- Trace native XP gain and level-up methods for representative spells, artifacts, and alchemy entities.
- Record argument types, modifiers, batching, observables, caps, and save ownership.
- Identify loaded/unlocked collections and active/loadout collections for each domain.
- Determine whether alchemy progression belongs to recipes, types, instances, or more than one layer.

Exit criterion: each candidate domain has a documented source hook, recipient catalog, native grant method, and re-entrancy strategy—or is explicitly deferred.

### S1 — Logging-only event probe

- Patch the narrowest verified native XP boundary.
- Log source UUID/type, raw XP, final XP, level before/after, and active state.
- Confirm one logical action produces the expected number of events.
- Detect batched and recursive events without changing progression.

Exit criterion: logs match visible progression for representative low-, mid-, and high-level entities without duplicate logical events.

### S2 — Spell vertical slice

- Implement dry-run recipient selection and projected grants.
- Add guarded native grants, recursion suppression, caps, and deterministic ordering.
- Validate bonus and split modes over save/load and loadout changes.

Exit criterion: unused unlocked spells gain exactly the configured amount, active/source XP follows the chosen mode, and native level-up behavior remains intact.

### S3 — Artifacts

- Add artifact catalogs, equipped-state exclusion, and artifact-specific XP scaling safeguards.
- Verify created/upgraded artifact identity remains stable across save/load.

Exit criterion: only eligible unequipped artifacts receive grants and no equipment stats or instances are duplicated.

### S4 — Alchemy

- Implement the verified recipe/type/instance progression layer from S0.
- Exclude selected and active alchemy work and prevent completion-event duplication.

Exit criterion: eligible inactive alchemy entities receive correct XP without changing ingredients, outputs, slots, timers, or active instances.

### S5 — UI and release integration

- Expose all settings cleanly to the optional in-game mod configuration panel.
- Add concise grant summaries and optional detailed diagnostics.
- Complete compatibility testing with Chronomancer, Automata, and Achievement Resonance.

Exit criterion: extended-session tests show bounded grants, stable saves, no recursive XP, and no requirement for the configuration UI plugin.

## Verification matrix

Required scenarios include:

- low, mid, capped, locked, and newly unlocked recipients;
- source level-up and recipient level-up in the same event;
- rapid repeated casts/actions and a large batched XP award;
- loadout/equipment/alchemy selection changes during play;
- bonus mode, split mode, dry-run, emergency disable, zero share, and maximum configured share;
- save/load, scene change, reset/prestige boundaries, and plugin removal;
- 1× and accelerated Chronomancer modes;
- empty allowlists, stale UUIDs, all recipients capped, and no eligible recipient;
- coexistence with global XP modifiers and Achievement Resonance progression bonuses;
- proof that mod-generated grants cannot recursively produce further grants.

## Open questions

1. Do spells gain both per-spell mastery XP and spell-type XP from the same action?
2. Is artifact XP owned by the definition, a created artifact instance, or both?
3. Which alchemy object actually owns level and mastery XP: recipe, type, active instance, or player-global state?
4. Are XP values granted before or after the game's experience-rate modifiers at the safest hook?
5. Which resets preserve each domain's XP and stable identity?
6. Should maxed sources still create a catch-up pool, or only XP that actually advances the source?
