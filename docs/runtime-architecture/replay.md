# Deterministic service-cycle replay

> **Lifecycle: Accepted production contract.**
> Schema-v5 `.osce` semantic snapshots contain numeric semantic
> facts and fingerprints rather than the detached feature values required to reconstruct an evaluation.
> They are not replay artifacts. Registration/Recording, Format, detached-oracle Execution, and a narrower
> production replay driver are implemented.

[Back to dossier](README.md) | [Service-cycle runtime](service-cycle-runtime.md) | [Architecture](architecture.md)

## Truth boundary

Graph validation, event sorting, or scripting native success from action outcomes is not deterministic
replay. An honest replay must decode an artifact written by the production capture path, reconstruct every
decision-relevant value, invoke the same feature evaluator, drive the real Common registry and frame pump,
and compare the complete result.

The existing semantic section remains authoritative for causal and native-boundary evidence. A replay
artifact adds explicitly versioned, feature-owned detached records; it never attempts to recover values from
projection fingerprints or arbitrary serialized objects.

## Implemented boundary

`Runtime/ServiceCycle/Replay/Contracts` currently provides:

- explicit versioned codec and semantic-comparer ports with strict per-record byte bounds;
- stable numeric record, completeness, fault, and mismatch identities;
- a cached fail-closed structural validator for detached record types.

Every root and nested non-scalar record explicitly implements the data-free
`IServiceCycleReplayRecord` marker. Detached records must be non-empty feature-owned readonly value graphs
composed only from reviewed scalar values, enums, and similarly marked nested readonly records. Reference
or interface fields, arbitrary framework value types, strings, collections, constructed generics, nullable
values, ambient time/random sources, handles, native/runtime types, non-literal static storage, empty
markers, explicit layouts, declared size, excessive flattened scalar/inline size, and mutable layouts fail
closed. Sequential packing is accepted because it can only preserve or reduce padding beneath the
validator's conservative layout upper bound;
codecs serialize declared fields rather than managed padding. Codec descriptors also have a hard Common
byte ceiling. These checks prove storage shape only; feature parity tests
must still prove semantic completeness.

The validator remains one cached recursive contract checker. Its source separates graph traversal,
type classification, managed-layout accounting, and stable result values, but those parts share the same
validation state and preserve the original rejection order. This organization adds no runtime object,
reflection pass, or alternate acceptance path.

`Registration` and `Recording` implement opt-in adaptation, action-first detached recording, bounded
append-only storage, trace-session/codec-manifest binding, coherent export snapshots, first-failure
accounting, and an offline bounded footer wait. The implementation isolates record/codec OOM failures,
retains uncaptured live workers for alias checks, and prevents delayed duplicate footers.

`Format` implements the strict canonical container, semantic join, corruption checks, and decoder/exporter.
`Execution` implements exact typed hydration/comparison, a detached evaluator oracle, and production-shaped
registry/pump replay. Production replay is intentionally stricter than the detached oracle: it requires a
full contiguous replay-service topology, at least one detached cycle per slot, exact publication and
lifecycle evidence, lifecycle-unique clocks, same-pump capture/request publication, coherent pump-phase
timing, and supported callback ordering. Sparse and zero-cycle artifacts are detached-oracle-only.
Containable production callback and comparison exceptions become stable replay outcomes while
`StackOverflowException`, `OutOfMemoryException`, and `AccessViolationException` remain outside
containment. The exact-tree portable gate covers the complete execution boundary.

Typed execution retains one canonical artifact scan followed by one ordered decode for each selected cycle.
Artifact selection and stable failure placement, per-cycle record decoding, and the immutable typed result
are separate source responsibilities over the same codecs and scratch storage. Execution registration owns
one typed factory without also owning the fixed-capacity numeric catalog; sealing and owner-thread dispatch
remain catalog responsibilities. The semantic join index likewise remains one immutable lookup projection:
construction performs the existing two semantic passes and ancestry build, while lookup operations and
structural key values are organized separately over the same dictionaries and arrays. These boundaries add
no runtime collaborator, rescan, alternate admission path, or lookup change.

## Non-negotiable boundary

`ServiceRunner<TFrame,TConfig,TState,TAction>` and `SuiteFramePump` do not gain replay callbacks, replay
generics, optional branches, or payload storage. Ordinary services pay no per-action replay cost.

Replay is a separate typed registration/composition layer. It adapts a replayable feature definition to the
ordinary service-cycle ports and owns its codecs, comparers, bounded sidecar, completeness state, and export
metadata. Feature code that opts in always produces the same detached record values; enabling capture only
changes whether those values are encoded and retained.

