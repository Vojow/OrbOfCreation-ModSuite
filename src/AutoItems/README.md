# Auto Items

Auto Items is the disabled-by-default Scroll and Relic automation service compiled into
`OrbModSuite.dll`. It has no independent plugin identity, cadence, quick control, or configuration
store.

The worker consumes immutable shared-world consumable facts, requires exactly one supported native
family, gives eligible Relics priority over Scrolls, and plans at most one action per publication.
All evaluator paths wait for another world or configuration publication; the engine prevents a
second attempt against the same world reading.

`AutoItemsConsumableUseGameAction` owns the native boundary. It validates its complete reflection
schema at lifecycle scope, re-resolves stable UUID plus exact type, rechecks family, visibility,
native inventory idleness, `CanFire()`, Scroll randomization and live targeting, captures ownership
permits, submits through `SelectAndFire()` under native multi-buy quantity one, and verifies exact
stock/queue evidence. An ambiguous attempted mutation quarantines the whole action until lifecycle
replacement and carries its exact reason through the receipt and feature health.

Configuration is additive and uses the suite's committed publication:

- `AutoItems.Mode` defaults to `Disabled`;
- `AutoItems.UseScrolls` defaults to `true`;
- `AutoItems.UseRelics` defaults to `true`.

Dispatch uses the cycle-pinned configuration by deliberate architecture decision. There is no
current-configuration reader in the adapter or GameAction. Committed master disable releases the
ownership lease as a fast backstop.

Fruit, Potion, Thread, allowlist, Auto Scribe, gameplay quick-control, and installer work are
outside this implementation. Native evidence and remaining live-validation limits are documented
in the [Auto Items native pipeline](../../docs/reverse-engineering/auto-items-native-pipeline.md).
