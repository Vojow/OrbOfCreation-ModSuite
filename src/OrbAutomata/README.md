# Orb Automata

Orb Automata is a BepInEx 5 automation suite for Orb of Creation. Version `0.9.0` combines adaptive, grouped Auto Buy with bounded 16-call legacy-runtime bursts and ServiceCycle Auto Harvest. Fruit and treasure capabilities cannot mask an eligible sibling. A feature-neutral Automata host owns the ServiceCycle registry, frame pump, lifecycle, emergency control, timing publication, and pump-shutdown lease; Auto Harvest is its first feature registration. Auto Harvest owns the optional manual full-trace session and a separately buffered, normally-on compact decision journal while gameplay remains independent of either observation product. Every native operation still passes through fail-closed family adapters, normalized admission facts, lifecycle isolation, and capture-execute-capture postconditions.

Automata claims Structure, Upgrade plus native multi-buy override, Spell Cast, Concept Assignment, Spell Level Purchase, and Harvest Action independently. The exact AutobuyOrb GUID blocks only Structure and Upgrade automation. Claims are released on configuration/lifecycle teardown, prepared work is cancelled on loss, and ownership is rechecked after live native validation immediately before mutation. Unknown unregistered automation is not disabled and cannot be proven absent.

## Build

Set `OOC_GAME_DIR` to the Orb of Creation installation root, then build the project:

```powershell
$env:OOC_GAME_DIR='C:\Program Files (x86)\Steam\steamapps\common\Orb of Creation'
dotnet build .\src\OrbAutomata\OrbAutomata.csproj -c Release
```

Do not commit the referenced BepInEx, Unity, Harmony, or game DLLs.

## Release defaults

- Auto Buy starts `Active` with separate `Excess100` thresholds for structures and upgrades; progression-aware spell leveling is enabled within Auto Buy and can be disabled separately.
- Auto Cast starts `Disabled` and can be toggled with `Left Alt + X` or its queue-adjacent button.
- Gameplay controls extend outward from the native Auto Buy queue switch with 12-pixel gaps: native Auto Buy, Automata Auto Buy, Auto Cast, Auto Concept, then Mentor when installed. The strip is outside the action queue and does not use the status-effects container.
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
- Orb Mod Config leaves each mode, toggle shortcut, status-button visibility, emergency control, and diagnostics editable while locking inactive feature tuning. Nested Auto Buy fields also require their applicable include, batch, or grouping policy.

At runtime, those persisted settings are mapped once per change into one composed immutable configuration record with General, Auto Buy, Auto Cast, Auto Concept, Auto Harvest, Safety, Performance, Diagnostics, Replay, and Reserves sections. Engines and controls consume that record rather than BepInEx entries or feature-specific configuration mirrors. A saved ServiceCycle cycle pins the complete record it began with; later changes apply to a later cycle.

The former runtime-probe, per-session purchase-limit, DryRun, expert-override, Auto Research, and Auto Harvest runtime-selector settings are not part of the release configuration or Mod Config UI. Automata schema 3 runs the reviewed chain in order: schema 0 to 1 maps the Auto Concept mode and interval settings, schema 1 to 2 preserves the historical private marker, and schema 2 to 3 replaces `RespectActionMultiplier`, `RepeatWhileAffordable`, `StructureRepeatMode`, and `FixedStructureLevelsPerCandidate` with `PurchaseGrouping` plus `FixedGroupSize`. The retired Auto Harvest selector remains inert because it is not bound, parsed, displayed, or used. An existing older file is backed up as the first free `.pre-schema-v3.bak` sibling before the complete transaction; malformed, negative, or future schema data fails closed without starting Automata.

## Auto Harvest

Auto Harvest is independent from Auto Buy and Auto Agromancy. When `AutoHarvest.Mode=Active`, it considers only the exact audited fruit-tree and treasure-tree collect pairs selected by `CollectFruitTrees` and `CollectTreasureTrees`. It alternates eligible pairs after each successful submission and never queues more than one supported collect action at a time.

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

The Common ServiceCycle engine is the only Auto Harvest driver. Automata registers a feature-owned activation service during plugin startup, but does not construct the lifecycle-bound runtime until the first playable `Main` frame. Generation zero remains invalid rather than being converted into a synthetic native lifecycle. Construction is attempted once; an ordinary failure disables Auto Harvest alone without selecting an alternate path or affecting sibling services. Fatal process failures are never relabeled as an isolated feature fault.

