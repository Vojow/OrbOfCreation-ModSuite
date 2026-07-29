# Auto Agromancy migration

Status: **Automated and installed validation complete — interactive runtime validation pending**

## Goal

Bring Auto Agromancy's native level-balancing behavior onto the current ModSuite
architecture without restoring the retired monolithic Automata service registry.
The feature is integrated as a disabled-by-default ServiceCycle service. The
current installed assembly pair is admitted by current `main`, and the Release
candidate has passed the portable, real-reference, and installed-contract gates.
Auto Agromancy-specific Unity runtime evidence remains tracked separately in
AA-020c/AA-100.

## Preserved behavior contract

- Search every native level from 1 through `GetMaximumInstances()` and select
  the highest level sustainable against live true resource rates.
- Add the selected pair's observed drain contribution back before evaluating a
  rebalance.
- Include `actionCost` plus a positive element/internal-resource cost.
- Apply the native level drain modifier and `ResourceSO.GetTrueSpend`.
- Accept an exact zero remaining rate.
- Support non-monotonic native costs with a bounded full search.
- Mutate only through native `AddInstance` or `ChangeInstance`, after lifecycle
  and action-family revalidation, and verify exact identity and level.
- Fail closed on missing, invalid, changing, or unaudited native facts.
- Cap an exact search at 4,096 levels and 25 ms.

## Adapted on top of current `main`

- `src/AutoAgromancy/Policy/AutoAgromancyLevelPlanner.cs` is the native-free,
  bounded planner.
- `src/AutoAgromancy/Native/AutoAgromancyNativeAdapter.cs` retains the audited
  reflection contract and verified main-thread mutation boundary.
- Portable planner and native-adapter tests were carried forward under
  `tests/OrbModding.Tests/Services/AutoAgromancy/`.
- The sources keep the existing `OrbAutomata` namespace, matching the other
  feature folders after the suite source split.

The former `AutoAgromancyController`, legacy `IAutomataService` registration,
old `AutomataConfiguration`, and PR-96-era plugin wiring were deliberately not
carried forward. Those types no longer exist on `main`, and reviving them would
create a second scheduler/configuration path.

## Decisions for the current port

These decisions keep the first port smaller than the earlier branch and avoid
mixing a runtime migration with a configuration redesign.

1. **Auto Harvest remains separate.** Auto Agromancy receives an independent
   `Disabled`/`Active` mode. The existing Auto Harvest mode, selectors, button,
   status, and documentation do not move.
2. **No gameplay control in the first port.** Auto Agromancy is configured from
   Mods settings only. A quick button or shortcut is a later UX feature.
3. **One level mutation per accepted frame.** Rebalancing every active pair is
   sliced across fresh world generations. This prevents one stale rate snapshot
   from authorizing several resource-drain changes.
4. **Registration order is explicit:** world collection, Auto Harvest, Auto
   Agromancy, Auto Buy, Spell Leveling, Auto Cast, Auto Concept, and Mentor.
   A verified Auto Harvest submission can only affect a world snapshot collected on a
   later frame; placing the services beside one another makes that sequencing
   visible without claiming same-frame freshness.
5. **The first port does not require a schema bump.** Adding a new key with a
   disabled default is additive. `SuiteConfigurationSchema` stays at version 5
   unless evidence is found that a released suite version wrote settings that
   must be transformed. Experimental branch configuration is not migration
   evidence.
6. **Activation is the final portable slice.** The public BepInEx binding was
   added only after the ServiceCycle runtime, ownership, action boundary, and
   diagnostics were composed; it defaults to `Disabled`.

## Execution tracker

This document is the migration's source of truth. Update the table and progress
log in the same change as implementation:

- `Done` means the task's exit evidence ran against that exact tree.
- `In progress` means active code or audit work exists but the exit evidence is
  incomplete.
- `Blocked` names the task or external evidence required to continue.
- `Pending` means no implementation claim is made.

