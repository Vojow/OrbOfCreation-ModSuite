# Auto Buy testing

[Automata test map](README.md) · [Runtime architecture](../../runtime-architecture/README.md)

Auto Buy is a typed ServiceCycle service. The deleted legacy scheduler,
incremental catalog, dirty-resource index, retry dictionaries, completion
settlement model, and synthetic scheduler simulations are not compatibility
surfaces.

## Risk contract

Auto Buy must decide from the published world snapshot rather than reading the
game — it has no capture; `AutoBuyFrameProjector` runs on the worker — evaluate
only native-free immutable facts, preserve UUID plus exact-type identity, respect
reserves and live queue room, and freshly revalidate lifecycle and native
admission before every mutation. Structures commit one exact queued
level. Upgrades may commit a verified partial or full native multi-buy, with the
global multiplier restored on every exit.

## Test ownership

| Concern | Primary tests |
|---|---|
| Worker admission, reserves, ranking, grouping, and batch shape | [AutoBuyCycleEvaluatorTests.cs](../../../tests/OrbModding.Tests/Services/AutoBuy/Runtime/ServiceCycle/AutoBuyCycleEvaluatorTests.cs) |
| Worker input, eligibility, full/reduced/ledger-starved grouping, action, and requested-level cardinality | `AutoBuyCycleEvaluatorTests` and the decision-journal state projection |
| Worker-side frame projection from the shared world snapshot | [AutoBuyFrameProjectorTests.cs](../../../tests/OrbModding.Tests/Services/AutoBuy/Runtime/ServiceCycle/AutoBuyFrameProjectorTests.cs) |
| Final lifecycle, queue-room, native admission, mutation, and postcondition gates | [AutoBuyCycleActionAdapterTests.cs](../../../tests/OrbModding.Tests/Services/AutoBuy/Runtime/ServiceCycle/AutoBuyCycleActionAdapterTests.cs) |
| Typed registration on the shared Automata host | [AutoBuyServiceCompositionTests.cs](../../../tests/OrbModding.Tests/Services/AutoBuy/Runtime/ServiceCycle/AutoBuyServiceCompositionTests.cs) |
| Human-readable purchase outcomes | [AutoBuyPurchaseNarrationTests.cs](../../../tests/OrbModding.Tests/Services/AutoBuy/Runtime/ServiceCycle/AutoBuyPurchaseNarrationTests.cs) |
| Native multiplier restoration and quarantine | [NativeMultiBuyScopeTests.cs](../../../tests/OrbModding.Tests/NativeMultiBuyScopeTests.cs) |
| Profile-stage bracketing and operation counts | `tests/OrbModding.ProfileTests/AutoBuyProfileTests.cs` |
| Exact installed native shape | `tests/OrbModding.GameContractTests` |

Shared ServiceCycle action, batch, lifecycle, tracing, trace-format, and pump
changes also require their focused Common tests. A change to drain several
actions per frame must cover emergency interruption, per-action semantic facts,
terminal receipts, fairness between registered services, and a bounded
main-thread execution policy.

## Commands

Run the complete portable gate:

```bash
./script/test
```

Then compile against the audited game references:

```bash
OOC_GAME_DIR="$PWD/lib" dotnet build src/OrbModSuite.csproj \
  -p:RequireGameReferences=true -p:UseGameStubs=false -c Debug
```

Performance changes are incomplete until a profiler-enabled debug build is
installed and compared against a recorded gameplay trace. Portable timings and
synthetic operation counts do not establish Unity-frame performance.

## Runtime handoff

Portable tests cannot prove Harmony callback order, actual queue consumption,
save/load behavior, visible controls, or player responsiveness. Follow Automata
V3/V4 in the [runtime protocol](../runtime-validation.md). Record the exact
trace run folder and compare Auto Buy capture, action, total pump, worst-frame,
worker decision, commit, skip, rejection, and fault distributions before
starting the next worklist item. The dashboard correlates worker duration with
captured candidates and planned actions, reporting average milliseconds plus
microseconds per input candidate and per planned action.
The profiler-enabled debug install starts the full trace and performance
profile automatically; closing the game is sufficient to stop and flush both.
