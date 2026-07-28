# Automata services

This folder contains the explicit composition boundary for the independently owned automation
services, which live in sibling feature folders under `src/`.

## Registration rules

- `Plugin` registers one shared ServiceCycle activation with `AutomataServiceRegistry`; feature order is explicit inside `IAutomataServiceCycleFeature[]`: world collection first, then Auto Harvest, Auto Buy, Spell Leveling, Auto Cast, and Auto Concept.
- Registration is explicit. Do not discover services through reflection, filesystem conventions, or static constructors.
- The activation implements `IAutomataService`; feature-specific APIs stay inside their typed ServiceCycle composition and diagnostics bridges.
- Registration order is also tick, cancellation, lifecycle-invalidation, and disposal order. Changing it is a runtime behavior change and requires focused ordering tests.
- The registry coordinates lifecycle only. It does not own feature policy, game objects, subscriptions, settings, or cross-feature service location.
- New services need a cohesive feature folder under `src/`, an explicit registration site, a bounded lifecycle implementation, and tests that prove their position in the ordering contract. The registry uses a preallocated default capacity sized for the planned service portfolio rather than treating the current five services as a permanent ceiling.

## Auto Harvest module

`src/AutoHarvest` is split by dependency direction:

- `Policy` owns stable pair identity, immutable pair facts and decisions, and fail-closed eligibility.
- `Native` owns immutable reflected contracts, lifecycle-bound bindings, live state reads, and verified native mutation behind an opaque token.
- `Runtime/ServiceCycle` owns immutable cycle state, worker evaluation,
  owner-thread adapters, scheduler admission, feature diagnostics projection,
  and native lifecycle hooks.
- `Diagnostics` owns stable health projection and typed runtime snapshots.

The neutral Automata ServiceCycle host owns the reusable service definition,
pump composition, configuration publication, and shutdown. Do not add feature-named copies of those facilities.

Keep the `OrbAutomata` namespace until a deliberate API migration is planned; folders describe ownership without changing existing type identity.
