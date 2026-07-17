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
- On 2026-07-17, all 271 supported game-independent behavior and knowledge-map tests passed with `UseGameStubs=true`.
- On 2026-07-17, all 12 supported installed-game metadata contract tests passed against the audited assemblies.
- Automata, Mentor, and Mod Config built in Release against the real installed game references with zero warnings. The required Unity facade, UI, and TextMeshPro references are part of the build contract.
- The supported-suite package rehearsal contained only Automata, Mentor, Mod Config, and Orb Modding Common DLLs; experimental DLL guards passed.

The static ILSpy contract check confirmed these installed methods:

- `ResearchSO.CanDevelop()`, `GetDevelopError()`, `GetDevelopmentCost()`, and `Develop()`.
- `StructureSO` and `UpgradeSO` registries, availability, costs, queue state, and purchase methods; `ActionManager.GetRemainingRoom()`; and native multi-buy access/restoration.

### Automata Auto Buy runtime evidence

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

Before continuous mode is enabled, visually confirm the exact one-level queue/completion, the expected resource deduction, and restoration of the native multi-buy value. Keep `ActivePurchaseLimitPerSession=1` until those observations are recorded.

Structure-only DryRun evidence on 2026-07-14 with `0.1.3`:

- With Upgrades excluded and no allowlist, Automata discovered 180 Structure candidates.
- `Concentration` (`bf4e596c-3ee0-4194-b0c2-d4a7af1a85f6`) was selected consistently.
- Its logged next-level cost was `9.679e44` Sigil against `6.398e60` available, for a maximum cost ratio of `1.513e-16` under `Excess100`.
- No purchase occurred in DryRun.

The subsequent Structure endurance probe passed with ten sequential native purchases, one terminal session-limit record, no Automata warnings/errors, and no post-limit purchase or candidate-scan records. Sanitized Upgrade and Structure transcripts are committed under `tests/fixtures/automata/` and asserted by the portable test suite.

The unrestricted personal-beta log then exposed queue downtime: version `0.1.3` bought only one ranked candidate per complete 409-candidate scan, and budgeted scans could span several 0.5-second intervals. Version `0.1.4` introduced a configurable ranked batch (`MaxPurchasesPerBatch`, default 8), but runtime evidence showed every batch ending at one purchase with `CpuLimited=True`: a native purchase took roughly 8–11 ms while the shared budget was 4 ms.

Version `0.1.5` retains the ranked candidates after the scan and continues the same batch on consecutive frames. Each frame remains CPU-bounded, but continuation bypasses the evaluation interval and does not rescan. Every batch member remains distinct and is revalidated against live resources, reserves, queue room, and the session limit immediately before purchase. Portable tests force a purchase to exceed the per-frame budget and prove that the remaining ranked candidates complete on following frames from one catalog scan. Runtime queue-utilization evidence for `0.1.5` is still required.

Version `0.1.6` added two opt-in policies. `BatchSizingMode=FillAvailableQueue` derives batch completion from live queue room instead of a fixed count. `StructureRepeatMode=BulkDevelopment` reads the current `Player.GetBulkDevelopment()` value for each structure group and queues that many consecutive levels through independently revalidated one-level purchases. This deliberately avoids native upgrade multi-buy, whose `Purchase()` loop does not clamp its queued level count to `ActionManager.GetRemainingRoom()`. Runtime validation on 2026-07-14 confirmed live Bulk Development grouping and queue-aware filling; version `0.1.8` therefore makes `BulkDevelopment` the default repeat policy while retaining `Fixed` and `Single` overrides.

### Automata Auto Cast control runtime evidence

A 2026-07-17 static audit of the hash-matched game assembly and serialized Main scene established `Canvas/ContentArea/RightSidebar/AttributeBar/AutoBuyToggle` as the native anchor. The action queue is a separate sibling that expands toward the toggle; `StatusContainer` owns passive abilities and status effects. The native toggle carries a `ManagedView` reference to `AutoBuyerView`, so clones must remove that binding before activation. The suite strip ends 12 pixels before the native toggle's left edge and extends outward in Automata Auto Buy, Auto Cast, Auto Concept, Mentor order. The `0.6.0` release-candidate build still requires interactive confirmation after installation.

For the current candidate, verify on both a new game and NG+ that all enabled suite controls appear even when the native Auto Buy feature is locked, remain outside the action queue with uniform gaps, and keep Auto Buy → Auto Cast → Auto Concept → Mentor order. Click `CN` through OFF/ON and emergency-blocked states, confirm the Auto Concept configuration changes with it, and confirm no cloned control changes the native Auto Buy state or queue contents.

The `0.3.1` desktop/handheld control probe passed on 2026-07-14:

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
| Mentor | Spell mastery catalogs, XP hook, recipient identity, native grant path, and recursion suppression |
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

After V3 evidence is approved, use a disposable save and ensure no other auto-buy plugin is installed. Start Disabled, set one cheap observed UUID in `AutoBuy.AllowedUuids`, choose `BatchSizingMode=Fixed` with `MaxPurchasesPerBatch=1`, then activate and immediately return to Disabled after the first visible queued level. Verify its cost, resource deduction, queue entry, and completion. Test an Upgrade and Structure separately. Repeat with independent affordability thresholds and with both zero and non-zero reserves. Finally test `RespectActionMultiplier=true` at 5×: it must submit at most five individually revalidated levels, never exceed free queue room, and stop early at a reserve boundary. Emergency disable must stop new purchases immediately. A sustained queue test must show scan continuations and prepared batches checking room every frame rather than waiting for the idle evaluation interval.

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
