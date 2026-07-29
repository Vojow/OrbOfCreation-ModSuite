# Orb Modding Common testing

[Testing hub](README.md) · [Source layout](../../src/README.md) · [Suite integration](suite-integration.md)

Common contains safety and scheduling contracts consumed by every supported
mod. A Common change therefore requires consumer-oriented tests, not only a
unit test for the changed type.

## Contract map

| Common contract | Primary tests | Required consumers |
|---|---|---|
| Lifecycle generation/readiness | [GameLifecycleMonitorTests.cs](../../tests/OrbModding.Tests/GameLifecycleMonitorTests.cs) | Automata, Mentor, Mod Config |
| Local frame-bounded delivery | [GameplayInvalidationBusTests.cs](../../tests/OrbModding.Tests/GameplayInvalidationBusTests.cs), [ModConfigPerformanceTests.cs](../../tests/OrbModding.Tests/ModConfigPerformanceTests.cs) | gameplay invalidation and Mods maintenance |
| Action-family ownership | [ActionFamilyOwnershipTests.cs](../../tests/OrbModding.Tests/ActionFamilyOwnershipTests.cs), [ActionFamilyIntegrationTests.cs](../../tests/OrbModding.Tests/ActionFamilyIntegrationTests.cs) | all native mutation features |
| Native mutation verification | [NativeMutationVerifierTests.cs](../../tests/OrbModding.Tests/NativeMutationVerifierTests.cs) | Auto Buy, Auto Cast, Auto Concept, spell leveling, Mentor |
| Queue-capacity arithmetic | [QueueCapacitySnapshotTests.cs](../../tests/OrbModding.Tests/QueueCapacitySnapshotTests.cs) | Auto Buy |
| Typed registry resolution | [TypedRegistryResolverTests.cs](../../tests/OrbModding.Tests/TypedRegistryResolverTests.cs) | native feature adapters |
| Structured decisions/status | [AutomationDecisionTests.cs](../../tests/OrbModding.Tests/AutomationDecisionTests.cs), [FeatureStatusTests.cs](../../tests/OrbModding.Tests/FeatureStatusTests.cs) | Automata, Mentor, and Mod Config projections |
| Failure circuits | [AutomationCircuitBreakerTests.cs](../../tests/OrbModding.Tests/AutomationCircuitBreakerTests.cs) | native feature adapters |
| Audited-build mutation gate | [AssemblyAuditGateTests.cs](../../tests/OrbModding.Tests/AssemblyAuditGateTests.cs) | every native mutation in the suite |
| Configuration transaction | [ConfigurationSchemaTests.cs](../../tests/OrbModding.Tests/ConfigurationSchemaTests.cs) | all supported plugin binders |
| Generated known identities | [KnownEntitiesGenerationTests.cs](../../tests/OrbModding.Tests/KnownEntitiesGenerationTests.cs), [KnowledgeMapTests.cs](../../tests/OrbModding.Tests/KnowledgeMapTests.cs) | all consumers of `KnownEntities` |
| ServiceCycle execution and lifecycle | [Runtime/ServiceCycle](../../tests/OrbModding.Tests/Runtime/ServiceCycle) | all eight production registrations and the frame pump |
| ServiceCycle semantic trace | [Runtime/ServiceCycle/Tracing](../../tests/OrbModding.Tests/Runtime/ServiceCycle/Tracing), [Runtime/ServiceCycle/Observation](../../tests/OrbModding.Tests/Runtime/ServiceCycle/Observation) | all services, trace capture, and offline verification |
| Trace segment storage and lifecycle catalog | [FileTraceSegmentStorageTests.cs](../../tests/OrbModding.Tests/Runtime/Tracing/FileTraceSegmentStorageTests.cs), [LifecycleDefinitionCatalogTests.cs](../../tests/OrbModding.Tests/Runtime/Catalog/LifecycleDefinitionCatalogTests.cs) | ServiceCycle observation products and future service capture adapters |
| Runtime architecture boundaries | [ArchitectureBoundaryTests.cs](../../tests/OrbModding.Tests/Services/ArchitectureBoundaryTests.cs), [ServiceCycleArchitectureTests.cs](../../tests/OrbModding.Tests/Runtime/ServiceCycle/Registration/ServiceCycleArchitectureTests.cs) | Common and every future ServiceCycle service |

## Selection

Run the exact contract test first, then at least one affected consumer scope and
`Fast`. Examples:

```powershell
dotnet test tests/OrbModding.Tests/OrbModding.Tests.csproj -p:UseGameStubs=true --filter "FullyQualifiedName~GameplayInvalidationBus"
dotnet test tests/OrbModding.Tests/OrbModding.Tests.csproj -p:UseGameStubs=true --filter "FullyQualifiedName~TypedRegistryResolver"
powershell -NoProfile -ExecutionPolicy Bypass -File tools/test-portable.ps1 -Lane Fast
```

Run `PerformanceAll` for invalidation, caching, registry, queue,
decision-publication, or failure-circuit hot-path changes. Run installed
contracts when Common owns or normalizes a native/reflected fact.

## Required invariants

- Lifecycle observations are main-thread, generation-stamped, and idempotent
  where equivalent callbacks overlap.
- Bounded queues coalesce conservatively rather than dropping safety work.
- Local bounded delivery resumes its remaining work on later frames;
  disable/dispose retains correct final state.
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
- Keep local frame guards distinct from ServiceCycle scheduling.
