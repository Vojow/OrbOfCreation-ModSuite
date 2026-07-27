# Runtime architecture engineering decisions

> **Lifecycle: Accepted production foundation.** These decisions are normative for new architecture work.

[Back to dossier](README.md) | [Service-cycle runtime](service-cycle-runtime.md) | [Goals and invariants](goals-and-invariants.md) | [Architecture](architecture.md)

This is a register. Each entry states one durable choice and points at the document that specifies
it; where an entry carries the specification itself, it says so. D16 and D17 are cited from source
comments, so their numbers are fixed — no entry here is ever renumbered or reused.

## D1 - Feature services are explicit modules

Each automation capability is one cohesive module separating configuration, state, evaluation, action
execution, diagnostics projection, and native adapters, with tests mirroring those responsibilities.
Not flat project-root files, partial-class state piles, or one central feature engine: folder and
dependency structure are part of correctness. See [modularity invariants](goals-and-invariants.md).

## D2 - Composition is explicit and deterministic

Services register through one deliberate Common composition boundary in stable order. Registration is
transactional, rejects duplicate identities and capacity overflow, and unwinds acquired resources on
failure; feature APIs stay typed and only complete service slots are erased for the pump. No assembly
scanning, filesystem convention, reflection autoloading, static-constructor discovery, or service
location. See [registration and composition](architecture.md).

## D3 - Native game access is a hexagonal main-thread boundary

Unity objects, reflected members, native registries, mutation permits, and postcondition checks stay
behind feature-owned main-thread adapters; everything crossing to a worker is a suite-owned neutral
value. Actions carry typed intent, and the adapter re-resolves and revalidates immediately before
mutating. See [action dispatch](architecture.md) and the [native boundary](service-cycle-runtime.md).

Identity is stable UUID plus expected native type. Diagnostic names never establish identity. Native
metadata and lifecycle-bound object bindings are cached separately.

## D4 - Common owns a small service-cycle runtime

Common owns the frame pump, typed runners, ownership handoffs, lifecycle and emergency control,
action rotation, clocks, and diagnostics/trace transport — not Auto Harvest pairs, game UUIDs,
economy policy, reflection failure scopes, or feature diagnostics meaning. Each of those is a
separate cohesive module; a facade accumulating every constructor and behavior is rejected even if
tested. See [Common module structure](architecture.md).

## D5 - Ordinary services use strict half-duplex cycles

`Waiting -> Capturing -> Evaluating -> Executing -> Waiting`, with lifecycle-scoped state, one
reusable response buffer, and one sleeping thread. There are two shapes and no third: the ordinary
one consumes the published world and has no capture, the source produces it and is the only shape
with one. Evaluation is synchronous and Unity-free, and the phases never overlap for one service.
Specialized future work does not complicate this default API without a measured requirement. See
[the two service shapes](architecture.md).

## D6 - Configuration, world, and strategy are next-cycle snapshots

Three publications, all registry-owned, immutable, latest-wins and generation-stamped. A cycle pins
one reading of each and names all three on its identity; the runtime hands the pinned snapshots to
the worker as arguments, so no service holds a publisher and the two halves of a cycle cannot
disagree about what the game looked like. Later publications never cancel or partially alter current
work. See [three publications](architecture.md).

## D7 - Batches are finite but not suite-capped

A service may return any finite number of actions; Common maintains one batch per service, reuses and
grows its storage, and imposes no gameplay count ceiling or truncation. Each frame gives every active
service one turn from a rotating start, bounded by its registered action limit, and every attempt is
independently validated. The first rejection or fault terminates the batch and no deferred retry
exists. See [action batches](service-cycle-runtime.md).

## D8 - Emergency stop is immediate Common control

Not saved configuration: while active, no native action call occurs, every unattempted action is
`Rejected(EmergencyStop)`, and no new cycle starts. A running evaluator may finish and its actions are
rejected without execution; clearing never resurrects a batch. See
[emergency stop](service-cycle-runtime.md).

## D9 - Service state is private and diagnostics are projections

`TState` may be mutable across cycles but belongs only to its worker; UI, traces, Common, and other
services see an immutable semantic projection published atomically with exact cycle identity.
Persistence beyond a lifecycle requires a separate versioned save-safe design. See
[service state](service-cycle-runtime.md).

