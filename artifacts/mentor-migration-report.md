# Mentor ServiceCycle migration report

## Phase 0 — survey and execution plan

Surveyed at mainline `bc1a4d6` on branch `shift/mentor-cycle`. The working tree was
clean. The native-contract manifest started at 660 capture, 70 action, 32 legacy,
and 5 patch contracts.

### What Mentor does today

Mentor observes native mastery-XP gains and awards a configured percentage of that
XP to lower-mastery recipients without subtracting from the source.

- Spells are always available as a domain once native mastery and spellbook
  progression are unlocked. `EquippedSpells` lets each equipped spell share with
  discovered spells below that source's mastery. `HighestDiscovered` accepts only
  a source at the discovered catalog's highest mastery and shares with every
  discovered spell below it.
- Artifacts are opt-in. XP is observed only while an equipped artifact runs its
  native `IncrementActive` path. A source must be at the created catalog's highest
  mastery; recipients are created artifacts below that mastery.
- Alchemy is opt-in. A source must be an ordinary, available alchemy recipe at the
  ordinary catalog's highest mastery; recipients are ordinary available recipes
  below it. Scholar Concepts are excluded by exact recipe/type identity and the
  native `ConceptRecipes` membership.
- `SharedPool` divides the configured percentage once across all recipients.
  `PerRecipient` gives the configured percentage to every recipient, so total
  bonus XP scales with recipient count.
- Every planned grant carries an exclusive source-mastery ceiling. The action
  boundary re-resolves the recipient, checks discovery/creation, identity,
  ordinary-alchemy classification, action-family ownership, and
  `recipient mastery < source ceiling`, then uses the domain's native mutation
  path and proves its postcondition.

The legacy engine also owns catalog reconciliation, relationship-evidence chains,
capture/unroutable/parked ledgers, per-frame cooperative slicing, its own failure
registry and feature-status projection, direct `ConfigEntry` reads, a second
gameplay-invalidation pump, and the Alt+M quick button.

### Native surface and existing world coverage

The current engine reads or writes these gameplay categories:

- All domains: exact UUID and registry identity, mastery level, saved mastery XP,
  and discovery/creation state.
- Spells: `SpellRecipeSO.GainMasteryExp`; the equipped loadout through
  `SpellManager.instance.activeSpells`; spell discovery/progression/reset signals.
- Artifacts: `EquipmentSO.IncrementActive`,
  `EquipmentSO.GetExperienceElement`, the associated
  `ExperienceContainer.GainExperience`, clone/level/residual-XP accessors,
  `EquipmentSO.GainMasteryLevels`, saved `masteryXp`, and
  create/discover/level/reset signals.
- Alchemy: `AlchemyRecipeSO.GainMasteryXp`, recipe type and concept-registry
  classification, and discover/apply-mastery/reset signals.
- Progression unlocks: `ViewSO.IsAvailable` for `MasteriesEnabled` plus the
  spellbook, artifact workshop, or alchemy screen.
- Diagnostics: native display names.

The published world already carries spell discovery, mastery XP and level;
equipment creation, mastery XP and level, plus equipped level; alchemy discovery,
mastery XP and level; equipped spell-slot recipe identities; Concept recipe
membership; every view identity; and the collected lifecycle epoch. Two small
facts are missing for Mentor decisions: the composed `ViewSO.IsAvailable` answer
and each alchemy recipe's core type identity. Exact earned-XP deltas are not
derivable from successive saved-XP snapshots because mastery rollover and Mentor's
own grants can change those values. They must remain deliberate patch inputs.

### Target shape

Mentor fits the ordinary service shape. It does not capture the game:

1. The remaining mastery hooks append bounded, sequence-stamped, value-only XP
   observations on the Unity main thread. The world source publishes those
   observations beside the authoritative recipe/equipment/view facts.
2. The worker consumes each observation once, selects recipients entirely from
   the pinned world, applies the configured economy policy from the immutable
   suite configuration, consolidates grants by recipient and source-mastery
   ceiling, and publishes typed actions plus a semantic state projection.
3. The main-thread action adapter re-resolves one exact recipient against the
   current lifecycle, rechecks ownership and native eligibility, performs the
   native grant, suppresses its own observation callback, and verifies the exact
   native transition. The fixed ServiceCycle dispatch policy replaces both
   operations-per-frame and CPU-time budgeting.

