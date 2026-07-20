# Automata integration testing

[Automata test map](README.md) · [Common testing](../common.md) · [Suite integration](../suite-integration.md)

This page owns Automata behavior that is broader than one feature.

## Primary ownership

- [AutomataTests.cs](../../../tests/OrbModding.Tests/AutomataTests.cs) — configuration defaults, migration-facing behavior, and
  feature composition.
- [AutomataConfigurationSchemaTests.cs](../../../tests/OrbModding.Tests/AutomataConfigurationSchemaTests.cs) — schema-zero migration, rollback, and
  safe typed binding.
- [AutomataCoordinatorTests.cs](../../../tests/OrbModding.Tests/AutomataCoordinatorTests.cs) — shared scheduling, ownership loss, lifecycle,
  quarantine, and sibling progress.
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
- Shared budget denial delays work without dropping it or starving another
  registered feature.
- Runtime status reports saved configuration separately from actual readiness.

## Runtime handoff

Changes here normally require combined Automata V3/V4 checks and may require V6
combined-suite validation. Configuration-schema changes also require malformed,
rollback, save/reload, and read-only Mod Config observations.
