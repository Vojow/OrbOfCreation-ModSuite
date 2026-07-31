# Auto Scribe

> **Lifecycle: Live crafting verified; final reviewed build is installed.** The audited identity
> facade, shared world evidence, planner, Auto Items coordination, guarded one-shot mutation,
> configuration, diagnostics, and semantic role picker are implemented. The Mods rail now renders.
> Worker-definition and status-publication boundary violations are fixed. The latest retained run
> proves 164 verified native completions with no Auto Scribe faults. Source also fixes the
> rejected-work retry cadence, external-automation coverage classification, and adds the missing
> gameplay quick control. The complete branch review and automated compatibility gates are green;
> interactive progression and layout evidence remains open.

[Back to plans](README.md) | [Auto Items plan](auto-items.md) |
[Native evidence](../reverse-engineering/auto-scribe-native-pipeline.md)

## Progress checkpoint

Keep this section current whenever a phase, decision, blocker, or resume point changes. A later
session must be able to resume without reconstructing the design from conversation history.

Last updated: **2026-07-30**

| Phase | Status | Exit condition |
|---|---|---|
| 1. Product model and static native evidence | **Complete** | Every supported Scribe recipe's output Scroll, enchantment, level, and target-selection relationships are proven |
| 2. Current-build compatibility audit | **Complete** | `steam-windows-2026-07-29` accepts the installed assembly pair on `main` |
| 3. Shared world publication | **Complete** | Levelled Scroll stock, structure enchantments, exact Scribe recipes, target eligibility, and Scribe work publish atomically |
| 4. Read-only coverage planner | **Complete** | A native-free planner publishes production demand and Scroll-use directives by opaque role and level without emitting mutations |
| 5. Target-aware Auto Items integration | **Complete** | Auto Items submits a Scroll only when the shared plan and immediate native preflight both find a valid target for its strongest owned level |
| 6. Guarded bounded Scribe mutation | **Complete** | The feature submits at most one revalidated one-shot native Scribe craft at a time and verifies its queue or instant-stock postcondition |
| 7. Configuration and UX | **Complete** | Disabled-by-default controls and a semantic role picker avoid exposing or persisting UUIDs |
| 8. Lifecycle, diagnostics, and observability | **Complete** | Emergency stop, dependency loss, lifecycle replacement, quarantine, and coverage state have explicit diagnostics |
| 9. Automated verification | **Complete** | The portable gate constituents pass 1,994 ordinary and 90 profile tests plus the profiler-tool build; Release production line coverage was last measured at 74.53%; all 26 installed game-contract tests and the zero-warning real-reference Release build pass |
| 10. Interactive game validation | **Core behavior verified; hardening open** | Live evidence proves verified Scribe completions; install the bounded-retry/UI build, confirm cadence, then continue disposable-save lifecycle scenarios |
| 11. Documentation and cleanup | **Complete** | Behavior documentation and responsibility boundaries are current and the stacked PR is reviewable |
| 12. Gameplay quick control | **Implemented; interactive layout gate open** | Auto Scribe shares one committed mode with its Mods command, renders native configured-intent/health state, and occupies the new paired tray row with Auto Items |
| 13. Full test pyramid | **Automated layers complete; runtime gates open** | Direct guarded-adapter, coverage convergence, installed-reflection, retained-journal, and disposable-save layers have explicit ownership and acceptance criteria |
| 14. Carry-fill and live progression replanning | **Implemented; runtime validation open** | Locked roles remain dormant until visible, every fresh publication observes new roles and level ceilings, the native boundary advances to the highest affordable level, and stronger stock replaces weaker stock up to the native carry limit |
| 15. Per-recipe frontier and fair ceiling progression | **Corrected; runtime validation open** | Every facade role tracks its own created/queued frontier, selection rotates without starvation, and covered roles keep making bounded next-level affordability probes so native purchase can raise the ceiling after resource growth |

### Current resume point

