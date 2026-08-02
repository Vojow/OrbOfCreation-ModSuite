# Resource growth terms

A resource is a stock with a growth equation attached. Its Details panel enumerates that equation
term by term, and different resources are driven by different terms.

| Term | What it does |
|---|---|
| Rate | Passive production per second. |
| Interest Rate | Production proportional to the current stock, quoted per minute. |
| Capacity | The stock ceiling, and a hard gate on what you can buy. |
| Quality | Divides what a purchase actually takes out of your stock. |
| Gained | A flat multiplier on everything the resource gains. |
| Attribute Cost | A multiplier on the price of Attributes priced in this resource. |
| Reverb Rate | Rare. Protection against spending a stock down to a rounded zero. |
| Replenish Ratio | Rare. Same family as Reverb. |
| Decay Ratio | How far the resource can be pushed above its storage before it is dragged back. |

## Rate and Gained

The displayed rate is the raw rate multiplied by Gained. E.g., one observed mana tooltip: raw rate
`24.8/s`, Gain `172 %`, displayed `+42.6/s`. Both halves are separately modifiable and effects target
them separately.

A brand-new resource often already shows a Gain above 100 % with nothing pointed at it; that is
[achievement-strength.md](achievement-strength.md).

### Cast-only and mixed rates

Some resources have no passive production and read `+0/s`. Knowledge and Thaumaturgy are cast-only
early in a run: their effective rate is `casts per unit time × gain per cast × Gain%`, so cooldowns,
spots and spell power are their rate terms rather than anything on the resource. A resource can move
between regimes during a run: a purchasable passive generator for a cast-only resource is a normal
milestone.

## Quality and Attribute Cost

Quality never changes the price you see, only the payment; see
[cost-pipeline.md](cost-pipeline.md).

Attribute Cost is a per-resource multiplier on the price of Attributes bought with that resource.
Quality feeds it only indirectly: the term is divided by the resource's Quality raised to the
**Attribute Quality Bonus**, which is zero until researched. Anything raised to the power zero is
one, so without that research Quality has no effect on Attribute prices at all.

## Holding as an effect

Possessing a resource can itself be a modifier. E.g., merely holding Thaumaturgy grants +30 Mana
Capacity, soft-capped at 100 Thaumaturgy held. Spending the stock spends the bonus with it, so
holding and spending are genuinely different actions for such a resource.
