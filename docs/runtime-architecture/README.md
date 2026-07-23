# Runtime architecture dossier

> **Lifecycle: Accepted production foundation.** ServiceCycle is the production Auto Harvest runtime and the intended host for migrated automation services.

[Back to documentation](../README.md) · [Active plans](../plans/README.md)

## Purpose

ServiceCycle separates reading the game from deciding what to do. It reads game
state on Unity's main thread, copies only the facts a feature needs, and lets
that feature make its decision in the background. Before performing an action,
the main thread checks the current game state again. A stale decision can
therefore be discarded instead of overriding the game.

Read the maintained contracts in this order:

1. [Service-cycle runtime](service-cycle-runtime.md) defines execution, lifecycle, scheduling, and action ownership.
2. [Observability](observability.md) defines the independent full-trace, decision-journal, and performance-profile products.
3. [Replay](replay.md) defines deterministic recording and replay eligibility.
4. [Goals and invariants](goals-and-invariants.md) records correctness and safety rules.
5. [Architecture](architecture.md) explains component boundaries and data flow.
6. [Engineering decisions](decisions.md) records durable design choices.

## Production boundary

- A feature-neutral Automata host owns one registry and one frame pump.
- Each registered service is polled at most once per accepted frame.
- Main-thread capture copies bounded native facts into Unity-free records.
- A service worker owns its mutable planning state and evaluates synchronously.
- Returned actions are advisory until the main thread revalidates current native facts.
- Lifecycle replacement retires stale workers without blocking the Unity thread.
- The game remains authoritative for availability, cost, quantity, queue room, completion, and mutation results.
- Auto Harvest is the first production service; Auto Buy is the next planned migration.

The previous Auto Harvest executor and superseded shared runtime experiments have been deleted. There is one production path and no runtime selector or fallback implementation.

## Observability boundary

Manual full traces, the rolling decision journal, and opt-in performance profiles are independent diagnostic products. They share neutral transport/storage primitives but have separate formats, controls, retention, and failure boundaries. Diagnostic failure disables or faults that diagnostic path without changing gameplay behavior.

Replay is stricter than manual investigation. Only a complete startup-rooted artifact with the required topology and lifecycle evidence is eligible for deterministic production replay.

## Verification boundary

- `./script/test` is the complete bounded portable development gate.
- Real-reference builds and installed-game contract tests validate reflected game boundaries.
- Interactive runtime validation is required for gameplay, UI, save, and installation claims.

Passing one boundary does not imply another. Current commands and runtime procedures live in the [testing guide](../testing/README.md).

## Next work

The proposed [Auto Buy ServiceCycle port](../plans/autobuy-service-cycle-port.md) is the next separate
consumer validation. It will test the host with a larger service, replace game-side policy queries with raw
fact capture and pure formulas, and provide comparative tracing evidence before performance changes are
chosen. The shared-foundation cleanup does not itself begin that migration.
