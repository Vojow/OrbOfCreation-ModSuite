# Glyphs, Augments and Recipe Books

Two completely different systems share the word *glyph*, and the game itself uses a third name for one
of them. Sorting the vocabulary out is the whole first half of this page; the second half is the
authoritative model of how spell augmentation actually works, which is not what the UI suggests at
first glance.

## The naming minefield

Three names, two systems:

| Name you will see | What it actually is | Where it appears |
|---|---|---|
| **Glyph** | A pool unlocker — a tech-tree badge. A spell, augment or other rollable is in the pool only if all of its glyphs are unlocked. | Upgrade tooltips ("Learn Insight", "Learn Psionic", "Learn Storm"), the Glyph Discoveries tree |
| **Recipe Book** | The same thing as the above. The game's own item tooltips call the pool unlockers Recipe Books ("Expansion = Recipe Book: widen and enlarge"). | Item tooltips |
| **Spell Augment** / **Augment Glyph** | The socketable modifier you attach to a spell — Quick, Heavy, and so on. Crafted in **Glyphcraft**. | Magic > Augments, the Augment Glyphs panel on the Spell Loadout page |

The overlap is historical: the game is one developer's decade-long project and the naming changed
several times. Nothing in it is broken; you simply cannot infer which system a tooltip means from the
word *glyph* alone. The reliable test is **what it does**: unlockers expand what you can later roll,
augments change a spell you already own.

This page uses **glyph (unlocker)** and **augment** where the distinction matters, and follows the
game's "Augment Glyphs" label when describing that specific panel.

## Glyphs as pool unlockers

Unlocker glyphs are option-space expanders. They contribute no rate and no power of their own; they
change what the discovery pools *contain*. Observed examples: Learn Insight grants the Manifestation
glyph and unlocks the Insight resource; Learn Psionic grants the Elemental glyph and opens a new
spell/augment pool; Learn Storm grants the Storm and Spark books.

Because a rollable needs **all** of its glyphs unlocked to enter the pool, unlockers are also the lever
that controls *when* it is worth paying to roll — a pool you enrich before drawing from gives better
options for the same price. Glyph Discoveries is its own discovery tree with its own price ladder,
separate from Spell Discoveries; see [discovery.md](discovery.md).

## Spell augments: where they come from

Augments are crafted at **Glyphcraft** (Magic > Augments > Glyphcraft), one of the game's
compose-and-confirm discovery surfaces: you pay a cost, compose components, and the game resolves what
you get. There is no recipe list to browse. The **Augment Table** upgrade raises your maximum number
of augment slots by one each time it is bought.

Augments are deliberately double-edged — most of them are, in the game's phrasing, "pros and cons"
items:

| Augment | Effect on the spell it is socketed into |
|---|---|
| Quick | ×1.15 cost, ×0.70 cooldown |
| Heavy | ×1.40 power, +10 % Special, +100 % cooldown, −10 % cast speed |

(The **Special** statistic multiplies the non-scaling multiplicative components of an effect — for
example a charm's ×1.12 Magic Resources Gained row.)

## The augment model

This is the part the UI hides. The model below is authoritative; several intuitive readings of the
Augment Glyphs panel are wrong.

### Each spell has its own glyph slots

Augment slots belong to the **spell**, not to a shared board. What you configure is a per-spell
layout.

### You own N usable copies of each augment

The Augment Glyphs panel shows a count per augment — the familiar **"1/1"** reading. That is a
**usable-copy count**, not a slot count and not an on/off state. You raise it by **upgrading** that
augment.

Each purchased augment level grants:

- **+1 Max Usage** per level, and
- **+1 Free Usage** per **six** levels.

Quick starts at Max Usage 1 and Free Usage 0, so at level `L` it has Max Usage `1 + L` and Free Usage
`floor(L / 6)` absent outside modifiers.

The two counters mean different things:

- **Max Usage is loadout-wide.** It is the total number of copies of that augment available across
  your whole equipped loadout, enforced after subtracting the copies already equipped.
- **Free Usage is per spell.** That many copies of the augment *in each spell* do not charge the
  augment's usage cost. Every non-free copy costs **one Spell Weight**, and copies are never merged
  into a single weight charge.

So four copies of Quick spread across four spells draw four against Max Usage, while Free Usage 1
would waive the weight for the first copy in each of those spells independently.

### The layout is chosen before the spell is added

This is the rule that catches everyone:

1. You set the glyph layout for a spell **first**.
2. **Adding the spell to the loadout bakes those glyphs in** and raises the spell's load cost
   accordingly.
3. Changing the glyphs on a spell that is already loaded means **remove the spell → change the layout
   → add it back**.

**There is no in-place augment change in the UI.** Re-adding a spell also means re-paying the cooldown
downtime of that lane, and a spell cannot be swapped out while it is on cooldown — see
[casting-and-spells.md](casting-and-spells.md).

### Socketed copies compound

Each socketed copy applies its factors to that spell, and multiple copies of the same augment
**compound as powers**. For `q` copies of Quick on one spell the spell takes ×`1.15^q` cost and
×`0.70^q` cooldown. This is why stacking copies is a real strategy shape rather than a rounding error,
and why the weight charge is per copy.

### Levels also grant global passives, even unsocketed

Every **purchased level** of an augment applies its own global modifiers regardless of whether the
augment is socketed anywhere. For Quick, each level applies **×1.04 Cantrip cooldown speed and ×0.94
Cantrip cost**, globally.

That splits an augment into two separate value streams: the socket effect (large, targeted, weighted,
double-edged) and the level passive (small, global, free of weight, permanent for the run). Levelling
an augment you never socket is still a real effect.

### Socketing costs weight

Socketing a non-free copy adds **+1 weight** to that spell's load cost — observed directly: Whirling
Sorcery went from weight 2 to weight 3 after Quick was socketed. Load cost growth after an add is the
glyph, not spell levelling.

### Augments can change a spell's effective types

Glyph setup runs before a spell's types are established and can add or replace elemental tags. A
tag-targeted buff therefore resolves against the spell's **effective** type list, which glyphs can
change. Never infer targeting from the spell's printed name; see
[casting-and-spells.md](casting-and-spells.md#keywords-and-the-spell-type-taxonomy).

## Paying for augment upgrades

Augment upgrading is bought with the **Glyph Upgrades** advancement currency — earned by levelling the
Wizardry and Shaper progression tabs, not with ordinary resources. Two consequences:

- Advancement currencies are **finite within a run**: attribute levels eventually get too expensive to
  keep buying, so the supply stops. Only a reset refills the curve.
- **An augment upgrade cannot be undone during a run.** The allocation is permanent until the next
  reset.

See [progression-advancements.md](progression-advancements.md) for how advancement currencies are
earned and why nothing is wasted at cap, and [time-and-prestige.md](time-and-prestige.md) for what a
reset restores.

## Quick reference

| Question | Answer |
|---|---|
| Does levelling an augment help if it is socketed nowhere? | Yes — the per-level global passives apply regardless. |
| What does "1/1" on the Augment Glyphs panel mean? | Usable copies of that augment, currently available / total. |
| Can I move an augment between two loaded spells? | Not in place. Remove, change the layout, re-add. |
| Do two copies of Quick on one spell halve the cooldown twice? | They multiply: ×0.70², and ×1.15² cost. |
| Is Max Usage per spell? | No — loadout-wide. Free Usage is the per-spell one. |
| Does socketing cost anything besides the augment? | +1 weight per non-free copy, and the load cost is locked in at add time. |
