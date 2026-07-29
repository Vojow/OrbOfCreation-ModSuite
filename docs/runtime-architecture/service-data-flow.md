# How a service gets its data

The canonical statement of what a ServiceCycle service is allowed to read, and where that data comes
from. If anything else in this repository disagrees with this document, this document is right and the
other thing is stale.

## The rule

**A service receives exactly three objects: configuration, world state, and strategy.** All three are
immutable, published, generation-stamped snapshots. A service does not read the game to make a
decision. It reads those three.

The one exception is the **action boundary**, which is not a data source but a safety net: before
mutating the game, an action re-validates against live native state and refuses if the world moved.
That stays, and it is not "collection". Deciding uses snapshots; acting re-checks reality.

## Why this exists

Every service used to read the game itself, on the Unity thread, once per cycle — the *capture* or
*collect* phase. That was a hack from before there was anything to share. It meant N services each
paying to read overlapping state, each with their own native contracts to keep working, each free to
invent their own idea of what a number means, and all of it on the frame budget.

One shared collection replaces all of it. The collect phase is **gone** for ordinary services —
not deprecated, not optional: the ordinary contract has no capture member, so a service that wants
one has to be the source service instead, and there is only ever one of those.

## The two kinds of service

### Source service — publishes one of the three

Reads the game (or the config file) on the main thread and hands back one immutable object that
everyone else consumes. There are three, and there will only ever be three:

| Publication | Owner | Lives in |
| --- | --- | --- |
| World state | the collection service | Common — it is the *game's* world, not any feature's |
| Configuration | the runtime, one installation for the whole suite | Common |
| Strategy | the strategist (not built) | Common — the `SuiteStrategy` type is neutral; the policy that will fill it is Automata's |

A source service keeps a capture, because reading is its job. It is the only shape that has one:
`IServiceCycleSourceDefinition` declares `Capture`, and the ordinary contract it shares a main-thread
half with does not.

### Ordinary service — consumes all three

Everything else. No capture, no native reads at decision time, no per-service collection, and no
per-service idea of what the world is. Its cycle input is the pinned snapshots and nothing else — the
worker's `Evaluate` takes the configuration and the world as arguments, so there is no publisher to
read twice and no place to keep a capture even if a service wanted one.

## World service flow

1. **Main thread** grabs all the raw data and stamps the frame with the pump frame it was collected
   on. Not the frame it publishes on — derivation takes frames, and a publish-time stamp would claim
   the snapshot was newer than an action it is missing.
2. Hands off to the **worker thread**.
3. Allocates a fresh world state. Nothing published is ever written again, so immutability is
   structural rather than a convention a reader has to trust.
4. **Does the expensive math** — every derived number any service needs, computed once, here, off the
   main thread. This is the point of the whole design: the game recomputes derived values from scratch
   on every call, so we own that math instead of asking 400 times a cycle.
5. Publishes **one action** carrying the finished immutable snapshot and its generation.
6. The pump executes that action, which makes the snapshot the newest one. Publication-class actions
   dispatch before mutating ones, so a snapshot handed back this frame is live before any consumer
   decides anything this frame.

## Ordinary service flow

1. The pump asks whether this service may start: its own wake policy, **and** whether the world has
   advanced past its last game mutation. A service that changed the game does not decide again until
   the world has been re-read since — otherwise it re-decides against a world its own action already
   invalidated and does the same thing twice.
2. If it may start, it is handed immutable references to world, configuration and strategy.
3. The worker reads them freely. They are immutable, and anything newer is a *different object*, so
   there is no shared mutable state and nothing to synchronise.
4. The worker decides and emits actions.
5. The pump executes the actions on the main thread, each re-validating against the live game. Then
   back to 1.

## Generations, and what is retained

A generation is a `ulong` stamped on a publication so a consumer can ask "is this newer than what I
last acted on". It is **an identity tag, never a key into storage**.

`ServiceWorldPublisher` holds exactly one field: the latest publication. There is no history, no map,
no ring buffer, and no pool. A worker pins a reference for the duration of its cycle; once it drops
that reference and a newer publication exists, the old one is garbage. Live at any moment: the latest,
plus one per in-flight cycle.

A pool was considered and rejected — the reuse would have to be proven safe against every worker that
might still hold a reference, which is the invariant nobody can check locally, in exchange for
allocations that happen a few times a second off the main thread.

## What a service may not do

- Read the game to make a decision. That is what the world snapshot is for.
- Keep its own collection phase because "the snapshot does not have X". X goes in the snapshot.
- Hold a publisher at all. The runtime pins each publication once when the cycle opens and hands the
  snapshot over; a service that could read again mid-cycle would evaluate against one world and act
  against another.
- Carry state between cycles. The worker is stateless by contract; pacing comes from the wake policy.

## Status

Built and load-bearing:

- Publication, generation stamping, immutability, latest-wins, and the freshness gate — enforced by
  the runtime rather than restated by each consumer ([W50](world-collection-decisions.md)).
- Collection as a registered service, so it sits inside budget accounting, tracing, health and
  emergency stop rather than beside them, with publishing as a first-class action effect so worker
  output reaches consumers through the ordinary pipeline in the same frame.
- **Step 4's rate math.** Every published resource row carries a `TrueRate` computed by
  `GameResourceRateMath` on the worker — the first derived number the suite owns outright instead of
  asking the game for.
- **Step 4's cost math.** `PurchaseCosts` publishes what one more level of each structure *and each
  upgrade* costs, from two separate chains sharing only the table they land in. The structure chain is
  compared against the game's own answer by the differential verification run, entity by entity in a
  live save; the upgrade chain has no differential entry of its own.
- **Both consumers off the game while deciding.** Auto Buy's candidates are the snapshot's structures
  and upgrades, priced, levelled and classified from published rows; Auto Harvest takes six of its
  eight facts from the snapshot, including the action's audited structural safety, computed on its
  worker from the plot-authoring, phase-descriptor and effect-block tables. Neither has a capture: what
  each one did is a frame projector the worker calls immediately before deciding
  ([W51](world-collection-decisions.md)). Auto Harvest's quarantine and contract circuit stay at the
  action boundary and reach the worker as result codes it records in its own state.

Not built, and honest about it:

- **Strategy delivery.** No service publishes a bulletin, so the third of the three publications is
  stamped by the runtime but never carries anything.

The live action queue is collected but never decided against. Both services compete for the same slots
and consume them with their own actions, so a published reading would be wrong inside a single world
generation; the queue is read at the action boundary, immediately before the mutation that depends on
it ([W53](world-collection-decisions.md)).
