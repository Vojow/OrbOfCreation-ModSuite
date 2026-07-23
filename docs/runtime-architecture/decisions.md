# Runtime architecture engineering decisions

> **Lifecycle: Accepted production foundation / live slice passed / observation products implemented.**
> These decisions are normative for new architecture work. Common ServiceCycle is production-composed for
> Auto Harvest, the superseded runtime is deleted, aggregate findings are closed, and the separately owned
> observation products passed their portable gates. Release remains separate.

[Back to dossier](README.md) | [Service-cycle runtime](service-cycle-runtime.md) | [Goals and invariants](goals-and-invariants.md) | [Architecture](architecture.md)

## D1 - Feature services are explicit modules

Each automation capability is a cohesive service module. It separates configuration, capture, state, evaluation, action execution, diagnostics projection, and native adapters. Tests mirror those responsibilities.

New work does not accumulate as flat project-root files, partial-class state piles, or one central feature engine. Folder and dependency structure are part of correctness.

## D2 - Composition is explicit and deterministic

Services register through one deliberate Common composition boundary in stable order. Assembly scanning, filesystem conventions, reflection autoloading, static-constructor discovery, and general service location are not used.

Registration is transactional, rejects duplicate identities/capacity overflow, and unwinds all acquired resources after failure. Feature APIs remain typed; only complete service slots are erased for the Common pump.

## D3 - Native game access is a hexagonal main-thread boundary

Unity objects, reflection members, native registries, mutation permits, and postcondition checks remain behind feature-owned main-thread adapters. Frames, configuration, strategy, state, actions, diagnostics, and traces use suite-owned neutral values.

Stable UUID plus expected native type establishes identity. Diagnostic names never do. Native metadata and lifecycle-bound object bindings are cached separately. Actions carry typed intent; the adapter re-resolves and revalidates immediately before mutation.

When final policy and mutation verification require the same native state, the feature captures one immutable
pre-mutation snapshot and shares it across both checks. A second native read is reserved for the
post-mutation snapshot; repeating an unchanged preflight traversal is not an additional safety boundary.

Temporary native global state remains adapter-owned and is restored with `try/finally` on every path.

## D4 - Common owns a small service-cycle runtime

Common owns the top-level frame pump, typed runners, ownership handoffs, lifecycle/emergency control, action rotation, clocks, generic diagnostics transport, and semantic trace transport.

It does not own Auto Harvest pairs, game UUIDs, economy policy, reflection failure scopes, or feature diagnostics meaning.

The pump, runner, registration, reusable action store, lifecycle replacement, diagnostics, and tracing are separate cohesive modules. A facade that accumulates every constructor and behavior is rejected even if tested.

## D5 - Ordinary services use strict half-duplex cycles

The default service follows `Waiting -> Capturing -> Evaluating -> Executing -> Waiting` with one reusable frame, lifecycle-scoped state, reusable response/action buffer, and one sleeping thread.

Capture, evaluation, and action execution never overlap for that service. Evaluation is synchronous and Unity-free. The ordinary contract has no custom async task, shared worker lane, polling awaiter, checkpoint, incumbent, or cooperative continuation.

Future specialized work does not complicate this default API without a measured feature requirement.

## D6 - Configuration and strategy are next-cycle snapshots

Only a successful Save publishes immutable runtime configuration. Draft, invalid, reverted, or failed changes remain invisible.

Automata has one canonical composed configuration record, grouped by feature and cross-cutting concern. BepInEx entries are persistence and editing bindings at the composition edge: changing one rebuilds the complete immutable record atomically. Runtime services, ownership, health projection, and controls consume that record through narrow source/editor ports and never retain or inspect `ConfigEntry` objects.

Each cycle pins its `TConfig`; capture copies the relevant facts from a separately typed immutable strategy publication into `TFrame` and records the exact generation used. Those policy inputs remain fixed through evaluation and complete batch drain. Later publications never cancel or partially alter current work. The next cycle consumes the latest snapshots and may skip intermediate versions.

Pinned and latest versions remain visible in diagnostics.

