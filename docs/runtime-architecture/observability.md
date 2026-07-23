# Service-cycle observability

> **Lifecycle: Accepted production observability / portable-verified.** The production runtime exposes bounded
> live diagnostics, a causal semantic ring, finite deterministic replay capture, offline `.oscr` reports,
> manual full traces, a rolling decision journal, and compile-time performance profiles. These products
> separate manual investigation, long-running decision history, and performance measurement without changing
> gameplay scheduling or replay honesty.

[Back to dossier](README.md) | [Service-cycle runtime](service-cycle-runtime.md) | [Replay](replay.md)

## Why there are three modes

One event stream cannot efficiently answer all three observability questions:

1. **What exactly happened while I reproduced this?** needs a user-controlled, lossless full trace.
2. **Why has this service behaved this way over time?** needs a compact decision lineage with repeated
   outcomes coalesced and empty frames omitted.
3. **Where is this subsystem spending time or allocating?** needs opt-in stage samples and operation counts,
   not another causal log.

These are three products over one small mechanical foundation. They share identity value types, atomic file
storage, and reusable block-transport code. Each product owns a separate pool, queue, sleeping writer,
control state, format, retention policy, status, and failure boundary. One product cannot consume another's
buffers or backpressure its producer. Profiling samples do not enter replay comparison, and a compact journal
never claims to be a complete trace.

## Measured sizing evidence

The first installed-game Auto Harvest artifact contains 3,584 semantic events and 52 completed cycles over
54.014 seconds. Its 1,032,588 bytes are about 1.09 MiB/minute, 65.6 MiB/hour, or 1.54 GiB/day at that observed
rate. It reached the finite window because schema-v5 stores one fixed 272-byte record for every accepted pump,
not because Auto Harvest copies a large game state. About 79% of its records were pump summaries and 95% of
those pumps were otherwise idle.

The exact detached Auto Harvest cycle input is 31 bytes. The observed warmed capture average of about
0.144 ms covers substantially more than copying those bytes: lifecycle-coherent binding checks, the shared
active-action list traversal, fruit and treasure native fact reads, frame construction/copies, detached input
construction, and publication into the worker-side recording bridge. Reflected native calls, list traversal,
boxing, and invocation argument arrays are the likely costs; stage evidence is required before optimizing.

The current one-minute limit is therefore an in-memory replay-window policy. It is not an appropriate disk
retention policy for a user-controlled trace.

## Shared foundation

Common owns neutral contracts for:

- mode-specific commands and immutable status snapshots;
- stable suite, service, lifecycle, cycle, batch, and action identities;
- a reusable bounded producer-block handoff and sleeping background-writer mechanism;
- session manifests, ordered segments, checksums, atomic publication, and explicit gaps;
- machine-neutral relative artifact names and privacy validation.

Feature code supplies bounded typed projections or detached records. Orb Mod Config only invokes each mode's
neutral control port and renders status. It never owns a pump, worker, exporter, or filesystem path. Commands
are queued and applied by the runtime root on Unity's main thread.

The shared transport is conceptually a single-producer `BufferedSegmentLane<TRecord>` plus a format-owned
writer, encoder, and storage port. This is code reuse, not a shared sink instance or cross-mode coordinator.
A product with facts from more than one thread owns one lane per producing thread and merges those ordered
lanes on its background writer. It never upgrades the hot path to a contended multi-producer queue or routes
worker payloads through Unity merely for storage. The three production compositions are independent:

Full trace and performance profile both commit a new immutable session directory as dense named segments
followed by one terminal manifest. They therefore share one neutral segment-session storage port and one
atomic directory implementation; each product still supplies its own artifact basename, segment extension,
manifest name, encoder, terminal semantics, and controller. The decision journal does not use that session
storage owner because its restart reconciliation, rolling prefix retention, and lack of terminal manifest
are materially different.

| Product | Producer behavior | Storage behavior | Disabled behavior |
|---|---|---|---|
| Full trace | Manual start/stop, exact facts | Complete explicit sessions | No pool or writer |
| Decision journal | Always-on coalesced decisions | Generous rolling retention | Not normally disabled |
| Performance profile | Finite capture in a profiling build | Explicit profile sessions | Absent from normal builds |

