# Service-cycle observability

> **Lifecycle: Accepted production observability / portable-verified.** The production runtime exposes bounded
> live diagnostics, a causal semantic ring, manual full traces, a rolling decision journal, and compile-time
> performance profiles. These products separate manual investigation, long-running decision history, and
> performance measurement without changing gameplay scheduling.

[Back to dossier](README.md) | [Service-cycle runtime](service-cycle-runtime.md)

## Four systems, four mandates

Per [the north star](../north-star.md), observability is four systems and each has one job. That document
wins wherever this one drifts from it.

1. **Profiler** — debug builds only, compiled out of release entirely. Measured spans over every pump-frame
   phase and worker stage, reported through the dashboard script. Internal "how fast is this part" tooling;
   pre-allocation is welcome here as a performance detail.
2. **Full trace** — the bug-report recorder, available in release builds: record, reproduce, share the
   artifact. Its mandate is the four streams — raw capture data, configuration publications, strategy
   publications, action outcomes — plus the high-level runtime events needed to follow a session. Near-zero
   cost while idle; extra cost and allocation are acceptable while recording, and in debug builds its own
   overhead must never appear inside profiler spans. **What exists records three of the four.** The
   raw-capture stream has no store: its volume and codec are an open decision, filed as
   [full-trace world store](../plans/full-trace-world-store.md), so the mandate is not met yet.
3. **Decision log** — always on, high signal, low noise: lifecycle boundaries, strategy changes,
   configuration saves, emergency stops, service health transitions, not per-cycle minutiae. Size-capped and
   rotated; the mandate is that the suite — BepInEx's own logs included — keeps at most ~100 MB on disk
   however long it runs unattended. What exists covers the writing the suite does itself: the journal is
   capped at 99.8 MB and each launch prunes all but the newest eight run folders, which are bounded by count
   and, deliberately, not by size. Nothing constrains BepInEx's own `LogOutput.log`, so the mandate as
   written is not met.
4. **Replay** — retired as a runtime system. Scripted re-execution of recorded runs was deleted, not
   rebuilt; hand-crafted scenario fixtures serve its testing value, and the full trace records exactly the
   inputs a future recompute harness would need without the runtime carrying a line of machinery for it.

These products share one small mechanical foundation: identity value types, atomic file storage, and reusable
block-transport code. Each owns a separate pool, queue, sleeping writer, control state, format, retention
policy, status, and failure boundary. One product cannot consume another's buffers or backpressure its
producer, and a compact journal never claims to be a complete trace.

## Measured sizing evidence

An armed full trace runs about 1.15 MiB/minute at schema v7's 288-byte record, or roughly 69 MiB/hour.
That rate is set by the schema storing one fixed record for every accepted pump, not by any feature
copying a large game state: in the first installed-game session about 79% of the records were pump
summaries and 95% of those pumps were otherwise idle. It is why an always-on full trace is not the
design, and why the ring in mode 1b exists instead.

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
| Decision journal | Always-on coalesced decisions | Capped rolling retention | Not normally disabled |
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

Producer and writer advance private indexes through a fixed circular order rather than scanning for a
free block. Payload and metadata are prepared first, accepted/sealed counters published second, and
only then does the producer publish `Ready`, so a fast writer cannot make written counts overtake
accepted counts or understate the queue high-water mark. Signals may coalesce, because the writer
drains every consecutive ready block before sleeping again. Stop seals a partial block, closes
producer admission, signals, and returns without joining.

Publishing a full block immediately claims its one fixed successor. If none is free, the record that
filled the block remains accepted and the append returns the compound `AcceptedAndBufferExhausted`
outcome; the session faults at the next sequence and callers must not retry the accepted record. Fault
state keeps reason and earliest incomplete sequence in one immutable atomic value, and a later storage
failure may replace an earlier backpressure reason only when it proves an earlier record was lost.
Admission reports `Faulting` while accepted blocks drain, and terminal `Faulted` only after incomplete
completion evidence has been attempted.

Block counts are throughput headroom, not retention: the full trace's ten represent roughly nine
minutes of pending data at the observed rate. Queue high-water, block fill interval, encode duration,
and write duration are measured so block count and size follow evidence. Exhausting every empty block
is an explicit storage failure, not a reason to block gameplay or grow memory without bound.

