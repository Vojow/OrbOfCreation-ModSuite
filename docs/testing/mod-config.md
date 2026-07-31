# Orb Mod Config testing

[Testing hub](README.md) · [Mod Config behavior reference](../../src/ModConfig/README.md) · [UI overhaul validation](ui-overhaul-validation.md) · [Runtime protocol](runtime-validation.md)

## Risk contract

Mod Config must project supported configuration without changing serialized
meaning, preserve staged edits and scroll/navigation state, isolate plugin and
subscriber failures, display saved configuration separately from runtime
health, and select like a native tab. Its first install is admitted by one shared readiness gate
after the native delayed-list renderer has populated every required top-bar icon. A short bounded
startup lane distinguishes zero-count loading from genuine failure. Once the supported game
baseline has created its UI objects, a missing audited shape is a surfaced suite failure rather
than a request for alternate chrome.

## Test ownership

| Concern | Primary tests |
|---|---|
| Catalog, typed values, apply/revert/default | [ModConfigTests.cs](../../tests/OrbModding.Tests/ModConfigTests.cs) |
| Responsive row/layout behavior | [ModConfigPanelLayoutTests.cs](../../tests/OrbModding.Tests/ModConfigPanelLayoutTests.cs) |
| Runtime health/status projection | [ModRuntimeStatusProjectionTests.cs](../../tests/OrbModding.Tests/ModRuntimeStatusProjectionTests.cs), [ConfigurationSchemaStatusProjectionTests.cs](../../tests/OrbModding.Tests/ConfigurationSchemaStatusProjectionTests.cs) |
| Shared invalidation handoff | [ModConfigGameplayInvalidationTests.cs](../../tests/OrbModding.Tests/ModConfigGameplayInvalidationTests.cs) |
| Native tab selection, first-install trigger, navigation cadence, and work budgets | [ModConfigPerformanceTests.cs](../../tests/OrbModding.Tests/ModConfigPerformanceTests.cs) |
| Cross-plugin schema transactions | [ConfigurationSchemaTests.cs](../../tests/OrbModding.Tests/ConfigurationSchemaTests.cs), [AutomataConfigurationTests.cs](../../tests/OrbModding.Tests/AutomataConfigurationTests.cs) |
| Schema 4 to 5 retirement transaction | [SuiteConfigurationSchemaFiveTests.cs](../../tests/OrbModding.Tests/SuiteConfigurationSchemaFiveTests.cs) |
| Schema 5 to 6 Auto Concept fallback migration | [SuiteConfigurationSchemaSixTests.cs](../../tests/OrbModding.Tests/SuiteConfigurationSchemaSixTests.cs) |
| Schema 6 to 7 Auto Concept training-period migration | [SuiteConfigurationSchemaSevenTests.cs](../../tests/OrbModding.Tests/SuiteConfigurationSchemaSevenTests.cs) |
| One-path quick-control publication | [AutomataConfigurationTests.cs](../../tests/OrbModding.Tests/AutomataConfigurationTests.cs), [AutomataFeatureStatusTests.cs](../../tests/OrbModding.Tests/AutomataFeatureStatusTests.cs) |
| Native frame and single-pixel-writer ownership | [ConfiguredIntentIconButtonVisualTests.cs](../../tests/OrbModding.Tests/ConfiguredIntentIconButtonVisualTests.cs), [ModConfigPanelLayoutTests.cs](../../tests/OrbModding.Tests/ModConfigPanelLayoutTests.cs) |
| Auto Items staged picker and raw-editor exclusion | [AutoItemsTemporaryItemPickerViewTests.cs](../../tests/OrbModding.Tests/Services/AutoItems/AutoItemsTemporaryItemPickerViewTests.cs) |
| Native-surface install reporting | [SuiteUiSurfaceDiagnosticsTests.cs](../../tests/OrbModding.Tests/OrbModConfig/SuiteUiSurfaceDiagnosticsTests.cs) |
| Installed native navigation shape | `OrbModding.GameContractTests` Mod Config contract |

## Commands

```powershell
dotnet test tests/OrbModding.Tests/OrbModding.Tests.csproj -p:UseGameStubs=true --filter "FullyQualifiedName~ModConfig|FullyQualifiedName~ConfigurationSchema"
powershell -NoProfile -ExecutionPolicy Bypass -File tools/test-portable.ps1 -Lane Fast
```

