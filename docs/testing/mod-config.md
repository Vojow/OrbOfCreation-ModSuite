# Mod Config and suite UI testing

[Testing hub](README.md) · [Behavior reference](../../src/ModConfig/README.md) ·
[Runtime protocol](runtime-validation.md)

## Risk contract

Mod Config projects the one committed configuration without changing serialized
meaning. It preserves staged edits and navigation, separates saved intent from
runtime health, owns its pixels and listeners, and fails closed when audited
native UI primitives are unavailable.

## Primary ownership

| Concern | Tests |
|---|---|
| Catalog, typed editors, Apply/Revert/Default | [ModConfigTests.cs](../../tests/OrbModding.Tests/ModConfigTests.cs) |
| Responsive layout and pointer ownership | [ModConfigPanelLayoutTests.cs](../../tests/OrbModding.Tests/ModConfigPanelLayoutTests.cs) |
| Navigation, maintenance bounds, and startup | [ModConfigPerformanceTests.cs](../../tests/OrbModding.Tests/ModConfigPerformanceTests.cs) |
| Status and action-outcome presentation | [ActionOutcomeSurfacePresentationTests.cs](../../tests/OrbModding.Tests/OrbModConfig/ActionOutcomeSurfacePresentationTests.cs), [RuntimeDiagnosticsProjectionTests.cs](../../tests/OrbModding.Tests/OrbModConfig/RuntimeDiagnosticsProjectionTests.cs) |
| Backup admission and health | [AutomaticSaveBackupTests.cs](../../tests/OrbModding.Tests/AutomaticSaveBackupTests.cs), [AutomaticSaveBackupHealthTests.cs](../../tests/OrbModding.Tests/OrbModConfig/AutomaticSaveBackupHealthTests.cs) |
| Configuration schema transactions | [ConfigurationSchemaTests.cs](../../tests/OrbModding.Tests/ConfigurationSchemaTests.cs), [SuiteConfigurationSchemaSixTests.cs](../../tests/OrbModding.Tests/SuiteConfigurationSchemaSixTests.cs) |
| Quick controls and native UI construction | [QuickControlColumnTests.cs](../../tests/OrbModding.Tests/QuickControlColumnTests.cs) |
| Temporary-item picker | [AutoItemsTemporaryItemPickerViewTests.cs](../../tests/OrbModding.Tests/Services/AutoItems/AutoItemsTemporaryItemPickerViewTests.cs) |
| Surface failure reporting | [SuiteUiSurfaceDiagnosticsTests.cs](../../tests/OrbModding.Tests/OrbModConfig/SuiteUiSurfaceDiagnosticsTests.cs) |

## Commands

```powershell
dotnet test tests/OrbModding.Tests/OrbModding.Tests.csproj -p:UseGameStubs=true --filter "FullyQualifiedName~ModConfig|FullyQualifiedName~QuickControl|FullyQualifiedName~ConfigurationSchema"
powershell -NoProfile -ExecutionPolicy Bypass -File tools/test-portable.ps1 -Lane Fast
```

Then run `./script/test`; native/reflection changes also require installed-game
contracts against the audited copy.

## Required cases

- Apply, Revert, and Default affect only intended staged values; external
  conflicts remain explicit and atomic.
- The committed store is the sole intent source. A direct control publishes one
  saved transition, and no later frame echoes it.
- Runtime health cannot mutate configuration; status joins intent and health in
  one central projection.
- After the first playable Main boundary, one shared readiness gate observes all
  six native top-bar icons at 100 ms intervals for at most 30 seconds. Loading is
  not a failure; duplicates, wrong types, missing sprites, or expiry enter the
  named three-attempt failure path.
- The closed gameplay footprint has exactly two suite buttons. The seven feature
  controls exist only while disclosure is open, and fault attention is not
  color-only.
- Every suite-created UI node that needs layout requests `RectTransform`
  explicitly. A plain stub `GameObject` remains a `Transform`.
- Native-skinned panel frames retain raycast ownership so wheel input works over
  content, gaps, text, and padding; decoration does not steal input.
- Suite-owned buttons have no native transition pixel writer, while each action
  still fires exactly once.
- The temporary-item editor is a discovered-item exact-UUID whitelist with an
  approval count and removable unresolved entries; no raw editor, family switch,
  bulk approval, or immediate persistence path exists.
- Release Runtime copy is player-facing and structurally excludes profiling-only
  actions. Waiting, completed, quiet non-completion, and faults stay distinct
  without relying on color.

## Runtime handoff

Follow V3 in the [runtime protocol](runtime-validation.md) at minimum and maximum
supported resolution/UI scale, then V5 for persistence. Record screenshots only
as visual evidence; semantic claims still require state, logs, and exact action
outcomes.