## D10 - Lifecycle replacement orphans instead of preempting

Replacement terminates old native work by generation and creates factory-fresh state for the newest
safe generation while an already-running evaluator finishes in isolation and has its response
discarded. No `Thread.Abort`, checkpoints, mid-evaluation cancellation, or shared state transfer. Two
physical runner positions are the hard per-service bound. See
[lifecycle retirement](service-cycle-runtime.md).

## D11 - Faults recover without crash loops

Expected refusal is a normal decision or rejection; exceptions are caught at the capture, evaluator,
and action boundaries and isolated to the service. A failed evaluation publishes nothing, keeps its
worker alive, recreates working state safely, and retries through monotonic debounced backoff. Public
evidence is stable categories and counters, never raw exception data. See
[fault recovery](service-cycle-runtime.md).

## D12 - One semantic model with separately owned observation products

One causal service-cycle vocabulary feeds live diagnostics, the bounded ring, and disk capture. Full
trace, decision journal, and performance profile are separate products, each owning its lanes,
writer, format, controls, retention, status, and failure boundary; they share only format-neutral
transport and storage mechanics, never mutable buffers or backpressure. Unity never waits for
telemetry or I/O, and a drop marks a capture incomplete rather than altering gameplay. See
[observability](observability.md).

## D13 - Runtime UI is additive and purpose-built

Runtime diagnostics live on the dedicated Runtime page inside the Mods surface — not a fake plugin or
configuration file — and never read worker state or Unity objects. See
[diagnostics and UI](architecture.md).

## D14 - Structure and evidence are delivery requirements

Every checkpoint keeps dependency direction, narrow constructors, explicit ownership, focused tests,
and buildable code. Portable evidence is reported honestly and never promoted to real-reference,
interactive, package, or release approval.

Review is risk-based and bounded. A meaningful runtime milestone receives at most one independent
review by default; concrete findings are assessed and fixed or explicitly rejected once, and ordinary
tests plus runtime evidence then decide acceptance. Re-review is reserved for a newly discovered
gameplay-safety or correctness risk, not used as an open-ended search for more defensive behavior.

Developer-only observability has one containment invariant: it must not change gameplay behavior.
Inside that boundary it uses one direct path and may fail or disable itself visibly. It does not need
compatibility paths, automatic restart, speculative retries, or recovery layers merely to make a first
live run succeed.

## D15 - Adopted replacements delete obsolete paths

A new runtime may exist source-adjacent while it is uncomposed and tested. A service is cut over
atomically, and no selector, compatibility branch, dual execution, or fallback remains afterward. Git
history is the rollback boundary.

Do not advance a configuration schema merely to scrub a retired selector. Stop binding and displaying
the obsolete value and leave old serialized text inert unless supported data needs a real migration.

## D16 - The suite owns transcribed economy math, gated by an assembly hash

**Specified here.** Asking the game to recompute a derived value is not cheap, and the cost is not the
reflection. `StructureSO.GetNextCost()` chains four `ResourceCostList` transforms, each a LINQ
projection into a fresh list, and caches nothing. Measured against the shipped macOS build, evaluating
that chain from the same inputs in suite-owned code is roughly 11.5x faster per structure,
bit-identical for every entity in a real save, and allocates nothing where the game allocates about
six lists per call.

Base values are a different matter. `GetQuantity()` returns a field. `ValueModifierRecord.GetValue()`
is not ported so much as *reproduced*, because it is a branch before it is a computation:
`calculationDirty ? Calculate() : calculatedValue`. The suite answers it the way the game would and
never through the accessor, since `Calculate()` runs an allocating LINQ pass over both modifier
dictionaries and then writes four fields of game state — the game recomputing and re-stamping its own
observable at whatever point in the frame the suite's pump happens to run. An accessor that writes on
read is a mutation however innocuous the write looks, and the suite does not mutate game state outside
the action boundary.

So the reading is the memo rule, and the dirty flag decides it: a clean record reads as its
`calculatedValue`, a dirty one as `Adjust(baseValue)` over both modifier sets, computed and not
stored. Neither half is the rule alone. Taking the memo raw publishes the `[NonSerialized]` zero of a
record nothing has touched since load; folding unconditionally publishes a number for records the game
will never recompute, since a record with no modifiers is never dirtied and charges from its memo for
the rest of the session. Both were shipped alone and each cost a live failure in the opposite
direction — see [W5](world-collection-decisions.md). **The number to publish is the number the game
will act on**, which makes this an exact reading rather than a tolerated stale one.