The neutral host seals an explicitly populated typed registry and polls every registered service once per Unity frame. A mixed-type composition test registers two unrelated frame/config/state/action graphs in the same host; no host type references Auto Harvest. The current production composition contributes only Auto Harvest, while the next Auto Buy port will add a second explicit typed registration rather than another pump. Main-thread capture copies only native-free facts; one sleeping worker evaluates at most the two supported harvest pairs; and any advisory action returns to a later Unity frame for fresh native validation. Auto Harvest capture, action rotation, and replay-copy stepping do not register with or request work from the legacy suite performance coordinator.

The opt-in profiling build also records the action path in separate stages: current-fact revalidation, the
before snapshot, the native `AddInstance` invocation, the after snapshot, and postcondition evaluation.
The before snapshot is captured once and supplies both current-policy facts and verifier admission; the
after snapshot remains a separate native traversal. Ordinary assemblies compile out the profiler types and calls.

Saved configuration publishes only after a successful Apply and affects the next cycle. Lifecycle replacement clears stale native bindings, emergency disable rejects unattempted work immediately, and action-family ownership is checked both in diagnostics and immediately before mutation. Capture preserves native failure evidence: an exception thrown inside a correctly bound native call is retryable, while reflected shape, access, owner, or return-contract drift latches until restart without invoking the broken path again. A failed shared active-action snapshot is feature-scoped; a failed pair-fact read remains pair-scoped. Process-lifetime contract failures remain closed until restart rather than being converted into ordinary lifecycle retries.

The Runtime page projects pair health from immutable ServiceCycle state without inventing a legacy cycle identity. A worker response requests one full service projection; a zero-wait contended read remains pending until a later frame succeeds. Emergency-stop and action-family conditions remain immediate. Optional replay capture is off by default and writes one finite artifact at the first action, accepted lifecycle boundary, or window limit. Its 4,096-event semantic ring begins closing at 3,584 events and checks once per normal frame until every physical runner is between cycles. Semantic and replay evidence stay paired while an admitted cycle finishes; at the settled boundary recording closes and semantic emission detaches before another pump. It then copies at most 64 frozen facts on each later frame without acquiring a legacy scheduler lease. A 64-event final-frame reserve prevents overwrite while the boundary is pending; exhausting that reserve discards only the optional capture. Lifecycle replacement during an in-flight cycle or composition removal also discards a window whose terminal worker evidence cannot be completed. Encoding and storage remain on a low-priority background thread, and the Unity frame never waits for tracing or copies the full window at once. The log reports arming, the exact close reason, capture or export failure, and the stable relative filename only after its flush and atomic commit; export admission alone is never presented as success.

The separate Runtime-page full-trace control records incremental diagnostic-only sessions under
`BepInEx/config/OrbOfCreation-ModSuite/trace/full/`. `./script/trace --full <session-directory> [report.md]`
strictly validates and reports those sessions; it does not reinterpret them as replay artifacts.

The compact decision journal starts once with the lifecycle-bound ServiceCycle runtime and records coalesced
numeric service decisions under `BepInEx/config/OrbOfCreation-ModSuite/trace/journal/`. It owns ten reusable
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

Automata discovers native `StructureSO` and `UpgradeSO` candidates into a lifecycle-aware UUID index. It keeps locked content for bounded retry, quarantines missing or contradictory native identities, and reconciles the native registries incrementally. Ordinary evaluations refresh only dirty candidates, maintain deterministic cached ranking, and revalidate every level immediately before calling the native purchase method.

`AutoLevelSpells=true` runs while Auto Buy is active and is configured in the Auto Buy section. Its capability follows native progression automatically: `Locked` while no discovered spell passes its own leveling prerequisites, `Single` after that contract unlocks, and `All` after the exact `UnlockLevelAllSpells` Upgrade has completed. Single mode pays the spell's live native cost and confirms one native `PurchaseLevel()` per mutation. All mode calls the game's native `SpellManager.TryLevelAllSpells()` action. Queued upgrades do not count, affordability and readiness are revalidated immediately before mutation, and any ambiguous failure after a cost attempt blocks further spell leveling for that lifecycle.

Resource dependencies are learned from the native current-cost result. Each referenced native resource is read once per evaluation epoch, including true quantity, quality, capacity, and effective attribute-cost modifier. A stable Structure reserve or affordability rejection on ordinary quantity resources parks below its exact required quantities; sub-threshold income and quantity-only changes to already-satisfied dependencies do not rebuild the same cost, while crossing any blocker wakes the Structure for full multi-resource revalidation. Bandwidth resources remain conservatively tracked because their native admission uses missing usage rather than true quantity. Capacity, quality, effective-cost, identity, availability, lifecycle, policy, queue, and completion changes wake conservatively. Upgrade quantity retries remain conservative because native `CanPurchase()` combines affordability with lifecycle and queue admission. Save loads, gameplay-manager restarts, scene changes, and NG+ start a new lifecycle epoch. Unknown cost, resource, lifecycle, or identity state fails closed.

