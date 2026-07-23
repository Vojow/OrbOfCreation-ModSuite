# Automata services

This folder contains independently owned automation services and their explicit composition boundary.

## Registration rules

- `Plugin` constructs each supported service and registers it with `AutomataServiceRegistry` in the deliberate order: Auto Harvest, Auto Buy, Auto Cast, Auto Concept, then Spell Level.
- Registration is explicit. Do not discover services through reflection, filesystem conventions, or static constructors.
- A runtime service implements `IAutomataService`; feature-specific APIs stay on its concrete type and may be retained by `Plugin` for signal handling.
- Registration order is also tick, cancellation, lifecycle-invalidation, and disposal order. Changing it is a runtime behavior change and requires focused ordering tests.
- The registry coordinates lifecycle only. It does not own feature policy, game objects, subscriptions, settings, or cross-feature service location.
- New services need a cohesive folder under `Services`, an explicit registration site, a bounded lifecycle implementation, and tests that prove their position in the ordering contract. The registry uses a preallocated default capacity sized for the planned service portfolio rather than treating the current five services as a permanent ceiling.

## Auto Harvest module

`AutoHarvest` is split by dependency direction:

- `Policy` owns stable pair identity, immutable pair facts and decisions, and fail-closed eligibility.
- `Native` owns immutable reflected contracts, lifecycle-bound bindings, live state reads, and verified native mutation behind an opaque token.
- `Runtime/ServiceCycle` owns immutable cycle state, worker evaluation,
  owner-thread adapters, scheduler admission, feature replay records/codecs,
  feature diagnostics projection, and native lifecycle hooks.
- `Diagnostics` owns stable health projection and typed runtime snapshots.

The neutral Automata ServiceCycle host owns the reusable service definition,
replay decoration and capture lifecycle, pump composition, configuration
publication, and shutdown. Do not add feature-named copies of those facilities.

Keep the `OrbAutomata` namespace until a deliberate API migration is planned; folders describe ownership without changing existing type identity.
