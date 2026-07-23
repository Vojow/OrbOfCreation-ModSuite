# Goals and invariants

> **Lifecycle: Accepted production foundation.** These rules define the production-composed ServiceCycle runtime and its independent observation products.

[Back to dossier](README.md) | [Service-cycle runtime](service-cycle-runtime.md) | [Architecture](architecture.md)

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

For one supported game build, resources, upgrades, structures, attributes, spells, agriculture actions, and other definitions form a finite knowable universe. Their quantities, rates, levels, costs, availability, queue state, and unlock state change continually.

Consequences:

- Build lifecycle-scoped definition catalogs after native registries are ready.
- Use dense suite-local handles internally while retaining stable UUID plus expected native type at boundaries.
- Store verified static relationships once instead of rediscovering them per frame.
- Treat generated manifests as version-pinned evidence and fixtures, not permission to bypass runtime contract validation.
- Pull one service-shaped frame when that service is ready to decide.
- Expect every frame to become stale; useful bounded staleness is normal.

Native objects may register late, unlock later, be destroyed, or be recreated. A finite catalog does not make Unity references process-lifetime singletons.

### The game remains authoritative

- The game owns availability, cost, quantity, rates, queue room, completion, and final mutation validity.
- Planning may use captured native results but must not silently reproduce unknown economy formulas.
- Every action is advisory until the main thread re-resolves stable identity and performs current validation.
- Stale suite values may remain useful. Stale native references may not.
- Unknown or contradictory mutation evidence fails closed.
- Temporary native global-state changes remain adapter-owned and use `try/finally` restoration on every path, including rejection, fault, and emergency transition.

## Main-thread invariants

- Unity objects, reflected game objects, lifecycle transitions, registries, and native APIs stay on the Unity main thread unless an audited contract proves otherwise.
- Native capture and mutation occur only through feature-owned main-thread adapters.
- Background evaluators receive suite-owned snapshots with no native references.
- Common owns one top-level frame pump independent of any feature plugin.
- Each service is considered at most once per pump call.
- Each active service attempts at most one action per Unity frame initially.
- A duplicate pump for the same Unity frame cannot execute another action.
- All native actions remain sequential on the Unity thread and revalidate after earlier services may have mutated the game.

There is initially no additional action-pass time budget. Total capture/action time and service counts are measured so this policy can be revisited without changing service APIs.

## Service-cycle invariants

### Strict half-duplex ownership

Every current service generation follows:

`Waiting -> Capturing -> Evaluating -> Executing -> Waiting`

- One reusable frame and one reusable response/action buffer belong to one current runner.
- Exactly one side owns each mutable store at a time.
- Capture never overlaps that service's evaluation.
- Evaluation never overlaps that service's batch execution.
- The next capture never begins before the current batch is terminal and the wake policy is eligible.
- Worker handoffs have explicit synchronization and sequence validation; there is no hot polling.
- No lock is held while native or feature code runs.

### Dedicated service execution

- Each registered service normally has one named current background thread; disabled services keep it sleeping, and lifecycle retirement may occupy only one additional physical runner position.
- Evaluation is synchronous CPU work, not a custom async process.
- Ordinary services do not use shared latency/deep lanes, awaiters, checkpoints, incumbents, or cooperative continuations.
- A non-returning evaluator violates its contract but cannot touch Unity or block another service's worker.
- Explicit finite registration bounds service count; there is no runtime discovery or unbounded dynamic plugin set.

### Explicit state

- Service state is mutable and may persist across cycles.
- Only its worker reads or writes it.
- Common, UI, traces, and other services receive immutable semantic projections instead of the live state object.
- State cannot retain a frame, response writer, Unity/native object, adapter, live configuration entry, or another service's state.
- State is lifecycle-scoped unless a separate versioned persistence contract is reviewed.

## Data ownership invariants

### Feature-shaped pull

- A service defines the exact frame it needs.
- Capture occurs only when the service is waiting and its wake policy is eligible.
- Disabled, emergency-stopped, evaluating, or batch-draining services do not refresh frames.
- The preferred path fills the complete bounded game-neutral frame in one main-thread capture.
- Multi-frame capture is a future feature-specific response to measured native cost, not a default broker.
- Static catalogs and dynamic values remain separate.

### Read-only publication

- The main thread is the sole frame writer.
- The worker receives a read-only surface and never observes a partially filled frame.
- The main thread does not reuse the frame until the worker returns it.
- Frames, configuration, strategy, actions, and diagnostic projections contain reviewed neutral DTOs only.
- Mutable backing arrays are not exposed through downcastable collection surfaces.
- Structural tests audit representative object graphs because C# 10 cannot express deep immutability or negative generic constraints.

## Configuration and strategy invariants