The installed-game `ResourceCostList` and `ResourceTuple` schema is validated once per native type. Every tuple must decode before any reserve decision uses the vector; adapter failures are quarantined with bounded retry and rate-limited warnings. Empty native cost lists remain valid free actions when native `CanPurchase` accepts them. Manual Structure queue and Upgrade purchase signals invalidate only the matching native candidate and cancel stale planned work. Synchronous signals caused by Automata's own purchase are correlated to that exact native object, so a CPU-sliced Fixed, Bulk Development, or action-multiplier group can finish its initial queue-room-clamped limit. Capacity decreases still stop the next mutation; increases are consumed by the immediate rerank after that clamp settles. Structure/Upgrade completion hooks remain broad because completed effects may change other candidates.

Every active native mutation now uses a capture, execute, capture, verify boundary. Auto Buy requires an exact queued-level delta, Auto Concept requires the exact queued assignment delta, spell leveling verifies native mastery advancement, Auto Cast verifies the audited `Spell.Fire` hook, and Auto Harvest requires one exact new native plot action. A no-op, partial, unexpectedly large, throwing, or unobservable result records structured before/after evidence and blocks that candidate or feature for the current lifecycle. Recovery is deliberately limited to scene, save-load, reset, or NG+ lifecycle invalidation; ordinary evaluation and configuration polling cannot silently retry an ambiguous mutation.

A definite Auto Buy rejection before any native call advances to the next ranked candidate and retries the rejected candidate on bounded exponential delays from 0.25 to 5 seconds. This prevents a permanently rejecting leader from starving healthy lower ranks without retrying an operation that may already have mutated game state.

The next-beta health pass publishes Auto Buy, Auto Cast, Auto Concept, Spell Leveling, and Auto Harvest independently through Common. Controls and tooltips now separate saved configuration from progression locks, lifecycle readiness, ordinary operation, temporary queue or safety blocks, unavailable contracts, partial degradation, and verified faults. The projection consumes existing engine evidence and publishes only canonical condition transitions; it does not add catalog scans, candidate work, or native mutations.

Automata consumes the shared Common lifecycle monitor. Scene entry/exit, save loading, save completion, gameplay-manager readiness, reset/NG+, and registry-rebuild observations advance one coalesced generation across the suite. Every generation transition cancels prepared Auto Buy, Auto Cast, Auto Concept, spell-level, and Auto Harvest work before another native mutation can start; equivalent callbacks from multiple installed suite plugins are idempotent within the same frame.

Queue and completion hooks now also mirror stable UUID/type invalidations through Common's bounded completed-frame bus. Manual queue changes and completion settlement still run synchronously first; the bus only coalesces secondary scheduling work. Auto Concept active-list changes wake concept evaluation as inventory events, while discovery or mastery progression also wakes spell-level capability evaluation. Unknown identities widen to the native family rather than using names or retaining native objects.

`AffordabilityMode` and `UpgradeAffordabilityMode` are independent:

- `BuyAll` accepts any natively affordable action that passes reserves.
- `Excess10`, `Excess100`, and `Excess1000` limit each resource cost to 1/10, 1/100, or 1/1000 of its current amount.

Reserves are an optional second policy. After each level, Automata requires enough resource for that level plus the larger of `AbsoluteReserve` and `cost × RelativeReserveMultiplier`. Because the game deducts each native cost immediately, a repeated or multiplied purchase rechecks the progressively lower live balance before every next level.

`PurchaseGrouping` defines how many independently validated levels one ranked candidate receives before Auto Buy advances. `Single` uses one level, `Fixed` groups Structures by `FixedGroupSize`, `BulkDevelopment` follows the live player value for Structures, and `ActionMultiplier` follows the live native multiplier for either purchase family. Upgrades receive one level in every mode except `ActionMultiplier`. Every group is capped to usable queue room.

Upgrade submission temporarily forces the native global multi-buy value to one and verifies that the captured value is restored afterward, including when the setter or purchase throws. If restoration cannot be confirmed through the native getter, further automated Upgrade mutations are quarantined for the process and removed from admission, cached ranking, and pending batches. Structure purchases do not use that global and remain independently eligible.