The manual full-trace lane initially owns ten reusable blocks sized near 1 MiB. Journal and profile lanes use
the same protocol but choose block count and record capacity from their own measured rates; they do not each
reserve another arbitrary 10 MiB. In any lane, one block is producer-owned, zero or more sealed blocks are
queued or writer-owned, and the remainder are empty. Filling a block performs one constant-time ownership
handoff and immediately claims an empty block; it never flushes or waits. The background thread encodes and
atomically commits sealed blocks, clears them, and returns them to the empty queue. The queues require a
publication memory barrier but no Unity-thread lock or poll. The writer sleeps when no block is ready.

Blocks advance through one ownership cycle:

```text
Free -> ProducerOwned -> Ready -> WriterOwned -> Free
```

Producer and writer advance private indexes through a fixed circular order. They do not scan for an arbitrary
free block. Payload and metadata are prepared first, accepted/sealed counters are published second, and only
then does the producer publish `Ready`; a fast writer therefore cannot make written counts overtake accepted
counts or understate the queue high-water mark. The reverse publication returns a cleared block to `Free`.
Signals may coalesce because the writer drains every consecutive ready block before sleeping again. Stop
seals a partial block, closes producer admission, signals, and returns without joining; the writer drains
accepted blocks and then publishes the terminal session result.

Publishing a full block immediately attempts to claim its one fixed successor. If none is free, the record
that filled the block remains accepted and the append returns the compound `AcceptedAndBufferExhausted`
outcome; the session faults at the next sequence and callers must not retry the accepted record. Fault state
keeps reason and earliest incomplete sequence in one immutable atomic value. A later storage failure may
replace an earlier backpressure reason only when it proves an earlier record was lost. Admission reports
`Faulting` while accepted blocks drain or discard, and terminal `Faulted` only after incomplete completion
evidence has been attempted.

The full trace's ten blocks are throughput headroom, not a retention limit. At the first observed rate they
represent roughly nine minutes of pending data; even if future services fill one every five seconds, the
writer may lag about forty-five seconds before the pool is exhausted. Queue high-water, block fill interval,
encode duration, and write duration are measured so block count and size follow evidence. Exhausting every
empty block is an explicit storage/backpressure failure, not a reason to block gameplay or grow memory
without bound.

Memory remains bounded in every mode. Disk duration does not inherit that small memory bound: a writer drains
incremental segments while recording continues. Backpressure, overwrite, or storage failure stops only the
affected observation session, commits explicit incomplete/gap evidence where possible, and never blocks or
changes gameplay.

## Mode 1: manual full trace

The Runtime page provides momentary **Start full trace** and **Stop trace** controls plus state, duration,
bytes written, segment count, and any loss/failure result. Start arms at the next safe non-pumping boundary;
the UI distinguishes `Arming`, `Recording`, `Stopping`, `Complete`, and `Incomplete`.

A full trace retains:

- every semantic service-cycle event and accepted pump summary;
- feature-detached cycle input, state, and action records when the feature supplies them;
- session/segment fences sufficient to validate order and cross-segment causal parents;
- an explicit completeness classification.

Manual format v1 publishes `segment-{ordinal}.oscs` files with a 96-byte header, at most 3,854 unchanged
schema-v5 semantic records, and a 48-byte footer containing exact terminal sequence fences and an IEEE
CRC32. Ten transport blocks therefore remain below 10 MiB of payload storage and each full segment remains
below 1 MiB. The final fixed 160-byte `manifest.oscm` records the two independent session identities,
topology, accepted and durable counts, committed bytes, timestamps, explicit terminal reason, first missing
transport and semantic sequences, and the permanent `DiagnosticOnly` eligibility. The manifest is published
only after all accepted blocks drain; its absence means an interrupted session rather than an inferred
success.

The background writer atomically claims a new `session-{id}` directory, flushes each file to disk under a
temporary name, then publishes it with a no-overwrite rename. Segment ordinals are dense and sessions are
never resumed or pruned automatically. A semantic-producer failure seals and drains its accepted partial
block before marking the next record missing. Initialization or manifest-publication failure leaves the
durable segments unmodified and no manifest; there is no recovery path that fabricates terminal evidence.

The Common runtime session is composed once by Orb Automata behind a neutral single-command control/status
port. Full-trace, decision-journal, and profile controllers and path policies are suite-host infrastructure;
an individual service supplies only its typed replay payload codecs and optional native-operation profile
stages. Orb Mod Config receives neither the pump nor any filesystem authority. With no active recording, the controller owns no blocks, writer, or storage session. Start creates
the dedicated sink and worker, then remains `Arming` until worker initialization completes, every service is
between cycles, and emergency stop is clear. Waiting for a real emergency clear avoids inventing a historical
`EmergencyEntered` transition at a mid-runtime boundary. A focused two-trace dispatcher lets startup replay
and manual tracing coexist while preserving independent source, close, invalidation, and fault ownership.
Stop detaches at the same settled boundary and returns immediately while the writer drains. Shutdown during
an active cycle force-discards pump ownership, drains the accepted prefix, and publishes an incomplete
`RuntimeShutdown` manifest; a later manual session can attach normally.

