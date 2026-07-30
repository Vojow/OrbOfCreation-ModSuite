# Goals and invariants

> **Lifecycle: Accepted production foundation.** These rules define the production-composed ServiceCycle runtime and its independent observation products.

[Back to dossier](README.md) | [Service-cycle runtime](service-cycle-runtime.md) | [Architecture](architecture.md)

This is a register: one line per rule, pointing at the document that specifies it. Four things are
stated nowhere else and this file is their specification — the product goals, the four conditions on
owned economy math, the strategy rules, and the non-goals.

## Product goals

The runtime should make automation:

- responsive enough to keep up with fast native queues;
- capable of simple local decisions and future expensive strategy work;
- safe across save/load, reset, NG+, scenes, unlocks, native refusal, and faults;
- modular enough that feature code expresses product policy rather than orchestration mechanics;
- observable live and analyzable offline;
- efficient in Unity time, background CPU, copying, allocation, and logging;
- testable without the game through typed deterministic inputs and exact outcomes;
- extensible to cross-domain strategy without one god planner or one god runtime class.

The long-term product should support Auto Buy, harvesting, Agrimancy, spells, crafting, loadouts, and other game domains, plus an optional high-level strategist. Enabling every supported service should eventually be capable of playing the complete game safely under user policy.

## Game-model facts

### Finite definitions and changing values

- For one supported game build the definitions form a finite knowable universe, while their
  quantities, rates, levels, costs, availability, queue and unlock state change continually.
  [Definition catalogs and changing values](architecture.md) specifies how each half is held.
- Generated manifests are version-pinned evidence and fixtures, never permission to bypass runtime
  contract validation.
- Expect every reading to become stale; useful bounded staleness is normal.
- Native objects may register late, unlock later, be destroyed, or be recreated. A finite catalog does
  not make Unity references process-lifetime singletons.

### The game remains authoritative

- The game owns availability, cost, quantity, rates, queue room, completion, and final mutation validity.
- Planning may use captured native results but must not silently reproduce unknown economy formulas.
- Planning may evaluate an *owned* economy formula, on any thread, when all four of the following hold. Fewer than four is the silent reproduction the previous rule forbids.
  - It was transcribed from the decompiled game assembly rather than inferred from observed behaviour, and the transcription records which assembly it came from.
  - That assembly is covered by a hash baseline in `data/native-contracts.json`, and the suite refuses to load at all on a build matching no baseline. There is no fallback path, deliberately: a mismatch invalidates the reflection contracts exactly as much as the ported arithmetic, so falling back to "ask the game" would read members by name from an equally unverified assembly.
  - It is differentially tested against the game's own result for real entities in a live session, and disagreement is a failure rather than a tolerance. Offline tests cannot establish this on their own, because they assert against values derived by hand from the same decompiled source — a misreading would be reproduced identically in the port and in its expected value, and pass.
  - Native revalidation at the action boundary stays authoritative regardless. Owning the arithmetic changes what the suite may compute off-thread, never what it may trust when mutating.
- Every action is advisory until the main thread re-resolves stable identity and performs current validation.
- Stale suite values may remain useful. Stale native references may not.
- Unknown or contradictory mutation evidence fails closed.
- Temporary native global-state changes remain adapter-owned and use `try/finally` restoration on every path, including rejection, fault, and emergency transition.

## Main-thread invariants

- Unity objects, reflected game objects, lifecycle transitions, registries, and native APIs stay on the Unity main thread unless an audited contract proves otherwise.
- Native capture and mutation occur only through feature-owned main-thread adapters, and background evaluators receive suite-owned snapshots with no native references.
- Common owns one top-level frame pump independent of any feature plugin. Each service is considered at most once per pump call, and a duplicate pump for the same Unity frame executes no further action.
- Each active service receives one bounded action turn per Unity frame: one action by default, more only where its registration selects a fixed larger limit.
- All native actions remain sequential on the Unity thread and revalidate after earlier services may have mutated the game.
- There is no action-pass time budget. Total capture/action time and service counts are measured so that policy can be revisited without changing service APIs.

