# Agromancy

Agromancy is the game's farming and harvesting system, on the World tab.

- The screen shows **plots**. A plot offers a list of **actions** and holds **nodes** that move
  through phases over time — growing, resting, idle. Harvestable nodes fill to a ready count (e.g.,
  `2/2` ready to harvest) and are then collected.
- **Fruit trees** and **treasure trees** are the two node kinds observed early.
- Agromancy both **produces and consumes** resources: an allocation problem with inputs and outputs,
  not just a timer you tap.
- The cadence is **slow**. Many minutes pass between meaningful actions, and it is normal for a save
  to sit with trees planted and nothing ripe.

Growth continues while the screen is closed. The one screen-bound detail: **whether an action can be
taken is only re-evaluated while the Agromancy screen is open**. A plot can therefore look stale —
an action that has actually become available does not show until you open the screen — and once an
action has shown as available it stays available.

Two authored quirks, not corruption: a plot can offer the same action twice, and it can keep
running instances of an action that has been removed from its list.
