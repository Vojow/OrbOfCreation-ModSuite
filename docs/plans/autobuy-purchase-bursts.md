# Auto Buy bounded purchase bursts

> **Lifecycle: Implemented / installed-game runtime gate pending.** Auto Buy may
> submit several independently verified purchases inside one coordinator lease,
> bounded by live queue room, ranked grouping, a 16-call ceiling, and the
> existing 1 ms purchase slice. Portable safety and throughput simulations are
> implemented; desktop and Steam Deck native timing remain required.

[Back to plans](README.md) · [Auto Buy scheduler](autobuy-rejection-index.md) · [Performance architecture](performance-suite.md) · [Auto Buy testing](../testing/automata/auto-buy.md)

## Goal

Refill the native action queue faster than one purchase per rendered frame when
the audited one-level Structure or Upgrade calls are cheap enough, without
weakening any per-level admission or mutation postcondition.

## Policy

- The shared coordinator still admits at most one mutation-owning lease in a
  suite frame. Auto Cast, Auto Concept, Spell Leveling, and Mentor never overlap
  an Auto Buy burst.
- One Auto Buy lease may attempt at most 16 native purchases.
- The effective burst is smaller when live queue room, the current ranked
  candidate group, the batch quota, or the 1 ms purchase slice is exhausted.
- There is no new player setting. Cheap calls use available headroom; an
  individually expensive call naturally leaves the frame at one purchase.
- Ranking fairness is unchanged: each Structure receives only its selected
  group before the next ranked candidate, and ordinary Upgrades receive one
  level before advancing.

## Per-level transaction

Before every native call, Auto Buy rechecks mode and emergency state, ownership,
lifecycle generation, exact candidate state, live cost and reserve policy, and
authoritative queue capacity. The Structure or Upgrade adapter invokes exactly
one audited purchase method and requires an exact queued-level delta of `+1`.
The selected candidate and lazy resource epoch are invalidated before another
level can run.

The burst stops immediately after a queue/capacity boundary, policy rejection,
ownership or lifecycle change, emergency disable, CPU-budget crossing, native
exception, failed postcondition, ambiguous mutation, or the 16-call ceiling.
Attempted ambiguous mutations keep their existing lifecycle quarantine.

## Coordinator accounting

A burst consumes one non-preemptible mutation lease, preserving suite-level
feature exclusion and weighted frame fairness. Completion evidence reports the
actual native calls, mutation attempts, verified commits, and one operation per
audited call. Compatibility counters that count admitted leases remain one for
the frame; mutation-attempt counters can increase by up to 16.

## Portable gates

`AutoBuyBurstTests` and `AutomataCoordinatorTests` cover:

- deterministic 1/2/4/8/16 purchase outcomes from modeled native cost;
- the 16-call ceiling and Bulk-3 candidate fairness;
- finite Upgrade handoff;
- native multi-buy restoration after every Upgrade in a full burst;
- exact queue reservation, capacity shrink, rising cost, and reserve boundaries;
- emergency disable, ownership loss, and lifecycle replacement inside a burst;
- ambiguous second-mutation containment;
- eight completions per frame with queue refill headroom;
- one coordinator lease with exact multi-call accounting; and
- Auto Cast progress without a same-frame mutation overlap.

Existing stage history retains its 1.1 ms modeled purchase and therefore remains
a stable one-purchase-per-frame comparison. Burst performance is a separate
runtime-derived gate rather than a silent rewrite of that baseline.

## Runtime gate

Before promotion, capture desktop and Steam Deck/Proton evidence for native
purchase duration, burst size distribution, queue refill depth, synchronous
Harmony ordering, Upgrade multi-buy restoration, UI popup/tween pressure, and
Auto Cast/Mentor wait frames. The run must show no reserve breach, queue overfill,
manual-slot consumption, ownership overlap, lifecycle-stale purchase, ambiguous
retry, or unbounded frame overrun.
