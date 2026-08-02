# Consumables

Consumables are the game's stockpiled one-shot items: fruits, potions, relics, scrolls and threads.
They are used from the Inventory panel or a hotbar key and produced by crafting.

## Families

The game authors **68 consumables** across exactly **8 family patterns**. Five family names describe
what an item *does*:

| Operation family | What it is |
|---|---|
| Fruit | Temporary buff item |
| Potion | Temporary buff item |
| Relic | Permanent, immediate effect (advancement levels and similar) |
| Scroll | Applies an enchantment to a target |
| Thread | Temporary buff item |

The remaining family tags — **Treasure**, **Food**, **Modification**, **Resource** — are not
operations. They describe how an item is acquired or what category it belongs to: a "treasure relic"
and a "fruit relic" are both simply Relics.

Exactly **four** items carry two operation families at once (`Fruit + Relic`) and all four behave as
**Relics**: Blitz Berry, Continuous Coconut, Frugal Fig and Power Pear. No other combination in the
game spans two operations.

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
That grant goes through the carry rules like any other gain.

The player's multi-buy setting governs consumables too: it decides how many units a single use action
collects.

## Scroll targeting

Scroll-type consumables **target randomly within their authored pool**. Each scroll carries a target
structure defining which entities it can enchant; the eligible list is computed against the strongest
level of that scroll you currently own, and one entry is chosen at random. An empty eligible list
means the scroll has nothing valid to enchant.

## Buff shape

A buff consumable's effects are usually targeted at a resource **type**, so items whose types overlap
on a dual-typed resource both land on it. E.g., one observed food fruit gives, for `12.9` s,
`+98.2 %` Total Mental Resources Gained and `+65.5 %` Mental Missing / min — a gain multiplier plus a
missing-percent fill (see [growth-levers.md](growth-levers.md)). The item's level raises the numbers.