## Component structure

```text
Runtime/ServiceCycle/Replay/
  Contracts/    implemented: detached-record rules, codec/comparer ports, stable outcomes
  Recording/    implemented: bounded sidecar, coherent fences, complete footers
  Format/       implemented: strict container, join, decoder, exporter
  Execution/    implemented: typed oracle and constrained real registry/pump driver

feature service/
  Replay/       reviewed detached records, explicit codecs, hydration and comparison adapters
```

Dependency direction is inward toward neutral replay contracts. Replay composition may adapt Execution and
Registration; ordinary Execution, Registration, Orchestration, Tracing, and feature-native adapters do not
depend on Replay.

## Artifact identity and versioning

Exact replay uses a distinct `.oscr` container so semantic-only `.osce` files can never be mistaken for
reconstructable input. Container schema version 1 carries the exact canonical schema-v5 `.osce` bytes as
one section plus explicitly typed replay payload and completeness sections. A bounded section directory
records stable section kind, schema version, byte length, and checksum; the container header records its own
version and checksum. Unknown required sections or versions, duplicate sections, length overflow, checksum
failure, trailing bytes, or a semantic/replay fence mismatch fail before feature code runs. The outer
`.oscr` version and embedded `.osce` version are independent strict contracts: `.oscr` version 1 currently
accepts semantic schema 5 only and does not decode older semantic schemas. Accepting any other semantic
schema requires an explicit compatibility decision and decoder change; it never follows the current codec
constant implicitly.

### Implemented `.oscr` version 1 wire shape

Version 1 is little-endian and uses a fixed 96-byte header plus fixed 40-byte
directory entries. Exactly six required sections appear once, in this order, with no gaps, overlaps, or
trailing bytes:

1. container manifest version 1;
2. the exact canonical schema-v5 `.osce` semantic snapshot bytes;
3. codec manifest version 1;
4. replay-record index version 1;
5. replay payload version 1;
6. cycle footers version 1.

The header carries the `OSCR` magic, container/header/directory versions and sizes, checked total and
directory lengths, section count and required flags, semantic session and last-event fence, replay
publication/record/footer fences, and zeroed reserved storage. IEEE CRC32 (polynomial `0xEDB88320`) covers
the whole container except its own four-byte field, and every exact section has its own CRC. Unknown kinds,
versions, or flags; nonzero reserves; arithmetic overflow; duplicate or reordered sections; checksum
failure; and length disagreement fail before any feature codec or callback runs.

The codec manifest uses fixed 24-byte entries sorted by numeric trace-service key then record role. Each
replayable service has exactly one cycle-input, state, and action entry containing codec schema, maximum
encoded bytes, and canonical-encoding requirement. The record index uses fixed 88-byte entries containing
global sequence, the full 48-byte cycle key, record kind/schema/index, payload offset/length, record CRC,
and zero reserve. Sequences are exactly `1..N`, and offsets exactly partition the payload.

Each fixed 768-byte footer contains its sequence and full cycle key, disposition, expected actions,
record-sequence bounds and retained count, encoding evidence, decision time, canonical wake policy,
completeness, exact previous receipt, and at most sixteen fixed projection entries plus fingerprint. Global
record commits from concurrently evaluating services may interleave. A footer's first/last sequences are
bounds, never a claim that its records are contiguous; joining groups records by the complete cycle key and
checks the retained count.

### Implemented semantic join

A provisional worker footer becomes authoritative only when the same full cycle key has the required
capture admission/start, queue/start, evaluation, state-publication, and batch-publication semantic chain.
Schema v5 also carries the service capacity and concrete resolved wakes. Action count,
projection fingerprint, wake, previous receipt, detached input/previous-state/actions/next-state, and one
semantic terminal must agree exactly. Native outcomes come only from authoritative semantic action and
terminal evidence, never from a detached feature record.

Evaluation- and projection-aborted footers have separate fail-closed join rules and cannot claim state or
batch publication. Every queued replayable cycle needs exactly one footer. A footer without a cycle, a
cycle without a footer, duplicate or missing records, sequence gaps, partial snapshots, unsupported
ordinary-service cycles, unjoined semantic evidence, or fence disagreement makes the artifact incomplete
and execution-ineligible.

### Recording-session scope

