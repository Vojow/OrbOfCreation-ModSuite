# Shared world collection — decisions and quirks

Running record for the work that turned per-service collection into one shared, published world
snapshot. Entries are numbered `W<n>` so they can be cited from code and from other documents.
Numbers are never reused and never removed.

Two kinds of entry live here:

- **Live** — the reasoning still governs current behaviour. Read these before changing what they
  describe.
- **Historical** — a problem that was found and fixed, or a ruling something later overturned. Kept
  to a line or two so a citation still resolves and so nobody re-derives a conclusion that was
  already reversed. Do not read them as descriptions of the code.

The current *rules* of world collection are in [`world-collection.md`](world-collection.md) and
[`service-data-flow.md`](service-data-flow.md); those are the authority wherever an entry here
disagrees. Suite-wide standing decisions are in [`decisions.md`](decisions.md). Counts, byte sizes,
codec versions and manifest totals quoted below were true when written and are not maintained.

## Index

Every entry keeps its heading below; this roster says only which are still binding.

**Live** — W1–W6, W8, W11, W16–W22, W24–W26, W28–W37, W39, W42–W46, W48–W62.

**Historical** — W7, W9, W10, W12, W13, W14, W15, W23, W27, W38, W40, W41, W47.

---


## W1 — The published snapshot is a fresh allocation per cycle, not a rotating buffer

**Live.** `GameWorldFrameDeriver.Build` allocates one row array per category and
`PublicationTable.Create` copies it into the published table. Nothing published is ever written
again.

Several workers read the snapshot on their own threads, so it has to be immutable in fact and not
merely by convention. Double or triple buffering would make "immutable" depend on a reader finishing
before the writer wraps — an invariant nobody can check locally, that fails silently and rarely. A
per-cycle allocation makes the guarantee structural: a consumer that pinned a snapshot holds an
object no producer has a reference to, at a cost of one allocation per category per cycle, on an
interval rather than per frame. The derive scratch is reused per buffer, so the copy into the
published table is the only allocation that survives the cycle.

## W2 — The frame carries raw samples; the collector cannot be the frame

**Live.** `GameWorldCycleFrame` holds one `WorldSampleBuffer<TSample, TRow>` per category.
`GameWorldCollector` fills it on the Unity thread; `GameWorldFrameDeriver` reads it on the worker.

A service frame crosses to a worker thread and the structural validator rejects delegate storage in
one. The collector is almost entirely delegates — an accessor compiled per member per category — so
it can never be a frame. Splitting the readings out is what makes collection registrable as an
ordinary service at all. **Rejected:** having capture call both `Collect` and `Build`, a much smaller
change that puts derivation back on the Unity thread, the single thing this design exists to prevent.

## W3 — Derivation is a separate object from the binder

**Live.** Deriving is `WorldRowDeriver<TSample, TRow>`, not a method on `WorldRowBinder`. A worker
holding the binder in order to derive would also hold `Read` and every compiled game accessor behind
it; splitting them hands the worker a type with no native surface to reach for. (The abstract
`WorldRowBinder.Deriver` property that let a binder *name* its deriver was deleted in W28 — it forced
a shared instance and nothing read it.)

## W4 — Publication is an action, and the action pipeline was widened to admit it

**Live.** The original decision was that the worker publishes directly and the service emits no
actions, because `ServiceActionResult.Committed` was valid only with a verified
`NativeMutationOutcome` behind it and routing publication through the pipeline would have meant
fabricating mutation evidence.

**What that missed.** The pipeline's safety property is *no action may claim a native mutation it
cannot prove*. What the code encoded was stronger and unintended — *every committed action is a
native mutation* — true only because pressing a button was the sole kind of action that existed when
it was written. The evidence type was too narrow, not the pipeline. So a result declares its
**effect**: a publication carries the channel and generation it published and contributes truthful
**zeroes** to `ServiceNativeCallTotals`, which keeps "how hard did we poke the game" meaning that for
the services that really do. The published count defaults to zero, so a publishing batch that forgets
to declare itself is refused rather than admitted.

**What this buys.** Publication happens on the main thread, at one point in the pump, and actions
dispatch *before* start decisions — so a snapshot the worker handed back is live before any consumer
decides what to do with it, in the same frame. A promote hook called outside `PumpFrame` would have
been a second path for worker output, outside budget accounting and tracing, and a frame later.

**Accepted consequence.** `DispatchActions` returns early under emergency stop, so emergency stop
also freezes the world generation and every world-bound service is gated on a world that has not
moved. Coherent, but a behaviour change agreed deliberately rather than discovered.

## W5 — A modifier record is read the way the game reads it, and the folded fraction is measured

**Live (standing decision D16).** `ValueModifierRecord.GetValue()` is
`calculationDirty ? Calculate() : calculatedValue`, and both halves are reproduced here without
calling it: the memo while the record is clean, `Adjust(baseValue)` over both modifier sets while it
is dirty. Calling the accessor is what recalculates and re-stamps observables — mutating game state
on the suite's schedule — and this is how that is avoided without giving up exactness.

**Neither half is the rule on its own, and the suite learned that twice.** Reading `calculatedValue`
raw published the `[NonSerialized]` zero of a record nothing had touched since load, which priced all
180 structures at nothing. Folding unconditionally published a number the game will never adopt: a
record with *no* modifiers is never dirtied, so the game keeps charging from its memo forever, and
"correcting" `StructureSO.passiveCostMod` from its memo of 0 to its base of 100 over-priced every
structure by 1.25 to the power of its owned levels. A clean record's memo is what the game will act
on however far a recomputation would drift from it.

**What it costs.** A live run measured 40–45% of `ValueModifierRecord`s dirty at the moment the suite
reads them; consumables were 100% and alchemy types 0%. That fraction is the share of reads that fold
— the rest are a flag test and a field load, which is why reading exactly is affordable at roughly
five thousand records a collection.

**Quirk.** "Dirty" and "never calculated" are the same flag, and the memo rule is why that stopped
being a problem: a never-calculated record the game *will* recompute is dirty, so it folds instead of
publishing the `default(BigDouble)` its memo deserialises to, while one the game will never recompute
keeps that zero because that zero is the price. `AutomataWorldCollectionCheck` proves the reading
against the game's own `GetValue()`, reconstructed from the cache, the flag and `Adjust(baseValue)`
rather than called, so nothing is written and no observer is re-stamped.

## W6 — Verification runs everything in one frame

**Live.** The differential shortcut runs every pass to completion inside the frame the key was
pressed in. Spreading work across ticks is wrong for a manual diagnostic twice over: each pass would
read a different frame's game state, and the run would be invisible to the person who triggered it.
The stall is the acknowledgement.

**Quirk.** Order matters, in one direction only. Both ported-math passes settle dirty flags on
purpose so the two sides compare the same inputs, and collection itself calls `GetTrueRate()`, which
recalculates. So the cache survey runs first and reports last; otherwise it measures the check rather
than the game.

## W7 — Withdrawn

**Historical.** Recorded a scheduling conflict during the migration, not a decision about the system.
The number is kept so later citations stay stable.

## W8 — A cost can name a resource that is not in `ResourceSO.All`

**Live.** `world.Resources` is built from `ResourceSO.All`, which does not contain element-owned
resources: a harvest element creates its `ResourceSO` at runtime and never registers it. A cost
naming one would find nothing and silently skip the candidate. `TryFindResource` therefore searches
`Resources` first and `HarvestResources` second. A resource in neither still fails the candidate,
deliberately — a missing row means the pass could not read that resource, and treating unreadable as
free is how you buy something unaffordable.

## W9 — The stub let `GetTrueQuantity()` disagree with its own quantity

**Historical.** `ResourceSO`'s stub carried a settable `trueQuantity` beside the `quantity` and
`quality` it is derived from, so fixtures asserted on numbers the game would never produce. The stub
computes it as the game does now. First instance of the stub-drift class; the rule is in W16.

## W10 — The upgrade stub stored the answers instead of deriving them

**Historical.** `UpgradeSO`'s stub carried `purchaseLevel`, `queuedPurchaseLevel` and `finite` as
independent settable fields where the game derives all three from `level`, `queuedLevels` and
`maxLevel`. Invisible while Auto Buy read through accessors; wrong the moment it read snapshot
fields. Same class as W9.

## W11 — A retired profile stage code is burned, never reassigned

**Live.** When a stage's work stops happening, its code leaves the stage-code table, the trace tool's
name table and the dashboard — and the number is left burned rather than given to the next stage that
needs one. Reusing it would make a trace recorded before the change decode as a stage that means
something entirely different, silently. The decoder drops the retired name and lets the fallback
answer "Stage NNNN", which is the honest thing for an artifact recorded before the pass went away.
Burned so far: Auto Buy's `CandidateLevelReads`, `CandidateAvailabilityAdmission`,
`CandidateCostInvoke` and `CandidateCostDecode`, the six main-thread stages W50 retired, and Auto
Harvest's capture-side 1003–1005.

**A stage follows its work rather than retiring when the work merely moves** (W41), and a stage whose
cost falls to zero stays measured — it is still where the time goes if the projection stops being
cheap, and a stage that vanishes takes its history with it.

The decoder compiles only under `SERVICE_CYCLE_PROFILE`, which `script/test` once defined for the
profile *tests* and not for the tool, so a retirement left the decoder naming constants that no
longer existed and the gate stayed green for three commits. The gate builds the tool with the
profiler symbol now.

## W12 — A contract can be sourced by the file that *ports* a member, not only by one that calls it

**Historical mechanism, live rule.** The bounded-level predicates stayed in the manifest after their
last reflective caller went away: a ported predicate depends on the original at least as hard as a
reflective call does, and if the game changes `HasFiniteLevels()` our copy is silently wrong with
nothing to catch it. That still holds. The manifest no longer records *which file* reads a member, so
the "sources means files that depend on this" quirk this entry recorded is gone with the coupling.

## W13 — Auto Buy's candidate accessors shrank to two methods

**Historical.** `CandidateAccessors` was cut to `CanPurchase()` and `GetPurchaseCost()` once the
snapshot answered the rest, because `HasCompleteContract` was *rejecting candidates* when accessors
nothing reads failed to bind. Both remaining accessors have since gone too (W34, W39) and the type no
longer exists.

## W14 — Auto Harvest has no collection phase to replace *(conclusion superseded)*

**Historical.** Auto Harvest was exempted from the migration on the finding that only one of its
eight facts — plot visibility — was already in the snapshot, and that forking one fact-reading
routine into two that must agree forever was a bad trade for one boolean.

The per-fact accounting was right; the conclusion was not. Two services were in scope precisely so
that no shared mechanism could be built around a single consumer's shape, and exempting one removed
that check while leaving the goal looking satisfied. The cost showed up immediately in W22. The
exemption was withdrawn; W37 is the migration, and the objection dissolved by *deleting* the second
caller's fact reading rather than duplicating the first's.

