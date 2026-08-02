# Game Systems

How Orb of Creation actually works: the systems, the math, and how they interact. Written for
a player who wants to understand the game — and read at the start of every session that drives
or reasons about it. Facts only; how to *play well* lives in [`../strategy/`](../strategy/README.md).

Everything here describes game version 1.0.5. Claims are either decompiled from the game's
code, observed directly in-game, or explained by the game's design; anything unverified is
marked as such inline. Unknowns are collected in [open-questions.md](open-questions.md).

## Reading order

1. [value-computation.md](value-computation.md) — how the game computes every number: caching,
   modifier folding, rounding, big-number behavior. Read this first; everything depends on it.
2. [resources.md](resources.md) — the growth terms, capacity and overcap, splash, resource
   types and keywords.
3. [casting-and-spells.md](casting-and-spells.md) — casting, costs, cooldowns, channels,
   charging, the loadout, output/reserve levels.
4. [glyphs-augments-recipe-books.md](glyphs-augments-recipe-books.md) — the naming minefield,
   and how spell augmentation really works.
5. [mastery-and-xp.md](mastery-and-xp.md) — the earned-by-doing tracks: spell mastery, spell
   types, casting level, spell levels.
6. [discovery.md](discovery.md) — the roll/pick layer: pools, pricing ladders, rerolls, and
   the commitment point.
7. [attributes-upgrades-development.md](attributes-upgrades-development.md) — Attributes
   (structures), upgrades, the development queue, cost scaling.
8. [progression-advancements.md](progression-advancements.md) — tab XP, Orb XP, advancement
   currencies, research, orbs, and the requirement graph.
9. [concepts.md](concepts.md) — the Scholarism line: slots, stacks, drain, concept mastery.
10. [time-and-prestige.md](time-and-prestige.md) — time runes, Time Advancements, challenges,
    NG+ and what persists.
11. [world.md](world.md) — Agromancy, Druidry, Aspects, Dimensional.
12. [consumables-and-items.md](consumables-and-items.md) — fruits, potions, relics, scrolls,
    threads; carry limits and what happens at capacity.
13. [crafting-and-equipment.md](crafting-and-equipment.md) — Scribe, Workshop crafting,
    artifacts and their loadout.
14. [ui-map.md](ui-map.md) — every tab and screen, and the seven interaction patterns the
    whole game is built from.
15. [open-questions.md](open-questions.md) — what we don't know yet.

## Vocabulary

The game, its code, and its own tooltips sometimes use different names for the same thing.
The load-bearing translations appear in each page where they matter; the quick table lives in
[ui-map.md](ui-map.md#vocabulary).
