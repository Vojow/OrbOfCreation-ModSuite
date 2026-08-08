# Ritual lifecycle native pipeline

This dossier audits the player-visible Ritual list controls in Orb of Creation v1.0.5. It covers
selection, starting-level staging, battle activation and ending, and cancellation of an active duration
reward. Composed Ritual discovery belongs to the shared Devote surface documented for
`game_discover`; ordinary Alchemy and Concept assignment are separate capability families.

## Audited surface and dead predecessor

The pinned assembly is
`artifacts/game-v105/Orb Of Creation_Data/Managed/Assembly-CSharp.dll`. The player-visible Ritual
list is `UIRitualList`/`UIRitual`, not the older runestone-selection prototype:

- `RitualManager.SelectRitualRuneStones(Guid)` (`0x060006BD`) has an empty body.
- `RitualManager.GetRitualFromStones(List<RuneStoneSO>)` (`0x060006C1`) returns `null`.
- Current Ritual discovery is a `UIDiscoverablePage` compose/preview/confirm surface and remains
  `game_discover(surface="devote")`. The lifecycle tool does not expose the dead runestone path.

## Selection and starting level

`UIRitualList.ClickRitual(RitualSO)` (`0x06002658`) raises the optional UUID event, then calls
`RitualVariable.ToggleValue(RitualSO)`. This is one toggle: selecting an unselected ritual stages
it, and clicking the selected ritual again clears it.

`UIRitual.SetJumpStart(int)` (`0x0600264E`) calls
`RitualSO.ChangeStartingLevel(int)` (`0x06001367`). The native method clamps the requested level to
`0..GetMaxSelectedLevel()`, writes `selectedLevel`, and rebuilds the completion-fill cost for that
level. `GetMaxSelectedLevel()` (`0x06001393`) is
`max(reachedLevel + 1, Player.GetCeremonialLevel())`. A `forceLevel` ritual displays the forced
value and disables the selector, so the action refuses rather than pretending the dial moved.

`GetSelectedCompletionCost()` (`0x06001383`) derives a fresh nominal list from the authored base,
the per-level modifier scaled by the selected level, and `completionCostMod`. The world capture
reads those authored cost tuples and modifier-program records once; the published answer is then
derived off-thread by the same owned modifier math used elsewhere. It does not read the staged
`ResourceFillList`: that UI-owned cache can still describe the previous selection immediately
after a toggle and produced two prices for one staged state.

The visible value is the amount actually removed from the resource, not the nominal ledger input.
`UIResourceCost.GetText()` calls `ResourceSO.GetTrueSpend()`, which divides the nominal cost by
`quality.AsPercent()`. `ResourceCostList.PerformCost()` ultimately takes the same path through
`ResourceSO.Spend()`. MCP ritual completion prices therefore apply that audited player-facing
conversion after the owned nominal formula, exactly once.

The outcome sentinels are the selected variable's membership and the game-written
`RitualSO.selectedLevel`. No cost or fill-list delta is verified.

## Activation

`UIRitual.RenderActivationButton()` (`0x0600264C`) publishes
`RitualSO.GetActivationCost()` (`0x06001382`) and `GetActivationCostState()` (`0x06001385`) on the
visible button. `UICostButton.OnClick()` (`0x06002204`) checks the displayed error and
`ResourceCostList.HasEnough()`, calls `PerformCost()`, then invokes its callback. The suite repeats
that ordering with live native facts and payment last.

The callback is `RitualManager.ActivateSelectedRitual()` (`0x060006BF`). It reads the selected
ritual and calls `RitualManager.StartRitual(RitualSO)` (`0x060006C0`), which calls
`BattleManager.StartRitual(RitualSO)` (`0x0600048E`). The battle manager establishes the battle
view and active ritual, calls `RitualSO.Initiate()` (`0x0600136C`), then summons the first enemies.
The simplest game-written outcome is `RitualSO.inBattle == true`; resource balances and ritual
counters are not postcondition gates.