## W15 — Splitting a file costs three rounds of manifest surgery

**Historical.** Every contract used to list the source files that depend on it, so moving a type
between files broke the manifest even though nothing about the game changed. This migration was the
evidence filed against that coupling; `sources[]` has since been dropped, and the declared-literal
check is global — see the [native contract manifest](../testing/native-contracts.md).

## W16 — Two global counts were still making the game recalculate

**Live.** Auto Buy read the multi-buy multiplier and the bulk development level through
`GlobalVariables.GetMultiBuy()` and `Player.GetBulkDevelopment()`, then `IntVariable.AsInt()` — which
reaches `GetValue()` and so recalculates and re-stamps an observable when the record is dirty. That
is the write-on-read the collector exists to avoid (D16), surviving in a migrated service because the
two reads look like plain scalars rather than modifier records. They come from `world.IntVariables`
by identity now, read through `bind.ModifierRecord("value")` — the memo rule of W5, like every other
record — with the two uuids pinned in `data/known-entities.tsv`.

**The gap this closes.** Reading by uuid assumes the asset carrying that uuid *is* the one the
singletons return, and nothing offline can prove it — the stub asserts an association it defines
itself. `AutomataWorldCollectionCheck.CheckGlobalSingletons` calls both singletons in the running game
and compares, so the equivalence is checked where it can be. The failure mode if it ever breaks is
the safe one: the read falls back to 1, so the suite buys one level at a time.

**Third instance of the stub-drift class** (after W9 and W10), and the rule it settles: **a stub that
stores an answer beside the fields the game derives it from is a fixture that agrees with itself.**

## W17 — A file split blinded a completeness gate

**Live rule, fixed instance.** `EveryValueMemberOfACollectedTypeIsDeclared` walked the world
directory non-recursively, so moving the binders into `Categories/` cut its coverage from 32
collected types to 3 while `Assert.NotEmpty(collected)` stayed happily satisfied — green and blind
for four commits. Nothing was actually undeclared.

Fixed with `SearchOption.AllDirectories` **and** an exact count assertion, because the failure was
not the missing recursion: **a coverage check that can lose 90% of its coverage without failing is
not a coverage check.** `NotEmpty` cannot tell "everything is declared" from "almost nothing was
looked at". W26 and W48 are the same lesson one layer up.

## W18 — A snapshot's generation is the frame it was collected on, not the frame it was published

**Live.** `GameWorldCycleFrame.CollectedAtFrame` is stamped on the Unity thread during capture,
travels to the worker, and becomes the published `WorldGeneration`.

Derivation takes frames. Collection reads the game on frame 100, Auto Buy purchases on frame 101, and
the derived snapshot publishes on frame 102 — describing a world in which that purchase never
happened. A generation minted at publish time would be numerically newer than the action it is
missing, so a consumer asking "has the world moved past my last action" would be told yes and would
buy the thing again. Stamped at collection, the same snapshot answers 100, which is not newer than
101, and the consumer correctly waits. Generations stay monotonic — the publisher refuses anything
not strictly newer than the live one. The caller supplies the meaning; the publisher keeps ordering.

**Quirk.** The publisher seeds an empty world at generation 1, so a frame-stamped host must not stamp
frame 1. No real host can — Unity's frame counter is in the thousands before a save is playable — but
a test driving the pipeline by hand has to start its counter past 1.

## W19 — The frame counter is injected at collection, and threaded by the pump everywhere else

**Live.** The collection port takes the `Func<long>` the host pumps with, so the collector can stamp
the frame its readings were true on. Every other frame-identity need is served by the pump, which
passes `frameIdentity` into `ServiceCycleSlot.TryExecuteOne`. Collection's capture is the one place
where the frame is *data* — a property of the readings, carried to the worker. Everywhere else the
frame is scheduling state, which the runtime already owns and should not be told about from outside.
The first version injected the same delegate into Auto Buy's freshness guard as well; W22 removed
that consumer instead of adding a third.

## W20 — The gate is a start refusal, not a wait decision and not an unavailable capture

**Live.** A gated service never reaches `ShouldStart`. `ServiceCycleSlot.TryStartCycle` returns no
attempt while the world is behind, so the slot does not start this frame and tries again on the next.

**Why not at capture.** `ServiceCaptureResult.Unavailable` means *the game was not readable*, and it
drives fault backoff and feature status. Reporting "we deliberately have nothing to do" through it
would corrupt a health signal on a perfectly healthy runtime.

**Why not a `Wait` decision either.** That was the first version and wrong twice over. It made
freshness the *service's* answer, so every consumer would restate a rule that belongs to consuming a
shared snapshot at all; and a `Wait` carries a wake policy, which silently converted "start on the
first frame the world is fresh" into "re-check once per evaluation interval". Refusing at the slot
compares two numbers the runtime already holds, and still commits no cycle.

The current rule, including strictly-after comparison and arming at birth, is in
[`world-collection.md`](world-collection.md).

## W21 — On-demand collection is noted and deferred

**Live deferral.** Collection could wait until a service asks for a snapshot rather than running on a
fixed interval. It is not built because the interval is already faster than any consumer evaluates,
so the work it would save is currently cheap, and the decision that would justify it is a measured
one — how the suite feels in a real session — that has not been taken. `DemandPulledLiveView` is the
precedent for optimising against a guess here. **What would reopen it:** a measured full-pass
main-thread cost high enough that four passes a second is a visible frame cost, or a consumer whose
interval is much slower than the collection one.

**Rejected outright:** skipping a cycle when the world has not changed at all. There is no such thing
as an unchanged world in this game, so the check would never fire, and it would throttle a consumer
to the collection rate — a behaviour change disguised as an infrastructure setting.

## W22 — Freshness is a runtime rule, enforced by the slot

**Live.** "Do not act twice on one reading of the world" is a property of *consuming a shared
snapshot*, not of buying things. It is identical for every consumer, depends on nothing a feature
knows, and both numbers it compares are pump frames the runtime already holds. Only the generation
crosses the seam (`IServiceWorldGenerationSource`), never the world, so this stays a scheduling
question rather than a second way to read the snapshot.

**How it was got wrong first, and why that mattered.** The rule was originally written inside Auto
Buy — a guard class plus a branch in `AutoBuyService.ShouldStart`. The reason that looked acceptable
is W14: exempting Auto Harvest left exactly one consumer, and a rule with one consumer reads like
that consumer's business.

**Since.** The opt-in went too. `BindWorld` was removed outright — an opt-in every service takes is
not an opt-in — and the gate is unconditional, armed by activation and by committing a native
mutation. [`world-collection.md`](world-collection.md) states the current mechanism.

## W23 — Recording a world-bound service is faithful; replaying one is not, yet

**Historical.** The gate was reachable only from `ServiceRegistration.BindWorld`, and Auto Harvest
registered through the replay path, which had no `BindWorld` at all — the rule built "for every
consumer" was unreachable from the second one. Replay itself has since been retired, so the open half
of this entry — reconstructing a world generation during replay — cannot arise.

## W24 — One upgrade action is several queue slots, and the reserve was only checking for one

**Live.** `AutoBuyConfiguration.LeaveQueueSlots` keeps slots free for the operator. Rejecting once
`remainingRoom <= reservedSlots` is correct for one slot per action, which structures are —
`StructureSO.Purchase(forceOne: true)` queues exactly one. It is wrong for upgrades:
`UpgradeSO.Purchase()` loops the multi-buy multiplier and `QueueAction(times)` stacks once **per
committed level**, and unlike the structure path the upgrade loop never consults `GetRemainingRoom()`
at all. Room 5, reserve 3, request 4: the check passed, four levels queued, one slot left. Found by
decompiling rather than by reading our own code. The adapter clamps the request to
`remainingRoom - reservedSlots` before submitting — same live read, same place, no new native call.

**Two things worth carrying forward.** `CanPurchase()` is `HasMetLevelRequirements() &&
ActionManager.CanLoadAction(this)` — a *room and requirements* gate, not an affordability one;
affordability lives inside `Purchase()` as a per-level `HasEnough()` that breaks the loop. And
`PerformCost()` runs per level at queue time, so resources are spent when a purchase is queued, not
when it completes — which makes the resource reserve floor a live problem too (W25).

## W25 — The resource reserve floor is charged per batch, in the worker, not re-read live per action

**Live.** Eligibility asks whether a candidate clears `cost + max(absoluteReserve, cost ×
relativeMultiplier)` against the snapshot's quantities. Many actions then run in one frame, each
planned against those same untouched numbers, and since `PerformCost()` runs at queue time (W24) the
batch really does spend as it goes — collectively straight through a floor every action individually
respected. The worker keeps a per-cycle ledger of what the batch has committed per resource and
charges each purchase against it as it plans; a purchase that no longer fits is skipped rather than
ending the batch, so a candidate drawing on a different resource still gets its turn.

**Why not read the live game inside the action**, which is what the queue reserve does. The queue
reserve can: it is one cheap int. A live floor check needs each cost resource's true quantity, read
as raw fields because `GetTrueQuantity()` goes through `GetValue()` and writes (D16) — so an
in-action version would be a second implementation of what the collector already does, that must
agree with the first forever. The gate (W22) is what makes the ledger sufficient rather than a
half-measure: the only spending the snapshot can be missing during a batch is the batch's own.

**Known lower bound.** A multi-level request is charged `levels × next-cost`, and each level actually
costs more than the last, so the curve can let a multi-level buy dip into the reserve; the game's own
per-level `HasEnough()` still prevents going negative. Charging it exactly needs `GameCostMath` wired
in, pending the live differential run.

## W26 — Common was outside the native-contract audit, and the world was about to move into it

**Live rule, fixed instance.** `src/Common` was not one of the roots the reflection audit walked, so
the audit never visited two game-facing reflection sites already living there. Their contracts *were*
declared; what was missing is the thing that keeps them declared as the code changes. An unwalked
directory does not fail — it reports nothing, which is indistinguishable from having nothing to
report. Same failure class as W17, one layer up. And it was about to get worse: the next step moved
34 reflection-heavy world files into exactly that directory, which would have quietly stopped the
audit covering the entire world collector while staying green.

**The rule, now the practice.** A source root is not something to add when a directory starts
reflecting; every source directory should be one from the start, because the cost of a missing one is
silence. `sourceAudit.roots` lists every feature directory.

## W27 — The world moved to Common in two commits, and the second one caught the first's blind spot

**Historical.** World state and its collection machinery moved to `src/Common/Runtime/World`, renamed
`Automata*` → `Game*`, because Common is the suite's shared game-facing library and a non-Automata
service can now have game state. The move and the rename were split on purpose — mixing a relocation
with a rename makes the diff unreadable — and the split exposed that the rename sweep had missed
`data/`, where three contract source paths kept the old filenames. The manifest no longer names
source files at all (W12), so that tripwire is gone with the coupling it guarded.

