# Chronicle

Chronicle is the suite-owned, read-only run timer and comparison domain. It compiles into
`OrbModSuite.dll`; it is not a separate plugin and it does not register Harmony patches or native
reflection adapters.

The accepted milestone source is the immutable schema-3 `GameWorldState` publication. Chronicle
projects exact UUID/type-backed view availability, the Restoration upgrade's exhausted state, and
the saved world-completion Boolean into a primitive observation after each ServiceCycle host tick.
It accepts those predicates only when the `views`, `upgrades`, `bool variables`, and `resources`
collections all report a clean collection. It retains no Unity or game object and performs no
progression mutation. The ServiceCycle boundary exposes only a neutral immutable
world/generation/lifecycle/clock capture; Chronicle, not Automata, owns milestone interpretation.

The run snapshot also contains seven curated, non-exclusive feature-resource KPI sections. Rows
capture independently on their first complete observation with `Visible == true`; a later upgrade
can therefore reveal Arcanum under Magic or Ore under World. The 39 exact `ResourceSO` UUIDs and 44
feature links are presentation metadata rather than a claim that the game owns a native
feature-to-resource relation. Captured facts are discovery ticks, visibility, raw and true
quantity, true net rate, and capacity/fill state. Resources visible when a run starts are
`Preexisting` and receive no invented discovery or reading. Each section exposes an explicit
producer/usage relationship so cross-feature links remain understandable to UI and MCP consumers.

The run engine has explicit start, pause, resume, and abandon
commands. It records Magic at zero, marks already-satisfied predicates `Preexisting`, records new
predicates exactly once, and finishes only on a false-to-true observation of the saved
`PersistenceHasCompletedWorld` flag. Lifecycle replacement or loss of a valid world publication
pauses the run without backfilling elapsed time or splits. The clock uses the suite's exact
monotonic ticks while Chronicle is running; title scenes, incomplete collections, manual pauses,
and automatically paused intervals do not count. A backward clock, world generation, or previously
observed native progression also pauses fail-closed.

Completed runs are retained in a validated schema-v1 sidecar under the suite configuration
directory. Writes occur only on Chronicle events and use atomic replacement; invalid content is
preserved and makes history read-only. Compatible PB, previous, and exact selected-run comparisons
include split deltas plus resource quantity/rate/capacity deltas and ratios.

Mods exposes a native-skinned **Runs** rail page with a live timer, split matrix, expandable
feature-resource subsections, an archive, and explicit controls. Perf-debug Game MCP exposes the
same snapshot and command boundary—including `chronicle_select_comparison`—through its bounded
main-thread mailbox. Runtime validation remains tracked in
[the active plan](../../docs/plans/chronicle.md).