Memory remains bounded in every mode. Disk duration does not inherit that small memory bound: a writer drains
incremental segments while recording continues. Backpressure, overwrite, or storage failure stops only the
affected observation session, commits explicit incomplete/gap evidence where possible, and never blocks or
changes gameplay.

## Mode 1: manual full trace

The Runtime page provides momentary **Start full trace** and **Stop trace** controls plus state, duration,
bytes written, segment count, and any loss/failure result. Start arms at the next safe non-pumping boundary;
the UI distinguishes `Arming`, `Recording`, `Stopping`, `Complete`, and `Incomplete`. In a release build that
control is the only way a trace begins. A profiling build starts one automatically when the runtime is
constructed, alongside the performance profile.

A full trace retains:

- every semantic service-cycle event and accepted pump summary;
- the configuration and strategy publications a cycle pinned, and the outcome of every action it
  dispatched, under that cycle's identity;
- the feature's own numeric projection of a cycle, when the feature writes one;
- session/segment fences sufficient to validate order and cross-segment causal parents;
- an explicit completeness classification.

It does not retain the raw world capture; see the open decision named above.

Schema v7 names all four generations a cycle pinned, so every cycle-scoped event says which reading of the
world it acted on and not only which configuration and strategy. The pump summary carries how many cycles the
frame started and how many services the world freshness gate held; read together, `0 started / 3 held` is a
stalled collector and `0 started / 0 held` is an idle suite. The two are indistinguishable without both.

Capture and action facts also name the pump frame they ran inside, on the frame-identity offset the record
already reserved — same 288 bytes, same format version. The field is optional on those kinds rather than
required, because the same facts can be emitted from a host control transition between frames: an emergency
stop rejecting live batches belongs to no frame and says so by carrying none. Frame zero is a legal frame, so
absence is the field's absence and never a zero value.

Manual format v1 publishes `segment-{ordinal}.oscs` files with a 96-byte header, at most 3,640 unchanged
schema-v7 semantic records, and a 48-byte footer containing exact terminal sequence fences and an IEEE
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

The session is composed once by Orb Automata behind a neutral single-command control/status port. With
no active recording the controller owns no blocks, writer, or storage session. Start creates the sink
and worker and stays `Arming` until worker initialization completes, every service is between cycles,
and emergency stop is clear — waiting for a real emergency clear avoids inventing a historical
`EmergencyEntered` transition at a mid-runtime boundary. A two-trace dispatcher lets a host-owned
trace and manual tracing coexist with independent source, close, invalidation, and fault ownership.
Stop detaches at the same settled boundary and returns immediately while the writer drains; shutdown
during an active cycle drains the accepted prefix and publishes an incomplete `RuntimeShutdown`
manifest.

The runtime advances the session immediately before emergency synchronization and again after the
pump, so a same-frame emergency transition is real captured evidence rather than synthesized history.
Public status carries only the session-directory basename, duration, counters, terminal result, and
completeness, published at state, whole-second, segment, or failure boundaries rather than per event;
active and terminal fields never come from different moments.

Orb Mod Config renders the control as a suite-level card rather than a plugin diagnostics card,
because the neutral port carries no feature identity, and polls it on the existing open-panel
0.1-second maintenance pass. Command acceptance and producer consumption are revisioned, so the button
shows a disabled `Starting` or `Stopping` without a second UI-owned state machine. The view receives
neither the pump nor any session, writer, storage adapter, or host path.

**What a capture argument may name.** `--full` and `--dashboard` resolve the input themselves: a
`full/session-<id>` directory, the `full/` folder holding it, the `run-<timestamp>/` folder that run wrote,
or the trace root that holds the run folders. A root resolves to its newest run folder and says on stderr
which one it read and which it skipped; a folder holding two full-trace sessions is an error that lists
them. `--journal` and `--performance` take their own directory directly, because neither is reachable from
a full-trace session.

`./script/trace --full <capture> [report.md]` validates the manual session and emits one report
with separate service, pump, and worker/service semantic views. The reader holds at most one bounded segment
at a time and makes additional sequential passes for the independent views, so memory does not limit
session duration. It verifies every segment envelope and checksum,
dense transport and semantic order, topology, exact cross-segment parent identity and timestamp ordering,
and the terminal manifest fences. A missing manifest is reported as `Interrupted` over the validated durable
prefix; it is never promoted to complete. The records carry numeric service identities and causal worker
phases, not feature names or physical thread scheduling. Names come from the session roster beside the
records when the capture wrote one, and a capture without one is reported under its numbers rather than
having names inferred for it. A manual session is the tool's default input mode.