## W28 — The rate chain moved into the deriver, and the terms that belong to no resource moved onto the frame

**Live.** Every published `WorldResource` carries a `TrueRate` computed by `GameResourceRateMath` on
the worker — the first number the suite derives for itself rather than asking the game for, and the
thesis of the design made concrete: one computation per resource per cycle, off the Unity thread,
with no `GetValue()` call and therefore no write.

**The frame-wide terms needed somewhere to live.** Six inputs belong to no single entity, and all six
are main-thread-only: five player globals — resource overflow, overflow loss, reset time passed,
structure cost percent, attribute quality bonus — plus Unity's fixed delta time. They are read once
per collection into a `WorldFrameGlobals` struct stamped onto `GameWorldCycleFrame` alongside the
samples, which forced the resource derivers from static shared singletons into per-cycle instances
taking the globals in their constructor: a shared instance holding mutable globals would let the next
collection rewrite the terms a worker was still deriving against. The five player globals are
`ValueModifierRecord`s like any other and are read under the memo rule (W5), so adding one is a
binding and a field rather than a mechanism — `GetAttributeQualityBonus()` was added exactly that way.

**Binding failure degrades per term rather than uniformly, and never throws**, so an assembly that
renamed one player accessor does not fail the whole collection over five scalars. Neutral is not zero
everywhere: zero is right for the additive rate terms and wrong for `structureCost`, which multiplies
— a zeroed one prices every structure at nothing — so that degrades to its identity of one.
`attributeQualityBonus` is an exponent, and the exponent that leaves its base alone is zero, not one:
`Pow(quality, 0)` is a divisor of one, where `Pow(quality, 1)` would divide the price by the quality.

**The `*HasActive` flags are modifier counts, not zero-checks.** The chain branches on whether a term
*participates* — the game's `HasActiveElements()` — not on whether it contributes zero, and those
differ for a modifier stack that happens to cancel out.

**The fixture rule this established, restated because it recurred twice more (W31, W34): a fixture
built from a type's defaults tests almost nothing about a multiplicative chain, because the defaults
are the identity.** The first rate fixture left seven terms at zero on both sides of the assertion,
so deleting three of them kept every test green. Pinning the mapping needed every term distinct,
non-zero, and exercised on both sides of the capacity line. Five of the six activity flags remain
individually unobservable — `HasActiveRate` ORs them into one conjunction, so only their aggregate is
testable and a per-flag assertion would be theatre.

## W29 — The global modifier registry became a category, so entities can carry a modifier identity

**Live.** `ValueModifierVariable.All` is collected as its own category. Each row carries the
modifier's type as an integer, its `adjustReal` magnitude, and its stack order — the whole of the
game's `ValueModifier` arithmetic, and exactly what `GameValueModifier` is ported from.

**Why a registry rather than a copy per entity.** A structure's `costPerQuantity` is a
`ValueModifierRef`: it points at one of these rather than holding one, and other entity kinds do the
same. Collecting the registry once lets an entity row carry a Guid the deriver resolves, instead of
every referring entity duplicating a value that is shared by construction — and instead of calling
`GetMod()` per entity, which boxes the returned struct once per entity per pass.

**`ValueModifierVariable.GetValue()` is a plain field read**, unlike almost every other `GetValue()`
in this game: no dirty flag, no recalculation, no observable. The binder still reads the field
directly, because the rule that collection never calls an accessor is worth more than the one call it
would save, and the next `GetValue()` someone reaches for will not be this one.

**The enum needed a new binding shape.** `value` is an inline `ValueModifier` struct, so its `type` is
an enum *inside a field*. `NestedEnumField` composes the two bindings and both of their checks,
including that the underlying type is exactly `int` — a widened enum stops binding rather than
silently changing what every comparison means.

## W30 — The portable gate's waits are spin-based with a wall-clock budget

**Live, still open.** A family of test waits use `SpinWait.SpinUntil(condition, TimeSpan.FromSeconds(n))`.
Spinning burns the core the background worker needs, so the wait actively competes with the thing it
is waiting for, and a few seconds of wall clock is a guess that holds only on an idle machine. This
is what made the gate fail on a first attempt and pass on its retry while the machine was building
four projects. W33 fixed one distinct sub-family and W52 unified the ServiceCycle deadlines; the
`SpinWait.SpinUntil` sites are still unconverted. Recorded so the retry in `script/test` does not
quietly become acceptable.

## W31 — Structures are priced in the snapshot, which needed one more piece of math than the port had

**Live.** `GameWorldState.PurchaseCosts` publishes what one more level of each structure costs, in
each resource, computed on the worker.

**The port was incomplete and it did not look it.** `GameCostMath.ComputeNextCost` took
`nextCostModPercent` as a *parameter*, and the verifier supplied it by reading `GetNextCostMod()`
from the game — reasonable for a verifier, but it meant the chain could never run without the game.
`GetNextCostMod` is ported now:

```
Max(passiveCostMod, 100 / costPerQuantity.GetMod().MultiplyScalar(GetNextQuantity()).Adjust(1))
    * (activeCostMod * Player.GetStructureCost().AsPercent()).AsPercent()
```

The *ported* value feeds the end-to-end cost comparison, so the live differential run verifies what
actually ships rather than a chain propped up by the game's answer to the hard part.
**`GetNextQuantity()` and the committed quantity are the same number**, which their
names hide: `GetBaseLevel() + queuedQuantity` versus `quantity + queuedQuantity`, and
`GetBaseLevel()` returns `quantity`. Stated here because a future reader will assume a bug.

**A cost is the one published fact that is not one row per entity**, so `WorldTable`'s duplicate-
identity rejection and the identity walk cannot serve it. Purchase costs get their own buffer, a
second walk of the structure registry, a table sorted by (entity, resource), a range lookup, and a
*named* exemption in the identity-walk test. **Scratch cannot live on the frame:** the chain needs a
`BigDouble[]`, and the structural validator rejects an array of a game value type inside a service
frame — so the buffer holds readings only and the scratch moved to the per-cycle deriver.

**A zero attribute-cost modifier withholds the price rather than zeroing it.** `attributeCostMod` is
authored at parity, so a zero reaching the chain is a reading the chain cannot price honestly however
it arose — and multiplying by it would make the entity free, the one error direction that makes Auto
Buy commit to a purchase it cannot pay for. A zero quality is the same refusal from the other side:
it is the base of the power the modifier is divided by, so it publishes an infinity. Either way the
entity publishes no price at all, and a consumer that finds none falls back. The memo rule (W5)
removed the reading that used to *manufacture* such a zero — a never-calculated record the game will
recompute now folds rather than reading as its deserialised zero — but it does not make the guard
unnecessary, because a zero the game itself holds is still a zero this chain must not multiply by.

## W32 — Upgrades are priced too, on a chain that shares nothing with the structure one

**Live.** `PurchaseCosts` carries upgrades as well, so the table answers "what does one more level of
this cost" for every candidate. The port is `UpgradeSO.GetLeveledCostList()`:

```
int num = Math.Min(level + queuedLevels, HasFiniteLevels() ? maxLevel - 1 : int.MaxValue);
resourceCost.SetToLevel(resourceCostModPerLevel, num + 1).RoundToTwoSigs()
// SetToLevel(list, n) => n == 1 ? costs : list.MultiplyScalar(n - 1).Adjust(cost)
```

**The two chains share their inputs' shape and nothing else**, which is why the deriver dispatches on
which registry an identity belongs to rather than parameterising one chain. A structure multiplies by
a per-resource attribute modifier, a global per-quantity modifier, a cost-scaling modifier and a
frame-wide structure-cost multiplier, ending with `RoundToTwoSigsEarly`; an upgrade multiplies by a
modifier *list* scaled to its level and ends with `RoundToTwoSigs`. The rounding difference is not
cosmetic: `…Early` only alters values in `[10, 100)`, so an upgrade's price is snapped at every
magnitude and a structure's usually is not. **The attribute modifier is a structure-only
precondition** — the upgrade chain never touches it, so W31's withheld price would suppress upgrade
prices over a reading that says nothing about them.

**A modifier list is read by content; a single modifier is read by identity.** `costPerQuantity`
travels as a Guid into the modifier registry. `resourceCostModPerLevel` is a `ModifierListRef` whose
`Standard` subclass resolves one of nine shared lists off `GlobalValues.instance` by a `refType` enum
rather than naming a variable, so carrying an identity would mean reproducing that nine-way mapping.
**Exponents are a second list, not more modifiers:** `ValueModifierList.Adjust` merges the modifiers
into their order groups *first*, then lets each exponent group act on the merged result, and
exponentiating each modifier before merging gives a different answer whenever two share an order.

**Two readers append to one buffer, so neither resets it.** The structure and upgrade cost readers
both write into `frame.PurchaseCosts`; whichever ran second would have discarded the other's rows,
and which that is depends on traversal order. The collector resets it once, before either runs.

## W33 — The gate's retry was hiding a busy-wait that starved the thread it was waiting for

**Live rule, fixed instance.** Forty-six waits across six pump test files were written as
`while (pump.PumpFrame(frame++).CapturesAttempted == 0) { }`. `PumpFrame` runs on the calling thread,
so the loop holds a core at full tilt against the worker whose progress it is waiting for.
Instrumented on a machine loaded to eight busy cores, the wait for the capture after a faulted batch
took **86 to 1908 frames**; with a `Thread.Yield()` in the loop body it took **2**, every time. That
is not merely wasteful — every pumped frame emits semantic trace events into the fixture's 256-entry
ring, so a spinning wait overruns the ring and a test asserting the *absence* of an event can no
longer prove its drain was complete. To reproduce, run the single test in isolation under
`nproc - 2` busy-loop processes: alongside its neighbours it passes, because xunit's own thread churn
supplies the yield the loop does not.

**A wall-clock deadline is a hang detector or it is a bug.** Sizing one to how long the work takes
today makes every future slowdown look like a broken feature. One test allowed a background writer
500ms for a commit that takes about a second on a cold process, so it failed *deterministically* when
run alone and passed only alongside neighbours that had warmed the runtime up. Its deadline is a
named thirty-second hang detector now; nothing there asserts a latency, so a longer ceiling weakens
no assertion. W52 applied the same treatment across the ServiceCycle tests.

## W34 — Auto Buy takes the published price, and the native cost path is deleted rather than left idle

**Live.** Auto Buy looks a candidate's price up in `world.PurchaseCosts`. The game's
`GetPurchaseCost()` rebuilds the whole cost list on every ask — six LINQ projections and six
allocations, no cache — and Auto Buy asked four hundred times a cycle; the collection pass computes
the same number once per entity, on the worker.

