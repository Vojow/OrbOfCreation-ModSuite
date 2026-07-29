# Orb Mentor testing

[Testing hub](README.md) · [Mentor behavior reference](../../src/Mentor/README.md) · [Interactive checklist](mentor-runtime-validation.md)

## Risk contract

Mentor must consume each exact native mastery input once, choose recipients only
from an immutable world publication, and grant through a freshly revalidated
native boundary. Stable UUID/type identity, lifecycle coherence, source mastery
ceiling, economy arithmetic, recursion suppression, and exact postconditions are
the safety boundary.

## Test ownership

| Concern | Primary tests |
|---|---|
| Worker policy, domains, economy, ordering, and overflow evidence | [MentorCycleEvaluatorTests.cs](../../tests/OrbModding.Tests/Services/Mentor/Runtime/ServiceCycle/MentorCycleEvaluatorTests.cs) |
| Action result mapping and lifecycle/fault semantics | [MentorCycleActionAdapterTests.cs](../../tests/OrbModding.Tests/Services/Mentor/Runtime/ServiceCycle/MentorCycleActionAdapterTests.cs) |
| Live revalidation and spell/artifact/alchemy postconditions | [MentorNativeAdapterTests.cs](../../tests/OrbModding.Tests/Services/Mentor/Runtime/ServiceCycle/MentorNativeAdapterTests.cs) |
| Typed registration, publication flow, and status | [MentorServiceCompositionTests.cs](../../tests/OrbModding.Tests/Services/Mentor/Runtime/ServiceCycle/MentorServiceCompositionTests.cs) |
| Bounded exact-XP input journal | [MentorMasteryEventJournalTests.cs](../../tests/OrbModding.Tests/Services/Mentor/Runtime/World/MentorMasteryEventJournalTests.cs) |
| Harmony bindings and patch-place contracts | [HarmonyBindingHeadlessTests.cs](../../tests/OrbModding.Tests/HarmonyBindingHeadlessTests.cs), `OrbModding.GameContractTests` |
| Trace roster and dashboard projection labels | [TraceRosterTests.cs](../../tests/OrbModding.Tests/Runtime/ServiceCycle/Observation/Roster/TraceRosterTests.cs), `OrbModding.ProfileTests.TraceDashboardReaderTests` |

## Commands

```powershell
dotnet test tests/OrbModding.Tests/OrbModding.Tests.csproj -p:UseGameStubs=true --filter "FullyQualifiedName~Mentor"
powershell -NoProfile -ExecutionPolicy Bypass -File tools/test-portable.ps1 -Lane Fast
```

The focused filter is only a navigation aid. Run `./script/test` and the
installed-game contract suite before claiming the migration or a native-boundary
change is green.

## Required cases for behavior changes

- Each sequence is consumed once; retained-history overflow is explicit.
- `EquippedSpells` and `HighestDiscovered` preserve their distinct source rules.
- Artifact and alchemy enablement remain independent.
- Shared-pool and per-recipient arithmetic produce the exact action amounts.
- Registry presence is not treated as discovery, creation, equipment, or unlock.
- Every action rechecks UUID/type, lifecycle, ownership, eligibility, and the
  exclusive source-mastery ceiling.
- A throw, no-op, partial, unexpected, unsaved, or unobservable native result
  never becomes a committed action.
- Disabled Mentor performs no grants and advances past retained inputs safely.

## Runtime handoff

Use the [Mentor interactive checklist](mentor-runtime-validation.md) after the
shared V0–V3 gates. Validate spells, artifacts, and alchemy independently, then
run the combined-suite gate. Record the exact build, disposable save, settings,
earned XP, recipient transitions, control/status state, logs, and rollback.