What makes owning the derived math tolerable is the gate, not the speed. The four conditions in
[goals and invariants](goals-and-invariants.md) are load-bearing together, and the accepted cost is
that any game patch disables the whole suite until re-audited. The mitigation is `script/re-audit`
making re-auditing cheap, not softening the gate.

Porting proceeds one layer at a time, each proven before the next begins. Two world-collector
readings, `GetTrueRate()` and `IsAvailable()`, remain genuine game calls for exactly this reason:
stacking a second unverified transcription on an unverified first would leave no way to attribute a
differential failure to either.

## D17 - World collection is derived from the runtime type, never from the save record

**Specified here.** Every entity category has two shapes: the `ScriptableObject` it is at runtime, and
the `SaveDataBase<T>` record it serialises into. They are not the same set of fields, and the
difference is exactly the set of values this suite exists to read. A save record carries only what
must survive a restart, so everything the game recomputes on load is absent from it by construction.
Building a collector's field list from `SaveData`/`ApplyTo` therefore reads as a complete inventory
while omitting the entire cached layer. It did: the first pass over twenty-five categories was derived
that way and missed 165 scalars and 125 modifier records, including all six of `ResourceSO`'s rate
terms and the cached `ConsumableSO.quantity` that answers "how many do I have". The save record stays
useful as a shortlist of what the game considers *state*. It is a shortlist, not the list.

**Enumerate the runtime type.** Walk the declared instance members of the `ScriptableObject` itself.
Collect the value-typed ones and the `ValueModifierRecord`s. Justify each omission rather than each
inclusion — the default is that a cached number the game keeps is a number some service will want,
because the game keeps it for the same reason.

**Private is not a signal.** `ConsumableSO.quantity`, `ResourceSO.inLossMode`,
`StructureSO.currentBuildTime` and `DiscoveryTreeSO.totalDiscoveredCount` are all private, all cached,
and all the exact number a consumer needs. Visibility describes the game's encapsulation, not the
value's usefulness, and compiled field accessors do not care.

**A count is a fixed-size fact about a variable-size thing.** An immutable publication cannot carry
`List<RitualEffectInstance>`, and deferring the list is often right. It is never a reason to defer
`ritualInstances.Count`, which is what "is this ritual currently running" actually means. Defer the
elements; collect the cardinality, and any scalar the game itself derives from the collection. A
single-valued reference to another entity is likewise collectable: a `Guid` is a value type, so
`AlchemyTypeSO.selectedLevel` and every edge like it travels as an identity without needing a
container. Collecting entities but no edges leaves the snapshot holding numbers it cannot attribute.

**Do not sort members into runtime state and definition constants.** The tempting fourth rule is to
skip fields the game never writes. It was measured and rejected: classifying the 270 members remaining
after the first three rules by whether the declaring type assigns them put 186 in "runtime" and 84 in
"definition", and the definition bucket contained `HarvestElementSO.harvestRate` and
`AlchemyRecipeSO.cachedRequiredXp`, which are plainly caches written from other classes. A rule whose
failure mode is silently dropping a cached value is the rule this decision exists to replace.

So: every declared instance scalar and every `ValueModifierRecord` on a collected type is collected,
without asking what writes it. The reads are compiled delegates over fields; the price is a wider row,
and a wider row is the cheap side of this trade.

**A record that distributes is not a record that holds.** `OrderedMultiplierRecord` and
`MergingModifierRecord` derive from `ModifierRecord`, not from `ValueModifierRecord`, and have no
cached value — they are plumbing that pushes modifiers, transformed, into the member records handed to
them by `AddRecord`. The distributed effect therefore reaches the snapshot through those members,
which are already collected under the memo rule. What the distributor alone knows is its own total,
the `Adjust(100)` its tooltip prints as a percentage. `ModifierRecord.Adjust` is pure, so computing it
would not breach D16; it is deferred because it needs the two variable-size modifier dictionaries, not
because reading it would mutate. Until a named service wants that number, the row carries the
active-modifier count.
