# Automation

This folder is the automation feature area of the suite: Auto Buy, Auto Cast, Auto Concept, Spell Leveling, and Auto Harvest. It is not a separate plugin and carries no version of its own; everything here compiles into `OrbModSuite.dll` and loads under the suite's single plugin GUID.

Auto Buy, Auto Harvest, Spell Leveling, Auto Cast, and Auto Concept are registered ServiceCycle services and share the frame pump with world collection. Auto Buy receives a fixed 16-action turn per Unity frame; the other feature services retain a bounded one-action turn. Fruit and treasure capabilities cannot mask an eligible sibling. A feature-neutral host owns the ServiceCycle registry, frame pump, lifecycle, emergency control, timing publication, and pump-shutdown lease. That host also owns the observation products — the optional manual full-trace session, the normally-on compact decision journal, and the profiling build's performance profile — so no feature owns them and gameplay is independent of all three. Every native operation passes through fail-closed family adapters, normalized admission facts, lifecycle isolation, and capture-execute-capture postconditions.

Automation claims Structure, Upgrade plus native multi-buy override, Spell Cast, Concept Assignment, Spell Level Purchase, and Harvest Action independently. The exact AutobuyOrb GUID blocks only Structure and Upgrade automation. Claims are released on configuration/lifecycle teardown, prepared work is cancelled on loss, and ownership is rechecked after live native validation immediately before mutation. Unknown unregistered automation is not disabled and cannot be proven absent.

## Build

The suite builds as one project. Set `OOC_GAME_DIR` to the Orb of Creation installation root:

```powershell
$env:OOC_GAME_DIR='C:\Program Files (x86)\Steam\steamapps\common\Orb of Creation'
dotnet build .\src\OrbModSuite.csproj -c Release
```

Do not commit the referenced BepInEx, Unity, Harmony, or game DLLs.

## Release defaults

- Auto Buy starts `Active` with separate `Excess100` thresholds for structures and upgrades; progression-aware spell leveling is enabled within Auto Buy and can be disabled separately.
- Auto Cast starts `Disabled` and can be toggled with `F8` or its queue-adjacent button.
- Gameplay controls extend outward from the native Auto Buy queue switch with 12-pixel gaps: native Auto Buy, emergency stop, suite Auto Buy, Auto Cast, Auto Concept, then Mentor. The strip is outside the action queue and does not use the status-effects container.
- Auto Concept starts `Disabled`; `Active` fills compatible acquired Active Concept slots breadth-first, then batches safe quantity depth up to native mastery limits.
- Auto Harvest starts `Disabled`; its fruit-tree and treasure-tree selectors default on behind that master switch, with no gameplay button or shortcut.
- All four mode selectors expose only `Disabled` and `Active`.
- One native queue slot is reserved for manual actions.
- `PurchaseGrouping=BulkDevelopment` gives each ranked Structure one live Bulk Development-sized group and each Upgrade one level before the ranked pass repeats. Every submitted level is independently revalidated.
- Queue admission uses the shared fail-closed `QueueCapacitySnapshot`: authoritative native total capacity and remaining room determine occupancy, then the Auto Buy usage limit and manual reservation are applied once to derive usable room.
- Cost/quality Structure priority is off by default and can be enabled with `PrioritizeCostAndQualityStructures`.
- Native action-multiplier grouping is available as an explicit `PurchaseGrouping` mode and is off by default.
- Absolute and relative reserves default to zero; affordability modes provide the default spending margin.
- Operational decision logging is off; startup, warning, and error records remain enabled.
- `EmergencyDisable` immediately stops new automated purchases, casts, concept mutations, spell levels, and harvest submissions.
- The configuration UI leaves each mode, toggle shortcut, status-button visibility, emergency control, and diagnostics editable while locking inactive feature tuning. Nested Auto Buy fields also require their applicable include, batch, or grouping policy.

At runtime, those persisted settings are mapped once per change into one composed immutable configuration record with General, Auto Buy, Auto Cast, Auto Concept, Auto Harvest, Safety, Performance, Diagnostics, and Reserves sections. Engines and controls consume that record rather than BepInEx entries or feature-specific configuration mirrors. A saved ServiceCycle cycle pins the complete record it began with; later changes apply to a later cycle.

