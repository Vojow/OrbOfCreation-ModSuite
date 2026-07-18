# Orb Automata

Orb Automata is a BepInEx 5 automation suite for Orb of Creation. Version `0.8.1` fixes queue-filling Auto Buy so multiple affordable Structures and Upgrades share the prepared queue pass instead of one candidate monopolizing it, alongside Auto Cast, opt-in Auto Concept rotation, and progression-aware spell leveling through the game's native APIs.

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
- All three mode selectors expose only `Disabled` and `Active`.
- One native queue slot is reserved for manual actions.
- When several Structures or Upgrades are eligible, each receives one live-validated level per ranked pass; a lone candidate may fill all usable queue room.
- Cost/quality Structure priority is off by default and can be enabled with `PrioritizeCostAndQualityStructures`.
- Native action-multiplier handling is off by default.
- Absolute and relative reserves default to zero; affordability modes provide the default spending margin.
- Operational decision logging is off; startup, warning, and error records remain enabled.
- `EmergencyDisable` immediately stops new automated purchases, casts, concept mutations, and spell levels.
- Orb Mod Config leaves each mode, toggle shortcut, status-button visibility, emergency control, and diagnostics editable while locking inactive feature tuning. Nested Auto Buy fields also require their applicable include, batch, multiplier, or repeat policy.

The former runtime-probe, per-session purchase-limit, DryRun, expert-override, and Auto Research settings are not part of the release configuration or Mod Config UI. Existing legacy keys are removed when the configuration is loaded and saved.

## Auto Buy

Automata discovers native `StructureSO` and `UpgradeSO` candidates into a lifecycle-aware UUID index. It keeps locked content for bounded retry, quarantines missing or contradictory native identities, and reconciles the native registries incrementally. Ordinary evaluations refresh only dirty candidates, maintain deterministic cached ranking, and revalidate every level immediately before calling the native purchase method.

`AutoLevelSpells=true` runs while Auto Buy is active and is configured in the Auto Buy section. Its capability follows native progression automatically: `Locked` while no discovered spell passes its own leveling prerequisites, `Single` after that contract unlocks, and `All` after the exact `UnlockLevelAllSpells` Upgrade has completed. Single mode pays the spell's live native cost and confirms one native `PurchaseLevel()` per mutation. All mode calls the game's native `SpellManager.TryLevelAllSpells()` action. Queued upgrades do not count, affordability and readiness are revalidated immediately before mutation, and any ambiguous failure after a cost attempt blocks further spell leveling for that lifecycle.

Resource dependencies are learned from the native current-cost result. Each referenced native resource is read once per evaluation epoch, including true quantity, quality, capacity, and effective attribute-cost modifier. Quantity or quality changes dirty only dependent candidates; save loads, gameplay-manager restarts, scene changes, and NG+ start a new lifecycle epoch. Unknown cost, resource, lifecycle, or identity state fails closed.

The installed-game `ResourceCostList` and `ResourceTuple` schema is validated once per native type. Every tuple must decode before any reserve decision uses the vector; adapter failures are quarantined with bounded retry and rate-limited warnings. Empty native cost lists remain valid free actions when native `CanPurchase` accepts them. Manual Structure queue and Upgrade purchase signals invalidate only the matching native candidate and cancel stale planned work. Synchronous signals caused by Automata's own purchase are correlated to that exact native object, so a CPU-sliced Fixed, Bulk Development, or action-multiplier group can finish its initial queue-room-clamped limit. Structure/Upgrade completion hooks remain broad because completed effects may change other candidates.

`AffordabilityMode` and `UpgradeAffordabilityMode` are independent:

- `BuyAll` accepts any natively affordable action that passes reserves.
- `Excess10`, `Excess100`, and `Excess1000` limit each resource cost to 1/10, 1/100, or 1/1000 of its current amount.

Reserves are an optional second policy. After each level, Automata requires enough resource for that level plus the larger of `AbsoluteReserve` and `cost × RelativeReserveMultiplier`. Because the game deducts each native cost immediately, a repeated or multiplied purchase rechecks the progressively lower live balance before every next level.

