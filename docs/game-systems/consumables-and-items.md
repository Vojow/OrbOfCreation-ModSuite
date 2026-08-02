# Consumables and items

[Back to game systems](README.md)

Consumables are the game's stockpiled one-shot items: fruits, potions, relics, scrolls and
threads. They are used from the Inventory panel or a hotbar key, they are produced by crafting
(see [crafting-and-equipment.md](crafting-and-equipment.md)), and they follow a set of
capacity rules that can silently eat a paid craft if you do not know them.

## Families

The game authors **68 consumables** across exactly **8 family patterns**. Five family names
describe what an item *does*:

| Operation family | What it is |
|---|---|
| Fruit | Temporary buff item |
| Potion | Temporary buff item |
| Relic | Permanent, immediate effect (advancement levels and similar) |
| Scroll | Applies an enchantment to a target |
| Thread | Temporary buff item |

The remaining family tags — **Treasure**, **Food**, **Modification**, **Resource** — are not
operations. They describe *how an item is acquired* or what category it belongs to. A
"treasure relic" and a "fruit relic" are both simply Relics; Treasure and Fruit tell you where
they came from.

### The one genuine cross-family set

Exactly **four** items carry two operation families at once — `Fruit + Relic` — and all four
behave as **Relics**:

- Blitz Berry
- Continuous Coconut
- Frugal Fig
- Power Pear

These are the permanent fruits. Their Fruit tag is an acquisition label; their behavior is
Relic. No other combination in the game spans two operations.

### Permanent items and preparation times

`24` consumables are authored as permanent (no duration). Using one is not instant — every
consumable runs a preparation period first:

| Group | Count | Preparation |
|---|---|---|
| Scrolls | `8` | `1` s |
| Fruit + Relic permanents | `4` | `5` s |
| Treasure + Relic permanents | `12` | `8` s |

## Carry limit and what happens at capacity

Carry capacity is a **single global value, enforced separately for each exact item**. Every
distinct consumable — Advancement Scroll, Power Scroll, Blitz Berry — independently gets the
same limit. It is **not** a shared pool across a family, and it is **not** per level row.

Each unit you hold has a **level**. When a new unit arrives and you are already at capacity,
the incoming unit is compared against the **weakest unit you own**, and the comparison is on
level alone:

| Situation | Incoming level | Outcome |
|---|---|---|
| Below capacity | any | Added to a free slot. Nothing is removed. |
| At capacity | strictly **stronger** than the weakest | The weakest unit is removed and the incoming one is kept. A real upgrade. |
| At capacity | **equal** to the weakest | A unit is churned out and replaced. Your level coverage does not improve, though non-level attributes may. |
| At capacity | strictly **weaker** than the weakest | The incoming unit is **silently lost**. No message, no refusal. |
| Carry capacity `0` or below | any | **Every** gain is suppressed. Nothing is ever added. |

"Weakest" means lowest level and nothing else — power and other attributes are not
tie-breakers — and ties keep whichever unit the game happens to hold first.

### Payment happens before the capacity decision

This is the sharp edge. A craft **pays its cost when you submit it**, and the capacity
decision happens **when it completes**. Instant crafting and queued crafting run the exact
same completion path; there is no separate admission rule for queued items.

So two things follow:

- A craft that completes weaker than your current weakest owned unit is **paid for and then
  silently lost**.
- A queued craft that was stronger than your weakest unit at submission time can become equal
  or weaker by the time it completes — because the crafts ahead of it in the queue raised your
  floor — and is then churned or lost on the same rule.

The game returns no admission result to whoever paid. Nothing in the interface announces the
loss.

## Toxicity

Using items fills a **Toxicity meter**, and items cannot be used while it is full. Toxicity is
a real resource with a cap and the game's unusual growth levers pointed in reverse:

- Each use adds the item's Toxicity cost to the meter (food fruits observed at `8`).
- The meter **drains back down over time**, and the drain follows a missing-percent shape in
  reverse: the fuller the meter, the faster it empties.
- It also carries a **resting rate**: go a while without using any items and recovery speeds
  up further.
- Research nodes modify these aspects separately.

In effect Toxicity is a **rate limiter on item usage**, and it creates a real timing decision:
drain to empty and burst a stack of items in one window (the natural fit when lining item
effects up with spells and charm windows), or keep the meter topped and use items steadily as
headroom appears. The levers themselves are described in
[resources.md](resources.md#resting-rate).

### A worked fruit: Brain Berry

One captured tooltip shows the whole food-fruit shape at once — **Brain Berry, Lv 9, Fruit
Food**, costing `8` Toxicity:

> For `12.9` s: `+98.2 %` Total Mental Resources Gained, `+65.5 %` Mental **Missing / min**.

Two effects, both targeted at a resource *type*: a gain multiplier, and a per-minute fill of
the **missing** amount measured against capacity. Fruits of different types stack, and a
dual-typed resource sits in both blast radii — eating a Mental fruit and an Energetic fruit
together hits Psi (Mental + Energetic) with both fills. The item's level raises the numbers;
the missing-percent mechanics live in [resources.md](resources.md).

## Using an item

Using a consumable produces an immediate, visible receipt and then a delayed effect:

1. The cost is paid.
2. Stock drops by `1` and queued quantity rises by `1`.
3. Preparation begins — `1` to `8` seconds depending on the item (see the table above). Only
   one item prepares at a time; the inventory refuses a second while one is in flight.
4. The effect fires. Temporary items then hold an active usage for their duration; permanent
   items are done.

Some items **replenish**: after firing, the cost is refunded and a unit is granted back for
free. That grant goes through the same capacity rules as any other gain.

The player's multi-buy setting also governs consumables: it decides how many units a single
use action collects.

### Scroll targeting

Scroll-type consumables **target randomly within their authored pool**. Each scroll carries a
target structure that defines which entities it can enchant; the eligible list is computed
against the strongest level of that scroll you currently own, and one entry is chosen at
random. An empty eligible list means the scroll has nothing valid to enchant.

## Related pages

- [crafting-and-equipment.md](crafting-and-equipment.md) — the Scribe and Workshop pages that
  produce these items, and the Starting Level that sets a scribed scroll's level.
- [resources.md](resources.md) — Toxicity and the other capped resources.
- [progression-advancements.md](progression-advancements.md) — what Relics that grant
  advancement levels are actually paying into.
- [open-questions.md](open-questions.md) — Zeal and unverified item effects.
