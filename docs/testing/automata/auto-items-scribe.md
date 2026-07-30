# Auto Items and Auto Scribe testing

[Automata test map](README.md) · [Runtime protocol](../runtime-validation.md) ·
[Auto Items plan](../../plans/auto-items.md) · [Auto Scribe plan](../../plans/auto-scribe.md)

Auto Items and Auto Scribe are separate ServiceCycle services with one shared
coverage policy. Auto Scribe prepares the strongest currently affordable Scroll; Auto
Items consumes an adaptive batch through the game's native random target path. Tests must
therefore prove each service independently and also prove convergence across
fresh world publications.

## Evidence basis

| Claim source | What it establishes | What it does not establish |
|---|---|---|
| Installed assembly metadata | Exact types, members, overloads, fields, constructors, and UUID/type relationships used by reflection | Method semantics, visible UI result, or save persistence |
| Retained 2026-07-30 journal | Actual service cadence, action dispositions, native-call counts, mutation evidence, faults, and world-gate behavior for the installed DLL | Which structure visibly changed or whether a later save/load retained it |
| Portable stubs and tests | Deterministic policy, ordering, lifecycle rejection, final revalidation, postconditions, quarantine, and multi-publication convergence | Fidelity beyond the exact native contracts represented by the stubs |
| Product plan and observed game knowledge | Highest same-type Scroll level replaces lower effective coverage; current maximum Scribe level is desired; native targeting owns structure choice; automatic Scribe work belongs to the player | Release-grade proof until the corresponding P5 scenario is observed on a disposable save |

The toxicity resource's inverted headroom representation, exact-zero recovery
boundary, rest boost, single prepared-consumable behavior, and visible
enchantment replacement remain explicit P5 observations. Automated policy
tests assume those audited contracts; a contrary runtime observation changes
the model rather than being normalized away.

## Risk contract

- Identity authority is UUID plus exact native type. Names are diagnostic only.
- Auto Items never writes toxicity or effects. It makes one native submission,
  requests an adaptive Scroll batch bounded by stock, useful targets, toxicity
  headroom, and an internal safety cap, then verifies stock/queue evidence.
  Relics and temporary items remain one-at-a-time.
- Nonzero toxicity does not pause Scrolls or Relics while current headroom
  admits them. Recovery latches only after an otherwise useful item cannot fit,
  and clears only at exact zero toxicity.
- One prepared consumable blocks another native item submission. Healthy
  verified chains resume after a fresh world publication and poll native-busy
  state at no more than 250 ms.
- Auto Scribe re-reads the native maximum starting level and affordability at
  every mutation boundary, probes above that frontier, and crafts the highest
  currently affordable level. Native purchase advances the shared ceiling; an
  unaffordable frontier falls back to useful lower production. It tries
  deficient visible recipes in semantic cheapest-to-most-expensive order and
  fills each unlocked Scroll family to its native carry cap, so stronger future
  Scrolls can replace weaker stock. It reserves same-or-higher stock, queued
  work, and pending use, and never edits persistent player automation.
- Manual one-shot Scribe work is queued supply. Player-owned automatic work is
  external production pressure: it suppresses competing production but remains
  reported separately.
- Unknown coverage, changed identity, stale lifecycle, lost ownership, lost
  dependency health, target disappearance, or ambiguous mutation fails closed.

## Test pyramid

| Layer | Purpose | Required evidence |
|---|---|---|
| P0: pure policy and identity | Fast exhaustive rules, ordering, levels, allowlists, toxicity admission, coverage math | `ScrollCoveragePlannerTests`, identity catalog tests, picker tests, Auto Items profile/evaluator cases |
| P1: collector and native boundary components | Exact publication shapes, final main-thread revalidation/postconditions, and semantic picker behavior | `GameWorldCollectorTests`, native-adapter tests, and `AutoItemsScribePickerViewTests` |
| P2: service and cross-feature integration | Receipts, wake policy, action ownership, lifecycle/status state, and craft → queue → stock → pending use → applied coverage convergence | worker/type-safety tests, `AutoItemsScribeFeatureRuntimeTests`, `ActionFamilyIntegrationTests`, and the convergence scenario in `ScrollCoveragePlannerTests` |
| P3: installed assembly contracts | Every reflected field, method, declaring type, constructor, target selector, queue, and levelled inventory contract | `AutoItems_MatchesReadOnlyWorldPublicationContracts` and `AutoScribe_MatchesOneShotQueueAndTargetPreflightContracts` |
| P4: retained journal and trace acceptance | Real scheduler cadence, disposition, native-call, mutation, world-gate, and fault evidence | decoded OSJD report plus an optional correlated full trace/profile |
| P5: Unity UAT | Actual target choice, visible enchantment replacement, toxicity/rest behavior, queues, controls, saves, lifecycle, and manual races | disposable-save scenarios below |

