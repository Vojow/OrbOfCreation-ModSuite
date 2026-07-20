# Auto Cast testing

[Automata test map](README.md) · [Native contracts](../native-contracts.md) · [Runtime protocol](../runtime-validation.md)

## Risk contract

Auto Cast must discover the active native loadout, respect charge and resource
requirements, reject undecodable cost vectors, preserve manual interruption,
verify the audited native fire boundary, and isolate failures from other
Automata action families.

## Primary ownership

- [AutoCastTests.cs](../../../tests/OrbModding.Tests/AutoCastTests.cs) owns defaults, policy, resource thresholds, charge modes,
  target resolution, native failure behavior, lifecycle blocking, and hot-path
  contract caching.
- [AutomationAdmissionAdapterTests.cs](../../../tests/OrbModding.Tests/AutomationAdmissionAdapterTests.cs) owns normalized cost and readiness facts.
- [NativeMutationVerifierTests.cs](../../../tests/OrbModding.Tests/NativeMutationVerifierTests.cs) owns capture/execute/capture failure semantics.
- [AutomataCoordinatorTests.cs](../../../tests/OrbModding.Tests/AutomataCoordinatorTests.cs) owns scheduling and sibling-feature isolation.
- Game contract tests own exact loadout, cast, resource, target, and Harmony
  members from the installed assemblies.

## Focused command

```powershell
dotnet test tests/OrbModding.Tests/OrbModding.Tests.csproj -p:UseGameStubs=true --filter "FullyQualifiedName~AutoCastTests"
```

Also run `Fast` for any production change. Run `PerformanceAll` when polling,
candidate discovery, reflection caching, or coordinator cadence changes.

## Required cases for behavior changes

- Disabled/default behavior performs no cast mutation.
- Partial or malformed cost vectors reject the entire cast.
- Charged and instant spells use their correct readiness contract.
- Target/loadout changes invalidate prepared work.
- Fire exceptions or unverified results block only Auto Cast for the supported
  recovery boundary.
- Emergency disable and manual interruption stop future automation without
  altering already accepted native work.

## Runtime handoff

V3 proves control placement and read-only state. V4 proves a real cast, resource
deduction, charge behavior, target behavior, interruption, and emergency
disable. An installed contract alone cannot prove that Harmony observation saw
the actual runtime fire.
