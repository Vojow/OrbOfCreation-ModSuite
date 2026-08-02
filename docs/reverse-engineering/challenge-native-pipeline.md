# Challenge selection, activation, abandonment, and offer pipeline

This dossier is the audited native boundary for `V-CHAL-01` through `V-CHAL-04` on Orb Of
Creation v1.0.5. One `ChallengeGameAction` owns player selection, activation queueing,
abandonment, and both Time and prestige offer refreshes. MCP and portable tests use that same
boundary; there is no separate tooling transaction.

## Identity and state

Every targeted mode resolves the submitted stable UUID as exactly `ChallengeSO`. Names are
diagnostics only. `ChallengeSO.ChallengeState` is fixed by installed metadata to:

| Value | Native member | Player meaning |
|---:|---|---|
| 0 | `None` | idle, not queued or active |
| 1 | `QueuedStart` | selected for activation at the next applicable transition |
| 2 | `CurrentlyActive` | active and applying its authored effects |
| 3 | `Passed` | completed successfully |
| 4 | `Failed` | abandoned or otherwise failed |

The immutable `challenges` world projection reads each entity's state, level, cap, seen/reward
flags, native `IsAvailableToRun`, `IsCompletedOnce`, `IsMaxLevel`, next difficulty, and next base
reward. A second same-frame `challenge decisions` capture reads the ordered preferred selection,
Time offers, prestige offers, selection cap, completion/fetched flags, and rerolls. It is attached
once to the world rather than rebuilt in the MCP worker.

## Selection

`UIChallengeItem.ToggleSelection` (`0x0600224E`) delegates to the preferred
`ChallengeListVariable.Toggle` operation. The action reproduces that decision directly on the
native preferred list after revalidating that the exact target is in the current Time or prestige
offer list, that `HasEmptySpot` permits a new selection, and that
`IsChallengeRestricted(target)` is false. Unselecting an already-selected challenge remains
available even when the list is full or the target would now conflict.

The only success gate is exact membership inversion for the submitted UUID. Selection count,
offer membership, and restrictions are decision evidence; they do not substitute for target
identity.

## Queue and abandon

`UIChallengeItem.ToggleActivate` (`0x0600224C`) calls
`ChallengeSO.ToggleQueueActivation` (`0x06000936`). Only a currently offered target in state
`None` or `QueuedStart` is admitted. Success is the exact target changing `None` to `QueuedStart`
or `QueuedStart` to `None`.

`UIChallengeItem.AbandonActivation` (`0x0600224D`) calls
`ChallengeSO.AbandonChallenge` (`0x06000937`). Only `CurrentlyActive` is admitted. Installed state
metadata and the native callback establish `Failed` as the requested terminal transition, so
success requires state value 4 on the same exact target. Reward, effect, level, and other ledger
observations never gate the action.

## Time and prestige offer refresh

The two UI entry points are intentionally distinct:

- `UITimeScreenManager.FetchNewChallenges` (`0x06002444`) commits first-fetch or reroll state and
  calls `ChallengeManager.LoadNewActiveChallenges` (`0x060004DA`).
- `UIPersistentResetModal.FetchNewChallenges` (`0x060024EE`) commits the same decision and calls
  `PersistentResetManager.FetchNewChallenges` (`0x06000653`).

Installed IL proves both UI methods set `hasFetchedChallenges` on the first fetch or decrement
`challengeRerollsLeft` on later fetches before their native offer callback. The GameAction preserves
that order: all read-only admission completes, the shared family permit is captured, the flag or
reroll is committed, then the appropriate callback runs.

Installed IL also proves both native fetchers call `ChallengeListVariable.CycleOut`
(`0x06001634`) before `Instantiate` (`0x06001631`), and `Instantiate` calls
`ChallengeSO.QueueActivation` (`0x06000935`). Fetch therefore verifies the outcome, not merely a
list counter: the requested Time or prestige offer list must be non-empty and every materialized
offer must be in `QueuedStart`. Ordered offer UUIDs are retained as fault evidence and returned
fully named in the newer world post-state. Fetched flags and reroll values are reported decision
facts, not accounting gates.