The former runtime-probe, per-session purchase-limit, DryRun, expert-override, Auto Research, and Auto Harvest runtime-selector settings are not part of the release configuration or the configuration UI. Automation has no configuration schema of its own: it binds into the suite's single configuration file, which the shared pre-bind transaction marks at `ConfigurationSchemaVersion` 3. The 2-to-3 step moves only inherited defaults: Auto Cast's `Left Alt + X` becomes `F8`, and differential verification becomes unbound in favor of its Mods Runtime button. Player-customized chords are preserved. Malformed, negative, or future schema data fails closed without starting the suite.

## Auto Harvest

Auto Harvest is independent from Auto Buy. When `AutoHarvest.Mode=Active`, it considers only the exact audited fruit-tree and treasure-tree collect pairs selected by `CollectFruitTrees` and `CollectTreasureTrees`. It alternates eligible pairs after each successful submission and never queues more than one supported collect action at a time.

Each pair-set capture admits both pair circuits once, resolves the shared active-list/scaling contract once,
then resolves the fruit and treasure bindings independently and caches only their immutable serialized safety
graphs. Visibility, prerequisites, readiness, duplicates, slot room, identity currency, and mutation
postconditions remain live. A missing or unsafe pair therefore cannot starve a healthy sibling; transient
live-state gaps use bounded retry, static pair-contract failures remain process-bound, and partial availability
is reported as degraded.

Every evaluation resolves the exact plot, action, active-list, scaling-weight, and reward-pool identities through the lifecycle-stamped typed registry. It then verifies native plot visibility, exact action membership, empty prerequisites, positive remaining quantity, native one-instance readiness, the reusable `Idle` to `Resting` phase contract, empty resource drain and persistent effects, and the exact `EarnTreasure` completion graph. Unknown or changed evidence rejects the action.

Pair fact capture validates the plot's available-action list type closure and exact action membership in one
native list pass. Shared active-action traversal remains a separate snapshot because it supplies duplicate,
slot, and mutation-transition evidence for both pairs.

The pair-set resolver performs the lifecycle-generation coherence check once. The following fact read verifies
the plot and action stable UUIDs directly; it does not reread the same registry generation. Scheduler admission
may inspect configuration, quarantine, and family ownership to avoid reserving unnecessary work, while the
action callback checks them again to produce the authoritative terminal result.

Submission always requests quantity one through `ActivePlotNodeActions.AddInstance`. Auto Harvest requires both the action list's native `HasEmptySpot()` predicate and at least one enumerated empty action entry, and may consume the final free entry. The visible plot-space meter (for example `30/33`) is unrelated, and this action list is also separate from Auto Buy's global action queue. Auto Harvest captures the active list before and after mutation and requires exactly one new engaged matching entry with quantity one and the corresponding entry delta. If an attempted mutation cannot be verified, that tree pair remains blocked until a scene, save-load, reset, or NG+ lifecycle transition.

`EvaluationIntervalSeconds` defaults to one second and applies only while the feature is enabled. Disabled mode and `EmergencyDisable` perform no Auto Harvest scans or new submissions. Auto Harvest does not plant, replant, replace, enrich, force growth, destroy plots, modify saves, or coexist with another plot-action automation mod. Interactive behavior is covered by the [runtime validation guide](../../docs/testing/runtime-validation.md).

### ServiceCycle runtime

The Common ServiceCycle engine is the only Auto Harvest driver. The suite registers a feature-owned activation service during plugin startup, but does not construct the lifecycle-bound runtime until the first playable `Main` frame. Generation zero remains invalid rather than being converted into a synthetic native lifecycle. Construction is attempted once; an ordinary failure disables Auto Harvest alone without selecting an alternate path or affecting sibling services. Fatal process failures are never relabeled as an isolated feature fault.

The neutral host seals an explicitly populated typed registry and polls every registered service once per Unity frame. A mixed-type composition test registers unrelated frame/config/state/action graphs in the same host; no host type references Auto Harvest or Auto Buy. Production explicitly registers world collection, Auto Harvest, and Auto Buy rather than creating another pump. Main-thread capture copies only native-free facts; sleeping workers evaluate feature policy; and every advisory action returns to the Unity thread for fresh native validation.

Auto Buy audits each exact candidate type and native convenience signature once, then binds typed read delegates for live availability, admission, levels, and cost. Stable UUID, exact type name, and optional Structure-priority metadata are cached only by native object reference within the current lifecycle and are discarded on lifecycle replacement. Resource quantities, costs, queue room, and every other mutable gameplay fact remain fresh per capture.