- Worktree: `.analysis/worktrees/auto-scribe-plan`
- Branch: `agent/auto-scribe-plan`
- Stacked base: `agent/auto-items-plan` at `9f01300`
- Dependency: draft PR #99, which supplies lifecycle-safe Scroll consumption.
- Current task: validate the corrected per-recipe Scribe frontier and fair role rotation.
  Native decompilation confirms that `PurchaseQuantity(purchasedQuantity, previousQuantity)`
  raises `maxStartingLevel` to their sum and that completed output raises the Scroll's
  `maxCreatedLv`. Runtime evidence showed why those facts must remain distinct: Advancement's
  cheaper affordability frontier raised the shared ceiling to 67 and the old planner incorrectly
  imposed 67 on every recipe. Source now derives each role's target from its own `maxCreatedLv`,
  stronger stock, pending use, and queued work; rotates every enabled producible facade role; and
  keeps covered roles probing their own next level so the selected recipe can advance as soon as it
  becomes affordable. Active Scribe work suppresses duplicate progression probes.
- The post-install branch review consolidated semantic role parsing and production ordering,
  caches the immutable role selection by configuration generation, enforces lifecycle ownership
  in both production workers, and shares one fail-closed reflection exception policy across Auto
  Items and Auto Scribe. It also repairs the guarded installer's Git-for-Windows process and hash
  fallbacks discovered during installation.
- The reviewed tree passes 1,997 ordinary portable tests, 90 profiler tests, all 26 installed
  contracts, and the real-reference Release build with zero warnings and errors. Differential
  verification recognizes the exact installed assembly pair as the admitted
  `steam-windows-2026-07-29` baseline.
- Clean reviewed commit `5e108552a13b1d0e2f28af0910595b1537ef4859` is installed with
  SHA-256 `F40EE602B8FB462A290C12AA8D71BF9FBFF82290B56B24CF2037514E1EA80860`.
  The repaired guarded installer accepted Windows-style paths, reran the complete portable and
  installed-reference gates, copied all 12 active saves to
  `backups/pre-modsuite-install-20260730T135346Z`, and copied the prior DLL to
  `BepInEx/modsuite-backups/pre-modsuite-install-20260730T135346Z` before replacing it. No save was
  edited and no file was deleted.
- Implemented production roles: Advancement, Development, Echoing, Excellence, Learning, and
  Power. Investment and Speed remain coverage-only because the audited Scribe registry has no
  production recipe for them.
- Installed hashes observed on 2026-07-30:
  - `Assembly-CSharp.dll`: `436210E61D9F8B84658609D35E32BC274356170005AC15FE93FA36D4D9F7AA4C`
  - `Assembly-CSharp-firstpass.dll`: `D14D52652591ED3CB5ACF55186478DD3873F3C836871E0F68AA861D1767F480A`
- `origin/main` accepts this exact pair as baseline `steam-windows-2026-07-29` in
  `GameAssemblyAudit` and `data/native-contracts.json`. The branch adds and passes the
  feature-specific relationship and mutation contracts for that accepted baseline.
- Installed metadata contracts pass against this exact pair. Release commit
  `dd6b9895a35ca02a8b4c2291348000e41275c047` was installed on 2026-07-30 with SHA-256
  `92bd54c1ab16d90968b9c392c1c30a69dcf1ac7fb85f4665f966c4a9d10004d5`.
- Rollback checkpoint:
  - save copies: `backups/pre-modsuite-install-20260730T094146Z` (12 files);
  - previous DLL: `BepInEx/modsuite-backups/pre-modsuite-install-20260730T094146Z` (1 file).
- The first post-install launch loaded the DLL but stopped the suite before UI or gameplay
  mutation because the existing config carried future schema 7 while this branch supports schema
  5. The schema-7 file is preserved as
  `dev.vojow.orbofcreation.modsuite.cfg.pre-autoscribe-schema5-restore-20260730T115200Z.bak`.
  The known `pre-schema-v6` schema-5 backup is active, with the player's 10-second Auto Concept
  fallback and training values restored and `EnableButtonShell = true`.
- The configuration repair ran only after exact executable-path detection confirmed that the game
  was closed. No game save was edited, and the fail-closed launch attempted no ModSuite native
  mutation.
- The 2026-07-30 11:52 launch accepted schema 5 and loaded ModSuite, then exposed two independent
  code defects:
  - Auto Items worker state retained a `HashSet<Guid>` temporary allowlist, so structural safety
    rejected the state and disabled the Automata ServiceCycle host.
  - the native Mods rail rejected the new `Auto Items` page before rendering because it had no
    audited icon mapping; `Auto Scribe` would have failed for the same reason next.