## Service-cycle invariants

### Strict half-duplex ownership

- Every current generation follows `Waiting -> Capturing -> Evaluating -> Executing -> Waiting`, where `Capturing` is the phase a service's own main-thread callbacks run in. See [the core state machine](service-cycle-runtime.md).
- Exactly one side owns each mutable store at a time: capture never overlaps that service's evaluation, evaluation never overlaps its batch execution, and the next cycle never begins before the batch is terminal and the wake policy is eligible.
- One reusable response/action buffer belongs to one current runner; only the source additionally owns a world buffer.
- Worker handoffs have explicit synchronization and sequence validation. There is no hot polling, and no lock is held while native or feature code runs.

### Two shapes and no third

- A service is either ordinary — consuming the published world — or the source that produces it. The two contracts are siblings sharing a main-thread half, not one extending the other. See [the two service shapes](architecture.md).
- Ordinary services have no capture stage. Nothing an ordinary service could read on the main thread is absent from the shared world, and a main-thread read costs frame time to learn it twice.
- A service declares exactly two type parameters, state and action. Configuration and the source's buffer are named types rather than parameters, because there is one suite and one game.
- A worker definition is a distinct object from the main-thread definition and may not be, implement, or retain it, so a worker cannot reach a native capture or action adapter through its service dependency.

### World freshness

- A service does not start a cycle against a world collected before it went live or before its own last attempted game-facing action. The gate is unconditional, strictly-after, and armed by activation and every attempt rather than by a feature declaration or terminal disposition. [Acting twice on one world](world-collection.md) specifies it.
- The gate is a start refusal, not a wake policy: a commit is missing from the pinned world, while a skip, rejection, or fault is evidence that live reality diverged from the facts which produced the action. A held service is skipped and asked again next frame, no feature callback is entered, and every held frame is recorded, because holding a service is otherwise indistinguishable from that service having nothing to do.
- A source that cannot answer holds the service closed, because "unknown" is not "fresh".

### Dedicated service execution

- Each registered service normally has one named current background thread; disabled services keep it sleeping, and lifecycle retirement may occupy only one additional physical runner position.
- Evaluation is synchronous CPU work, not a custom async process, and ordinary services use no shared lanes, awaiters, checkpoints, incumbents, or cooperative continuations.
- A non-returning evaluator violates its contract but cannot touch Unity or block another service's worker.
- Explicit finite registration bounds service count; there is no runtime discovery or unbounded dynamic plugin set.

### Explicit state

- Service state is mutable, may persist across cycles, and is read or written only by its own worker. Common, UI, traces, and other services receive immutable semantic projections instead. See [service state](service-cycle-runtime.md).
- State cannot retain a pinned publication, the world buffer, a response writer, a Unity/native object, an adapter, a live configuration entry, or another service's state.
- State is lifecycle-scoped unless a separate versioned persistence contract is reviewed.

## Data ownership invariants

### One collection, feature-shaped projection

- The game is read once, by the source, into a shared immutable world publication. No ordinary service reads the game independently; each derives the exact projection one decision needs on its own worker thread from the snapshot the cycle pinned. See [shared world collection](world-collection.md).
- Only the raw grab of native values belongs on the main thread. Classification, ranking, and every derived quantity are computed off-thread from those raw readings, because main-thread time is the scarce resource and derivation does not need the Unity thread. One exception is declared and bounded: a modifier record is read as the game's own `GetValue()` would answer it — its memo while it is clean, its fold over base value and both modifier sets while it is dirty. That is arithmetic on the Unity thread, taken deliberately, because the alternative is a snapshot carrying a number the game will not act on. See [D16](decisions.md) and [W5](world-collection-decisions.md).
- Projecting the world and deciding from the projection are one step: same thread, back to back, same pinned snapshot, with nothing between them able to observe the projection.
- Capture occurs only when the source is waiting and its wake policy is eligible. Disabled, emergency-stopped, evaluating, or batch-draining services start no cycle, so nothing refreshes underneath them.
- The complete bounded game-neutral reading is filled in one main-thread capture. Multi-frame capture is a future feature-specific response to measured native cost, not a default broker.
- Static catalogs and dynamic values remain separate.
- What the world publication collects is enumerated from each category's runtime type, not from its save record; the two differ by the whole cached layer, and [D17](decisions.md) records why that mistake is easy to make and expensive to keep.