### Generation-keyed publication stores

The semantic stream says which generation of configuration and strategy a cycle decided against; it does not
say what those generations held. While a session records, it writes `configuration-<generation>.oscv` and
`strategy-<generation>.oscv` beside its segments — the first time it sees each generation, so three services
deciding on one configuration cost one payload rather than three. A decision event's generation numbers are
therefore a lookup into the session's own stores, which is what makes the artifact self-contained.

Store files are UTF-8 text: a header line of `OSCV <version> <store> <generation-hex>`, then sorted
`path = value` lines. Text and reflected rather than a fixed-width codec, because these publications are
settings trees that grow with the suite and a hand-written codec would silently stop recording what was added
to them last; sorting means two generations of one store diff to what actually changed. A store write that
fails stops storing and does not stop the recording — losing the settings behind a generation costs a reader
less than losing the events would. The analysis tool's report lists the stores a session recorded.

### The session roster

A fixed record identifies a service by a number, and a number tells a reader nothing: a capture that says
service 2 spent four milliseconds deriving has not said which feature that was. So a recording writes
`roster.oscr` beside its segments, once, before the manifest seals the session — one line per service, giving
the trace identity, the identity the runtime actually registered, and the name the rest of the suite uses for
it. The record layout is untouched; the names are said once per session rather than once per event.

Both artifacts that share the session format carry it, including the in-game ring dump, because the reader of
a bug report has no more idea what service 2 was than the reader of a capture. The file is UTF-8 text with a
header line of `OSCR <version> <count>` and `<kind> <identity> <machine-id> = <display name>` rows. Rows are
kinded rather than assumed to be services: the same question is coming for the configuration and strategy
publications, and a roster that already says what kind of thing each row names will answer it without a
second artifact.

A service the suite has no display name for keeps its registered identity rather than being left out, so an
unnamed feature reads as `orbautomata.auto-agromancy` — true, and visibly missing a name — instead of as
"Service 4", which would look finished while saying nothing. A roster that cannot be written, or that a
reader cannot parse, costs the names and nothing else: it degrades to exactly where an absent one does, which
is the numeric identities the format has always carried. Captures recorded before the roster existed
therefore keep reading.

The world store the mandate's raw-capture stream needs is **not** here. The world republishes four times a
second and its payload is the entire raw reading of the game, so one payload per world generation is a
different problem in kind from one per settings save, and it has no persistence codec to borrow. It is
deliberately outstanding rather than approximated; the volume and codec ruling it waits on is filed in
[the world store plan](../plans/full-trace-world-store.md).

Manual full trace therefore carries three of the four streams the north star's full-trace mandate names —
configuration publications, strategy publications, and action outcomes on the semantic wire — and not raw
capture data.

## Mode 1b: the recent-event ring

The suite always holds its most recent semantic events — currently 8,192 of them, a few megabytes — in a
fixed ring attached to the pump at composition. It never writes to disk on its own; the Runtime page's
**Dump recent events** control is what turns the ring into an artifact, written synchronously into
`trace/run-<timestamp>/recent/`. The status names the artifact, the event count, and how many older events
the ring had already dropped, because a bounded ring may lose history but may not lose it silently.

This is what an always-on recorder would have been, minus the disk. An always-on full trace runs about
69 MiB an hour, which both defeats the suite's disk budget and contradicts the full-trace mandate's own
near-zero idle cost. What a ring buys instead is that the events leading up to a problem exist when a user
notices it, rather than only after they reproduce it under an armed session.

A dump is the same `OSCS`/`OSCM` pair an armed session produces, so the analysis tool reads one without
knowing it is a dump. It lands in its own `recent/` directory rather than in `full/` because the dashboard
correlates exactly one full session with the profile beside it, and a dump is neither that session nor a
replacement for it.

Recording has no small elapsed-time or total-byte cutoff. It ends on the user's stop command,
runtime shutdown, actual storage/backpressure failure, or semantic corruption. A recording session is never
truncated and no completed session is pruned in part. The visible byte count makes storage use clear.

