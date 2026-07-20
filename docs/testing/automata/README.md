# Orb Automata testing

[Testing hub](../README.md) · [Automata behavior reference](../../../src/OrbAutomata/README.md) · [Runtime protocol](../runtime-validation.md)

Orb Automata has several independent native action families and a shared
coordinator. Start with the feature being changed, then include integration
evidence whenever configuration, lifecycle, scheduling, queue capacity, status,
or action-family ownership is affected.

## Feature guides

- [Auto Buy](auto-buy.md) — candidate decisions, reserves, grouping,
  continuation, lifecycle, queue safety, and progression-shaped performance.
  Its [negative simulation plan](auto-buy-negative-simulations.md) owns invalid,
  race, seeded, and adverse-performance scenarios.
- [Auto Cast](auto-cast.md) — loadout discovery, charges, costs, targeting,
  mutation verification, and controls.
- [Auto Concept](auto-concept.md) — catalog classification, slot policy,
  mastery balancing, drain, and lifecycle behavior.
- [Spell leveling](spell-leveling.md) — capability unlocks, single/all modes,
  costs, queueing, and completion semantics.
- [Automata integration](integration.md) — configuration, coordinator,
  ownership, feature health, Harmony bindings, and cross-feature scheduling.

## Required progression

For an isolated feature change:

1. Run the feature’s focused component tests.
2. Run its headless integration/E2E scope.
3. Run `Fast`.
4. Run `AutoBuyPerformance` or `PerformanceAll` when scheduling, coordinator,
   invalidation, or hot-path work changed.
5. Run installed contracts when a reflected member, Harmony target, native type,
   UUID/type assumption, or mutation postcondition changed.
6. Complete proportional Automata V3/V4 UAT for packaged runtime behavior.

Changes to the shared coordinator, lifecycle generation, queue capacity,
invalidation bus, or action ownership are not isolated feature changes. They
also require [suite integration](../suite-integration.md).

## Cross-feature invariants

- Emergency disable prevents new native mutations immediately.
- Failure or quarantine in one action family does not stop healthy siblings.
- Every prepared mutation is generation-valid and rechecks ownership and live
  native facts immediately before execution.
- Queue reservations are applied once and total native capacity remains
  authoritative.
- Native multi-buy state is restored on every Upgrade path.
- Disabled features do not scan or rebuild catalogs in the background.
- No test may treat display name, registry presence, or `IsAvailable()==false`
  as sufficient identity or completion evidence.
