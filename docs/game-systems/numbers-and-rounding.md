# Numbers and rounding

Numbers are stored as a **mantissa and an exponent** — the `4.52e3` form the game displays. That
representation holds enormous values comfortably but keeps only a fixed number of significant digits.

## Spending to zero

**Spending your entire stock can round the remainder to literal zero.** When a purchase price is many
orders of magnitude below your holdings, the subtraction has no digits left to record the leftover
and the remainder collapses to 0 instead of a small number. The Reverb Rate and Replenish Ratio
growth terms exist as protection against this; see [growth-terms.md](growth-terms.md).

## Two significant digits, two rules

Prices round to two significant digits, and the game uses two different rounding rules:

- **Attributes** use an early-rounding form that only alters values in the range `[10, 100)`. Outside
  that window an Attribute price is whatever the pipeline produced, unrounded.
- **Upgrades** use the full form, which snaps at every magnitude. An upgrade price is always two
  significant digits.

Discovery pool prices and spell level costs also snap to two significant digits, which is why ladders
read as clean numbers like 90 / 900 / 9,000.
