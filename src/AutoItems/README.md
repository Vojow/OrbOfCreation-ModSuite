# Auto Items

> **Lifecycle: Scroll, Relic, Fruit, and Potion implementation complete; combined and interactive
> validation in progress.**

[Plan](../../docs/plans/auto-items.md) |
[Native pipeline evidence](../../docs/reverse-engineering/auto-items-native-pipeline.md)

Auto Items automates exact native Scroll and Relic families and conservatively supports explicitly
allowlisted Fruit and Potion UUIDs.

The implemented boundary is deliberately split:

- Common world collection publishes raw `ConsumableSO` scalars, every `ConsumableTypeSO`
  membership, both native resource-cost vectors, and every usage's stable UUID, pending/engaged
  state, and remaining/maximum duration.
- `Policy/AutoItemsConsumableProfile.cs` maps those neutral facts to the four exact Auto Items
  families. It requires exactly one supported family, a capped inverted toxicity resource, a valid
  immediate toxicity cost, and retains evidence that other native costs exist.
- `ServiceCycle/` registers a bounded one-action service. The worker emits at most one stable-UUID
  action from immutable world facts and publishes compact decision metrics.
- The main-thread adapter re-resolves exact identity and family, checks visibility, the shared
  Inventory idle predicate, `CanFire()`, lifecycle and ownership, pins multi-buy to one, and verifies
  the exact stock/queue submission edge.
- Scrolls require live randomization capability and call the game's own
  `SetRandomization(true)`/`IsRandomized()` path. Relics have first priority whenever the native
  readiness and toxicity-headroom checks admit them.
- Fruit and Potion controls default off and additionally require an exact UUID in
  `TemporaryItemAllowlist`, a finite positive native duration, toxicity-only cost vectors, enough
  native toxicity headroom, and no other pending or active temporary usage.
- The Mods page provides an on-demand temporary-item picker with native names, family, stock,
  immediate toxicity cost, base duration, and All/Fruit/Potion/Owned/Selected filters. Selection
  persists as sorted exact UUIDs; unavailable selected identities are preserved and the raw UUID
  editor remains available.
- A temporary submission verifies stock, queue, and pending-usage creation immediately, then waits
  for a later publication to prove engagement. It blocks further item automation through expiry.
  Scrolls and allowed temporary items keep filling toxicity while headroom permits. Once no
  otherwise-eligible item fits, a lifecycle-scoped recovery latch blocks new uses until toxicity
  returns to exact zero, after which ordinary priority resumes. Missing or contradictory activation
  quarantines only that exact item for the lifecycle; ambiguous Scroll/Relic mutations retain
  feature-wide lifecycle quarantine.

Auto Items defaults to `Disabled`; `UseScrolls` and `UseRelics` default on behind it,
`UseFruits`/`UsePotions` default off, and the temporary allowlist defaults empty. There is no
gameplay quick button in this slice.
