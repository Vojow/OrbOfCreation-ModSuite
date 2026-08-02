# The World tab

[Back to game systems](README.md)

World is the game's "outdoors" tab. It holds four subtabs — **Aspects**, **Dimensional**,
**Agromancy** and **Druidry** — and, like every other top tab, it also shows the persistent
right-hand **Upgrades / Inventory** strip. That strip is not part of World; it is a global
panel visible everywhere, and it is easy to mistake for a second row of World subtabs.

Two of the four subtabs are documented below in real detail. Dimensional is not: almost
nothing about it has been established beyond the fact that it exists and that Dimensional
attributes feed the **Shaper** progression tab. See
[open-questions.md](open-questions.md).

## Agromancy — the harvest domain

Agromancy is the game's farming and harvesting system.

- The screen shows **plots**. A plot offers a list of **actions**, and holds **nodes** that
  move through phases over time — growing, resting, idle. Harvestable nodes fill up and are
  then harvested.
- **Fruit trees** and **treasure trees** are the two node kinds observed early; both fill to a
  ready count (for example `2/2` ready to harvest) and are then collected.
- The cadence is **slow**. Many minutes pass between meaningful actions, and it is normal for a
  save to sit with trees planted and nothing ripe.
- Agromancy both **produces and consumes** resources. It is an allocation problem with inputs
  and outputs, not just a timer you tap.

### The screen has to be open

This is the single most surprising thing about Agromancy: **the game only refreshes harvest
state while the Agromancy screen is open.** Plot and action state is cached behind the UI, and
the plot list's own render pass is what re-evaluates it.

The visible symptom is a plot that looks stale — an action that has actually become available
does not appear until you open the screen. Once it has appeared, it stays available: the
check latches on and is not re-tested afterwards. So "I don't see it yet" on this screen is
usually not a bug, and opening the page is what fixes it.

### Two plot quirks

Both of these are authored behavior, not corruption:

- A plot can offer **the same action twice** in its action list.
- A plot can **hold instances of an action it no longer offers** — the action was removed from
  the plot's list, but existing instances of it keep running.

A third, marked **derived from the game's data model and not confirmed in play**: an action's
remaining-instance count is computed against the *idle* nodes, with some costs absorbed first
by nodes that are already growing or resting. That figure is allowed to go negative and the
game leaves it negative rather than clamping it.

## Aspects

**World > Aspects** is a set of **three pedestal slots** holding placed aspects, and it is how
the game's major late systems arrive: the known aspects — **Aspect: Workshop**, **Aspect:
Alchemy Lab** and **Aspect: Rituals**, each granted by its upgrade — unlock their system
through these world slots.

New aspects arrive through **discovery events** — there is no standing "aspect offers" page,
so the offer appears when the game decides to present it rather than somewhere you can go and
check.

The zero-free-slot case has **two conflicting accounts**, and which is right is unresolved.
Read out of the game's code: an aspect-granting upgrade can be paid for and completed while no
pedestal is free, and the aspect is then simply never placed — no refusal, no refund. Recalled
from play: buying with zero free slots made the interface **invent an extra slot** to hold the
purchase, and a later slot-granting upgrade then left one slot permanently empty. These may be
two different paths through the same system; treat both with care until one is confirmed. See
[open-questions.md](open-questions.md).

## Druidry

Druidry is a discipline in its own right, with its own attributes on its own subtab; its
levels feed the **Druid** progression tab. "Life Weaver" is a Druidry entity.

One structural note that affects what you can see: Druidry content is reachable **only** through
the World > Druidry subtab. Most content in this game is reachable through two routes — a
parent tab and a subtab both list it — but World does not co-reference Druidry the way the
Magic tab co-references Wizardry. If the Druidry subtab is not yet available, its content is
not visible anywhere else.

## Dimensional

Dimensional is the fourth World subtab. Its attributes belong to the Dimensional category,
which feeds the **Shaper** progression tab. Everything else about it — what it produces, what
it gates, how it is played — is unrecorded. See [open-questions.md](open-questions.md).

## Related pages

- [progression-advancements.md](progression-advancements.md) — how Druid and Shaper tab levels
  turn into advancement currencies.
- [attributes-upgrades-development.md](attributes-upgrades-development.md) — the attribute and
  upgrade model these disciplines use.
- [resources.md](resources.md) — the resources Agromancy feeds and consumes.
- [ui-map.md](ui-map.md) — the full tab map, including the global Upgrades / Inventory strip.
- [open-questions.md](open-questions.md) — Dimensional, aspect semantics, plot internals.
