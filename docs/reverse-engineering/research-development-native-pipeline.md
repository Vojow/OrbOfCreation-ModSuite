# Research development native pipeline

## Scope and result

This dossier covers `V-RES-01` through `V-RES-04`: develop or queue research, pause, resume,
cancel, and apply one free bonus level. `ResearchGameAction` is the single lifecycle mutation
boundary used by `game_research`. No production automation owns Research policy, so planner
symmetry is not applicable; a future planner must consume this same action rather than add a
parallel transaction.

The implementation is portable and installed-contract proven, not live promoted. No game, save,
or live MCP endpoint was touched while building it.

## Audited UI and native routes

The shipped v1.0.5 assembly exposes these exact entry points:

- `UIResearchItem.DevelopResearch` (`0x060025F0`) calls `ResearchSO.PurchaseLevel`
  (`0x060011B4`). That method reads `SettingsManager.IsResearchQueueMode` and dispatches to
  `Develop` (`0x060011B5`) or `QueueDevelopment` (`0x060011B6`).
- `UIResearchItem.PauseResearch` (`0x060025F1`) and `ResumeResearch` (`0x060025F2`) call the
  same-named `ResearchSO` methods.
- `UIResearchItem.CancelDevelopment` (`0x060025F7`) calls
  `ResearchSO.CancelDevelopment` (`0x060011B7`).
- `UIResearchItem.AddBonusLevel` (`0x060025F8`) calls
  `ResearchSO.SubmitBonusLevel` (`0x060011BA`). The compiler-generated predicate used by
  `CanApplyBonusLevels` calls `ResearchTypeSO.HasFreeBonusLevelsLeft` for each associated type.

Installed IL fixes the queued route's decision order. It reads native multi-buy, clamps against the
authored maximum, obtains each `GetDevelopmentCostAtLevel`, cumulatively calls
`ResourceCostList.Add`, checks the cumulative `HasEnough`, then checks
`IsWithinDevelopRangeAt`. Only accepted levels are committed through `ApplyResearchCost`; an idle
target immediately starts the first accepted level. Research waiting levels are internal state,
not an entry in the suite's global action-queue categories.

The immediate route sets developing/active state, applies the research cost, recalculates derived
research data, and establishes its drain. Pause/resume change active drain state. Cancel clears
active/developing/waiting state, reapplies the cost calculation, recalculates, and calls
`ResourceFillList.ClearInvestment`. Bonus submission applies one self-bonus level and updates its
research-type usage. Resource, investment, drain, type-usage, effect, and audio changes are native
side effects and reported state, not separate success gates.

## Same-world pre-decision read

The lifecycle-complete `WorldResearchBinder` captures all decision facts on Unity's main thread in
the shared immutable world generation:

- exact identity, visible/available/completed state, purchased/base/bonus/total/current queued
  levels, authored and artificial caps, requirement level and direct adjustments;
- current Research Queue Mode and native multi-buy;
- immediate cost or the accepted prefix of the native per-level cumulative queue-cost loop;
- each cost resource UUID, exact cost, and current canonical spendable amount;
- active/paused progress, required time/stages, and each investment resource's invested, required,
  and remaining values;
- associated research-type UUIDs, remaining free bonus levels, current investment, and maximum
  investment.

The queue evaluator deliberately rebuilds the accepted prefix after probing affordability. Native
`ResourceCostList.Add` mutates the cumulative list before a failed candidate is rejected; returning
that final probe object would overstate the price of the levels the game will actually accept.
Both passes use the same audited native cost methods and never approximate BigDouble math.

The MCP projection renders the current state plus only applicable next verbs. `develop` names
route, accepted levels, maximum batch, affordability, exact named costs and holdings, or one stable
blocker. Active research exposes `cancel`; pause/resume appear only when queue mode is disabled,
matching the UI. Idle eligible research exposes `bonus`. All entity references use the shared live
identity catalog and all magnitudes use the suite's one scientific-string formatter.

## Boundary ordering and verification

Each submission orders:

1. Unity-main-thread check and lifecycle quarantine check;
2. complete lifecycle binding-set availability;
3. exact submitted lifecycle match;
4. stable UUID plus exact `ResearchSO` resolution and freshness check;
5. live capture of route, multi-buy, level/cap/range verdicts, cost affordability, state,
   investment/progress, and free bonus capacity;
