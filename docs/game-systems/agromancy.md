# Agromancy

Agromancy is the game's farming and harvesting system, on the World tab.

- The screen shows **plots**. A plot offers a list of **actions** and holds **nodes** that move
  through phases over time — growing, resting, idle. Harvestable nodes fill to a ready count (e.g.,
  `2/2` ready to harvest) and are then collected.
- **Fruit trees** and **treasure trees** are the two node kinds observed early.
- The cadence is **slow**. Many minutes pass between meaningful actions, and it is normal for a save
  to sit with trees planted and nothing ripe.
- Agromancy both **produces and consumes** resources: an allocation problem with inputs and outputs,
  not just a timer you tap.

## The screen has to be open

**The game only refreshes harvest state while the Agromancy screen is open.** An action that has
actually become available does not appear until you open the screen; once it has appeared it stays
available. A save that looks stalled may just be waiting to be looked at.

**Code shape:** plot and action state is cached behind the interface, and the plot list's own render
pass is what re-evaluates it — the availability check latches on and is not re-tested afterwards.

## Two plot quirks

Both are authored behaviour, not corruption:

- A plot can offer **the same action twice** in its action list.
- A plot can **hold instances of an action that has been removed from its list** — the removal does
  not stop instances that are already running.