## D7 - Batches are finite but not suite-capped

A service may return any finite number of actions. Common maintains one batch per service, grows/reuses storage, and imposes no gameplay count ceiling or truncation.

Each frame attempts at most one action per active service from a rotating start. The first rejection or fault terminates the current batch, preserves earlier commits, and discards the untouched suffix. No deferred old-action retry exists.

Native validation remains authoritative for every attempt.

## D8 - Emergency stop is immediate Common control

Emergency stop is not saved configuration. While active, Common performs no native action call, rejects every unattempted action in current and late-arriving batches as `Rejected(EmergencyStop)`, and pauses new capture.

A running pure evaluator may finish. Its returned actions are rejected without execution. Clearing stop starts fresh work; it never resurrects a batch.

## D9 - Service state is private and diagnostics are projections

`TState` may be mutable across cycles but belongs only to the service worker. UI, traces, Common, and other services never inspect the live state object.

After successful evaluation, the worker produces an immutable semantic projection. Common publishes that projection atomically with exact cycle/context identity. Rich feature details use bounded main-owned copies.

Persistence beyond a lifecycle requires a separate versioned save-safe design.

## D10 - Lifecycle replacement orphans instead of preempting

Lifecycle replacement terminates old native work by generation, creates factory-fresh state/ownership for the newest safe generation, and lets an already-running evaluator finish in isolation. Its late response is discarded. A prior lifecycle's projection never seeds the replacement state.

The runtime does not use `Thread.Abort`, checkpoints, mid-evaluation cancellation, or shared state transfer. Two physical runner positions are the hard per-service bound: normally current plus optional retiring, but both may retire during a storm while the service pauses. A stopping runner counts as live until its worker acknowledges stopped; only then may its position be reused for the newest coalesced generation.

## D11 - Faults recover without crash loops

Expected refusal is a normal decision or rejection. Exceptions are caught at capture, evaluator, and action boundaries and isolated to the service.

A failed evaluation publishes no actions/state, retains the last successful projection, keeps its worker alive, safely recreates working state, and retries through monotonic debounced backoff. Stable categories and counters are public; raw private exception data is not exported.

## D12 - One semantic model with separately owned observation products

Common uses one causal service-cycle vocabulary for live diagnostics, bounded in-memory evidence, disk capture, and replay. It records exact context/cycle/state/batch/action/emergency/lifecycle/fault identities and outcomes.

Manual full trace, compact decision journal, and performance profile are separate products rather than one
universal file or sink. Each owns its block lanes, writer, format, controls, retention, status, and failure
boundary. Every lane remains single-producer; any multi-thread merge occurs on the product's background
writer. Products may reuse a format-neutral buffered-segment transport and atomic storage port, but never
share mutable buffers or backpressure. Full-trace facts may feed several offline views; the journal coalesces
decisions for long retention; profiling adds measurements only in builds compiled with its probes.

The semantic vocabulary, bounded capture, schema-v5 codec, graph validation, snapshot exporter, strict
detached-record contracts, replay registration/recording, `.oscr` Format, detached evaluator oracle, and
production-shaped replay driver are implemented. Schema v5 preserves `ServiceCapacity` and explicit
start/admission/resolved-wake evidence. Every accepted pump is retained for exact fairness rotation.

Production replay deliberately requires a full contiguous topology matching schema-v5 `ServiceCapacity`
header metadata, at least one detached cycle per slot for initial configuration hydration, gap-free
configuration generations beginning at one, consistent pre-cycle `LifecycleActivated` evidence,
lifecycle-unique clocks, same-pump capture/request publication, and independently coherent pump-phase
timing. Exact response waits use the complete cycle identity and never impose global footer order. Sparse
and zero-cycle artifacts remain detached-oracle-only; missing-cycle execution yields typed failure evidence.

Lifecycle-construction evidence fails during callback-free preflight. Callback-issued lifecycle, emergency,
configuration, and non-capture-derived external strategy mutations fail after typed preparation but before
pumping, while capture-derived strategy publication remains supported evidence. Containable callback,
comparison, and cleanup failures become stable outcomes without relabelling an earlier primary phase;
`StackOverflowException`, `OutOfMemoryException`, and `AccessViolationException` remain outside
containment.

