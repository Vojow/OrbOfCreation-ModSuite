# Concepts

Concepts are a slow, passive scaling layer under the Scholar side of the game. They arrive behind
**Conceptualization**, one or two milestones after the Scholarism attribute opens the Scholar line —
reaching the Scholarism tab does not mean you have Concepts yet. They are unrelated to the Alchemy
screen; see [vocabulary.md](vocabulary.md).

The game uses **advance** for one concept level: a tooltip reading `25 s advance` means the concept
gains one level every 25 seconds while slotted.

## Discovering a concept

Concepts are discovered, not bought outright, and each discovery costs Psi at an escalating price —
e.g., 20 Psi for the first and 100 for the second, climbing from there. Concepts have their own
discovery tree, so their ladder does not interact with spell or glyph rolls. The game contains `46`
authored concept recipes, and a newly discovered concept can arrive already at a high mastery level.

## What a discovered concept does

Once discovered, a concept exists permanently, and two things then happen continuously:

- It costs a **constant background drain** — a per-second cost in Psi, sometimes plus a second
  resource, paid whether or not you are looking at the screen.
- It **levels passively**: every advance interval it gains a level, and each level applies its effect
  again.

Every concept does one of exactly two things:

| Shape | What it does |
|---|---|
| Cost reduction | Reduces the cost of an entire group of attributes |
| Per-development yield | Generates a resource every time an attribute level is developed |

E.g., two observed concepts: one yielding `+91.4` mana per attribute developed for `0.3` Psi/s on a
`25` s advance; one reducing Scholar cost by `-8.03 %` per advance (`×1.004`/advance) for `0.25`
Psi/s + `0.5` Knowledge/s on a `90` s advance.

Concepts **scale** what you already have; they do not unlock new content.

## Slots, stacks and concept mastery

A discovered concept does nothing until it is placed in a **concept slot**. You start with one slot;
more are unlocked by upgrades.

Each concept carries its own **mastery** level, which rises over time as the concept is used. Concept
mastery does not give the effect directly — it sets **how many stacks of that concept fit into a
single slot**. More stacks make that concept level faster and also raise its drain.

## An empty slot is normal

Removing a concept does not shrink the slot list: the game keeps the slot and leaves it empty, and
the blank row is saved to disk as-is. It is a placeholder, not lost progress, and the next concept
you add reuses it.