| Task | Status | Depends on | Deliverable and exit evidence |
|---|---|---|---|
| AA-000 Adapt branch to current main | Done | — | Reusable planner/native adapter moved under `src/AutoAgromancy`; 20 focused tests and the complete portable gate pass. |
| AA-010 Audit native contracts | Done | AA-000 | Exact installed types/members are manifest-owned and source-audited; current `main` admits the installed Windows pair and 24/24 installed contracts pass. |
| AA-020 Prove native-free cost parity | In progress | AA-010 | Immutable model matches the native-call oracle across scaling, quality, internal-resource, non-monotonic, and invalid-number cases. |
| AA-030 Design world publications | Done | AA-020 | Flat row/table design and explicit capture state have bounded atomic collection/failure semantics; level×resource expansion is rejected. |
| AA-040 Publish active Druidry facts | Done | AA-020, AA-030 | Shared world source publishes bounded atomic active-pair, ordered cost, scaling, rate, and trigger facts; collector and full portable gates pass. |
| AA-050 Implement pure worker domain | Done | AA-040 | Typed observation/sweep state and evaluator plan at most one mutation; direct increases remain pending until accepted or authoritatively removed. |
| AA-060 Implement live action boundary | Done | AA-020, AA-050 | Stable identity, live config/lifecycle/ownership/fingerprint revalidation, exact mutation, postcondition, rollback, and quarantine are composed behind the action port. |
| AA-070 Implement trigger producers | Done | AA-040, AA-050 | Exact authoritative plot-list increases and verified Auto Harvest commits publish monotonic epochs; no-op, wrong-list, and decrease paths are covered. |
| AA-080 Compose runtime and diagnostics | Done | AA-050, AA-060, AA-070 | Independent ownership, bounded-one registration, lifecycle invalidation, feature status, diagnostics bridge, ordering, and shutdown composition are active. |
| AA-090 Activate configuration and docs | Done | AA-080 | Disabled-default setting is reachable through Mods/BepInEx without a schema bump or Auto Harvest mode change; behavior/testing docs are linked. |
| AA-100 Validate installed runtime | In progress | AA-090, admitted native build | Final Release candidate `37A7363D…12D1F2` is installed with verified backups; startup and Auto Agromancy-specific Unity scenarios remain pending because the game was deliberately not launched unattended. |

### Progress log

- **2026-07-29 — AA-000 complete.** Rebased the feature commit onto current
  `main`, retained only the reusable planner/native boundary and their tests,
  removed the retired scheduler/configuration wiring, and passed 1,887 portable
  plus 90 profile tests.
- **2026-07-29 — AA-010/AA-020/AA-030 started.** Native contract, exact cost
  representation, and shared-world publication audits are running in parallel.
  No gameplay path or public setting is enabled.
- **2026-07-29 — AA-030 scheduling constraint confirmed.** ServiceCycle has no
  external per-service wake API. Plot and verified-harvest epochs will be
  published as world facts and coalesced by worker state. A pending sweep uses
  the service's normal immediate follow-up policy between fresh world
  generations; no feature-owned poller, event subscription, or second
  scheduler will be added. Same-frame wake semantics are explicitly outside
  this migration.
- **2026-07-29 — AA-010 audit complete, installed build not admitted.** The
  installed Windows assembly pair
  (`436210E6…F7AA4C` / `D14D5265…480A`) is not an admitted suite baseline.
  Its inspected Druidry scaling and true-spend IL still matches the compact
  formula below, but it must remain refused until the repository's full
  re-audit process admits it. The audit also found missing manifest coverage
  for the active-list, instance, action-reference, instance-scaling, and exact
  mutation members; those entries belong to AA-040/AA-060, before the
  respective reflected readers are made reachable.
