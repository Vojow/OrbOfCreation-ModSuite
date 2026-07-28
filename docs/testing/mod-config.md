# Orb Mod Config testing

[Testing hub](README.md) · [Mod Config behavior reference](../../src/ModConfig/README.md) · [Runtime protocol](runtime-validation.md)

## Risk contract

Mod Config must project supported configuration without changing serialized
meaning, preserve staged edits and scroll/navigation state, isolate plugin and
subscriber failures, display saved configuration separately from runtime
health, and remain usable before optional native progression UI exists.

## Test ownership

| Concern | Primary tests |
|---|---|
| Catalog, typed values, apply/revert/default | [ModConfigTests.cs](../../tests/OrbModding.Tests/ModConfigTests.cs) |
| Responsive row/layout behavior | [ModConfigPanelLayoutTests.cs](../../tests/OrbModding.Tests/ModConfigPanelLayoutTests.cs) |
| Runtime health/status projection | [ModRuntimeStatusProjectionTests.cs](../../tests/OrbModding.Tests/ModRuntimeStatusProjectionTests.cs), [ConfigurationSchemaStatusProjectionTests.cs](../../tests/OrbModding.Tests/ConfigurationSchemaStatusProjectionTests.cs) |
| Shared invalidation handoff | [ModConfigGameplayInvalidationTests.cs](../../tests/OrbModding.Tests/ModConfigGameplayInvalidationTests.cs) |
| Navigation cadence and work budgets | [ModConfigPerformanceTests.cs](../../tests/OrbModding.Tests/ModConfigPerformanceTests.cs) |
| Cross-plugin schema transactions | [ConfigurationSchemaTests.cs](../../tests/OrbModding.Tests/ConfigurationSchemaTests.cs), [AutomataConfigurationTests.cs](../../tests/OrbModding.Tests/AutomataConfigurationTests.cs) |
| One-path quick-control publication | [AutomataConfigurationTests.cs](../../tests/OrbModding.Tests/AutomataConfigurationTests.cs), [AutomataFeatureStatusTests.cs](../../tests/OrbModding.Tests/AutomataFeatureStatusTests.cs) |
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

## Runtime handoff

Portable layout tests cannot prove Unity text measurement, canvas clipping,
navigation ordering, input focus, or actual config-file persistence. V4 UAT
must cover early-progression UI, long descriptions, responsive resizing,
staged edits, Apply/Revert/Default, external changes, save/reload, and removal.

## Known next work

- Add stable `ModConfigDecision` and `ModConfigReliability` lanes.
- Add screenshot-assisted layout evidence for the supported resolution/UI-scale
  matrix without treating screenshots as semantic assertions.