The runtime consumes one bounded command and advances the session immediately before emergency
synchronization, then advances it again after the pump. A same-frame emergency transition is therefore real
captured evidence rather than synthesized history. Public status carries only the session-directory basename,
duration, counters, terminal result, and completeness. Active counters publish at state, whole-second, segment,
or failure boundaries instead of revising UI-facing status for every semantic event. A committed manifest
becomes public status only after the session observes the writer's terminal state; the writer may finish the
manifest just before that transition, but active and terminal fields never come from different moments.

Orb Mod Config renders the control as a suite-level card rather than attaching it to a plugin diagnostics
card: the neutral port intentionally carries no feature identity. It polls the port revision only through the
existing open-panel, coordinator-admitted 0.1-second maintenance pass. Command acceptance and producer
consumption are themselves revisioned, so the button shows a disabled `Starting` or `Stopping` state without
a second UI-owned state machine. The view never receives a session, writer, storage adapter, or host path.

`./script/trace --full <session-directory> [report.md]` validates the manual session and emits one report
with separate service, pump, and worker/service semantic views. The reader holds at most one bounded segment
at a time and makes additional sequential passes for the independent views, so neither memory nor an
unrelated replay byte ceiling limits session duration. It verifies every segment envelope and checksum,
dense transport and semantic order, topology, exact cross-segment parent identity and timestamp ordering,
and the terminal manifest fences. A missing manifest is reported as `Interrupted` over the validated durable
prefix; it is never promoted to complete. The first format carries numeric service identities and causal
worker phases, not feature names or physical thread scheduling, so the report says so instead of inferring
them. Existing `.oscr` input retains its original explicit decode path, and `--profile` is rejected for a
manual session.

Manual full trace streams the complete semantic event sequence only. Detached feature inputs, state, and
actions remain owned by the separate `.oscr` replay artifact and are not implied by the manual-trace manifest.

Recording has no small elapsed-time or total-byte cutoff. It ends on the user's stop command,
runtime shutdown, actual storage/backpressure failure, or semantic corruption. Completed manual sessions are
not silently pruned; cleanup is an explicit user operation. The visible byte count makes storage use clear.

Starting in the middle of a running game is valid for diagnosis but is not automatically exact production
replay. Until a checkpoint schema records restorable topology, fairness position, lifecycle/configuration/
strategy roots, wake/receipt state, and feature initial state, the manifest marks such a session
`DiagnosticOnly`. Existing startup-rooted `.oscr` capture and its strict replay eligibility remain unchanged.

The existing two-slot semantic snapshot exporter is not the manual-trace transport. It copies the complete
resident ring on the owner thread and writes repeated whole snapshots. The incremental full-trace sink replaces
that uncomposed path and deletes it after its named ownership, storage-fault, and completeness risks have moved
to focused block-lane, writer, and format tests. The composed `.oscr` artifact exporter remains until a later
change proves byte-for-byte and replay equivalence.

## Mode 2: compact service decision journal

The journal records worker and terminal meaning rather than frames. One bounded numeric entry describes a
cycle's pinned identity, decision/projection, wake, action count, fault state, and eventual batch terminal.
Feature projectors provide stable reason values; native objects and rich strings never enter the journal.

Consecutive equivalent decisions coalesce into one span with first/last time, cycle range, repeat count, and
terminal totals. Empty pump time is represented by elapsed time between entries, not one record per frame.
Lifecycle, configuration, strategy, fault, action, and health transitions always break a span.

The journal is diagnostic rather than replayable. It uses generous rolling retention because it is normally
on, but it reports every evicted segment and gap. The first production candidate retains 10,080 segments: at
one partial checkpoint segment per minute that is at least seven days, while 10,080 completely full segments
would occupy about 631 MiB. This is an explicit live-measurement policy, not a stable default or a replay-ring
event limit.

Journal format v1 uses one fixed 512-byte numeric record. A decision span retains the service ordinal,
lifecycle/configuration/strategy identity, capture and cycle ranges, first/last monotonic time, stable start
and capture codes, exact returned wake, the bounded Common state projection, fault range, terminal result,
action count, and aggregate native mutation totals. Configuration, strategy, lifecycle, and emergency
transitions use explicit record kinds rather than pretending to be cycles. No service names, native objects,
exception text, or formatted feature messages enter the format.