- **2026-07-29 — AA-020 portable core implemented.** Added exact native-free
  resource spend math, Druidry action/element/instance scaling math, and a
  compact all-level planner. The model preserves base-cost tuple order and
  uses stable resource UUIDs, four resolved record values, and flat authored
  cost/speed modifier rows. Twenty-six focused tests pass, including quality,
  duplicate/internal costs, exact zero, non-monotonic scaling, invalid values,
  and the 4,096-level bound. Differential fixtures against an admitted native
  build and the complete gate remain before AA-020 is `Done`.
- **2026-07-29 — AA-030 publication design complete.** Publish composite pair
  rows, ordered base/current-cost rows, four resolved scaling values plus flat
  modifier rows, and an explicit capture-state scalar. Empty tables must never
  ambiguously mean both “no active pairs” and “capture failed.” Resolve
  resources across normal and element-owned resource tables and fail only the
  Auto Agromancy publication atomically.
- **2026-07-29 — portable gate green after the first implementation slice.**
  `./script/test` passed on this exact worktree: 1,913 portable tests and 90
  profile tests, with the production-shaped stub build succeeding without
  warnings. AA-020 remains `In progress` because an admitted native-call
  differential result is still required; the passing portable gate does not
  promote the unknown installed assembly pair.
- **2026-07-29 — AA-020c/AA-040a contract gate advanced.** Added portable
  compact-vs-carried-oracle parity fixtures for raw scaling,
  quality/full-speed drain, and non-monotonic inputs. Extended the source audit
  to recognize the adapter's exact custom reflection selectors, removed its
  presentation-only sound/flash reflection, generated the
  `ActiveHarvestActions` identity, and declared the remaining gameplay oracle
  contracts. The unknown installed Windows build passes the exact declared
  contract sweep but is deliberately not added as an audited baseline.
- **2026-07-29 — AA-040 through AA-090 implemented.** Added the exact
  `ActiveHarvestActions` reader and atomic publication, native-free
  observation/sweep worker, exact live mutation boundary, monotonic plot and
  verified-harvest triggers, independent level-adjustment ownership, ordinary
  bounded-one ServiceCycle registration, diagnostics/status projection, and a
  disabled-default `AutoAgromancy.Mode` binding. The feature is documented as
  configuration-only and has no gameplay quick button.
- **2026-07-29 — final portable gate green.** The current worktree passed 1,928
  ordinary portable tests, 90 profile tests, and the profiler-enabled trace
  tool build. Focused migration integration and pure-worker suites also passed
  6/6 each. A detected direct increase remains pending across drift rejection
  until a balanced level is observed or removal refreshes the baseline; newer
  trigger epochs coalesce without restarting an active sweep, and pair-count
  changes safely restart its bounded cursor.
- **2026-07-29 — installed validation remains blocked, not skipped.** The
  installed Windows assembly pair still has no admitted baseline. No DLL was
  installed and no runtime success is claimed. AA-020c and AA-100 remain open
  until that build is explicitly re-audited and the documented Unity
  scenarios/native-call differential are run.
- **2026-07-29 — current Steam build passed the unstamped headless re-audit.**
  Steam app 1910680 build ID `24426975`, Unity `6000.0.70f1`, and assembly pair
  `436210E6…F7AA4C` / `D14D5265…7F480A` passed the real-reference build, the
  complete portable/profile gate, and all 23 non-baseline installed metadata
  contracts. After integrating the actual current `main`, the same exact pair
  is an admitted baseline. The merged contract gate exposed and removed one
  stale `src/OrbAutomata` source-audit root; no native member mismatch remained.
- **2026-07-29 — current-main automated gates green.** On the merged worktree,
  the focused Auto Agromancy suite passed 42/42, `./script/test` passed 1,936
  ordinary portable tests and 90 profile tests, the profiler-enabled trace
  build completed with zero warnings, and the admitted installed-game contract
  suite passed 24/24. The Release real-reference build completed with zero
  warnings.