What is bounded is the number of `trace/run-<timestamp>/` capture folders. Full trace and performance profile
write one folder per process launch; the always-on journal does not. Nothing else deletes those folders, so
each launch prunes the oldest until at most eight remain, counting the folder that launch may write. Folders
go whole: a surviving one is still the correlated full/profile pair the analysis tool requires, which pruning
by file or by byte budget would destroy. The folder name is a fixed-width UTC timestamp, so oldest means
oldest by name rather than by a filesystem timestamp a copy would not preserve. A folder that cannot be
deleted is left for the next launch; retention never denies the suite its own recording.

Starting in the middle of a running game is valid for diagnosis but does not root the session at a known
initial state. Until a checkpoint schema records topology, fairness position, lifecycle/configuration/
strategy roots, wake/receipt state, and feature initial state, the manifest marks such a session
`DiagnosticOnly`.

The incremental full-trace sink is the only manual-trace transport; its ownership, storage-fault, and
completeness risks are covered by the block-lane, writer, and format tests.

## Mode 2: compact service decision journal

The journal records worker and terminal meaning rather than frames. One bounded numeric entry describes a
cycle's pinned identity, decision/projection, wake, action count, fault state, and eventual batch terminal.
Feature projectors provide stable reason values; native objects and rich strings never enter the journal.

Consecutive equivalent decisions coalesce into one span with first/last time, cycle range, repeat count, and
terminal totals. Empty pump time is represented by elapsed time between entries, not one record per frame.
Lifecycle, configuration, strategy, fault, action, and health transitions always break a span.

The journal is diagnostic rather than a complete record. It uses rolling retention because it is normally on,
and it reports every evicted segment and gap. Production retains 1,520 segments. A segment is 80 header plus
128 fixed 512-byte records plus a 40-byte footer, so 1,520 completely full segments occupy 99,797,120 bytes —
the journal's share of the north star's ~100 MB cap on the suite's total on-disk footprint, with the run
folders and BepInEx's own logs the rest of it. The floor on coverage is the checkpoint: one partial segment
per minute is over 25 hours of unattended play before the oldest evidence rolls off, and a journal whose
segments fill on decisions rather than on the checkpoint covers proportionally longer. The cap is the budget,
not a live-measurement candidate; segment rate, bytes, and eviction counts remain reported so the split
between the products can move on evidence.

Journal format v2 uses one fixed 512-byte numeric record. A decision span retains the service ordinal,
lifecycle/configuration/strategy identity, capture and cycle ranges, first/last monotonic time, stable start
and capture codes, exact returned wake, the bounded Common state projection, fault range, terminal result,
action count, how many of the committed actions committed by publishing, and aggregate native mutation
totals. That published count is what makes the span's evidence claims checkable: an action handing over a
snapshot makes no native call, so only the rest of the batch owes attempted and committed mutations, and a
span that published everything must carry no native evidence at all. Without it the record cannot tell an
action that could not call the game from one that owed a call and produced none.

Configuration, strategy, lifecycle, world-gate, and emergency transitions use explicit record kinds rather
than pretending to be cycles. Lifecycle and world-gate transitions carry the service they belong to;
configuration, strategy, and emergency transitions carry none, because the thing that changed is the suite's.
No service names, native objects, exception text, or formatted feature messages enter the format.

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

Reconciliation asks the consumer's own header probe whether the newest retained segment is one this build can
write after; the adapter still knows nothing about the payload. A store that cannot be continued — another
schema version, or an artifact whose name the adapter cannot read — is deleted, counted as discarded, and
restarted at ordinal zero, because the directory outlives the process and refusing it instead would leave the
journal permanently dead on that machine. Ordinal exhaustion remains fatal and destroys nothing, because a
store that cannot name a successor is still readable evidence. The discarded count reaches the log once,
loudly, when recording starts, and stays on the status card.

A pump-facing observation port translates the runtime's immutable returned facts into those records without
owning a clock, filesystem, worker, registry, or game object, and is the sole owner of ordinal validation and
the fixed cursor array. One cursor per registered service retains a captured decision across deferred request
publication, joins the later response and terminal receipt, and sequence-deduplicates lifecycle retirement
evidence. The owner supplies one monotonic observation timestamp at each call, so delayed worker timestamps
cannot violate cross-service ordering. Attaching establishes exact lifecycle, configuration, strategy, fault,
and lifecycle-sequence baselines without fabricating change records. No delegate dispatch or per-observation
allocation is introduced.