Equivalent decisions coalesce independently per service on the Unity owner thread. The coalescer compares a
fixed maximum of sixteen projection values, updates one open value record, and publishes only on a decision
change, an explicit transition, a durability checkpoint, or shutdown. It does not sort, allocate, lock, or
perform I/O. A checkpoint seals the current partial transport block and immediately claims its successor;
the background writer remains solely responsible for encoding and disk publication. Production owns ten
blocks of 128 records, about 640 KiB of bounded pending record storage, and checkpoints once per minute.
Buffer pressure, observed segment rate, bytes, and eviction counts determine later tuning.

Each `OSJD` segment has an 80-byte envelope, at most 128 fixed records, and a 40-byte `OSJF` footer with exact
run/ordinal/sequence fences and an IEEE CRC32. This is a separate format and sink from `OSCS` full trace;
sharing the reusable block handoff does not share buffers, writer state, backpressure, or retention failure.
The journal consumer maps each new process run onto the persistent ordinal recovered by the format-neutral
atomic file adapter. It rolls only its own `.osjd` directory to an injected segment quota, reports startup and
ongoing eviction counts, and never runs encoding, reconciliation, commit, or deletion on Unity. If a segment
commits but pruning fails, that segment remains durable and the consumer faults before accepting another;
the transport does not relabel it as missing. Stop remains nonblocking. The runtime owner must observe the
old sink reach `Stopped` or `Faulted` before constructing a replacement over the same directory, so a new
reconciliation cannot race the old writer's temporary file.

The record, coalescer, partial-block handoff, segment codec, and restart-aware rolling consumer are
portable-verified. A pump-facing observation port now translates the runtime's immutable returned facts into
those records without owning a clock, filesystem, worker, registry, or game object. One fixed cursor per
registered service retains a captured decision across deferred request publication, joins the later response
and terminal receipt, and sequence-deduplicates lifecycle retirement evidence. The owner supplies one
monotonic observation timestamp at each call, so delayed worker timestamps cannot violate cross-service
ordering. Attaching establishes exact lifecycle, configuration, strategy, fault, lifecycle-version, and
retained lifecycle-fact sequence baselines without fabricating change records.

The observer is the sole owner of ordinal validation and the fixed cursor array. Lifecycle translation
receives an already-bound cursor by reference rather than retaining a second array authority. Each cursor
remains one value containing its baseline, fault, pending-decision, and lifecycle-sequence state; source
ownership separates binding/fault state, decision capture, response/terminal completion, and lifecycle
construction projection without copying that state or adding another transition machine. The observer uses
the same object and containment boundary while separating execution, lifecycle/emergency, and stop/advance
routing. No delegate dispatch or per-observation allocation is introduced.

The journal stores the latest actual feature-returned wake. It does not relabel Common's later fault-retry
deadline as a feature decision. Persistent fault state applies an exact recovery before any new fault carried
by the same fact. Every recovery seals the prior service span; a recovery for a masked older tracker does not
clear a different currently visible fault. Activating a replacement lifecycle requires the retired cycle to
be closed and resets its runner-scoped fault state. An unavailable or capture-faulted attempt retains its
capture/cycle identity but has no strategy generation; only a successfully captured cycle may claim one.
Configuration and strategy publications emit transitions only when their generation advances. Lifecycle
transition code `1` means requested and `2` means activated; emergency transition codes are the stable numeric
emergency reason.

The Common journal runtime initializes and reconciles storage only on its sleeping writer, then arms on the
Unity owner thread until every service is between cycles with one exact current lifecycle and emergency stop
is clear. Baseline capture is isolated in a binder rather than embedded in the frame pump. While attached,
the pump selects its existing immutable rich-fact acquisition path, forwards returned start, response, action,
lifecycle, publication, and emergency facts, and advances the coalescer once per accepted frame. It reads each
configuration/strategy publication pair once and shares that pair with independently attached observers.

Journal stop detaches synchronously and seals accepted records without joining the writer. A storage or
observer fault detaches the journal and leaves scheduling, mutation, and any separately owned semantic trace
running. A pump-owned runtime lease is acquired before storage reconciliation and released only after the old
writer is terminal and detached, so another runtime cannot race the same production directory. The runtime
exposes no automatic restart or fallback path. Disposal initiates the same nonjoining stop; later owner ticks
only reap terminal writer state and cannot re-arm it. Ordinary pump disposal fails closed until that lease is
released. Final composition shutdown instead uses the lease holder's one-shot pump-disposal path: it detaches
and seals the journal, retires the pump so replacement on that pump is impossible, and leaves the independent
writer to drain without joining the Unity thread.