- **2026-07-29 — Release candidate installed with verified backups.** With the
  game closed, `./script/install release` reran the complete gates, copied 12
  save files to
  `backups/pre-modsuite-install-20260729T123949Z`, preserved the previous DLL
  under
  `BepInEx/modsuite-backups/pre-modsuite-install-20260729T123949Z`, and
  installed `OrbModSuite.dll` SHA-256
  `66948E8E3E35D1B08BCDEDB9B49AE6940D1931DE7B7E68986D55D14C3553A303`.
  The installer was made compatible with Git for Windows by accepting
  `sha256sum` and Windows PowerShell process checks when `shasum`/`pgrep` are
  absent.
- **2026-07-29 — runtime evidence boundary recorded.** The existing game log
  for the same admitted assembly pair contains a complete shared suite
  differential block from ModSuite 0.4.0: global resolution 5/5,
  cost-per-quantity 180/180, published costs 744/744, affordability 409/409,
  accessors 960/960, modifiers 5,677/5,677, cost verification 522/522, rates
  640/640, upgrade requirements 229/229, and structure requirements 180/180.
  This proves the shared game-math baseline, not the newly installed Auto
  Agromancy wiring. No current-candidate startup or mutation claim is made.
- **2026-07-29 — review gaps closed and final candidate installed.** The
  service now advances a sweep only after observing its committed target,
  restarts same-count sweeps when pair identities change, preserves exact
  zero-level membership through `ChangeInstance`, accounts for both forward
  and rollback attempts, and revalidates through an Auto Agromancy-scoped
  four-category live read. Diagnostics consume the service's own projection
  and fault evidence; emergency-resume and trace rosters name the feature.
  Exact action/element GUID checks and the plot-trigger Harmony/reflection
  contracts are action-place audited, while the source audit covers custom
  exact-method, collection, and reference selectors.
- **2026-07-29 — final automated evidence green.** The exact installed tree
  passed 1,950 ordinary portable tests, 90 profile tests, the
  profiler-enabled build with zero warnings, 24/24 installed-game contracts,
  and the real-reference Release build with zero warnings. The admitted Steam
  build remains `24426975` with assembly pair
  `436210E6…F7AA4C` / `D14D5265…7F480A`.
- **2026-07-29 — guarded final installation verified.** With the game closed,
  the installer copied 12 save files to
  `backups/pre-modsuite-install-20260729T131155Z`, preserved the previous DLL
  under
  `BepInEx/modsuite-backups/pre-modsuite-install-20260729T131155Z`, and
  installed the sole `OrbModSuite.dll` with SHA-256
  `37A7363D750B32D07F1E08B7E60D7812FFF7B5295393CFC6060E5864B012D1F2`.
  The built and installed hashes match exactly and the game remains closed.

**Next runnable slice:** when the user is present, launch the game with a
disposable validation save, run **Mods > Runtime > Run differential
verification**, then execute the ten documented Auto Agromancy Unity scenarios.
The game was intentionally not launched or driven unattended.

### Runnable task slices

These are independently reviewable changes. A later slice does not become
reachable merely because an earlier internal type exists.