**The now-unused pieces go in the same commit** — the native cost adapter, its binding, its
return-type guard, its unit test and the `upgrade.get-purchase-cost` contract. Leaving a native
adapter in place with live declared contracts and no caller is how a contract manifest starts
describing a build nobody exercises. `structure.get-purchase-cost` stays because the cost verifier
still calls it: comparing the port against the game *should* keep asking.

> **Correction.** `upgrade.get-purchase-cost` is declared again, at place `action`, and so is
> `structure.get-purchase-cost`'s diagnostics ownership. Neither is on the pricing path — that is
> still the published table — but Auto Buy's refusal diagnosis asks the game why it said no, and one
> of the terms it names is `GetPurchaseCost().HasEnough()`. The rule this entry states is unchanged:
> a contract exists because a caller exercises it, and these have one again.

**Two stub defaults were wrong and this is what surfaced it.** `ResourceSO.attributeCostMod`
defaulted to zero where the game authors parity, and `Player` had no `GetStructureCost()` at all, so
the frame-globals reader silently degraded on every stub-backed test. The W28/W31 lesson in a third
costume: **a stub whose defaults are not the game's defaults is a fixture that agrees with itself**,
and it stays agreeable until something reads the field.

**Every fixture that expects a candidate has to describe a price**, since a candidate with no
published price is skipped. Plumbing tests name costs at parity deliberately; the collector tests
cover the arithmetic away from parity, where a multiplicative chain can be observed.

## W35 — A plot node's quantities are summed during collection, because asking the game would write

**Live.** `WorldPlotNode` splits into a reading carrying `IdleQuantity` and `TotalQuantity` and a row
carrying the `RemainingQuantity` derived from them.

**The accessors are unusable, and that is the interesting part.** `PlotNodeSO.GetQuantity()` and
`GetTotalQuantity()` both go through `GetPhaseInstance(phase)`, which lazily builds a dictionary
cache *and creates a missing instance* on the way past. Calling either is a write to game state from
a pass whose entire contract is that it does not write — the same trap as D16's `GetValue()`, worth
stating twice because "there is an accessor for that" keeps looking like the safe option.

**The total sums authored phases, not instances.** The game's `GetTotalQuantity()` iterates
`phaseInfos` — the phases the node *declares* — and asks for each one's instance. Summing the
instance list would count an instance for a phase the node does not author, putting the suite's total
out of step with every calculation the game bases on its own. The collector test uses three distinct
per-phase counts for exactly this reason: with equal counts, a reader that ignores an instance's
phase agrees with one that does not.

**`GetRemainingQuantity()` is not symmetric in its two usage terms.**

```
GetQuantity() - actionQuantityUsageMain.AsInt()
              - Math.Max(actionQuantityUsageAny.AsInt() - GetOtherQuantity(Idle), 0)
```

The *main* usage comes straight off the idle count; the *any* usage is absorbed first by whatever is
busy growing or resting and only reaches the idle count once that runs out. It is also allowed to go
negative and is left that way: the game's callers compare it against zero rather than clamping.

## W36 — Plot-and-action pairs are their own table, and one of the two costs could not be ported

**Live.** `PlotActions` publishes one row per plot-and-action pair: how many times the plot offers the
action, how many instances it is running, whether prerequisites are met, what one run costs the plot,
and how many more runs would fit.

**A pair is not an entity.** Neither side owns it, it has no identity of its own, and every term in
"may this action be started on this plot" is a function of both. So it is its own table, keyed by the
composite and searched by `WorldPlotActionLookup` — the one place `WorldLookup` cannot serve, because
that keys on a single identity — and exempt from the identity walk for a stronger reason than
purchase costs: neither guid on the row is the row's own. **Both sides are counted, not flagged**: a
plot can name the same action twice in `availableActions` and can hold an instance of an action it no
longer offers, so a flag loses the first and a table keyed on either side alone loses the second.

**`Prerequisites.Container.Check()` is a write.** It stamps `gameId` and, on success, sets
`available` and leaves it set — a sticky latch, so the field carries the same answer without the
write. Third accessor in this log that looks like a question and is not (D16's `GetValue()`, W35's
`GetPhaseInstance()`). Reading the latch errs one way only: a prerequisite that became satisfiable
since the game last asked reads as unmet, which is the right direction for a gate.

**One branch of `GetElementCost` is not ported, and fails closed.**

```
!useSizeModForCost || elementCost <= 0
    ? elementCost
    : Math.Max(BigDouble.Floor(elementCost * GetGivenCostIncrease() / givenElement.sizeMod.AsPercent()).ToInt(), 1)
```

`GetGivenCostIncrease()` is a product over `sizeModNodes` this suite has not ported and does not
collect. When the list is empty the product is one and the port is exact; when it is not, the row
publishes `ElementCostKnown = false` rather than the unscaled cost, which would be too cheap in the
one direction that makes a consumer start a run the plot cannot pay for. Whether that branch is live
for the two harvest pairs could not be checked offline, so it stands as a known limit.

**An instance's reference is read without resolving it.** `IdObjectRef` holds a serialized string and
a `_guid` that `GetGuid()` memoises the parse into. Straight off a save load nothing has asked yet,
so a reader trusting the memoised field alone would see a plot with no running actions at exactly the
moment a consumer most wants to know what is running. Collection reads both fields and parses without
writing back. **`GetMaximumRemInstances()` branches on the *authored* `elementCost`** rather than the
scaled one — the two agree because the scaling branch floors at one, but the game's own condition is
the one kept. Its answer may go negative, and this port deliberately differs: "how many more runs
fit" has no reading below none, so it is clamped.

## W37 — Auto Harvest decides from the snapshot

**Live, with one superseded part.** Auto Harvest's facts come from the world snapshot through
`AutoHarvestWorldFacts`, a pure function of an immutable `GameWorldState`. The `ReadFacts` routine
this entry described has gone entirely: what the action boundary genuinely cannot be handed — the
live instance it submits into — is `ReadPrototype`, and the six live reads it took to re-ask the
decision come off the snapshot (W54). One of those six reached `Prerequisites.Container.Check()` and
*wrote*, so re-checking was mutating.

**The split is between deciding and acting, not between old and new.** `ReadFacts` was on the capture
port and the submission port at once and the two callers wanted different things; they moved apart
rather than being merged, because the spine already said deciding uses snapshots and acting re-checks
reality.

**One pin per cycle, not one per pair.** Both pairs are judged against the same snapshot. Pinning
per-pair would compile and mostly agree with itself, and would mean the fruit pair and the treasure
pair were decided against different worlds — the exact failure the pin rule exists to prevent, and
one nothing downstream could detect. There is a test that counts pins.

**"Exactly one instance" survives the migration as a fact, not an accident.** The old reader treated
prerequisites and readiness as *unknown* when it could not find exactly one runtime instance, because
it had no prototype to ask. The snapshot answers prerequisites without an instance, so the gate was
re-stated deliberately: the action boundary submits into that instance, so a pair without exactly one
is not *decidable* rather than not ready. The pair table's `InstanceCount` carries it.

**`ElementCostKnown` cannot be isolated through the collector, and is kept anyway.** An unknown cost
publishes zero, and zero is not one, so the cost comparison already rejects it — deleting the guard
breaks no test that goes through collection. That is exactly the trap W36 describes: a later deriver
publishing the *unscaled* cost would make the pair look ready at a price the plot cannot pay.
`AutoHarvestWorldFacts.IsReady` is therefore tested directly against hand-built rows.

## W38 — Auto Harvest's capture stops reading the game, and the queue moves to the action boundary

**Historical, corrected by W53.** The two facts describing the live action queue stopped being
decision inputs and are evaluated once, at the action boundary, on top of the facts the worker
judged. That part stands: the boundary already re-read the queue and re-evaluated the policy before
mutating, so the capture-time copy only decided whether to bother asking, and the rejection reasons
now mean the narrower and truer thing — "the service decided to act and the boundary refused". What
this entry got wrong was ruling the queue *unpublishable*; one ruling was doing two jobs, and W53
separates them.

## W39 — Auto Buy's capture stops asking about the queue too, and the plan stops guessing at it

**Live.** `CanPurchase()` and the action queue's capacity and free room left Auto Buy's capture. The
purchase adapter already re-resolved the candidate and re-checked admission before mutating, and the
action adapter already re-read the live room and clamped to it, so both were pre-filters over an
answer that had to be taken again anyway.

**The plan is no longer bounded by a queue estimate.** The worker ranks every eligible candidate and
emits one action each; the boundary re-reads the room before every submission and the first one that
does not fit cascade-terminates the batch. `FillAvailableQueue` means "keep going until only
`LeaveQueueSlots` remain", and only the boundary can know when that is — the worker's copy was a
guess that aged between the pass that took it and the action that used it. The batch is still bounded
at runtime, by the truth rather than by a number carried across a thread hop.

**Three native contracts were deleted, not orphaned**, on the rule cited from later entries: **an
audited member nobody reads is an audit of nothing.**

## W40 — The action's audited safety is a boundary check, not a decision input

**Historical, superseded by W54 on where the check lives.** The safety verdict moved out of the
published facts into `AutoHarvestPolicy.EvaluateSubmission`, read from the resolved binding at the
moment of submission, on the grounds that the audit was a whole-graph structural equality check that
reduces to no row. **Its rule survives and W54 keeps it:** publishing a *summary* of a whole-graph
check would be publishing a verdict the deriver cannot justify. What changed is that the *terms*
turned out to be publishable, so nothing has to be summarised.

Two sub-decisions still hold. Safety is checked *before* the queue: an unsafe action is unsafe
whether or not there is room, and a rejection naming the full queue reads as "try again later" for
something that must never be submitted. And a codec version moves with the record rather than the
layout being widened for compatibility, because an older recording decodes to evidence the current
policy would not have decided from.

## W41 — Auto Harvest's capture makes no native call at all

**Historical.** Binding resolution left capture — a pre-filter, since the action adapter already
resolves the pair it is about to mutate, re-checks its lifecycle and re-checks the quarantine, and
capture's copy added only two compile-time uuids. Capture became a pure function of configuration,
the world and this service's own memory, and could no longer fail. Capture as a phase has since been
deleted outright (W52).

Two rulings from it are cited elsewhere: a profile stage **moves with its work** rather than retiring
when the work merely relocates (W11), and `RegistryNotReady` is read off the snapshot because a world
holding no plot nodes at all has not been collected yet — a different thing from a world that holds
plots but not this pair. Collapsing the two would report a loading game when an asset is missing.

## W42 — Authored effects become a world category, and the property travels as its name

**Historical; retired with the configurable structure-priority heuristic.** `EntityEffects` published, per structure, what its purchase does to another entity's named
property: the two identities, the property's name, and what the modifier does to a value of one. It
is a classification of the build's authored content, which is a fact about the game and therefore the
snapshot's job; the ranking that reads it stays Automata policy (W43).

