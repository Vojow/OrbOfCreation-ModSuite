# Chronicle test map

[Back to testing hub](README.md) · [Active plan](../plans/chronicle.md)

Chronicle is read-only timing state over the immutable shared world. It owns no native adapter,
Harmony patch, game save, or progression action.

| Risk | Evidence |
|---|---|
| Preexisting progress receives no fabricated time | `ChronicleRunTrackerTests` |
| Duplicate and simultaneous observations record exactly once | `ChronicleRunTrackerTests` |
| Restoration completes only on the saved flag's false-to-true edge | `ChronicleRunTrackerTests` |
| Lifecycle replacement, missing worlds, and regressed clock/progression pause fail-closed | `ChronicleRunTrackerTests` |
| Exact monotonic ticks exclude manually or automatically paused intervals | `ChronicleRunTrackerTests` |
| Snapshots do not expose mutable milestone arrays | `ChronicleRunTrackerTests` |
| Exact view, upgrade, and BoolVariable UUID predicates | `ChronicleWorldObservationProjectorTests` |
| Partial source categories pause instead of permanently blocking a target | `ChronicleWorldObservationProjectorTests` |
| Restoration remains a bounded one-shot upgrade and completion remains a saved false-initial flag | `ChronicleWorldObservationProjectorTests` |
| Resource catalog identities are unique per feature subsection and schema frozen | `ChronicleRunTrackerTests` |
| Later unlocks capture under their producer feature (Arcanum/Magic and Ore/World) | `ChronicleRunTrackerTests` |
| Cross-feature relationships capture the same resource under every curated subsection | `ChronicleRunTrackerTests` |
| Resource KPI discovery time and quantity/rate/capacity facts freeze exactly once | `ChronicleRunTrackerTests` |
| Preexisting resources receive no fabricated discovery or resource reading | `ChronicleRunTrackerTests` |
| Missing curated resources affect only their KPI row | `ChronicleRunTrackerTests` |
| Completed history round-trips through the atomic sidecar and becomes a compatible PB | `ChronicleRunTrackerTests` |
| Invalid history is preserved and blocks further writes | `ChronicleRunTrackerTests` |
| Runs remains a distinct native Mods page and its rail glyph is audited/distinct | `ModConfigTests` |
| Exact comparison selection crosses the bounded MCP mailbox | `GameMcpChronicleTests` |
| Partial resource collection pauses before a discovery can be captured | `ChronicleWorldObservationProjectorTests` |
| MCP discovery, resource, annotations, mailbox, and terminal result | `GameMcpChronicleTests` and `GameMcpStreamableHttpProtocolTests` |
| New source remains under native-contract source audit | `NativeContractManifestTests` |

Run the focused portable slice with:

```powershell
dotnet test tests/OrbModding.Tests/OrbModding.Tests.csproj `
  -p:UseGameStubs=true --filter FullyQualifiedName~Chronicle
dotnet test tests/OrbModding.ProfileTests/OrbModding.ProfileTests.csproj `
  -p:UseGameStubs=true --filter FullyQualifiedName~GameMcpChronicle
```

The complete portable gate remains `./script/test`. On an installed game, run the manifest tests
and real-reference Release build. Interactive validation must use a disposable fresh run and cover
each view split, Restoration unlock, ritual completion, title/load/reset/NG+ transitions, and a
progressed save whose completion flag is already true.
