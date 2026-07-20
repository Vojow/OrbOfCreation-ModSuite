# Auto Buy negative simulation plan

[Auto Buy testing](auto-buy.md) · [Testing hub](../README.md) · [Runtime replay](../runtime-replay.md) · [Runtime protocol](../runtime-validation.md)

**Lifecycle:** Implemented portable matrix; one explicit baseline gap is quarantined

## Purpose

These simulations exercise invalid, contradictory, changing, and failing game
observations while the production Auto Buy engine is actively evaluating and
submitting work. They complement focused adapter/unit tests; they do not replace
installed-game contracts or Unity runtime validation.

Every engine-reliability scenario must prove both sides of containment:

1. the unsafe candidate or observation cannot mutate native state; and
2. unrelated healthy work continues or resumes at the documented recovery
   boundary.

## Global invariants

Check applicable invariants immediately after every injected event:

- no purchase occurs without a valid authoritative queue snapshot;
- queue depth never exceeds a consistent authoritative capacity;
- manual queue reservations are applied exactly once;
- accepted spending never crosses the configured reserve;
- candidate current plus queued levels never exceeds its finite maximum;
- completed automation levels never exceed submitted automation levels;
- at most one supported native mutation is admitted per simulated frame;
- stale lifecycle/native identities never mutate;
- ambiguous attempted mutations do not retry before explicit lifecycle recovery;
- persistent faults use bounded work and do not starve healthy siblings;
- identical initial state plus event tape produces identical order and counters.

## Queue and admission failures

| ID | Scenario | Injection | Required result | Owner |
|---|---|---|---|---|
| NQ-01 | Queue snapshot unavailable | queue capture returns unavailable for several frames | zero mutations/deductions during outage; recovery without lifecycle reset | `AutoBuySimulationFailureTests` |
| NQ-02 | Capacity shrinks during prepared group | lower capacity after one mutation while ranked work remains prepared | stop at refreshed live room; never overfill | `AutoBuySimulationRaceTests` |
| NQ-03 | Capacity contradicts occupancy | set capacity below current queue occupancy | queue snapshot rejected and mutation paused until consistent | `AutoBuySimulationFailureTests` |
| NQ-04 | Manual action consumes last slot after validation | enqueue a manual action immediately before native submit | manual action remains; automation rejects without overfill | `AutoBuySimulationRaceTests` |
| NQ-05 | Capacity expands while waiting | increase capacity without lifecycle reset | prepared/live work consumes only newly valid room | `AutoBuyReliabilityTests` |
| NQ-06 | One-slot reservation edge | toggle one-slot queue between reservation one and zero | zero or exactly one automated entry respectively | existing E2E/unit coverage |

## Purchase and mutation failures

| ID | Scenario | Injection | Required result | Owner |
|---|---|---|---|---|
| NP-01 | Native rejection before mutation | candidate rejects before queue/resource change | exact state unchanged; healthy sibling progresses after the rejected candidate leaves eligibility | `AutoBuySimulationFailureTests` |
| NP-02 | Exception normalized before mutation | simulated adapter reports caught native exception as rejection | no mutation; explicit eligibility recovery reaches healthy sibling | `AutoBuySimulationFailureTests` |
| NP-03 | Mutation reports failure | queue/resource change occurs but adapter reports ambiguous failure | exact delta retained, candidate blocked, no retry before lifecycle | `AutoBuySimulationFailureTests` |
| NP-04 | Exception normalized after mutation | simulated adapter reports caught post-mutation exception | same containment as NP-03 | `AutoBuySimulationFailureTests` |
| NP-05 | Lifecycle recovery of ambiguous block | advance lifecycle after NP-03/04 | block clears only for new generation; fresh mutation may resume | `AutoBuySimulationFailureTests` |
| NP-06 | Permanently failing highest rank | cheapest candidates reject throughout sustained turnover | healthy lower ranks progress; retry/evaluation work remains bounded | `AutoBuyAdversePerformanceTests` |

Raw exceptions deliberately escaping a simulated adapter belong to harness
contract tests. Production reflection adapters are responsible for normalizing
native exceptions before the engine boundary.

## Cost, resource, and lifecycle evidence