- The fixes store sorted allowlist identities in `PublicationTable<Guid>` and map Auto Items and
  Auto Scribe to the already-audited native Alchemy and Scholar sprites. Direct regression tests
  cover both failures. The complete portable gate passes 1,939 ordinary tests and 90 profile tests;
  the profile build completes with zero warnings and zero errors.
- Corrected release commit `2b8a243bd14fd8668107a30bd9f83dd9d8605a29` was installed from a
  clean tree on 2026-07-30 with SHA-256
  `6d84df24938ecf98ebff424acfaa4419b1f0d60aa0cf65197aed7e1107958b69`.
  The installer reran the complete portable gate and all 26 installed game-contract tests, backed
  up 12 save files to `backups/pre-modsuite-install-20260730T100334Z`, and backed up the previous
  DLL to `BepInEx/modsuite-backups/pre-modsuite-install-20260730T100334Z`.
- The 2026-07-30 12:05 launch confirmed both consolidated Mods rail entries render, then the shared
  host failed closed before gameplay mutation because `AutoItemsWorkerDefinition` retained a
  cross-thread activation tracker containing a lock and `HashSet<Guid>`. The same complete
  separation audit also found the next latent failure: `AutoScribeWorker` retained a delegate and
  an array-backed identity profile.
- Source now reconciles verified temporary submissions through `PreviousReceipt` into
  lifecycle-scoped `AutoItemsCycleState`, including activation and exact-item quarantine state.
  Auto Scribe role identities use `PublicationTable<AutoScribeRoleDescriptor>`, and its worker no
  longer retains a main-thread dependency delegate. Regression tests run the same complete
  production-worker separation validator used by shared-host registration for both services.
- The post-fix current tree passes 1,942 ordinary portable tests, 90 profile tests, all 26
  installed-game contracts, and the installed-reference Release build with zero warnings and
  errors. Clean commit `0c10a1cffa27bbf49642295b7c295ce82c17d759` is installed with
  SHA-256 `4c3e72992f9ebc16483495ff386063092e9dc500a9be429c8f7fe7e912299cdc`;
  the installed and built hashes match.
- The installer created the latest rollback checkpoint after confirming the game was closed:
  - save copies: `backups/pre-modsuite-install-20260730T101946Z` (12 files);
  - previous DLL: `BepInEx/modsuite-backups/pre-modsuite-install-20260730T101946Z` (1 file).
- The 2026-07-30 12:21 launch proves the shared host registers and world collection completes
  (4,168 entities across 46 categories). The user-triggered 2.3 MiB dump and `Player.log` identify
  the next blocker exactly: `AutoItemsFeatureStatusProjector` emitted
  `Operational`/`None` with a non-empty reason summary, while lifecycle publication constructed a
  `FeatureStatusReason` unconditionally. The resulting `ArgumentException` aborted every Automata
  update before readiness could propagate, leaving Auto Items waiting and Auto Scribe correctly
  blocked on its parent. Source now emits an empty operational reason and makes lifecycle
  publication consistent with the existing non-lifecycle status path.
- The current post-fix tree passes the 1,944-test portable gate, 90 profile tests, all 26
  installed-game contracts, and the installed-reference Release build with zero warnings and
  errors.
- Clean commit `d946629c499e6b3ce177e899611b6b114c978ce8` is installed with SHA-256
  `42c4177dcf73a856319fc7dd628244822c9522bf001e2e3e1c182408c3240c24`;
  the installed and built hashes match. The installer created:
  - save copies: `backups/pre-modsuite-install-20260730T103147Z` (12 files);
  - previous DLL: `BepInEx/modsuite-backups/pre-modsuite-install-20260730T103147Z` (1 file).
- The 2026-07-30 12:33 launch confirms the shared host registers, collection completes with 4,181
  entities across 46 categories, and lifecycle readiness propagates. The user-triggered trace then
  records two Auto Items actions faulting and 435 Auto Scribe boundary rejections in 15.6 seconds.
- Auto Items' native adapter returned the expected `TargetUnavailable` preflight, but its action
  mapper omitted that result and converted it to `AdapterFault`. The mapper now preserves it as an
  expected rejection.
- Both planners returned `WakePolicy.Immediate` after emitting work. A transient native rejection
  therefore created a tight proposal loop. Planned work now uses each feature's configured
  `AfterDecision` cadence; focused regressions pin that policy for Auto Items and Auto Scribe.