When `RespectActionMultiplier=true`, Automata reads the game's current action multiplier, caps it to free queue room, and submits that many one-level native purchases. It does not pass an uncapped multiplier into `UpgradeSO.Purchase()`, whose native implementation does not cap itself to remaining queue room. Holding a modifier key can change the live multiplier, so the option remains off by default.

Upgrade submission temporarily forces the native global multi-buy value to one and verifies that the captured value is restored afterward, including when the setter or purchase throws. If restoration cannot be confirmed through the native getter, further automated Upgrade mutations are quarantined for the process and removed from admission, cached ranking, and pending batches. Structure purchases do not use that global and remain independently eligible.

When action-multiplier handling is off, `RepeatWhileAffordable=true` gives each prepared eligible Structure or Upgrade one level before repeating the ranked pass. The already prepared next candidate continues on the following admitted frame without a catalog rescan; if the shared native action queue fills, that candidate waits for the first reopened slot. A lone eligible candidate can still fill every usable slot. Automata does not divide the current balance by one potentially stale cost: it re-reads and revalidates native availability, current cost, affordability threshold, reserves, maximum level, and queue admission before every level.

Set `RepeatWhileAffordable=false` to restore bounded Structure groups through `StructureRepeatMode`: `BulkDevelopment` follows the live player value, `Fixed` uses `FixedStructureLevelsPerCandidate`, and `Single` buys one level. In that fallback mode, Upgrades remain one level per ranked candidate.

`PrioritizeCostAndQualityStructures=true` adds one purchase-priority tier above ordinary cost-ratio ordering. A Structure receives that tier only when its stable native effect definition and a non-mutating `ValueModifier.Adjust(1)` preview prove that it reduces `Cost`, `CostScaling`, or resource `AttributeCost`, or increases resource `Quality`. Dynamic targets, unknown properties, unreadable modifiers, and effects with the wrong direction receive no boost. The option changes ranking only after native availability, `CanPurchase`, exact current cost, affordability, reserves, allow/block lists, and queue safety have passed; it never makes a locked or unaffordable Structure eligible. Classification is lazy and cached, so it is not performed while the option is off and is not repeated in the per-frame evaluation path.

### Queue scheduling

The configured evaluation interval applies only while Auto Buy is idle. Once a scan begins, CPU-budgeted continuation slices resume on every Unity frame. A prepared ranking advances by one native mutation per admitted frame, so multiple candidates enter the queue in rapid succession without unsafe background-thread game access. A full queue is polled at 10 Hz and retains the next candidate for the first newly available slot. After one pass, reranking begins on the next frame while existing native actions continue.

This work remains on Unity's main thread because the game registries, ScriptableObjects, resources, and action queue are not thread-safe. `CpuBudgetMilliseconds` limits each frame's scan and purchase work without inserting the old full evaluation delay between continuation slices.

Auto Buy and Auto Cast register separate read and native-mutation work with the suite performance coordinator. Catalog, lifecycle, cost, and admission reads resume only after a cooperative read lease; every queued level, fired spell, or normal full-charge release requires its own native-mutation lease. The suite admits at most one such mutation per Unity frame. Auto Buy receives a bounded three-turn scheduling weight while it has continuous work, then yields to the next waiting subsystem; this improves queue filling without starving Mentor or Auto Cast. A denied lease retains the pending candidate, repeat count, or owned charge hold, so Fixed, Bulk Development, action-multiplier groups, and charge release continue without restarting or overlapping another suite mutation. Disabled automation clears pending work and stops requesting leases; manual input, scene exit, emergency stop, and unload still release an owned charge hold immediately to avoid trapping player control.

Automata is designed to be the only auto-buy plugin in the installation. Running another buyer against the same resources and queue is unsupported.

Automata finishes the configured repeat group for each candidate and advances through the prepared ranking before refreshing dirty resource and cost state. For the default affordable policy, that group is one level whenever multiple recommendations exist, preventing the cheapest candidate from consuming the entire queue; if only one recommendation exists, its group may use all usable room. It does not predict future levels: every mutation is admitted independently.

