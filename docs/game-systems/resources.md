# Resources

A resource is a stock with a growth equation attached. Open any resource's Details panel and
the game enumerates that equation term by term — it is not one "rate" but a set of independent
mechanisms, and different resources are driven by different ones.

Everything here folds through the machinery in
[value-computation.md](value-computation.md#the-five-kinds-of-modifier); this page describes
what the terms mean, not how the modifiers combine.

## The growth terms

The Details panel lists these terms:

| Term | What it does |
|---|---|
| Rate | Passive production per second. |
| Interest Rate | Production proportional to the current stock, quoted per minute. |
| Capacity | The stock ceiling. A hard gate on what you can buy, not just a storage limit. |
| Quality | Divides what a purchase actually takes out of your stock. |
| Gained | A flat multiplier on everything the resource gains. |
| Attribute Cost | A multiplier on the price of Attributes priced in this resource. |
| Reverb Rate | Rare. Protection against spending a stock down to a rounded zero. |
| Replenish Ratio | Rare. Same family as Reverb; which of the two does what is unestablished. |
| Decay Ratio | How far the resource can be pushed above its storage before it is dragged back. |

### Rate and Gained

The rate you see on a tooltip is the raw rate multiplied by Gained. On a mana tooltip
captured mid-run: raw rate `24.8/s`, Gain `172%`, displayed rate `+42.6/s` — `24.8 × 1.72 =
42.66`. Both halves are separately modifiable, and effects target them separately.

A brand-new resource often already shows a Gain above 100% with no upgrade pointed at it.
That is **Achievement Strength**, the cross-run meta bonus: each point contributes +1% to the
gain rate of the `All` resource type, so 28 points is exactly ×1.28 on every resource in the
game. (Each point also grants +1 Starting Time Advancement — see
[time-and-prestige.md](time-and-prestige.md).)

### Interest Rate

Interest is gain proportional to the stock you are holding, so it compounds. It appears late.
The only observed instance is a very late-game resource carrying an Interest Rate of
`16.8/min` (contributed by two named sources), where interest income exceeded that resource's
production rate by roughly 26 orders of magnitude. How common interest is, and which resources
carry it, is unknown. That resource had no Capacity at all; whether interest saturates against
a cap on a capped resource is unverified.

### Resting rate

Some resources regenerate faster the longer they go **untouched** — leave the stock alone and
its gain accelerates. Together with interest and missing-percent fills, this is the third of
the game's unusual growth levers, and the three reward opposite behaviours: **missing-% pays
you for being empty, interest pays you for being full, and resting rate pays you for not
transacting.** A constant dribble of automatic spending permanently suppresses a resting
bonus, so a resource carrying one is a reason to leave it deliberately alone. Toxicity is a
confirmed carrier (its recovery speeds up when items go unused for a while — see
[consumables-and-items.md](consumables-and-items.md#toxicity)); which other resources carry a
resting rate, and the exact shape of the acceleration, have not been pinned down (unverified).

### Quality and Attribute Cost

Quality never changes the price you see. It changes the payment: **actual spend = displayed
cost ÷ Quality**.

Attribute Cost is a per-resource multiplier applied to the price of Attributes bought with
that resource. Quality feeds into it too, but only indirectly: the Attribute Cost term is
divided by the resource's Quality raised to the **Attribute Quality Bonus**, a research-driven
value that is **zero until researched**. Anything raised to the power zero is one, so on a run
without that research Quality has no effect on Attribute prices at all and only affects direct
spends.

### Reverb Rate and Replenish Ratio

Both are rare, and both are described as protection against the big-number rounding hazard:
spending a stock that spans many orders of magnitude can round your remainder to literal zero
instead of a small leftover (see
[value-computation.md](value-computation.md#big-numbers)). Which term does which job is not
established (unverified).

### Decay Ratio

Decay Ratio governs how far the resource can be pushed above its storage before the overcap
pull described below starts dragging it back. How the displayed ratio maps onto the loss
formula has not been established (unverified).

## Where the rate comes from: passive, cast-only, and mixed

Some resources have no passive production at all and read `+0/s`. Early in a run both
Knowledge and Thaumaturgy are **cast-only**: the only thing that makes them is casting a
spell. Their effective rate is

```
casts per unit time × gain per cast × Gain%
```

so cooldowns, spell slots and spell power are their rate terms rather than anything on the
resource itself. See [casting-and-spells.md](casting-and-spells.md).

A resource can move between these regimes during a run — a purchasable passive generator for a
previously cast-only resource is a normal milestone.

## Stock as a modifier

Holding a resource can itself be an effect. Merely possessing Thaumaturgy grants **+30 Mana
Capacity**, soft-capped at 100 Thaumaturgy held — the bonus is paid for by the stock sitting
in your pocket, and spending the stock spends the bonus with it. Holding and spending are
therefore genuinely different actions for such a resource, not just different timings.

## Capacity is a gate, not a speed bump

When a price exceeds your capacity for the resource it is priced in, the purchase is not slow —
it is **unreachable**. Time-to-affordable is infinite under the current cap, and the price
renders red. The set of things you can buy is bounded by capacity, not by stock.

The game teaches this deliberately in the opening minutes. Infuse Orb costs **25 → 50 → 100 →
200** mana, doubling each level, against a starting mana capacity of **100**. The fourth level
is priced above the ceiling, so the purchase list forces you elsewhere — in that case onto the
first glyph, which opens the chain that eventually raises the cap.

Resolving a cap gate is normally a **cross-currency detour**: the thing that raises the cap is
priced in a different resource than the thing that is gated. The opening example runs
glyph → spell → Knowledge → the Wisdom attribute (`+84.7` base mana capacity, `×1.074/level`,
bought with Knowledge) → Infuse Orb becomes reachable again. That loop repeats several times
in the early game.

Two more capacity facts:

- **The game computes time-to-cap for you.** Mana tooltips read `Maxed in: 0s/16.7s` —
  current-stock-to-cap given the current rate.
- **Some effects fill a fraction of what is *missing*, measured against capacity rather than
  current stock**, so multiplying capacity first and filling afterwards produces very large
  bursts. Confirmed on food fruits: a captured Brain Berry tooltip reads `+65.5 % Mental
  Missing / min` for its duration — a per-minute fill of the missing amount, targeted at a
  resource **type** (see
  [consumables-and-items.md](consumables-and-items.md#a-worked-fruit-brain-berry)). Because the
  target is a type, two fruits whose types overlap on a dual-typed resource (Psi is Mental
  **and** Energetic) stack their fills on it. Whether the fill reads effective or base capacity
  is still unestablished.

## Overcap and the loss timer

Resources can go **above** their capacity. What happens next is a one-shot three-second timer
followed by a rubber band.

Once the timer has engaged and quantity `Q` exceeds capacity `C`, the loss rate is

```
0.85 × (Q − C) + 0.5   units per second
```

evaluated on discrete updates until quantity reaches capacity, where it stops. It is a pull on
the *excess* plus a fixed tail — not a percentage of the total — so a large overcap drains fast
and a small one lingers.

What resets the three-second timer:

| Event | Resets the timer |
|---|---|
| Any nonzero gain or spend through the normal path — purchases included | Yes |
| An active modifier-backed rate, or an active drain, each tick | Yes |
| A plain authored base rate | **No** |

That distinction is the whole mechanic: a resource that is only fed by its plain base rate
will decay back to cap, while a resource being touched by discrete events or by a
modifier-backed rate holds its overcap indefinitely.

Two caveats worth carrying:

- The loss constants above were read off **advancement resources** (explicit loss 0, base loss
  0.5, overflow-loss modifier 100%). Whether ordinary resources share the same constants is
  **not established**.
- Rate-fed resources such as mana were never observed to overcap, while discrete-payout
  resources do. That looks like a consequence of the reset rules rather than a separate rule,
  but it has not been confirmed (unverified).

### Spark: the exception

Spark behaves unlike every other observed resource. It decays toward **zero**, not toward its
cap, and it does so even while below capacity. It does not decay while its channel is active.
Its gain scales inversely with how much you are holding — more when low, less when high — so
its equilibrium sits at zero. No formula for any of this has been extracted (unverified), and
it is described as the only resource that behaves this way.

## Splash

Splash is the mechanic behind effects that read like *"generate supply of this TYPE divided
amongst its components, division based off rarity value"*. It targets a resource **type**, not
a resource, and it distributes across the type's members.

For a splash amount `S`, the game selects the `N` resources of that type that are discovered
and have a positive rarity, and gives each one

```
share_i = 100 × S × r_i / (N × r_max)
```

before that resource's own gain modifiers, where `r_i` is that resource's **lifetime**
production rate — total lifetime quantity divided by time since the run started, not a rolling
window — and `r_max` is the largest such rate among visible generated resources.

Three consequences follow directly from the formula:

- **Splash feeds the rich.** The share is proportional to how fast you already produce the
  resource. Observed: a `+10.4 Mental` splash landed as **+7.79 Knowledge and +0.421 Psi** —
  the abundant resource took the bulk and the scarce one got a rounding error's worth.
- **Splash does not conserve.** The shares do not sum to `S`; the total is
  `(100S/N) × Σ(r_i / r_max)`, which can be more or less than `S`.
- **Splash does not feed its own weights.** Splash gains deliberately do not count toward
  lifetime quantity, so splashing a resource never raises its future share. A capped recipient
  only registers the amount that actually fit.

The inputs that do move splash weights are: non-splash lifetime production, resource-type
membership, how many members of the type are discovered (`N`), capacity limiting what gets
registered, and each recipient's own final gain modifier. **Current quantity is not an input
at all.**

The Mental type in the examined data contains Control, Knowledge, Psi, Skill, and Cognitive
Disc.

## Rarity Value

The **Rarity Value** on a resource tooltip is not a price and not a measure of your holdings.
It is a live ratio:

```
Rarity Value_i = r_max / (100 × r_i)
```

— the inverse of that resource's lifetime production rate relative to the fastest visible
generated resource. The fastest resource therefore always reads `0.01`. A Psi Rarity Value of
`3.37e4` means Psi's lifetime production rate is 3.37 million times slower than the fastest
eligible resource.

It is **dynamic**: it moves as your lifetime production and the visible population of
resources change. Its only mechanical role is as the splash allocation weight above — the
splash share is exactly `(S/N) ÷ Rarity Value`. It is **not** a general exchange rate between
resources and does not mean one resource is worth that much of another.

Confusingly, a **second, authored rarity number** exists on each resource and is used by
cost-list rarity calculations (Psi's is `2`, against a tooltip Rarity Value in the tens of
thousands). The two are unrelated; the tooltip shows the calculated one.

## Resource types and keywords

Every resource carries one or more type keywords, and effects target those keywords rather
than individual resources. An effect that boosts "the rate of all Mental resources" boosts
every Mental resource you own, present and future.

The full type list is:

> Blooming, Building, Celestial, Elemental, Energetic, Essence, Hexed, Liquid, Magic, Mental,
> Metal, Natural, Parchment, Spacial, Advancement, All Capped, All, Influential, Progression,
> Spiritual, Tempered.

Most of these are flavor categories. Four are structural targeting groups rather than themes:
`All` (everything — Achievement Strength targets this one), `All Capped`, `Advancement`, and
`Progression`.

Observed memberships:

| Resource | Types |
|---|---|
| Mana | Magic, Elemental |
| Knowledge | Magic, Mental |
| Psi | Mental, Energetic |
| Thaumaturgy | Spacial |

Because most resources carry two keywords, a single keyword-targeted effect usually touches
more than you expect, and early in a run a broad-sounding effect is often degenerate — "+6.45%
base Elemental resource rate" is a mana-rate multiplier when mana is your only Elemental
resource. Attribute names and Attribute categories are also targetable keywords, so the same
grammar reaches beyond resources.

## Resources that are really allocations

Some things the game models as resources are budgets, not stocks. Advancement currencies
display as `quantity / cap` where the **cap is what you have earned** and the **quantity is
what is still unallocated**: `0/2` means you earned two and spent both. A progression level
raises the cap rather than granting a unit, so an advancement sitting at `2/2` becomes `2/3` —
nothing is ever wasted by being "full". See
[progression-advancements.md](progression-advancements.md).

The same shape covers the game's capacity/bandwidth resources — Spell Capacity, plot capacity,
Alchemical Capacity, advancement points and others; there are 25 of them in the data. They are
allocated and reallocated rather than produced and consumed.

## Tokens: Momentum

Momentum is not a resource but is counted like one, and its timing rules are unusual enough to
be worth stating.

It holds up to **ten tokens**, with a four-second duration before scaling. Each effective
whole stack contributes **+8% build speed (additive), ×1.08 Cantrip cooldown speed, and ×1.04
Cantrip power**. The stack runs **one shared countdown**: adding a token does **not** refresh
it, and when the countdown elapses a **single token** expires, after which the next token takes
the following interval. Effects change only as whole token boundaries are crossed, so
fractional stacks contribute nothing.

Kinetic Mind is the observed source: casting it adds one Momentum token and causes a Mental
splash. Momentum is one of 24 emblem passives in the data; the others do not all share its
accumulating shared-timer shape.

---

Open items from this page are collected in [open-questions.md](open-questions.md).