**The property travels as a name, which departs from the rule that an enum travels as its integer.**
A resource effect names a member of `ResourceSO.ModifiableType`; an upgradeable-object effect carries
an authored string from its target type's own property record. The integer of the first is not a
representation the second has, and the name is what both mean. The enum's members are read once at
bind time by `Enum.GetNames`/`GetValues`, so a build that renumbers the enum cannot silently rename
an effect.

**`Adjust(1)` is computed in the deriver, not published as type-and-amount.** The direction of an
effect is what a consumer asks about, and the arithmetic that answers it is the game's — the
already-ported `GameValueModifier.Adjust`. A modifier whose type the port does not model publishes
`RatioKnown = false` rather than a plausible one, for the same reason a price is withheld (W31).
**Effects that resolve their target at apply time are dropped:** `useTargetRef` means the modified
object is decided from whatever applies the effect, so the object it names is not the edge.


## W43 — Auto Buy classifies economic priority from the snapshot

**Historical; retired.** Auto Buy now ranks by cost ratio and stable UUID. The configuration key,
policy, world category, native reads, and tests for the structure-effect priority tier were deleted
together.

**Two paths became one rule per target kind, behaviour-preserving on this build.** The classifier
asked different questions of a resource effect and an object effect; the table has one column for the
property, so the policy asks one question of each row and lets the target's own table decide which
vocabulary applies. Every name still reaches the path that already accepted it, and the
`AttributeCostMod` branch W42 recorded as dead was dead *on the object path* only — the merge
dissolves the distinction rather than carrying the dead branch forward.

**The snapshot is now the authority on what a target is.** The classifier asked the live object for
its base types; the policy asks which published table holds the identity. A target the collector
never published is worth nothing rather than classified — a snapshot that does not describe an entity
is not evidence about it. **No per-lifecycle cache:** the classification is a binary search now,
taken fresh each cycle against the snapshot the frame is being built from, because caching it would
carry a value across world generations to save a lookup.

**A guard no test can fail, kept deliberately.** Removing the `RatioKnown` check is invisible — an
unmodelled modifier publishes a ratio of one, which is neither above nor below one — but the policy
would otherwise be comparing a number its own table documents as meaningless.

## W44 — Auto Buy's candidates are the snapshot's, and its capture stops reading the game

**Live.** The reader walks `world.Structures` and `world.Upgrades` instead of scanning
`StructureSO.All` and `UpgradeSO.All`. The per-candidate stable-id read, the per-lifecycle metadata
cache keyed on object reference, and the exact-type guard go with the scans.

**The exact-type guard moves to the action boundary, where a copy of it already ran.**
`PurchaseAccessors.TryCreate` refuses any object whose type is not exactly the audited `StructureSO`
or `UpgradeSO` from `Assembly-CSharp`, immediately before mutating. Running the same check at capture
made capture a pre-filter over an answer the boundary takes again — the shape W41 removed from Auto
Harvest.

**One source of identity instead of two.** The reader used to take a candidate's stable id off the
live object and look its facts up in the snapshot by that id, so the two could name different
entities. That disagreement is now unrepresentable, and the test that proved the mismatched candidate
was dropped is replaced by one pinning capture's candidates *as* the snapshot's population — a claim
that can only be asserted against a world deliberately built to disagree with the stub registries.

**Candidate order changed from registry order to identity order, and nothing depends on it.** The
evaluator sorts by priority, then cost ratio, then uuid — a total order — so capture order is not an
input to any decision, and identity order is stable across runs where the registry's was not.

## W45 — Auto Harvest remembers its own faults instead of reading them at capture

**Live.** The quarantine and the contract circuit stay where they are written — the action boundary —
and what they mean for a decision reaches the worker as a result code recorded in
`AutoHarvestCycleState.Faults`.

**A code per failure, because the receipt is the worker's only view.** A single `AdapterFault`
covered a quarantined pair, a refused binding and an unverified mutation alike — enough for a human
reading a log, not enough for a decision. `PairContractUnavailable`, `FeatureContractUnavailable` and
`PairFaulted` replace it: how far a failure reaches decides whether one pair or the whole feature
stops being tried, and what kind it is decides what the health report says.

**A scope is only claimed when the circuit was actually tripped.** The resolver blocks the circuit as
it fails, so the scope is readable immediately after a refused resolution; a resolution that failed
without tripping it stays an unattributed adapter fault rather than being reported as a contract the
build does not have. **The memory clears with the lifecycle for free**, because state is created per
lifecycle.

**One guard removed as provably redundant, one kept and pinned.** A feature-fault check in the action
decision could not change an outcome, because the fault memory already answers a feature fault for
both pairs. The sibling check is *not* redundant — it is what stops an eligible pair when the other
reported something feature-wide — and now has a test that fails without it.

## W46 — Action-family ownership is checked where the action happens

**Live.** Neither frame carries whether this instance owns the action family it would act on; both
action adapters re-read the lease before mutating. Same shape as W39, W41 and W44: a lease another
plugin can take mid-cycle is a property of the live runtime, so an answer carried through a decision
is a pre-filter over one that has to be taken again.

**Auto Buy was not enforcing it at all.** Its ownership mask was captured every cycle and read by
nothing, so a purchase went through whether or not the suite held the structure or upgrade lease. The
check runs at the boundary now, per kind, because holding the structure lease says nothing about
upgrades, and declines with `ActionFamilyUnavailable` — a rejection rather than a fault, since
standing down for a plugin that owns the family is the arbitration working and the lease can come
back. An emergency-active flag went with it as a duplicate of the safety configuration read.

## W47 — What deleting the capture phase actually costs, and the shape it should take

**Historical.** A survey filed before any of the work, once both ordinary services captured nothing:
seventeen files named the capture result or context, ninety-nine mentioned capture at all, fifty-two
test files touched the surface. It ruled out the tempting shape — making `TFrame` the three
publications and having `Evaluate` read them — because that would have made replay of a world-bound
service structurally impossible rather than merely missing, and set the split in the order each piece
unblocks the next. W49, W50, W51 and W52 are those four slices.

## W48 — Only the suite-wide half of configuration belongs in Common

**Live.** A configuration record belongs in Common when a non-Automata service would consult the same
one. Four do: is the suite enabled, is the emergency stop pulled, what is the frame budget, and how
much does a service say about its decisions. They are `SuiteGeneralConfiguration`,
`SuiteSafetyConfiguration`, `SuitePerformanceConfiguration` and `SuiteDiagnosticsConfiguration` —
`Suite*` rather than `Game*`, because they describe the mod suite rather than the game.

**What stayed, and why moving it would be worse.** The feature sections name Automata's features, as
do the replay and reserve records. Moving those would make Common know what Auto Buy is, which is the
coupling the split exists to prevent. **The rename is not cosmetic:** `AutomataSafetyConfiguration`
in Common would read as "Automata's safety settings, which other plugins happen to use", and the next
service wanting an emergency stop would reasonably write its own rather than depend on a rival
feature's record.

**The manifest walk was re-verified, per W17.** Planting an `AccessTools.Method` literal in the new
directory makes the reflection audit fail naming that file, proving the walk reaches it. Nothing that
reflects moved, so no contract changed; the check was to prove the gate *would* have noticed.

## W49 — The runtime stamps the strategy generation, and pins it before capture runs

**Live.** The capture result no longer carries a strategy generation.

**It was a lie in every service.** All three returned `new StrategyGeneration(1)` hardcoded, which was
numerically right only because no service publishes a bulletin yet — the moment the strategist lands,
every cycle would have gone on claiming generation one while evaluating against something else. The
number belongs to whoever owns the publisher, and that is the runtime.

**The generation is pinned before `Capture`, not after** — read alongside the configuration
generation at the top of the attempt and stamped into `ServiceCaptureContext`. That closes a hole: a
service could publish a bulletin from inside its own capture and hand back the generation it had just
created, and the runtime would record a cycle as having consulted a bulletin that did not exist when
the cycle opened. The *context* rather than the fact is where it lives, because the one caller that
genuinely needs the number inside `Capture` reads it there.

**An unbound service is stamped `StrategyGeneration.Initial`, which is one.** Zero is not available —
the trace identity throws on it — and a third number for "no strategist" would name the policy the
neutral bulletin already names. `Current()` falls back to one while `Reported()`, which diagnostics
use, answers `default`, so an unbound service reads as *no* strategy rather than a neutral one.

**A journal branch that could only ever assign zero was removed.** An immediate decision never became
a cycle, so it consumed no bulletin. The journal's `EnsureStrategy` is where a captured cycle's
generation enters, guarded on `IsCaptured` for the same reason: a capture that found nothing consumed
no bulletin, so advancing the journal's strategy would be journalling a change nothing acted on.
*(The per-slot generation binding this entry introduced is gone — the registry is itself the
generation source every slot reads.)*

## W50 — The registry owns the one world, and hands it to both halves of a cycle

**Live.** Second slice of W47, and the correction of a design mistake that survived since the world
was first published: the `ServiceWorldPublisher<T>` *class* had lived in Common from the beginning,
but the *instance* was constructed and owned by an OrbAutomata type, which made the game's world one
feature's property. It is a field of `ServiceCycleRegistry` now, because there is one game and
therefore one world, and the registry is the one suite-wide object that already exists.

**A service is given the write half or nothing.** `ServiceCycleRegistry.WorldPublication` is an
`IServiceWorldPublicationSink<GameWorldState>` — `Publish` and no way to read back. Collection needs
to write; nobody needs to read, because reading is the runtime's job, and a collector that could also
read would be a second, unpinned path to the world.

**The runtime pins once and hands the snapshot to both halves.** `ServiceCycleStartCoordinator` reads
the publication next to the configuration and the strategy generation, at the top of the attempt, and
the same snapshot reaches both halves. Two halves of one cycle cannot disagree about what the game
looked like, and no service holds anything it could read twice. (`GameWorldState` became public,
members unchanged, because a public interface cannot take an internal parameter type.)

**Auto Buy's capture became the worker's projection.** `AutoBuyFrameProjector` is a static class
holding nothing, taking the world as an argument and borrowing the frame's row arrays. Field-free was
forced by the graph audit rather than chosen: a worker may hold no collection, so the dictionary that
deduplicated resource rows became a linear scan. The audit also refused a test fixture that recorded
the world it was handed, and it was right to: holding the world across cycles must not compile.

**One bite-check branch is left open and named.** Re-reading the publication on the coordinator's
*deferred* path — taken when the non-blocking probe cannot publish because the previous response has
not been drained — is uncovered. If that branch ever re-reads the world, a cycle would evaluate
against a snapshot its own capture never saw, and nothing downstream would notice.

**The wall I reported was not there.** I told the user Common could not hand a typed world to a
worker without a fifth generic parameter on every service. That was wrong and I had not checked:
nearly every instantiation was already `GameWorldState`, and the audit forbids a worker *holding* a
publisher while saying nothing about arguments. **The failure was not the conclusion but the order —
I reported a constraint as established before establishing it.**

