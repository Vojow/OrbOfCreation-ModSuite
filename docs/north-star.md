# The North Star

Read this first. It is the goal every change in this repository serves; when a change seems
to conflict with it, either the change is wrong or this document is. Say which — don't
quietly split the difference.

## The system in one paragraph

This suite is a background brain for Orb of Creation. The main thread does three small jobs:
read raw numbers out of the game, swap published references, and apply decided actions back
into the game. Everything else — all math, all policy — happens on worker threads against
immutable snapshots. Stale data is accepted by design: a decision made against a 200 ms old
world is a good decision, because the game barely moves frame to frame and the payoff is a
main thread we barely touch.

## The dataflow

1. **Collect** (main thread): the world collector reads raw values out of the game and stamps a
   generation. Almost all of it is a plain field read. The one derived computation on the main
   thread is deliberate: a modifier record is read the way the game's own `GetValue()` reads it —
   its memo while it is clean, its fold over base value and modifiers while it is dirty — because
   the alternative is publishing a number the game will not act on. Nothing is written, and the
   collect it sits inside is measured rather than assumed — the trace dashboard is the instrument —
   so it is a cost we accept knowingly.
2. **Derive** (worker): compute everything we would otherwise have to ask the game for
   (costs, rates, availability) into a new immutable `GameWorldState`.
3. **Publish** (action): the finished snapshot returns through the ordinary action pipeline;
   applying it swaps the current-world reference and advances the live generation.
4. **Consume**: an ordinary service starts only against a world collected strictly after it went
   live, and strictly after the frame of its own last committed change to the game. Generations
   and frames share one clock — a snapshot's generation is the pump frame it was collected on —
   so the comparison is like with like. The gate is born armed: acting on the seed publication
   means acting on an empty world where nothing is priced, so a service waits for a reading
   later than itself before its first cycle. After that, the shape of what a service commits
   raises the floor, not a declaration: only a committed native mutation does. A publication
   changed no game state; a rejected or skipped action left the world exactly as the snapshot
   described it; neither raises it. A service therefore never acts on a world it does not appear
   in, never acts twice on one generation, and never acts on a world collected before its own
   last change landed. A held service is simply skipped and asked again next frame, and every
   held frame is recorded. The worker receives `(world, config, strategy)` as arguments and
   returns actions; the main thread applies them.

Configuration is the same shape with a different source (the config file); strategy is the
same shape with a neutral constant bulletin until a strategist exists.

## The three publications

World, configuration, strategy. Each is:

- **one** suite-wide slot with **one** suite-wide generation — never per-service;
- deeply immutable (machine-checked), shared across threads without locks;
- replaced only by the main thread; old generations die by GC when the last worker drops
  them. Holding a stale snapshot is a memory cost, not a correctness bug.

Every service is handed all three and ignores what it doesn't need.

## Two service shapes — there is no third

- **Source**: captures raw values on the main thread, derives on the worker, publishes the
  result as an action. The world collector is one; a future strategist is another.
- **Ordinary**: no capture phase at all. Generation gate, then worker
  `Evaluate(world, config, strategy, ref state) → actions`, then main-thread execution.

## Where the game may be touched

Two places for a migrated service — a source service's capture, and action application — plus
the one declared exception below, plus the declared legacy surface of the services nobody has
migrated yet. The third bucket is real and sizeable: 96 of the manifest's 772 contracts are marked
`legacy`, against 627 capture, 45 action and 4 patch. It is not a permitted place so much as a
ratcheted debt, and it retires service by service as each migration lands. No worker, no
evaluator, no policy code touches the game anywhere. This is enforced, not hoped for:

- the **native-contract manifest** is a closed audit of the game surface: every game member a
  source file names must be a declared, audited contract, and each contract declares the place
  it is touched — capture, action, patch, or legacy — with the legacy set ratcheted so it can
  only shrink;
- the **worker-storage audit** ensures worker-thread code can only reach inert types —
  value types and sealed, deeply immutable objects on an explicit allowlist. The ban is on
  holding a *path back to the game* (delegates, interfaces, adapters, mutable shared
  objects): whatever a worker holds, worker code can call, and touching Unity off the main
  thread is a data race. Immutable snapshots are inert and fine to hold.

One declared exception: the **differential verifiers** — read-only, off-by-default,
main-thread diagnostics that prove our ported math against the live game. They exist
precisely because we own formulas the game also computes; they are an audited exemption,
not a third service shape. A verifier's before/after probe belongs to the exemption too:
`Spell.Fire` is patched so a mutation verifier can tell a fire that happened from one that
did not.