The opt-in profiling build also records the action path in separate stages: current-fact revalidation, the
before snapshot, the native `AddInstance` invocation, the after snapshot, and postcondition evaluation.
The before snapshot is captured once and supplies both current-policy facts and verifier admission; the
after snapshot remains a separate native traversal. Ordinary assemblies compile out the profiler types and calls.

Saved configuration publishes only after a successful Apply and affects the next cycle. Lifecycle replacement clears stale native bindings, emergency disable rejects unattempted work immediately, and action-family ownership is checked both in diagnostics and immediately before mutation. Capture preserves native failure evidence: an exception thrown inside a correctly bound native call is retryable, while reflected shape, access, owner, or return-contract drift latches until restart without invoking the broken path again. A failed shared active-action snapshot is feature-scoped; a failed pair-fact read remains pair-scoped. Process-lifetime contract failures remain closed until restart rather than being converted into ordinary lifecycle retries.

The Runtime page projects pair health from immutable ServiceCycle state without inventing a legacy cycle identity. A worker response requests one full service projection; a zero-wait contended read remains pending until a later frame succeeds. Emergency-stop and action-family conditions remain immediate.

The separate Runtime-page full-trace control records incremental diagnostic-only sessions under that
launch's own `BepInEx/config/OrbOfCreation-ModSuite/trace/run-<timestamp>/full/` folder, beside the
performance profile from the same launch. Each launch prunes all but the newest eight run folders.
`./script/trace --full <session-directory> [report.md]` strictly validates and reports those sessions.
Profiler-enabled debug builds start both this full trace and the ServiceCycle performance profile
when the shared runtime is created. Closing the game stops both with the runtime-shutdown reason
and flushes their accepted prefixes; release builds retain manual, opt-in recording.

The compact decision journal starts once with the lifecycle-bound ServiceCycle runtime and records coalesced
numeric service decisions under the stable `BepInEx/config/OrbOfCreation-ModSuite/trace/journal/` directory,
which is deliberately not per-launch so its size cap governs one directory. It owns ten reusable
blocks, checkpoints the current span once per minute, and initially retains 10,080 rolling segments. Those are
live-validation settings, not a stable disk quota: the Runtime page reports accepted and written records,
bytes, retained and evicted segments, buffer pressure, and terminal faults so the quota can follow measured
rates. Journal initialization or writer failure detaches only observation; it does not stop Auto Harvest,
start a replacement writer, or switch formats. Shutdown seals the accepted prefix and returns without waiting
for disk I/O on Unity's main thread.

Portable tests measure the warmed journal control tick, pump, and always-on diagnostics across 64 successful
cycles and keep every owner and worker sample within its reviewed 64-byte ceiling. Installed-game frame cost
remains an interactive profiling gate.

## Auto Buy

Auto Buy asks the game nothing while it decides. Its candidates *are* the shared world snapshot's structures and upgrades, and identity, availability, current and queued levels, prices, resource quantities and the economic priority a candidate's authored effects earn it all arrive as published rows. Each cycle projects that snapshot into a flat frame on the worker — no candidate index, no incremental reconcile, no dirty tracking — ranks the eligible ones by priority, then by cost ratio, then by uuid, and plans one purchase per eligible candidate in that order. The game is touched only where the purchase is made: `CanPurchase()` and the queue's remaining room are read there, and the boundary re-validates every level immediately before it mutates.

`AutoLevelSpells=true` runs while Auto Buy is active and is configured in the Auto Buy section. Its capability follows native progression automatically: `Locked` while no discovered spell passes its own leveling prerequisites, `Single` after that contract unlocks, and `All` after the exact `UnlockLevelAllSpells` Upgrade has completed. Single mode pays the spell's live native cost and confirms one native `PurchaseLevel()` per mutation. All mode calls the game's native `SpellManager.TryLevelAllSpells()` action. Queued upgrades do not count, affordability and readiness are revalidated immediately before mutation, and any ambiguous failure after a cost attempt blocks further spell leveling for that lifecycle.

A candidate's price is the published `WorldPurchaseCost` row for its next level, computed by the suite's own port of the game's cost chain and verified against the game entity by entity in a live session. Reserves and affordability are applied to that price against the published resource quantities, so what a purchase would leave behind is decided on the worker rather than discovered at the boundary. A candidate nobody could price is refused rather than treated as free. There is no per-candidate retry or backoff: the batch stops at the first native rejection, and the next cycle re-plans from a fresh world, so no decision is ever taken twice against facts that have moved. Save loads, gameplay-manager restarts, scene changes and NG+ start a new lifecycle; unknown cost, resource, lifecycle or identity state fails closed.