The journal stores the latest actual feature-returned wake. It does not relabel Common's later fault-retry
deadline as a feature decision. Persistent fault state applies an exact recovery before any new fault carried
by the same fact. Every recovery seals the prior service span; a recovery for a masked older tracker does not
clear a different currently visible fault. Activating a replacement lifecycle requires the retired cycle to
be closed and resets its runner-scoped fault state. An unavailable or capture-faulted attempt retains its
capture/cycle identity but has no strategy generation; only a successfully captured cycle may claim one.
Configuration and strategy publications emit transitions only when their generation advances, and they name
the suite rather than a service: there is one configuration record and one strategy bulletin that every
service reads, so one publication is one record however many services are registered. Being suite-wide, such a
record closes every open decision span before itself, the way an emergency transition does. Lifecycle
transition code `1` means requested and `2` means activated; emergency transition codes are the stable numeric
emergency reason.

A service the world freshness gate holds closed reaches the journal as its own record kind, naming the service
and its active lifecycle generation. The gate is otherwise silent — a held service attempts nothing, which
reads exactly like a service that had no work — and a stalled world collector holds every mutating service at
once, so without the record a stalled suite is an absence of evidence rather than evidence. Transition code
`1` means the live world was collected before the service's own last action attempt; `2` means no source could answer
at all. A hold is one record however long it lasts: the gate re-defers a held service every frame, and a
further record follows only when the action being waited past changes or when the reason changes. A hold that
never ends is therefore one record whose missing successor is the stall itself.

The Common journal runtime initializes and reconciles storage only on its sleeping writer, then arms on the
Unity owner thread until every service is between cycles with one exact current lifecycle and emergency stop
is clear. Baseline capture is isolated in a binder rather than embedded in the frame pump. While attached,
the pump selects its existing immutable rich-fact acquisition path, forwards returned start, response, action,
lifecycle, publication, world-gate, and emergency facts, and advances the coalescer once per accepted frame.
World-gate facts are scanned only on a frame that deferred somebody, because a slot keeps its last deferral
indefinitely and an unconditional scan would walk every slot every frame to rediscover a known hold. It reads the
suite's one configuration/strategy publication pair once per frame from the registry and shares that pair with
independently attached observers, rather than reading the same pair off each registered slot.

Journal stop detaches synchronously and seals accepted records without joining the writer. A storage or
observer fault detaches the journal and leaves scheduling, mutation, and any separately owned semantic trace
running. The observation port contains every failure it can survive, and keeps the first exception together
with the observation it was caught in, so a dead journal reports the guard it violated rather than only that
its producer failed; a coalescer that lost its sink names the sink. A pump-owned runtime lease is acquired
before storage reconciliation and released only after the old writer is terminal and detached, so another
runtime cannot race the same production directory. The runtime
exposes no automatic restart or fallback path. Disposal initiates the same nonjoining stop; later owner ticks
only reap terminal writer state and cannot re-arm it. Ordinary pump disposal fails closed until that lease is
released. Final composition shutdown instead uses the lease holder's one-shot pump-disposal path: it detaches
and seals the journal, retires the pump so replacement on that pump is impossible, and leaves the independent
writer to drain without joining the Unity thread.

A separate read-only status port carries numeric transport, retention, queue-pressure, and fault
evidence, one bounded artifact basename, and — when something named a fault — the observation it happened
in and that failure's own message. It has no command, pump, observer, storage, or host-path authority. One
owner-thread producer publishes revisioned immutable values; removing that producer returns
the port to `Unavailable`. Status invariants reject impossible terminal counts, pending terminal blocks,
segment counts larger than durable records, failure results without the exact first incomplete sequence, and
fault detail on an unavailable port.

Automata production composition creates one journal source after constructing the pump and before
returning the lifecycle-bound runtime. The source claims
`BepInEx/config/OrbOfCreation-ModSuite/trace/journal` once per process, supplies a fresh run identity, and has
no restart or alternate path. That path is deliberately stable across launches rather than nested under the
launch's own `run-<timestamp>/` folder: the rolling segment cap and the restart reconciliation both govern one
directory, so a per-launch directory handed every launch a fresh budget and left reconciliation nothing to
reconcile. The correlated-capture reader therefore resolves the journal beside a run folder rather than inside
it. A pre-existing status producer prevents storage creation. Ordinary storage
initialization failure publishes one terminal status and leaves the pump active; a later observer, writer, or
retention failure detaches only the journal. The runtime advances journal control once after synchronizing
the live emergency state and before the ordinary pump, adding no second service poll or after-pump pass.

