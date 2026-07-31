# Auto Items

Auto Items is the disabled-by-default Scroll, Relic, and temporary-item automation service compiled into
`OrbModSuite.dll`. It has no independent plugin identity, cadence, or configuration store. Its one
feature-wide quick control is owned by the shared automation control registry.

The worker consumes immutable shared-world consumable facts and preserves native family membership
as a set. A sole supported membership selects its operation; the game's authored `Fruit + Relic`
topology selects the permanent Relic operation, while every other cross-operation combination
fails closed. Eligible Relics take priority over exact-UUID-approved
temporary items and Scrolls, and the worker plans at most one action per publication. Published
preparation state gates every family. Scroll
admission additionally requires published non-empty target evidence for the strongest owned level
and refuses the Scroll while any non-expired manual or automatic Scribe work targets its recipe.
All evaluator paths wait for another world or configuration publication; the engine prevents a
second attempt against the same world reading.

`AutoItemsConsumableUseGameAction` owns the native boundary. It validates its complete reflection
schema at lifecycle scope, re-resolves stable UUID plus exact type, rechecks family, visibility,
native inventory idleness, absence of an open `TargetingManager` request, `CanFire()`, Scroll
randomization/live targeting, and temporary
duration/toxicity-only cost vectors. It captures ownership permits, submits through
`SelectAndFire()` under native multi-buy quantity one, and verifies exact stock/queue evidence plus
usage creation and preparation, including the exact planned level for Scrolls and Relics. Before mutation it rejects active native preparation and
also scans exact queued and pending evidence; a queue that claims to be idle while retaining either
is inconsistent. Immediately before Scroll or Relic mutation it also requires the native
`SoundManager` singleton and every reachable pooled `AudioElement.audioSource` to be ready. Missing
audio infrastructure or fewer than two idle/reusable non-looping entries is a transient,
non-mutating refusal because native preparation can pin one entry and must leave another for
completion/progression audio. Native preparation plays audio
before it finishes stock and inventory-queue bookkeeping. A Scroll ambiguity or inconsistent idle queue quarantines only Scroll use, so an
otherwise safe Relic remains available. A Relic ambiguity still quarantines the shared non-temporary
boundary, while a temporary ambiguity quarantines only that exact UUID. The mutation attempt that
creates a quarantine remains a fault with native evidence. Later no-mutation quarantine refusals are
expected rejections, so a safe lifecycle quarantine cannot accumulate action faults or engage the
suite emergency stop by itself. Scroll failure evidence names the randomization or native-use stage,
retains a bounded inner exception stack, and records exact before/after level, stock, queue,
preparation, usage, randomization, and targeting state.

One lifecycle-scoped follow-up observes the committed temporary item through later publications.
Exactly one usage must engage before disappearing. Multiple usages, premature expiry, or missing
engagement evidence quarantines the exact item loudly. Any temporary usage excludes Scroll/Relic
and other temporary uses; any native consumable preparation excludes every new consumable plan.
Expected live-race refusals are retained as health evidence without repeating the same warning on
every publication, and a later clean publication clears only transient refusal health.

After a committed Scroll or Relic submission, lifecycle-scoped settlement waits for a strictly
newer clean consumables publication. It accepts either the exact queued/preparing/pending topology
or an already drained native queue. Contradictory queue, preparation, usage, or level evidence
quarantines only the affected permanent family for that lifecycle. Total stock is deliberately not
part of settlement because Scribe capacity replacement can preserve quantity while changing levels.
Auto Items and Auto Scribe also share a lifecycle-scoped publication-gap coordinator: every actual
native attempt closes both adapters and pauses permanent settlement until the world publisher commits
a clean consumables reading collected in a strictly later Unity frame. A pre-mutation capture that
finishes derivation later cannot clear the gate.

Configuration is additive and uses the suite's committed publication:

- `AutoItems.Mode` defaults to `Disabled`;
- `AutoItems.UseScrolls` defaults to `true`;
- `AutoItems.UseRelics` defaults to `true`.
- `AutoItems.TemporaryItemAllowlist` defaults empty and accepts comma-separated exact UUIDs only.

An empty allowlist leaves temporary items inert. The allowlist is parsed once per committed
configuration generation and pinned to the cycle; actions carry no configuration key. There is no
current-configuration reader in the adapter or GameAction. Committed master disable releases the
ownership lease as a fast backstop.

The Mods page is the only in-game editor for exact temporary-item approval. It sends each discovered
item through the worker's shared exact-topology family resolver and lists only resolved Fruit,
Potion, and Thread operations, ordered by operation then native name. Fruit + Relic items therefore
remain Relics and are not listed. Each row shows the native icon, its resolved operation followed by
every other authored family name, and current stock beside a raised/recessed approval row.
The state line always says how many
discovered items are approved. Stored UUIDs that do not resolve remain explicit removable rows; a
failed native discovery read is a red failure state and never masquerades as the healthy
`No discovered temporary items yet` state; stored entries remain visible and removable alongside
that failure. Every click changes only the staged serialized value,
so Apply, Revert, external-conflict handling, and persistence remain the ordinary Mod Config path.
There is no raw-text, family, select-all, or blacklist control.

The shared quick control changes only `AutoItems.Mode`; exact temporary-item approval remains on
the Mods page. Native evidence and remaining live-validation limits are documented in the
[Auto Items native pipeline](../../docs/reverse-engineering/auto-items-native-pipeline.md).
