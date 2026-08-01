# Repository test strategy

[Testing hub](README.md) · [Headless E2E](headless-e2e.md) ·
[Runtime validation](runtime-validation.md)

## Supported baseline

- Windows 64-bit Orb of Creation, Unity `6000.0.70`, Mono backend
- BepInEx `5.4.23.x`
- plugin target `netstandard2.1`
- Steam Deck through the Windows game under Proton

Other game builds, native Linux, and BepInEx 6 require explicit audit and
runtime evidence before they become supported.

## Layers and claims

1. **Unit/component:** pure policy, arithmetic, state transitions,
   configuration transactions, and failure containment.
2. **Native-shaped integration:** production adapters against deliberately small
   `Assembly-CSharp`-shaped stubs.
3. **Headless E2E and performance:** complete deterministic journeys through the
   real ServiceCycle engine and simulated native boundaries.
4. **Installed contracts:** admitted assembly-pair hashes and exact native
   metadata, without loading Unity.
5. **UAT:** Harmony application, native effects, persistence, controls, layout,
   player control, and observed frame behavior.

Each layer states what it cannot prove. Portable success is not installed-game
compatibility; metadata success is not a verified mutation; a screenshot is not
a semantic assertion.

## Portable gate

```bash
ORB_TEST_ATTEMPTS=1 ./script/test
```

The script runs the ordinary and profile test projects sequentially, then builds
the trace tool with profiling enabled. It has a hard 60-second limit per attempt.
Concurrency tests synchronize on events or observable state; sleeps are not
evidence. Performance tests assert deterministic work and allocation bounds;
host timing is diagnostic until repeated in the installed game.

Useful focused scopes are:

```powershell
dotnet test tests/OrbModding.Tests/OrbModding.Tests.csproj -p:UseGameStubs=true --filter "Category=HeadlessIntegration"
dotnet test tests/OrbModding.Tests/OrbModding.Tests.csproj -p:UseGameStubs=true --filter "Category=HeadlessE2E"
dotnet test tests/OrbModding.Tests/OrbModding.Tests.csproj -p:UseGameStubs=true --filter "Category=PerformanceSimulation"
powershell -NoProfile -ExecutionPolicy Bypass -File tools/test-portable.ps1 -Lane Reliability
```

## Installed contracts

```bash
OOC_GAME_DIR=/path/to/audited/game dotnet test \
  tests/OrbModding.GameContractTests/OrbModding.GameContractTests.csproj \
  --configuration Release
```

With `OOC_GAME_DIR`, the suite verifies the complete admitted assembly pair and
all declared member, overload, parameter, field, inheritance, and return types.
Without it, installed checks skip while manifest structure and source coverage
still run. A skip must never be reported as compatibility evidence.

## Coverage

The one shipped assembly has one enforced line-coverage floor: **73.4%**.
`tests/coverage.runsettings` explicitly includes `[OrbModSuite]*`; the test and
stub assemblies are excluded. `tools/check-coverage.ps1` fails a missing package
and checks the overall and package rate, which are the same one-assembly
measurement. Branch coverage is diagnostic only.

Coverage is a regression floor, not a reason to test low-value implementation
detail. Core state machines should prefer focused behavioral cases; Unity view
code needs component seams, installed contracts, and UAT.

## Review policy

- Test-only changes may merge after the complete portable gate, coverage,
  repository hygiene, installed contracts, and applicable real-reference builds.
- Runtime changes also require proportional UAT from
  [runtime validation](runtime-validation.md).
- Release candidates run the full runtime sequence from the exact archive.
- Live defects become portable red-green regressions when the native contract
  can be modeled faithfully; a divergent stub is fixed, not treated as a license
  to weaken the product.

Count changes are reviewed as a single ledger: ordinary tests, profile tests,
installed tests, manifest schema, contracts, source exemptions, known entities,
and compiler warnings. Additions and removals both require a cause.

## Game updates

An unknown complete assembly pair enters compatibility quarantine. Audit it as
an explicit manifest diff, run installed metadata checks even when the hashes
fail, compile against the candidate references, and complete focused UAT. Hash
acceptance never replaces exact adapter checks or postcondition verification.