| Slice | Status | Depends on | Scope |
|---|---|---|---|
| AA-020a Spend/scaling math | Done | AA-010 | Exact `AsPercent`, true-spend, action/element combine, instance cost/speed, and full-speed drain math. |
| AA-020b Compact planner | Done | AA-020a | Stable-resource, ordered-cost, native-free exact scan through level 4,096. |
| AA-020c Differential evidence | In progress | AA-020b | Portable compact-vs-carried-oracle fixtures pass; an admitted native-build differential remains required. |
| AA-040a Known identity and contracts | Done | AA-020c | `ActiveHarvestActions` is generated; active-list, instance, cost, scaling, modifier, resource, and exact mutation contracts are manifest-owned and source-audited. |
| AA-040b World row types | Done | AA-040a | Composite pair, ordered cost, resolved scaling/modifier rows, trigger epochs, and explicit capture state are immutable publications. |
| AA-040c Atomic world reader | Done | AA-040b | The reader re-resolves each pass, caps pairs/costs/modifiers, publishes together, and closes only Auto Agromancy facts on failure. |
| AA-040d World verification | Done | AA-040c | Collector, structural publication, identity-walk, known-entity, and full portable gates pass. |
| AA-050a Internal config/domain | Done | AA-040d | Disabled-default config plus typed action, fingerprint, state, decision, and result-code shapes. |
| AA-050b Observation/sweep state | Done | AA-050a | Prior levels, monotonic trigger epochs, deterministic cursor, one planned pair, no queued native objects or work. |
| AA-050c Evaluator/service/projection | Done | AA-050b | Native-free evaluator, bounded dispatch, idle/immediate wake policy, and numeric projection are registered. |
| AA-060a Live resolver/fingerprint | Done | AA-050c | Exact immediate four-category world re-read and captured-fact fingerprinting keep planning off the Unity thread without rescanning unrelated categories. |
| AA-060b Transaction and quarantine | Done | AA-060a | Exact apply/postcondition, safety rollback, attempted-unverified quarantine, and stable result codes are implemented. |
| AA-060c Action adapter guards | Done | AA-060b | Live config generation, lifecycle, ownership and mutation permits, exact identity/type, observed level, maximum, visibility, and fingerprint are revalidated. |
| AA-070a Epoch source | Done | AA-040d | Atomic monotonic plot and verified-harvest-submission counters are published through world facts. |
| AA-070b Exact producers | Done | AA-070a | The exact plot `AddInstance` Harmony hook and verified Auto Harvest callback advance their respective epochs only after proven commits. |
| AA-080a Ownership/composition | Done | AA-050c, AA-060c, AA-070b | Independent level-adjustment family, service runtime, explicit registration order, lifecycle invalidation, and shutdown disposal are composed. |
| AA-080b Diagnostics/integration | Done | AA-080a | ID-based diagnostics bridge, roster/status surfaces, ownership integration, and full portable/profile gates pass. |
| AA-090a Atomic activation | Done | AA-080b | Public disabled-default BepInEx setting, reader/status/Mods wiring, and schema-5 compatibility are active. |
| AA-090b Reachability docs | Done | AA-090a | User, runtime architecture, Automata behavior, and feature testing docs describe the reachable configuration-only feature. |

AA-040a's scaling contract includes the raw-input graph selected by AA-020:
`HarvestActionSO.costMod`, `speed`, and `instanceScaling`;
`InstanceScalingRef.scaling`; `InstanceScalingSO.instanceScaling`; the exact
cost/speed `ScalingConversion` access; and the flattened `ValueModifierList`
entries. This larger audited surface is the deliberate trade for removing up
to 4,096 main-thread `GetScalingInfo` calls per active pair.

## Critical architecture gate

The old adapter both read native cost facts and chose a level on the Unity main
thread. That is not a valid ServiceCycle ordinary service. The current runtime
requires:

```text
Unity source collection
  -> immutable native-free world facts
  -> worker chooses at most one target level
  -> Unity action boundary revalidates the exact live pair and facts
  -> native mutation
  -> exact postcondition verification
```

The port must therefore represent the native Druidry cost calculation as
immutable world facts that the worker can evaluate exactly. Calling
`BalanceActive` or `BalanceActiveSelection` from an action adapter would hide
planning on the Unity thread and is not an acceptable shortcut.

If the installed-game audit cannot express the native level scaling and true
spend inputs as bounded immutable facts, stop at Step 1. Do not add a second
scheduler, perform worker access to native objects, or make main-thread planning
the fallback.

## Step-by-step implementation

Each step is intended to be a reviewable work slice. The complete portable gate
must pass after every slice.

### Step 0 — Freeze the contract and baseline

**Work**

- Keep the preserved behavior contract and the decisions above as the
  acceptance specification.
- Record the current installed-game assembly hash and verify that every native
  type/member used by the carried adapter is covered by
  `data/native-contracts.json`.
- Add the Auto Agromancy feature page to the Automata testing map, marked
  implementation-pending.