Not every Harmony patch is scheduled to die, and the honest accounting is three groups.
The **signal patches** die with the generation gate: the queue pair is already gone, and
the completion postfix survives only as an imperative nudge to unmigrated Spell Leveling.
The **five lifecycle hooks** die when the snapshot's collected epoch is proven to detect a
save-load; until that is measured they are the intended "we are moving to a new game,
trash everything" signal rather than a wart. The **mastery, concept, and fire hooks** are verifier
probes and inputs to services nobody has migrated yet, and they retire with those services.

## The simplicity doctrine

Every seam must pay rent. Scheduled for deletion because they don't:

- the assembly split — everything merges into one DLL; features ship together and users
  enable or disable services in the config UI;
- per-service configuration publishers and per-service `ConfigGeneration`;
- the `TConfig` and `TFrame` type parameters — configuration arrives as an argument exactly
  like the world, and scratch space lives in worker state. Both are gone; the contract is
  `<TState, TAction>`;
- the capture phase and its adapters for ordinary services. Also gone: an ordinary service
  evaluates straight off the publications its cycle pinned, and only the world collector
  still captures;
- the `Bind*` registration ceremony — registration is a list of services, not a negotiation;
- the three plugin identities — one `BaseUnityPlugin`, one GUID, one config file. A clean
  break, taken deliberately while no release is pending; the config itself gets reimagined
  as part of the strategist work;
- every reference to OrbChronomancer and OrbAchievementResonance. They exist only on
  abandoned branches; with one DLL built from one tree, "not in the source" *is* the
  exclusion, and the name-based denylists retire with the split;
- the tree converges on one mod with a folder per feature — but files move only after the
  native-contract manifest stops naming hand-maintained source paths, never as a side
  effect of the merge commit itself. `sources[]` has been dropped, so that precondition is
  met;
- the CPU-budget / resumable-work-across-frames regime. A migrated service does minimal
  main-thread work per frame by design and needs no budget or slicing drama. The
  `CpuBudgetMilliseconds` knob and its coordinator machinery survive only to serve the
  not-yet-migrated services, and retire with them.

No compatibility concern may block a simplification: replay formats bump freely (recordings
are disposable test artifacts), upstream's project layout is not a constraint, and an
existing seam is never its own justification.

## Observability — four systems, four mandates

What each system is **for**. Code and older docs that claim otherwise are wrong; this
section wins.

1. **Profiler** — debug builds only, compiles out of release entirely (zero overhead).
   OpenTelemetry-style measured spans with globally enumerated integer span ids: every
   pump-frame phase, every worker stage. Worker-stage spans are deferred: a worker
   definition may not hold runtime-owned storage, so it cannot hold the probe, and the
   runtime times the whole evaluation instead — the stage ids that tried are burned.
   Pre-allocation is welcome here as a performance detail. Reported through the dashboard
   script. This is internal "how fast is this part" tooling.
2. **Full trace** — the bug-report recorder, available in release builds: a user presses
   record, reproduces the problem, shares the artifact, and the trace answers "what did the
   service see and decide". It records the four streams — raw capture data, configuration
   publications, strategy publications, action outcomes — plus the high-level runtime
   events needed to follow a session. Near-zero cost while idle; while recording, extra
   cost and allocation are acceptable. In debug builds, where it may run alongside the
   profiler, its own overhead must never appear inside profiler spans as a red herring.
3. **Decision log** — always on, high signal, low noise: lifecycle boundaries, strategy
   changes, configuration saves, emergency stops, service health transitions — not
   per-cycle minutiae. Size-capped and rotated; the suite (BepInEx's own logs included)
   keeps at most ~100 MB on disk no matter how many days it runs unattended.
4. **Replay** — retired as a runtime system. Scripted re-execution of recorded runs
   (clock scripting, causal joining, derived-state feedback) is deleted, not rebuilt. Its
   testing value is served by hand-crafted scenario fixtures; the full trace keeps the
   door open by recording exactly the inputs a future recompute harness would need,
   without the runtime carrying a line of machinery for it.

Fixed-size frames and ring buffers were a regime designed for per-service input shapes.
With every service taking the same three inputs, pre-allocation survives only where it
pays (profiler spans) — it is not a system-wide constraint.

## Sequence

1. this document;
2. aggressive seam cleanup, including the one-DLL merge;
3. game reads reduced to the two allowed places;
4. one reviewed, squashed commit to the public fork;
5. migrate the remaining services onto the runtime;
6. build out strategy and new features.