Portable success stops at P2. Installed contracts do not prove runtime behavior.
Journal evidence does not prove the visible target or save result. No layer may
be substituted for the one above it.

## Current coverage ledger

| Layer | Current state | Remaining evidence |
|---|---|---|
| P0 | **Pass** | Extend only when policy changes |
| P1 | **Pass** | Extend when another reflected mutation path is added |
| P2 | **Pass** | Retain the full convergence scenario and feature-ownership regressions |
| P3 | **Pass on `steam-windows-2026-07-29`** | Re-run for every new assembly pair |
| P4 | **Historical evidence captured; post-fix acceptance open** | Install only with authorization, then prove corrected cadence, zero Auto Items mapper faults, and fast healthy Scroll chains |
| P5 | **Open** | Complete and record the disposable-save scenarios below |

“Automated complete” means P0-P3 pass together. It is not a release claim:
P4 and every affected P5 scenario remain mandatory.

The current Release coverage run passes the repository production floor at
74.53% line coverage (minimum 73.40%); branch coverage is 63.04% diagnostic.
The picker views are covered through rendered controls and event callbacks,
not through exclusions or a reduced threshold.

## Dump-derived regression map

The retained 2026-07-30 run produced five concrete regression classes:

| Observed evidence | Regression requirement |
|---|---|
| Auto Items had 240 faulted actions and zero native calls | `TargetUnavailable` is an expected rejection, and a pre-call rejection never carries mutation evidence |
| Auto Scribe produced 60,423 rejected decisions | rejected work returns the configured cadence rather than an immediate proposal loop |
| Auto Scribe completed 164/164 mutations with 492 native calls | one healthy queue action commits one mutation with the expected three-call evidence |
| Auto Items waited a full idle interval between healthy Scrolls | a committed Scroll chain continues immediately after fresh publication; native-busy polling is bounded at 250 ms |
| A Scroll backlog outgrew serial single-item submissions | one native submission reserves the largest safe batch, restores the player's multi-buy setting, and projects the requested quantity into the journal |
| Newly unlocked Scroll recipes and higher Scribe levels were missed | invisible recipes are dormant rather than degraded; every fresh publication reopens newly visible roles, while the native boundary probes above the current maximum and cheapest-first selection drives ceiling progression |
| Lifecycle readiness and the shared host were previously unavailable | stale epoch, lost ownership, lost Scroll consumption, and lost capture permit all reject before a native call |

These are permanent regression cases. A future dump can add cases but cannot
remove these without an explicit behavior change.

## Focused commands

```powershell
dotnet test tests/OrbModding.Tests/OrbModding.Tests.csproj `
  -p:UseGameStubs=true `
  --filter "FullyQualifiedName~AutoItems|FullyQualifiedName~AutoScribe|FullyQualifiedName~ScrollCoveragePlanner"
```

Then run the complete portable gate:

```bash
./script/test
```

For an installed game:

```powershell
$env:OOC_GAME_DIR = "C:\path\to\Orb of Creation"
dotnet test tests/OrbModding.GameContractTests/OrbModding.GameContractTests.csproj -c Release
dotnet build src/OrbModSuite.csproj -c Release -p:RequireGameReferences=true -p:UseGameStubs=false
```

## P0/P1 required case matrix

Auto Items must cover:

- Scroll, Relic, Fruit, Potion, Thread, unknown family, and ambiguous family;
- zero, nonzero-with-headroom, exact-fit, saturated, partially recovered, exact
  zero recovered, invalid capacity, and cost larger than full capacity;
- Relic priority at zero and nonzero toxicity;
- native-random Scroll requirement and no-candidate rejection;
- adaptive Scroll batch limits for stock, useful targets, toxicity headroom,
  and the internal cap; multi-buy restoration and batch-size projection;
- exact temporary allowlist, family switch without allowlist, pending,
  engagement, expiry, no refresh, and exact-item quarantine;
