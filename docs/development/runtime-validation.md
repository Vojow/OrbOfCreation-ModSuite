# Local runtime validation protocol

[Back to compatibility and testing](testing.md)

## Purpose

Work completed on a computer without Orb Of Creation can prove ordinary C# behavior, but it cannot prove that a plugin compiles against, loads into, or behaves correctly with the installed game. Validation therefore moves through increasingly invasive gates. A failure stops that mod at the current gate; passing a later gate never excuses a failed earlier one.

No runtime test may directly edit an active `.sav` file. Close the game before copying or restoring saves.

## Current validation status

Baseline checked on 2026-07-14:

- Installed Unity version: `6000.0.70`, Mono backend.
- Installed BepInEx version: `5.4.23.5`.
- `Assembly-CSharp.dll`: `5845797D40E4631517DE9F4D6296F10C7381AAD5DA733128B2C4685E66E8711F`.
- `Assembly-CSharp-firstpass.dll`: `D14D52652591ED3CB5ACF55186478DD3873F3C836871E0F68AA861D1767F480A`.
- Both installed assembly hashes match the audited repository baseline.
- On 2026-07-19, all 568 supported game-independent behavior and knowledge-map tests passed in Release with `UseGameStubs=true` on the combined invalidation, structured-decision, feature-health, circuit-breaker, queue-acceptance, and action-family-ownership stack.
- The checked-in coverage gate passed at 71.94% overall production line coverage: Automata 73.14%, Mentor 72.09%, Mod Config 26.40%, and Orb Modding Common 87.76%.
- On 2026-07-19, all 19 supported installed-game metadata contract tests passed against the audited assemblies.
- Automata, Mentor, and Mod Config built in Release against the real installed game references with zero warnings. The required Unity facade, UI, and TextMeshPro references are part of the build contract.
- The supported-suite package rehearsal contained only Automata, Mentor, Mod Config, and Orb Modding Common DLLs; experimental DLL guards passed.
- All seven deterministic Auto Buy performance scenarios passed, and the issue #24 run matched every checked-in queue, refill, evaluation, and operation-density baseline metric exactly.

The static ILSpy contract check confirmed the active `StructureSO` and `UpgradeSO` registries, availability, costs, queue state, and purchase methods; `ActionManager.GetRemainingRoom()` plus the authoritative `ActionManager.instance.actionableItems.maxQueuedItems` capacity chain; native multi-buy access/restoration; concept assignment contracts; spell-leveling contracts; and Mentor's three progression-domain contracts. Focused `Assembly-CSharp`-shaped fixtures now execute those production reflection paths headlessly; this strengthens regression coverage without replacing runtime UAT.

### Historical Automata Auto Buy runtime evidence

The following records explain earlier safety and performance decisions. Their probe-only `DryRun`, Research, and per-session-limit settings are not part of the current public configuration.

Active probe completed on 2026-07-14 with Orb Automata `0.1.2`:

- The installed assembly audit matched the baseline and the gameplay queue initialized after the title/load transition.
- Auto Buy parsed one allowed UUID, discovered the complete 409-candidate Structure/Upgrade registry, and respected the 1,024 candidate cap.
- `Imbue Dimensional` (`6489c59d-306d-4085-803a-49e09fdc5099`) was admitted at a logged one-level cost of `5.5e5` Dimensional Core against `4.097e60` available.
- Active mode invoked the native Upgrade purchase path once and verified that the queued purchase level increased.
- `SessionPurchases=1` was logged, and the following completed scan stopped at `ActivePurchaseLimitPerSession=1` without a second purchase.
- Research remained disabled throughout the probe.

A subsequent 10-purchase endurance probe also passed:

- Ten sequential native Upgrade purchase calls succeeded and were counted as `SessionPurchases=1` through `SessionPurchases=10`.
- Purchase eleven was blocked by `ActivePurchaseLimitPerSession=10`.
- No Automata warning, exception, purchase failure, missing-method failure, or multi-buy restoration error appeared in the complete BepInEx log.
- The next queued-level cost changed from `1.6e7` before the batch to `9.7e21` after it, confirming that Upgrade queued-level cost scaling was refreshed.
- The probe revealed that read-only registry scans continued after the session limit. Version `0.1.3` stops both purchases and candidate scans once the limit is reached; a regression test covers this behavior.
- The `0.1.3` repeat confirmed ten purchases followed immediately by the terminal message `no further purchases or candidate scans`; the player manually reported that the resulting in-game state looked correct, with no visible level or multi-buy anomaly.

At the time of this probe, continuous mode remained gated on visual confirmation of one-level queue/completion, expected resource deduction, and native multi-buy restoration. The temporary `ActivePurchaseLimitPerSession` guard was removed before the public configuration was finalized.

Structure-only DryRun evidence on 2026-07-14 with `0.1.3`:

- With Upgrades excluded and no allowlist, Automata discovered 180 Structure candidates.
- `Concentration` (`bf4e596c-3ee0-4194-b0c2-d4a7af1a85f6`) was selected consistently.
- Its logged next-level cost was `9.679e44` Sigil against `6.398e60` available, for a maximum cost ratio of `1.513e-16` under `Excess100`.
- No purchase occurred in DryRun.

The subsequent Structure endurance probe passed with ten sequential native purchases, one terminal session-limit record, no Automata warnings/errors, and no post-limit purchase or candidate-scan records. Sanitized Upgrade and Structure transcripts are committed under `tests/fixtures/automata/` and asserted by the portable test suite.

The unrestricted personal-beta log then exposed queue downtime: version `0.1.3` bought only one ranked candidate per complete 409-candidate scan, and budgeted scans could span several 0.5-second intervals. Version `0.1.4` introduced a configurable ranked batch (`MaxPurchasesPerBatch`, default 8), but runtime evidence showed every batch ending at one purchase with `CpuLimited=True`: a native purchase took roughly 8–11 ms while the shared budget was 4 ms.

Version `0.1.5` retains the ranked candidates after the scan and continues the same batch on consecutive frames. Each frame remains CPU-bounded, but continuation bypasses the evaluation interval and does not rescan. Every batch member remains distinct and is revalidated against live resources, reserves, queue room, and the session limit immediately before purchase. Portable tests force a purchase to exceed the per-frame budget and prove that the remaining ranked candidates complete on following frames from one catalog scan. Runtime queue-utilization evidence for `0.1.5` is still required.

Version `0.1.6` added two opt-in policies. `BatchSizingMode=FillAvailableQueue` derives batch completion from live queue room instead of a fixed count. `StructureRepeatMode=BulkDevelopment` reads the current `Player.GetBulkDevelopment()` value for each structure group and queues that many consecutive levels through independently revalidated one-level purchases. This deliberately avoids native upgrade multi-buy, whose `Purchase()` loop does not clamp its queued level count to `ActionManager.GetRemainingRoom()`. Runtime validation on 2026-07-14 confirmed live Bulk Development grouping and queue-aware filling; version `0.1.8` therefore makes `BulkDevelopment` the default repeat policy while retaining `Fixed` and `Single` overrides.

Orb Automata `0.8.0` was stress-tested on 2026-07-17 with a disposable copy of Save 1 in which only 13 ordinary spend resources were raised to `9e60`; bandwidth, point, weight, and experience resources were left unchanged. Under `BuyAll`, `FillAvailableQueue`, one reserved queue slot, and native repeat-while-affordable behavior, the pre-fix build completed 140 native purchases with zero failures but evaluated 58,973 candidates in roughly one minute because it discarded the prepared ranked batch whenever a one-slot repeat group completed. The tested correction kept a prepared next candidate across a full-queue wait, but still ended the selected candidate's repeat group at the queue-room snapshot taken when that group began.

