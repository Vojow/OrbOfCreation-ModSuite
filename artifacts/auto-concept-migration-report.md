# Auto Concept ServiceCycle migration report

Branch: `shift/auto-concept-cycle`  
Base: `42365639abd70762c2e6d002f4ae9fb7d112e817`

## Commits and gates

Every implementation commit passed both stub builds, the portable gate, and the
installed-game contract gate against its own tree.

| Commit | Subject | Portable | Profile | Installed contracts |
|---|---|---:|---:|---:|
| `42c2e2155ed9df25c6ceee8e70aa4834e14d7c09` | `world: publish concept assignments and drain inputs` | 1,928 | 88 | 24 |
| `c2ecdea9f91ae3e20225ada966cf50f1b04b1bc4` | `auto concept: plan assignments from the published world` | 1,937 | 88 | 24 |
| `89d96e9c00af2ff2d683af011fb3fbb3216040cb` | `auto concept: act through the verified native boundary` | 1,950 | 88 | 24 |
| `2f4847bae5e4ce9956b0347afd14b073ce3f35d1` | `auto concept: register the cycle and retire the legacy runtime` | 1,946 | 89 | 24 |

The final clean stub builds pass at the existing ceilings: 44 source-build
warnings and 244 test-build warnings, with no errors. The final portable count is
lower than commit 3 because the legacy controller tests retired with the
controller; the profile count gained the trace-dashboard label test.

## Drain-projection decision

The worker owns only facts that can be honestly published. One world reader now
publishes:

- Concept recipe membership and the core-type edge;
- current and queued assignment quantities;
- drain ratio and drain readability;
- authored recipe drain rows and current active drain rows; and
- the ordinary world-resource quantity, rate, drain, true-rate, and soft-cap
  ratios used by planning and the rollback watchdog.

The prospective quantity multiplier stays at the main-thread action boundary.
The game's answer exists only after constructing a throwaway
`AlchemyInstance`, setting the proposed quantity, and calling
`GetDrainCostMod()`. The captured recipe scalar is not evidence that this
method can be reproduced worker-side.

The boundary therefore keeps the parity-critical calculation together:

1. clamp the desired target to the live mastery maximum;
2. construct the prospective instance and obtain its drain multiplier;
3. multiply the recipe drain vector and subtract the live instance's current
   drain;
4. quality-adjust each positive increment with `GetTrueSpend`;
5. require `currentRate - increment >=
   (currentRate + currentDrain) * RateReservePercent / 100`;
6. require `quantity / trueSoftCap >= MinimumResourcePercent / 100` for finite
   resources; and
7. halve the candidate delta until a safe target is found or none remains.

Before applying anything, the action adapter also rejects a stale collected
epoch and the native adapter re-resolves exact recipe identity, settled current
and queued quantity, ownership belief, compatible replacement, slot admission,
and live mastery maximum. Add/remove still uses
`NativeMutationVerifier` around queued-quantity before/after captures.

A projection refusal is deliberately non-latching: the worker advances its
deterministic candidate cursor and may reconsider the recipe against a later
world. Only an attempted mutation with an ambiguous postcondition blocks the
native adapter until lifecycle replacement. This is recorded as W62.

## Harmony patch decisions

All three Auto Concept patch classes retired:

- the add/remove postfix;
- the rebuild/setup-max postfix; and
- the discover/apply-mastery postfix.

They only fed `AutoConceptLifecycleSignal` and woke the legacy controller. The
ServiceCycle worker now observes those changes through the next collected world,
so retaining patch-fed state would create a second competing generation signal.
`AutoConceptLifecycleSignal` and the patch-only `RebuildCounts` and
`SetupMaxSlotsValue` contracts were deleted together.

`SpellFirePatch` remains because it is Auto Cast's before/after mutation-verifier
probe. The five shared lifecycle hooks remain under W55, and Mentor's hooks and
four shared legacy contracts were left untouched.

## Native-contract manifest

| Place | Before (`4236563`) | After (`2f4847b`) | Delta |
|---|---:|---:|---:|
| capture | 646 | 657 | +11 |
| action | 61 | 70 | +9 |
| legacy | 57 | 32 | -25 |
| patch | 5 | 5 | 0 |
| total | 769 | 764 | -5 |

The five removed contracts were the two patch-only list methods and the three
planner-only recipe progress methods. The remaining former Auto Concept
contracts moved to capture or action according to their actual reader.

The following shared contracts stay legacy because Mentor still names them:
`alchemy-recipe.apply-mastery`, `alchemy-recipe.discover`,
`alchemy-recipe.is-discovered`, and `abstract-list.value`. Auto Concept was
removed from their ownership accounting.

## Retired legacy runtime and CPU-budget surface

- Deleted `AutoConceptController` and its controller tests.
- Retained pure `AutoConceptModel` policy helpers and the shared classifier
  adoption tests.
- Reduced `AutoConceptNativeAdapter` to action-time live authority; it no longer
  performs planner catalog/progress scans.
- Replaced the Auto Concept legacy registry slot with the shared ServiceCycle
  activation. `AutomataProductionComposition` now contains no legacy Automata
  feature service.
- Removed `AutoConceptEvaluate` and `AutoConceptMutation` from
  `SuitePerformanceWorkIdentities`.
- Removed their two performance-profile rules, reduced the supported rule count
  from six to four, updated the pinned profile hash and pipeline count, and
  updated fairness/evidence tests to the live producer set.

The shared CPU-budget coordinator remains load-bearing for Mentor's mutation and
cooperative work, Mod Config UI work, and gameplay-invalidation delivery, so it
was not removed.

## Deviations and stop conditions

There were no behavior-parity deviations and no stop condition was reached.
The migration followed the requested four-commit rhythm. It did not change the
trace wire format, an assembly baseline, or the manifest schema; it did not
modify `src/Mentor/**`, `docs/strategist/**`, or the four shared Mentor
contracts.
