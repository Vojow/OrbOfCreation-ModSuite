# Automata integration testing

[Automata test map](README.md) · [Common testing](../common.md) · [Suite integration](../suite-integration.md)

This page owns Automata behavior that is broader than one feature.

## Primary ownership

- [AutomataTests.cs](../../../tests/OrbModding.Tests/AutomataTests.cs) — configuration defaults, migration-facing behavior, and
  feature composition.
- [ConfigurationSchemaTests.cs](../../../tests/OrbModding.Tests/ConfigurationSchemaTests.cs) — schema-zero migration, rollback, and
  safe typed binding.
- [AutomataFeatureStatusTests.cs](../../../tests/OrbModding.Tests/AutomataFeatureStatusTests.cs) and [AutomataRuntimeEvidenceTests.cs](../../../tests/OrbModding.Tests/AutomataRuntimeEvidenceTests.cs) — health
  projection and runtime evidence.
- [HarmonyBindingHeadlessTests.cs](../../../tests/OrbModding.Tests/HarmonyBindingHeadlessTests.cs) — patched target binding against game-shaped
  fixtures.
- [ActionFamilyOwnershipTests.cs](../../../tests/OrbModding.Tests/ActionFamilyOwnershipTests.cs) and [ActionFamilyIntegrationTests.cs](../../../tests/OrbModding.Tests/ActionFamilyIntegrationTests.cs) — exact
  family claims, conflicts, release, and recovery.

## Focused command

```powershell
dotnet test tests/OrbModding.Tests/OrbModding.Tests.csproj -p:UseGameStubs=true --filter "FullyQualifiedName~Automata|FullyQualifiedName~ActionFamily|FullyQualifiedName~HarmonyBinding"
```

Configuration work must additionally run the repository configuration-schema
scope documented in [strategy](../strategy.md). Coordinator or scheduling work
must run `PerformanceAll` and the checked suite performance evaluator.

## Integration invariants

- Configuration failure prevents startup without partial binding or mutation.
- Independent action families can stop, recover, and report health separately.
- Known Auto Buy conflicts block only Structure/Upgrade ownership.
- Lifecycle transitions cancel all prepared work before any new native mutation.
- The one ServiceCycle pump rotates action turns without dropping work or starving another registered
  feature.
- Runtime status reports saved configuration separately from actual readiness.
- Every control, ownership decision, host activation, and status join reads the configuration store's
  committed snapshot; raw BepInEx state is persistence input only.
- Each STOP/resume press cancels prepared work before synchronously committing its one saved
  emergency state; no resume-arming state exists.

## Runtime handoff

Changes here normally require combined Automata V3/V4 checks and may require V6
combined-suite validation. Configuration-schema changes also require malformed,
rollback, save/reload, and read-only Mod Config observations.