### Read-only publication

- The main thread is the sole writer of the source's world buffer. The worker receives a read-only surface, never observes a partial fill, and returns the buffer before the next capture; it crosses threads once per cycle and only in one direction.
- The world, configuration, and strategy publications, actions, and diagnostic projections contain reviewed neutral DTOs only, and no mutable backing array is exposed through a downcastable collection surface.
- `PublicationTable<T>` is the one audited bounded container permitted inside an immutable configuration, world, strategy, or action value: its array is private, copied from the caller's span at construction so no external alias survives, and never handed back. Admitting the container does not admit its contents — `T` is still walked under the full rules of the role the table appears in.
- Structural tests audit representative object graphs because C# 10 cannot express deep immutability or negative generic constraints.

## Publication invariants

- There are exactly three publications — world, configuration, and strategy — all constructed and owned by the registry, each latest-wins, immutable, and generation-stamped. One publisher per kind makes a generation suite-wide. See [three publications](architecture.md).
- A cycle pins one reading of each when it starts, names all three generations on its identity, and keeps them fixed through evaluation and the whole batch drain. No service holds a publisher, so the two halves of a cycle cannot disagree about what the game looked like.
- New world, configuration, or strategy never cancels, orphans, or partially changes current work. The next cycle consumes the latest snapshots and may skip intermediate versions.
- The world publication becomes live on the main thread during the action pass, and publishing services dispatch before mutating ones, so a snapshot acquired in a frame is visible to every consumer in that same frame and no consumer sees it change mid-decision.
- A change to a persisted setting publishes a complete immutable suite configuration snapshot and a new generation, whatever changed it. UI drafts are not runtime configuration, and failed validation, failed persistence, revert, and abandoned edits change no setting and therefore publish nothing.
- Feature-grouped immutable records are the canonical runtime configuration; persistence-framework entries stay behind the composition adapter and services never retain a `ConfigEntry` or a feature-specific mirror.
- The pump reads the emergency stop off the configuration publication every frame. Nothing pushes it in, so the state the pump is in cannot drift from what the suite is configured to do; a snapshot that says nothing about it leaves an explicitly engaged stop alone.
- Diagnostics expose pinned and latest versions so delayed application is explicit.
- Configured-intent status projection has its own application-boundary generation because it must order
  controls before deferred ServiceCycle activation. Every feature bridge carries it; older status writes
  are rejected, and registry transitions expose the replacement snapshot synchronously.
- User configuration and hard safety policy outrank strategic advice.

## Action invariants

### Finite uncapped batches

- A service may return zero to any finite number of actions. Common has no gameplay item-count cap, never truncates a batch, and keeps at most one batch resident per service. See [action batches](service-cycle-runtime.md).
- Batch storage may grow and retain its high-water capacity; count and bytes remain measurable.
- Every feature supplies a reviewable finite-batch termination argument; checked growth failure faults before publication and never leaks a partial batch.
- Actions carry stable identity and typed intent only.

### Rotation and terminal behavior

- The pump scans from a rotating service index, each active service attempts no more than its registered action limit, and one service's result does not suppress later services in the pass.
- `Committed` and `Skipped` advance that batch's cursor; only `Committed` increments its committed count.
- The first `Rejected` or `Faulted` action terminates the batch. Earlier commits remain committed, and the rejected action and untouched suffix are never executed or retried; a later cycle may replan, but an old batch is never resumed.
- Every attempted action receives current native identity, availability, resources, queue, ownership, lifecycle, mutation, and postcondition validation as applicable; affordability, reserves, and strategy come from the cycle-pinned policy snapshots.