## W51 — Auto Harvest's projection moves to the worker, and three profile stages cannot follow

**Live.** Third slice of W47. **The stated blocker was not the blocker:** the plan said the move
needed a replay record that had already been deleted. The real check was whether capture touched the
game at all, and it did not — it read the pinned world and four compile-time uuids. It became a
static, field-free `AutoHarvestFrameProjector`, exactly as Auto Buy's did.

**The three capture-side profile stages could not come with it.** A worker definition may hold no
runtime-owned storage, and the profile probe is precisely that. Codes 1003–1005 are retired and
burned (W11), with the trace tool's decoder arms in the same commit — the gate builds that tool under
the profiler symbol for exactly this reason, so a split would have failed the gate rather than rotted
quietly.

**A counter outlived its writer twice.** `ReadyPairs` lost its only writer with the capture adapter;
`SelectedPairs` turned out to be in the same position one commit later, every surviving mention a
zero carried through the record, the aggregate key and the dashboard. The profile record keeps its
fixed size and the retired slots became zero padding the reader checks. **Tests that hand-built a
ready frame now publish a world**, through a shared `AutoHarvestTestWorlds` factory — setup that is
copied is setup that drifts.

## W52 — The two shapes become contracts: one configuration, one capture buffer, one world

**Live.** Fourth slice of W47 and the end of it. The capture phase does not leave the ordinary
contract by convention; it stops being expressible.

**Two shapes, siblings rather than one extending the other.** A service is either a *source*, which
reads the game on the main thread and publishes what it read, or *ordinary*, which reads the
publications and nothing else. `IServiceCycleMainThreadDefinition<TAction>` holds what the main
thread asks of either — identity, wake, fault policy, `ShouldStart`, `TryExecute` — and each shape
declares its own worker factory on top. The plan assumed the source contract would extend the
ordinary one; that cannot compile once the workers diverge, because an ordinary worker takes the
pinned world and a source worker takes the buffer its own capture filled. Naming the worker in the
shared half was the mistake; only the main-thread half is shared.

**Two type parameters die, and neither was ever a choice.** `TConfig` is pinned to
`SuiteRuntimeConfiguration` — one suite, one configuration record — and `TFrame` to
`GameWorldCycleFrame`, surviving only on the source path, because there is one game and therefore one
shape of raw reading. A type parameter in either place was a promise that a second could exist, and
it cannot. What is left is `<TState, TAction>`, and the frame apparatus (`CreateFrame`,
`ReleaseFrame`, `ProjectFrame`, `ServiceFrameStorage`) went with the parameter that died.

**The buffer is one per lifecycle, minted by the runtime.** `ServiceRunnerFactory` became abstract
with one hook returning the start coordinator and the worker together, so the source's buffer is a
local shared by exactly the pair that needs it. A feature-owned buffer would be shared by two
overlapping lifecycles; a field on the factory would make correctness depend on construction order.

**The world arrives as an argument to `Evaluate`, and the strategy still does not.** Projecting the
world and deciding from the projection are one step, on one thread, against one pinned snapshot, so
they collapse into the evaluation. Strategy is the honest gap: no service publishes a bulletin, and
passing the neutral one would invent a delivery seam for a consumer that does not exist — the W50
mistake exactly. It lands with the strategist.

**A start-decision fault stops calling itself a capture.** One fault tracker serves both main-thread
callbacks and it only knew `Capture`, so an ordinary service recorded its `ShouldStart` throw as one.
`ServiceFaultCategory.Start` joins the enum; the category rides the trace as an int32 at a fixed
offset, so a new member changes no byte layout and no format version.

**A wedge the gate had been mistaking for a flake.** Every cross-thread wait in these tests carried
its own one-to-five-second ceiling, each really an assertion that the machine was not busy. They
share one generous deadline now (W33), which turned a sibling-service test from a three-second
timeout into a thirty-second one — the useful result, because the run either finishes in a tenth of a
second or never finishes at all. It reproduces on the pre-change base under load, so it is neither
new nor a timing artifact. Left open and named rather than papered over.

## W53 — Both action queues are published, and a queue reading still admits nothing

**Live.** Correction to W38, which ruled the queue unpublishable.

**The failure mode is about admission, not about publication.** Both services compete for the same
slots and consume them with their own actions, so a collected queue reading is wrong *within* a
single world generation, not merely stale between them, and the freshness gate cannot help because
the gate is per-consumer — it answers "has the world been re-read since *I* acted", and the other
service's action is not mine. That is decisive about what may admit an action into a slot. It says
nothing about what a plan may be *shaped* by, and we read it as though it did. The cost of the
conflation was that no worker could know queue room at all — a hole with no defence, since a plan
that cannot see the queue is not thereby safer, only blinder.

**So the reading is published and the authority is not.** `WorldActionQueue` carries a row per queue
and `WorldActionQueueSlot` a row per slot. The boundary is untouched: `GetRemainingRoom()` is still
read live before every Auto Buy submission, and the live active-action list before every harvest
submission. Nothing plans against the published rows yet, and publishing ahead of the consumer was
the deliberate half of that — the alternative is inventing a consumer to justify the table, which is
the W50 mistake.

**There are two queues, and the campaign map named one.** Auto Harvest competes for the plot-action
queue and Auto Buy for the attribute queue; they are different list variables with different shapes,
and a single "the action queue" category would have had to average them. **Each is read as far as it
is already understood, and no further:** the plot-action queue publishes a row per slot, because its
occupants are the pairs Auto Harvest already reads one at a time at its boundary, while the attribute
queue is effectively an integer — occupancy, plus an edge to the `IntVariable` holding its maximum,
because that registry is collected whole and the link is then the one the game states.

**Neither is reached by a registry walk.** A list variable's `All` is declared on its generic base and
the member binder does not walk base types, so both queues are resolved by uuid through the identity
registry every other lookup already goes through — which keeps the action-manager singleton out of
the collector entirely. **A queue is an entity and a slot is not:** `ActionQueues` is walked like any
other identity table, while `ActionQueueSlots` is keyed by its queue and index and joins the costs
and the pairs in `NotIdentityTables`.

## W54 — The action-safety audit becomes published facts and a worker-side verdict

**Live.** Supersedes W40 on where the check lives, and keeps its rule about what may be published.

**The auditor is gone.** `AutoHarvestStaticContractAuditor` ran at binding time, walked a plot's
phases and an action's cost and completion effects through some twenty-two reflected members, and
cached a verdict on the binding — the last thing on the harvest path that reached into the live game.
Its inputs are collected now like every other fact: plot authoring into `PlotAuthoring` and
`PlotPhaseDescriptors`, the audited scalars onto the action's own row, and each authored completion
block into `EffectBlocks`.

**W40's rule holds; only its conclusion moved.** Publishing a *summary* of a whole-graph structural
check would be publishing a verdict the deriver cannot justify — still true, and still why no column
says "safe". What is published is the terms. The verdict is
`AutoHarvestActionSafety.For(GameWorldState, in AutoHarvestPairAuthoring)`, a pure function of the
snapshot computed on the worker from the same world the plan was made against and carried to the
boundary on the action rather than looked up there. That is the W42/W43 pattern: facts in the
collector, classification in the service.

**Two comparisons are weaker than they were, and the trade was taken with its condition written
down.** The audit required that the modifier's scaling weight and the script's treasure pool be the
very objects the registry resolved — two `ReferenceEquals` checks. A published row carries identities
rather than objects, so both are uuid comparisons now, and an object replaced by another carrying the
same uuid compares equal here where it did not before. What catches that replacement is the collected
lifecycle epoch (W55): a new epoch discards every retained fact and the world is read again. **So the
trade holds only while the five lifecycle hooks live.** If they ever die without an epoch proven to
work without them, this has to be re-decided rather than inherited.

**One term was dropped rather than ported, because nothing could prove it load-bearing.** The audit
also required the completion block's ordinal be zero, but with the completion count already required
to be one, a block's position could never be anything but the first. A check no test can fail is not
coverage.

## W55 — The snapshot says which run of the game it describes

**Live.** `GameWorldCycleFrame.CollectedAtEpoch` is stamped at capture and carried onto
`GameWorldState.CollectedAtEpoch`. Its source is the lifecycle monitor's generation, read through the
delegate the collection port already threaded, fed by the five imperative lifecycle hooks. That is
the permanent design rather than an interim one.

**A generation says when; an epoch says which run.** W18's generation is a frame number, monotonic
within one run of the game; it cannot tell a reader that the run itself was replaced — which is
precisely what the identity comparisons W54 weakened need told.

**What it buys immediately: structural readings stop being retaken every pass.** Plot authoring
(which also fills the phase descriptors) and effect blocks are skipped when the same frame object is
being filled at the same epoch it was last filled at. Skipping means skipping the *native reads*, not
the derivation: the buffers are left alone rather than reset, so the tables are rebuilt every cycle
from unchanged samples, and a skipped category returns the report of the pass that actually read it.

**What it cannot do yet, stated so nobody assumes otherwise.** It cannot detect a save-load that
changes no identity, because nothing has measured whether one occurs. That experiment needs a running
game and a save worth spending, and it is deferred rather than filed as unknown. Until it runs, the
five `PatchOptional` lifecycle hooks are the signal — and they are the *intended* signal for "we are
moving to a new game, trash everything", not a wart awaiting removal.

**One thing that deliberately did not re-point at the epoch.** `AutoHarvestBindingCoherence` stays
anchored on resolver lifecycle generations: what it guards is whether a registry resolution is still
current, a fact about the resolver rather than about a snapshot. Auto Buy's action boundary *did*
re-point — it compares against the snapshot's epoch, ferried in on the action, rather than the
runner's frozen lifecycle value, so a world already collected under a new epoch is no longer refused
while runner replacement lags behind it. The unsafe direction still refuses: a plan made against a
run the game has since left cannot submit, and neither can one carrying a zero epoch.

## W56 — The patches that survive, and what each is waiting on

**Live.** The north star stops claiming Harmony patches are scheduled to die, and names three groups
with what each waits on. Recorded because "the signal patches die with the gate" was load-bearing in
the campaign's goal statement and is true of a minority of the patches.

**The signal patches serving a migrated service are gone.** Auto Buy reads finished structures and
upgrades off the snapshot, so the patches that announced them had nothing left to tell it. The two
queue postfixes turned out to publish onto an invalidation bus with no subscriber at all.