One recording session represents one bounded suite capture/export epoch and may be shared by multiple
explicitly registered replayable services. It binds once to one semantic trace session and freezes the
three codec descriptors for each numeric service key. Lifecycle replacement requires independent codec
objects for simultaneously live physical workers, but their descriptors must remain identical.

A worker state-factory contention deferral makes that entire recording epoch execution-ineligible. The
deferral occurs before the replay worker consumes its pending input, so it has semantic evidence but no
detached records or footer to execute exactly. Recording cannot resume on those existing workers: a later
capture overwrites the pending input and latches the old cycle incomplete. Discard the session and create
fresh replay registrations after initial or replacement state construction has settled. Production replay
does not synthesize, forgive, or retry an evaluation that never ran.

The session publishes its append-only payload, footer high-water, and first failure as one coherent export
snapshot. The first observational failure makes the whole artifact incomplete and stops further codec work
for that epoch; gameplay continues and terminal footers may still record bounded failure evidence. This is
intentional bounded failure isolation, not per-service availability behavior.

The recording session remains one synchronization owner: construction/admission, coherent snapshots and
offline footer waits, codec-manifest binding, record/footer append, and first-failure publication are
separate source responsibilities over the same gates and arrays. Immutable recording values are grouped by
cycle identity/context, record/footer evidence, and export fences/snapshots. The replay definition adapter
likewise remains one service definition, with worker/frame construction, capture/action routing, and
physical-worker/codec binding kept explicit. These separations introduce no extra runtime objects or
forwarding boundary.

The replayable worker keeps its codec/evaluator contract separate from worker-side recording execution.
It remains the same audited worker object: gameplay actions are still appended before detached action
records, live record-production failures remain observational, and projection seals the same pending
action count.

## Recording flow

1. Main-thread capture fills the ordinary reusable `TFrame` and produces a detached cycle-input record with
   the pinned configuration and captured strategy facts.
2. The replayable worker invokes the same evaluator implementation used for gameplay.
3. For every proposed action, feature code appends the gameplay action first, then offers its detached action
   record to the replay sidecar. Encoding failure or exhaustion cannot retract or fault the gameplay append.
4. The worker records detached previous/next state, projection, wake, and complete action order. Codecs see
   only detached values, never frames, mutable state, gameplay actions, adapters, or native objects.
5. The bounded sidecar publishes a cycle high-water and completeness result. Exhausting payload bytes or
   record-index slots produces distinct stable incomplete evidence and stops further encoding while the
   gameplay batch continues unchanged.
6. Export snapshots semantic and replay high-water fences without scanning or copying a gameplay batch on
   Unity. Its worker emits one canonical container and performs all I/O.

Production Auto Harvest uses one finite recording epoch, not a recorder that silently promises an entire
game session. The first action, accepted lifecycle boundary, or configured window limit requests closure.
A 4,096-event semantic ring requests that close at 3,584 events, leaving 512 events for an already-admitted
cycle to reach a post-pump boundary. Semantic and replay recording remain paired while that cycle finishes;
closing admission earlier could retain a semantic cycle whose replay transaction never began. The owner
thread performs one constant-time settled check per later frame; it never waits. Once every physical runner
is between cycles, the pump closes recording admission and detaches semantic emission before another pump
can publish a cycle. The retained cursor is then immutable. The owner thread copies at most 64 frozen
semantic events on each later frame, so the roughly
one-megabyte retained window is never copied in one Unity update. This finite copy step is locally bounded
and independent of the legacy suite coordinator; it may share a Unity frame with legacy work during the
migration period. Stopping replay cannot clear gameplay work. Exporter initialization, backpressure, or
recording-snapshot contention can retry against the frozen evidence without consuming more semantic
capacity. A changed cursor during this staged copy faults the exporter because only a frozen source is valid.
The last 64 event slots remain reserved for one maximum Auto Harvest pump; if the runtime cannot settle
before that boundary, it discards the optional capture instead of writing incomplete evidence. A captured
cycle still waiting to publish is not settled. Lifecycle replacement while any cycle is in flight and
removing a registered service both invalidate the capture rather than trusting missing or unreachable worker
terminal evidence. Capacity limits are validated during construction without materializing maximum-sized
buffers. Recording arrays are allocated on the first worker commit, export slots are allocated during worker
startup, and canonical encoding creates one exact-sized output on that worker after the frozen high-water
snapshot is known. This makes activation-frame work independent of configured byte and record capacities;
the first recorded cycle deliberately includes the one-time recording initialization in its measured replay
overhead. Ordinary capture/export faults stop this optional evidence path without affecting gameplay, while
stack exhaustion, memory exhaustion, and access violations escape the writer, cleanup, and worker boundaries.