| ID | Scenario | Injection | Required result | Owner |
|---|---|---|---|---|
| NE-01 | Cost unresolved | adapter marks current vector unresolved | candidate fails closed; no partial spend | `AutoBuySimulationFailureTests` |
| NE-02 | Negative cost | complete vector contains a negative cost | invalid-resource decision; no purchase | `AutoBuySimulationFailureTests` |
| NE-03 | Negative quantity | current resource observation is negative | invalid-resource decision; no purchase | `AutoBuySimulationFailureTests` |
| NE-04 | Missing resource identity | simulated decoder rejects an empty resource UUID | whole vector unresolved; no purchase | `AutoBuySimulationFailureTests` |
| NE-05 | Duplicate contradictory resource | simulated decoder rejects conflicting entries | whole vector unresolved; no purchase | `AutoBuySimulationFailureTests` |
| NE-06 | Multi-resource partial failure | one of several resource observations is unresolved | no part of vector is accepted; sibling progresses | `AutoBuySimulationFailureTests` |
| NE-07 | Availability unknown | availability read fails | contract unresolved; no cost or mutation | `AutoBuySimulationFailureTests` |
| NE-08 | Native admission contract unknown | candidate lacks complete purchase contract | contract unresolved; no mutation | `AutoBuySimulationFailureTests` |
| NE-09 | Lifecycle evidence unavailable | lifecycle refresh returns no evidence | candidate invalid/quarantined; sibling remains eligible | `AutoBuySimulationFailureTests` |
| NE-10 | Lifecycle evidence contradictory | negative levels or impossible max/queue flags | candidate invalid; no mutation | `AutoBuySimulationFailureTests` |
| NE-11 | Cost rises after planning | raise cost immediately before submit | final live revalidation/rejection preserves resources | `AutoBuySimulationRaceTests` |
| NE-12 | Resource drops after planning | external spend occurs immediately before submit | no reserve breach or negative balance | `AutoBuySimulationRaceTests` |
| NE-13 | Reserve changes during backlog | increase and later relax reserve | immediate stop and configuration-triggered recovery | `AutoBuyReliabilityTests` |
| NE-14 | Threshold chatter | repeatedly move just below/above exact threshold | bounded reevaluation and no missed valid crossing | `AutoBuySimulationRaceTests` |
| NE-15 | Availability flips during ranked pass | lock prepared candidate before its turn | no stale purchase; remaining order preserved | `AutoBuySimulationRaceTests` |
| NE-16 | Lifecycle changes during prepared group | replace identity/wrapper between CPU-sliced levels | stale group discarded; fresh generation resumes | `AutoBuySimulationRaceTests` |

## Completion and queue-corruption observations

| ID | Scenario | Required result | Owner |
|---|---|---|---|
| NC-01 | Completion count exceeds queued levels | reject before state change | `AutoBuySimulationCompletionTests` |
| NC-02 | Completion UUID/type differs from queue front | reject atomically | `AutoBuySimulationCompletionTests` |
| NC-03 | Manual action is queue front | reject exact automation completion without removing manual action | `AutoBuySimulationCompletionTests` |
| NC-04 | Nested native completion | throw a deterministic harness-contract error; outer completion remains finishable | `AutoBuySimulationContractTests` |
| NC-05 | Completion callback arrives after lifecycle replacement | old observation cannot refresh/mutate new generation | `AutoBuySimulationCompletionTests` |
| NC-06 | Echo actions consume reopened room | automation observes final live room and never overfills | existing E2E plus completion tests |
| NC-07 | Several completion callbacks include one malformed observation | accepted observations remain ordered; malformed observation is atomic and does not poison later valid work | `AutoBuySimulationCompletionTests` |
| NC-08 | Queue cleared during active settlement | settlement state resets; no stale follow-up mutation | `AutoBuySimulationCompletionTests` |

Replay codec/parser rejection remains owned by `RuntimeReplayTests`. These
completion tests own the world/engine state after a rejected observation.

## Adverse deterministic performance workloads

These tests use fixed frames and operation budgets. They do not gate host
wall-clock time and do not alter the clean early/mid/late/endgame history.

| ID | Workload | Disturbance | Required metrics |
|---|---|---|---|
| NF-01 | Scarce economy | repeated reserve-threshold crossings | bounded evaluations; exact wakeup; no reserve breach |
| NF-02 | Locked catalog | 10–25% unavailable candidates | bounded lifecycle/cost reads; available candidates progress |
| NF-03 | Failing leaders | cheapest 5% reject before any native call | healthy lower ranks progress; rejecting leaders retry on bounded exponential delays and can recover |
| NF-04 | Capacity oscillation | alternate 64/128/304 valid capacities | no overfill; renewed room consumed promptly |
| NF-05 | Manual bursts | deterministic manual actions take reopened slots | manual ownership preserved; automation recovers |
| NF-06 | Completion bursts and gaps | clustered completions followed by quiet frames | settlement/evaluation work remains bounded |
| NF-07 | Lifecycle interruption | reload halfway through sustained workload | stale work discarded; target progress resumes |
| NF-08 | Resource-read outage | unresolved costs for a bounded interval | zero unsafe mutation; bounded recovery work |
| NF-09 | Low-Bulk endgame matrix | 180 Structures at a 1,000-level ceiling plus 24 finite Upgrades; live Bulk Development 1/3/10/100 | every candidate progresses fairly; one-mutation-per-frame and evaluation-per-purchase budgets hold |
| NF-10 | Mixed transient cost outages | three disjoint cohorts become unresolved and recover while healthy work continues | each failed cohort stays unchanged during its outage and resumes on authoritative invalidation |
| NF-11 | Bulk-3 completion invalidation storm | up to eight native completions before each tick against 130 Structures | callbacks coalesce into fewer evaluation batches; groups never exceed three; all candidates progress |
| NF-12 | Near-threshold partial groups | exponentially rising live costs sit one unit below and then exactly at reserve boundaries | a Bulk-10 group stops at the first invalid level and resumes exactly at the next valid crossing |
| NF-13 | Heavy-tail candidate read | one modeled read in each slice costs 35 ms while ordinary reads cost 0.02 ms | the indivisible over-budget read completes, later work resumes, and no frame mutates more than once |
| NF-14 | Catalog ramp under load | register candidates in four batches while purchases and completions continue, growing 28 to 137 | old and newly registered candidates progress without queue overfill or unbounded evaluation work |

