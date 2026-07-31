# Orb Mod Config testing

[Testing hub](README.md) · [Mod Config behavior reference](../../src/ModConfig/README.md) · [UI overhaul validation](ui-overhaul-validation.md) · [Runtime protocol](runtime-validation.md)

## Risk contract

Mod Config must project supported configuration without changing serialized
meaning, preserve staged edits and scroll/navigation state, isolate plugin and
subscriber failures, display saved configuration separately from runtime
health, and retry startup until the audited native UI exists. Once the supported game baseline has
created its UI objects, a missing audited shape is a surfaced suite failure rather than a request
for alternate chrome.

## Test ownership

| Concern | Primary tests |
|---|---|
| Catalog, typed values, apply/revert/default | [ModConfigTests.cs](../../tests/OrbModding.Tests/ModConfigTests.cs) |
| Responsive row/layout behavior | [ModConfigPanelLayoutTests.cs](../../tests/OrbModding.Tests/ModConfigPanelLayoutTests.cs) |
| Runtime health/status projection | [ModRuntimeStatusProjectionTests.cs](../../tests/OrbModding.Tests/ModRuntimeStatusProjectionTests.cs), [ConfigurationSchemaStatusProjectionTests.cs](../../tests/OrbModding.Tests/ConfigurationSchemaStatusProjectionTests.cs) |
| Shared invalidation handoff | [ModConfigGameplayInvalidationTests.cs](../../tests/OrbModding.Tests/ModConfigGameplayInvalidationTests.cs) |
| Navigation cadence and work budgets | [ModConfigPerformanceTests.cs](../../tests/OrbModding.Tests/ModConfigPerformanceTests.cs) |
| Cross-plugin schema transactions | [ConfigurationSchemaTests.cs](../../tests/OrbModding.Tests/ConfigurationSchemaTests.cs), [AutomataConfigurationTests.cs](../../tests/OrbModding.Tests/AutomataConfigurationTests.cs) |
| Schema 4 to 5 retirement transaction | [SuiteConfigurationSchemaFiveTests.cs](../../tests/OrbModding.Tests/SuiteConfigurationSchemaFiveTests.cs) |
| Schema 5 to 6 Auto Concept fallback migration | [SuiteConfigurationSchemaSixTests.cs](../../tests/OrbModding.Tests/SuiteConfigurationSchemaSixTests.cs) |
| Schema 6 to 7 Auto Concept training-period migration | [SuiteConfigurationSchemaSevenTests.cs](../../tests/OrbModding.Tests/SuiteConfigurationSchemaSevenTests.cs) |
| One-path quick-control publication | [AutomataConfigurationTests.cs](../../tests/OrbModding.Tests/AutomataConfigurationTests.cs), [AutomataFeatureStatusTests.cs](../../tests/OrbModding.Tests/AutomataFeatureStatusTests.cs) |
| Native frame and single-pixel-writer ownership | [ConfiguredIntentIconButtonVisualTests.cs](../../tests/OrbModding.Tests/ConfiguredIntentIconButtonVisualTests.cs), [ModConfigPanelLayoutTests.cs](../../tests/OrbModding.Tests/ModConfigPanelLayoutTests.cs) |
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
- Disabled/absent plugins remain honest status-only or absent entries.
- Feature modes appear only in the feature header and gameplay quick-controls column; both publish through
  the committed store, while policy fields remain staged.
- Every suite-owned `Button` has `targetGraphic == null`, so hover, press, release, selection, and
  interactable changes cannot repaint a suite-rendered state.
- Inactive audited rail candidates remain capturable while Mods is open. Quick controls reuse that
  family’s inactive/active frame pair rather than sampling spell buttons.
- Feature quick controls cannot retain or construct a text-rendering path or clone a native toggle.
  Suite-created UI nodes explicitly request `RectTransform`; a plain `GameObject` remains a
  `Transform` in the portable stub just as its installed Unity contract declares. Both UI surfaces
  log the first failed attempt as retrying on their shared cadence. The third consecutive failure
  publishes a Runtime failure and an error log naming the exact member/check and
  expected-versus-actual types where applicable.

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