The repeated profile completed 150 native purchases with zero failures and 1,483 candidate evaluations, a 97.5% reduction from the pre-fix run. Logs confirmed queue waits, CPU slicing, live slot feeding, and no native mutation failures. A broader synthetic profile that raised 58 serialized resource quantities to `9e150` was rejected as a validation harness because the game did not complete loading; it produced no actionable Automata exception. The original saves, installed plugins, configuration, and log were hash-restored after both runs, and the game was closed.

Version `0.8.1` corrects the remaining multi-candidate handoff behavior: when several recommendations exist, each receives one live-validated level per prepared ranked pass, while a lone affordable candidate can still fill all usable queue room. Portable tests cover a 200-level lone-candidate fill and consecutive-frame Structure/Upgrade handoff without per-candidate rescans or the idle evaluation interval.

The next-beta structured-decision change requires a focused UAT observation without altering the queue workload: hover Auto Buy while disabled, eligible, resource-blocked, natively blocked, and waiting for queue room; confirm the tooltip and rate-limited log describe the same stable condition. Change only an observed resource quantity below the same threshold and confirm it does not create repeated condition noise, then cross the threshold and confirm the decision transitions. The deterministic completion-storm and periodic-completion reports must remain equal to the reviewed queue-output and operation-count baseline before this check.

The installed `0.8.1` build passed its focused desktop queue test on 2026-07-18. Slot 3 was replaced with a disposable copy of the cloud-refreshed Slot 1, and only the same 13 ordinary spend resources used by the earlier profile were raised to a minimum of `9e60`; eight records required a change. Bandwidth, point, weight, experience, and other special resources were untouched. With Structure and Upgrade affordability set to `BuyAll`, `FillAvailableQueue`, one reserved slot, affordable repeats enabled, action-multiplier handling off, and unrelated suite automation disabled, the visible shared queue moved from `14/304` after load to `174/304` after five seconds and `302/304` after ten seconds. Different queue icons appeared immediately rather than one candidate consuming the entire queue.

The closed-run log recorded 1,797 successful native submissions from 166 distinct candidates: 1,757 Structure levels and 40 Upgrade levels, with zero native purchase failures. The original Automata and Mentor configurations were restored after the run, the active Slot 1 SHA-256 remained unchanged, and pre-test Slot 3 plus installed-DLL backups were retained for recovery.

### Automata Auto Cast control runtime evidence

A 2026-07-17 static audit of the hash-matched game assembly and serialized Main scene established `Canvas/ContentArea/RightSidebar/AttributeBar/AutoBuyToggle` as the native anchor. The action queue is a separate sibling that expands toward the toggle; `StatusContainer` owns passive abilities and status effects. The native toggle carries a `ManagedView` reference to `AutoBuyerView`, so clones must remove that binding before activation. The suite strip ends 12 pixels before the native toggle's left edge and extends outward in Automata Auto Buy, Auto Cast, Auto Concept, Mentor order. The `0.7.0` beta still requires post-release Proton confirmation.

For the current beta, verify on both a new game and NG+ that all enabled suite controls appear even when the native Auto Buy feature is locked, remain outside the action queue with uniform gaps, and keep Auto Buy → Auto Cast → Auto Concept → Mentor order. Click `CN` through OFF/ON and emergency-blocked states, confirm the Auto Concept configuration changes with it, and confirm no cloned control changes the native Auto Buy state or queue contents.

For `TimedCycle`, use a 10-second period and confirm that native setup time is excluded, mastery catch-up does not rotate early, the planned inactive concept occupies the released compatible slot, and the next assignment receives a new full period. With at least two acquired compatible slots, drain one candidate's required resource to zero and confirm Auto Concept neither re-adds it nor churns remove/add mutations, while another compatible resource-safe candidate still occupies the second slot. For spell leveling, validate the Auto Buy tooltip and logs in all three native capability states: no mutation while Locked, exactly one ready affordable spell level per Single mutation, and the native level-all result only after `UnlockLevelAllSpells` is completed. Confirm insufficient resources spend nothing, a queued level-all Upgrade remains Single, disabling Auto Buy stops spell leveling without stopping Auto Concept, and Mentor still observes native mastery changes without duplicate XP work.

