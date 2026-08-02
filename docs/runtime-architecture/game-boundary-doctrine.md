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

### Perf-debug inspection exception

The Game MCP is a debugger, not an ordinary service or a production gameplay dependency. A
parameterized tooltip, inspected panel, active-screen catalog, fixed probe, or framebuffer cannot be
truthfully published as general world state. A perf-debug MCP operation may read that exact fact
directly provided the HTTP worker submits one immutable operation, Unity performs the read on its
main thread, the result is native-free and terminal, and no gameplay mutation occurs. UI state
changes such as navigation or opening an inspected tooltip are classified and audited as UI
mutations, not called read-only.

This exception does not permit speculative capture. There is no periodic MCP snapshot, cache warm,
hidden navigation, or “freshness” loop. Broadly reusable changing facts go into `GameWorldState`;
authored graphs are build-time or lifecycle-structural; genuinely parameterized UI facts are read
only for the request that names them. See
[Game MCP frame operations](game-mcp-frame-operations.md).

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

### Live entity identity catalog

`RuntimeIdentityRegistryBinding` is the one Common-owned binding for
`IdScriptableObject.RuntimeLookup` and `IdScriptableObject.GetGuid()`. Typed registry resolution and
the identity catalog share it; no feature, MCP surface, diagnostic, or log owns another registry or
UUID-to-name reader.

The first shared world capture after `RuntimeReady` in a Playing generation enumerates that registry
once on Unity's main thread. The pass records lifecycle generation and registry count before and
after enumeration, requires every value's `GetGuid()` to equal its dictionary key, and copies only
UUID, exact runtime type, `TooltipableObject.GetName()` when applicable, and the Unity asset name.
One entity's name accessor may fail without discarding other identity-correct rows. Any lifecycle or
count instability discards the entire candidate and retries on a later ordinary capture; an identity
contradiction or total binding failure publishes an empty unavailable catalog and reports one error
for that lifecycle. Names never block world publication, a feature, or a mutation.

The successful table is UUID-sorted and immutable. One snapshot reference is attached to every
`GameWorldState` in that lifecycle and placed in a Common latest-wins holder for non-world
presentation consumers; it is not rebuilt per publication and its rows are not copied into entity
categories. A lifecycle transition first replaces both views with an unbound empty snapshot, so no
Unity asset or name survives save/load, reset, NG+, or scene-generation replacement.

`EntityIdentityFormatter` is the sole rendering facade and never reflects or touches Unity. Its
total fallback ladder is live display name, live asset name, generated `KnownEntities` diagnostic
name before the live bind only, then bare canonical UUID. Rendered labels always retain the UUID.
After bind, a missing label reports one UUID-only warning per `(generation, UUID)` and does not fall
back to authored bootstrap metadata. MCP entity references, refusal receipts, feature diagnostics,
logs, and traces all use this same substrate; names remain diagnostics and never identity authority.

Portable and installed-contract gates prove this shape and its exact native members. Stability of
the live registry at `RuntimeReady` remains a supervised promotion check; this lane does not claim
live UAT and does not launch the game.

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
- The Game MCP owns transport only. All stateful tools cross its single post-pump frame-operation
  inbox; an empty inbox performs no projection, health read, native read, or authority refresh.

## Proposed ruling: verification protects the action, not the ledger

> **Pending maintainer ratification at landing.** This wording records the round-2 ruling for review;
> it is not presented as doctrine that predates that ratification.

A GameAction postcondition proves that the exact stable target and expected native type performed
the requested transition. Wrong target, wrong type, an absent transition, or an ambiguous outcome
fails verification and may quarantine the capability. Payment deltas, resource balances, counters,
timers, flags, and other accounting facts remain exact receipt evidence, but they never gate,
downgrade, or quarantine an otherwise observed target outcome. In particular, a sub-ULP native
`BigDouble` subtraction cannot disprove a transition the game performed.

After a native exception, capture the strongest available after-state before classifying it. If the
requested identity and outcome are present, commit verified and retain the exception plus all ledger
facts as evidence. If the requested outcome is absent or target identity is ambiguous after a
mutation began, fail closed and quarantine. Admission checks still prevent unaffordable or otherwise
ineligible attempts; this ruling changes postcondition meaning, not preflight authority.

## Proposed ruling: MCP responses are signal-dense

> **Pending maintainer ratification at landing.** This wording records the round-2 ruling for review;
> it is not presented as doctrine that predates that ratification.

An MCP success contains status, stable code, the requested data or outcome, and only facts needed for
the next decision. It omits request echoes, zero counters, empty collections, inapplicable predicate
stanzas, and matching expected/observed generation pairs. Absence means not applicable. There is one
honest response shape per tool and no response-verbosity option.

A refusal or fault keeps the evidence that explains it: exact reason, native call/mutation audit,
generation mismatch where present, and the decomposed action receipt. Brevity is a success posture,
not permission to hide uncertainty or failure evidence.

## Proposed round-5 clarification: the MCP is a player surface, not an audit envelope

> **Pending maintainer ratification at landing.** This clarification supersedes conflicting response
> examples in the preceding round-2 proposal without rewriting that historical ruling.

MCP reads use `available`/`unavailable`; mutations use `committed`/`refused`/`faulted`. A success has
no code that restates its status, no static mutation-scope label, no attempts/committed counters, no
request echo, and no payment or generation-mismatch stanza. Failure retains decomposed evidence.
Every entity reference carries stable UUID plus player-facing name, category/type when relevant, and
internal name only when it differs. Every game-domain magnitude is one rounded string: zero is `0`;
all nonzero values use a lowercase scientific exponent and at most two mantissa decimals.
Integral and large-number implementations of the same player concept therefore have one wire type;
protocol counters and identifiers remain integers. Native numeric sentinels are translated into
their domain semantics, normally field absence, rather than forwarded as plausible magnitudes.

Read tools retain one `worldGeneration` naming the immutable publication that answered. Action
schemas and results have no world generation: the GameAction revalidates live identity and mutable
facts, then the operation returns the newer published post-state that would otherwise require a
follow-up read. Resource and cost rows use one canonical spendable `amount`; deeper collector and
factor internals remain outside the player surface. Compact text is legitimate when it is faster for
an agent to read than structured data and no handle extraction is required.