The ordinary four-generic runner contains no replay callback and pays no per-action replay overhead. Exact replay is a separate opt-in service registration whose feature adapter produces detached, recursively value-only readonly records or bounded fragments for cycle inputs, previous/next state, and actions. Replay enabled and disabled follow the same feature record-production path; only encoding/retention changes, and gameplay action append precedes action-record encoding. Codecs never receive live frames, mutable state, gameplay action storage, or native adapters. This keeps codec/storage faults observational while leaving the ordinary execution contract unchanged.

Unity never waits for telemetry, replay payloads, or I/O. Opt-in exact replay encoding may add bounded, separately measured worker response latency. Drops are exact and make replay captures incomplete rather than altering gameplay. Replay uses explicit feature codecs and the real evaluator/pump; graph sorting alone is not called replay. Common structurally rejects reference-bearing replay records and isolates codec/storage faults. Feature parity tests, not a dishonest compiler claim, prove that record production is noninterfering and contains every decision-relevant value.

Semantic snapshot export uses two preallocated handoff slots and accepts sources with at most 8,192
resident events. The owner thread uses a zero-wait atomic admission handshake, copies only a coherent
resident suffix, and never waits for the worker; a dedicated worker encodes and stores it. Retention is
commit-before-delete during a successfully reconciled exporter lifecycle: the configured maximum is the
steady-state retained target, while a later deletion failure may leave exactly one additional newly committed
artifact before admission faults closed. Startup reconciliation failures also fault admission closed, but may
leave an inherited namespace above the configured target because cleanup is deliberately not reported as
complete. The previous good artifact is never deleted before its replacement is durable.

## D13 - Runtime UI is additive and purpose-built

Runtime diagnostics use the dedicated Runtime page inside the Mods surface. It is not a fake plugin or configuration file. Ordinary settings retain staged editing, Save/Revert behavior, navigation, and scroll position. The page renders ServiceCycle implementation and capability health without retaining or fabricating a kernel-cycle identity.

The feature bridge projects bounded capability health, including emergency, ownership, progression, native readiness, and contract failures. Rich ServiceCycle phase, context, batch, wake, and fault evidence remains available through its diagnostics and trace surfaces rather than being converted into the old runtime snapshot. The UI never reads worker state or Unity objects.

Registry callbacks enqueue bounded typed transitions or mark a dirty latch. Projection and rendering occur on the coordinated main-thread UI pass.

## D14 - Structure and evidence are delivery requirements

Every checkpoint keeps dependency direction, narrow constructors, explicit ownership, focused tests, and buildable code. Portable evidence is reported honestly and never promoted to real-reference, interactive, package, or release approval.

Review is risk-based and bounded. A meaningful runtime milestone receives at most one independent review by
default. Concrete findings are assessed and fixed or explicitly rejected once; ordinary tests and runtime
evidence then decide acceptance. Re-review is reserved for a newly discovered gameplay-safety or correctness
risk, not used as an open-ended search for more defensive behavior.

Developer-only observability has one containment invariant: it must not change gameplay behavior. Inside that
boundary it uses one direct path and may fail or disable itself visibly. It does not need compatibility paths,
automatic restart, speculative retries, or recovery layers merely to make a first live run succeed.

## D15 - Adopted replacements delete obsolete paths

The new runtime may exist source-adjacent while it is uncomposed and tested. A service is cut over atomically. No selector, compatibility branch, dual execution, or fallback remains afterward.

The Auto Harvest cutover atomically removed its old executor from composition. The now-unreachable Process,
Lanes, Host, shared scheduler/kernel, live-view broker, duplicate trace/replay vocabulary, orchestration,
and legacy-only tests were deleted. Git history is the rollback boundary.

Do not advance a configuration schema merely to scrub a retired selector. Stop binding/displaying the obsolete value and leave old serialized text inert unless supported data needs a real migration.