- The newest retained run `08deee25f4049578` proves Auto Scribe's guarded mutation path works:
  service 4 completed 164 actions, with 164 committed mutations, 492 native calls, and zero
  faulted observations. Its 60,423 rejected decisions also confirm that the installed pre-fix DLL
  was reevaluating rejected work far too aggressively; the source cadence fix remains necessary.
- Auto Items and Auto Scribe now have gameplay quick controls backed by the same committed
  configuration store as their Mods commands. They form one new two-button row above the existing
  Mentor/Harvest, Concept/Cast, and Buy/STOP rows, reuse the audited Alchemy and Scholar icons,
  expose runtime health in native tooltips, and participate in emergency-stop resume preview and
  quick-strip diagnostics.
- The feature-owned [Auto Items and Auto Scribe test pyramid](../testing/automata/auto-items-scribe.md)
  now covers policy, exact native boundaries, service coordination, installed reflection,
  retained-journal acceptance, and disposable-save behavior. Direct Auto Scribe adapter tests pin
  lifecycle, ownership, dependency, queue, live-race, postcondition, and quarantine behavior.
- Those tests exposed and fixed a coordination defect: automatic Scribe work was included in
  ordinary queued supply, making `ExternallyProducing` unreachable. Manual one-shot work now
  reserves supply; unexpired player-owned automatic work is reported separately and suppresses
  competing suite production.
- Current-tree verification passes the portable gate's three constituent commands: 1,994 ordinary
  tests, 90 profiler tests, and the profiler trace-tool build. Release production line coverage
  was last measured at 74.53% against the 73.40% floor. All 26 installed-game contracts, including
  the newly declared carry-cap and Scribe-level members, and the zero-warning real-reference
  Release build also pass.

## Goal

Fully automate preparing the strongest currently affordable Scribe Scrolls for every structure
that the game considers a valid target, then retain stronger reserve stock up to each Scroll's
native carry cap so later levels can replace weaker copies.

The completed loop is:

```text
native progression unlocks a higher Scribe level or another structure
  -> Auto Scribe observes exact coverage and target-level stock
  -> the shared coverage plan publishes production demand and Scroll-use directives
  -> Auto Scribe submits one missing Scroll to the native Scribe queue
  -> the native one-shot Scribe action creates the Scroll
  -> Auto Items submits the strongest Scroll through native random targeting
  -> the native targeter upgrades the most deficient valid structure
  -> fresh world publication reduces the remaining deficit
  -> Auto Scribe stops queueing when supply covers every deficit
```

Auto Scribe prepares Scrolls. Auto Items consumes them. Neither service writes an enchantment,
chooses a target structure, changes toxicity, edits a save, or reproduces an effect directly.

## Product model

The screenshot's Advancement, Power, and Learning rows are a visible progression slice, not the
catalog boundary. The checked-in identity extraction contains at least eight Scroll-named
`ConsumableSO`/`EnchantmentSO` pairs and six Scroll-named `CraftingRecipeSO` candidates:

| Candidate role | Recipe UUID candidate | Scroll UUID | Enchantment UUID |
|---|---|---|---|
| Advancement | `a4a02a8f-6573-411c-a30c-6d9bcee12605` | `5f6aa08d-7da6-4c7a-89c9-aabcfe48e886` | `0796ee25-e1f6-4c5c-abba-aad46e02318b` |
| Development | `b15690ab-828c-42b9-ad69-70f169a45961` | `09d6101a-460d-4ce9-b7d4-46c4abaeadb7` | `cb354ece-fd8c-4ffc-a67e-b24cc3fe5fa5` |
| Echoing | `008ccaa9-da26-4b55-95a5-5bc5df9c62f0` | `164dbfa9-8b9f-4976-9d17-ad3ad6b07a62` | `d854b177-865f-45ee-97a3-23d904df1ba1` |
| Excellence | `6c5c36ea-4736-46d2-b961-6227d4cce5d3` | `49057abe-fe54-481e-99bc-2b82c3995c6b` | `7b17670e-b3b6-401f-83f7-9c0e6d157852` |
| Investment | None identified | `da5eab6d-ab4c-4b32-aca1-2e83b6d3a64b` | `f75cea6e-5d21-439f-bce4-79199b22434d` |
| Learning | `49da8d21-0f6a-492e-bd9a-15531b1737d5` | `ec14ee5d-66a3-4b28-a271-25dca2414387` | `b74c2058-4113-4b6c-b11e-1c97304d236c` |
| Power | `9c0a2b96-45fa-4aca-83ba-8efad8895608` | `4bb8af50-fc7d-44a7-b1fc-937c390f8aec` | `b9d5f0f7-43fd-4bad-a8e2-8a73f2f1d1d6` |
| Speed | None identified | `b2232a7d-5c97-44c9-9520-686e99fa8293` | `9f068bad-f3a0-47de-84f4-407e67622fe1` |