A separate read-only status port carries only numeric transport, retention, queue-pressure, and fault
evidence plus one bounded artifact basename. It has no command, pump, observer, storage, or host-path
authority. One owner-thread producer publishes revisioned immutable values; removing that producer returns
the port to `Unavailable`. Status invariants reject impossible terminal counts, pending terminal blocks,
segment counts larger than durable records, and failure results without the exact first incomplete sequence.

Auto Harvest production composition creates one journal source after constructing the pump and before
returning the lifecycle-bound runtime. The source claims
`BepInEx/config/OrbOfCreation-ModSuite/trace/journal` once per process, supplies a fresh run identity, and has
no restart or alternate path. A pre-existing status producer prevents storage creation. Ordinary storage
initialization failure publishes one terminal status and leaves the pump active; a later observer, writer, or
retention failure detaches only the journal. The runtime advances journal control once after synchronizing
the live emergency state and before the ordinary pump, adding no second service poll or after-pump pass.

Mod Config renders the neutral status as a read-only Runtime-page card. It receives neither a command nor a
root path: only the safe `journal` basename and immutable counters cross the port. There is deliberately no
configuration toggle for the normal journal. UI refresh remains on the existing open-panel maintenance pass.

The warmed production-composition allocation check includes the journal's one pre-pump control tick over
64 successful cycles and retains the existing separate 64-byte owner/worker per-cycle ceilings. The storage
reconciliation assertion also proves that journal recovery begins off the Unity owner thread. This bounds
managed allocation and thread placement; installed-game stage timing remains the profiler's live gate.

The current fact surface cannot honestly report terminal action/native totals without a receipt, an orphan
disposition when lifecycle evidence has no receipt, the scheduler's separate retry deadline, or emergency
episode/generation identity. Those values remain absent rather than inferred. Runtime activation must first
obtain an exact snapshot for every registered service; a contended snapshot delays activation instead of
guessing the initial fault state. Auto Harvest ownership, path, status, and shutdown composition are now
portable-verified.

`./script/trace --journal <journal-directory> [report.md]` selects a third explicit decoder route; it never
sniffs or falls through to the OSCS or OSCR parsers. The read-only adapter inventories one contiguous suffix
of canonical `journal-{ordinal}.osjd` files, ignores only exact owned temporary names, and decodes each
selected segment once through the authoritative codec. Persistent storage ordinals must be contiguous;
record sequences must be contiguous within a process run; every adjacent later run begins at sequence one;
and a run identity cannot reappear after another run. A nonzero first ordinal or first run-local sequence is
reported as absent retained history, not corruption or a proven eviction cause. An interior hole fails closed
because production retention removes only the oldest prefix.

The reader holds one at-most-65,656-byte segment plus run and numeric-service summaries. It streams the
potentially large lineage into a temporary text spool, so a malformed late segment publishes neither a
partial output file nor a plausible partial console report. New commits after the initial inventory are
outside that selected window; loss or corruption of a selected file fails the report. The final atomic
Markdown separates retained run coverage, numeric service/action/native/fault aggregates, and durable
coalesced record order. It explicitly says that OSJD has no terminal manifest, cross-run clock, wall time,
pump/frame timing, physical worker scheduling, service names, projection schema, or replay state. Those facts
remain unavailable rather than inferred. Live rate evidence must still validate or replace the candidate
sizing above.

## Mode 3: opt-in performance profile

Profiling is finite, aggregate-first, and compile-time optional. Ordinary builds do not define the profiling
symbol, so probe call sites and their arguments are omitted: they perform no branch, timestamp read, counter,
allocation, buffer construction, or writer startup. A deliberately produced profiling build includes those
probes and then uses runtime start/stop controls for finite capture windows. It uses preallocated counters and
sparse samples, performs no per-sample formatting or logging, and exports after the window closes.

An ordinary-build structural test inspects the compiled code and rejects profiler types, composition, probe
calls, buffers, worker startup, or profile-only timestamp reads. Compile-time absence is evidence, not a
runtime no-op implementation.

`EnableServiceCycleProfiler=true` is the only profiling build switch and defines
`SERVICE_CYCLE_PROFILE` before every project is evaluated. Profiled stub and real builds use isolated
intermediate and output trees. The normal portable gate first inspects the ordinary Common assembly for
complete profile-namespace absence, then builds and runs a separate locked profiling test graph under the
same 60-second deadline.