### Emergency stop

- Emergency stop is an immediate Common control, not saved configuration. No native call occurs while it is active, no new cycle starts, and every unattempted action in current and late-arriving batches is terminally `Rejected(EmergencyStop)`. See [emergency stop](service-cycle-runtime.md).
- A running evaluator may finish. Its late valid response still publishes its state projection and wake policy and retains worker state — emergency stop never pretends the pure evaluation did not occur — while its actions are rejected without execution.
- Clearing stop never resurrects rejected work.

## Wake and liveness invariants

- A response independently specifies its actions and wake policy; supported anchors are immediate, after decision publication, after batch terminal, an absolute monotonic time, and the registration default. See [wake policy](service-cycle-runtime.md).
- A deadline may expire while actions drain, but the next cycle still waits for batch terminal. A zero-action response is immediately terminal.
- Continuous game updates never restart evaluation, because no unsolicited refresh occurs, and ordinary rejection does not retry an old action — the wake policy controls fresh replanning.
- Timers use monotonic time rather than frame counts or ambient wall clock.

## Lifecycle invariants

- Save/load, reset, NG+, scene replacement, shutdown, and other audited boundaries advance the lifecycle generation. Old pending captures and action suffixes receive no further native call, and late old-generation responses cannot publish current state or actions. See [lifecycle retirement](service-cycle-runtime.md).
- A fresh runner owns factory-created lifecycle-scoped state, plus a fresh world buffer where the shape has one; prior-lifecycle projections never seed it. Native caches are lifecycle-scoped and re-resolved after replacement.
- An old evaluator may finish as an isolated orphan; its result is discarded by generation and its thread exits. The runtime never uses `Thread.Abort`, unsafe preemption, or shared mutable state transfer.
- Each service has exactly two physical runner positions — normally current plus optional retiring, both retiring during a storm, in which case the service pauses and coalesces to the newest generation. No third runner exists.
- A stopping runner remains live and retains its gate and stores until its worker acknowledges stopped; Unity never waits or reuses that position early. Retirement emits one terminal fact immediately, and eventual worker exit is separate cleanup evidence.

## Fault invariants

- Expected policy refusal is not an exception. Capture, evaluation, and action-adapter exceptions are isolated to the service, and the worker thread survives ordinary evaluator exceptions. See [fault recovery](service-cycle-runtime.md).
- A failed evaluation publishes no actions or new state projection, and potentially partial state is recovered through an explicit safe state factory or the last successful neutral publication.
- Fault retries use monotonic bounded backoff, coalesce demand, and debounce identical evidence; successful work resets its fault episode.
- Fault diagnostics use stable categories and counters, not private exception messages, paths, or stack traces. A fault loop cannot spam logs or block other services.

## Strategy invariants

- The strategist publishes immutable versioned goals and constraints; it does not call native mutations directly. Domain services own their native contracts, local planning, and action construction.
- Strategy may express resource goals, targets, reserves, spend limits, embargoes, pauses, priorities, and time horizons. Every constraint has scope, provenance, precedence, and replacement/expiry semantics.
- A missing or failed strategist does not prevent safe local fallback automation. The first published bulletin is neutral and reproduces unstrategised behaviour exactly, so a strategist that never runs, faults, or is disabled changes nothing.
- The cycle-pinned strategy snapshot is advisory beneath cycle-pinned user policy and current native validation.
- Strategy may only tighten what user configuration already permits, never loosen it. Configuration is evaluated first and independently; the stance is consulted only on spends the operator would have allowed, and can then only refuse. A wrong, stale, or hostile bulletin therefore costs throughput and nothing else.
- A constraint that cannot be evaluated against the captured facts is reported as inapplicable rather than silently skipped or invented, so an authoring error stays visible in diagnostics.
- If future strategy search needs a specialized execution contract, it does not complicate ordinary service runners.

## Modularity invariants

