# Auto Items

> **Lifecycle: Scroll, Relic, Fruit, Potion, and Thread implementation complete; combined and interactive
> validation in progress.**

[Plan](../../docs/plans/auto-items.md) |
[Native pipeline evidence](../../docs/reverse-engineering/auto-items-native-pipeline.md)

Auto Items automates exact native Scroll and Relic families and conservatively supports explicitly
allowlisted Fruit, Potion, and Thread UUIDs.

The implemented boundary is deliberately split:

- Common world collection publishes raw `ConsumableSO` scalars, every `ConsumableTypeSO`
  membership, both native resource-cost vectors, and every usage's stable UUID, pending/engaged
  state, and remaining/maximum duration.
- `Policy/AutoItemsConsumableProfileBuilder.cs` maps those neutral facts to the five exact Auto Items
  families. It requires exactly one supported family, a capped inverted toxicity resource, a valid
  immediate toxicity cost, and retains evidence that other native costs exist.
- `ServiceCycle/` registers a bounded one-action service. A pure candidate scanner selects from
  immutable world facts, while the evaluator owns lifecycle, activation, busy, and recovery gates.
  The worker parses the temporary allowlist only when its configuration generation changes and
  stores the sorted identities in Common's audited immutable publication table; mutable collection
  storage never enters service state.
- The main-thread adapter re-resolves exact identity and family, checks visibility, the shared
  Inventory idle predicate, `CanFire()`, lifecycle and ownership, pins multi-buy to one, and verifies
  the exact stock/queue submission edge. Temporary duration and toxicity-only cost vectors are
  revalidated live before mutation.
- Scrolls require live randomization capability and call the game's own
  `SetRandomization(true)`/`IsRandomized()` path. Relics have first priority whenever the native
  readiness and toxicity-headroom checks admit them.
- Fruit, Potion, and Thread controls default off and additionally require an exact UUID in
  `TemporaryItemAllowlist`, a finite positive native duration, toxicity-only cost vectors, enough
  native toxicity headroom, and no other pending or active temporary usage.
- The Mods page provides an on-demand temporary-item picker with native names, family, stock,
  immediate toxicity cost, base duration, and All/Fruit/Potion/Thread/Owned/Selected filters. Selection
  persists as sorted exact UUIDs; unavailable selected identities are preserved and the raw UUID
  editor remains available. Its consolidated rail entry uses the already-audited native Alchemy
  top-bar icon.
- A temporary submission verifies stock, queue, and pending-usage creation immediately, then waits
  for the service-cycle receipt and a later publication to prove engagement. Receipt, activation,
  and exact-item quarantine memory live only in lifecycle-scoped worker state; no lock, mutable
  collection, or native reference crosses the worker boundary. Any pending or active temporary
  usage blocks every automated family—including Relics—through expiry and is rechecked
  immediately before mutation.
  Scrolls and allowed temporary items keep filling toxicity while headroom permits. Once no
  otherwise-eligible item fits, a lifecycle-scoped recovery latch blocks new uses until toxicity
  returns to exact zero, after which ordinary priority resumes. Missing or contradictory activation
  quarantines only that exact item for the lifecycle; ambiguous Scroll/Relic mutations retain
  feature-wide lifecycle quarantine.

Auto Items defaults to `Disabled`; `UseScrolls` and `UseRelics` default on behind it,
`UseFruits`/`UsePotions`/`UseThreads` default off, and the temporary allowlist defaults empty. There is no
gameplay quick button in this slice.
