# Persistent reset native pipeline

## Scope and result

This dossier covers `V-PREST-01`, the irreversible persistent reset commonly called prestige.
`PrestigeGameAction` is the one mutation boundary used by `game_prestige`; no automation feature
owns a second planner or transaction for this player verb.

The portable implementation is not live promotion. The disposable-save checklist below remains
mandatory because the transaction resets broad progression, applies challenge state and rewards,
changes the persistent resource, and reloads the scene.

## Audited entry and transaction

In game v1.0.5:

- `PersistentResetManager.PersistentReset` is token `0x06000651`.
- `PersistentResetManager.PersistentResetLogic` is token `0x06000652`.
- the public method references `UIScreenFlash.FadeIn` and installs
  `PersistentResetLogic` as the animation-complete callback;
- `UIPersistentResetModal.ResetWorldInteractable` reads its two authored `BoolVariable` fields
  named `hasCompleteWorldCycle` and `hasFetchedChallenges`; the reset manager exposes fields with
  the same names and types, but assembly metadata cannot prove the prefab references point to the
  same two assets;
- the private transaction orders `SetupPersistentValues`,
  `GameManager.PersistentResetGameState`, `ChallengeListVariable.ActivateRewards`,
  `ChallengeListVariable.Activate`, `SetPersistentResource`, then
  `GameManager.CleanGame`;
- `CleanGame` reloads the active scene.

The fade is UI presentation, not gameplay admission or transaction identity. A tool request may
arrive with no persistent-reset modal rendered, so the action invokes `PersistentResetLogic`
directly on the Unity main thread. Harmony's existing lifecycle prefix still observes that exact
method entry. This preserves the native transaction while removing an otherwise unfulfillable
render-animation dependency.

## Pre-decision read

The challenge-decision reader captures from the same `PersistentResetManager` in one world frame:

- world-cycle completion and fetched-challenge flags;
- current, projected, and previous persistence values;
- persistent reset count;
- the stable persistent-resource UUID;
- ordered selected, Time-offer, and prestige-offer challenge identities.

`challengeState.prestige` joins the resource UUID to the already-published resource row and exposes
the current spendable amount, capacity only when capped, and at-capacity state. It derives queued
prestige challenges and queued rewards from the same immutable world. `reset.available` is true
only when both native UI admission flags are true. There is no price or affordability concept to
invent for this reset.

## Action ordering and verification

`game_prestige` requires `confirm:true`. The GameAction orders:

1. Unity-main-thread check;
2. lifecycle quarantine and complete-binding availability;
3. exact submitted lifecycle match;
4. live `PersistentResetManager.instance` and exact member capture; these manager flags are the
   screen-independent preflight analogue of the modal's same-named authored flags;
5. world-cycle-complete refusal;
6. challenges-fetched refusal;
7. exclusive `PrestigeLifecycle` mutation permit, last;
8. one call to `PersistentResetLogic`;
9. verification that the suite-observed lifecycle epoch increased by exactly one.

One exact lifecycle replacement is the requested identity/outcome. No replacement or multiple
replacements cannot describe the one requested reset. Reset counters, challenge transitions,
reward application, and resource changes are observations, never accounting gates. A returned
native call with no lifecycle replacement faults and quarantines. Any exception after invocation
also faults and quarantines even when the lifecycle prefix ran, because prefix observation alone
does not prove that the remaining broad transaction or scene reload completed.

On verified success the MCP frame operation waits for a newer published world after the scene
reload. It returns the new scene, `prestigeState`, and `challengeState`; this is the follow-up read's
answer inline, without a receipt or payment stanza. Failure retains the pre-reset admission facts,
reset count, native stage, observed lifecycle, and quarantine flag.

## Contract set

The lifecycle binding set contains the reset-manager, integer, and boolean types; exact singleton,
admission, reset-count fields; boolean/integer readers; and private reset transaction. The shared
world reader additionally binds the persistent resource and three persistence-value fields. Every
touch is represented by a `prestige.*` manifest contract and installed-game coverage. Withholding
any lifecycle member makes the action `contract_unavailable` before native work.

## Disposable-save promotion checklist

1. Back up the disposable save and record scene, reset count, persistence values, persistent
   resource amount, selected challenges, queued prestige challenges, and queued rewards.
2. Confirm `world_list(category="challenges")` matches the persistent-reset modal values and
   reports `challengeState.prestige.reset.available=true`.
3. Verify every queued challenge/reward reference is fully named and matches the UI.
4. Call `game_prestige` with `confirm:false`; verify schema refusal and zero native effect.
5. Call `game_prestige(confirm=true)` once and do not issue a second reset while it is pending.
6. Verify the scene reload occurs and the terminal MCP response arrives exactly once.
7. Verify the response contains the fresh scene, `prestigeState`, and `challengeState` with no
   receipt, payment, or old-world generation alarm.
8. Compare new persistence values and persistent-resource amount to the UI as observations.
9. Verify queued prestige challenges became active and queued rewards were applied in game.
10. Verify `hasCompleteWorldCycle` and fetched-challenge state reset as the new world's UI shows.
11. Verify suite services and MCP reads recover on the fresh lifecycle without stale identities.
12. Reload the disposable save once and verify the same persistent progression and challenge state
    survive the game's ordinary save path.

Any missing scene reload, wrong challenge identity/state, duplicate terminal response, stale-world
response, or failure to recover publication blocks promotion. Numeric accounting differences are
reported but do not retroactively redefine the reset outcome.

The promotion pass must also compare `reset.available` to the rendered modal. That observation is
the required proof that the modal and manager's same-named `BoolVariable` references are the same
authored assets on the shipped prefab; portable metadata proves their shapes, not serialized Unity
reference identity.