Worker-side decisions are domain enablement, unlock interpretation from published
views, ordinary-versus-Concept classification, source-policy qualification,
recipient selection/order, economy arithmetic, event de-duplication, grant
consolidation, and status metrics. Boundary-only facts are current native
identity/type, current recipient eligibility and mastery ceiling, current
action-family ownership, lifecycle coherence, artifact container behavior, and
mutation postconditions.

### Harmony plan

These are deliberate patch-place inputs and remain:

- `SpellRecipeSO.GainMasteryExp` postfix: exact spell XP input.
- `AlchemyRecipeSO.GainMasteryXp` postfix: exact ordinary-alchemy XP input.
- `EquipmentSO.IncrementActive` prefix/finalizer together with
  `ExperienceContainer.GainExperience` prefix: associate the exact XP earned
  during one successful equipped-artifact tick.

The discovery, purchase/apply-mastery, create/level, reset, and spell-loadout
hooks retire. Their only job is to invalidate Mentor-owned catalogs or loadout
state, and the next world generation already publishes those facts. The five
suite lifecycle hooks remain Automata-owned under W55/W57. `SpellFirePatch`
remains Auto Cast's declared verifier probe.

### Configuration, status, and controls

Mentor settings join `SuiteRuntimeConfiguration`; the BepInEx reader is the only
place that copies live entries. Evaluator, policy, status, diagnostics, and action
code receive immutable values only. `Performance/OperationsPerFrame` and
`Performance/CpuBudgetMilliseconds` are removed with a schema 3-to-4 migration
that discards those obsolete keys. Alt+M remains a main-thread control that
changes the bound mode and publishes the next suite configuration. The quick
button and tooltip read the same dual-axis service-cycle status path as the other
migrated services.

### Contract retirement plan

Contracts used by world collection move to capture; mutation and verification
members move to action; the four retained hook targets move to patch. Contracts
used only by retired invalidation hooks or legacy catalog/diagnostic reads are
deleted. In particular:

- `alchemy-recipe.apply-mastery` and `.discover` are deleted with their hooks.
- `alchemy-recipe.is-discovered` is deleted in favor of the published
  `discovered` field.
- `abstract-list.value` becomes capture-only because world collection remains its
  actual reader.
- `spell-manager.active-spells` becomes capture and
  `spell-manager.instance` becomes action for their surviving Auto Cast and Spell
  Leveling uses.

Contract-place edits and the two-way legacy allowlist ratchet land together.
Manifest schema version 3, assembly baselines, and the trace wire formats do not
change.

### Performance coordinator plan

After Mentor leaves, the only production registrations are Mod Config UI
maintenance and gameplay-invalidation delivery. They do not contend for a native
mutation family and each already owns an explicit per-frame bound. Cross-client
weighted admission, soft/hard elapsed-time budgets, mutation exclusion,
starvation policy, coordinator metrics, and the coordinator-specific evidence
profile are therefore vacuous. Replace Mod Config admission with its existing
one-maintenance-pass-per-frame guard, keep the invalidation bus's explicit
operation cap, and retire the coordinator, its work identities, its evidence
pipeline/profile, and tests/docs that claim those are live. The ServiceCycle
profiler, full trace, decision journal, and host trace dump remain unchanged.

### Commit plan

1. Publish Mentor decision facts and mastery-event inputs in the world.
2. Add immutable Mentor configuration and worker-side evaluation.
3. Add verified native action adapters and bounded mastery-input patches.
4. Register Mentor, preserve Alt+M/status/UI behavior, and retire the legacy
   runtime, redundant hooks, legacy contracts, and obsolete config.
5. Remove the now-vacuous shared performance coordinator and simplify its two
   remaining clients.
6. Pin the trace roster and Mentor dashboard projection schema.
7. Align the roadmap, architecture/testing docs, changelog, and this evidence
   report.

Each implementation commit must pass both stub builds, the single-attempt portable
gate, and the installed-game contract gate before the next commit begins.

## Commits and gates

Every checkpoint used author and committer
`Marvin Bitterlich <644950+marvin-bitterlich@users.noreply.github.com>`. The two
stub builds, single-attempt portable gate, and installed-game contract gate ran
against each checkpoint's complete working tree before its commit.

