# Discovery

[Back to game systems](README.md)

Discovery is how the game hands you new *things* rather than more of a thing you already have:
spell recipes, time runes, augment glyphs, rituals, artifacts. It is one mechanism wearing five
names. You pay a cost, the game rolls, and you must make a choice.

## One mechanism, several screens

Every discovery screen in the game is the same interaction with a different currency and a
different output family. Four instances have been confirmed with a pixel-identical layout:

| Screen | Where | Produces |
|---|---|---|
| Spellcraft | Magic > Spellbook > Unlock | spell recipes |
| Glyphcraft | Magic > Augments > Glyphcraft | augment glyphs |
| Devote | Rituals > Discover | rituals |
| Runecraft | Time > Time Runes > Create | time runes |

Two further screens almost certainly belong to the same family — Alchemy > Learn and
Workshop > Artifacts > Create — but have not been inspected. *[Unverified.]*

The game's data holds **seven** discovery trees in total, so at least one more surface exists
beyond the six above. *[Unverified: the remaining tree has not been matched to a screen.]*

## The screen is compose-and-confirm, not a catalogue

Every discovery page has the same four parts, top to bottom:

1. a **cost header** — what this roll costs,
2. a **component row** — the pieces you own and can put in,
3. a **composition area** — what you have placed,
4. a **Confirm** button.

You compose components. **The game resolves which output that composition produces.** You do not
pick the output.

This is worth stating flatly because it inverts the mental model most idle games train:
**there is no recipe list anywhere in the game.** There is no page that enumerates the spells,
glyphs, runes or rituals you could make and lets you select one. The only lever you have is which
components you put in and when you press Confirm.

The practical consequence is that **what you own defines what can come out**. Glyphs are pool
unlockers — an output becomes reachable once all of the glyphs it requires are unlocked — which is
why glyph purchases widen future rolls rather than doing anything immediately. Some of the game's
own tooltips call these pool unlockers **Recipe Books** while the upgrade tooltips call them
Glyphs; both refer to the same thing, and neither is the *augment* glyph you socket into a spell.
See [glyphs-augments-recipe-books.md](glyphs-augments-recipe-books.md).

## Required components versus optional pool picks

Two kinds of roll exist, and they behave differently in almost every respect.

**Required components** are marked with an exclamation mark. These are the critical path — the
game is not gambling with your progression here. A required roll is effectively rigged to a single
option: you press Confirm and you receive the one thing the game intends you to have next. Every
required roll observed so far is priced in **mana**.

**Optional pool picks** draw several candidates from that tree's pool and make you choose one of
them. These are the rolls where number-of-choices, rerolls and the pricing ladder all matter, and
they are priced from the ladder below rather than in mana.

| | Required component | Optional pool pick |
|---|---|---|
| Marker | exclamation mark | none |
| Options offered | effectively one | the choice-count statistic |
| Cost | mana | the tree's pool ladder |
| Advances the price ladder | no | yes |

## Choices and rerolls are statistics

How many options a roll offers you is a **statistic**, not a constant — observed at 2 early in a
run. So is **Max Discovery Rerolls**, observed at 1. Like other statistics they are subject to the
game's normal modifier machinery, so upgrades and effects can move them.

The reroll rules, as observed:

- Rerolling replaces the options offered. **Rerolling never rerolls the rarity** of the offer.
- You **gain a reroll back only by taking a choice without having rerolled**. Spend the reroll and
  that restraint bonus does not arrive.
- Rerolls do **not** advance the pricing ladder.
- The observed sequence is: reroll count 1 goes to 0, the current selection is cleared, any further
  reroll attempt reports the reroll as already used, and confirming the pick then displays the
  *next* roll's cost.

## The commitment point

This is the single most important rule on this page.

**Paying the cost is the commitment, not the pick.** Pressing Confirm on the cost header rolls the
dice. Once rolled, you must choose one of the results. There is no "walk away" — the choice sits
there open.

An open choice **blocks the roll queue**. While a discovery choice is unresolved you cannot roll
newly unlocked required components in that tree, so a pending choice can stall the critical path
behind an optional pick you were undecided about.

Therefore **deferral is only valid before paying**. "I'll decide later" is a decision you make
about whether to pay, not about which option to take.

Two facts make deferral before paying genuinely free:

- **Unpicked options persist into later draws.** Declining to take an option this time does not
  burn it; it can appear again.
- **Nothing about an offer expires on a timer.** The cost is what it is; the pool is what it is.

Waiting also *changes* the offer, because unlocking further glyphs enlarges the pool the roll draws
from — a later roll of the same price can present strictly better candidates.

## Pricing: one ladder per tree, counted only on optional picks

Each discovery tree keeps **its own** count of pool discoveries, and the price of the next pool
pick is looked up by that count. The counter increments **only when the discovery you select is not
required**. Required picks and rerolls leave it alone, and picks in another tree, production, and
progression do not enter it at all.

**Spell Discoveries** gives its first five pool picks a base cost of `90` Knowledge and scales them
by a factor of `10` per step:

| Pool pick | Cost |
|---|---|
| 1st | `90` Knowledge |
| 2nd | `900` Knowledge |
| 3rd | `9,000` Knowledge |
| 4th | `90,000` Knowledge |
| 5th | `900,000` Knowledge |
| 6th and onward (infinite tier) | `1,000,000` Knowledge + `500` Thaumic Scrolls |

**Glyph Discoveries is a separate tree with a separate ladder**: base `200` Knowledge +
`50` Thaumaturgy, with its own scaling. Its scaling is evaluated through its own modifier list and
must **not** be assumed to be the same ×10 rule — the two trees genuinely differ.

Two further pricing facts:

- Each tree applies its own configured **research cost reduction**, so a researched discount on one
  tree does nothing for another.
- Pool costs are **rounded to two significant digits**, like most prices in the game. See
  [value-computation.md](value-computation.md).

A persistent reset (NG+) clears recipe and glyph discovery along with each tree's choice state, so
**every ladder restarts at its base price on a new run**. See
[time-and-prestige.md](time-and-prestige.md).

## Where offers appear

Discovery **offers are events, not a standing page**. There is no inbox that collects pending
discoveries; they surface where and when the game raises them. World > Aspects has three pedestal
slots holding placed aspects, but it is a display of what you have placed, not a list of what is on
offer.

The first time rune of a run costs `100` mana and is always the Discovery-rarity one.

## The pool is authored, not improvised

Which candidates a tree can offer is fixed in the game's data ahead of the roll: a given glyph
unlocks a knowable set of outputs, and a roll selects from that authored set. The game deliberately
does not show it to you — not knowing what is in the pool is part of the design, not a gap in the
UI.

The concrete per-tree offer tables are not documented here; they are listed as an outstanding item
in [open-questions.md](open-questions.md).

## Related pages

- [glyphs-augments-recipe-books.md](glyphs-augments-recipe-books.md) — pool unlockers versus
  socketable augments, and why three names describe two things.
- [casting-and-spells.md](casting-and-spells.md) — what you do with a discovered spell.
- [time-and-prestige.md](time-and-prestige.md) — time runes and what a reset clears.
- [resources.md](resources.md) — the currencies the ladders are priced in.
