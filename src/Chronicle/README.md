# Chronicle

Chronicle is the suite-owned, read-only run timer and comparison domain. It compiles into
`OrbModSuite.dll`; it is not a separate plugin and it does not register Harmony patches or native
reflection adapters.

The accepted milestone source is the immutable schema-3 `GameWorldState` publication. Chronicle
projects exact UUID/type-backed view availability, the Restoration upgrade's exhausted state, and
the saved world-completion Boolean into a primitive observation after each ServiceCycle host tick.
It accepts those predicates only when the `views`, `upgrades`, `bool variables`, `resources`, and
`time runes` collections all report a clean collection. It retains no Unity or game object and performs no
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

Time-rune build capture records ordered observed level transitions from the same immutable world.
Each event carries exact `TimeRuneSO` UUID/type, the game's display label, elapsed ticks, level
before/after, levels gained, mastery level, discovery rarity, and exact authored type membership.
Tempo, Scaling, and Investment build shares are weighted by levels gained. A rune with zero or
multiple core archetypes is isolated as `Other`. Multiple purchases inside one 250 ms publication
are honestly represented as one observed level range rather than fabricated individual clicks.
Missing or regressed rune evidence pauses fail-closed. Detail is bounded at 512 events while the
complete archetype totals continue accumulating.

The run engine has explicit start, pause, resume, and abandon
commands. It records Magic at zero, marks already-satisfied predicates `Preexisting`, records new
predicates exactly once, and finishes only on a false-to-true observation of the saved
`PersistenceHasCompletedWorld` flag. Lifecycle replacement or loss of a valid world publication
pauses the run without backfilling elapsed time or splits. The clock uses the suite's exact
monotonic ticks while Chronicle is running; title scenes, incomplete collections, manual pauses,
and automatically paused intervals do not count. A backward clock, world generation, or previously
observed native progression also pauses fail-closed.

Completed runs are retained in a validated schema-v2 sidecar under the suite configuration
directory. Writes occur only on Chronicle events and use atomic replacement; invalid content is
preserved and makes history read-only. Schema-v1 history remains readable without invented rune
events. Compatible PB, previous, and exact selected-run comparisons include split deltas plus
resource quantity/rate/capacity deltas and ratios; compatible rune runs additionally expose their
level-weighted build mix.

Mods exposes a native-skinned **Runs** rail page with a live timer, split matrix, filtered/paged rune
timeline, current/PB build mix, expandable feature-resource subsections, an archive, and explicit
controls. Perf-debug Game MCP exposes the same snapshot and command boundary—including the
read-only filtered `chronicle_runes` query and `chronicle_select_comparison`—through its bounded
main-thread mailbox. Runtime validation remains tracked in
[the active plan](../../docs/plans/chronicle.md).