Profile format v1 is independent of the semantic trace and decision journal. Each `OSPS` segment has a
128-byte envelope, at most 4,096 fixed 144-byte numeric records, and a 40-byte `OSPF` footer with dense
session/ordinal/sequence fences and an IEEE CRC32. A terminal 160-byte `OSPM` manifest declares calibration,
build identity, trace/allocation flags, accepted and durable counts, the first missing sequence, segment
bytes, and complete or incomplete termination. No manifest means interrupted evidence; it is never inferred
complete.

Records are tagged aggregates or sparse samples. Both retain a neutral numeric stage code, service ordinal,
lifecycle temperature, raw tick range, allocation evidence, and the exact eight-counter operation signature.
Aggregates add occurrence count, total/minimum/maximum elapsed ticks, and total allocation while omitting
cycle/frame identity. Samples retain one cycle/frame identity and require count one with identical
total/minimum/maximum duration. Impossible summaries and unknown tags fail closed. When allocation probing is
unavailable, the session flag says so and every encoded allocation total must be zero; reports must render
that state as `Unavailable`, not measured zero.

The profile consumer owns its own calibration, terminal manifest, storage port, encoding buffer, and one
owner-thread SPSC sink. It reuses only the format-neutral reusable-block handoff. The narrow facade preserves
the transport's `AcceptedAndBufferExhausted` result, so the record that filled the last block is never retried
and the manifest names the next sequence as missing. Stop supplies its terminal reason atomically with the
request, disposal maps deliberately to runtime shutdown, and neither path joins the sleeping writer.

This is developer tooling, so delivery favors one understandable path over defensive completeness. Profiling
must not alter gameplay, but the profiler itself may stop and report its first fault. It does not retry a failed
measurement, switch clocks or counters, recover an interrupted session, or accumulate compatibility fallbacks.
Focused format, containment, and production-boundary tests plus one live run are sufficient acceptance; repeated
adversarial re-review is not part of the profiler workflow.

Profile sessions publish beneath a profile-owned root as `session-{id}` directories containing dense
`segment-{ordinal}.osps` files and one final `manifest.ospm`. Creation and commit use the same neutral atomic
session-directory primitive as manual full trace: same-directory temporary paths, disk flush, close, then a
no-overwrite rename. The adapters share only that filesystem mechanism. Profile sequencing, filenames,
manifest state, and failures remain independently owned. Existing or interrupted sessions are never resumed,
overwritten, or pruned automatically.

The profile-build-only aggregator uses one fixed power-of-two open-addressed table allocated with the session
and keeps its configured load at or below one half. Its exact key is stage, service ordinal, lifecycle,
temperature, and the eight-counter operation signature. Equivalent measurements update occurrence count,
total/minimum/maximum elapsed ticks, total allocation, and the raw start range in place. Each group retains a
fixed-size deterministic reservoir: it keeps the first samples, then uses only the stable group hash and that
group's occurrence ordinal to choose replacements. Unrelated groups and frame cadence therefore cannot alter
which observations a group retains. Stop emits aggregates in first-seen group order, followed by each group's
reservoir. Capacity, allocation-capability disagreement, and arithmetic exhaustion fail the entire profile
instead of publishing plausible partial evidence. Exact `uint` operation counts remain representable through
`uint.MaxValue`; an attempted increment beyond that latches probe failure rather than blending different real
signatures. The warmed record and seal paths allocate zero managed bytes. Raw probes, production sizing,
runtime composition, controls, and reporting are separate responsibilities rather than aggregate-store logic.

The profile-only measurement port now owns raw observation without owning any feature, storage, or writer.
Begin reads the optional allocation counter before the raw timestamp; completion reads the raw timestamp
before allocation, so elapsed time excludes allocation-counter overhead. A one-shot capability check warms
the runtime counter and requires a touched 256-byte witness allocation to advance it by at least the payload
size. Known platform absence becomes `Unavailable`; backward values and unexpected failures invalidate the
profile rather than changing capability mid-session. The resulting capability retains the exact counter and
owner thread; same-thread calibration binds that capability and the exact raw clock while bracketing the suite
monotonic clock with two raw reads and storing their checked midpoint. The owner-thread recorder validates
checked deltas, exact operation counters, and aggregate admission behind one first-fault boundary. Its
preallocated bounded token stack permits nested pump/stage measurements only in LIFO order; reuse,
out-of-order completion, depth exhaustion, or sealing with active work invalidates the whole profile.
Non-process-fatal observation failures stop later recording without escaping into gameplay, and no alternate clock,
counter, retry, or partial-evidence path exists. The warmed begin/complete path allocates zero managed bytes.
Production sizing, Common-stage probes, runtime composition, controls, and reporting remain separate
responsibilities.