A value that could not be read is not evidence. A candidate whose every cost row prices at zero has not been shown to be affordable, only to be unpriceable, so it is excluded rather than bought — the failure direction that once planned all 180 structures at once after a cold load. One free row on an otherwise priced candidate is different and is simply skipped, because relative to the rows that did price it really is free. Bulk grouping raises a single action's requested level to the game's own live count — the multi-buy multiplier for an upgrade, the bulk-development count for a structure — and the action boundary clamps that request to the queue room above the configured reserve, since the game queues one entry per level.

Every active native mutation now uses a capture, execute, capture, verify boundary. Auto Buy requires an exact queued-level delta, Auto Concept requires the exact queued assignment delta, spell leveling verifies native mastery advancement, Auto Cast verifies the audited `Spell.Fire` hook, and Auto Harvest requires one exact new native plot action. A no-op, partial, unexpectedly large, throwing, or unobservable result records structured before/after evidence and blocks that candidate or feature for the current lifecycle. Recovery is deliberately limited to scene, save-load, reset, or NG+ lifecycle invalidation; ordinary evaluation and configuration polling cannot silently retry an ambiguous mutation.

When the game refuses a purchase Auto Buy planned, the batch stops there and Auto Buy stands itself down instead of retrying. A refusal means the planner and the game disagree about the same facts, and every retry is another wrong answer — one session spent itself planning a single upgrade the game refused 1,988 times. So the boundary first asks the game *why* it refused, reading `IsAvailable()`, `IsMaxLevel()`, `IsMaxQueuedLevel()` and its verdict on the price, writes a refusal bundle recording what both halves believed, and turns Auto Buy's own setting off through the same path the toggle button uses. Turning it back on is one click, and nothing else re-enables it.

Feature health publishes Auto Buy, Auto Cast, Auto Concept, Spell Leveling, and Auto Harvest independently through Common. Controls and tooltips now separate saved configuration from progression locks, lifecycle readiness, ordinary operation, temporary queue or safety blocks, unavailable contracts, partial degradation, and verified faults. The projection consumes existing engine evidence and publishes only canonical condition transitions; it does not add catalog scans, candidate work, or native mutations.

Automation consumes the shared Common lifecycle monitor. Scene entry/exit, save loading, save completion, gameplay-manager readiness, reset/NG+, and registry-rebuild observations advance one coalesced generation across the suite. Every generation transition cancels prepared Auto Cast and Auto Concept work before another native mutation can start; equivalent callbacks arriving repeatedly within the same frame are idempotent.

Auto Concept subscribes to Common's shared gameplay-invalidation bus in its own domain, waking on inventory changes and on progression. Every notice carries the stable UUID and the expected native type name; an identity the publisher cannot resolve widens to a domain-wide notice rather than falling back to a display name or retaining a native object.

`AffordabilityMode` and `UpgradeAffordabilityMode` are independent:

- `BuyAll` accepts any natively affordable action that passes reserves.
- `Excess10`, `Excess100`, and `Excess1000` limit each resource cost to 1/10, 1/100, or 1/1000 of its current amount.

Reserves are an optional second policy. After each level, Auto Buy requires enough resource for that level plus the larger of `AbsoluteReserve` and `cost × RelativeReserveMultiplier`. Because the game deducts each native cost immediately, a repeated or multiplied purchase rechecks the progressively lower live balance before every next level.

`PurchaseGrouping` defines how many independently validated levels one ranked candidate receives before Auto Buy advances. `Single` uses one level, `Fixed` groups Structures by `FixedGroupSize`, `BulkDevelopment` follows the live player value for Structures, and `ActionMultiplier` follows the live native multiplier for either purchase family. Upgrades receive one level in every mode except `ActionMultiplier`. Every group is capped to usable queue room.

Upgrade submission temporarily forces the native global multi-buy value to one and verifies that the captured value is restored afterward, including when the setter or purchase throws. If restoration cannot be confirmed through the native getter, further automated Upgrade mutations are quarantined for the process and removed from admission, cached ranking, and pending batches. Structure purchases do not use that global and remain independently eligible.