- UI drafts are not runtime configuration.
- Feature-grouped immutable records are the canonical runtime configuration; persistence-framework entries remain behind the composition adapter.
- A configuration change replaces the complete current record atomically. Runtime services never retain a `ConfigEntry` or maintain feature-specific mirror snapshots.
- Only a successful Save publishes a complete immutable service configuration snapshot and new version.
- Failed validation, failed persistence, revert, and abandoned edits publish nothing.
- A service pins configuration when its cycle begins; its capture adapter copies the latest relevant typed strategy facts into the frame and reports the exact strategy generation used.
- That evaluation and its entire batch retain the pinned configuration plus captured strategy facts; no runner stores a hidden `object` strategy payload.
- New configuration or strategy never cancels, orphans, or partially changes current work.
- The next cycle consumes the latest published snapshots; intermediate versions may coalesce.
- Diagnostics expose pinned and latest versions so delayed application is explicit.
- User configuration and hard safety policy outrank strategic advice.

## Action invariants

### Finite uncapped batches

- A service may return zero to any finite number of actions.
- Common has no gameplay item-count cap and never truncates a batch.
- At most one batch is resident per service, preventing batch accumulation.
- Batch storage may grow and retain its high-water capacity; count and bytes remain measurable.
- Every feature supplies a reviewable finite-batch termination argument; checked growth failure faults before publication and never leaks a partial batch.
- Actions carry stable identity and typed intent only.

### Rotation and terminal behavior

- The pump scans from a rotating service index.
- Each active service attempts at most one action in that frame.
- One service's result does not suppress later services in the pass.
- `Committed` advances that batch's cursor.
- The first `Rejected` or `Faulted` action terminates the batch.
- Earlier commits remain committed; the rejected action and untouched suffix are never executed or retried.
- A later cycle may replan; an old batch is never resumed.
- Every attempted action receives current native identity, availability, resources, queue, ownership, lifecycle, mutation, and postcondition validation as applicable; affordability, reserves, and strategy come from the cycle-pinned policy snapshots.

### Emergency stop

- Emergency stop is an immediate Common control, not saved configuration.
- No native call occurs while it is active.
- Every unattempted action in current and late-arriving batches is terminally `Rejected(EmergencyStop)`.
- A running evaluator may finish, but its actions are rejected without execution.
- A late valid response still publishes its state projection and wake policy and retains worker state; emergency stop never pretends the pure evaluation did not occur.
- New captures remain paused until stop clears.
- Clearing stop never resurrects rejected work.

## Wake and liveness invariants

- A response independently specifies its actions and wake policy.
- Supported initial anchors are immediate, after decision publication, after batch terminal, an absolute monotonic time, and registration default.
- A deadline may expire while actions drain, but the next cycle still waits for batch terminal.
- A zero-action response is immediately terminal.
- Continuous game updates never restart evaluation because no unsolicited refresh occurs.
- Ordinary rejection does not retry an old action; the service's wake policy controls fresh replanning.
- Timers use monotonic time rather than frame counts or ambient wall clock.

## Lifecycle invariants

- Save/load, reset, NG+, scene replacement, shutdown, and other audited boundaries advance lifecycle generation.
- Old pending captures and action suffixes receive no further native call.
- Late old-generation responses cannot publish current state or actions.
- A fresh runner owns a fresh frame and factory-created lifecycle-scoped state; prior-lifecycle projections never seed it.
- An old evaluator may finish as an isolated orphan; its result is discarded by generation and its background thread exits.
- The runtime never uses `Thread.Abort`, unsafe preemption, or shared mutable state transfer.
- Each service has two physical runner positions. Normally they are current plus optional retiring; both may be retiring during a storm, in which case the service pauses and coalesces to the newest generation. No third runner exists.
- A stopping runner remains live and retains its gate/stores until its worker acknowledges stopped; Unity never waits or reuses that position early.
- Retirement emits one terminal fact immediately; eventual worker exit is separate cleanup evidence, and worker gates/stores remain alive until that exit or process termination.
- Native caches are lifecycle-scoped and re-resolved after replacement.

## Fault invariants

- Expected policy refusal is not an exception.
- Capture, evaluation, and action-adapter exceptions are isolated to the service.
- A failed evaluation publishes no actions or new current state projection.
- The worker thread survives ordinary evaluator exceptions.
- Potentially partial state is recovered through an explicit safe state factory or last successful neutral publication.
- Fault retries use monotonic bounded backoff, coalesce demand, and debounce identical evidence.
- Successful work resets its fault episode.
- Fault diagnostics use stable categories and counters, not private exception messages, paths, or stack traces.
- A fault loop cannot spam logs or block other services.

## Strategy invariants

- The strategist publishes immutable versioned goals and constraints; it does not call native mutations directly.
- Domain services own their native contracts, local planning, and action construction.
- Strategy may express resource goals, targets, reserves, spend limits, embargoes, pauses, priorities, and time horizons.
- Every constraint has scope, provenance, precedence, and replacement/expiry semantics.
- A missing or failed strategist does not prevent safe local fallback automation.
- The cycle-pinned strategy snapshot is advisory beneath cycle-pinned user policy and current native validation.
- If future strategy search needs a specialized execution contract, it does not complicate ordinary service runners.

## Modularity invariants

