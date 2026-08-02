# Spell augments

Augments are the socketable modifiers attached to a spell. They are crafted at **Glyphcraft**, one of
the compose-and-confirm discovery surfaces (see [discovery.md](discovery.md)), and the **Augment
Table** upgrade raises your maximum number of augment slots by one each time it is bought.

Augments are deliberately double-edged. Two worked examples — not the set; the authored catalogue is
unknown (see [open-questions.md](open-questions.md)):

| Augment | Effect on the spell it is socketed into |
|---|---|
| Quick | ×1.15 cost, ×0.70 cooldown |
| Heavy | ×1.40 power, +10 % Special, +100 % cooldown, −10 % cast speed |

(**Special** multiplies the non-scaling multiplicative components of an effect, such as a charm's
×1.12 Magic Resources Gained row.)

## Slots belong to the spell

Augment slots belong to the **spell**, not to a shared board. What you configure is a per-spell
layout.

## Usable copies, not slots

The Augment Glyphs panel's "1/1" reading is a **usable-copy count**, not a slot count and not an
on/off state. Each purchased augment level grants **+1 Max Usage** and **+1 Free Usage per six
levels**; e.g., an augment starting at Max Usage 1 and Free Usage 0 has Max Usage `1 + L` and Free
Usage `floor(L / 6)` at level `L`, absent outside modifiers.

- **Max Usage is loadout-wide** — the total copies of that augment available across the equipped
  loadout, enforced after subtracting copies already equipped.
- **Free Usage is per spell** — that many copies *in each spell* do not charge the augment's usage
  cost. Every non-free copy costs **one Spell Weight**, and copies are never merged into a single
  weight charge.
- A third waiver, **loadout-wide free usages**, waives copies across the whole loadout rather than
  per spell.

## The layout is chosen before the spell is added

1. Set the glyph layout for a spell first.
2. **Adding the spell to the loadout bakes those glyphs in** and raises its load cost accordingly.
3. Changing the glyphs on a loaded spell means remove the spell, change the layout, add it back —
   which re-pays that lane's cooldown downtime.

There is no in-place augment change.

## Socketed copies compound

Each socketed copy applies its factors to that spell, and multiple copies of the same augment
compound as powers: `q` copies of a ×1.15-cost, ×0.70-cooldown augment give ×`1.15^q` and ×`0.70^q`.
Socketing a non-free copy adds +1 weight to that spell's load cost, so load-cost growth after an add
is the glyph, not spell levelling.

## Levels grant global passives even unsocketed

Every **purchased level** of an augment applies its own global modifiers regardless of whether the
augment is socketed anywhere — e.g., ×1.04 Cantrip cooldown speed and ×0.94 Cantrip cost per level.
That splits an augment into two value streams: the socket effect (large, targeted, weighted,
double-edged) and the level passive (small, global, weightless, permanent for the run).

## Augments can change effective types

Glyph setup runs before a spell's types are established and can add or replace elemental tags, so a
tag-targeted buff resolves against the changed list; see [spell-types.md](spell-types.md).

## Paying for augment upgrades

Augment upgrading is bought with the **Glyph Upgrades** advancement currency rather than ordinary
resources, and the allocation is permanent until the next reset. See
[advancement-currencies.md](advancement-currencies.md).
