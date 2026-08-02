# Consumables

Consumables are the game's stockpiled one-shot items: fruits, potions, relics, scrolls and threads.
They are used from the Inventory panel or a hotbar key and arrive through crafting and gathering.

## Family tags

The data authors **68 consumables**, each carrying one or more family tags: Fruit, Potion, Relic,
Scroll, Thread, Treasure, Food, Modification, Resource. The tags mix two roles, and the full mapping
is **not yet established** — it gets recorded as it is played, not inferred:

- **Source**: Fruit marks an item gathered from a fruit tree (see [agromancy.md](agromancy.md)).
- **Effect**: a fruit can be a **Relic** or a **Food**, and that side determines what it does — a
  Relic is a permanent, immediate effect (advancement levels and similar); a Food is a temporary
  buff.
- **Scroll** applies an enchantment to a target.

In the audited data four items are tagged both Fruit and Relic — Blitz Berry, Continuous Coconut,
Frugal Fig and Power Pear — consistent with the source/effect split. Which role Potion, Thread,
Treasure, Modification and Resource each play is unrecorded.

## Permanence and preparation

`24` consumables are authored as permanent (no duration). Using one is not instant — every consumable
runs a preparation period first:

| Group | Count | Preparation |
|---|---|---|
| Scrolls | `8` | `1` s |
| Fruit + Relic permanents | `4` | `5` s |
| Treasure + Relic permanents | `12` | `8` s |

## Using an item

1. The cost is paid.
2. Stock drops by `1` and queued quantity rises by `1`.
3. Preparation begins. **Only one item prepares at a time**; the inventory refuses a second while one
   is in flight.
4. The effect fires. Temporary items then hold an active usage for their duration; permanent items
   are done.

Some items **replenish**: after firing, the cost is refunded and a unit is granted back for free.
Replenishment is a researchable bonus rather than an authored property of the item — its in-game
name is unrecorded. The grant goes through the [carry rules](carry-limits.md) like any other gain.

The player's multi-buy setting governs consumables too: it decides how many units a single use action
collects.

## Scroll targeting

You aim a scroll at a target of your choice; an optional **random mode** instead picks randomly from
the scroll's eligible pool. Each scroll carries a target structure defining which entities it can
enchant; the eligible list is computed against the strongest level of that scroll you currently own,
and an empty eligible list means the scroll has nothing valid to enchant.

## Buff shape

A buff consumable's effects are usually targeted at a resource **type**, so items whose types overlap
on a dual-typed resource both land on it. E.g., one observed food fruit gives, for `12.9` s,
`+98.2 %` Total Mental Resources Gained and `+65.5 %` Mental Missing / min — a gain multiplier plus a
missing-percent fill (see [growth-levers.md](growth-levers.md)). The item's level raises the numbers.