## Duration cancellation

`UIRitual.RenderContent()` displays the Cancel button only when both
`RitualSO.IsDurationRitual()` (`0x0600138E`) and `IsDurationActive()` (`0x0600138D`) are true.
`UIRitual.CancelRitual()` (`0x06002653`) calls `RitualSO.Cancel()` (`0x06001369`). That method drains
the `ritualInstances` list, ends each duration effect, and republishes the active-ritual count.
It does **not** cancel an in-progress battle. The MCP verb is therefore named `cancel_duration`,
and its sentinel is the game-written duration-active predicate becoming false.

## Ending an active battle

The battle screen's **End Ritual** control invokes `BattleManager.EndRitual()` directly. The
method closes the battle view, ends the active `RitualSO`, opens the results modal unless that
ritual hides it, and finally clears `BattleManager.activeRitual`. Native
`BattleManager.IsInCombat()` is exactly `activeRitual.HasValue()`, so the action admits only the
UUID currently stored in that variable and verifies the single game-written outcome
`IsInCombat() == false`. The results modal is a native consequence, not a second postcondition.

What that modal shows is nevertheless the game's own record of the run, and both halves of it are
readable state rather than a second sentinel. `RitualSO.IsFailedRun()` is `wavesCompleted < 5` — a
hard five, not `GetRequiredWaves()` — and `RitualSO.End()` reads it before clearing `inBattle`,
branching to the negative popup and `RefundActivationCost()`. The banked resources are
`RitualSO.currentSpoils`, a `List<SpoilsRecordEntry>` whose entries expose a `resource` property and
a `quantity` field, enumerated for display by `GetSpoilsNodes()`. World collection reads both, so
`end` publishes the verdict and the spoils from the settled world rather than from the waves count.

## Preconditions and risk

Every mutation resolves UUID plus exact `RitualSO`, requires discovery, a current lifecycle, the
Unity main thread, and live `RitualManager` and `BattleManager` instances. Ordinary list controls
require no active battle; `end` instead requires the named ritual to be the active battle. Level
staging additionally requires selection, an unlocked dial, and an in-range level.
Activation revalidates selection and the exact native activation cost immediately before payment.
Duration cancellation requires the exact active-duration predicates.

Selection and level staging are reversible UI-backed state changes. Activation spends resources
and enters combat; ending a battle finalizes its outcome and can open results, so both are
high-risk transitions. Duration cancellation ends ongoing rewards and
is irreversible for that effect instance. Wrong identity/type or an absent requested transition
fails closed; no accounting receipt or refund inference is assembled.

## Disposable-save live checklist

1. On a copied save, list discovered Ritual rows and compare selected state, reached/selected/max
   levels, and duration state with the Ritual screen.
2. Select one idle discovered ritual; verify the screen highlight and MCP settled selection agree.
3. Change its starting level inside the published range; verify the screen dial, activation price,
   and completion price agree with the settled row.
4. Attempt an out-of-range and a forced-level change; verify refusal occurs before native mutation.
5. Attempt activation while unaffordable; verify the refusal names the short resource and neither
   battle nor balances change.
6. Activate an affordable ritual; verify the payment shown on screen, battle entry, enemy wave,
   and settled `inBattle` transition. Payment is observed live but is not a verifier gate.
7. While the battle is active, call `end` with another ritual and verify it refuses; then call it
   with the active ritual and verify the battle ends, results open, settled `inBattle` becomes
   false, and the reported verdict and spoils match the results modal.
8. Complete a duration ritual, confirm its Cancel button is visible, call `cancel_duration`, and
   verify the effect disappears and the settled duration state becomes inactive.
9. Attempt `cancel_duration` on a non-duration or inactive ritual and verify an ordinary refusal.
10. Cross a lifecycle boundary and verify any stale request refuses without a native call.