**The completion postfix retired with Spell Leveling, as this record said it would.**
`AfterNativeCompletion` was a single static handler with two registrations —
`StructureSO:CompleteAction` and `UpgradeSO:CompleteAction`, enumerated in
`Plugin.NativeCompletionHookTargets` — feeding Spell Leveling's `NotifyNativeChange()`. It existed
because an unmigrated Spell Leveling had no generation to gate on, and a completed build or purchase
could be the one that unlocked leveling. A migrated Spell Leveling is handed a fresh world whenever
one is collected, which is the same news arriving by the ordinary route, so both registrations, the
handler, and the two `CompleteAction` contracts went together. Nothing replaced them: the whole point
of a generation gate is that a service woken by it needs no second way to be told.

**Auto Concept's three signal patches retired with its migration.** The add/remove postfix, the
rebuild/setup-max postfix, and the discovery/mastery postfix only woke the legacy controller.
ServiceCycle sees assignments, quantities, discovery, and mastery in the next collected world, so
keeping a second patch-fed generation would create two answers to the same question. Their
`AutoConceptLifecycleSignal` state and the patch-only `RebuildCounts` and `SetupMaxSlotsValue`
contracts were deleted together, on W39's rule that an audited member nobody reads is an audit of
nothing.

**What is left, and what each waits on.** `SpellFirePatch` now remains solely as the
before/after probe of `NativeMutationVerifier.Execute` for Auto Cast's fire, so it belongs to the
north star's declared verifier exception — a verifier that may not observe the game cannot verify
anything. The five lifecycle hooks wait on the experiment W55 defers, and the hooks registered from
`ComposeMentor` retire with Mentor.

## W57 — Lifecycle observation installs with Automata, not with Mentor

**Live.** The five optional lifecycle hooks — `SaveStateManager:ImplementLoadedJson` prefix and
postfix, `GameManager:InitGame`, `GameManager:ResetGameState` and
`PersistentResetManager:PersistentResetLogic` — are installed from `ComposeAutomata`, enumerated in
`Plugin.LifecycleObservationHooks`.

**Why.** In `ComposeMentor` they sat after Mentor's two early returns: a mastery hook that is
unavailable, or one whose patch throws, blocks the Mentor runtime and returns before them. So a
blocked Mentor also stopped lifecycle observation, silently, and what that costs is not Mentor's to
spend. `GameLifecycleMonitor`'s generation stops moving, the collected epoch freezes with it, the
collector's structural-fact skip never re-reads after a save-load (W55), and Auto Buy's boundary check
compares one stale epoch against another and admits. A degraded mastery feature is still a feature; a
frozen epoch is migrated services deciding against a world that is no longer the game. The three
`SpellManager` loadout postfixes stay Mentor's own signal and remain behind that early return.

**Quirk.** The list is a `(Target, Handler, Postfix)` tuple table rather than a plain string array,
because one target carries both a prefix and a postfix.
`PluginLifecycleObservationHookTests` pins the set and the handler shapes, not the call site:
composition wants a bound configuration, a Harmony instance and a live Chainloader, so "a blocked
Mentor still installs these" is not a fact a headless test can assert.

## W58 — The per-level prerequisite becomes rows, because the game's own answer takes an argument

**Live.** `WorldEntityRequirement` is one published row per authored condition on an entity's *next*
level, keyed by the upgrade or structure it gates, read once per lifecycle by
`WorldEntityRequirementReader` and sorted by owner and then authored position.

An entity carries two prerequisite containers and they answer different questions. `prerequisites`
gates the entity at all, and its verdict latches into a `available` field the snapshot already
publishes. `prerequisitesPerLevel` gates the specific level being bought — the game asks
`Check(level + queuedLevels + 1)` for an upgrade and `Check(quantity)` for a structure — so there is
no field to read and no parameterless call to make. Auto Buy planned `ScribeScroll4` and the game
refused it on exactly this term, which the snapshot could not see and the refusal bundle could only
name by elimination.

**What is published is the conditions, not the verdict.** Every value a condition compares against is
already a row in the same snapshot — a research level, a structure quantity, a spell's mastery, a
global variable's value — so the verdict is arithmetic a worker can do for itself, and doing it there
keeps the snapshot free of an answer that is only true at one level. It also leaves the rows for the
consumers that want the fact rather than the answer: "this upgrade waits on that research reaching
six" is what chain planning needs and a boolean throws away.

**Generic over the owner, deliberately.** The row carries which registry its owner belongs to, because
the level a container is checked at is a property of the owner rather than of the condition. No
structure in the shipped content authors a per-level condition, so covering them buys nothing today —
and that is the point: a build that starts authoring one is read rather than silently bought through.

**Not the game's own `Check`.** The `Visible` and `Available` comparisons ask their target for a
whole-entity gate, which is the no-argument `Prerequisites.Container.Check()` — it stamps `gameId` and
latches `available`. That is a write, and W36 already logged it as one. Neither occurs in any
per-level container in this baseline, but "none today" is exactly the fact that needs a guard rather
than a habit, so both are refused rather than approximated. The parameterised overload is used in one
place and one only: as the oracle the differential verifier compares this suite's verdict against.

**`Discovered` is not a third member of that family.** Every implementation returns a serialized flag
and writes nothing, so the `Discovered` comparison on a condition that names its target's type —
spell, alchemy recipe, ritual — is modelled like any other. `GenericRequirement`'s `Discovered` is
still refused, and for a different reason than the writing ones: its target is an arbitrary
`UpgradeableObject`, `IsDiscovered()` is virtual across six implementers, and they do not all read the
same field — `EquipmentSO` returns `isCreated` where the other five return `discovered`, and a target
that is not `IDiscoverable` at all answers `true`. A row carries an identity, not a type, so there is
no way to pick the right override from one. That is the same ground `GenericRequirement`'s `Level` is
refused on, and the two should be read as one rule rather than as two coincidences.

**Eight of the twenty-six comparisons are refused, on three grounds.** Six reach the latching
`Check()`: `UpgradeRequirement.Visible`, `ResearchRequirement.Visible`, `StructureRequirement.Available`,
`AlchemyRecipeRequirement.Visible`, `SpellRequirement.Visible` (through `IsDiscoverVisible()`, which
checks two containers) and `GenericRequirement.Visible`. One asks for state the snapshot does not
publish: `SpellRequirement.MasteryLevelReady` reads `masteryXpContainer.IsReadyToLevel()`, which is
not a write and not a gap in principle — it is simply a fact nobody has had a reason to collect yet,
and it is named here so that adding it later is a decision rather than a discovery.
`GenericRequirement.Discovered` is the third ground, above. The remaining eighteen are modelled.

**Unknown is a row, not an absence.** A condition class this suite has not been audited against
publishes a row of kind `Unknown` carrying the class name, and the pass reports itself incomplete and
names it. An entity with no rows reads as unconditional, which is precisely the wrong answer for one
gated by something nobody modelled — so the two cases are never allowed to look alike. The capture
port deduplicates its announcement on the report's text and the reader runs once per lifecycle, so
that is one line per run of the game rather than one per pass.

**Structural, on the collector's own epoch.** Authored conditions change at a lifecycle boundary and
nowhere else, so the reader joins `WorldPlotAuthoringReader` and `WorldEffectBlockReader` on the
existing skip. The epoch it rides is the world capture port's own — `AutomataWorldCapturePort` stamps
`frame.CollectedAtEpoch` from the host's lifecycle counter for the whole collection — not any one
service's.

## W59 — Mastery readiness is the game's own answer, because the threshold is not published

**Live.** `WorldSpellRecipe.MasteryLevelReady` is `SpellRecipeSO.IsReadyToLevelMastery()`, bound with
`Call<bool>` beside the fields the rest of the row reads. W58 named this fact as the one thing
`SpellRequirement.MasteryLevelReady` wanted and nobody had collected; Spell Leveling's migration is the
reason to collect it, and this is that decision.

**Why a call and not a comparison.** Every other mastery track publishes both halves and lets the
worker subtract: `WorldAlchemyRecipe` carries `MasteryXp` and `CachedRequiredXp`, and readiness is
arithmetic. A spell has no such pair. The threshold lives in a `masteryXpContainer` the snapshot does
not publish and whose members are not in the manifest at all, so `MasteryXp` has nothing to be
compared against. Publishing the composed boolean is not a shortcut around a number that exists; it is
the only readable form of the fact.

**It is a read.** W58 already recorded that `IsReadyToLevel()` "is not a write and not a gap in
principle", which is what separates it from the `Check()` family W36 refused: those stamp a game id and
latch `available`, and collection does not write. A parameterless predicate that composes published and
unpublished state and returns is the same shape as `UpgradeSO.IsAvailable()` and
`ResearchSO.IsAvailable()`, both of which capture has called since the first pass.

**The prerequisite half deliberately did not follow it.** `SpellRecipeSO.levelingPrerequisites` is
reachable only through the no-argument `Prerequisites.Container.Check()`, which is one of the latching
writes, so it stays off the snapshot and is re-read at the action boundary instead. That is not a
shortfall: the boundary is the authority on whether an action may run, and a planner that cannot see
the gate plans a level the boundary refuses penalty-free. What the snapshot buys is that the planner
does not propose a spell with no banked experience — the common case, and the one a refusal loop would
otherwise be made of.

**Rejected:** publishing a spell-level cost table beside the readiness flag, mirroring W31/W32.
`SpellRecipeSO` declares no authored cost member — the only handle is `GetLevelCost()`, a call
returning a `ResourceCostList` — so a cost table would have to either call it during collection or port
arithmetic from fields nobody has audited. Affordability is re-read at the boundary with the game's own
`HasEnough()`, which is already an action contract.

## W60 — The equipped loadout is a category, reached by uuid and keyed by position

**Live.** `GameWorldState.SpellSlots` publishes one row per readable position in the player's spell
loadout, and `GameWorldState.SpellCosts` publishes what casting out of each position costs. Both come
from one reader, `WorldSpellSlotReader`. Auto Cast's migration is the reason to collect them, and this
is that decision.

**Reached by uuid, not through the singleton.** The loadout hangs off `SpellManager.instance`, and
`Spell` has no per-type `All` registry to walk. Rather than read the manager, the reader takes the
route W-era action queues already take: `SpellManager.activeSpells` is an ordinary list variable with a
uuid of its own — `ActiveSpells`, now in `data/known-entities.tsv` — so it is fetched from
`IdScriptableObject.RuntimeLookup` and its `value` is walked. Nothing in collection touches the spell
manager, which keeps the one singleton read in the suite at the action boundary where it belongs.

**Not a `WorldPlainBinder`, deliberately.** A plain binder declares `TypeName => "Spell"`, and that
declaration is what `EveryValueMemberOfACollectedTypeIsDeclared` keys on: every scalar and
modifier-record field on the named type would then have to be declared, including the many `Spell`
fields nobody reads and nobody has audited. `Spell` is a runtime instance rather than an authored
asset, and the suite wants sixteen answers from it, not its serialized shape. A bespoke
`IWorldCategoryReader` asks for exactly those sixteen and declares each one, which is the honest
scope. The W17 category count stays at 33 because no new collected *type* was declared.