Continuation is always active: after each candidate's group, Auto Buy advances through the remaining prepared ranking and begins another pass while its batch quota and live queue room permit. The prepared next candidate does not require a catalog rescan. A lone candidate in `Single` mode may consume all usable room as an equivalent sequence of one-candidate passes. Native availability, current cost, affordability, reserves, maximum level, and queue admission are re-read before every level.

`PrioritizeCostAndQualityStructures=true` adds one purchase-priority tier above ordinary cost-ratio ordering. A Structure receives that tier only when its stable native effect definition and a non-mutating `ValueModifier.Adjust(1)` preview prove that it reduces `Cost`, `CostScaling`, or resource `AttributeCost`, or increases resource `Quality`. Dynamic targets, unknown properties, unreadable modifiers, and effects with the wrong direction receive no boost. The option changes ranking only after native availability, `CanPurchase`, exact current cost, affordability, reserves, allow/block lists, and queue safety have passed; it never makes a locked or unaffordable Structure eligible. Classification is lazy and cached, so it is not performed while the option is off and is not repeated in the per-frame evaluation path.

### Queue scheduling

The configured evaluation interval applies while Auto Buy has no immediately
useful work. Its ServiceCycle worker may return a prepared batch larger than one
frame can safely consume. Common gives Auto Buy one turn of at most 16 exact
one-level action callbacks per Unity frame. The turn stops earlier when the batch
completes, an action rejects or faults, or emergency stop becomes active. Every
callback rereads authoritative queue room and candidate state immediately before
mutation; a skipped native no-op advances to the next action.

Native submission remains on Unity's main thread because the game registries,
ScriptableObjects, resources, and action queue are not thread-safe. The current
16-action count limit is a trace-driven safety bound, not yet a wall-clock
budget.

Auto Buy, Auto Harvest, and world collection share the same fair ServiceCycle frame
pump. Auto Buy's 16-action turn cannot consume Auto Harvest's default one-action turn. The fixed
limit is intended to become a measured frame-time budget once traces establish
the appropriate target and accounting.

The suite is designed to be the only auto-buy plugin in the installation. Running another buyer against the same resources and queue is unsupported.

Auto Buy finishes the configured purchase group for each candidate and advances through the prepared ranking before refreshing dirty resource and cost state. With the default `BulkDevelopment` policy, every ranked Structure receives one live group and every Upgrade receives one level, preventing a cheap Structure from monopolizing the queue indefinitely. It does not predict future levels: every mutation is admitted independently.

Active membership and ranked recommendation views use reused buffers and deterministic bounded walks; routine evaluations do not rebuild reflected wrappers or sort the complete registry. The slow ten-second registry reconciliation reuses wrappers when native identity is unchanged.

Native completion signals do not discard a safely prepared Fixed, Bulk Development, or action-multiplier group. The group continues through independently revalidated levels within the current bounded burst, then the prepared ranking advances. Completion effects settle before the next repeat pass, so a newly unlocked higher-ranked candidate can pre-empt that pass. Completed Structures and Upgrades retain their native identity and family; a completion burst coalesces into one refresh of each affected family. Manual queue changes still cancel stale prepared work immediately.

Rapid completion signals are generation-coalesced while a bounded lifecycle settlement is already running. They do not restart its cursor or an in-progress candidate scan; one follow-up generation retains eventual broad effect discovery. A recommendation produced across that window is advisory only and must still pass a fresh authoritative candidate and queue validation before its native mutation.

Routine active and locked-content lifecycle probes run on a fixed 250 ms cadence rather than once per purchase evaluation. Each maintenance slice checks at most eight active and sixteen slow-reconciliation entries, so faster queue turnover cannot multiply background reflection work.

Structures must pass native availability before Auto Buy reads costs or calls the purchase contract. The supported native `UpgradeSO.CanPurchase()` contract combines affordability with lifecycle, requirements, and queue admission, so Auto Buy calls it first and then decodes the exact current cost before classifying a false result. A proven reserve or affordability failure remains subscribed to its resource dependencies; a cost-safe false result is parked outside high-frequency quantity updates for bounded lifecycle or completion retry. Native bandwidth costs are explicitly identified through `ResourceSO.IsBandwidthResource()` and remain tracked because their admission uses missing usage rather than ordinary quantity.