Run `PerformanceAll` when attachment cadence, navigation integrity, listener
installation, recovery, or layout work changes.

## Required cases for behavior changes

- Serialized values and enum meanings survive catalog projection.
- Apply, Revert, and Default affect only the intended staged values.
- Configuration transactions are backup-first, all-or-nothing, and rollback
  exact bytes on failures.
- Subscriber exceptions do not stop other settings or plugins.
- Runtime status never claims that a saved value is already active.
- A quick control consumes exactly one pending saved snapshot synchronously; the next frame must
  not publish an echo of the same change.
- A quick control resolves its next value from committed state even if a raw external edit is pending.
- Initial binding state becomes generation 1 without an identical startup publication.
- Feature health cannot change configured intent; the central join is the only status projection that
  combines those axes.
- Same-page rebuilds preserve scroll position; page changes reset it.
- Selecting Mods requests open state whether it was inactive or already active. Only selecting a
  native sibling closes it, and MCP tab selection invokes that same button path.
- After the first `Main` end-of-frame boundary, one shared gate inspects all six required direct
  top-bar icon candidates at most every 100 ms for at most two seconds. Zero or partial-zero counts
  stay on that bounded startup lane without incrementing either surface's failure state. Once all
  six populated icons exist, quick controls and Mods are admitted in the same Update. Duplicates,
  null icons, type/field mismatches, and absence after the window enter the existing five-second,
  terminal-after-three failure path immediately.
- Native-skinned panel frames remain raycast targets. Wheel delivery is continuous across the
  settings viewport, including row gutters, runtime-card text, and blank padding; the open feature
  drawer also blocks wheel input across its padding and grid gaps rather than passing it to native UI.
- Disabled/absent plugins remain honest status-only or absent entries.
- Feature modes appear only in the feature header and gameplay feature drawer; both publish through
  the committed store, while policy fields remain staged. General's emergency command uses the same
  immediate committed-state toggle as STOP and is not staged behind Apply.
- The Auto Items temporary allowlist renders only its specialized discovered-item picker. Its rows
  stage exact UUIDs and flow through the same Apply/Revert transaction as generic settings; no
  `TMP_InputField`, raw editor, family/bulk switch, or immediate persistence path may exist.
- Every suite-owned `Button` has `targetGraphic == null`, so hover, press, release, selection, and
  interactable changes cannot repaint a suite-rendered state.
- Inactive audited rail candidates remain capturable while Mods is open. Quick controls reuse that
  family’s inactive/active frame pair rather than sampling spell buttons.
- The closed gameplay hierarchy has exactly two live suite buttons; the seven feature controls are
  live only under the open panel. The emergency square remains the exact captured native button
  size, uses the audited `power-lightning` Sprite, and pairs with a 32-pixel disclosure footer.
  Clear/stopped uses full deep-green/deep-red frame treatment on both regions. A closed
  contained-feature fault or block changes the disclosure's color and activates a separate marker,
  while open/closed uses different frames and glyphs.
- Feature quick controls cannot retain or construct a text-rendering path or clone a native toggle.
  Suite-created UI nodes explicitly request `RectTransform`; a plain `GameObject` remains a
  `Transform` in the portable stub just as its installed Unity contract declares. Startup
  zero-count observations are not failures and produce no per-surface retry tick. Both UI surfaces
  log their first genuine failed attempt as retrying on the five-second cadence. The third
  consecutive genuine failure publishes a Runtime failure and an error log naming the exact
  member/check and expected-versus-actual types where applicable.

## Runtime handoff

Portable layout tests cannot prove Unity text measurement, canvas clipping,
  navigation ordering, input focus, or actual config-file persistence. V5 UAT
must cover early-progression UI, long descriptions, responsive resizing,
staged edits, Apply/Revert/Default, external changes, save/reload, and removal.
Use the [UI overhaul checklist](ui-overhaul-validation.md) for the post-install pass.

## Known next work

- Add stable `ModConfigDecision` and `ModConfigReliability` lanes.
- Add screenshot-assisted layout evidence for the supported resolution/UI-scale
  matrix without treating screenshots as semantic assertions.