Export admission is not durable completion. An optional observer receives the exact artifact ordinal and
byte count only after storage has flushed and committed the segment. A committed artifact is still reported
when later retention cleanup faults; a pre-commit write failure instead reports that ordinal as discarded.
Exporter-wide source, startup, ordinal-exhaustion, storage, retention, and worker failures remain distinct.
Observer callbacks are one-shot and exception-contained, may run on the owner or exporter thread, and must
not touch Unity or game objects. Stop and disposal remain owner-thread nonblocking, so the worker's final callback can follow
their return or a terminal status transition while accepted work drains; observers tolerate these late calls.
Common exposes no path or logger dependency; Automata maps the ordinal to its stable relative artifact name
at the BepInEx edge.

The exporter likewise remains one two-slot synchronization owner. Owner-thread admission, worker storage,
fault publication, and nonblocking lifetime are source partitions of that object rather than wrappers or
additional queues. Immutable decoded document values are grouped by container records, receipts, and
semantic joins; their public shape and construction semantics remain unchanged.

No replay record contains a path, user/host identity, exception text, raw save data, Unity/game object,
handle, delegate, interface, arbitrary collection, or reflection-serialized graph.

### Human-readable trace report

The checked-in offline reader turns a canonical artifact into a compact Markdown report:

```bash
./script/trace capture.oscr
./script/trace capture.oscr report.md
./script/trace --profile auto-harvest capture.oscr report.md
```

It uses the same strict container decoder as replay execution, then reports sample count, total, average,
and maximum duration in separate main-thread and worker/elapsed tables. Pump action and capture phases
already contain the corresponding per-operation intervals, so the report presents those samples in a clearly
contained table rather than as independent peer work. This retains their distinct sample count, average, and
maximum when one pump contains zero or several operations. It labels all scopes as non-additive: phase rows
sit inside the complete pump, per-operation rows regroup capture/action phase time, and worker, end-to-end,
and replay-sidecar intervals can overlap. The timeline keeps causal events in timestamp/sequence order and
hides idle `PumpCompleted` events from display only; their durations still contribute to the timing table.
Artifact output includes only the input filename, never its directory.

Feature interpretation is explicit. The generic artifact records a trace-service ordinal and codec
descriptors, not a stable feature name, so `--profile auto-harvest` is the caller's assertion of feature
identity; descriptor checks are compatibility fences, not authentication. The generic reader never makes
that assertion from a coincidentally matching byte shape. The selected profile requires exactly one service
with the feature's cycle-input, state, and action codec contracts, strictly decodes every retained feature
record through the production codecs, then uses the decoded action to name
`Fruit tree`, `Treasure tree`, or `No action`. A missing, incompatible, or ambiguous match is an error rather
than a silent generic fallback. The report header records that this feature profile was explicitly selected;
running the generic reader against the same artifact never adds a feature label.

The Auto Harvest table projects each artifact cycle through its already validated semantic join. Queue wait
runs from `CycleQueued` to `CycleStarted`; worker processing uses the evaluation terminal duration;
publish-to-action elapsed runs from `BatchPublished` to `ActionAttempted`; action work uses the action
terminal duration; end-to-end runs from `CaptureStarted` to the batch terminal. Publish-to-action is wall
time after the worker response was published, including time until a Unity-frame pump attempts the action.
The current schema gives evaluation completion and response publication the same worker timestamp, so it
does not fabricate a separate response-construction or publication CPU duration.
Cycles whose footer or semantic join is incomplete retain their reason and show dashes for every interval;
the report never derives authoritative-looking timing from rejected causal evidence.

The figures have deliberately narrow meanings. Semantic durations are recorded runtime evidence, not a
Unity Profiler replacement. Worker-processing duration starts after request dequeue and ends immediately
after state projection. It includes state preparation, evaluation, projection, and detached replay record
construction, which the replayable worker performs on both recording-enabled and disabled paths. When
recording is enabled it also contains record codec/retention work and the cycle-footer append. It excludes
response construction and handoff publication. The narrower replay record metric measures codec encoding
and retained-record append only; it is a contained subset of worker-processing time and excludes detached
record construction and the footer append. Its allocation count is recording overhead, not total worker or
Unity allocation. The current Auto Harvest recording epoch closes after its first attempted
action, so one artifact can explain one fruit or treasure action but cannot by itself prove the complete
two-pair live demonstration. Pair correctness remains separate live-validation evidence.