- Common owns generic orchestration, clocks, registration, handoffs, lifecycle, emergency stop, action rotation, diagnostics transport, and telemetry. Features own catalogs/queries, the world capture adapter, state, evaluators, action adapters, diagnostics projections, and codecs — the configuration snapshot is not among them, because the registry owns the one publication.
- Reflection and game contracts remain behind feature-owned hexagonal adapters, and composition is explicit and deterministic: no reflection autoloading, service locator, or static-constructor discovery.
- Policy, capture, evaluation, state projection, action execution, and presentation remain independently testable.
- New code uses cohesive purpose-named folders and small classes. A central class accumulating every constructor and behavior is a design failure.
- Only one runtime is composed for a service. After cutover, incompatible legacy paths and tests are deleted.

## Observability invariants

- Every context generation actually consumed, plus every cycle, capture, state publication, batch, action, lifecycle, emergency transition, and fault episode, has stable session-local identity and carries causal parents and pinned generations. Unconsumed intermediate publications may coalesce. See [observability](observability.md).
- Live diagnostics and disk traces project from one semantic event model. Schema v7 includes the registered service capacity and explicit cycle-start, capture-admission, and resolved-wake evidence.
- Every accepted pump emits its semantic summary, including otherwise idle pumps, so the fairness rotation chosen for every frame stays legible. Rich formatting and disk I/O remain off the pump path.
- Unity never waits for diagnostics, telemetry, or I/O, and observability never changes gameplay outputs or native decisions.
- In-memory traces and disk handoffs are bounded and expose exact overwrite/drop evidence. Trace loss marks a capture incomplete; it never changes gameplay actions.
- Trace payloads are written incrementally on the worker into separate bounded storage; Unity never scans or copies a complete gameplay batch for tracing.
- Lifecycle-construction evidence fails before feature callbacks. Callback-issued lifecycle, emergency, configuration, and non-capture-derived external strategy mutations fail closed after typed preparation but before pumping. Containable adapter, cleanup, and observer exceptions become stable typed failures preserving the earlier primary phase; `StackOverflowException`, `OutOfMemoryException`, and `AccessViolationException` remain outside containment.
- Production readiness waits use the complete service/lifecycle/configuration/strategy/capture/cycle identity and never serialize on global footer order.
- No reflection serialization, raw save object, native object, arbitrary path, user/host name, or exception text enters exported traces.
- Deterministic tests use virtual clocks and real evaluator/pump contracts.
- Warm idle/handoff/in-capacity append/drain/receipt/fixed-event paths allocate zero managed bytes, and whole-slot erasure never boxes feature values.

## Non-goals

- Perfectly fresh mirrors of all game state.
- A world snapshot a consumer may treat as current. One shared snapshot of the whole game, republished on an interval and stamped with a generation, is built and registered; what stays a non-goal is the freshness guarantee, not the snapshot. Every published reading is bounded stale by construction, and anything that must be current against the game is revalidated natively at the action boundary. See [`world-collection.md`](world-collection.md).
- One physical shared worker scheduler for ordinary services.
- General async/actor/process authoring machinery.
- Reimplementing the game's economy wholesale, or porting any part of it outside the four conditions above.
- Eliminating final native validation.
- One global planner containing every domain algorithm.
- Automatic retry of rejected actions.
- Immediate application of ordinary configuration/strategy changes to current work.
- Retaining obsolete runtime paths as rollback mechanisms.
- Installed-game or release claims from portable evidence.

## Measurement questions

- Does the world collection need to span multiple frames?
- What response-buffer growth and retained capacity occur in real workloads?
- How much Unity time do the world capture and one-action-per-service passes consume?
- Does measured throughput later justify multiple action rounds or a time gate?
- Which state projections need richer bounded feature detail?
- What fault backoff durations provide useful recovery without noise?
- Does any real planner need mid-step native questions, overlap, cooperative search, or persisted state?

Resolve these with deterministic workloads, allocation evidence, and separately approved runtime profiling. They do not reopen the accepted ordinary service-cycle model by default.
