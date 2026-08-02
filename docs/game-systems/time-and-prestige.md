# Time, runes and prestige

[Back to game systems](README.md)

The Time tab holds everything that outlives a single run: Time Runes and the Time Advancements
that pay for them, Challenges, and the Reset page that starts the next run. This is the game's
prestige layer.

## Time Runes

Runes are acquired through the **Runecraft** page (Time > Time Runes > Create), which uses the
same compose-and-confirm layout as every other discovery surface in the game — you supply
components, the game resolves the output. The first rune of a run costs `100` mana and is
always the Discovery-rarity one. Across a full run you can expect roughly **7 to 10 runes**.

### Three level systems per rune

The single most confusing thing about runes is that each one carries **three separate level
numbers**, and they mean different things.

| Track | Bought with | Cost curve | What it does |
|---|---|---|---|
| **Rune level** | Time Advancements, this run only | `0, 1, 2, 3, 4, …` — the first level is free | Grants persist XP and raises what future picks of that rune give (`+25 %` per level) |
| **Persist level** | Persist XP, across runs | Escalating thresholds: `100`, `250`, `400`, … | Where the permanent effects actually live |
| **Mastery level** | Mastery XP, roughly `1` per rune level bought | Escalating | Each mastery level grants `+1` **starting Time Advancement** next run |

Consequences worth spelling out:

- A rune left at **level 0 is inert** — it grants no persist XP at all. The free first level is
  what switches a rune on.
- Persist XP **rolls over**. Overshooting a threshold is not wasted; the remainder counts
  towards the next one.
- Picking an Investment rune grants `100` persist XP by default; levelling the rune raises the
  XP granted per pick for the rest of that run.
- All three tracks have diminishing returns, and rune levels get more expensive as you buy
  them — which is why Time Advancements tend to be spread over several runes rather than
  poured into one.

### Only Persistent runes are cross-run capital

Not every rune persists. Runes carry a **Persistent** tag (the Investment family has it;
Tempo and Scaling variants do not). Runes without the tag are run-local: they do not count
towards any persistent-rune rule and leave nothing behind at reset.

### Persist effects survive without repicking

This one matters and it is easy to get backwards. On a persistent reset the **rune itself is
cleared** — its level, its discovery, and its discovery rarity are all wiped, so the rune is
not still picked in the new run. What survives is the **persistent advancement levels the rune
already granted**, and those keep applying whether or not you ever pick that rune again.

Repicking a rune is therefore only needed to buy *more* levels and bank *more* persist XP —
never to keep what you already earned.

### Meta-runes

Some runes buff whole *classes* of runes rather than a game system. "Persist Persist", for
example, grants `+12 %` Investment Power — which multiplies its own XP gain, because it is
itself an Investment rune. Rune effects can be **reflexive** (a rune affecting itself) and
**retroactive** (a class multiplier applying to a rune of that class you already picked
earlier in the run).

Investment Power stacks additively rather than multiplicatively: Persist Persist grants
`+12 %` per persist level, so three levels turn a base `225` pick into exactly `306`
(`×1.36`), not `316`. See [value-computation.md](value-computation.md) for why.

One live tooltip ties the whole model together. Persist Persist at persist level `4` (`169` XP
banked, next threshold `800`), with its next rune level priced at `7` TA, showed **Gain 407
XP** per pick — exactly `100 × (1 + 0.25 × 7) × (1 + 0.12 × 4)`: the base pick XP, times the
rune's `+25 %`-per-level pick bonus at rune level `7`, times its own `+12 %`-per-persist-level
Investment Power, reflexively applied to itself. The same tooltip read its mastery bar as
`3.56/6` — rune mastery XP accrues in fractions, and the level-1 threshold is `6`.

## Time Advancements

Time Advancements (TA) are the currency that buys rune levels. Two facts define them:

- They are **refunded on every world reset**. TA is a pure allocation currency — you are
  deciding where it sits, never whether to consume it.
- Your starting TA next run is the sum of your rune mastery (`+1` per mastery level) and your
  Achievement Strength (`+1` per point).

## Achievement Strength

Achievement Strength is a single number earned by completing achievement levels, and it pays
twice:

- `+1 %` global resource gain per point. `28` points is exactly `×1.28` All Resources Gained.
  This is why a brand-new resource in a later run can already read `Gain 128 %` with no
  keyword upgrade in sight — it is the prestige buff, not something you bought.
- `+1` starting Time Advancement per point, once the time-reset prerequisite is met.

## Challenges

Challenges live at **Time > Challenges**.

- New offers are fetched with the **New Challenges** button; they arrive Inactive and are
  activated per row.
- **Up to three challenges can be active at once.**
- Any active challenge can be **abandoned**, and a completed one shows a **Passed** state.

Challenges do more than scale numbers: they can **modify requirements**, applying as passive
modifiers on the requirement graph. A challenge named "Focus: Improved Scribing" applied
`-5` Improved Scribing Requirements, which showed up in-game as `leeway 5` on that research
node. That means an active challenge can put content within reach that would otherwise be
gated — the requirement itself moved, not just its cost.

Because they can be selected by difficulty, the set of active challenges is a real input into
what a run can afford to attempt.

## Reset and NG+

Prestige lives at **Time > Reset**, and that page publishes exactly three decision facts:

| Fact | Example from one save |
|---|---|
| Starting Time Advancements next run | `83` |
| Delta versus the last run | `+9` |
| What survives the reset | Challenges, Achievements, Time Advancements |

Everything not on that survival list is lost: resources, attributes, upgrades, spells, glyphs,
concepts, research, and the runes themselves — though, as above, the persistent advancement
levels runes granted keep applying.

Two further effects of NG+ are worth knowing because nothing else in the game does them:

- **Cost curves reset only at NG+.** Advancement and progression costs climb until attributes
  become unaffordable; an ordinary reset does not undo that escalation, and NG+ does. The
  per-tree discovery price ladder resets at NG+ as well.
- **NG+ starts you with bonus orb levels**, some pre-allocated and some free to allocate. Free
  bonus levels do not advance the paid cost curve — a level 2 with 2 bonus levels costs what a
  bare level 2 costs. See [progression-advancements.md](progression-advancements.md).

## Related pages

- [progression-advancements.md](progression-advancements.md) — advancement currencies,
  research, orbs, and the requirement graph challenges modify.
- [mastery-and-xp.md](mastery-and-xp.md) — spell mastery, the other track that converts into
  starting Time Advancements.
- [discovery.md](discovery.md) — how the Runecraft roll works and what a per-tree ladder is.
- [ui-map.md](ui-map.md) — where the Time tab's pages sit.
- [open-questions.md](open-questions.md) — unresolved prestige questions.