- Retain the existing planner and native-adapter tests as compatibility oracles.

**Exit evidence**

- `./script/test` passes on the untouched runtime.
- The installed-game contract audit either confirms the carried reflection
  surface or lists the exact drift that must be resolved before Step 1.

### Step 1 — Extract an exact native-free cost model

This is the highest-risk slice and should land before ServiceCycle composition.

**Work**

- Audit `HarvestActionInstance.GetScalingInfo`,
  `ScalingInfo.GetDrainCostMod`, the action/element cost tuple graph, resource
  quality, and `ResourceSO.GetTrueSpend`.
- Represent the audited calculation using the existing game-math value types
  where possible. Do not publish delegates, reflection objects, Unity objects,
  arbitrary collections, or display names as identity.
- Split the carried adapter conceptually into:
  - neutral native fact reading suitable for shared world collection;
  - feature policy projection into `AutoAgromancyLevelPlanner`;
  - feature-owned live revalidation and mutation.
- Add differential tests that compare the captured formula with the carried
  native-call oracle across:
  - levels 1, maximum, and 4,096;
  - non-monotonic scaling;
  - positive action plus internal-resource cost;
  - resource quality conversion;
  - exact-zero, negative, NaN, and overflow boundaries.

**Exit evidence**

- A native-free record can reproduce the old adapter's target exactly for every
  portable fixture.
- No worker-facing type contains `object`, `Type`, `MemberInfo`, a delegate, or
  a game/Unity reference.
- If exact parity is not achieved, the migration stops here for a new design
  decision.

### Step 2 — Publish active Druidry pairs through the shared world source

Auto Agromancy remains an ordinary service. It does not receive a feature-owned
main-thread capture path.

**Work**

- Add gameplay-neutral world rows for the authoritative
  `ActiveHarvestActions` list:
  - stable action UUID and expected `HarvestActionSO` type;
  - stable element UUID and expected `HarvestElementSO` type;
  - current and maximum level;
  - visibility/availability facts needed by policy;
  - flat resource-cost/scaling facts from Step 1.
- Use flat publication tables for repeated resource facts; do not put mutable
  arrays or lists inside a published row.
- Resolve the active list by its audited stable UUID and exact native list type.
  Names remain diagnostic only.
- Publish native-free monotonic trigger epochs for:
  - accepted Agromancy plot actions;
  - verified Auto Harvest native submissions.
- Invalidate all native bindings on scene, save-load, reset, and NG+ lifecycle
  changes.

**Exit evidence**

- World collector tests cover empty, one-pair, multi-pair, duplicate,
  malformed, lifecycle-replaced, and partial-contract cases.
- Type-safety and architecture-boundary tests accept the new publications.
- Collection failure closes Auto Agromancy facts without withholding unrelated
  world categories.
- Deterministic allocation/performance evidence shows the new collection is
  bounded with zero active pairs and the reviewed maximum fixture.

### Step 3 — Build the pure ServiceCycle domain

**Work**

- Add cohesive `src/AutoAgromancy` folders:
  - `Policy`;
  - `ServiceCycle/Domain`;
  - `ServiceCycle/Application`;
  - `ServiceCycle/Composition`;
  - `ServiceCycle/Native`;
  - `ServiceCycle/Diagnostics`.
- Define immutable/native-free:
  - `AutoAgromancyCycleState`;
  - `AutoAgromancyCycleAction`;
  - decision metrics and stable reason codes.
- Track prior observed levels, trigger epochs, and a deterministic pair cursor
  in worker state. Do not store native objects or variable-length work queues in
  the state.
- Treat a direct native level increase as a request to balance that pair.
  Removal only updates the baseline.
- Treat a new plot-action or verified Auto Harvest epoch as a request to visit
  every currently active pair.
- Select at most one pair/target per cycle. Preserve the trigger epoch and cursor
  until all pairs have been visited against fresh world publications.