Mod Config renders the neutral status as a read-only Runtime-page card. It receives neither a command nor a
root path: only the safe `journal` basename and immutable counters cross the port. There is deliberately no
configuration toggle for the normal journal. UI refresh remains on the existing open-panel maintenance pass.

The warmed production-composition allocation check includes the journal's one pre-pump control tick over
64 successful cycles under the separate 64-byte owner/worker per-cycle ceilings, and proves that journal
recovery begins off the Unity owner thread. Installed-game stage timing remains the profiler's live gate.

The current fact surface cannot honestly report terminal action/native totals without a receipt, an orphan
disposition when lifecycle evidence has no receipt, the scheduler's separate retry deadline, or emergency
episode/generation identity. Those values remain absent rather than inferred. Runtime activation must first
obtain an exact snapshot for every registered service; a contended snapshot delays activation instead of
guessing the initial fault state.

`./script/trace --journal <journal-directory> [report.md]` selects a third explicit decoder route; it never
sniffs or falls through to the OSCS parser. The read-only adapter inventories one contiguous suffix
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
pump/frame timing, physical worker scheduling, service names, or projection schema. Those facts remain
unavailable rather than inferred. Live rate evidence must still validate or replace the candidate
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

Stage codes come from one enumeration, `ServiceCycleProfileSpan`, which the runtime, both services and the
analysis tool all read. The numbers are wire values, so a retired span's number is burned rather than reused
and the blocks stay non-contiguous: 1-999 the suite runtime, 1000-1999 Auto Harvest, 2000-2999 Auto Buy. The
frame's own phases — acquire responses, dispatch actions, start cycles, reconcile lifecycle — are measured
alongside the whole pump; a frame reconciles lifecycle twice, so that span is two occurrences per frame.
Worker stages remain unmeasured: the probe is owner-thread affine and a worker definition may hold no
runtime-owned storage.

A span the enumeration marks as observer overhead — the three semantic-emission spans — is subtracted from
every span enclosing it before it is recorded. The full trace emits from inside the frame, so without that
fence `Overall pump` would report the cost of recording the frame rather than the cost of the frame, which is
exactly the red herring the full-trace mandate forbids. The subtraction happens in the measurement recorder,
which already tracks the nesting, so the probe API is unchanged and no reader has to know to subtract.

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

The aggregator uses one fixed power-of-two open-addressed table allocated with the session, keyed on stage,
service ordinal, lifecycle, temperature, and the eight-counter operation signature, at a load of one half or
less. Equivalent measurements update occurrence count, total/minimum/maximum elapsed ticks, total allocation,
and the raw start range in place, and each group retains a fixed-size deterministic reservoir chosen from the
stable group hash and that group's occurrence ordinal alone — unrelated groups and frame cadence cannot alter
which observations a group keeps. Stop emits aggregates in first-seen group order, followed by each group's
reservoir. Capacity, allocation-capability disagreement, and arithmetic exhaustion fail the entire profile
rather than publishing plausible partial evidence; an operation count that would exceed `uint.MaxValue`
latches probe failure rather than blending two real signatures. The warmed record and seal paths allocate zero
managed bytes.

The measurement port owns raw observation and no feature, storage, or writer. Begin reads the optional
allocation counter before the raw timestamp and completion reads the timestamp first, so elapsed time excludes
allocation-counter overhead. A one-shot capability check warms the runtime counter and requires a touched
256-byte witness allocation to advance it by at least the payload size; known platform absence becomes
`Unavailable`, while backward values and unexpected failures invalidate the profile rather than changing
capability mid-session. Same-thread calibration binds that capability and the exact raw clock while bracketing
the suite monotonic clock with two raw reads and storing their checked midpoint. The owner-thread recorder
validates checked deltas, operation counters, and aggregate admission behind one first-fault boundary, and its
preallocated token stack permits nested measurements only in LIFO order — reuse, out-of-order completion,
depth exhaustion, or sealing with active work invalidates the whole profile. No alternate clock, counter,
retry, or partial-evidence path exists.