Continuation is always active: after each candidate's group, Auto Buy advances through the remaining prepared ranking and begins another pass while its batch quota and live queue room permit. The prepared next candidate does not require a catalog rescan. A lone candidate in `Single` mode may consume all usable room as an equivalent sequence of one-candidate passes. Native availability, current cost, affordability, reserves, maximum level, and queue admission are re-read before every level.

`PrioritizeCostAndQualityStructures=true` adds one purchase-priority tier above ordinary cost-ratio ordering. A Structure receives that tier only when its stable native effect definition and a non-mutating `ValueModifier.Adjust(1)` preview prove that it reduces `Cost`, `CostScaling`, or resource `AttributeCost`, or increases resource `Quality`. Dynamic targets, unknown properties, unreadable modifiers, and effects with the wrong direction receive no boost. The option changes ranking only after native availability, `CanPurchase`, exact current cost, affordability, reserves, allow/block lists, and queue safety have passed; it never makes a locked or unaffordable Structure eligible. Classification is lazy and cached, so it is not performed while the option is off and is not repeated in the per-frame evaluation path.

### Queue scheduling

The configured evaluation interval applies only while Auto Buy is idle. Once a scan begins, CPU-budgeted continuation slices resume on every Unity frame. The transitional legacy scheduler may submit up to 16 exact one-level purchases inside one mutation-owning coordinator lease, stopping earlier at its configured 1 ms slice further clamped to the shared hard budget remaining in the frame, live queue room, current group, batch quota, lifecycle change, or any failed safety check. A synchronous native call may cross the remaining boundary after it starts; the burst stops immediately afterwards. A native completion wakes prepared work immediately; the full queue is polled at 10 Hz only as a fallback.

This work remains on Unity's main thread because the game registries, ScriptableObjects, resources, and action queue are not thread-safe. `CpuBudgetMilliseconds` limits each frame's scan and purchase work without inserting the old full evaluation delay between continuation slices.

Auto Buy, Auto Cast, Auto Concept, and spell leveling still register their evaluation and native-mutation work with the legacy suite performance coordinator. Auto Harvest ServiceCycle is independent: it registers no coordinator work and Common may execute its one rotated action in a frame where a legacy feature also holds a mutation lease. That temporary stacked frame cost is accepted during migration. After Auto Buy migrates, the accepted ServiceCycle policy remains one action per active service per frame.

Automata is designed to be the only auto-buy plugin in the installation. Running another buyer against the same resources and queue is unsupported.

Automata finishes the configured purchase group for each candidate and advances through the prepared ranking before refreshing dirty resource and cost state. With the default `BulkDevelopment` policy, every ranked Structure receives one live group and every Upgrade receives one level, preventing a cheap Structure from monopolizing the queue indefinitely. It does not predict future levels: every mutation is admitted independently.

Active membership and ranked recommendation views use reused buffers and deterministic bounded walks; routine evaluations do not rebuild reflected wrappers or sort the complete registry. The slow ten-second registry reconciliation reuses wrappers when native identity is unchanged.

Native completion signals do not discard a safely prepared Fixed, Bulk Development, or action-multiplier group. The group continues through independently revalidated levels within the current bounded burst, then the prepared ranking advances. Completion effects settle before the next repeat pass, so a newly unlocked higher-ranked candidate can pre-empt that pass. Completed Structures and Upgrades retain their native identity and family; a completion burst coalesces into one refresh of each affected family. Manual queue changes still cancel stale prepared work immediately.

Rapid completion signals are generation-coalesced while a bounded lifecycle settlement is already running. They do not restart its cursor or an in-progress candidate scan; one follow-up generation retains eventual broad effect discovery. A recommendation produced across that window is advisory only and must still pass a fresh authoritative candidate and queue validation before its native mutation.

Routine active and locked-content lifecycle probes run on a fixed 250 ms cadence rather than once per purchase evaluation. Each maintenance slice checks at most eight active and sixteen slow-reconciliation entries, so faster queue turnover cannot multiply background reflection work.

Structures must pass native availability before Automata reads costs or calls the purchase contract. The supported native `UpgradeSO.CanPurchase()` contract combines affordability with lifecycle, requirements, and queue admission, so Automata calls it first and then decodes the exact current cost before classifying a false result. A proven reserve or affordability failure remains subscribed to its resource dependencies; a cost-safe false result is parked outside high-frequency quantity updates for bounded lifecycle or completion retry. Native bandwidth costs are explicitly identified through `ResourceSO.IsBandwidthResource()` and remain tracked because their admission uses missing usage rather than ordinary quantity.