Scan-cap deferrals are counted separately from evaluated rejections, transitions back to ready are counted explicitly, and identical verbose rejection examples remain suppressed until their typed blocker signature changes. Repeated native mutation failures are rate-limited per candidate while aggregate attempt/failure totals remain visible. Reflection metadata for queue room, queued-level verification, and the global multi-buy contract is cached only after exact signature validation. The live multi-buy variable itself is fetched again for every Upgrade level so save or lifecycle replacement cannot leave a stale native reference.

During a prepared ranked pass, each candidate refreshes its own cost immediately before its level. Shared resource-dependent invalidation is coalesced while later candidates are still live-revalidated against current resources and native state. A failed admission skips that candidate for the current pass; an ambiguous native mutation failure ends the pass so dirty state can settle safely. If the queue fills, the next ranked candidate remains prepared across the wait and is live-revalidated when a slot reopens.

Queue capacity is refreshed after that live candidate/cost/reserve validation and immediately before every queued mutation. The supported native adapter reads total capacity from `ActionManager.instance.actionableItems.maxQueuedItems.AsInt()` and live room from `ActionManager.GetRemainingRoom()`. Negative values, remaining room greater than capacity, missing native objects, or an invalid policy input reject the snapshot and submit no purchase. The snapshot derives occupancy and subtracts `LeaveQueueSlots` exactly once before applying the current batch usage limit.

## Auto Cast

Auto Cast follows equipped spell slot order and fires at most one new spell per evaluation. Empty slots are skipped, active auras are treated as already satisfied, channels pause the rotation, and persistent spells are never turned off automatically.

`FullCharge=true` holds charge-capable spells through the game's native charge-input contract until the full-charge point. While Auto Cast owns that hold, the rest of the rotation pauses. The hold is released when charging completes, Auto Cast is disabled or emergency-blocked, the setting is turned off, manual spell input occurs, or the plugin shuts down. Set `FullCharge=false` to fire charge-capable spells immediately without charging.

A cast deferred by the shared coordinator is treated as a short-lived plan. Before firing, Auto Cast rediscovers the slot and requires the stable recipe UUID, exact native Spell reference, runtime type, and slot index to match. Scene changes, save implementation, and player-manager restarts discard prepared casts immediately.

Every finite-cap resource used by immediate or drain costs must meet `StartResourcePercent`. Immediate costs also pass the shared reserve policy. Manual casting pauses automation for `ManualPauseSeconds`, and an existing manual target prompt is never replaced.

The button shows desired intent as `AC OFF` or `AC ON`; emergency blocking preserves that intent and renders `AC ON / STOPPED`. Runtime readiness and fault detail remain in the same published status shown by the tooltip and Mods Runtime. It uses the first equipped spell icon when available.

## Auto Concept

Auto Concept uses the shared `OrbModding.Common.AlchemyGameplayDomainClassifier` as its concept-versus-ordinary-alchemy identity boundary. The classifier resolves the exact `ConceptRecipes` UUID/type asset and requires each exact `AlchemyRecipeSO` registry member to carry mutation-grade static-contract, serialized-asset, exact-runtime-type, stable-identity, registry, and relationship evidence; contradictory ordinary-and-Scholar typing fails the lifecycle closed. Auto Concept separately resolves `ActiveConcepts` for native slot and quantity ownership. It never uses the global alchemy recipe registry as a concept catalog and never mutates ordinary alchemy.

`ActiveConcepts`, `ConceptRecipes`, and the spell-level `UnlockLevelAllSpells` upgrade resolve through Common's lifecycle-aware typed registry resolver. Missing or not-yet-registered assets remain retryable, while wrong type, UUID contradiction, or unavailable audited accessors fail closed with structured UUID/type/status/evidence diagnostics. A generation change invalidates the retained native reference before another read or mutation.

`Mode=Active` ranks discovered concepts by mastery level, fractional XP progress, and stable UUID. It assigns one instance to each currently compatible acquired slot before deepening active assignments. Depth is submitted as one native batched quantity change up to the recipe's live mastery maximum or `PerConceptQuantityCap`.

`SlotManagementMode=TimedCycle` is the default once Auto Concept is enabled. It permits complete settled replacement, but every assigned concept receives its full configured settled-active period before rotation; catching the current highest mastery never ends that session early, and least-recently-assigned ordering prevents discovered compatible concepts from being starved. `RotateAll` instead removes one settled active concept only if a compatible inactive concept has strictly lower mastery, waits for native settlement, and then adds the exact planned replacement. Equal mastery never rotates on UUID ordering alone. `PreserveManual` never removes the quantity present when Auto Concept starts; it can rotate only assignments that Auto Concept added itself.

