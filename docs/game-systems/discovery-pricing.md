# Discovery pricing

Each discovery tree keeps **its own** count of pool discoveries, and the price of the next pool pick
is looked up by that count. The counter increments **only when the discovery you select is not
required**. Required picks and rerolls leave it alone, and picks in another tree, production and
progression do not enter it at all.

## The authored opening

**Spell Discoveries** prices its first five pool picks from `90` Knowledge, scaling by a factor of 10
per step:

| Pool pick | Cost |
|---|---|
| 1st | `90` Knowledge |
| 2nd | `900` Knowledge |
| 3rd | `9,000` Knowledge |
| 4th | `90,000` Knowledge |
| 5th | `900,000` Knowledge |
| 6th and onward | from `1,000,000` Knowledge + `500` Thaumic Scrolls |

**Glyph Discoveries is a separate tree with a separate ladder**: base `200` Knowledge + `50`
Thaumaturgy, with its own scaling evaluated through its own modifier list.

## Beyond the opening

In the infinite tier the price keeps climbing, and **faster than any constant factor**: the per-step
factor itself grows with the count (e.g., adjacent rungs observed at roughly ×93 mid-run and ×64,000
much later). The two currencies of a price always scale in lockstep. The formula has not been
extracted, so any constant-factor extrapolation past the authored opening is wrong; see
[open-questions.md](open-questions.md).

## Two further rules

- Each tree applies its own configured **research cost reduction**, so a discount on one tree does
  nothing for another.
- Pool costs round to two significant digits; see
  [numbers-and-rounding.md](numbers-and-rounding.md).

A persistent reset clears recipe and glyph discovery along with each tree's choice state, so **every
ladder restarts at its base price on a new run**; see
[reset-and-ng-plus.md](reset-and-ng-plus.md).