- disabled mode, emergency/lifecycle permit loss, native busy, manual stock
  race, target race, changed family/cost, and ambiguous postcondition;
- committed chain, rejected cooldown, fresh-world gate, and bounded busy poll.
- picker expansion, filtering, exact selection and unavailable-selection
  removal, per-item ambiguous-family isolation, non-blocking notices, defaults,
  raw editing, and status/rebuild callbacks.

Auto Scribe must cover:

- every facade role, all six producible roles, and coverage-only roles;
- missing, lower, equal, and higher enchantment levels;
- missing, lower, equal, higher, queued, pending-use, expired, and surplus stock;
- manual one-shot queue reservation versus external automatic production;
- largest-deficit then stable-role selection, disabled roles, no starvation;
- target level increase, structure appearance, zero targets, incomplete target
  evidence, recipe mismatch, and unknown baseline;
- gradual recipe unlock, locked-role dormancy, native carry-cap stock fill,
  lower-stock replacement pressure, and highest-affordable-level fallback;
- queue and instant-craft success, stale epoch, dependency loss, full queue,
  live supply race, live target race, native refusal, and quarantine recovery.
- feature-runtime status after evaluation, emergency stop, ownership loss,
  Auto Items dependency loss, lifecycle replacement, identity-profile
  unavailability, and mutation quarantine.

## P4 journal acceptance

Decode the retained journal:

```bash
./script/trace --journal --input "<BepInEx config>/OrbOfCreation-ModSuite/trace/journal"
```

For an isolated healthy Auto Items/Auto Scribe window:

- terminal faulted, orphaned, and fault-bearing observations are zero;
- every committed action has one committed mutation;
- pre-call rejections report zero native calls and zero mutation attempts;
- a healthy Auto Scribe commit reports the audited three-call mutation evidence;
- world-gate holds may follow commits and are expected freshness protection, but
  must resolve to later decisions;
- repeated rejections occur no faster than the configured feature interval;
- a Scroll backlog with ample headroom shows successive commits separated only
  by native preparation and fresh publication, not by the idle interval;
- the Auto Items projection reports the requested batch quantity, and each
  batch remains within observed stock, useful-target count, toxicity headroom,
  and the safety cap;
- unlocking another Scroll role or increasing the native Scribe maximum changes
  the next fresh-world decision without a configuration toggle or lifecycle
  restart.

The compact journal carries numeric service IDs. Use a correlated dashboard or
the runtime roster to attribute them; never permanently assume ordinal 3 or 4.
Retained rolling evidence may begin mid-run, so absence before the first retained
record is unknown rather than success.

## P5 disposable-save scenarios

1. **Scroll backlog:** start with several useful Scrolls and ample toxicity
   headroom. Confirm one submission reserves a safe multi-item batch and the
   game drains it serially until stock is cleared, coverage completes, or actual
   saturation occurs. Confirm the player's multi-buy value is restored.
2. **Recovery latch:** fill until the next useful item cannot fit. Partial
   recovery must not resume; exact zero must resume. An item larger than full
   capacity must not create an unreachable wait.
3. **Relics:** confirm priority at zero and at nonzero toxicity when headroom
   fits. Confirm insufficient headroom falls through to a cheaper useful Scroll.
4. **Coverage convergence and reserve:** observe missing coverage, one queued
   highest-affordable craft, stock, pending Scroll use, native enchantment
   replacement, and continued production until that Scroll family's native
   carry cap is filled.
5. **Unlock, level, and target changes:** unlock all Scroll recipes gradually,
   then unlock a higher Scribe level and a new valid structure while active.
   Each must alter the next fresh-world plan without a restart; stronger crafts
   must replace weaker carried stock.
6. **Player activity:** queue manual Scribe work, enable native Auto Scribe, use
   a Scroll manually, cancel targeting, and consume stock immediately before
   the suite boundary. The suite must reject or account without duplicating.
7. **Temporary families:** separately allowlist one Fruit, Potion, and Thread;
   verify pending, engagement, no stacking/refresh, expiry, and resumed fill.
8. **Lifecycle and control:** emergency stop, save/load, title/load, reset, NG+,
   scene replacement, shutdown/restart, quick-control rebuild, and configuration
   Apply/Revert must produce no stale mutation.

Record the exact commit, DLL hash, game assembly hashes, configuration, save
backup, journal run identity, and visible outcome. A release claim requires all
affected P5 scenarios, not only a clean log.
