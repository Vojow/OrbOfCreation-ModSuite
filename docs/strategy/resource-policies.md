# Resource Policies

Every resource carries a stance, and the stance changes as the run moves. This page is how to
pick it. The terms themselves — Rate, Interest, Capacity, Missing %, Gained, Quality — are
described in [`../game-systems/resources.md`](../game-systems/resources.md).

## The lifecycle: scarce → frontier → commodity → meaningless

Every resource walks the same path. At the start of a run nothing is a commodity, so everything
must be saved. Then a milestone lands, and the milestone is not just an unlock — **it
re-partitions the whole resource set.** Yesterday's frontier drifts to commodity; the newly
unlocked system's currency becomes the new frontier.

**Re-derive the partition at every milestone.** Why: a policy attached to a resource goes stale
the moment the resource changes class, and a stale save-stance is indistinguishable from a
deliberate one until the run is already slow.

Two edges of the lifecycle are easy to get wrong:

- **Commodities are not free.** You still prioritise among them, because the goal is time, not
  affordability. A commodity that shortens the clock outranks one that does not.
- **"Meaningless" is a real class.** A stock many orders of magnitude away from any price it
  could pay contributes nothing however large it looks — when a target sits 67 orders of
  magnitude away, saving is not a strategy. Spend it on anything that gains any benefit at all.

## The policy vocabulary

Say the policy, not the purchase. These are the statements that can bind every activity at once
— a per-purchase decision cannot express *don't*.

- **Floor** — never below N. For an input other activities depend on.
- **Save mode** — touch only above ~90 % of capacity, spend below ~10 %. For a frontier
  currency accumulating toward a named price.
- **Valuable** — anything priced under ~1 % of storage is beneath consideration. Sets the
  triviality threshold so small decisions stop consuming attention.
- **Burn priority** — a buff is currently pouring this resource in; spend it hard while it
  lasts.
- **Hoard mode** — an interest term compounds this stock; spending slows growth.
- **Income-relative triviality** — a price below one second of income is free. This is the
  honest generalization of "at cap, spending is free": what matters is the time it costs.
- **Direct directives** — "hold 30 of X for Y". The reservation form from
  [principles.md](principles.md); the highest-value statement in the vocabulary.

## Four spend modes, chosen by which growth term dominates

**Name the mode before ranking purchases.** The same candidate list ranks completely differently
under each, so ranking first and picking a stance afterwards produces incoherent advice. The
three unusual growth levers reward opposite behaviours — missing-% pays you for being empty,
interest pays you for being full, resting rate pays you for not transacting — so misreading
which one dominates inverts the right policy.

**HOARD — when interest dominates.** Income proportional to stock means spending directly slows
compounding. In the observed extreme, a stock's interest income ran ~26 orders of magnitude
above its production rate: buying production was worthless, and hoarding *was* growth. But
bounded spending against a compounding stock is quantifiably cheap — spending 1 % of stock
costs about four seconds of progress. Compute the cost; don't treat hoard mode as a taboo.

**BURST — when a missing-percent window is open.** Those fills read *capacity*, not current
stock, so the emptier you are the bigger the fill. Spend everything as fast as you can and keep
the tank empty until the window closes. This is also the mode at cap: marginal holding is worth
nothing when income is overflowing. Food fruits are the confirmed window-openers — type-targeted
`Missing / min` fills that stack across overlapping types, so line fruit picks up with dual-typed
targets and charm windows, budgeted against Toxicity.

**REST — when a resting rate is charging.** Some stocks gain faster the longer they go
untouched, and the bonus dies the moment something transacts. A standing dribble of automatic
activity — an auto-buy, a scheduled craft — suppresses the lever permanently, so resting a
resource is an active choice to route activity elsewhere, not neglect. Toxicity is the
confirmed carrier: waiting for a full drain before an item burst is exactly this mode.

**REINVEST — when rate dominates.** Buy the printer. Early generator levels are effectively
doublings, because the base addition is additive while each level carries a cumulative
multiplier over the previous ones — the second level of a generator roughly doubles current
production. This is the default mode; most of the time you are fine with just rate.

## Capacity and bandwidth are allocations, not consumptions

Advancement points, spell capacity and spell spots, plot capacity, the development queue, Time
Advancements — these are refunded, redistributable, or capped in place. They are **portfolio
problems** (how do I split what I hold), not spend-timing problems (when do I pay).

Applying a stock policy to an allocation produces nonsense: there is nothing to save for and
nothing to run out of, only a split to get right. Three consequences worth stating outright:

- **Advancement grants raise the cap, not the balance.** A grant arriving while the resource
  reads 2/2 makes it 2/3 — nothing is wasted at cap, so there is no urgency to spend and
  deferring an allocation is free.
- **The development queue serializes everything**, so ordering matters even in abundance, and
  headroom must be left rather than filled to the brim.
- **Spots and weight bind separately.** Spots are parallel cooldown lanes — throughput. Weight
  is capacity. A weight-0 spell still costs a lane, and a heavy spell can be blocked by both at
  once. See [`../game-systems/casting-and-spells.md`](../game-systems/casting-and-spells.md).

## Reserve floors belong to the resource, not the activity

"400 mana is nothing now — but if we cast so fast that we leave no mana over, we can't buy this
ever." A caster running at full throttle drained the pool to a level that blocked a pending
purchase for the rest of the hour.

**Set the floor on the resource and bind every consumer of it** — casting, buying, channelling,
crafting. Why: any consumer that owns its own floor can starve the others without ever
violating its own policy, and the starvation is invisible from inside that consumer.

Sustained-cost activities need a second check: **pay against the drain, not the entry cost.**
Affording a channel's cast price but not its per-second drain buys a few seconds of benefit and
wastes the cast.

## Never generate into a cap

Production into a full sink is discarded outright. Check the sink before scheduling the
generator, and bench a generator whose resource is sitting at capacity — its marginal income is
zero and its spell spot has better uses.

Rate-fed resources cannot overcap at all. Discrete-payout resources can sit above cap for a
short grace period, after which a rubber band pulls them back down; any discrete touch —
including a small purchase — restarts the grace. **Overcap stock is a decaying asset unless
something keeps touching it**, so treat it as spend-now value, not as savings.

## Holding can be the yield

Some stock modifies the world while merely held — holding Thaumaturgy raises mana capacity,
which can bridge a capacity gap without a whole detour lap through cap attributes.

So hold-versus-spend is a computation, never a habit. There are exactly three reasons to hold:

1. **Goal reservation** — it is the frontier currency of the active milestone.
2. **Interest** — income scales with stock, so spending slows compounding.
3. **Stock-as-modifier** — the holding itself grants an effect you are using.

If none of the three applies, holding is just delay with extra steps.