Active membership and ranked recommendation views use reused buffers and deterministic bounded walks; routine evaluations do not rebuild reflected wrappers or sort the complete registry. The slow ten-second registry reconciliation reuses wrappers when native identity is unchanged.

Native completion signals no longer discard a safely prepared Fixed, Bulk Development, or action-multiplier group. The group continues one independently revalidated level per admitted frame, then all completion effects observed during that window settle once before the next ranked group. Manual queue changes still cancel stale prepared work immediately.

Routine active and locked-content lifecycle probes run on a fixed 250 ms cadence rather than once per purchase evaluation. Each maintenance slice checks at most eight active and sixteen slow-reconciliation entries, so faster queue turnover cannot multiply background reflection work.

Structures must pass native availability before Automata reads costs or calls the purchase contract. The supported native `UpgradeSO.CanPurchase()` contract combines affordability with lifecycle, requirements, and queue admission, so Automata calls it first and then decodes the exact current cost before classifying a false result. A proven reserve or affordability failure remains subscribed to its resource dependencies; a cost-safe false result is parked outside high-frequency quantity updates for bounded lifecycle or completion retry. Native bandwidth costs are explicitly identified through `ResourceSO.IsBandwidthResource()` and remain tracked because their admission uses missing usage rather than ordinary quantity.

Scan-cap deferrals are counted separately from evaluated rejections, transitions back to ready are counted explicitly, and repeated native mutation failures are rate-limited per candidate while aggregate attempt/failure totals remain visible. Reflection metadata for queue room, queued-level verification, and the global multi-buy contract is cached only after exact signature validation. The live multi-buy variable itself is fetched again for every Upgrade level so save or lifecycle replacement cannot leave a stale native reference.

During a prepared ranked pass, each candidate refreshes its own cost immediately before its level. Shared resource-dependent invalidation is coalesced while later candidates are still live-revalidated against current resources and native state. A failed admission skips that candidate for the current pass; an ambiguous native mutation failure ends the pass so dirty state can settle safely. If the queue fills, the next ranked candidate remains prepared across the wait and is live-revalidated when a slot reopens.

## Auto Cast

Auto Cast follows equipped spell slot order and fires at most one new spell per evaluation. Empty slots are skipped, active auras are treated as already satisfied, channels pause the rotation, and persistent spells are never turned off automatically.

`FullCharge=true` holds charge-capable spells through the game's native charge-input contract until the full-charge point. While Automata owns that hold, the rest of the rotation pauses. The hold is released when charging completes, Auto Cast is disabled or emergency-blocked, the setting is turned off, manual spell input occurs, or the plugin shuts down. Set `FullCharge=false` to fire charge-capable spells immediately without charging.

A cast deferred by the shared coordinator is treated as a short-lived plan. Before firing, Automata rediscovers the slot and requires the stable recipe UUID, exact native Spell reference, runtime type, and slot index to match. Scene changes, save implementation, and player-manager restarts discard prepared casts immediately.

Every finite-cap resource used by immediate or drain costs must meet `StartResourcePercent`. Immediate costs also pass the shared reserve policy. Manual casting pauses automation for `ManualPauseSeconds`, and an existing manual target prompt is never replaced.

The button shows `OFF`, `ON`, or `!` when emergency disable blocks an active configuration. It uses the first equipped spell icon when available.

## Auto Concept

Auto Concept resolves the exact `ConceptRecipes` and `ActiveConcepts` assets by UUID and validates every candidate against the three Scholar concept type UUIDs. It never uses the global alchemy recipe registry as a concept catalog and never mutates ordinary alchemy.

`Mode=Active` ranks discovered concepts by mastery level, fractional XP progress, and stable UUID. It assigns one instance to each currently compatible acquired slot before deepening active assignments. Depth is submitted as one native batched quantity change up to the recipe's live mastery maximum or `PerConceptQuantityCap`.