For the unified configuration pass, open each Automata and Mentor section with its owning mode disabled. Confirm tuning rows show a dependency message while mode, shortcut, status-button visibility, emergency disable, and diagnostics remain editable. Change a mode without applying and confirm rows unlock immediately from the staged value. Validate compound cases: Fixed Auto Buy batch size, Fixed Structure levels with action multiplier off, and Mentor artifact/alchemy share percentages. Disable a parent switch again and confirm saved or staged child values are retained rather than reset.

For unified feature health, confirm each gameplay control and tooltip distinguishes saved `OFF`, progression `Locked`, lifecycle `Not ready`, normal `Operational`, recoverable queue/safety blocking, unavailable contract, degradation, and fault evidence without changing the underlying queue or XP work. Enter and leave Main, load another save, and trigger the available reset/NG+ boundary; status generation must recover without retaining the previous save's reason. In Mentor, deliberately disable or make one optional domain unavailable while another is operational and confirm only the affected domain fails and the root reports degradation. In Orb Mod Config, verify the selected plugin's runtime band is visually separate from Apply/Revert feedback at each supported resolution, changes after the same runtime transition, and shows third-party plugins without publishers as `Not reported` rather than inventing health. Saving a setting must say only that configuration was saved.

For bounded failure circuits, inject or reproduce one transient resource-read failure and confirm Auto Buy stops probing it on every evaluation, then confirm the exact resource/registry change wakes it early. Reproduce an attempted no-op or unverifiable mutation only with the disposable validation fixture: the same candidate/domain must not attempt another mutation until a save/load, scene, reset, or NG+ transition advances lifecycle generation. A missing exact contract must remain blocked across that transition. Throughout the fault, a healthy later Auto Buy candidate and healthy Mentor sibling domain must continue receiving scheduler turns, repeated events must not amplify warnings, and the 30-minute combined soak must show no unbounded pending work or starvation.

For the enforced suite performance profile, capture one complete uncontaminated window for the same combined workload on Windows desktop and Steam Deck/Proton. Confirm all twelve exact identities use the checked 10/12/30-frame thresholds, cooperative and combined active-frame timing remain within target, and starvation, abandonment, and measurement-failure deltas stay zero. The checker must exit 0; exit 3 means the target or required sample window failed, while exit 1 means the evidence is invalid or incompatible. Native timing values remain nonblocking only when the result is literally `observe-only`; an insufficient native sample/window is not acceptable evidence. During the workload, consume most of one frame's 0.75 ms soft budget before Mentor planning and confirm Mentor uses only the remainder, performs no work at zero remainder, and later resumes the exact pending XP without a duplicate or drop.

For action-family ownership, first validate the suite alone: enable and disable each Automata feature and each Mentor domain, cross title/save/reset/NG+ boundaries, and confirm claims release and recover without replaying prepared work. Then use a disposable installation with the exact `IngoH.OrbOfCreation.AutoBuyOrb` plugin. Confirm Automata submits no Structure or Upgrade mutation and reports the conflict, while Auto Cast, Auto Concept, Spell Leveling, Mentor, manual actions, and already queued native actions remain unchanged. Remove AutobuyOrb, restart or advance the supported lifecycle, and confirm only fresh Automata work resumes. A similarly named plugin must not be blocked; logs must state that unknown unregistered automation cannot be proven absent.

Historical `0.3.1` desktop/handheld control evidence from 2026-07-14:

- Automata resolved the native `UIToggleButton` bound to `AutoBuyManager.autoBuyEnabled` and cloned it under `Canvas/ContentArea/RightSidebar/AttributeBar`.
- The first clone inherited the native absolute position and was hidden underneath the original. The corrected build offsets it by the rendered native width plus four pixels; the runtime placement was `(-54,-35)` at `50x50`, visibly left of Auto Buy.
- Both the button and `Left Alt + X` used the same state control. The player observed `TEST` and `OFF` transitions while `AutoCast.Mode=DryRun`.
- Native status-area popups displayed the matching activation text. The earlier Active attempt displayed `BLOCKED`, and the engine emitted one expected probe-guard warning because `RuntimeProbeConfirmed=false`.
- No UI attachment, popup reflection, per-frame exception, or fallback warning appeared in the complete BepInEx log.

## Validation order

Run supported mods in this order:

1. **Automata** — begin Disabled; Active is tested only on a disposable backed-up save.
2. **Mentor** — begin Disabled, then validate XP sharing on a disposable backed-up save.
3. **Mod Config** — validate navigation, editors, and status-control synchronization.
4. **Combined supported suite** using only allowlisted plugins.

## Gate V0 — repository and real-reference build

Run from the repository root:

```powershell
dotnet test tests/OrbModding.Tests/OrbModding.Tests.csproj -p:UseGameStubs=true
$env:OOC_GAME_DIR = 'C:\Program Files (x86)\Steam\steamapps\common\Orb of Creation'
dotnet test tests/OrbModding.GameContractTests/OrbModding.GameContractTests.csproj
dotnet build src/OrbAutomata/OrbAutomata.csproj -c Release
dotnet build src/OrbMentor/OrbMentor.csproj -c Release
dotnet build src/OrbModConfig/OrbModConfig.csproj -c Release
```

Acceptance criteria:

- Unit tests pass from a clean checkout.
- Each plugin builds against the real installed DLLs with zero errors.
- The output contains the plugin DLL and `OrbModding.Common.dll`, but no copied game, Unity, BepInEx, or Harmony assemblies.
- The installed assembly hashes equal the baseline recorded above.
- No plugin is installed while this gate is failing.

## Gate V1 — static contract audit

Before launching the game, compare every reflected or Harmony target with the installed assemblies. Record exact signatures, visibility, return type, and overload count.

Required checks:

| Mod | Contracts that must be proven |
|---|---|
| Automata | `StructureSO`/`UpgradeSO` registries, state, cost, queue, and one-level purchase paths; `ActionManager`; native multi-buy; `ResourceTuple`; `BigDouble` |
| Mentor | Spell, artifact, and alchemy catalogs; availability and persistence; XP hooks; recipient identity; native grant or audited native-sequence path; recursion suppression; and per-domain failure isolation |
| Mod Config | Native navigation host, Mods view lifecycle, editor contracts, and queue-adjacent status-control anchor |

Acceptance criteria: every contract used by active code is either verified or guarded by a safe no-op. A warning-only fallback is not enough for a feature advertised as active.

## Gate V2 — one-plugin load smoke test

Prepare once:

1. Close the game and Steam cloud synchronization for the test window if it could overwrite local rollback files.
2. Copy the entire save directory to a timestamped backup outside the live directory.
3. Preserve the existing `BepInEx/config` files and `BepInEx/LogOutput.log`.
4. Install only one mod and its matching `OrbModding.Common.dll`.
5. Keep all automation and sharing modules Disabled during the initial smoke test.

For each mod, launch to title, load the backed-up test slot, wait two minutes, return to title, and quit normally.

Acceptance criteria:

- BepInEx lists the expected plugin GUID and version once.
- No `TypeLoadException`, `MissingMethodException`, Harmony patch error, or repeating exception appears.
- The runtime assembly audit reports the expected baseline.
- Title → game → title → quit completes normally.
- The original save remains loadable after removing the plugin.

## Gate V3 — read-only behavior probes

### Automata

- Keep `AutoBuy.Mode=Disabled`. Confirm the public configuration exposes only Disabled/Active modes and contains no runtime-probe, per-session-limit, DryRun, or Research settings.
- Confirm the installed-assembly contract tests still match the native Structure, Upgrade, queue-room, resource-cost, Bulk Development, and action-multiplier members.
- Confirm a migrated older config removes the deprecated keys and saves valid release defaults.
- Confirm no other auto-buy plugin is installed during Automata validation.
- Candidate/cost behavior moves to the disposable active gate because the release build intentionally has no mutating-looking DryRun mode.

