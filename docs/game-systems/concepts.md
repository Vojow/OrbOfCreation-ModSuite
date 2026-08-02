# Concepts

[Back to game systems](README.md)

Concepts are a slow, passive scaling layer that lives under the Scholar side of the game. They
are cheap to run once understood and easy to misfile, because three different things in this
game are named almost the same.

## The naming reality

- **Scholarism** is the *tab*, not the feature. Buying the Scholarism attribute opens the
  Scholar line; Concepts arrive one or two milestones later, behind **Conceptualization**.
  Reaching the Scholarism tab does not mean you have Concepts yet.
- **Concepts** are the feature described on this page.
- **Alchemy** is a completely unrelated feature that appears several tabs later, with its own
  Learn and Loadout pages — see
  [crafting-and-equipment.md](crafting-and-equipment.md#alchemy-the-real-feature).

The confusion is not the player's fault: the game's own internals implement Concepts using
alchemy-named types, so anything that reads the game's data will call a concept an "alchemy
recipe" and a filled concept slot an "alchemy instance". The player-facing separation is
absolute — the Alchemy screen has nothing to do with Concepts.

The game also uses **advance** as the word for one concept level: a concept's tooltip reads,
for example, `25 s advance`, meaning it gains one level every `25` seconds while slotted.

## Discovering a concept

Concepts are discovered, not bought outright, and each discovery costs Psi at an escalating
price — the first is `20` Psi, the second `100`, and it climbs from there. The roll works like
every other discovery in the game (see [discovery.md](discovery.md)); Concepts have their own
discovery tree, so their price ladder does not interact with spell or glyph rolls.

The game contains `46` authored concept recipes, so discovery keeps producing new ones for a
very long time. A newly discovered concept can arrive already at a high mastery level: one
concept picked mid-run was usable at mastery `32` the instant it was discovered.

## What a discovered concept does

Once discovered a concept exists permanently. Two things then happen continuously:

- It costs a **constant background drain** — a per-second cost in Psi, sometimes plus a second
  resource. The drain is paid whether or not you are looking at the screen.
- It **levels passively** over time. Every advance interval it gains one level, and each level
  applies its effect again.

Every concept does one of exactly two things:

| Shape | What it does | Observed type name |
|---|---|---|
| Cost reduction | Reduces the cost of an entire group of attributes | Reductive |
| Per-development yield | Generates a resource every time an attribute level is developed | Reflective |

Two concepts observed in play, with their live numbers:

| Concept | Type | Effect | Drain | Advance |
|---|---|---|---|---|
| Restorative Learning | Reflective | `+91.4` mana per attribute developed | `0.3` Psi/s | `25` s |
| Study Mind | Reductive | `-8.03 %` Scholar cost per advance, `×1.004`/advance | `0.25` Psi/s + `0.5` Knowledge/s | `90` s |

Concepts **scale** what you already have. They do not unlock new content — that is Research's
job (see [progression-advancements.md](progression-advancements.md)). The bonus is
nevertheless substantial, and because the whole loop is passive it is easy to set once and
forget for a long stretch of play.

## Slots, stacks and concept mastery

A discovered concept does nothing until it is placed in a **concept slot**. You start with one
slot; more are unlocked by upgrades.

Each concept carries its own **mastery** level, which rises over time as the concept is used.
Concept mastery does not give the effect directly — it sets **how many stacks of that concept
you may put into a single slot**.

Stacks are the tuning knob and they cut both ways:

- more stacks in a slot make that concept **level faster**;
- more stacks also **raise its drain**.

So the real decision is a balance between two things you can see: how high you can keep
concept levels across the board, and how much permanent drain the run can absorb without
starving everything else. Levelling is where the payoff lives; drain is what it costs.

## An empty slot is normal

Removing a concept does not shrink the slot list — the game keeps the slot and leaves it
empty. During a swap you will briefly see a blank row between filled ones, and that blank row
is saved to disk as-is. It is a placeholder, not lost progress; the next concept you add
reuses it.

## The invented-slot quirk

There is a known oddity in how this game handles buying into a full set of slots: if you buy
when **zero** slots are free, the game invents an extra slot in the interface to hold the
purchase. A later upgrade that adds a slot then adds one on top of that, and the extra one
stays forever empty.

Marked as **recalled from play, not re-observed**: only around three places in the game are
believed to behave this way, and the one clearly remembered is a crafting screen. Whether
Concepts is one of the three is **unconfirmed** — see
[open-questions.md](open-questions.md).

## Related pages

- [resources.md](resources.md) — Psi, and what a permanent per-second drain does to a resource's
  net rate.
- [discovery.md](discovery.md) — how rolls, pools and per-tree price ladders work.
- [mastery-and-xp.md](mastery-and-xp.md) — the other earned-by-doing tracks; concept mastery
  behaves like them.
- [ui-map.md](ui-map.md) — where the Scholar tab's pages sit.