NF-09 through NF-14 are runtime-informed perturbations, not sanitized runtime
replays or observed progression profiles. Their deterministic operation budgets
are intended for scheduler comparisons; host wall-clock duration remains
diagnostic only.

## Seeded state-machine simulation

`AutoBuySimulationStateMachineTests` generates a deterministic bounded tape from
reviewed events:

- resource increase/decrease;
- availability lock/unlock;
- manual enqueue;
- automation completion;
- valid queue-capacity change;
- transient queue/cost/purchase fault;
- reserve change;
- lifecycle reload;
- emergency disable/re-enable.

The runner checks global invariants after every event. A failure reports the
seed, initial world, event index, bounded event tail, queue/resource/candidate
state, lifecycle generation, decisions, and counters. It then attempts bounded
prefix reduction. Replay-compatible lifecycle/resource/queue/progression/
completion events can be emitted through the sanitized V1 vocabulary; synthetic
fault controls remain test-only and are never written as runtime replay events.

## Simulator contract tests

These validate the harness rather than production Auto Buy and must live in
`AutoBuySimulationContractTests`:

- zero/negative initial capacity;
- negative capacity update;
- blank resource identity;
- negative or over-limit manual action count;
- manual enqueue beyond room;
- zero/negative/over-limit exact completion count;
- nested completion;
- negative echo count;
- use after disposal;
- exact completion mismatch atomicity.

Harness-contract passes cannot be cited as engine reliability evidence.

## Implementation order

1. Add explicit observation and purchase fault modes without changing normal
   simulation metrics.
2. Add queue/cost/lifecycle failure and race tests.
3. Add completion and harness-contract tests.
4. Add the seeded event tape, invariant checker, and bounded failure report.
5. Add adverse deterministic performance tests with reviewed operation budgets.
6. Run `AutoBuyReliability`, `AutoBuyPerformance`, `PerformanceAll`, and `All`;
   update this lifecycle and the Auto Buy guide with exact evidence.

Steps 1–5 and NF-09–14 are implemented. NF-03 is now enforced against the production
scheduler. A definite rejection before any native call advances to the next
ranked candidate and records a 0.25-to-5-second exponential retry. Attempted or
ambiguous mutations retain lifecycle quarantine and are never placed on this
ordinary retry path.

Current-tree focused evidence from 2026-07-20 (`Release`, game stubs):

- `AutoBuyDecision`: 8 passed, 0 skipped;
- `AutoBuyReliability`: 143 passed, 0 skipped;
- `AutoBuyPerformance`: 22 passed, 0 skipped;
- `PerformanceAll`: 31 passed, 0 skipped;
- `All`: 852 passed, 0 skipped.

NF-03 has no `Skip` marker; no case in this matrix is intentionally
quarantined.

## Implemented suites

- `AutoBuySimulationFailureTests` covers NQ-01/03, NP-01–05, and NE-01–10.
- `AutoBuySimulationRaceTests` covers NQ-02/04 and NE-11/12/14–16.
- `AutoBuySimulationCompletionTests` covers NC-01–03, NC-05, NC-07, and NC-08;
  existing E2E cases retain NC-06.
- `AutoBuySimulationContractTests` covers the complete harness-contract list,
  including NC-04.
- `AutoBuySimulationStateMachineTests` runs four 240-event deterministic tapes
  with per-event invariants, deterministic replay comparison, bounded event-tail
  diagnostics, and first-failing-prefix reduction.
- `AutoBuyAdversePerformanceTests` enforces NF-01–08 with modeled operation
  budgets, including permanent rejecting-leader isolation and timed recovery.
- `AutoBuyRuntimeDerivedSimulationTests` enforces NF-09–14, including the
  four-value low-Bulk matrix, staggered recovery, completion coalescing,
  exact reserve boundaries, heavy-tail slicing, and incremental catalog growth.

## Runtime handoff

The simulator cannot prove real exception normalization, Harmony ordering,
native multi-buy restoration, or save behavior. Mirror NP-03/04, NE-16, and the
resource-read outage during the bounded failure-circuit probe in
[runtime validation](../runtime-validation.md), then perform the combined soak.
