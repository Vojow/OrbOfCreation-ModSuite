# Auto Buy rejection-aware scheduler plan

> **Lifecycle: Grouped continuation and definite-rejection backoff implemented / runtime gate pending.** Typed rejection evidence, independent purchase grouping, repeating ranked passes, exact Structure reserve/affordability threshold wakeups, and bounded pre-mutation retry are portable-tested. Game-backed fixed and high-resource queue-filling runs completed without native failures. Conservative Upgrade handling, broader profiling, and the Steam Deck matrix remain.

[Back to plans](README.md) · [Orb Automata plan](automata.md)

## Goal

Reduce idle time between valid purchases and eliminate wasteful repeated evaluations without making Auto Buy less safe. The shared performance coordinator and `LeaveQueueSlots` reserve remain mandatory boundaries.

The scheduler should evaluate Structures and Upgrades as one economic candidate set, explain why every non-ready candidate is waiting, and wake resource-blocked candidates only when relevant resource state can make them eligible.

## Invariants

- Submit purchases only through the existing native mutation paths.
- Revalidate availability, reserve, affordability, and queue room immediately before every purchase.
- Keep coordinator admission around native mutations.
- Never consume the configured empty queue slots.
- Keep Structures and Upgrades in one deterministic ranked recommendation set.
- Preserve the current native `CanPurchase` ordering until runtime evidence shows that a different order is correct.
- Treat `Auto Buy rejected` as an evaluation outcome, distinct from `Auto Buy could not purchase`, which means the native mutation failed.

## Target state model

Each active candidate has exactly one current scheduler state:

1. `Ready` — ranked with all other eligible Structures and Upgrades.
2. `ResourceWait` — blocked by one or more structured resource thresholds.
3. `LifecycleWait` — locked, unavailable, or rejected by the native purchase contract.
4. `TimedRetry` — needs a bounded retry because no reliable event exists.
5. `ConfigExcluded` — filtered by allowlist or blocklist.
6. `Completed` — terminal lifecycle state.
7. `Invalid` — unresolved adapter data or quarantined mutation path.

## Delivery phases

Current branch evidence: installed IL confirms that Upgrade `CanPurchase()` combines max-queued, affordability, availability, queued-level requirements, and queue admission. Automata therefore preserves that native call, decodes the exact current cost before classifying a false result, retains resource dependencies when its own reserve or affordability policy identifies a resource wait, and parks other ordinary-resource native rejections for bounded lifecycle retry. Bandwidth resources remain tracked because native admission uses missing usage rather than ordinary quantity.

### Phase 1 — Evidence and observability

- Replace free-form-only rejection outcomes with stable typed reasons.
- Attach structured resource blockers containing resource identity, cost, available quantity, and required quantity.
- Track each candidate's latest rejection signature and distinguish repeated unchanged rejection from a state transition.
- Emit rate-limited aggregate rejection summaries while retaining bounded verbose examples.
- Preserve existing purchase ordering and invalidation behavior.

Portable exit gate: unit tests prove multi-resource blocker capture, rejection-state transitions, aggregate counts, and unchanged queue/coordinator purchase behavior.

### Phase 2 — Threshold-indexed resource wakeups

The current bounded slice stores exact blocker thresholds on stable Structure decisions for ordinary quantity resources. Quantity-only updates below those thresholds, including already-satisfied dependencies of a multi-resource candidate, stay parked; the first blocker crossing wakes the candidate, while bandwidth, capacity, quality, attribute-cost, identity, availability, lifecycle, policy, queue, and completion evidence remains conservative. Upgrades intentionally retain their previous quantity retry behavior because native `CanPurchase()` also represents lifecycle and queue state. Unavailable-resource backoff and broader indexed diagnostics remain follow-up work.

- Add a reverse wait index keyed by resource UUID and required quantity.
- Move resource-blocked candidates into the index after evaluation.
- On a resource change, dirty only candidates whose threshold may have crossed; keep capacity, quality, attribute-cost, identity, and unknown changes conservative.
- Recompute all blockers after any candidate wakes because multi-resource costs require every blocker to clear.
- Bound periodic reconciliation so missed native events cannot strand a candidate permanently.

Portable exit gate: deterministic tests show fewer reevaluations during sub-threshold income ticks, immediate wakeup at threshold crossing, and no missed multi-resource eligibility.

### Throughput slice — grouped continuing passes