- Common owns generic orchestration, clocks, registration, handoffs, lifecycle, emergency stop, action rotation, diagnostics transport, and telemetry.
- Features own catalogs/queries, capture adapters, configuration snapshots, state, evaluators, action adapters, diagnostics projections, and codecs.
- Reflection and game contracts remain behind feature-owned hexagonal adapters.
- Composition is explicit and deterministic; no reflection autoloading, service locator, or static-constructor discovery.
- Policy, capture, evaluation, state projection, action execution, and presentation remain independently testable.
- New code uses cohesive purpose-named folders and small classes. A central class accumulating every constructor and behavior is a design failure.
- Only one runtime is composed for a service. After cutover, incompatible legacy paths and tests are deleted.

## Observability and replay invariants

The semantic event, bounded ring, schema-v5 codec, graph validation, snapshot exporter, worker-side sidecar,
`.oscr` artifact, detached evaluator oracle, and production-shaped pump driver are implemented. Schema v5
includes the registered service capacity and explicit cycle-start, capture-admission, and resolved-wake
evidence.

- Every context generation actually consumed by capture or a queued cycle, plus every cycle, capture, state publication, batch, action, lifecycle, emergency transition, and fault episode, has stable session-local identity. Unconsumed intermediate configuration or strategy publications may coalesce before the frame pump observes them.
- Events carry causal parent identifiers and pinned generations.
- Live diagnostics and disk traces project from one semantic event model.
- Every accepted pump emits its semantic summary, including otherwise idle pumps, so exact replay retains the fairness rotation chosen for every frame. Rich formatting and disk I/O remain off the pump path. Dedicated lateness/drop summaries and profiling samples remain planned extensions.
- Unity never waits for diagnostics, telemetry, replay payloads, or I/O; observability never changes gameplay outputs or native decisions. The ordinary runner has no replay callback or per-action replay cost. Opt-in exact replay encoding may add bounded, separately measured worker response latency.
- In-memory traces and disk handoffs are bounded and expose exact overwrite/drop evidence.
- Trace loss marks a capture incomplete; it never changes gameplay actions.
- Exact replay is separate opt-in composition using detached, recursively value-only readonly cycle-input, previous/next-state, and action records or bounded fragments. Structural validation rejects references, collections, `object`, interfaces, delegates, handles, and native/runtime types. Codecs never receive live frames, mutable state, gameplay actions, or adapters.
- Replay enabled and disabled use the same feature record-production path; only encoding/retention changes. Gameplay action append succeeds before detached action-record encoding. Common isolates codec/storage faults; feature parity tests establish record-production noninterference and semantic completeness.
- Replay action records are encoded incrementally on the worker into separate bounded storage; Unity never scans or copies the complete gameplay batch for tracing.
- Replay uses explicit versioned feature codecs for detached exact inputs plus exact Common context and external outcomes; feature parity tests prove semantic completeness.
- Production pump replay accepts only a full contiguous registered replay topology matching `ServiceCapacity`, with at least one detached cycle per slot, gap-free configuration publications beginning at generation one, consistent initial `LifecycleActivated` evidence, exact cycle-addressed readiness, lifecycle-unique clocks, same-pump capture/request publication, and coherent independently reconstructible pump-phase evidence. Sparse and zero-cycle artifacts may run only through the detached evaluator oracle; missing-cycle execution returns typed failure evidence.
- Lifecycle-construction evidence fails before feature callbacks. Callback-issued lifecycle, emergency, configuration, and non-capture-derived external strategy mutations fail closed after typed preparation but before pumping. Capture-derived strategy publication remains supported replay evidence.
- Production readiness waits use the complete service/lifecycle/configuration/strategy/capture/cycle identity and never serialize on global footer order.
- Containable replay callback, adapter, comparison, cleanup, and production-driver exceptions become stable replay failures while preserving an earlier primary failure phase. `StackOverflowException`, `OutOfMemoryException`, and `AccessViolationException` remain outside containment.
- No reflection serialization, raw save object, native object, arbitrary path, user/host name, or exception text enters exported traces.
- Deterministic tests use virtual clocks and real evaluator/pump contracts.
- Warm idle/handoff/in-capacity append/drain/receipt/fixed-event paths allocate zero managed bytes and whole-slot erasure never boxes feature values.

## Non-goals

- Perfectly fresh mirrors of all game state.
- A continuously refreshing world snapshot.
- One physical shared worker scheduler for ordinary services.
- General async/actor/process authoring machinery.
- Reimplementing the game's economy.
- Eliminating final native validation.
- One global planner containing every domain algorithm.
- Automatic retry of rejected actions.
- Immediate application of ordinary configuration/strategy changes to current work.
- Retaining obsolete runtime paths as rollback mechanisms.
- Installed-game or release claims from portable evidence.

## Measurement questions

- Does any feature-shaped capture need to span multiple frames?
- What response-buffer growth and retained capacity occur in real workloads?
- How much Unity time do all service captures and one-action-per-service passes consume?
- Does measured throughput later justify multiple action rounds or a time gate?
- Which state projections need richer bounded feature detail?
- What fault backoff durations provide useful recovery without noise?
- Does any real planner need mid-step native questions, overlap, cooperative search, or persisted state?

Resolve these with deterministic workloads, allocation evidence, and separately approved runtime profiling. They do not reopen the accepted ordinary service-cycle model by default.