- Use `ServiceActionDispatchPolicy.Bounded(1)`.

**Exit evidence**

- Pure tests cover direct increase, removal, plot-action trigger, Auto Harvest
  trigger, no active pairs, pair disappearance, pair insertion during a sweep,
  repeated/coalesced epochs, lifecycle replacement, and deterministic order.
- The worker uses only configuration, world, strategy, cycle context, and its
  own state.
- Service state projection explains why no action was planned without relying
  on diagnostic text parsing.

### Step 4 — Build the live revalidation and mutation boundary

**Work**

- Make the action record carry stable action UUID, stable element UUID,
  expected native types, observed current level, planned target level,
  lifecycle generation, and the minimum fact fingerprint required to reject a
  changed plan.
- Immediately before mutation, revalidate:
  - suite and Auto Agromancy configuration;
  - emergency stop;
  - lifecycle generation;
  - action-family ownership/permit;
  - authoritative active-list identity and exact type;
  - exact action/element identity and native types;
  - current/max level and every resource/cost/rate fact used by the plan.
- Reject a changed world and let the next cycle replan. Never raise the planned
  target opportunistically on the main thread.
- Apply only `AddInstance` or `ChangeInstance`.
- Verify exact pair identity and resulting level, then re-read affected native
  rates. Roll back to the exact previous level when immediate safety evidence
  fails; verify the rollback.
- Latch an unverifiable attempted mutation until lifecycle replacement.

**Exit evidence**

- Adapter tests cover every failed revalidation independently.
- No mutation occurs after configuration disable, emergency stop, ownership
  loss, lifecycle replacement, identity/type mismatch, cost/rate drift, or
  pair disappearance.
- Commit, verified rollback, rejected-no-call, attempted-unverified, and
  contract-fault results map to stable ServiceCycle action codes and native
  mutation evidence.

### Step 5 — Add exact trigger producers

**Work**

- Add the concrete
  `PlotNodeActionInstanceListVariable.AddInstance(PlotNodeActionInstance, int)`
  patch to `Plugin.HarmonyPatchTypes`.
- In the patch:
  - accept only the authoritative `ActivePlotNodeActions` UUID and exact list
    type;
  - capture the matching quantity before the call;
  - publish a new epoch only when the matching quantity increased;
  - retain no native reference after the hook returns.
- Extend Auto Harvest's verified mutation callback to advance the Auto
  Agromancy harvest-submission epoch only after its existing postcondition
  succeeds. This is queue-engagement evidence, not eventual tree-action
  completion.
- Let shared world collection publish the epochs; the worker does not subscribe
  to events or read plugin statics directly.

**Exit evidence**

- Headless Harmony tests prove wrong list, wrong overload, failed add,
  unchanged quantity, removal, and exception paths do not advance the epoch.
- Auto Harvest rejection or unverified mutation does not wake Auto Agromancy.
- Repeated events coalesce by monotonic epoch without losing a pending sweep.

### Step 6 — Compose lifecycle, ownership, and diagnostics

**Work**

- Add an Auto Agromancy action family distinct from ready-plot Harvest Action
  ownership. Acquire and recheck its mutation permit independently.
- Implement `AutoAgromancyServiceCycleFeature` and its non-generic runtime.
- Register it in the explicit production order decided above.
- On configuration and lifecycle transitions:
  - invalidate native bindings;
  - clear mutation quarantine only for a new lifecycle;
  - retire stale worker state through the normal ServiceCycle path.
- Add feature-status projection for disabled, parent-disabled, emergency stop,
  lifecycle not ready, action-family conflict, contract unavailable, pending,
  operational, and mutation-fault states.
- Publish bounded numeric decision metrics to the existing journal/full-trace
  path. Do not introduce a feature-owned logger, trace store, or pump.

**Exit evidence**

- Registration-order, mixed-feature, lifecycle, emergency-stop, shutdown, and
  ownership tests pass.
