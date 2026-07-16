# Orb Automata

Orb Automata is a BepInEx 5 automation suite for Orb of Creation. Version `0.4.2` provides Auto Buy and Auto Cast through the game's native purchase, queue, and spell APIs.

## Build

Set `OOC_GAME_DIR` to the Orb of Creation installation root, then build the project:

```powershell
$env:OOC_GAME_DIR='C:\Program Files (x86)\Steam\steamapps\common\Orb of Creation'
dotnet build .\src\OrbAutomata\OrbAutomata.csproj -c Release
```

Do not commit the referenced BepInEx, Unity, Harmony, or game DLLs.

## Release defaults

- Auto Buy starts `Active` with separate `Excess100` thresholds for structures and upgrades.
- Auto Cast starts `Disabled` and can be toggled with `Left Alt + X` or its queue-adjacent button.
- Both mode selectors expose only `Disabled` and `Active`.
- One native queue slot is reserved for manual actions.
- Structure repeat follows the live Bulk Development value.
- Native action-multiplier handling is off by default.
- Absolute and relative reserves default to zero; affordability modes provide the default spending margin.
- Operational decision logging is off; startup, warning, and error records remain enabled.
- `EmergencyDisable` immediately stops new automated purchases and casts.

The former runtime-probe, per-session purchase-limit, DryRun, expert-override, and Auto Research settings are not part of the release configuration or Mod Config UI. Existing legacy keys are removed when the configuration is loaded and saved.

## Auto Buy

Automata discovers native `StructureSO` and `UpgradeSO` candidates into a lifecycle-aware UUID index. It keeps locked content for bounded retry, quarantines missing or contradictory native identities, and reconciles the native registries incrementally. Ordinary evaluations refresh only dirty candidates, maintain deterministic cached ranking, and revalidate every level immediately before calling the native purchase method.

Resource dependencies are learned from the native current-cost result. Each referenced native resource is read once per evaluation epoch, including true quantity, quality, capacity, and effective attribute-cost modifier. Quantity or quality changes dirty only dependent candidates; save loads, gameplay-manager restarts, scene changes, and NG+ start a new lifecycle epoch. Unknown cost, resource, lifecycle, or identity state fails closed.

The installed-game `ResourceCostList` and `ResourceTuple` schema is validated once per native type. Every tuple must decode before any reserve decision uses the vector; adapter failures are quarantined with bounded retry and rate-limited warnings. Empty native cost lists remain valid free actions when native `CanPurchase` accepts them. Manual Structure queue and Upgrade purchase signals invalidate only the matching native candidate and cancel stale planned work. Synchronous signals caused by Automata's own purchase are correlated to that exact native object, so a CPU-sliced Fixed, Bulk Development, or action-multiplier group can finish its initial queue-room-clamped limit. Structure/Upgrade completion hooks remain broad because completed effects may change other candidates.

`AffordabilityMode` and `UpgradeAffordabilityMode` are independent:

- `BuyAll` accepts any natively affordable action that passes reserves.
- `Excess10`, `Excess100`, and `Excess1000` limit each resource cost to 1/10, 1/100, or 1/1000 of its current amount.

Reserves are an optional second policy. After each level, Automata requires enough resource for that level plus the larger of `AbsoluteReserve` and `cost × RelativeReserveMultiplier`. Because the game deducts each native cost immediately, a repeated or multiplied purchase rechecks the progressively lower live balance before every next level.

When `RespectActionMultiplier=true`, Automata reads the game's current action multiplier, caps it to free queue room, and submits that many one-level native purchases. It does not pass an uncapped multiplier into `UpgradeSO.Purchase()`, whose native implementation does not cap itself to remaining queue room. Holding a modifier key can change the live multiplier, so the option remains off by default.

Upgrade submission temporarily forces the native global multi-buy value to one and verifies that the captured value is restored afterward, including when the setter or purchase throws. If restoration cannot be confirmed through the native getter, further automated Upgrade mutations are quarantined for the process and removed from admission, cached ranking, and pending batches. Structure purchases do not use that global and remain independently eligible.

When action-multiplier handling is off, structures use `StructureRepeatMode`: `BulkDevelopment` follows the live player value, `Fixed` uses `FixedStructureLevelsPerCandidate`, and `Single` buys one level. Upgrades remain one level per ranked candidate.

### Queue scheduling

The configured evaluation interval applies only while Auto Buy is idle. Once a scan begins, CPU-budgeted continuation slices resume on every Unity frame. Once a ranked batch is prepared, it also checks queue room every frame and feeds the first newly available slot without waiting for the queue to become nearly empty or performing another scan. After a successful batch, the following scan begins on the next frame while existing native actions continue.

This work remains on Unity's main thread because the game registries, ScriptableObjects, resources, and action queue are not thread-safe. `CpuBudgetMilliseconds` limits each frame's scan and purchase work without inserting the old full evaluation delay between continuation slices.

Auto Buy and Auto Cast register separate read and native-mutation work with the suite performance coordinator. Catalog, lifecycle, cost, and admission reads resume only after a cooperative read lease; every queued level or fired spell requires its own native-mutation lease. The suite admits at most one such mutation per Unity frame. A denied lease retains the pending candidate and repeat count, so Fixed, Bulk Development, and action-multiplier groups keep their initial queue-room clamp instead of restarting. Disabled automation clears its pending work and stops requesting leases.

Automata is designed to be the only auto-buy plugin in the installation. Running another buyer against the same resources and queue is unsupported.

Structure repeat still follows `BulkDevelopment`, `Fixed`, or `Single` exactly. Automata finishes the configured group for the selected Structure, then refreshes dirty resource and cost state and reranks before mutating a different candidate. It does not predict future levels or assign special priority to cost-reduction effects.

Active membership and ranked recommendation views use reused buffers and deterministic bounded walks; routine evaluations do not rebuild reflected wrappers or sort the complete registry. The slow ten-second registry reconciliation reuses wrappers when native identity is unchanged.

## Auto Cast

Auto Cast follows equipped spell slot order and fires at most one new spell per evaluation. It skips empty and charged slots, treats an active aura as already satisfied, pauses while a channel is active, respects native readiness and targeting, and never turns persistent spells off.

A cast deferred by the shared coordinator is treated as a short-lived plan. Before firing, Automata rediscovers the slot and requires the stable recipe UUID, exact native Spell reference, runtime type, and slot index to match. Scene changes, save implementation, and player-manager restarts discard prepared casts immediately.

Every finite-cap resource used by immediate or drain costs must meet `StartResourcePercent`. Immediate costs also pass the shared reserve policy. Manual casting pauses automation for `ManualPauseSeconds`, and an existing manual target prompt is never replaced.

The button shows `OFF`, `ON`, or `!` when emergency disable blocks an active configuration. It uses the first equipped spell icon when available.

## Diagnostics

Set `Diagnostics.EnableOperationalLogging=true` only while troubleshooting. Summary mode records recommendations, successful work, batches, and queue waits. Verbose mode also records bounded candidate rejections and detailed Auto Cast resource snapshots.

Orb of Creation's LeanTween pool defaults to 400 simultaneous tweens. AutobuyOrb raises that capacity because very large purchase bursts can create enough UI popups to exhaust it. This is separate from queue scheduling; Automata does not currently override the global tween pool. If the BepInEx log reports LeanTween exhaustion or UI animations begin disappearing during unusually large batches, treat a restart-time tween-capacity option as a separate performance feature.
