# Auto Buy on ServiceCycle: what is still deferred

> **Lifecycle: Active.** Open items only. What Auto Buy does today is described by
> [Orb Automata](../../src/Automata/README.md), the
> [runtime architecture dossier](../runtime-architecture/README.md), and the
> [native purchase pipeline](../reverse-engineering/auto-buy-native-pipeline.md).

[Back to plans](README.md)

Auto Buy was ported onto ServiceCycle as a playable baseline first, and everything below was
deliberately left out of that baseline. Each item needs its own decision; doing them before there was
a baseline to measure against would have been scope creep against something unobservable. Nothing
here is lost work — it is parked with enough context to resume.

## How this work is done

Every performance question raised here must leave the trace dashboard able to answer that question
directly on future captures. Extend or revise the dashboard alongside the investigation rather than
leaving the answer in a one-off analysis. Prefer exact, service-separated, zoomable evidence over
visually compact aggregation, and remove or demote charts that do not inform a decision.

Land one behaviour or performance change at a time. After each, run the portable and real-reference
gates, install a profiler-enabled build, and capture one comparable session before starting the next.

## Open work

1. **An elapsed-time action budget.** Auto Buy's per-turn limit of 16 attempts is a count cap, not a
   wall-clock one. Measured full slices stay under a 16.7 ms frame in the large majority of cases but
   not all of them, and a synchronous native call can still overrun after it starts. A budget should
   decline to *begin* another action once its slice is exhausted.
2. **Restore deliberate worker overcommit.** Emit roughly twice the usable queue room so multi-frame
   draining can overlap native queue consumption. This is action-list overcommit, not multiple native
   mutations inside one action. Its value is doubtful now that each turn drains a bounded slice,
   because an ordinary native queue cannot free slots within one tick.
3. **Reassess static binding.** Use decompile artifacts to evaluate direct static game bindings and
   the fail-closed behaviour for unknown game versions.

### A deeper pass: eliminate reflection entirely

The current binding caches dispatch but keeps runtime discovery. A later pass should decompile the
game to learn the exact concrete types and member layout, then bind fully statically against
publicized references — no discovery at all. That removes the one-time audit cost and the delegate
indirection, and would show whether the game exposes cheaper bulk-read entry points the suite
currently reconstructs candidate by candidate.

The defensive guards (runtime audit, explicit unaudited-contract refusal, per-invoke
exception containment) exist so a game update fails closed with evidence. Since the suite
quarantines mutation against an unaudited build unless the player explicitly accepts that exact
pair, removing them is coherent only if each adapter still refuses unknown shapes.
Quantify the per-candidate cost of the audit and try/catch branches before deciding.

## Deferred (post-baseline)

### 1. Structured "X of Y levels" in the trace and decision journal
- **What:** surface the per-action level counts (`CommittedLevels` / `RequestedLevels`, already on
  `AutoBuyPurchaseSubmission`) as structured fields in the semantic trace and the decision journal,
  not only in the text log.
- **Why deferred:** the shared `NativeMutationCallOutcome` is a *call*-count model whose coherence
  invariant (`committed == attempts` for a verified action) cannot express "3 of 5 levels". Carrying
  it means a new feature-scoped units field on the shared `ServiceActionResult` — not inside
  `NativeMutationCallOutcome` — plus semantic-recorder and journal plumbing. It cuts across the Auto
  Harvest action path too, where "harvested X of Y" would use the same field, so it is a real
  contract decision.
- **Resume:** `AutoBuyCycleActionAdapter` discards the counts; the data is on the submission. The
  journal carries a per-cycle `RequestedLevels` through `AutoBuyServiceProjection`, which is a cycle
  total rather than a per-action pair.

### 2. Worker-side profile stages
- **What:** measure the frame projection and the ported formulas, the way the four action-boundary
  stages already measure the native edge.
- **Why deferred:** this is blocked rather than merely unscheduled. The profile probe is
  owner-thread affine and a worker definition may hold no runtime-owned storage, so worker stages
  need a different probe than the suite has.

### 3. Dynamic grouping beyond the native preferred count
- **What:** decide whether Auto Buy should ever expand beyond the live Bulk Development preference
  when still more levels are affordable.
- **Why deferred:** the worker now descends from that preferred count and emits the largest positive
  count whose exact published rising-cost sum clears the remaining batch ledger. It never substitutes
  `levels × next cost`; an intermediate count with no exact published sum is refused, while the
  one-level row remains the safe floor. Expanding beyond the player's native preference would be a
  new policy, not completion of the affordability fix.

### 4. Replicating the last game formula in the worker
- **What is left:** `CanPurchase()` is the one live game call on the action path, by design — it
  folds availability, the level caps, the price and the per-level prerequisites, each of which can
  move between the world the worker planned from and the mutation. The cost chain is ported and
  `GetTrueQuantity` is collected, so the open half is only whether that boundary call should become a
  formula at all.
- **Why deferred:** blocked on serialized prerequisite and topology evidence that portable tooling
  cannot extract. Asking the game at the moment of mutation carries no parity risk.

### 5. Worker-side `LeaveQueueSlots` subtraction
- **What:** the worker plans against raw captured room; the action adapter is the sole authority that
  reserves slots. Purchases are identical either way, but the worker over-plans by up to the reserve
  — one extra rejected journal entry per full-queue cycle.
- **Why deferred:** cosmetic and efficiency only; not worth churning the parity-tested evaluator.

### 6. Reserve semantics rethink
- **What:** an empty or malformed `AbsoluteReserve` resolves to the config default of 0, a deliberate
  break from the legacy reject-everything path. The whole reserve model is flagged for a later
  redesign.

## Open questions

### A. What native `Purchase()` reports on failure
`StructureSO.Purchase(bool)` and `UpgradeSO.Purchase()` both return void, so the adapter diffs the
queued level before and after. In live play `CanPurchase()` returns true, `Purchase()` is called, no
exception is thrown, and yet the queued-level delta is zero for roughly a fifth of attempts. Either
the game's `CanPurchase` and `Purchase` disagree, or the queued-level counter is the wrong success
signal for some purchase shapes. Decompiling `Purchase`, `CanPurchase` and `GetQueuedQuantity` would
settle it. The decision that follows is whether to keep post-verification, cheapen it, or trust
`CanPurchase` and drop it — main-thread cost and false-negative risk are the drivers.

### B. Gather → attempt → outcome decision chart
Per cycle: what was planned, how many levels were attempted, and the outcome per candidate, as a
timeline. Blocked on the per-action X-of-Y counts in deferral 1; the cycle totals the state
projection carries give the chart its rows but not its outcomes.

### C. Where cost decoding spends its time
Cost decoding and resource-state reads are the clearest remaining hot path, and they are world
collection's now rather than Auto Buy's. The suspicion is reflection-based BigDouble field extraction
plus per-resource-row reads. Investigate whether decode can be cheapened with cached delegates,
batched or flattened row reads, or structure-of-arrays buffers. SIMD does not apply — `BigDouble` is
a mantissa-and-exponent object graph, not a flat numeric array — but flattening the grab loop may.
