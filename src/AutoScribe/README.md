# Auto Scribe

Auto Scribe is a disabled-by-default ServiceCycle feature compiled into `OrbModSuite.dll`. It
produces the six audited levelled Scroll recipes needed to cover native structure enchantment
deficits. Investment and Speed remain coverage-only identities because the audited Scribe registry
has no production recipe for them.

The worker consumes immutable Scribe relationship facts, levelled Scroll counts, pending Auto Items
Scroll uses, active one-shot work, and player-owned `AutoScribeInstances`. Unknown or contradictory
evidence for any enabled producible role blocks the complete service for that publication and names
the first exact role and reason. A healthy role is never produced around an unknown sibling.

Selection uses stable semantic roles and an audited cost rank as a fair rotating order:
Advancement, Power, Learning, Excellence, Development, then Echoing. The cursor survives across
world publications and resets when role configuration changes, so a permanently affordable cheap
recipe cannot starve later unlocked recipes. It plans at most one action per world publication.
Persistent native automation is external production pressure only; the suite never creates, edits,
or removes an `AutoScribeInstances` entry.

Each visible recipe owns an independent progression frontier derived from that Scroll's
`maxCreatedLv`, strongest owned level, queued work, and pending use. The shared Scribe
`maxStartingLevel` is not copied onto every recipe. A covered stable role probes its next level;
the guarded action then brackets and binary-searches the monotonic native affordability boundary
and crafts the strongest affordable level at or above that request. This advances cheaper and more
expensive Scroll families independently.

Scroll coverage is a consumption pipeline, not an inventory target. For a positive native carry
limit, desired supply is the smaller of uncovered eligible structures and carry capacity. Owned
Scrolls, queued work, and pending uses at or above the frontier subtract from that demand; gifts are
therefore absorbed by the next publication without special handling. A non-positive carry limit
blocks the role with `NonPositiveCarryLimit`, because audited native `Gain()` clamps positive output
to that limit and silently drops it.

The same audit establishes that native “weakest” is level-only. At capacity, a strictly stronger
Scroll replaces the weakest, an equal-level Scroll replaces without changing coverage, and a
strictly weaker Scroll is silently lost after payment. Queue and instant completion execute the
same gain path, and crafting cost is paid before that capacity decision. Demand-driven production
never requests equal-level stock churn: frontier crafts are needed for uncovered structures and
replace dead weaker stock for free when capacity is full. Auto Scribe never calls `Discard()` and
owns no cleanup action.

`AutoScribeOneShotCraftGameAction` owns the only mutation boundary. It resolves one complete
lifecycle-scoped binding set before use, re-resolves the action's recipe, Scroll, enchantment,
registry, and queue identities live, proves the complete role relationship, then preflights
visibility, queue room, competing supply, a valid native target, affordability, exact cost, and
ownership. `PurchaseQuantity` is the last risk taken before the native construction, initiation,
and queue-or-instant admission sequence. Affordability search is bounded, uses only the audited
`CanBuyAt(BigDouble)` predicate, and the chosen level is revalidated for target, competing supply,
and exact cost before payment.

The action receipts the exact resource charge, `maxStartingLevel` transition, and exclusive queue
or instant-stock outcome. A native failure after payment or an ambiguous postcondition records the
observed partial commit, names the exact stage, and quarantines this GameAction until lifecycle
replacement. Nothing attempts rollback of game-owned irreversible effects.

The same `AutoScribeOneShotCraftGameAction` also owns the manual one-shot player capability exposed
as `game_craft`. Its player overload leaves the Auto Scribe planner and receipts unchanged, but
widens exact live revalidation to every concrete `CraftingRecipeSO`: native direct `Execute`, or the
authored page's stack/new/instant queue route. MCP success returns the newer named recipe decision
with next costs, holdings, affordability, and queue state; payment accounting is not a player-action
success gate. The installed mechanism and live checklist are documented in the
[one-shot crafting native pipeline](../../docs/reverse-engineering/one-shot-crafting-native-pipeline.md).

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