Every newly assigned lower-mastery concept in `RotateAll` or `PreserveManual` receives a catch-up training session. The session captures the highest eligible mastery level and fractional progress at assignment time, becomes timed only after the native quantity is settled and active, and protects the assignment until it reaches that target or `TrainingPeriodSeconds` elapses. `TimedCycle` uses the same timer but never applies the catch-up shortcut. The default is 300 seconds and the accepted range is 10 through 3600 seconds. Native setup time does not consume the period, and the controller schedules the exact next session deadline even when its idle fallback is longer.

The `CN ON/OFF` gameplay button toggles Auto Concept and represents configured intent; emergency blocking renders `CN ON / STOPPED`, while other runtime health remains tooltip evidence. `ShowToggleButton` defaults to true. Spell-leveling state is shown on the suite's Auto Buy tooltip instead.

`FallbackEvaluationIntervalSeconds` is an Advanced setting, not a rotation delay. It defaults to 300 seconds and accepts 10 through 1800 as the maximum idle delay between full plan calculations; native changes can request earlier passes. Existing `RebalanceIntervalSeconds` and `RebalanceIntervalMinutes` values migrate automatically.

Before every add or rotation, Auto Concept reconstructs that exact prospective native drain vector, rejects every positive drain whose authoritative resource state is zero, converts the remainder through each resource's live quality with `ResourceSO.GetTrueSpend`, and compares the projected rate with `RateReservePercent`. Finite resources must also meet `MinimumResourcePercent`. A replacement whose resource is at zero is skipped without blocking other resource-safe concepts or acquired slots in the timed order. Unknown vectors, identity mismatches, incompatible slots, and changed mastery limits fail closed. A 1 Hz watchdog checks only cached active assignments; if the native drain ratio falls below `MinimumDrainRatio` or a drained resource reaches zero, it schedules removal of only the quantity recorded as suite-owned.

Enabling the feature initializes the scoped shared classifier and snapshots current Active Concept quantities for ownership and rollback accounting. Disabled Auto Concept neither initializes nor rebuilds classifier/catalog evidence. Unexpected settled changes are rebaselined as player-owned. `PreserveManual` never replaces that baseline; `RotateAll` explicitly permits a complete settled assignment to be replaced for mastery balancing, but the drain watchdog still rolls back only Auto Concept-added quantity. Disabling the feature stops work and leaves native quantities unchanged. Save loads, scene changes, and manager lifecycle resets (including reset/NG+ manager restarts) invalidate classifier and runtime references and rebuild a new baseline only after Auto Concept is active again.

## Diagnostics

Transient shared classifier readiness failures use the existing 30-second Auto Concept warning gate. A contradictory or permanently invalid concept-domain contract blocks Auto Concept for that lifecycle and is logged once; `Unknown` evidence never falls back to ordinary names or broad alchemy membership.

Set `Diagnostics.EnableOperationalLogging=true` only while troubleshooting. `DecisionLogLevel=Off` suppresses all normal Auto Buy and Auto Cast records even when the legacy enable switch remains true. Summary mode rate-limits recommendations, batch totals, casts, and queue waits to low-frequency records. Verbose mode additionally records individual purchases, bounded candidate rejections, and detailed Auto Cast resource snapshots.

Auto Buy decisions use append-only Common codes rather than parsing diagnostic text. Candidate threshold parking, rejection telemetry, the latest tooltip status, and verbose rejection records all consume the same immutable decision. Observed quantities and wording can change without producing a new condition; stable thresholds, identities, policy, queue limits, and native states do produce a transition. Equivalent conditions are not republished to future Insights subscribers.

Every automation log message includes local date, time to milliseconds, and UTC offset so runtime reports can identify when a failure began.
Successful Auto Concept initialization records separate scoped-recipe, active-loadout, and eligible-candidate counts. When operational logging is enabled, assignment reservation, settled training start, catch-up/timeout completion, rotation, and a rate-limited no-change summary distinguish an active or idle balancer from a missed evaluation.

Orb of Creation's LeanTween pool defaults to 400 simultaneous tweens. AutobuyOrb raises that capacity because very large purchase bursts can create enough UI popups to exhaust it. This is separate from queue scheduling; the suite does not currently override the global tween pool. If the BepInEx log reports LeanTween exhaustion or UI animations begin disappearing during unusually large batches, treat a restart-time tween-capacity option as a separate performance feature.
