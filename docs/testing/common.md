# Orb Modding Common testing

[Testing hub](README.md) · [Source layout](../../src/README.md) · [Suite integration](suite-integration.md)

Common contains safety and scheduling contracts consumed by every supported
mod. A Common change therefore requires consumer-oriented tests, not only a
unit test for the changed type.

## Contract map

| Common contract | Primary tests | Required consumers |
|---|---|---|
| Lifecycle generation/readiness | [GameLifecycleMonitorTests.cs](../../tests/OrbModding.Tests/GameLifecycleMonitorTests.cs), [LifecycleStateMachineScenarioTests.cs](../../tests/OrbModding.Tests/LifecycleStateMachineScenarioTests.cs) | Automata, Mentor, Mod Config |
| Gameplay invalidation bus | [GameplayInvalidationBusTests.cs](../../tests/OrbModding.Tests/GameplayInvalidationBusTests.cs) | Automata, Mentor, Mod Config bridges |
| Shared performance coordinator | [PerformanceFoundationTests.cs](../../tests/OrbModding.Tests/PerformanceFoundationTests.cs), [SuitePerformanceEvidenceTests.cs](../../tests/OrbModding.Tests/SuitePerformanceEvidenceTests.cs) | Automata and Mentor coordinators, Mod Config recovery |
| Action-family ownership | [ActionFamilyOwnershipTests.cs](../../tests/OrbModding.Tests/ActionFamilyOwnershipTests.cs), [ActionFamilyIntegrationTests.cs](../../tests/OrbModding.Tests/ActionFamilyIntegrationTests.cs) | all native mutation features |
| Native mutation verification | [NativeMutationVerifierTests.cs](../../tests/OrbModding.Tests/NativeMutationVerifierTests.cs) | Auto Buy, Auto Cast, Auto Concept, spell leveling, Mentor |
| Queue-capacity arithmetic | [QueueCapacitySnapshotTests.cs](../../tests/OrbModding.Tests/QueueCapacitySnapshotTests.cs) | Auto Buy |
| Typed registry resolution | [TypedRegistryResolverTests.cs](../../tests/OrbModding.Tests/TypedRegistryResolverTests.cs) | Automata and Mentor classifiers/catalogs |
| Structured decisions/status | [AutomationDecisionTests.cs](../../tests/OrbModding.Tests/AutomationDecisionTests.cs), [FeatureStatusTests.cs](../../tests/OrbModding.Tests/FeatureStatusTests.cs) | Automata and Mod Config projections |
| Failure circuits/admission | [AutomationCircuitBreakerTests.cs](../../tests/OrbModding.Tests/AutomationCircuitBreakerTests.cs), [AutomationAdmissionAdapterTests.cs](../../tests/OrbModding.Tests/AutomationAdmissionAdapterTests.cs) | Automata and Mentor adapters |
| Configuration transaction | [ConfigurationSchemaTests.cs](../../tests/OrbModding.Tests/ConfigurationSchemaTests.cs) | all supported plugin binders |
| Generated known identities | [KnownEntitiesGenerationTests.cs](../../tests/OrbModding.Tests/KnownEntitiesGenerationTests.cs), [KnowledgeMapTests.cs](../../tests/OrbModding.Tests/KnowledgeMapTests.cs) | all consumers of `KnownEntities` |
| ServiceCycle execution and lifecycle | [Runtime/ServiceCycle](../../tests/OrbModding.Tests/Runtime/ServiceCycle) | Auto Harvest production registration and frame pump |
| ServiceCycle semantic trace and replay | [Runtime/ServiceCycle/Replay](../../tests/OrbModding.Tests/Runtime/ServiceCycle/Replay), [Runtime/ServiceCycle/Tracing](../../tests/OrbModding.Tests/Runtime/ServiceCycle/Tracing) | Auto Harvest replay capture and offline verification |
| Replay segment storage and lifecycle catalog | [FileTraceSegmentStorageTests.cs](../../tests/OrbModding.Tests/Runtime/Tracing/FileTraceSegmentStorageTests.cs), [LifecycleDefinitionCatalogTests.cs](../../tests/OrbModding.Tests/Runtime/Catalog/LifecycleDefinitionCatalogTests.cs) | ServiceCycle exporters and future service capture adapters |
| Runtime architecture boundaries | [ArchitectureBoundaryTests.cs](../../tests/OrbModding.Tests/Services/ArchitectureBoundaryTests.cs), [ServiceCycleArchitectureTests.cs](../../tests/OrbModding.Tests/Runtime/ServiceCycle/Registration/ServiceCycleArchitectureTests.cs) | Common and every future ServiceCycle service |

## Selection

Run the exact contract test first, then at least one affected consumer scope and
`Fast`. Examples:

```powershell
dotnet test tests/OrbModding.Tests/OrbModding.Tests.csproj -p:UseGameStubs=true --filter "FullyQualifiedName~GameplayInvalidationBus"
dotnet test tests/OrbModding.Tests/OrbModding.Tests.csproj -p:UseGameStubs=true --filter "FullyQualifiedName~TypedRegistryResolver"
powershell -NoProfile -ExecutionPolicy Bypass -File tools/test-portable.ps1 -Lane Fast
```

Run `PerformanceAll` for coordinator, invalidation, caching, registry, queue,
decision-publication, or failure-circuit hot-path changes. Run installed
contracts when Common owns or normalizes a native/reflected fact.

## Required invariants

- Lifecycle observations are main-thread, generation-stamped, and idempotent
  where equivalent callbacks overlap.
- Bounded queues coalesce conservatively rather than dropping safety work.
- Budget deferral preserves work and fairness; disable/dispose retains correct
  final evidence.
- Identity is stable UUID plus exact expected native type.
- Unknown, contradictory, or stale native facts fail closed.
- Mutation verification never turns an exception, no-op, or partial result into
  a committed success.
- Shared helpers add no per-frame reflection discovery, scene-wide searches, or
  avoidable allocation-heavy collection work.

## Runtime handoff

Common has no independent player-facing UAT. Validate every affected consumer
through [suite integration](suite-integration.md) and the ordered runtime gates.
Lifecycle, scheduling, or ownership changes normally require V6 combined-suite
evidence.

## Known next work

- Categorize Common contracts into stable `CommonDecision`,
  `CommonReliability`, and `CommonPerformance` lanes.
- Enforce reviewed branch floors for core state machines and failure paths.
- Split the largest shared performance fixtures by coordinator, evidence, and
  admission responsibility.
