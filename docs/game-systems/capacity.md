# Capacity is a gate

When a price exceeds your capacity for the resource it is priced in, the purchase is **unreachable**,
not slow: time-to-afford is infinite under the current cap, and the price renders red. The set of
things you can buy is bounded by capacity, not by stock.

The game teaches this in the opening minutes. Infuse Orb costs 25 → 50 → 100 → 200 mana, doubling
each level, against a starting mana capacity of 100, so the fourth level is priced above the ceiling
and the purchase list forces you elsewhere.

## Resolving a cap gate is a cross-currency detour

The thing that raises a cap is normally priced in a different resource than the thing that is gated.
The opening example runs glyph → spell → Knowledge → a capacity attribute bought with Knowledge →
Infuse Orb reachable again. That loop repeats several times in the early game.

## Time to cap

The game computes it for you: mana tooltips read `Maxed in: 0s/16.7s` — current stock to cap at the
current rate.
