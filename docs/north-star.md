# The North Star

Read this first. It states what the tree is trying to be. If code and this page
disagree, identify which one is wrong; do not preserve two stories.

## The system

OrbOfCreation-ModSuite is one BepInEx plugin, one configuration authority, and
one ServiceCycle runtime. The Unity main thread collects native-free facts,
publishes immutable readings, and applies actions. Workers derive state and make
policy decisions only from the world, configuration, and strategy publications
they were handed.

The game owns identity, authored structure, mutable availability, and transaction
execution. The suite owns audited formulas and policy. Stable identity is UUID
plus expected native type, never a display name. A mutation is successful only
when its exact postcondition is observed.

## Dataflow

1. **Collect:** the one Source service reads bounded native facts on the main
   thread every 250 milliseconds and stamps the capture with the current frame.
2. **Derive:** a worker turns that capture into an immutable `GameWorldState` and
   computes the suite-owned formulas.
3. **Publish:** the completed world returns through the action pipeline and
   replaces the suite-wide world publication.
4. **Consume:** each Ordinary service evaluates a pinned
   `(world, configuration, strategy)` tuple on a worker and returns actions to the
   main thread.
5. **Validate and act:** the action boundary resolves current native identity and
   mutable facts, performs at most its declared mutation, and verifies the exact
   before/after evidence.

An Ordinary service starts only on a world collected after it became live and
after its own last game-facing attempt. Commit, skip, rejection, and fault all
raise that freshness floor; Source publication attempts are the sole exemption.
No service acts twice on one world or retries against facts its last attempt may
have changed.

## Three publications

World, configuration, and strategy each have one suite-wide immutable slot and
one suite-wide generation. The main thread replaces them; workers hold pinned
readings without locks. A stale reading costs memory, not thread safety, and its
permission to act is bounded by the freshness and action rules above.

All configuration writers converge on the committed configuration store. Direct
controls publish a freshly saved snapshot synchronously; Mods-page and external
file changes commit through the same store. A running batch keeps its pinned
configuration. Re-reading current configuration inside each action would create
a second authority to narrow only a frame-scale window; lifecycle, emergency,
ownership, and live native facts remain immediate boundaries.

Strategy is advisory beneath user configuration and native validation. The
current neutral bulletin cannot authorize a spend configuration refused; a
future strategist may only narrow policy.

## Two service shapes

- **Source:** bounded main-thread capture, worker derivation, main-thread
  publication. World collection is the current Source.
- **Ordinary:** publication gate, worker evaluation, main-thread action. Every
  gameplay feature uses this shape.

There is no third scheduler, feature-local timer, alternate configuration store,
or second mutation path. Local UI maintenance and lifecycle repair bounds remain
local delivery mechanisms, not service engines.

## Native boundary

Every native member used at a capture, action, or patch boundary is declared in
the schema-3 native contract manifest. Remaining source-audit hits require
narrow, reasoned exemptions; an exemption is not gameplay authority. Workers
cannot retain a path back to Unity or mutable runtime owners.

The canonical rules for owned math, freshness classes, GameActions, UI capture,
and failure handling live in the
[game-boundary doctrine](runtime-architecture/game-boundary-doctrine.md). The
[engineering doctrine](development/engineering-doctrine.md) records the review
rules learned while applying them.

## Simplicity

Every seam pays rent. The released tree has one DLL, one plugin identity, one
configuration file, one runtime, one lifecycle model, one diagnostics join, and
one action definition per capability. New work extends those shared boundaries;
it does not add feature-specific publishers, current-state side channels,
schedulers, fallback renderers, or mutation adapters.

Prefer an explicit refusal over partial operation whose truth cannot be proved.
Prefer a clean pinned input over machinery that reacts a few frames sooner.
Prefer deleting a completed plan, retired compatibility seam, or duplicate guide
over preserving history in the active reading path.

## Observability

- **Performance profile:** compile-time opt-in measurements for owner and worker
  stages; absent from release builds.
- **Manual full trace:** opt-in semantic events plus configuration and strategy
  publication stores. Raw world payloads are not recorded; that remains the
  explicit [world-store decision](plans/full-trace-world-store.md).
- **Decision journal:** bounded rolling numeric service decisions with durable
  health and lifecycle evidence.
- **Recent-event dump:** an on-demand bounded snapshot of current host evidence
  for a bug report.

Observation may fail without changing gameplay, but it must report that failure
and may not start a substitute writer or format. Runtime replay is not a product
system; deterministic scenarios own reproducible re-execution.

## Direction

Keep the released runtime small, measure before optimizing, and add the
strategist as another publication rather than another engine. New capabilities
reuse lifecycle, ownership, binding, verification, freshness, diagnostics, and
GameAction infrastructure. The [active plans](plans/README.md) contain only work
that has not landed.