The probe seam carries the actual service ordinal and frame identity from the pump into each capture context,
with an explicit initialized state so a missing propagation path cannot become plausible service-zero,
frame-zero evidence. One owner-thread router attaches the current port; stack-local stage scopes hold only
their token and counters, and a `finally` abandonment pops an exceptional gameplay operation without reading
the clock, aggregating a failed stage, or changing the gameplay exception. The router latches one first probe
fault — invalid context, port failure, overlapping stages, counter exhaustion — after which later probes are
inert and gameplay continues. Ordinary builds contain none of the router, coordinates, scope, stage catalog,
or call-chain arguments.

Feature stages live on native adapters, where the main-thread cost is: Auto Harvest owns binding/coherence,
prototype resolution, the before and after snapshots, native submission, and postcondition verification; Auto
Buy owns its queue-room read, candidate resolution, admission revalidation, and native submission. One
profile-only operation accumulator is injected through each feature composition — neither ambient state nor a
Common service locator — and the reflection helpers count each actual field read, method call, stable-ID read,
visited list entry, and newly created nonempty invocation-argument array.

Every capture snapshots one temperature before binding and uses it for all of its stages. Cold state survives
failures until the first complete coherent binding matches the planned lifecycle; explicit lifecycle
invalidation after that produces `LifecycleRebind` until another coherent success, and the success itself keeps
the temperature it started with. A profile-only preflight checks a warm cached binding before the binding clock
starts, so ordinary lifecycle drift labels rebind work correctly; if drift races that preflight the sample is
abandoned and the next attempt is a rebind, because elapsed work is never relabeled after its clock began.
Expected native exceptions abandon only the interrupted stage before the feature translates them into typed
gameplay evidence.

The same probe measures semantic start, terminal, and pump-summary emission, and the overall owner-thread
pump. Those scopes nest around the feature stages, so the report shows both the whole pump and the
feature-owned native work inside it without a second clock or event model.

The profiler build exposes a separate Performance profile card when the ServiceCycle runtime is
constructed. Start creates a session and attaches its recorder to the stable profile probe; Stop detaches and
seals it. In a profiling build the card is not the only way in: the runtime auto-starts this profile and the
manual full trace together at construction, so a profiling session records from the first frame — which is
also the only way the cold-start frames are observable at all. There is no fixed capture deadline, configuration-schema change, or coupling to the manual full-trace
control. Ordinary builds contain no profile control, session, probe, buffers, or calls. The session aggregates on the owner thread,
then publishes its bounded records through ten reusable 256-record blocks to the existing lowest-priority
background writer. It requests stop while the game remains open and logs the artifact only after the writer
reports terminal durability. There is no automatic restart, retry, pruning, or quit-time join.

Artifacts live below `BepInEx/config/OrbOfCreation-ModSuite/trace/run-<timestamp>/profile/session-*`, in
that launch's own run folder beside the full trace it correlates with. Run
`./script/trace --performance <session-directory> [report.md]` to validate the manifest and dense segment
lineage and render stage, service, temperature, count, average/minimum/maximum microseconds, allocation, and
operation signatures. The report reads aggregates for totals and keeps sparse samples available in the binary
session for later investigation.

Ordinary assemblies retain each feature's neutral reflection-access helpers and its small
adapter-composition object: the helpers make direct native reflection calls with no profile state or
allocation, and the composition object is allocated once at feature construction to keep adapter wiring out
of the feature factory. The profiler router, counters, stages, contexts, writer, and buffers remain
compile-absent.

A profile counts list entries, reflected field and method access, stable-ID reads, invocation
argument arrays, and record-copy boundaries, and retains raw `Stopwatch` ticks with a same-thread
timestamp calibration. Allocation evidence uses `GC.GetAllocatedBytesForCurrentThread` only when a
capability probe proves the runtime implements it; otherwise the report says `Unavailable` rather
than claiming zero. Cold initialization is reported separately from warmed samples, and trace
overhead is measured with matched operation signatures in profile-only and profile-plus-trace
windows, comparing median and tail costs rather than one mean from an unmatched live run.

A correlated live window has run full trace, profile, and the always-on journal together and shown the
property that matters: the three products stay independently writable while describing the same event, with
one eligibility transition and committed native mutation appearing in all three. Session numbers are not kept
here — the trace dashboard answers the same questions on any fresh capture.

