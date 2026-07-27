# Runtime architecture dossier

> **Lifecycle: Accepted production foundation.** ServiceCycle is the production Auto Harvest runtime and the intended host for migrated automation services.

[Back to documentation](../README.md) · [Active plans](../plans/README.md)

## Purpose

ServiceCycle separates reading the game from deciding what to do. The game is read **once**, by one
service, off to the side; every other service decides against three immutable published snapshots —
configuration, world state, and strategy — and never reads the game to make a decision. Before
performing an action, the main thread re-checks live game state, so a stale decision is discarded
rather than overriding the game.

Read the maintained contracts in this order:

1. [How a service gets its data](service-data-flow.md) is the spine: the three publications, the two
   kinds of service, and what a service may not do. Start here.
2. [Service-cycle runtime](service-cycle-runtime.md) defines execution, lifecycle, scheduling, and action ownership.
3. [Observability](observability.md) defines the independent full-trace, decision-journal, and performance-profile products.
4. [Goals and invariants](goals-and-invariants.md) records correctness and safety rules.
5. [Architecture](architecture.md) explains component boundaries and data flow.
6. [Engineering decisions](decisions.md) records durable design choices.
7. [Shared world collection](world-collection.md) defines the one reader every service consumes.

## Production boundary

- A feature-neutral Automata host owns one registry and one frame pump.
- Each registered service is polled at most once per accepted frame.
- One collection service reads the game; ordinary services consume its published snapshot.
- Main-thread capture exists only for the source service; the ordinary contract has no capture member.
- A service worker owns its mutable planning state and evaluates synchronously.
- Returned actions are advisory until the main thread revalidates current native facts.
- Lifecycle replacement retires stale workers without blocking the Unity thread.
- The game remains authoritative for availability, cost, quantity, queue room, completion, and mutation results.
- Auto Harvest and Auto Buy are both production ServiceCycle services; world collection is the third, and the only one that reads for everyone rather than for itself.

There is one production path, no runtime selector, and no fallback implementation.

## Observability boundary

Manual full traces, the rolling decision journal, and opt-in performance profiles are independent diagnostic products. They share neutral transport/storage primitives but have separate formats, controls, retention, and failure boundaries. Diagnostic failure disables or faults that diagnostic path without changing gameplay behavior.

Recorded runs are re-read as evidence, never re-executed: there is no replay system, and hand-crafted
scenario fixtures carry that testing value.

## Verification boundary

- `./script/test` is the complete bounded portable development gate.
- Real-reference builds and installed-game contract tests validate reflected game boundaries.
- Interactive runtime validation is required for gameplay, UI, save, and installation claims.
- The differential verification pass runs in the game under Ctrl+Alt+Y and compares every owned
  number against the game's own answer for every entity in the save — record readings, frame
  globals, rates, prices and the exclusion buckets. In-session agreement is what
  [goals and invariants](goals-and-invariants.md) requires before an owned formula may be trusted,
  and offline tests cannot establish it.

Passing one boundary does not imply another. Current commands and runtime procedures live in the [testing guide](../testing/README.md).

## What this foundation does not have yet

- **A strategist.** No service publishes a `SuiteStrategy` bulletin; the publisher, the stances, and
  the neutral default exist, and every consumer already reads a neutral one.
- **The remaining migrations.** Auto Cast, Auto Concept, Spell Leveling and Mentor still run on the
  older per-feature path.

The [project roadmap](../plans/roadmap.md) sequences these; open decisions live in
[active plans](../plans/README.md).
