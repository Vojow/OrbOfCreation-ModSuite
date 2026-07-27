# Auto Buy stage profiles

[Reverse-engineering index](README.md) · [Auto Buy testing](../testing/automata/auto-buy.md) · [Entity catalog](entity-catalog.md)

## Two different profile classes

Auto Buy uses two intentionally separate concepts:

| Profile class | Purpose | May be called observed game progression? |
|---|---|---|
| Evidence-backed profile | Sanitized snapshot of one audited game build/save/lifecycle with native registry, availability, queue, and cadence observations | Yes, within its recorded scope |
| Synthetic stress profile | Deterministic workload chosen to exercise scaling, fairness, queue turnover, and completion pressure | No |

Names such as early, mid, late, and endgame in a stress test describe workload
shape. They are not claims that every player has exactly that catalog or queue
at that point.

## Evidence-backed facts available today

| Fact | Value | Evidence | Boundary |
|---|---:|---|---|
| Mapped `StructureSO` definitions | 180 | reviewed serialized entity mapping | definition count, not live availability |
| Mapped `UpgradeSO` definitions | 223 | reviewed serialized entity mapping | includes content not necessarily eligible for Auto Buy in one save |
| Observed shared queue capacity | 304 | sanitized current-build runtime validation | one progressed disposable save; live value remains authoritative |
| Candidate registries | `StructureSO.All`, `UpgradeSO.All` | installed static contracts | membership and timing are lifecycle-dependent |
| Individual accepted mutation | exact queued delta `+1` | Automata postcondition and headless adapter tests | exact native resource/callback order still needs runtime evidence |

No sanitized early-, mid-, late-, or endgame-progression snapshot currently
records available/purchasable Structure and Upgrade populations. Therefore no
current performance scenario is labelled an evidence-backed progression
profile.

## Current synthetic stress profiles

| Stress name | Structures | Upgrades | Structure target | Bulk Development | Queue | Completion cadence | Modeling purpose |
|---|---:|---:|---:|---:|---:|---:|---|
| Early | 8 | 2 | 10 each | 10 | 24 | 1 per 60 frames | small catalog, slow consumer |
| Mid | 64 | 12 | 40 each | 25 | 128 | 1 per 15 frames | catalog growth and moderate turnover |
| Late | 180 | 24 | 100 each | 100 | 304 | 1 per 4 frames | full mapped Structure set and fast consumer |
| Endgame | 180 | 24 | 1,000 each | 100 | 304 | 1 per frame | same catalog under maximum modeled turnover |

Only the late/endgame Structure count is tied to the reviewed mapping. Upgrade
subsets, targets, early/mid counts, Bulk Development values, queue sizes below
304, cost curves, resource wealth, and completion cadences are deliberate test
inputs.

These definitions must remain stable for A/B scheduler comparisons. An observed
profile may inspire a new stress profile, but must not silently rewrite checked
history.

## Runtime-derived perturbations

`AutoBuyRuntimeDerivedSimulationTests` kept diagnostic-inspired disturbances
separate from the four stable stage profiles: low Bulk Development sizes,
transient unresolved costs, completion storms, exact affordability boundaries,
one indivisible 35 ms modeled read, and registry growth from 28 to 137
candidates. It was deleted with the legacy Auto Buy runtime and has no
replacement. The perturbations are recorded here because they are worth
rebuilding against the ServiceCycle service, not because they run today. They
were never evidence-backed progression populations and never changed the checked
early/mid/late/endgame workload definitions.

## Observed-profile capture schema

Future sanitized profiles should record:

| Field | Requirement |
|---|---|
| Profile identity | stable name, capture date, audited assembly hashes |
| Progression context | new game/NG+ and coarse milestone labels without save contents |
| Lifecycle | transition kind and generation |
| Registries | total exact-type Structures/Upgrades and same-UUID contradictions |
| Eligibility | available, native-admitted, policy-included, and finite-completed counts by kind |
| Queue | native capacity, remaining room, occupancy, and manual actions |
| Economy | anonymized affordability buckets; never raw save data unless explicitly reviewed |
| Turnover | ordered completion frame deltas and bulk/echo indicators |
| Configuration | group size, continuation, reserve, queue reservation, and ownership state |
| Provenance | runtime log/fixture identifier and corresponding installed-contract result |

The capture must not contain save files, personal paths, unsanitized logs, or
display names used as identity.

## How profiles become tests

1. Validate the installed contract manifest against the capture's game build.
2. Review and sanitize the observation into a bounded fixture.
3. Add a fidelity test that reconstructs only the observed facts.
4. Keep stress tests separate and continue using them for deterministic
   regression budgets.
5. Compare candidate and baseline schedulers against the same profile version;
   a profile change is workload drift, not a performance improvement.