`./script/trace --dashboard <capture> <dashboard.html>` is the correlated offline projection. It runs the same strict readers, selects the newest retained journal run, clips its
decision spans to the full-trace window, and calibrates profile raw timestamps onto the Common monotonic clock.
It writes one JSON dataset and an HTML viewer. Correlation stays a presentation concern:
the three formats, writers, terminal states, and failure boundaries remain independent runtime products.

The viewer is organised by service rather than by phase. An overview page spends the pump frame as a stacked
bar per frame — response, capture, action, and whatever the pump measured but did not attribute — and then
one page per service that ran, each spending a cycle as capture, handoff, derive, project, and dispatch from
the wire's own timestamps. Derive is labelled *math + allocation* because no seam exists between them: the
evaluator's arithmetic and its snapshot allocation happen in one pass. Where a service owns profile spans they
appear beside its cycle stack; where it owns none the panel says so rather than rendering nothing. A final
evidence page keeps the semantic lanes, decision projections, stage aggregates, and the individual retained
profile samples. Pages are titled from the capture's roster. Older captures have none, so the shape-based
inference that predates it is kept for them — a service whose every commit published rather than mutated is
the collector — and a service that inference cannot name is still shown under its number.

Every pump frame and every cycle is classified cold-process, lifecycle-rebind, or warm, and the viewer
defaults to warm only. A first pass carries the JIT and the first touch of every buffer behind it, so leaving
it in an aggregate misreports the steady state by an order of magnitude; excluding it silently would hide a
real cost. Excluded frames are counted and totalled in a strip above the charts, and one toggle puts them
back. Above roughly 1,500 frames in range the pump chart switches to equal-time buckets of means and says so,
because one bar per frame stops being readable long before it stops being drawable.

The charting library is vendored into the tool and inlined into the generated page rather than fetched from a
CDN. A dashboard is a file that gets attached to an issue and opened days later on a machine that may have no
network; a page that renders empty offline is not evidence.

The full trace is the only required evidence. The profile session and the decision journal are optional, and
their absence is a fact about the capture rather than an error — a release build has no `profile/` session at
all, because the profiler is compiled out of one. A missing product leaves its panes empty and adds a note
that the dashboard renders as a banner under the header, so the reader is told what is not there instead of
being refused a dashboard.

Common also exposes a separate 1,200-frame owner-thread timing projection for the in-game Runtime page,
approximately 20 seconds at 60 FPS or 40 seconds at 30 FPS. It copies the existing pump report after the pump returns; it does not
start a trace, retain semantic payloads, perform a second service poll, or write to disk. Mod Config redraws
one fitted mesh through its existing open-only 0.1-second maintenance cadence. Every retained accepted pump
frame contributes one bar across the available plot, without paging, aggregation, or 1,200 Unity objects.
Height scales to the ninety-ninth percentile of the retained window rather than to its maximum, and frames
above that scale are drawn full height in red. One warm-up frame costs a couple of hundred milliseconds
against a steady frame of a fraction of one, so scaling to the maximum flattened every ordinary frame to
nothing for the whole twenty seconds that one sample stayed retained; a percentile absorbs a scene load, a
save, or a collection the same way, which a "first N frames" rule would not. The true maximum, the p95, and
the count of clipped frames stay in the summary line. Retention is frame-count based rather than wall-clock
based.

**What a profile's numbers may and may not be used for.** They are diagnostic elapsed time, not an
uncontaminated CPU or allocation claim, and a profile recorded with the semantic trace active is
measuring both. Aggregate maxima do not retain frame identity, so one wall-clock outlier is not
attributed to game work without a matching sampled record or an external profiler. A trace-off
profile is required before any of it becomes a performance acceptance gate.

## What is left to deliver

Two slices remain:

1. Widen the full trace to the four streams the north star names and make it release-capable.
2. Bring the suite's total on-disk footprint under the ~100 MB cap. The journal half is done — one stable
   directory capped at 1,520 segments (99.8 MB), with each launch pruning all but the newest eight run
   folders, count-bounded and by design not size-bounded. The cap as the north star states it also covers
   BepInEx's own logs, and nothing constrains `LogOutput.log` today, so the mandate is open.

Each slice must keep ordinary disabled-mode overhead to a predictable branch, preserve bounded Unity work,
run disk/encoding work off-thread, and pass trace-on/trace-off gameplay equivalence checks.