## Replay flow

1. Strictly decode the production container and validate checksums, versions, sizes, identities, causal
   graph, section ordering, completeness, and required payload coverage before running feature code.
2. Hydrate exact detached cycle input, pinned config/strategy facts, previous state, and prior receipt through
   the feature-owned replay adapter.
3. Invoke the production evaluator and compare exact next state, projection, wake policy, and the complete
   ordered action record sequence.
4. Script the recorded external/native outcomes through a feature-owned neutral main-thread adapter.
5. Drive the real `ServiceCycleRegistry` and `SuiteFramePump` using recorded frame identities, monotonic
   timestamps, lifecycle requests, configuration publications, and emergency transitions.
6. Compare terminal receipts and regenerated semantic events. Report the first stable mismatch by service,
   lifecycle/cycle, record kind, action index, and field code.

Any missing, dropped, truncated, oversized, corrupt, foreign-versioned, duplicated, forward-parented, or
incomplete required record fails before replay. There is no best-effort reconstruction.

The detached evaluator oracle can evaluate one valid sparse replay service independently. Production pump
replay instead requires replay participants to cover the complete contiguous registered topology represented
by the schema-v5 `ServiceCapacity` header; it does not project ordinary or missing slots away. Every slot
must have at least one detached cycle because production initial configuration hydration comes from that
slot's first cycle. A sparse or zero-cycle artifact can still receive a typed detached execution failure; it
does not escape through a missing-cycle lookup. Every accepted pump is retained so its start/next fairness
rotation is reproduced, even when that pump did no capture or action.

Every selected main-owned action enters the feature callback directly. Replay therefore requires the
corresponding `ActionAttempted` evidence whenever an active batch reaches the action pass; it has no
pre-dispatch scheduler-denial script or deferred old-action path.

The callback-free production preflight validates artifact completeness, positive capacity, exact codec and
registration coverage, per-slot cycle presence, and the absence of lifecycle-construction evidence before
any feature-owned factory runs. Typed participant preparation follows. Configuration publications must then
be contiguous from generation one; gaps fail closed rather than synthesizing missing values. Before frame
pumping, the coordinator derives one shared initial lifecycle from consistent pre-cycle
`LifecycleActivated` evidence, rejects a `CaptureCompleted -> CycleQueued` publication split across pumps,
and validates independently reconstructible action/capture duration totals plus the overall pump-phase sum.

Callback-issued lifecycle, emergency, configuration, and non-capture-derived external strategy mutations
cannot be moved outside their callback without changing semantics and therefore fail closed after typed
preparation but before pumping. Capture-derived strategy publication remains real evidence and is replayed
through the ordinary strategy-generation observation seam.

The production executor keeps four direct ownership boundaries. The artifact plan scans and indexes semantic
and detached evidence once, with separate access, collection, control, and per-service validation
responsibilities. The coordinator owns one execution session while its control barriers, admissibility and
timing checks, and final evidence mapping remain explicit. The replay clock owns the exact owner/worker read
schedules; pump-segment collection builds that owner schedule from one accepted frame's action, start,
capture, response, and control facts. These are parts of the existing objects rather than additional
orchestration layers, so splitting their source ownership does not add callbacks, synchronization, or
runtime allocation.

### Implemented execution boundary

Execution is deliberately two-phase. First, a typed evaluator oracle hydrates detached input, configuration,
frame, and previous state, calls the exact evaluator/projector port used by the production replayable
worker, and compares actions, next state, projection, and wake. Second, a production-shaped definition
drives a fresh real registry and frame pump with a virtual monotonic clock and native results scripted from
the authoritative semantic evidence. The production state factory, not expected-state hydration, creates
state for this second phase.

Typed participant preparation, live registration/publication, and bounded cleanup remain parts of one
participant object. Oracle hydration/evaluation, comparison, and cleanup likewise remain one verification
object and preserve first-divergence order. Their source partitions add no callback, delegate, or
intermediate execution layer.

Feature codecs are resolved only after the non-generic container and semantic join are completely valid.
Each decoded record is checked against the frozen descriptor, decoded under a stable failure location,
re-encoded, and byte-compared to reject noncanonical encodings. The replay driver derives lifecycle,
configuration, strategy, emergency, and frame controls from semantic order, uses artifact-bounded steps,
and never polls, sleeps, or waits without a fixed bound.