**The position is the key, and the holes are real.** A slot is not an entity: the player may leave a
position unfilled, and two positions may hold the same spell, so neither a guid nor a dense row index
names a slot. `SlotIndex` is the game's own index — the number `SpellManager.FireSpellIndex` takes —
counting the unfilled positions exactly as the game counts them. A hole publishes no row at all; an
empty-but-present slot publishes a row with `Occupied` false. Both read as "nothing to cast here",
which is the direction a missed reading should fail in, and neither can be mistaken for the other by a
consumer that asks `WorldSpellSlotLookup` for a position rather than indexing the table.

**Readiness is the game's own answer, on W59's licence.** `CastReady` is `Spell.CanCast()`, and the
three terms under it — `ChargeAvailable`, `ResourcesCovered`, `Attuning` — are the game's own
classification of why a refusal happened. These are parameterless predicates that compose published
and unpublished state and return; they neither stamp an id nor latch, which is what separates them from
the `Check()` family W36 refused. Publishing the composite *and* its terms is what lets a planner both
rank and explain, and the boundary re-reads all of it live before the game is touched (M3).

**The cost table W59 rejected is not this one.** W59 declined a spell-*level* cost table: the price of
buying a mastery level, reachable only through `SpellRecipeSO.GetLevelCost()`, wanted by a boundary
that already asks the game `HasEnough()` one candidate at a time. This is the price of *casting*,
reachable through `Spell.GetCost()` and `Spell.GetDrainCost()` on the equipped instance, and its
consumer is a planner that must compare every position in one pass to pick which to cast. Without it
the worker cannot apply a reserve floor or a fullness threshold at all, and would propose the same
refused slot every cycle — the refusal loop W59's reasoning exists to avoid. The entry-reading half is
already precedented: `resource-cost-list.costs`, `resource-tuple.resource` and `resource-tuple.value-big`
have been capture contracts since the first pass, and this reader binds them the same way
`WorldUpgradeCostReader` does.

**Priced per position, not per recipe.** A spell's cost is its recipe's authored cost after the
equipped instance's own modifier chain, and the recipe alone does not answer it. A table keyed by
recipe would have to pick one of the two answers for a spell equipped twice and be wrong about the
other, so the rows carry `SlotIndex` and `Kind` and let the same recipe be priced differently in two
places.

**Rejected:** publishing the loadout as an identity-keyed table of the recipes equipped, which is what
Mentor's `EquippedSpells` policy actually wants. It is a strictly weaker fact — it cannot say which
position a spell sits in, and a cast is addressed by position — so Auto Cast could not have used it.
Mentor can derive its set from these rows when it migrates.

**Rejected:** reading `SpellManager.instance` during collection, which would have been fewer moving
parts than a known-entity uuid. It puts a singleton read on the capture path for a list the identity
registry already holds, and `WorldActionQueueReader` recorded the same refusal for the action manager.

## W61 — The cast rotation advances on the plan, not on the cast

**Decision.** Auto Cast's round-robin cursor moves the moment a slot is chosen, whether or not the
cast the boundary was handed actually commits. The legacy engine advanced it only on a successful
fire.

**Why the difference is forced.** The legacy engine could afford a commit-only cursor because its
admission scan and its cast ran on the same thread in the same pass: the target preflight — a
reflective walk of the recipe's effect graph asking each target request whether anything is in range —
was inside the scan, so a spell with nothing to aim at was skipped *within* the pass and never became
the pending candidate. That walk is main-thread work over live objects with no snapshot form (W60), so
the worker cannot see it. A cursor that waited for a commit would therefore re-pick the same
targetless spell every cycle and starve every other slot, forever.

**What is preserved.** Advancing on the plan keeps the behaviour the legacy scan produced: a slot that
cannot cast costs *itself* its turn rather than costing every other slot theirs. The observable
difference is confined to the case the legacy engine handled inside its scan and this one handles at
the boundary — a refused cast now yields its turn to the next slot and comes round again on its own
next turn, one rotation later.

**Rejected:** feeding the boundary's refusal back into worker state so the cursor could stay put on a
commit and only advance on a live refusal. It would make the worker's rotation depend on action
results, which is a channel the runtime does not have and should not grow for one feature's cursor;
and it would reintroduce the starvation the moment a refusal was ever dropped.

**Rejected:** publishing a target-availability fact so the planner could skip targetless spells and
keep a commit-only cursor. The walk is unbounded reflective traversal of live scene objects per
equipped spell per frame — the precise shape of work the collection pass exists to keep off the
capture path — and it would answer for the frame the snapshot was taken in, not the frame the cast
lands in, so the boundary would still have to re-check it (M3).

## W62 — Concept state publishes, while prospective drain remains a boundary preflight

**Decision.** The world now publishes the native `ConceptRecipes` membership and core-type edge, the
active assignment's current and queued quantities and drain ratio, and both the authored and current
per-resource drain vectors. One `WorldAlchemyInstanceReader` fills the three tables from the two
identity registries. The worker therefore ranks and rotates stable identities, observes settledness,
owns its baseline ledger, and runs the unsafe-drain rollback watchdog without touching the game.

**The prospective multiplier does not publish.** The native answer for quantity N exists only after
constructing a throwaway `AlchemyInstance`, setting its quantity, and calling `GetDrainCostMod()`.
That is not a stored world fact and the published recipe's drain-cost scalar is not evidence that it
reproduces the instance method. The halving search, reserve test, quantity floor, and subtraction of
the live current drain consequently stay together in the action adapter's preflight, immediately
before any add or rotation removal. This is W59's licence applied to a quantity-dependent native
answer: the boundary owns an unpublished gate; the worker does not invent it.

**A preflight refusal does not latch.** The worker advances through its deterministic candidate order
when it publishes an attempt, as W61's cast rotation does, so one prospect the game refuses cannot
starve its siblings. After the sweep it returns to normal pacing and may consider the candidate again
against a later world. Only an attempted add/remove whose queued-quantity postcondition is ambiguous
blocks the native adapter for the lifecycle. This preserves the legacy distinction between an
ordinary projection failure and an unverified mutation.

**The captured vectors still pay rent.** Current drain plus the resource table is the rollback
watchdog's evidence. The authored vector travels as the belief attached to an add or rotation action,
so a refusal records which resources the published recipe said it could affect; it is not treated as
authority for the prospective amount. An empty vector remains distinguishable from a missing recipe
because registry membership has its own row.

## W63 — Attributes publish their primary tab as an identity edge

Scholar attributes are not a new runtime shape. The game stores them in the same
`StructureSO.All` registry and gives them the same quantity, queue, modifier, cost, and
effect fields as Wizardry attributes. Their durable distinction is
`StructureSO.structureType`, an ordinary `StructureTypeSO` reference.

The structure row therefore publishes that primary type's UUID beside the existing
state. It does not copy display names or introduce a Scholar-only registry, and it does
not flatten `structureSubTypes`: the requested tab ownership is the single primary
edge. An unset reference publishes `Guid.Empty`; a build without the field or its
identity contract fails the category binding. This makes every Scholar attribute
already collected by the generic registry walk identifiable without a main-thread hook
or a consumer-owned name table.

## W64 — Recipe-book unlocks publish as their own registry

Discovery trees refer to a `RecipeBookListVariable`, but availability belongs to each
`RecipeBookSO`: its prerequisites are evaluated by `IsAvailable()`, and the static
`RecipeBookSO.All` registry is the complete identity inventory. The world therefore
publishes one identity-keyed recipe-book row with the game's own availability answer.

This keeps “the book is unlocked” distinct from “a recipe from it was discovered” and
from a discovery tree's current offers. The capture calls no discovery mutation and
does not infer unlocks from display state. Current offers and their one-to-many price
vectors remain separate collection shapes rather than being collapsed into this row.

## W65 — Exact mastery gains are patch inputs published with the world

Mentor cannot derive earned mastery XP by subtracting two world snapshots. A native
mastery rollover may consume saved XP, and Mentor's own grant is another writer of the
same value. The three domain observations therefore remain deliberate patch inputs:
spell and alchemy publish their exact native method argument, while the artifact pair
associates the `ExperienceContainer` gain made during one successful equipped-artifact
tick.

The input is still consumed as world data. A bounded main-thread journal retains
sequence-stamped value rows for the current lifecycle; collection copies them onto the
frame, and worker state processes each sequence once even when later world generations
repeat the row. The journal resets its sequence when the collected epoch changes. If a
consumer falls more than the fixed history behind, the missing sequence is explicit
overflow evidence rather than silently reconstructed XP.

Everything surrounding the delta is an ordinary published fact. Recipe discovery,
creation, mastery, equipped spell identity, alchemy core type and Concept membership
already publish. `WorldView.Available` now carries the game's composed progression
answer on W59's licence, and `WorldAlchemyRecipe.CoreTypeId` carries the exact identity
edge the classifier previously re-read. The worker selects recipients from those facts;
the action boundary re-resolves and revalidates the one recipient it is about to mutate.

## W66 — Mentor is an ordinary service with exact-XP patch inputs

Mentor consumes the world, configuration, and strategy publications through the ordinary
service shape. Recipient qualification, source policy, economy arithmetic, ordering, and
action construction are worker decisions. Current UUID/type identity, lifecycle coherence,
ownership, recipient eligibility, the exclusive mastery ceiling, native execution, and the
postcondition remain action-boundary facts.

The mastery hooks from W65 are inputs, not a parallel runtime. They append value-only rows;
world collection publishes them; one worker sequence consumes them. Discovery, creation,
apply-mastery, reset, and loadout hooks retired because their only purpose was to invalidate
Mentor-owned catalogs that no longer exist. This preserves the exact deltas that snapshots
cannot derive without preserving signal patches for facts the world already publishes.

The fixed ServiceCycle turn is the complete execution bound. Mentor's operations-per-frame
and CPU-budget configuration therefore retired with its legacy controller. The Alt+M control
remains a configuration mutation on the main thread and publishes the resulting immutable
suite configuration like every other migrated quick control.

## W67 — Local delivery bounds do not justify a shared CPU coordinator

After Mentor migrated, the shared performance coordinator's only remaining clients were Mods
UI maintenance and gameplay-invalidation delivery. Neither selects a gameplay mutation or
competes for a feature action turn. Each already has the bound its work requires: Mods admits
at most one maintenance pass per frame, and invalidation drains at most its fixed operation
count before continuing next frame.

Weighted admission, soft and hard elapsed-time budgets, mutation leases, starvation
thresholds, work-identity registration, and their separate evidence profile consequently
described contention that no longer existed. They are deleted rather than retained as a
second scheduler. The two local guards remain because they bound their own delivery queues.
ServiceCycle's debug profiler, full trace, decision journal, and dashboard remain the
performance and causal evidence for feature work; their formats did not change.