- Give each ranked Structure its configured `Single`, `Fixed`, `BulkDevelopment`, or `ActionMultiplier` group, while Upgrades remain one level except in `ActionMultiplier` mode.
- Advance through the prepared ranking and repeat passes while queue quota and live admission permit; group size and continuation are independent policies.
- Revalidate the native current cost, reserve, and affordability policy before every individual level instead of projecting from a stale first-level cost.
- End immediately at the first failed admission check, fixed batch cap, emergency stop, manual queue invalidation, or queue-slot reserve.
- On a definite rejection before any native call, advance to healthy lower ranks and retry the rejected candidate on bounded exponential delay. Never use this retry for attempted or ambiguous mutation.

Portable exit gate: abundant resources feed every usable queue slot without per-level rescans, all ranked candidates progress, NF-03 cannot starve healthy lower ranks, and rising costs or reserves stop a group at the exact safe level.

### Throughput slice — bounded purchase bursts

- Retain one mutation-owning feature lease per Unity frame while allowing Auto
  Buy to submit up to 16 sequential exact one-level purchases inside that lease.
- Revalidate mode, emergency state, ownership, lifecycle, candidate state, live
  cost, reserves, and authoritative queue room before every native call.
- Stop at the 1 ms purchase slice, group/pass/batch boundary, live safety change,
  ambiguous outcome, or hard call ceiling; report each native call as an
  operation while preserving one lease admission.
- Keep other mutation owners excluded for the full lease and prove they resume
  on later frames.

Portable exit gate: modeled native costs yield deterministic 1/2/4/8/16-call
leases, eight completions per frame do not drain an abundantly supplied queue,
mid-burst safety changes stop the next call, and Auto Cast progresses without a
same-frame overlap.

### Phase 3 — Shared affordability projection

- Expose the same structured admission calculation to the optional configuration/UI surface.
- Show whether a target is affordable and an estimate of how many sequential levels fit current resources.
- Label the count as a projection because costs and modifiers can change after each native purchase.
- Do not poll the game independently from the UI; consume catalog snapshots produced by Automata.

### Phase 4 — Game-backed validation and tuning

- Desktop evidence: a fixed Structure batch submitted three purchases with zero failures and CPU slicing active. After policy-exclusion parking was added, `NotAllowed` reached the 89 excluded registered candidates once and stayed flat across later 30-second summaries; the one allowed reserve-blocked candidate continued to provide resource-wakeup data.
- A disposable 13-resource `9e60` profile completed 150 native purchases with zero failures. Retaining the next ranked candidate only across a full-queue wait reduced sustained candidate evaluations from 58,973 to 1,483 (97.5%) while preserving post-group dirty settlement whenever another queue slot remained usable.
- The `0.8.1` fair-pass profile filled the visible shared queue from `14/304` to `302/304` in ten seconds with one manual slot reserved. Its 130-second log recorded 1,797 successful submissions across 166 distinct candidates, including both Structures and Upgrades, with zero native failures.
- Capture separate counts for evaluation rejections and native purchase failures.
- Verify what Upgrade `CanPurchase` includes in the supported game build before changing cost-read ordering.
- Profile scans, dirty wakeups, coordinator waits, queue waits, and native mutation duration on desktop and Steam Deck/Proton.
- Compare time-to-next-purchase and rejected evaluations per successful purchase against the current release and AutobuyOrb under equivalent settings.

Runtime exit gate: no overspend, no queue-reserve violation, no coordinator bypass, no stranded eligible candidate, and materially fewer unchanged resource rejections.

## Why not a purchase queue or dependency tree first

A purchase queue becomes stale whenever a prior purchase changes costs, resources, availability, or queue room. A dependency tree cannot safely encode native lifecycle rules that are only observable through game APIs. The rejection-state index is the safer primitive: it narrows reevaluation work while retaining final native revalidation and deterministic ranking.

## Measurements

Track at minimum:

- evaluations and recommendations;
- rejections by typed reason;
- repeated unchanged rejections versus state transitions;
- candidates waiting per resource;
- resource wakeups and threshold crossings;
- native purchase attempts, successes, and failures;
- coordinator-denied frames and queue-reserve waits;
- time from becoming eligible to native purchase submission.

The key ratios are rejected evaluations per successful purchase and median/p95 eligible-to-submit latency. Log volume alone is not a speed metric.