Detached preparation and cleanup are one typed containment boundary: ordinary factory failures and
cleanup-only failures become stable replay outcomes, while an earlier mismatch or fault remains primary.
The production source alone opts its real-pump callbacks into the stricter fatal boundary. Fatal
start/capture/action failures escape synchronously; fatal state-factory, evaluation, projection, and
state/frame-release failures are captured at the worker root, preserve the first fatal through cleanup,
and are published before cleanup can block. Strict record-production and codec-encoding seams relay the
same fatal triple while live recording keeps allocation and access failures observational. Lifecycle
replacement construction applies that strict predicate as well. Bounded offline cleanup retains the whole
service slot, attempts every physical lifecycle position and prepared participant within the finite boundary,
then joins actual worker-thread termination. It retains and rethrows the original first-fatal identity only
after those cleanup attempts. This policy is not applied to live gameplay recording, and no
Unity-facing pump path joins or waits for a worker.

Offline production replay has two signal-driven readiness boundaries under the same finite worker timeout.
Before each pump, every current runner must have reached its first parked handoff state. A newly constructed
worker therefore cannot still own the handoff gate when the deterministic driver issues its zero-wait pump.
When a recorded `CycleStarted` says the next pump will acquire a response, the second boundary requires its
exact full cycle identity—service, lifecycle, configuration, strategy, capture, and cycle—and requires the
publishing worker to release the gate after publication. This prevents both the initial start probe and the
immediate response-acquisition probe from depending on host scheduling. Gameplay pumping remains
nonblocking; these waits exist only in the offline driver. Replay never serializes workers by
nondeterministic global recording-footer order. Each lifecycle receives an independent clock script,
preventing one generation's reads from consuming another's time evidence.

The production replay clock also supplies a bounded gate around reference-state construction only. That
gate is entered on the real worker before the registry-wide factory-token admission and released as soon
as state construction and identity publication finish. It prevents host scheduling from inventing a
transient first-state contention deferral that is absent from the artifact, while leaving evaluators
concurrent and preserving strict clock-completeness and semantic comparison. Ordinary runtime clocks do
not implement the gate, so gameplay retains its nonblocking state-factory contention retry behavior.

Containable exceptions from participant construction, codecs, evaluator/projector, native scripts, pump
callbacks, comparison, and cleanup are converted to stable execution failures. Once a primary preparation,
registration, pump, or comparison failure has been captured, a later cleanup failure cannot relabel it.
`StackOverflowException`, `OutOfMemoryException`, and `AccessViolationException` remain outside this
containment boundary.

## Performance and failure isolation

- Sidecar storage is separately bounded; gameplay action count is not. Tiny capacities are deterministic
  failure injection in tests, not an intended operating quota. Production capture should size a bounded epoch
  with ample headroom; `ByteBudgetExhausted` and `RecordCapacityExhausted` distinguish which selected capture
  bound was undersized.
- Large batches encode incrementally on the worker. Unity never scans or copies the returned batch.
- Codec and storage exceptions latch replay incomplete with stable codes and stop further sidecar work for
  that capture; gameplay state, projection, wake, actions, and receipts remain unchanged.
- Disabled, already-exhausted, and warmed successful paths have explicit allocation evidence.
- Evaluator time and replay-record encoding time/allocation are measured separately by the sidecar rather
  than by modifying the ordinary runner.
- Every accepted pump retains its fairness rotation evidence; this intentionally replaces the earlier
  quiet-idle trace claim.

## Adoption

Auto Harvest supplies feature-owned detached records, codecs, hydration/comparison adapters, and end-to-end
replay evidence through the production Common runner, registry, pump, recorder, exporter, decoder, and
evaluator. Auto Buy and other services add their own records and codecs when they migrate; Common does not
infer feature state through reflection or graph serialization.

## Portable acceptance gate

- canonical encode/decode and byte-stable re-encode of the production artifact;
- strict structural/privacy rejection and precise mismatch codes;
- no-action, large-batch, first/middle rejection, configuration/strategy publication, emergency, lifecycle
  orphan, typed evaluation-abort rejection, action fault, and capture-fault recovery scenarios;
- mutation of every recorded input/output category produces the expected first mismatch;
- enabled/disabled gameplay parity;
- throwing and deliberately costly codec isolation;
- 100,000 gameplay actions under a tiny replay budget remain bounded, promptly mark incomplete, preserve
  gameplay output, and never perform an O(n) Unity-thread scan;
- warmed successful, disabled, and exhausted allocation evidence;
- full artifact decode followed by the real evaluator and pump, not a test-only event script.

Installed-game and interactive evidence remain separate gates after portable acceptance.
