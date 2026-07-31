# Auto Scribe

Auto Scribe is a disabled-by-default ServiceCycle feature compiled into `OrbModSuite.dll`. It
produces the six audited levelled Scroll recipes needed to cover native structure enchantment
deficits. Investment and Speed remain coverage-only identities because the audited Scribe registry
has no production recipe for them.

The background worker consumes only immutable published Scribe relationship facts, levelled Scroll
counts, pending Auto Items Scroll uses, active one-shot work, and player-owned
`AutoScribeInstances`. Unknown or contradictory evidence for any enabled producible role blocks the
complete service for that publication and names the first exact role and reason. A healthy role is
never produced around an unknown sibling. Live resource objects and spend modifiers never cross
into that background planning boundary.

Selection uses stable semantic roles and an audited cost rank as a fair rotating order:
Advancement, Power, Learning, Excellence, Development, then Echoing. The cursor survives across
world publications and resets when role configuration changes, so a permanently affordable cheap
recipe cannot starve later unlocked recipes. It plans at most one action per world publication.
Persistent native automation is external production pressure only; the suite never creates, edits,
or removes an `AutoScribeInstances` entry.

Each visible recipe owns an independent progression frontier derived from that Scroll's
`maxCreatedLv`, strongest owned level, queued work, and unexpired use. The shared Scribe
`maxStartingLevel` is not copied onto every recipe. A covered stable role probes its next level only
after a later world publication confirms that no owned Scroll remains at that frontier. Any queued
quantity, active preparation, pending/engaged use at any level, or non-expired manual or automatic
Scribe work at any level activates the Scroll's capacity-replacement interlock and blocks both
deficit production and progression. This world-mediated handshake lets the background world, not a
UI-local guess, decide when prior Scroll capacity has cleared. The guarded action then brackets and
binary-searches the monotonic native affordability boundary and crafts the strongest affordable
level at or above that request.

For a Scroll with a positive native maximum carry load, coverage demand is capped at that capacity.
More uncovered structures do not cause futile same-level crafts once matching stock fills the
inventory. Stock below the current target level does not satisfy that target, so the planner still
allows the game's stronger-level replacement path at full total capacity.

`AutoScribeOneShotCraftGameAction` owns the only mutation boundary. It resolves one complete
lifecycle-scoped binding set before use, re-resolves the action's recipe, Scroll, enchantment,
registry, and queue identities live, proves the complete role relationship, then preflights
visibility, queue room, competing supply, a valid native target, affordability, exact cost, and
ownership on the Unity main thread. Immediately before payment it captures each cost resource's raw
quantity and the native `GetTrueSpend`/decay modifiers, rejects bandwidth resources, and rejects a
duplicate-row cost whose aggregate raw debit exceeds the live balance. `PurchaseQuantity` is the
last risk taken before the native construction, initiation, and queue-or-instant admission
sequence. Affordability search is bounded, uses only the audited `CanBuyAt(BigDouble)` predicate,
and, after the mutation permit, the chosen level is revalidated for visibility, queue room, native
affordability, target, every capacity-replacement signal, exact cost, and payable aggregate raw
debit immediately before payment.

The action preserves native cost-row order and receipts the exact raw post-state produced by each
captured debit through native `BigDouble` subtraction and zero clamping. This matters when a
positive debit is below the quantity's numeric resolution: native payment legitimately leaves the
raw quantity unchanged, and the receipt classifies that outcome without pretending a debit was
observable. It also proves the `maxStartingLevel` transition and the exclusive
queue-or-instant-stock outcome. A native failure after payment or an ambiguous postcondition records
the observed partial commit, names the exact stage, and quarantines this GameAction until lifecycle
replacement. The first quarantine fault remains the health root cause; rejected follow-up
submissions neither replace it nor repeat the warning. Nothing attempts rollback of game-owned
irreversible effects.

Auto Scribe payment and Auto Items consumable use share one lifecycle-scoped publication-gap gate.
Either native attempt blocks both adapters until the world publisher commits a clean consumables
reading from a strictly later Unity frame. This prevents asynchronously derived pre-mutation worlds
from admitting cross-feature work or settling permanent consumable follow-up state.

Configuration is additive:

- `AutoScribe.Mode` defaults to `Disabled`;
- `AutoScribe.Roles` is a comma-separated list of semantic keys, empty selects every audited
  producible role, and `none` selects no role.

Role narrowing is deliberately cycle-pinned under the runtime doctrine's bounded configuration
staleness rule. Actions contain recipe UUID, Scroll UUID, level, and collection lifecycle only;
they do not carry a role key or re-read current configuration. Every evaluator disposition wakes
only on another publication. The shared registry exposes one feature-wide mode quick control; this
lane adds no evaluation interval, timer, per-role control, temporary-item allowlist, installer
behavior, or persistent Scribe automation.

The installed evidence and remaining live-validation limits are documented in the
[Auto Scribe native pipeline](../../docs/reverse-engineering/auto-scribe-native-pipeline.md).
