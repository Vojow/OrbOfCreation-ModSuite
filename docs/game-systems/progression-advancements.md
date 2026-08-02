# Progression, advancements, research and orbs

[Back to game systems](README.md)

Buying attributes does two things. The obvious one is the attribute's own effect. The other one —
the one that decides how far a run reaches — is that every completed level pays into two experience
tracks, and those tracks are what unlock the game's gated content.

## Completion pays, queueing does not

**Every completed attribute level grants `+1` XP to its tab's progression track and `+1` Orb XP.**

The word *completed* is load-bearing. Queueing the level does not pay. Paying for the level does
not pay. The level has to finish developing in the queue. A purchase that develops five levels
grants five of each.

Orb XP accrues from **every** tab. There is no attribute that pays its tab but not the orb bar.

## Thresholds and rollover

| Track | Threshold at level `L` | Sequence |
|---|---|---|
| Tab progression | `40 + 10·L` | 40, 50, 60, 70, … |
| Orb XP | `50 + 5·L` | 50, 55, 60, 65, … |

XP beyond a threshold **rolls over** into the next level; one grant can cross more than one
threshold. Nothing is lost at a boundary.

Reading the display takes a moment of care, because the number shown is the *next threshold*, not
the remainder — except when it is. `Wizardry Lv3 in 60` is the level-3 threshold with two prior
levels already applied. `Scholar Lv1 in 21` is 21 XP **remaining** out of the 40-point first
threshold. When in doubt, compare against the table above.

## A tab level raises advancement maximums — it does not hand you a point

This is the rule that most often gets modelled backwards.

When a progression tab levels, it does **not** grant one unit of an advancement currency. It adds
`+1` to the **maximum quantity** of the advancement currencies it feeds.

So if Glyph Upgrades reads `2/2` and you gain a Wizardry level, it becomes `2/3`. **Nothing is
wasted at cap** — there is no such thing as a grant arriving while you are full and being thrown
away, and therefore no urgency to spend an advancement point before the next tab level lands.

The corollary is how to read the pair of numbers. **Quantity/cap reads as remaining/earned.**
`0/2` means you earned 2 and have allocated 2, leaving 0 to spend. It is a currency displayed as a
capacity.

**Allocations are permanent within a run.** There is no refund, no respec, and no way to move a
point from one node to another once spent.

### The grant table

| Progression tab | Advancement maximums it raises, per level |
|---|---|
| Wizardry | +1 Magical, +1 Glyph |
| Scholar | +1 Cognitive, +1 Technology |
| Alchemy | +1 Cognitive, +1 Materials |
| Artificer | +1 Ability, +1 Equipment |
| Construction | +1 Technology, +1 Equipment |
| Druid | +1 Ability, +1 Magical |
| Mystic | +1 Technology, +1 Materials |
| Shaper | +1 Technology, +1 Glyph |
| Orb | +1 Orb |

Advancement maximums are also raised by effects **outside** this table — resource-type levels and
various named upgrades all target the same maximums. Magical is additionally raised by Druid,
Magical-resource-type levels and Perfect Auras; Cognitive by Alchemy, Parchment-resource-type
levels, Perfect Learning and Cognitating; Technology by Construction, Mystic, Shaper,
Spacial-resource-type levels, Perfect Laboratory, Technology Tycoon, Bending, Technology Mastery
(+2) and three conversion effects (+1 each); Glyph by Shaper and Boost Glyphs (+50%). Because these
are modifiers, the effective cap is their folded result, not a stored integer — see
[value-computation.md](value-computation.md).

### Which attribute feeds which tab

An attribute's *category* decides the tab it pays into.

| Attribute category | Progression tab |
|---|---|
| Alchemist | Alchemy |
| Arcanist, Flameweaver, Stormshaper, Wizardry | Wizardry |
| Artificer, Reinforced | Artificer |
| Dimensional | Shaper |
| Druidry | Druid |
| Mystic | Mystic |
| Scholar | Scholar |
| Workshop | Construction |

Every one of them also grants Orb XP.

## Advancements are run-finite

There is no steady income of advancement points. They come from tab levels, tab levels come from
completed attribute levels, and attribute costs grow faster than production does. At some point in
every run attributes become unaffordable, the tabs stop levelling, and the advancement supply
simply stops.

That makes the whole layer an **allocation problem with a hard budget**. Only a persistent reset
(NG+) puts the cost curves back to zero — see [time-and-prestige.md](time-and-prestige.md).

## Research

Research is unlocked by the *Innovation* upgrade and gets its own tab.

- Each node costs **typed advancement points** — the school-specific currencies from the grant
  table above.
- A node **takes time** to develop. Observed durations run roughly 15–20 seconds; one Develop
  button read `20.0s`.
- A node also **drains a resource** for the duration of its development. These drains are usually
  negligible relative to production and only occasionally bind.
- **Most nodes cap at level 1.** A few go higher.
- **Completing a node may reveal further research.** The game does not preview what a node will
  open; you find out by finishing it.

The research page shows **five spendable school-point balances** across the top — observed as
`14/24/29/1/3` on one save — and each Develop button carries its per-school point cost, rendered
red when you are short.

Because the point supply is finite and run-global, unspendable-elsewhere is not a category here:
every research node competes with every other for the same pool.

## Orbs

Orb XP feeds a **global level bar along the very bottom of the screen**, below every tab. Each
level of that bar yields an orb.

- **Each orb research level costs exactly one orb.**
- **Orb research is the disciplines of magic** — the classes/tabs the rest of the game is organised
  around.
