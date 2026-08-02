# Splash and Rarity Value

Splash is the mechanic behind effects that read like *"generate supply of this TYPE divided amongst
its components, division based off rarity value"*. It targets a resource **type**, not a resource,
and distributes across that type's members.

For a splash amount `S`, the game selects the `N` resources of that type that are discovered and have
a positive rarity, and gives each one

```
share_i = 100 × S × r_i / (N × r_max)
```

before that resource's own gain modifiers, where `r_i` is that resource's **lifetime** production
rate — total lifetime quantity divided by time since the run started, not a rolling window — and
`r_max` is the largest such rate among visible generated resources.

Three consequences follow directly from the formula:

- **Splash feeds the rich.** The share is proportional to how fast you already produce the resource.
  E.g., one observed `+10.4 Mental` splash landed as `+7.79` Knowledge and `+0.421` Psi.
- **Splash does not conserve.** The shares total `(100S/N) × Σ(r_i / r_max)`, which can be more or
  less than `S`.
- **Splash does not feed its own weights.** Splash gains do not count toward lifetime quantity, so
  splashing a resource never raises its future share. A capped recipient registers only what fit.

The inputs that move splash weights are non-splash lifetime production, resource-type membership, how
many members of the type are discovered (`N`), capacity limiting what gets registered, and each
recipient's own final gain modifier. **Current quantity is not an input at all.**

## Rarity Value

The **Rarity Value** on a resource tooltip is not a price and not a measure of your holdings. It is a
live ratio:

```
Rarity Value_i = r_max / (100 × r_i)
```

The fastest resource therefore always reads `0.01`, and a Rarity Value of `3.37e4` means that
resource's lifetime production rate is 3.37 million times slower than the fastest eligible one. It
moves as lifetime production and the visible resource population change. Its only mechanical role is
as the splash weight: the splash share is exactly `(S/N) ÷ Rarity Value`. It is **not** an exchange
rate between resources.

A **second, authored** rarity number also exists on each resource and drives cost-list rarity rather
than splash — e.g., Psi's is `2`, against a tooltip Rarity Value in the tens of thousands. Its
quality-adjusted form is `authored rarity × Quality / 100`. The tooltip always shows the calculated
one.
