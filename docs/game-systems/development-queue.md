# The development queue

Buying an attribute or an upgrade does not give it to you. It **queues** it, and it develops over
time.

- **Attributes develop slowly; upgrades develop more slowly still**, and they share the **same**
  queue, so a slow upgrade occupies a slot an attribute could have used.
- Default capacity is **8** slots. Purchases raise it — e.g., *Improved Development* takes it from 8
  to 10 and adds +10 % development speed, and *Greater Mental Acuity* adds +2. Late-game capacity has
  been observed as high as **304**.
- Capacity and development *speed* are separate, separately upgradeable knobs.
- Early on the queue, not the resource, is what binds: even when a resource is abundant, the order in
  which you spend it matters.

## Occupancy counts stack units

Queue occupancy counts stack units, not distinct entries, because a multi-level purchase occupies
several units. E.g., one observed frame held 35 distinct queued objects and 131 queued stack units.

## Bulk Development

**Bulk Development** is a live game value (e.g., observed at 2 on one save and 16 on another).
Attributes request that many levels per purchase; **upgrades are always one level per action**.

It is raised mostly by research, and research moves two queue knobs that are easy to conflate: some
nodes add **parallel development slots** (more different purchases developing at once), while others
raise **bulk** (queued levels of the *same* attribute processed together in one slot's pass).
