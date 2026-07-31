# Auto Items

Auto Items is the disabled-by-default Scroll, Relic, and temporary-item automation service compiled into
`OrbModSuite.dll`. It has no independent plugin identity, cadence, quick control, or configuration
store.

The worker consumes immutable shared-world consumable facts, requires exactly one supported native
family, gives eligible Relics priority over exact-UUID-approved temporary items and Scrolls, and
plans at most one action per publication. All evaluator paths wait for another world or
configuration publication; the engine prevents a second attempt against the same world reading.

`AutoItemsConsumableUseGameAction` owns the native boundary. It validates its complete reflection
schema at lifecycle scope, re-resolves stable UUID plus exact type, rechecks family, visibility,
native inventory idleness, `CanFire()`, Scroll randomization/live targeting, and temporary
duration/toxicity-only cost vectors. It captures ownership permits, submits through
`SelectAndFire()` under native multi-buy quantity one, and verifies exact stock/queue evidence plus
temporary usage creation where applicable. A Scroll/Relic ambiguous mutation quarantines the whole
action; a temporary ambiguity quarantines only that exact UUID.

One lifecycle-scoped follow-up observes the committed temporary item through later publications.
Exactly one usage must engage before disappearing. Multiple usages, premature expiry, or missing
engagement evidence quarantines the exact item loudly. Any temporary usage excludes Scroll/Relic
and other temporary uses; native Scroll/Relic preparation excludes a temporary use.

Configuration is additive and uses the suite's committed publication:

- `AutoItems.Mode` defaults to `Disabled`;
- `AutoItems.UseScrolls` defaults to `true`;
- `AutoItems.UseRelics` defaults to `true`.
- `AutoItems.TemporaryItemAllowlist` defaults empty and accepts comma-separated exact UUIDs only.

An empty allowlist leaves temporary items inert. The allowlist is parsed once per committed
configuration generation and pinned to the cycle; actions carry no configuration key. There is no
current-configuration reader in the adapter or GameAction. Committed master disable releases the
ownership lease as a fast backstop.

Gameplay quick controls remain outside this implementation. Native evidence and remaining
live-validation limits are documented in the
[Auto Items native pipeline](../../docs/reverse-engineering/auto-items-native-pipeline.md).