Acceptance criteria: logged observations match the UI and ILSpy contracts, no save data changes are attributable to the probe, and no unresolved active-path member remains.

## Gate V4 — isolated active gameplay tests

Use a disposable copy of the backed-up save and enable only one mod.

### Automata

After V3 evidence is approved, use a disposable save and ensure no other auto-buy plugin is installed. Start Disabled, set one cheap observed UUID in `AutoBuy.AllowedUuids`, choose `BatchSizingMode=Fixed` with `MaxPurchasesPerBatch=1`, then activate and immediately return to Disabled after the first visible queued level. Verify its cost, resource deduction, queue entry, and completion. Test an Upgrade and Structure separately. Repeat with independent affordability thresholds and with both zero and non-zero reserves. For a reserve- or affordability-blocked Structure, capture evaluation counts while its relevant resource rises through several sub-threshold ticks, then crosses the logged required quantity: sub-threshold ticks must not repeatedly evaluate it, and the crossing must wake it without waiting for a broad catalog refresh. Change resource quality or effective attribute cost and confirm that it wakes conservatively. Finally test `RespectActionMultiplier=true` at 5×: it must submit at most five individually revalidated levels, never exceed free queue room, and stop early at a reserve boundary. Emergency disable must stop new purchases immediately. A sustained affordable-repeat test with multiple eligible candidates must add different ranked candidates on consecutive admitted frames, retain the next one during 10 Hz full-queue polling, and begin the next pass without waiting for the idle evaluation interval. Repeat with only one allowlisted candidate and confirm it can fill all usable room.

For the shared queue-capacity snapshot, repeat with a one-slot queue at `LeaveQueueSlots=1` and `LeaveQueueSlots=0`, then change native queue capacity while a candidate remains prepared. Confirm the immediately following mutation uses refreshed total capacity, occupancy, and remaining room; the manual reservation is applied once; and no purchase occurs for missing or contradictory native values.

## Gate V5 — persistence and rollback

For every active test:

- Save at the modified state, quit, reload, and compare the expected queues, levels, resources, modifiers, timing values, and tooltips.
- Remove the plugin and confirm the unmodded game loads.
- Restore the backup while the game is closed and confirm its checksum and visible state match the pre-test record.
- Treat any unexplained save-size jump, load warning, duplicate modifier, NaN/infinity, lost queue, or timing drift as a release blocker.

## Gate V6 — combined compatibility

Test Automata, Mentor, and Mod Config together at normal and accelerated game speeds supported by the environment.

Verify independent configs and keybinds, unscaled scheduler cadence, unchanged global multi-buy, acceptable frame time, save/reload, title return, queue-adjacent control ordering, and the ability to disable one plugin without breaking the others.

## Gate V7 — release candidate

- Repeat V0 from a clean checkout.
- Install from the exact release archive, not a developer output directory.
- Repeat the load smoke test on a clean BepInEx profile.
- Inspect the archive: only intended plugin DLLs and release documentation are allowed.
- Attach a completed report based on [`tests/runtime/report-template.md`](../../tests/runtime/report-template.md).

## Evidence and stop rules

Store sanitized reports under `tests/runtime/YYYY-MM-DD-<mod>-<commit>/`. Record the commit, plugin version, game/Unity/BepInEx versions, assembly hashes, installed mod set, configuration differences, save-backup checksum, expected and actual result, timing samples, and relevant log excerpts. Do not commit saves, personal paths, or unsanitized full logs.

Stop immediately and restore the backup after a crash loop, failed load, save warning, NaN/infinity, duplicate modifier, unintended purchase, queue loss, timing that does not return to baseline, or an exception repeated every frame. Do not continue to a higher speed or a combined-mod test after a lower gate fails.