The profile-build-only probe seam now carries the actual service ordinal and frame identity from the frame
pump into each capture context. Coordinates have an explicit initialized state, so a missing propagation
path cannot become plausible service-zero/frame-zero evidence. One owner-thread router attaches the current
measurement port; stack-local stage scopes hold only their token and exact operation counters. Successful
work completes explicitly, while a `finally` abandonment pops an exceptional gameplay operation without
reading the clock, aggregating a failed stage, or changing the gameplay exception. Ordinary builds contain
none of the router, coordinates, scope, stage catalog, or call-chain arguments.

Auto Harvest now owns five explicit main-thread stage probes: binding/coherence, shared active-action
traversal, fruit facts, treasure facts, and frame/ownership assembly. One profile-only operation accumulator
is injected through the feature composition; it is neither ambient state nor a Common service locator. Native
reflection helpers count each actual field read, method call, stable-ID read, visited list entry, and newly
created nonempty invocation-argument array. Frame assembly alone counts selected pairs and facts whose native
readiness is verified. These five stages report zero record copies because detached-record construction and
publication own those boundaries later.

The router latches one first probe fault for invalid context, port failure, overlapping stages, or counter
exhaustion. Later probes become inert and gameplay continues. A failed terminal call ends observation without
cleanup retries or an alternate measurement path. Counter exhaustion abandons the measurement instead
of publishing invented work. A successful registry resolution counts one stable-identity lookup at the feature
boundary; its elapsed stage time already includes the resolver internals, so the profiler does not reach into the
shared registry implementation merely to classify private reflection calls.

Every capture snapshots one temperature before binding and uses it for all of its stages. Cold state survives
failures until the first complete coherent pair set matches the planned lifecycle. Explicit lifecycle
invalidation after that success produces `LifecycleRebind` until another coherent success; the success itself
keeps the temperature it started with, and only the next capture is warm. A profile-only preflight checks a warm
cached binding set before the binding clock starts, so normal lifecycle drift labels the rebind work correctly.
If drift races that preflight, the current binding sample is abandoned and the following attempt is a rebind;
elapsed work is never relabeled after its clock began. Partial sibling availability retains the existing gameplay
path but does not publish a supposedly complete binding/coherence measurement. Expected native exceptions
abandon only the interrupted stage before the feature translates them into typed gameplay evidence. Nested
feature stages and counter exhaustion fail the profile without escaping into gameplay.

The same probe now measures detached replay-input construction and bridge publication, semantic start,
terminal, and pump-summary emission, and the overall owner-thread pump. These scopes nest around the existing
Auto Harvest capture stages, so the report can show both the full pump and its feature-owned native work without
adding a second clock or event model. Detached construction and publication each count their one record-copy
boundary; semantic and pump groups retain timing and allocation evidence.

The profiler build exposes a separate Performance profile card when the Auto Harvest ServiceCycle runtime is
constructed. Start creates a session and attaches its recorder to the stable profile probe; Stop detaches and
seals it. There is no fixed capture deadline, configuration-schema change, or coupling to the manual full-trace
control. Ordinary builds contain no profile control, session, probe, buffers, or calls. The session aggregates on the owner thread,
then publishes its bounded records through ten reusable 256-record blocks to the existing lowest-priority
background writer. It requests stop while the game remains open and logs the artifact only after the writer
reports terminal durability. There is no automatic restart, retry, pruning, or quit-time join.

Artifacts live below `BepInEx/config/OrbOfCreation-ModSuite/trace/profile/session-*`. Run
`./script/trace --performance <session-directory> [report.md]` to validate the manifest and dense segment
lineage and render stage, service, temperature, count, average/minimum/maximum microseconds, allocation, and
operation signatures. The report reads aggregates for totals and keeps sparse samples available in the binary
session for later investigation.

Ordinary assemblies retain the feature's neutral reflection-access helpers and the small adapter-composition
object. The helpers contain direct native reflection calls with no profile state or allocation, while the
composition object is allocated once at Auto Harvest construction to keep adapter wiring out of the feature
factory. Both are intentional architecture boundaries; the profiler router, counters, stages, contexts, writer,
and buffers remain compile-absent from the ordinary build.

The first Auto Harvest profile separates:

- binding/coherence checks;
- shared active-action traversal;
- fruit fact capture;
- treasure fact capture;
- frame assembly and ownership projection;
- detached input construction and bridge publication;
- semantic start, terminal, and pump-summary emission.

It also counts list entries, reflected field/method access, stable-ID reads, selected/ready pairs, invocation
argument arrays, and record-copy boundaries. Raw `Stopwatch` ticks are retained with a same-thread timestamp
calibration. Allocation evidence uses `GC.GetAllocatedBytesForCurrentThread` only when a known-allocation
capability probe proves that the runtime implements it; otherwise the report says `Unavailable` instead of
claiming zero.

Cold initialization is reported separately from warmed samples. Trace overhead is measured with matched
operation signatures in profile-only and profile-plus-trace windows, comparing median and tail costs rather
than relying on one mean from an unmatched live run.

The first manually controlled correlated live window ran full trace, profile, and the normally-on journal
together. The journal identifies one Treasure eligibility transition and committed native mutation; full trace
joins that decision to cycle 249's capture, worker evaluation, batch publication, and verified later-frame
action; and the profile contains the matching ready-pair operation signature. Full trace committed all 7,026
accepted records, and the profile committed all 111 aggregate/sample records. There were no worker faults,
rejected actions, orphaned cycles, or missing sequences. This demonstrates that the three products remain
independently writable while describing the same event.

The full trace contains 5,493 accepted pumps: 5,202 were otherwise idle and 291 performed response,
capture, or action work. Whole-pump elapsed time totaled 301.027 ms across the 95.462-second window and
averaged 0.055 ms; the one verified native Treasure submission dominated the tail at 27.410 ms, including
15.565 ms inside the action adapter. Ordinary Treasure fact capture averaged 13.081 microseconds, while the
single ready path took 1.542 ms and performed three additional reflected calls plus one invocation-argument
array. This points future optimization at the rare native-ready path, not the frame/ownership assembly,
detached-record copy, or idle pump.

`./script/trace --dashboard <capture-directory> <dashboard.html>` is the correlated offline projection for
that directory shape. It runs the same strict readers, selects the newest retained journal run, clips its
decision spans to the full-trace window, and calibrates profile raw timestamps onto the Common monotonic clock.
It writes one JSON dataset and an HTML viewer with service/time filters over pump frames, semantic lanes,
decision projections, stage aggregates, and sparse profile samples. Correlation stays a presentation concern:
the three formats, writers, terminal states, and failure boundaries remain independent runtime products.

Common also exposes a separate 1,200-frame owner-thread timing projection for the in-game Runtime page,
approximately 20 seconds at 60 FPS or 40 seconds at 30 FPS. It copies the existing pump report after the pump returns; it does not
start a trace, retain semantic payloads, perform a second service poll, or write to disk. Mod Config redraws
one fitted mesh through its existing open-only 0.1-second maintenance cadence. Every retained accepted pump
frame contributes one bar across the available plot, without paging, aggregation, or 1,200 Unity objects.
Height scales to the actual retained maximum while p95 remains a separate summary statistic. Retention is
frame-count based rather than wall-clock based.

That profile also records `Semantic trace active at start: True` and allocation measurement `Unavailable`.
Its 70.936 microsecond average pump, 27.448 ms maximum pump, and single 1.542 ms ready-Treasure fact sample are
diagnostic elapsed-time evidence, not an uncontaminated CPU or allocation claim. Aggregate maxima do not retain
frame identity, so one wall-clock outlier is not attributed to game work without a matching sampled record or
an external profiler. A trace-off profile is required before using these numbers as a performance acceptance
gate.

## Delivery order

1. Implement and verify the format-neutral reusable block transport and atomic writer ownership contract.
2. Add the independent full-trace sink, control/status port, incremental segments, and Runtime-page controls.
3. Add the strict streaming manual-session reader and scoped offline report views.
4. Add the independent compact response/terminal journal sink with explicit coalescing and retention evidence.
5. Add the profile file adapter, aggregate/sample probes, finite runtime composition, controls, and corrected
   timing labels. **Implemented as a compile-time-opt-in manual Start/Stop session.**
6. Add a replay recording epoch multiplexer for diagnostic mid-runtime detached records.
7. Treat restorable mid-runtime replay checkpoints as a separate, explicitly reviewed format change.

Each slice must keep ordinary disabled-mode overhead to a predictable branch, preserve bounded Unity work,
run disk/encoding work off-thread, and pass trace-on/trace-off gameplay equivalence checks.
