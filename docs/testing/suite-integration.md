# Suite integration testing

[Testing hub](README.md) · [Common testing](common.md) · [Runtime protocol](runtime-validation.md)

Suite integration owns behavior that cannot be assigned safely to one feature:
ServiceCycle scheduling, lifecycle generation, action-family ownership, invalidation,
feature health, configuration status, and combined runtime compatibility.

## Primary ownership

- [ActionFamilyIntegrationTests.cs](../../tests/OrbModding.Tests/ActionFamilyIntegrationTests.cs) — ownership loss/recovery across real
  feature action boundaries.
- [Runtime/ServiceCycle](../../tests/OrbModding.Tests/Runtime/ServiceCycle) — fair typed-service turns, lifecycle replacement, emergency stop, and bounded trace/profile evidence.
- [ArchitectureBoundaryTests.cs](../../tests/OrbModding.Tests/Services/ArchitectureBoundaryTests.cs) — every feature uses the one accepted runtime and workers cannot reach native state.
- [ModRuntimeStatusProjectionTests.cs](../../tests/OrbModding.Tests/ModRuntimeStatusProjectionTests.cs) and [ConfigurationSchemaStatusProjectionTests.cs](../../tests/OrbModding.Tests/ConfigurationSchemaStatusProjectionTests.cs) —
  cross-plugin status visibility.

## Commands

```powershell
dotnet test tests/OrbModding.Tests/OrbModding.Tests.csproj -p:UseGameStubs=true --filter "FullyQualifiedName~ActionFamilyIntegration|FullyQualifiedName~ServiceCycle"
powershell -NoProfile -ExecutionPolicy Bypass -File tools/test-portable.ps1 -Lane Reliability
powershell -NoProfile -ExecutionPolicy Bypass -File tools/test-portable.ps1 -Lane PerformanceAll
```

## Required invariants

- Each eligible service receives at most its fixed action turn per accepted frame.
- A service with a draining batch cannot starve later registrations.
- Action-family conflicts stop only overlapping mutations.
- Lifecycle transitions cancel all stale prepared work before any feature can
  resume.
- Emergency disable stops every supported automation feature immediately.
- Disabled modules perform no background scans or catalog rebuilds.
- Failure, quarantine, or absence in one plugin/domain remains isolated.
- Status and diagnostics remain bounded and contain no save path or private
  content.

## Runtime handoff

V6 is the authoritative combined-suite gate. Test the supported suite alone
first, then any explicitly supported known-conflict setup. Exercise title/load,
scene changes, save reload, reset/NG+, emergency disable, manual actions, and an
extended combined session. A headless fairness result cannot prove Unity frame
responsiveness or native callback ordering.

- Record comparable desktop and Steam Deck/Proton combined-suite profiles.