Scan-cap deferrals are counted separately from evaluated rejections, transitions back to ready are counted explicitly, and identical verbose rejection examples remain suppressed until their typed blocker signature changes. Repeated native mutation failures are rate-limited per candidate while aggregate attempt/failure totals remain visible. Reflection metadata for queue room, queued-level verification, and the global multi-buy contract is cached only after exact signature validation. The live multi-buy variable itself is fetched again for every Upgrade level so save or lifecycle replacement cannot leave a stale native reference.

During a prepared ranked pass, each candidate refreshes its own cost immediately before its level. Shared resource-dependent invalidation is coalesced while later candidates are still live-revalidated against current resources and native state. A failed admission skips that candidate for the current pass; an ambiguous native mutation failure ends the pass so dirty state can settle safely. If the queue fills, the next ranked candidate remains prepared across the wait and is live-revalidated when a slot reopens.

Queue capacity is refreshed after that live candidate/cost/reserve validation and immediately before every queued mutation. The supported native adapter reads total capacity from `ActionManager.instance.actionableItems.maxQueuedItems.AsInt()` and live room from `ActionManager.GetRemainingRoom()`. Negative values, remaining room greater than capacity, missing native objects, or an invalid policy input reject the snapshot and submit no purchase. The snapshot derives occupancy and subtracts `LeaveQueueSlots` exactly once before applying the current batch usage limit.

## Auto Cast

Auto Cast follows equipped spell slot order and fires at most one new spell per evaluation. Empty slots are skipped, active auras are treated as already satisfied, channels pause the rotation, and persistent spells are never turned off automatically.

`FullCharge=true` holds charge-capable spells through the game's native charge-input contract until the full-charge point. While Automata owns that hold, the rest of the rotation pauses. The hold is released when charging completes, Auto Cast is disabled or emergency-blocked, the setting is turned off, manual spell input occurs, or the plugin shuts down. Set `FullCharge=false` to fire charge-capable spells immediately without charging.

A cast deferred by the shared coordinator is treated as a short-lived plan. Before firing, Automata rediscovers the slot and requires the stable recipe UUID, exact native Spell reference, runtime type, and slot index to match. Scene changes, save implementation, and player-manager restarts discard prepared casts immediately.

Every finite-cap resource used by immediate or drain costs must meet `StartResourcePercent`. Immediate costs also pass the shared reserve policy. Manual casting pauses automation for `ManualPauseSeconds`, and an existing manual target prompt is never replaced.

The button shows `OFF`, `ON`, or `!` when emergency disable blocks an active configuration. It uses the first equipped spell icon when available.

## Auto Concept

Auto Concept uses the shared `OrbModding.Common.AlchemyGameplayDomainClassifier` as its concept-versus-ordinary-alchemy identity boundary. The classifier resolves the exact `ConceptRecipes` UUID/type asset and requires each exact `AlchemyRecipeSO` registry member to carry mutation-grade static-contract, serialized-asset, exact-runtime-type, stable-identity, registry, and relationship evidence; contradictory ordinary-and-Scholar typing fails the lifecycle closed. Auto Concept separately resolves `ActiveConcepts` for native slot and quantity ownership. It never uses the global alchemy recipe registry as a concept catalog and never mutates ordinary alchemy.

`ActiveConcepts`, `ConceptRecipes`, and the spell-level `UnlockLevelAllSpells` upgrade resolve through Common's lifecycle-aware typed registry resolver. Missing or not-yet-registered assets remain retryable, while wrong type, UUID contradiction, or unavailable audited accessors fail closed with structured UUID/type/status/evidence diagnostics. A generation change invalidates the retained native reference before another read or mutation.

`Mode=Active` ranks discovered concepts by mastery level, fractional XP progress, and stable UUID. It assigns one instance to each currently compatible acquired slot before deepening active assignments. Depth is submitted as one native batched quantity change up to the recipe's live mastery maximum or `PerConceptQuantityCap`.

`SlotManagementMode=TimedCycle` is the default once Auto Concept is enabled. It permits complete settled replacement, but every assigned concept receives its full configured settled-active period before rotation; catching the current highest mastery never ends that session early, and least-recently-assigned ordering prevents discovered compatible concepts from being starved. `RotateAll` instead removes one settled active concept only if a compatible inactive concept has strictly lower mastery, waits for native settlement, and then adds the exact planned replacement. Equal mastery never rotates on UUID ordering alone. `PreserveManual` never removes the quantity present when Auto Concept starts; it can rotate only assignments that Automata added itself.