- Diagnostic failure does not change gameplay behavior.
- Architecture tests prove there is still one registry, one frame pump, one
  world source, and no fallback runtime.

### Step 7 — Activate configuration and user-facing documentation

This is the first step that makes the feature reachable.

**Work**

- Add `AutoAgromancyConfiguration` to `SuiteRuntimeConfiguration` with:
  - `Mode`, default `Disabled`.
- Bind `[AutoAgromancy] Mode` in the suite configuration and Mods settings.
  Do not move or alias Auto Harvest keys.
- Keep schema version 5 for the additive key unless Step 0 found a released
  configuration that requires a real transformation.
- Add the feature status to Mods Runtime and configuration dependency/locking
  tests.
- Update the Automata README, user configuration guide, testing guide, and
  runtime-validation handoff with only behavior now reachable in production.

**Exit evidence**

- A fresh and an existing schema-5 configuration both receive a disabled Auto
  Agromancy setting without changing Auto Harvest.
- Disabled mode performs no active-list scanning beyond neutral world
  collection, opens no mutation permit, and produces no native mutation.
- Enabling/disabling, Apply, emergency stop, and lifecycle transitions are
  covered through the composed runtime.

### Step 8 — Installed-game validation

Use a disposable validation save; never edit an active save.

**Scenarios**

1. Enable with no active Druidry pair: no mutation and truthful idle status.
2. Directly increase a sustainable pair: the exact highest sustainable level is
   selected and verified.
3. Increase an unsustainable pair: restore the exact previous level.
4. Remove a level: preserve the native removal and only refresh the baseline.
5. Queue Plant Sapling and another Agromancy entity action: each accepted add
   advances the trigger and causes a later full-pair sweep.
6. Submit fruit and treasure actions through Auto Harvest: only verified native
   queue engagement advances the trigger.
7. Toggle disabled and emergency stop while work is pending: no later mutation.
8. Change scene, load a save, reset, and enter NG+: no stale reference or
   pending action survives.
9. Introduce an action-family conflict: Auto Agromancy alone stands down.
10. Exercise exact-zero, quality-adjusted, maximum-level, and multi-pair cases
    while recording trace/journal evidence.

**Exit evidence**

- Installed contract tests pass against the audited game build.
- Differential verification agrees with native scaling, true spend, and rates
  for every active pair in the validation save.
- Runtime traces show no Unity/game API access from a worker thread and at most
  one Auto Agromancy mutation per accepted frame.
- Frame-time/profile evidence stays within the suite's reviewed limits.
- Only after this evidence may documentation describe the feature as working
  in-game.

## Definition of done

The migration is complete only when:

- Auto Agromancy is an ordinary typed ServiceCycle feature using the single
  shared world source and frame pump.
- Every cross-thread identity is stable UUID plus expected native type.
- Level choice is native-free and worker-owned; the Unity thread only
  revalidates, mutates, and verifies.
- Auto Harvest remains behaviorally and configurationally independent.
- Disabled, emergency, ownership-loss, lifecycle, and unknown-contract paths
  fail closed.
- The full portable gate, installed contracts, differential verification, and
  Unity runtime scenarios all pass on the exact candidate tree/build.

## Installed-game evidence retained from the earlier branch

Interactive validation on 2026-07-25 established that a native Plant Sapling
click reached the exact concrete plot-action `AddInstance` signal. The test save
had no active Druidry pair and no available Druidry resource, so it did not prove
a visible nonzero-level mutation. That remains a release-blocking validation
item.

The earlier audit also found that Harmony interception of the Druidry UI
callback and inherited harvest-list add/change methods was bypassed by the
Unity/Mono runtime. All Agromancy entity buttons converged on the concrete,
non-generic plot-action list method, which is why the remaining integration
must use that exact target.

## Deferred opportunities

Reserve-aware sustainability, stored-resource affordability, per-action caps,
cross-action optimization, extra resource-rate wake sources, and a dedicated
gameplay control are future features. They are not migration requirements.
