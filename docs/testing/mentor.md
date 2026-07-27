# Orb Mentor testing

[Testing hub](README.md) · [Mentor behavior reference](../../src/Mentor/README.md) · [Interactive checklist](mentor-runtime-validation.md)

## Risk contract

Mentor must grant exactly earned mastery XP through audited native domains,
preserve pending XP under budget deferral, distinguish discovery/equipped/
completed state per domain, isolate optional-domain failures, and invalidate all
native references across lifecycle changes.

## Test ownership

| Concern | Primary tests |
|---|---|
| Core policy and spell relationships | [MentorTests.cs](../../tests/OrbModding.Tests/MentorTests.cs) |
| Coordinator, pending work, fairness | [MentorCoordinatorTests.cs](../../tests/OrbModding.Tests/MentorCoordinatorTests.cs) |
| Reflection-shaped runtime behavior | [MentorRuntimeHeadlessTests.cs](../../tests/OrbModding.Tests/MentorRuntimeHeadlessTests.cs) |
| Domain unlock capability | [MentorDomainUnlockTests.cs](../../tests/OrbModding.Tests/MentorDomainUnlockTests.cs) |
| Artifacts and alchemy | [MentorAlchemyDomainTests.cs](../../tests/OrbModding.Tests/MentorAlchemyDomainTests.cs), [AlchemyGameplayDomainClassifierTests.cs](../../tests/OrbModding.Tests/AlchemyGameplayDomainClassifierTests.cs) |
| Shared invalidation | [MentorGameplayInvalidationBridgeTests.cs](../../tests/OrbModding.Tests/MentorGameplayInvalidationBridgeTests.cs) |
| Deterministic work and bounded evidence | [MentorPerformanceTests.cs](../../tests/OrbModding.Tests/MentorPerformanceTests.cs) |
| Installed game shape | `OrbModding.GameContractTests` Mentor contracts |

## Commands

```powershell
dotnet test tests/OrbModding.Tests/OrbModding.Tests.csproj -p:UseGameStubs=true --filter "FullyQualifiedName~Mentor|FullyQualifiedName~AlchemyGameplayDomainClassifier"
powershell -NoProfile -ExecutionPolicy Bypass -File tools/test-portable.ps1 -Lane Fast
```

Run `PerformanceAll` for changes to relationship planning, coalescing, evidence
retention, coordinator cadence, or XP backlog processing.

## Required cases for behavior changes

- XP is never dropped, duplicated, or redirected to a newer relationship.
- Budget deferral preserves exact pending work and eventual progress.
- Spell, artifact, and alchemy domains fail and recover independently.
- Registry presence is not treated as discovery, equipment, or completion.
- Stable UUID plus expected native type owns identity.
- Lifecycle transitions discard old native instances and stale generation work.
- Disabled domains perform no scans or grants.

## Runtime handoff

Use the [Mentor interactive checklist](mentor-runtime-validation.md) after the
shared V0–V3 gates. Validate spells, artifacts, and alchemy independently before
the combined soak. Record exact build, disposable save, settings, earned XP,
native mastery change, logs, and rollback result.

## Known next work

- Add a stable `MentorReliability` lane rather than relying on name filters.
- Grow sanitized scenario-fixture coverage for ordering-sensitive XP/lifecycle failures.
- Record desktop and Steam Deck allocation/timing profiles for sustained mixed
  domain activity.