Every newly assigned lower-mastery concept in `RotateAll` or `PreserveManual` receives a catch-up training session. The session captures the highest eligible mastery level and fractional progress at assignment time, becomes timed only after the native quantity is settled and active, and protects the assignment until it reaches that target or `TrainingPeriodSeconds` elapses. `TimedCycle` uses the same timer but never applies the catch-up shortcut. The default is 300 seconds and the accepted range is 10 through 3600 seconds. Native setup time does not consume the period, and the controller schedules the exact next session deadline even when its idle fallback is longer.

The `CN ON/OFF` gameplay button toggles Auto Concept and represents configured intent; emergency blocking and other runtime health remain secondary tooltip evidence. `ShowToggleButton` defaults to true. Spell-leveling state is shown on the Automata Auto Buy tooltip instead.

`FallbackEvaluationIntervalSeconds` is an Advanced setting, not a rotation delay. It defaults to 300 seconds and accepts 10 through 1800 as the maximum idle delay between full plan calculations; native changes can request earlier passes. Existing `RebalanceIntervalSeconds` and `RebalanceIntervalMinutes` values migrate automatically.

Before every add or rotation, Automata reconstructs that exact prospective native drain vector, rejects every positive drain whose authoritative resource state is zero, converts the remainder through each resource's live quality with `ResourceSO.GetTrueSpend`, and compares the projected rate with `RateReservePercent`. Finite resources must also meet `MinimumResourcePercent`. A replacement whose resource is at zero is skipped without blocking other resource-safe concepts or acquired slots in the timed order. Unknown vectors, identity mismatches, incompatible slots, and changed mastery limits fail closed. A 1 Hz watchdog checks only cached active assignments; if the native drain ratio falls below `MinimumDrainRatio` or a drained resource reaches zero, it schedules removal of only the quantity recorded as Automata-owned.

Enabling the feature initializes the scoped shared classifier and snapshots current Active Concept quantities for ownership and rollback accounting. Disabled Auto Concept neither initializes nor rebuilds classifier/catalog evidence. Unexpected settled changes are rebaselined as player-owned. `PreserveManual` never replaces that baseline; `RotateAll` explicitly permits a complete settled assignment to be replaced for mastery balancing, but the drain watchdog still rolls back only Automata-added quantity. Disabling the feature stops work and leaves native quantities unchanged. Save loads, scene changes, and manager lifecycle resets (including reset/NG+ manager restarts) invalidate classifier and runtime references and rebuild a new baseline only after Auto Concept is active again.

## Diagnostics

Transient shared classifier readiness failures use the existing 30-second Auto Concept warning gate. A contradictory or permanently invalid concept-domain contract blocks Auto Concept for that lifecycle and is logged once; `Unknown` evidence never falls back to ordinary names or broad alchemy membership.

Set `Diagnostics.EnableOperationalLogging=true` only while troubleshooting. `DecisionLogLevel=Off` suppresses all normal Auto Buy and Auto Cast records even when the legacy enable switch remains true. Summary mode rate-limits recommendations, batch totals, casts, and queue waits to low-frequency records. Verbose mode additionally records individual purchases, bounded candidate rejections, and detailed Auto Cast resource snapshots.

Auto Buy decisions use append-only Common codes rather than parsing diagnostic text. Candidate threshold parking, rejection telemetry, the latest tooltip status, and verbose rejection records all consume the same immutable decision. Observed quantities and wording can change without producing a new condition; stable thresholds, identities, policy, queue limits, and native states do produce a transition. Equivalent conditions are not republished to future Insights subscribers.

Every Orb Automata message includes local date, time to milliseconds, and UTC offset so runtime reports can identify when a failure began.
Successful Auto Concept initialization records separate scoped-recipe, active-loadout, and eligible-candidate counts. When operational logging is enabled, assignment reservation, settled training start, catch-up/timeout completion, rotation, and a rate-limited no-change summary distinguish an active or idle balancer from a missed evaluation.

Orb of Creation's LeanTween pool defaults to 400 simultaneous tweens. AutobuyOrb raises that capacity because very large purchase bursts can create enough UI popups to exhaust it. This is separate from queue scheduling; Automata does not currently override the global tween pool. If the BepInEx log reports LeanTween exhaustion or UI animations begin disappearing during unusually large batches, treat a restart-time tween-capacity option as a separate performance feature.
