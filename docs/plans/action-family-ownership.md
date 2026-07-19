# Action-family ownership isolation

> **Lifecycle: Next beta / runtime validation pending.** Cooperative suite ownership and the exact AutobuyOrb conflict are portable-tested. Desktop coexistence and lifecycle UAT remain release gates.

[Back to plans](README.md)

## Goal

Prevent two automation features from mutating the same native action family while preserving unrelated automation, native queue authority, manual play, and third-party ownership.

## Contract

`OrbModding.Common.ActionFamilyOwnershipRegistry` provides process-local atomic lease sets. A claim either receives every requested family or none. Lease loss is visible through an allocation-free `IsHeld` read, disposal is idempotent, and stale leases cannot release a successor. Known external registrations synchronously revoke overlapping cooperative leases; removing an external registration never silently revives old work.

The registry is advisory. It does not patch, configure, disable, or call another plugin. Unknown plugins that do not register cannot be proven absent. The suite logs this limitation and does not infer conflicts from display names.

## Ownership map

| Feature | Families |
|---|---|
| Auto Buy Structures | Structure purchase |
| Auto Buy Upgrades | Upgrade purchase and native multi-buy override, atomically |
| Auto Cast | Spell cast |
| Auto Concept | Concept assignment |
| Spell Leveling | Spell-level purchase |
| Mentor Spells | Spell mastery-XP grant |
| Mentor Artifacts | Artifact mastery-XP grant |
| Mentor Alchemy | Alchemy mastery-XP grant |

Claims exist only while their feature is configured active and gameplay lifecycle readiness is current. Configuration disable, emergency disable, scene/save/reset/NG+ teardown, component disable, and plugin unload release the relevant claims. Every native mutation checks ownership again after ordinary live admission and immediately before invocation. Multi-step native transactions capture a one-shot permit at that boundary so synchronous hook-driven revocation blocks the next transaction without leaving the current one partially applied. Ownership loss cancels prepared work; Auto Cast may still release an Automata-owned charge hold as safety cleanup.

## Known conflict

The exact BepInEx GUID `IngoH.OrbOfCreation.AutoBuyOrb` is registered conservatively for Structure purchase and native multi-buy override. Therefore Automata Structures and Upgrades stop, while Auto Cast, Auto Concept, Spell Leveling, and Mentor remain independent. The suite never reads or changes AutobuyOrb configuration.

## Verification

Portable tests cover atomic rollback, duplicate owners, independent families, known-external revocation, explicit release/reacquisition, stale leases, exact versus similar GUIDs, final Auto Buy/Auto Cast mutation loss, pending Concept/spell-level cancellation, and independent Mentor-domain cancellation/recovery.

Runtime UAT must verify the exact AutobuyOrb combination, saved-setting toggles, title/save/reset transitions, no removal of native queued/manual actions, conflict tooltips/logs, and supported-package isolation. Invocation-only unknown automation remains an explicit limitation.