Fetch refuses before mutation when the world cycle is incomplete or a subsequent fetch has no
rerolls. It does not require either challenge screen to be rendered; the UI methods contain only
the flag/reroll wrapper around the manager pipeline, and the action calls that audited manager
pipeline on Unity's main thread.

## Boundary ordering, verification, and quarantine

Each submission follows one fixed order:

1. require the captured Unity main thread and an unquarantined lifecycle;
2. require the complete lifecycle binding set;
3. compare the submitted lifecycle epoch;
4. resolve and revalidate the exact `ChallengeSO` when the mode has a target;
5. reread both managers, lists, flags, rerolls, target state, and offer identities;
6. evaluate every mode-specific native precondition;
7. capture the cooperative `ChallengeLifecycle` mutation permit last;
8. commit the one exact native pipeline;
9. reread the same identity and outcome facts and verify only the requested transition.

A missing target transition or an incomplete fetch materialization faults and quarantines this
family for the lifecycle, matching B-001. A native exception after the exact requested outcome is
observable commits. Reroll arithmetic, fetched flags, reward/effect accounting, and unrelated
challenge counters remain evidence and can never overturn the correct identity/outcome.
Lifecycle invalidation discards bindings, registry resolutions, and quarantine state.

## MCP decision and action surface

`world_list(category="challenges")`, `world_get`, and `world_search` expose each named challenge
row plus a shared `challengeState` object. It contains ordered named selected challenges, Time
offers, prestige offers, selection capacity, rerolls, and explicit availability for both fetch
routes. Every row includes `select` and `queue` decisions; an active row additionally includes the
`abandon` decision. Challenge selection has no resource price, so costs and affordability are
truthfully absent rather than zero-filled.

`game_challenge` has five modes:

- `select`, `queue`, and `abandon` require `uuid`;
- `fetch_time` and `fetch_prestige` reject `uuid`.

A committed call waits for a newer immutable world and returns the complete named
`challengeState`; targeted modes also return the complete newer named `challenge` row. There is no
success receipt, payment stanza, request echo, or generation comparison. Refusals before native
work contain the exact named reason. Faults retain decomposed before/after state and ordered offer
identity evidence.

No existing automation owns challenge selection or activation policy. Planner symmetry is not
applicable; the single action and ownership family remain available for a future policy consumer
without creating a second mutation definition.

## Disposable-save promotion checklist

1. Compare the `challenges` rows and shared `challengeState` to both Time and prestige challenge
   screens: names, order, levels, native states, selected entries, cap, rerolls, next difficulty,
   and next reward.
2. Select an offered compatible challenge and verify the returned target and ordered selection;
   unselect it and verify exact membership removal.
3. Exercise selection-full and type-restricted refusals and confirm neither list changes.
4. Queue an idle offered challenge, then unqueue it; compare the exact state to the screen after
   each returned post-state.
5. Attempt queue on a non-offered and a non-idle/non-queued challenge and verify exact refusals.
6. With explicit approval, abandon an active disposable challenge and verify only that exact UUID
   enters Failed while the returned next decisions remain usable.
7. Exercise the world-cycle-incomplete fetch refusal and confirm flags, rerolls, and offers do not
   change.
8. On first Time fetch, verify `hasFetchedChallenges` becomes true, rerolls do not decrease, and
   every returned named offer is queued in native order.
9. On a later Time fetch, verify rerolls decrease once and the complete replacement offer list is
   returned without a read-back.
10. Repeat first/subsequent behavior for the prestige fetch surface and compare the returned list
    to the reset modal.
11. Exercise the zero-reroll refusal and confirm neither manager callback nor offer state changes.
12. Cross a scene/save lifecycle and prove bindings, identities, and any prior quarantine are
    discarded before another challenge action.

No game, save, or live MCP endpoint was touched while producing this dossier. The checklist is the
supervised promotion gate.
