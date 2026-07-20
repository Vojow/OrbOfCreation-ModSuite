# Suite integration testing

[Testing hub](README.md) · [Common testing](common.md) · [Runtime protocol](runtime-validation.md)

Suite integration owns behavior that cannot be assigned safely to one plugin:
shared scheduling, lifecycle generation, action-family ownership, invalidation,
feature health, configuration status, and combined runtime compatibility.

## Primary ownership

- [CombinedSuiteHeadlessTests.cs](../../tests/OrbModding.Tests/CombinedSuiteHeadlessTests.cs) — sustained shared backlog, fairness, and
  supported performance profile.
- [ActionFamilyIntegrationTests.cs](../../tests/OrbModding.Tests/ActionFamilyIntegrationTests.cs) — ownership loss/recovery across real
  feature coordinators.
- [LifecycleStateMachineScenarioTests.cs](../../tests/OrbModding.Tests/LifecycleStateMachineScenarioTests.cs) — reusable cross-feature lifecycle
  sequences.
- [RuntimeReplayTests.cs](../../tests/OrbModding.Tests/RuntimeReplayTests.cs) — sanitized ordering-sensitive event journeys.
- [SuitePerformanceEvidenceTests.cs](../../tests/OrbModding.Tests/SuitePerformanceEvidenceTests.cs) — exact registered work identity and
  checked evidence semantics.
- [ModRuntimeStatusProjectionTests.cs](../../tests/OrbModding.Tests/ModRuntimeStatusProjectionTests.cs) and [ConfigurationSchemaStatusProjectionTests.cs](../../tests/OrbModding.Tests/ConfigurationSchemaStatusProjectionTests.cs) —
  cross-plugin status visibility.

## Commands

```powershell
dotnet test tests/OrbModding.Tests/OrbModding.Tests.csproj -p:UseGameStubs=true --filter "FullyQualifiedName~CombinedSuite|FullyQualifiedName~ActionFamilyIntegration|FullyQualifiedName~LifecycleStateMachine|FullyQualifiedName~RuntimeReplay"
powershell -NoProfile -ExecutionPolicy Bypass -File tools/test-portable.ps1 -Lane Reliability
powershell -NoProfile -ExecutionPolicy Bypass -File tools/test-portable.ps1 -Lane PerformanceAll
```

## Required invariants

- No more than the supported native mutation admission occurs per frame.
- Long work in one subsystem cannot starve another beyond checked thresholds.
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

## Known next work

- Expand the two-fixture replay corpus with every reproducible ordering defect.
- Add bounded seeded event sequences and serialize reduced failures to replay.
- Record comparable desktop and Steam Deck/Proton combined-suite profiles.