`SlotManagementMode=TimedCycle` is the default once Auto Concept is enabled. It permits complete settled replacement, but every assigned concept receives its full configured settled-active period before rotation; catching the current highest mastery never ends that session early, and least-recently-assigned ordering prevents discovered compatible concepts from being starved. `RotateAll` instead removes one settled active concept only if a compatible inactive concept has strictly lower mastery, waits for native settlement, and then adds the exact planned replacement. Equal mastery never rotates on UUID ordering alone. `PreserveManual` never removes the quantity present when Auto Concept starts; it can rotate only assignments that Automata added itself.

Every newly assigned lower-mastery concept in `RotateAll` or `PreserveManual` receives a catch-up training session. The session captures the highest eligible mastery level and fractional progress at assignment time, becomes timed only after the native quantity is settled and active, and protects the assignment until it reaches that target or `TrainingPeriodSeconds` elapses. `TimedCycle` uses the same timer but never applies the catch-up shortcut. The default is 300 seconds and the accepted range is 10 through 3600 seconds. Native setup time does not consume the period, and the controller schedules the exact next session deadline even when its idle fallback is longer.

The `CN ON/OFF/!` gameplay button toggles Auto Concept and reports emergency blocking. `ShowToggleButton` defaults to true. Spell-leveling state is shown on the Automata Auto Buy tooltip instead.

`FallbackEvaluationIntervalSeconds` is an Advanced setting, not a rotation delay. It defaults to 300 seconds and accepts 10 through 1800 as the maximum idle delay between full plan calculations; native changes can request earlier passes. Existing `RebalanceIntervalSeconds` and `RebalanceIntervalMinutes` values migrate automatically.

Before every add or rotation, Automata reconstructs that exact prospective native drain vector, rejects every positive drain whose authoritative resource state is zero, converts the remainder through each resource's live quality with `ResourceSO.GetTrueSpend`, and compares the projected rate with `RateReservePercent`. Finite resources must also meet `MinimumResourcePercent`. A replacement whose resource is at zero is skipped without blocking other resource-safe concepts or acquired slots in the timed order. Unknown vectors, identity mismatches, incompatible slots, and changed mastery limits fail closed. A 1 Hz watchdog checks only cached active assignments; if the native drain ratio falls below `MinimumDrainRatio` or a drained resource reaches zero, it schedules removal of only the quantity recorded as Automata-owned.

Enabling the feature snapshots current Active Concept quantities for ownership and rollback accounting. Unexpected settled changes are rebaselined as player-owned. `PreserveManual` never replaces that baseline; `RotateAll` explicitly permits a complete settled assignment to be replaced for mastery balancing, but the drain watchdog still rolls back only Automata-added quantity. Disabling the feature stops work and leaves native quantities unchanged. Save loads, scene changes, and manager lifecycle resets discard live references and rebuild a new baseline.

## Diagnostics

Set `Diagnostics.EnableOperationalLogging=true` only while troubleshooting. `DecisionLogLevel=Off` suppresses all normal Auto Buy and Auto Cast records even when the legacy enable switch remains true. Summary mode rate-limits recommendations, batch totals, casts, and queue waits to low-frequency records. Verbose mode additionally records individual purchases, bounded candidate rejections, and detailed Auto Cast resource snapshots.

Every Orb Automata message includes local date, time to milliseconds, and UTC offset so runtime reports can identify when a failure began.
Successful Auto Concept initialization records separate scoped-recipe, active-loadout, and eligible-candidate counts. When operational logging is enabled, assignment reservation, settled training start, catch-up/timeout completion, rotation, and a rate-limited no-change summary distinguish an active or idle balancer from a missed evaluation.

Orb of Creation's LeanTween pool defaults to 400 simultaneous tweens. AutobuyOrb raises that capacity because very large purchase bursts can create enough UI popups to exhaust it. This is separate from queue scheduling; Automata does not currently override the global tween pool. If the BepInEx log reports LeanTween exhaustion or UI animations begin disappearing during unusually large batches, treat a restart-time tween-capacity option as a separate performance feature.