6. mode-specific native admission;
7. cooperative `ResearchLifecycle` mutation permit, last;
8. exactly one native callback;
9. same-target outcome verification.

Outcome gates protect the requested action, not its ledger:

- immediate develop: the exact target changes from not developing to developing and active;
- queued develop: the exact target's total queued levels increase;
- pause: the exact target remains developing and becomes inactive;
- resume: the exact target remains developing and becomes active;
- cancel: the exact target is no longer developing and total queued levels are zero;
- bonus: the exact target's self-bonus level increases by one.

Costs, holdings, investment, progress time, raw waiting count, research-type usage, and effect
recalculation are evidence. They cannot fault or downgrade a correct identity/outcome transition.
A missing outcome or a native throw before the outcome is observable faults and quarantines this
family for the lifecycle. A throw after the exact requested outcome is observable commits. Scene,
save-load, reset, and lifecycle transitions discard bindings, cached resolutions, and quarantine.

## MCP shape and refusal evidence

`game_research` requires `mode` and `uuid`; optional `expectedNativeType`, when present, must be
`ResearchSO`. Supported modes are `develop`, `pause`, `resume`, `cancel`, and `bonus`. Success waits
for a newer published world and returns the complete named research row described above. It carries
no receipt, payment stanza, generation comparison, request echo, or follow-up-read requirement.

Before-native refusals name the precise live blocker: absent identity, wrong lifecycle/thread,
unavailable binding/ownership, zero multi-buy, full research cap, native develop/cost/range refusal,
queue-mode pause/resume exclusion, wrong active/paused/idle state, or exhausted bonus capacity.
Post-native faults retain decomposed before/after state and quarantine evidence.

## Contract completeness

The action lifecycle binds 36 contracts as one indivisible set: five owner types; the exact level,
waiting, stage, self-bonus, active/developing, and max fields; every evaluator/state accessor; queue
mode, multi-buy, exact cost accumulation, and all five mutation methods. Withholding any member
produces `contract_unavailable` before identity or mutation.

The world decision reader adds 23 contracts for cost tuples/resources, fill-list entries,
research-type capacity/investment, and current/remaining time. Inherited UUID reads are correctly
declared once against `IdScriptableObject.GetGuid`; derived `ResourceSO` and `ResearchTypeSO` type
contracts remain exact. All 59 rows have installed member-exact coverage and stub equivalents.

## Disposable-save promotion checklist

1. Back up a disposable save and record one eligible idle research UUID/type, queue-mode setting,
   native multi-buy, levels/caps, exact cost holdings, progress/investment, and bonus capacity.
2. Compare its `world_get(category="research", uuids=[...])` row to the rendered Research panel.
3. In immediate mode, call `develop`; verify the returned row is active and its costs/holdings,
   progress, investment, cancel, and pause decisions match the screen.
4. Pause and resume that exact target; verify each terminal response contains the matching state and
   all next decisions without a read-back.
5. Cancel at partial progress; verify idle state, zero total queued levels, cleared investment as
   displayed, and the next develop/bonus decisions.
6. In queue mode with multi-buy greater than one, choose holdings that admit only a strict prefix;
   verify the read predicts that prefix and the committed response reports the same queued outcome.
7. Exercise unaffordable, requirements, leeway, artificial-cap, investment-cap, maximum-level, and
   zero-multi-buy refusals; prove zero native mutation on every refusal.
8. Verify pause/resume are absent from the queue-mode read and direct attempts refuse before native
   work, matching the rendered controls.
9. Apply one bonus level to an idle eligible target; verify exactly one self-bonus increase and the
   returned named research-type remaining capacity.
10. Exercise bonus exhaustion and developing-state bonus refusal; prove levels, capacity, and
    holdings are unchanged.
11. Cross a scene/save lifecycle; prove stale requests refuse and bindings, resolution, and any
    prior quarantine are discarded before a fresh request.
12. Reload the disposable save and compare durable research level, queue/progress, investment, and
    bonus state to the last native state that the game persisted.

Any wrong target/type, unexpected route, missing requested transition, stale post-state, duplicate
terminal response, or failure to recover after lifecycle replacement blocks promotion. Payment,
refund, investment, drain, and type-usage differences are reported for diagnosis but do not
retroactively redefine a correct identity/outcome.
