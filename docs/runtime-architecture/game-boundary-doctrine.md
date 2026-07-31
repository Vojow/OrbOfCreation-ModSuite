# Game boundary doctrine

> **Lifecycle: Accepted production rule set.** Every surface that touches the game — reads,
> owned math, mutations, suite UI, and the Game MCP — follows these rules. A lane that needs
> an exception records the justification in its report, and the accepted exception becomes a
> rule here rather than a per-feature variant.

[Back to dossier](README.md) · [Goals and invariants](goals-and-invariants.md)

## The stance

The suite owns its math and its behavior. The game is the authority on **identity** (what
exists, UUIDs, types), **structure** (relationships, unlock state, serialized graphs), and
**transaction execution** (mutations and their results). Every touch of game code, data, or
assets is a declared schema-3 contract. Nothing the suite does may depend on ambient game-UI
state — which screen is open, what is loaded, what the player happens to be looking at.

The two failure modes this doctrine exists to prevent are not symmetric. A broken declared
contract fails loudly and enumerably at audit time, before anything acts. An undeclared
dependency on UI state fails per-screen, per-session, invisibly. When a choice exists, take
the risk the manifest can see.

## Reads and owned math

- Compute what we can; collect what we cannot. Collection cost is a real budget: every native
  read is main-thread FFI work, and time spent collecting answers we can derive ourselves is
  time the suite does nothing.
- Owned math is verifier-covered. Divergence between suite math and the game's own answers
  must be detectable, loud, and named — never assumed away.
- Never re-derive what only the game can know. `WorldAlchemyRecipe` capturing the natively
  resolved `AlchemyRecipeSO.GetMaxUsageSlots()` instead of interpreting the raw modifier and
  its `-1` sentinel is the paid-for lesson: re-implementing *facts* (as opposed to *formulas*)
  silently diverges when the game changes.
- A published value that requires a screen visit to be fresh is a **collection defect**.
  Either the collector performs the declared revalidation the UI would perform, or the value
  is published as stale-capable and no action may be authorized by it. "Works when the screen
  is open" is never an accepted state.

## Boundary validators and freshness classes

The main thread re-checks live native facts before mutating. Some of the game's own validator
methods are backed by caches that only recompute when a screen views them — the game can be
the stale party while the suite's snapshot is honest. Every native validator used in an
action protocol is therefore classified in its contract dossier:

1. **Pure.** Computes from live state on every call. Trust it.
2. **UI-cached, revalidatable.** Backed by a visibility-driven cache with a known recompute
   path. The action protocol invokes that exact recompute as its own declared, scoped
   contract call immediately before asking — never ambient or blanket cache-touching.
   Warming an unrelated cache has changed game math before; every revalidation is named,
   scoped to its action, and audited.
3. **Unrefreshable or side-effectful.** No safe recompute path exists. Do not pre-validate.
   Attempt the mutation, verify postconditions exactly, and treat a rejection as the answer.
   The one-attempt-per-world rule makes this self-correcting: a stale-validator rejection
   costs one publication, and the next publication is a fresh collection.

Every rejection names the check that refused, in the named-cache vocabulary
(`UpgradeSO.cachedCostLevel` is the naming precedent). "The game said no" is not a reason.

Action dispatch deliberately continues under the cycle-pinned configuration even when a newer
committed generation appears while its batch drains. That staleness is bounded by the batch's
one-or-two-frame execution (normally one or two publications), and configuration has one
player-driven writer operating at human rate; adding a second configuration read or invalidation
path would buy only an imperceptible narrowing window. A future design with long-running batches
changes this invariant and must revisit the decision. Master-disable also releases feature
ownership leases on committed-configuration refresh as a fast backstop, but that is not the
policy-correctness mechanism. This bounded policy staleness does not relax live native
revalidation: game facts can change every frame without bound.

**Staleness probes.** Perf-debug capture QA may, for class-2 contracts, collect the cached
value, force the declared revalidation, and compare. Divergence is a build-behavior finding.
This is the detector for game updates that change caching behavior; a new game baseline
re-runs the probes as part of its audit.

## GameActions: the only way to mutate

Each game capability the suite can perform is defined **once** as a GameAction, and every
consumer — feature services, the Game MCP, tests — uses that one definition. A GameAction
declares:

- its **complete binding set**, resolved and validated at lifecycle scope before any use, so
  a resolution failure is impossible mid-transaction;
- its **preflights**, each with its freshness class;
- the **mutation** itself;
- its **expected evidence** — exact deltas, verified before/after;
- its **failure story** — what quarantines, what blocks, and what the health surface says
  when postconditions fail.

Mutation entry points, in strict order of preference:

1. **A data-layer composite** the game already owns: `UpgradeSO.Purchase`,
   `StructureSO.Purchase`, `SpellManager.FireSpellIndex`,
   `AlchemyInstanceListVariable.AddAlchemyInstances`/`RemoveAlchemyInstances`,
   `PlotNodeActionInstanceListVariable.AddInstance`.
2. **A disciplined re-drive** when the composite exists only inside a UI handler body.
   `SpellLevelNativeAdapter` is the reference implementation: every member prebound and
   validated before the first mutation is possible, the sequence wrapped in exact
   before/after verification, and irreversible steps (payments) preflighted hardest and
   taken last.
3. Invoking UI handler methods: **never.**

One evaluation round per world reading; any attempted round raises the world-gate floor.
This is engine-enforced (`ServiceCycleSlot`), not per-feature policy — a GameAction never
carries its own cooldown, retry timer, or candidate memory.

## Suite UI

- The suite owns every control and every pixel it presents. Live native UI objects are never
  cloned, repurposed, or listener-stripped into suite controls.
- Static assets — sprites, fonts, sliced frames — are borrowed through declared capture, like
  any other read. If capture fails, the control does not appear and health names the exact
  reason. There is no fallback rendering.
- Suite surfaces **speak the game's idiom**, not just its palette. If the native siblings of
  a surface are tabs, the suite's addition is a tab; if they are toggle buttons, it is a
  toggle. Interaction grammar is part of looking native.
- The Game MCP navigates suite surfaces exactly as it navigates native ones. A suite surface
  that requires MCP special-casing is wrongly built; fix the surface, not the MCP.

## Enforcement

- Every implementation lane cites this document; review findings reference the specific rule
  violated.
- A new capability is a new GameAction, or an explicitly justified exception recorded in the
  lane report — which then becomes doctrine, so the next feature reuses it instead of
  reinventing it.
- Bespoke per-feature variants of already-solved problems (binding, verification, freshness
  handling, asset capture) are findings, not style choices.