These rows originally exposed the discovery scope. The implemented baseline profile accepts them
only together with exact expected types, and the world reader revalidates each recipe/output/target
graph from native references. Names remain diagnostics and never authorize automation.

Threads are a separate `ConsumableTypeSO` family handled by Auto Items' exact-item temporary
policy. They are not Scroll roles, Scribe recipe outputs, or Auto Scribe coverage inputs.

The serialized `ScribeCraftingRecipes` registry contains exactly six entries, resolving to
`CraftScrollAdvancement`, `CraftScrollDevelopment`, `CraftScrollEcho`,
`CraftScrollExcellence`, `CraftScrollLearning`, and `CraftScrollPower`. Runtime publication now
requires an exact output Scroll, one exact request-target script, one matching enchant script, and
one unambiguous structure target graph before the role becomes usable.

The production catalog is reconstructed from accepted native relationships on every lifecycle:

1. enumerate recipes registered to the exact Scribe recipe type;
2. require an audited levelled-recipe shape;
3. require exactly one output `ConsumableSO` belonging to the exact Scroll family;
4. require an audited structure-target graph with an unambiguous enchantment identity;
5. publish the resulting stable recipe/Scroll/enchantment role.

An owned Scroll with no accepted Scribe recipe remains eligible for target-aware Auto Items use, but
Auto Scribe reports it as coverage-only and does not invent a production path. A role contributes
to Auto Scribe's completion goal only when its exact native Scribe recipe is proven.

The exact Scribe recipe type is `ee001474-8209-4238-9566-84899a877226`
(`CraftingRecipeTypeSO`, `ScribeCrafting`). Its native `maxStartingLevel` is the shared progression
frontier and manual selector cap, not every recipe's coverage target. Each role instead uses its
own Scroll's proven created/queued frontier. The player's selected `startingLevel` is a UI
preference and is not changed by the feature.

For one visible Scroll role at its own frontier `L`:

```text
coverage demand = native-valid structures with no matching enchantment at level >= L
stock target = native per-item carry limit, when finite
desired supply = max(coverage demand, stock target)
supply = owned Scroll counts at level >= L + matching queued crafts and pending Scroll uses
deficit = max(desired supply - supply, 0)
```

One bounded craft is eligible only while `deficit > 0`. At the native boundary the live unlocked
ceiling is re-read and used as the start of a bounded monotonic affordability probe. If a higher
level is affordable, the one-shot purchase queues that level and native `PurchaseQuantity` raises
`maxStartingLevel`; otherwise production falls back to the highest affordable useful level. The
game replaces weaker carried Scrolls when a stronger result arrives at the carry limit. A fresh
snapshot recomputes demand after each craft, use, recipe unlock, structure unlock, level unlock,
save/load, reset, or NG+ transition.

## Identity facade

UUIDs remain mandatory at the native boundary: the suite invariant is stable UUID plus expected
native type. They are not part of Auto Scribe policy, configuration, or normal UX.

An `IAutoScribeIdentityCatalog` facade selects an `AutoScribeIdentityProfile` by the accepted
`GameAssemblyBaseline.Id`. A profile is the single feature-owned mapping from semantic,
version-stable `ScrollRoleKey` values to exact recipe, Scroll, enchantment, recipe-type, list, and
capacity identities:

```text
ScrollRoleKey ("scribe.advancement")
  -> baseline identity profile
  -> recipe UUID + CraftingRecipeSO
  -> Scroll UUID + ConsumableSO
  -> enchantment UUID + EnchantmentSO
```

The collector and action boundary resolve and type-check native identities. The worker sees only
opaque role keys and native-free descriptors. A future game update that replaces UUIDs therefore
changes one audited baseline profile and its contracts, not the evaluator, scheduler,
configuration, or Auto Items integration. An absent, ambiguous, or mismatched profile fails closed.

## Locked decisions

1. **Separate stacked feature.** Auto Scribe is an ordinary ServiceCycle service and a separate
   reviewable PR stacked on Auto Items. It does not enlarge Auto Items into a crafting service.
