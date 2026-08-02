# Crafting, equipment and loadouts

[Back to game systems](README.md)

Manual crafting produces the consumables described in
[consumables-and-items.md](consumables-and-items.md); equipment is a separate, budgeted
loadout of artifacts. Both live under Workshop, with one crafting surface parked over in the
Scholar tab.

## Where manual crafting lives

There are **two** manual crafting pages, and they look nothing alike.

**Workshop > Crafting** is a list of recipe rows. Each row shows its complete cost line —
every resource, not a summary — and the cost renders **red when you cannot afford it**. A row
can also be greyed out entirely when the recipe is not currently craftable. Paper is one of
the recipes here; `7` recipes were visible on the observed save.

**Scholar > Scribe** is a **drop slot** rather than a list: you drop a target into the slot and
scribe onto it. An "Advancement" recipe was the one visible on the observed save.

Both pages carry a **Manual | Automate** split at the top. The game itself draws the line
between one-off crafting you drive by hand and standing production, and the Manual side works
on its own regardless of what the Automate side is set to.

### Starting Level, and what it does to a scribed scroll

A scribed scroll is created **at the value of the Scribe's Starting Level dial**. That level is
what the scroll carries into your inventory, and it is therefore what decides whether the new
scroll upgrades, churns or is lost against your existing stock — see the capacity rules in
[consumables-and-items.md](consumables-and-items.md#carry-limit-and-what-happens-at-capacity).

The dial has its own maximum (`Starting Level 4/4` on the observed save), and buying levels of
the relevant recipe is what raises that maximum.

Marked as **read out of the game's data, not observed in play**: the Scribe registry holds `6`
recipes while `8` scribe roles are authored; the Investment and Speed roles have no recipe of
their own in that data.

### Paying and queueing

Crafts **pay when you submit them**, not when they complete. A queue that is already full will
still accept another craft of a recipe that can stack with what is already queued. What
happens to the finished item when it arrives is governed entirely by the consumable capacity
rules, and a paid craft can be lost there.

## The level dial pattern

The same control appears in four places across the game: a **level dial with a maximum**, next
to a **purchase that raises the maximum**. Values below are from one observed save.

| Where | Dial | Raise purchase |
|---|---|---|
| Magic > Casting | `Output Lv 51/54` — one global dial for all casting, not per spell | Raise Output Lv |
| Magic > Casting | `Reserve Lv 49/49` | Raise Reserve Lv |
| Alchemy | `Alchemy Lv 43/43` | Raise Alchemy Level |
| Scholar > Scribe | `Starting Level 4/4` | (raised by recipe purchases) |

The dial is always freely adjustable downward; the purchase only moves the ceiling. Output and
Reserve levels are both covered in [casting-and-spells.md](casting-and-spells.md).

## Equipment — Workshop > Artifacts

Equipment is artifacts, and the page has three tabs: **Loadout**, **Create** and **Upgrade**.

### The loadout has two budgets

An artifact loadout is bound by **two independent limits at once**:

| Budget | Example reading | Meaning |
|---|---|---|
| Weight | `10/12` | Total weight of the equipped artifacts |
| Slots | `4/4` | Number of artifacts equipped |

Either can bind first. A loadout at `10/12` weight with `4/4` slots is full even though two
weight remain. Each artifact shows its own weight, rendered **red when it does not fit** — the
page runs its own fit check before you try.

This is the same shape as the spell loadout, which is bound by spots and by a spell-power
weight budget; see [casting-and-spells.md](casting-and-spells.md).

### Create and Upgrade

**Create** is a compose-and-confirm surface — you supply components and the game resolves what
you get, exactly as Spellcraft, Glyphcraft, Devote and Runecraft do. Marked as **not directly
observed**; the layout was inferred from the four confirmed instances of that pattern. See
[discovery.md](discovery.md).

**Upgrade** is its own page. Artifacts carry **levels**, raised there, and the page is priced
in **two gear currencies** rather than ordinary resources. A total **artifact mastery** figure
exists alongside the per-artifact levels.

## Alchemy, the real feature

Alchemy is a late tab and is **not** Concepts — see [concepts.md](concepts.md#the-naming-reality)
for why the two are so easily confused. It has its own **Learn** and **Loadout** pages.

The Alchemy loadout is the most heavily budgeted surface in the game: **six separate capacity
pools**, each with its own per-effect slot costs. Readings from one observed save were `2/9`,
`36/40`, `0/14`, `0/5`, `8/8` and `8/8`. Learn is presumed to be another compose-and-confirm
discovery surface (**not directly observed**).

What the six pools represent, what an alchemy effect does, and how Alchemy Level interacts
with either are **unknown**. See [open-questions.md](open-questions.md).

## Related pages

- [consumables-and-items.md](consumables-and-items.md) — what crafting produces and the
  capacity rules that decide whether it survives.
- [casting-and-spells.md](casting-and-spells.md) — the spell loadout, and the global Output
  level dial.
- [discovery.md](discovery.md) — the compose-and-confirm pattern Create and Learn use.
- [ui-map.md](ui-map.md) — where these pages sit and the interaction patterns they share.
- [open-questions.md](open-questions.md) — Reserve level, Alchemy semantics, artifact stats.