| Commit | Subject | Stub/test code warnings | Portable | Profile | Installed game |
|---|---|---:|---:|---:|---:|
| `a6a6d6185755c4d355ef4ea1f28516e55a32df0a` | `world: publish Mentor mastery inputs` | 44 / 242 | 1,964 | 89 | 24 |
| `1d51dcfba11642a87a38395e3e9d3debdc5abe7c` | `mentor: plan sharing from the world` | 44 / 242 | 1,970 | 89 | 24 |
| `650f102234b3e97a42503f4fc5bf7d6332d45a5e` | `mentor: execute sharing through the service cycle` | 44 / 242 | 1,977 | 89 | 24 |
| `67c81c1bcfa338786bfc0a78f533b71bfc9d6344` | `mentor: retire the legacy runtime` | 44 / 242 | 1,889 | 89 | 24 |
| `87fc8f75e544675bbd9f90a388d3d4724d6662f1` | `common: retire legacy CPU budget coordination` | 44 / 242 | 1,832 | 89 | 24 |
| `eb1627716f4bdeec0fb903609f7a9d85c9396a88` | `mentor: expose service-cycle trace labels` | 44 / 242 | 1,832 | 90 | 24 |

The final documentation/report carrier is intentionally not self-identifying:
its SHA is supplied in the handoff after the commit exists. It ran the same full
gate at 1,832 portable, 90 profile, and 24 installed-game tests.

The lower portable count is expected deletion, not lost coverage: the legacy
Mentor engine, coordinator/evidence product, and their synthetic fixtures no
longer exist. The retained Mentor coverage is organized around evaluator, action
adapter, native adapter, typed composition, exact-XP journal, status, Harmony
binding, trace roster, and dashboard projection contracts.

## Final contract counts

| Place | Before (`bc1a4d6`) | After | Delta |
|---|---:|---:|---:|
| `capture` | 660 | 661 | +1 |
| `action` | 70 | 84 | +14 |
| `legacy` | 32 | 1 | -31 |
| `patch` | 5 | 9 | +4 |

Mentor has no legacy contract. The sole residual legacy entry is
`spell.get-icon`, a read-only quick-button icon lookup unrelated to a feature
runtime. `abstract-list.value` is capture, its actual world-collection place.
`alchemy-recipe.apply-mastery`, `.discover`, and `.is-discovered` are deleted:
no surviving source reads them. The manifest schema remains version 3 and the
assembly baselines are unchanged.

The four surviving Mentor patch contracts are the exact mastery inputs described
in W65/W66. Members used for fresh boundary checks and mutation verification are
action contracts; world-published facts are capture contracts. Contract-place
and allowlist changes landed together with the two-way ratchet green.

## CPU-budget retirement

Retired:

- `SuitePerformanceCoordinator`, registrations, work identities, weighted
  admission, starvation thresholds, soft/hard elapsed-time accounting, mutation
  leases, and coordinator metrics;
- Mentor's `OperationsPerFrame` and `CpuBudgetMilliseconds` bindings, live
  `ConfigEntry` reads, and schema values (schema 3 to 4 discards both);
- the coordinator evidence DTO/exporter/checker, the
  `OrbModding.PerformanceEvidence` tool, its checked JSON profile, and the
  coordinator-specific synthetic test harnesses.

Survived:

- `ModConfigFrameWork` admits at most one maintenance pass per Unity frame. It
  has no feature-action or native-mutation role and needs no weighted scheduler.
- `GameplayInvalidationBus` drains at most 64 delivery operations per Unity
  frame and resumes its own queue later. Its sequence cutoff and conservative
  widening remain safety behavior.
- ServiceCycle's debug profiler, release-capable full trace, rolling decision
  journal, dashboard, fair fixed action turns, and typed lifecycle handoff are
  the live automation observation/execution machinery.

W67 records why the two local delivery bounds are not a reason to preserve a
second scheduler.

## Scope size

From `bc1a4d6` through the report carrier: 3,127 lines added and 13,589 deleted.

## Deviations

- No stop condition fired. Mentor fits the ordinary service shape without a new
  Common semantic seam.
- The trace wire format, assembly baselines, and native-contract manifest schema
  version did not change.
- Configuration schema changed from 3 to 4, as explicitly allowed, solely to
  discard the two retired Mentor performance keys.
- An initial profile-only gate attempt for the trace-label checkpoint found a
  missing `OrbMentor` namespace import in the offline dashboard reader. No test
  had failed. The import was added and the complete four-leg gate was rerun from
  the first build before commit.
- The managed environment intermittently denied writes to NuGet's global
  vulnerability cache and emitted two `NU1900` restore warnings. Product/test
  code warning baselines remained 44/242 on cold builds, every build succeeded,
  and all test legs were green.
- No merge, push, branch switch, game installation, tag, release, trace-schema
  change, or assembly-baseline change was performed.