2. **Advance the native Scribe ceiling without sharing recipe targets.** The exact native
   `ScribeCrafting.maxStartingLevel` is a progression frontier, not a production cap or a coverage
   requirement for recipes with different costs. A needed craft probes the selected recipe above
   it and queues that recipe's highest affordable level. Native `PurchaseQuantity` then raises the
   shared ceiling. If no frontier advance is affordable, the boundary falls back to the highest
   affordable useful level. The player's selected starting level is not a policy input.
   A covered role continues requesting its own next level, so new affordability is discovered
   without borrowing another recipe's frontier. Active Scribe work suppresses duplicate requests.
3. **Complete native catalog, not a screen list.** Every audited recipe registered to the exact
   Scribe type is considered. A role is supported only when its recipe, Scroll family, structure
   target, and enchantment relationship form one accepted graph. Names are diagnostics only.
4. **Native-valid coverage.** "All structures" means every structure the exact Scroll target
   selection accepts at the target scaling. Visibility alone is not enough.
5. **Higher replaces lower.** A structure is covered when the matching enchantment level is at
   least the target. Lower enchantments create one unit of demand; they are not removed directly.
6. **Native carry capacity bounds production.** Same-or-higher owned counts and in-flight work
   reserve one deficit each. Lower-level inventory does not satisfy a higher-level stock target and
   is replaced through the game's own carry-limit behavior; finite native capacity prevents an
   unbounded stockpile.
7. **Replaceable identity facade.** Policy and persisted selections use `ScrollRoleKey`; exact UUID
   plus expected-type tuples live only in an audited, baseline-specific identity profile.
8. **Auto Items remains authoritative for use.** Scrolls are consumed only through the existing
   guarded native-random path. Auto Scribe never invokes `ConsumableSO.SelectAndFire()`.
9. **Shared target-aware admission.** The read-only coverage planner publishes a per-role,
   per-level `ScrollUseDirective`. Auto Items blocks the strongest owned Scroll when the directive
   says there are zero candidates, unblocks it on a later generation with candidates, and fails
   closed on unknown evidence. Immediate native target preflight remains authoritative.
10. **No disposal policy.** The suite never discards lower or surplus Scrolls. They remain player
    inventory and are ignored when no native upgrade target exists.
11. **Bounded autonomous play.** `AutoScribe.Active` authorizes one-shot native Scribe queue
    submissions, not ownership of persistent native Auto Scribe instances. Pre-existing manual and
    automatic work remains game-authoritative capacity and supply pressure and is never edited.
12. **Fail closed on dependency loss.** Auto Scribe pauses production when Auto Items Scroll use is
    disabled, unhealthy, lifecycle-retired, or lacks action-family ownership. It does not build an
    unbounded stockpile while consumption is unavailable.
13. **Disabled by default.** The feature requires explicit activation and exposes why a role is
    producing, covered, queued, waiting, externally automated, or blocked.

## Implemented rulings

1. **Deterministic fair selection.** The worker rotates enabled visible deficits by the semantic
   rank stored in the baseline identity facade, wrapping after the last role. Covered roles with no
   active Scribe work join the rotation as next-level affordability probes. Disabled, locked,
   externally produced, or already queued roles are skipped, and a rejected or continuously
   cheaper recipe cannot starve Development, Echoing, or another audited future facade role. UUIDs
   and display names never decide order.
2. **External automation accounting.** Active one-shot work reserves supply. Matching automatic
   work suppresses competing production and is reported as external production; Auto Scribe never
   creates or edits the player's persistent automatic entries.
3. **Level-aware in-flight evidence.** Owned counts, pending uses, active work, and automatic work
   are all counted only at the requested level or higher.
4. **Canonical one-shot action.** The main-thread adapter revalidates lifecycle, ownership,
   identity, capacity, targets, supply, visibility, affordability, and then uses the audited
   `CraftingInstance(recipe, level)` path. It verifies either one queued entry or one same-or-higher
   stock unit for instant craft and quarantines ambiguous mutations.
5. **Bounded wake policy.** The worker uses the configured 0.25–10 second evaluation interval,
   waking immediately only after emitting one useful action. Shared world invalidation and
   lifecycle replacement provide earlier safe refreshes without an unbounded background scan.

## Required publication