- A great deal of later content gates on having a specific number of levels in a specific
  discipline. The run never produces enough orbs to cover everything, so orb allocation is
  permanently a question of which parts of the game you are choosing to reach.
- NG+ starts a run with some bonus levels already in place — some pre-allocated, some free for you
  to place.

## Free bonus levels: cheap on the cost curve, worthless to requirements

Bonus levels are the extra levels shown on top of what you bought (rendered in green). They have
two properties that pull in opposite directions.

**They do not advance the paid cost curve.** A node at purchased level 2 with 2 bonus levels costs
what level 2 alone costs — the next purchase is priced from your *purchased* level, not the
displayed one. Bonus levels are free effect, and they stay free.

**They do not satisfy requirements.** Which brings us to the graph.

## The requirement graph

### Displayed level is a three-term sum

What the game shows you as a node's level is:

```
displayed level = purchased level + base levels + bonus levels
```

**Requirement checks do not use the displayed term.** They evaluate purchased/base levels only.
A node showing a healthy green `+5` while its purchased level is `0` **fails** a requirement of
`≥ 5`, and the UI gives you no hint that the number it is showing you is not the number being
tested.

This is the single most confusing behaviour in the progression layer, and it is deliberate: bonus
levels are power, not progress.

### Visibility and availability are separate

A node is **hidden** until its prerequisite tier and its gating levels are met. It then becomes
visible but possibly still unavailable, and only later becomes purchasable.

So "I can't find it" is a data question, not a UI bug — the node exists, you have not met the
condition that reveals it. Conversely, seeing a node does not mean you can act on it.

Requirements can also be **per level**: the same node can demand different things at level 3 than
it did at level 1.

### Requirements reach across systems

A requirement is not restricted to the system the node lives in. Examples:

- *Arcanism II* costs only `10,000` Arcanum but requires the *Arcane Dominion* **research** to have
  at least one level. The research merely being *available* at level 0 is not enough.
- Requirements routinely demand **possession of a resource** you do not yet produce, which pushes
  the real blocker several systems away from the node you are looking at.
- Some are **hard gates** on a specific level count rather than soft scaling conditions.

### Challenges modify requirements

Challenges do not only change numbers — **they can modify requirements themselves**, applied as
passive modifiers. The observed case: the challenge *Focus: Improved Scribing* applies
**−5 Improved Scribing Requirements**, which the node displays in-game as `leeway 5`.

*[Unverified: one reading of the underlying requirement-adjustment value returned 0 while the node
correctly displayed leeway 5, so either the display is fed from somewhere else or a second
mechanism is involved.]*

See [time-and-prestige.md](time-and-prestige.md) for challenges generally.

### A worked example: why Improved Scribing will not research

This chain is the clearest illustration of everything above at once. The player wanted one research
node and could not even see it.

```
Improved Scribing            — hidden
├── requires Technology tier              — met
└── requires Scribism >= 1                — NOT met (level 0)
    ├── Scribism requires Improved Concepts >= 1   — met
    └── Scribism requires possession of Ink        — NOT met (0 Ink)
        └── Ink is produced by Refine Ink
            └── Refine Ink is hidden until Research Scribing >= 1
                └── Research Scribing requires:
                    ├── Innovation tier
                    ├── Research Electric >= 1
                    ├── possession of Elementia     — NOT met (0)
                    └── hard gate Expert Items >= 5 — NOT met
```

The last line is the trap. The player **had a visible `Expert Items (+5)` bonus** and still failed
the `>= 5` gate, because purchased level was 0 and the gate reads purchased levels.

Four levels of indirection, two resources not yet produced, and one requirement that looks
satisfied on screen and is not. This is what "hidden until its prerequisites are met" costs you in
practice, and it is why working backwards from a wanted node is the only reliable way to find the
real blocker.

## Reachability: most content has two routes, some has one

Where a purchasable appears in the UI is authored, and a candidate can belong to more than one
route. A route is a list that some screen shows; the candidate is reachable when **any** screen
carrying **any** of its lists is currently available.

Measured on one mid/late save:

| Kind | Two routes | One route |
|---|---|---|
| Attributes | 144 of 180 | 36 |
| Upgrades | 195 of 229 | 34 |

The two routes are usually one of these shapes:

- **Parent tab plus subtab.** The *Witchcraft* attribute sits in a single Wizardry list, and that
  list is shown by both the Magic tab itself and the Wizardry subtab.
- **Aggregate list plus screen list.** A Wizardry upgrade can sit in two different lists — the
  all-upgrades aggregate shown on the Upgrades panel, and the Magic screen's own upgrade list.
  Aggregate screens are legitimate routes, not summaries.

Single-route content is where things go missing. *Life Weaver* is reachable **only** through
World > Druidry, because World does not co-reference Druidry the way Magic co-references Wizardry.
If the one screen carrying a candidate is unavailable, the candidate is simply not reachable, even
though everything about it is otherwise satisfied.

The persistent right-panel Upgrades / Inventory strip is visible on every tab and is genuinely
global, not a per-tab strip.

## Related pages

- [attributes-upgrades-development.md](attributes-upgrades-development.md) — what you buy to feed
  these tracks.
- [time-and-prestige.md](time-and-prestige.md) — challenges, NG+, and what a reset restores.
- [value-computation.md](value-computation.md) — how folded modifiers produce the caps and levels
  above.
- [ui-map.md](ui-map.md) — where the research page, the orb bar and the tab counters live.
- [open-questions.md](open-questions.md) — unresolved requirement and challenge behaviour.