Extend the shared world collector rather than introducing a feature-owned scan. Publish only
native-free facts needed by policy:

- exact Scribe recipe identity, visibility, recipe type, level behavior, and target level;
- exact recipe-to-output Scroll identity;
- Scroll inventory counts by level and pending-use level;
- structure identity, visibility, and matching enchantment identity plus level;
- native target eligibility for each exact role at the current target scaling;
- native active Scribe queue capacity and each queued instance's role, level, and lifecycle;
- native Auto Scribe capacity and live automatic instances as external production pressure;
- completeness evidence naming every unavailable contract.

The worker receives one immutable world generation. It must not retain Unity objects, walk
serialized effect graphs, call target selectors, or infer relationships from names.

## Auto Scribe and Auto Items coordination

The background-safe `ScrollCoveragePlanner` produces one immutable `ScrollCoveragePlan` from the
same world generation used by both services. For every role and relevant level it publishes:

- valid native target count and uncovered deficit;
- covered, production-needed, or evidence-unknown state;
- `AllowUse`, `BlockNoCandidate`, or `BlockUnknown` for Auto Items;
- whether one bounded Scribe craft is currently useful.

This is the communication contract between the features. Its read-only planning slice remains
available even when Auto Scribe's master switch is disabled, so Auto Items can safely consume an
already-owned Scroll without requiring Auto Scribe production. Auto Scribe consumes only the
production directive; Auto Items consumes only the use directive. Neither service mutates the
other's configuration.

A pinned plan can become stale between evaluation and execution. Stale permission is closed by
re-running the exact native target check immediately before `SelectAndFire`; a stale block merely
delays use until the next world generation. The shared plan can tighten Auto Items admission but
can never override its lifecycle, toxicity, inventory, action-family, or native preflight gates.

## Native boundary

Every one-shot queue submission must run on the Unity main thread and revalidate:

- feature configuration, emergency stop, lifecycle, dependency health, and action-family ownership;
- exact recipe, recipe type, output Scroll, target-level identity, and current world generation;
- active Scribe queue room, native availability, computed cost, and required resources;
- current demand, same-or-higher stock, and in-flight work;
- player-owned automatic production absence for the affected role;
- exact active-list postcondition after adding one native craft.

An unknown type, overload, list, slot count, relationship, or postcondition rejects the action. An
ambiguous attempted mutation quarantines Auto Scribe for the lifecycle.

## Configuration and UX

The implemented surface is intentionally small:

- `AutoScribe.Mode`: `Disabled` by default, or `Active`;
- `AutoScribe.Roles`: a discovered-role picker persisted as stable `ScrollRoleKey` values, with all
  audited producible roles enabled by default behind the master switch;
- `AutoScribe.EvaluationIntervalSeconds`: bounded planning cadence.

The Mods page presents the roles as named checkboxes with All, None, and Default controls. Runtime
feature health explains dependency and safety states such as Auto Items Scrolls disabled, target
evidence unavailable, identity profile unavailable, lifecycle retirement, action-family conflict,
or mutation quarantine. Service diagnostics retain aggregate deficient, covered,
externally-producing, evidence-unknown, planned-action, and target-level projections for deeper
inspection. Exact UUIDs remain diagnostic native authority but are not persisted policy or normal
user-facing labels.

## Remaining delivery

1. Keep the complete portable and installed-contract gates green on every behavior change.
2. With explicit user authorization, install the reviewed build, capture the P4 journal criteria,
   and perform the P5 disposable-save scenarios below.
3. Record observed native behavior, fix any contract disagreement, and only then move the feature
   from draft/validation status toward release.

## Interactive validation outline

The final UAT must include:

- zero, one, and several active Scribe queue slots with multiple deficient producible roles;
- empty coverage, mixed lower levels, complete coverage, and a newly unlocked target level;
- a structure becoming visible while active;
- stock already covering demand and surplus lower-level stock;
- a player-owned Scribe automation before activation and manual queue work while active;
- Auto Items disabled, unhealthy, blocked by ownership, and re-enabled;
- toxicity wait while Scroll supply exists;
- emergency stop during production and immediately before a proposed mutation;
- save/load, reset, NG+, scene replacement, shutdown, and restart;
- no valid Scroll targets, canceled native targeting, and an observed postcondition mismatch.

No release claim is permitted until the native UI visibly agrees with published coverage, queued
work, and Scroll-use blocks across those boundaries.
