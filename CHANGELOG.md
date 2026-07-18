# Changelog

## Orb Modding Common 0.3.1 Beta 1 — 2026-07-18

- Add one fail-closed, lifecycle-scoped classifier for ordinary Alchemy and Scholar Concepts using exact native types, stable UUIDs, the authoritative `ConceptRecipes` snapshot, and audited type identities.
- Make Auto Concept consume the shared classifier for catalog admission and final add/remove identity validation, removing its duplicated Scholar-type boundary.

## Orb Automata 0.8.2 Beta 1 — 2026-07-18

- Keep queue feeding responsive during rapid native completions: finish the current bounded completion-settlement generation, coalesce intervening signals into one follow-up, preserve CPU-sliced scan progress, and wake a prepared candidate immediately when a completion reopens a slot.
- Retain the 10 Hz full-queue poll only as a fallback when no completion signal arrives; every resumed candidate still refreshes native availability, costs, resources, reserves, limits, and queue room before mutation.
- Promote the deterministic four-frame completion storm to an active performance regression gate covering near-full queue depth, purchase count, candidate-evaluation amplification, and idle refill frames.

## Orb Automata 0.8.1 Beta 1 — 2026-07-18

- Treat completed Structures and Upgrades as typed progression signals: Structure completion immediately schedules a bounded Upgrade-registry refresh, Upgrade completion schedules the corresponding Structure refresh, and bursts coalesce without discarding conservative cross-candidate settlement.
- Add a deterministic headless E2E harness that drives the production Auto Buy engine through simulated native queue, economy, failure, and save/load boundaries; computer-controlled real-game checks remain UAT.
- Add operation-count performance simulations for large candidate sets and shared-queue saturation, plus a documented completion-storm target for the remaining scan-restart regression.
- Prevent one cheap Structure or Upgrade from monopolizing a large shared action queue: when several candidates are ready, queue one independently validated level from each ranked candidate before repeating the pass.
- Continue directly through the prepared ranking on consecutive frames instead of forcing a catalog rescan between candidates; a full queue retains the next candidate and feeds it into the first reopened slot.
- Let a lone eligible candidate still consume all usable queue room, with native availability, current cost, reserves, maximum level, and final purchase validation before every queued level.
- Centralize queue allocation in `OrbModding.Common.QueueCapacitySnapshot`, keeping authoritative native capacity/occupancy, live remaining room, Auto Buy's usage limit, and the manual reservation distinct and provenance-tagged.
- Refresh the complete queue-capacity snapshot after live cost/reserve validation immediately before every Auto Buy mutation; contradictory or missing native values now fail closed.
- Add portable regressions for a 200-level lone-candidate fill and fair Structure/Upgrade handoff across multiple ranked candidates without the idle evaluation interval.
- Validate the `0.8.1` build on a disposable high-resource Slot 3: the visible shared queue rose from `14/304` to `174/304` after five seconds and `302/304` after ten, while 1,797 successful purchases covered 166 distinct candidates with zero native failures.

## Orb Automata 0.8.0 Beta 1 — 2026-07-17

- Prepare Orb Automata 0.8.0 with typed Auto Buy rejection telemetry, structured multi-resource blockers, and separate scan-cap, rejection-transition, and native-mutation failure accounting.
- Keep unaffordable Upgrades subscribed to their decoded resource dependencies even when native `CanPurchase()` rejects them; installed IL confirms that contract includes affordability as well as lifecycle, requirements, and queue admission.
- Let the selected Structure or Upgrade fill the usable queue room while it remains safe, revalidating every level and ending the prepared group at the first failed live admission before reranking.
- Cache validated queue, queued-level, and multi-buy reflection metadata while re-fetching the live global multi-buy variable for every Upgrade level, and coalesce shared resource invalidation until the owned repeat group settles.
- Rate-limit repeated native purchase-failure examples per candidate while retaining aggregate attempts, successes, and failures.
- Retain the next ranked candidate while the native queue is full, so each reopened slot is fed without another catalog scan; if queue room remains after a repeat group, settle dirty state and rerank before mutating a different candidate.
- Validate the queue-feeding path against a disposable 13-resource `9e60` save: 150 native purchases completed with zero failures while candidate evaluations fell from 58,973 before the fix to 1,483 after it in comparable sustained runs.

## Orb Of Creation Mod Suite 0.3.0 Beta 1 — 2026-07-17

- Unify disabled-feature configuration across the supported suite. Orb Mod Config 0.6.0 supports multiple staged prerequisites and refreshes enum dependencies immediately; Automata and Mentor now lock inactive tuning while keeping mode, shortcut, status-button, safety, and diagnostic controls usable.
- Prepare Orb Mentor 0.3.0 and Orb Modding Common 0.3.0 for compound spell/artifact/alchemy and nested Auto Buy configuration dependencies.
- Prepare Orb Automata 0.7.0 with `TimedCycle` as the new default Auto Concept slot-management policy, keeping every assignment active for the full settled `TrainingPeriodSeconds` before rotating to its remembered compatible replacement. Existing saved selections remain unchanged.
- Add progression-aware Auto Spell Leveling under Auto Buy, with a separate `AutoLevelSpells` switch. It detects Locked, Single, and All capability without player mode changes, revalidates per-spell prerequisites and live costs, and switches to the game's native level-all action only after the exact Upgrade is completed.
- Prevent Auto Concept from repeatedly removing and re-adding the same concept when its required resource is at zero, which could keep training on one slot even after more slots were acquired. Every positive prospective drain now rejects an authoritative zero resource, replacements must pass one-instance admission before the current assignment is removed, and unsafe candidates no longer block the timed-cycle order.
- Anchor Auto Buy, Auto Cast, Auto Concept, and Mentor outside the native action queue with 12-pixel gaps; remove cloned native view gating, resolve the audited inactive Auto Buy hierarchy without `AutoBuyManager` reference matching, and use an extensible shared ordered-slot layout. The new `CN ON/OFF/!` button toggles Auto Concept directly.
- Rename Auto Concept's technical idle polling control to `FallbackEvaluationIntervalSeconds`, migrate both previous seconds and legacy minutes values, and show it only under Advanced. `TrainingPeriodSeconds` remains the normal gameplay rotation timer.
- Prepare Orb Automata 0.6.0 and Orb Mentor 0.2.0 as an intermediate local candidate before the current beta versions.
- Add Auto Concept `SlotManagementMode`: `RotateAll` replaces a settled active concept when a compatible discovered concept has strictly lower mastery, while `PreserveManual` retains the previous manual-baseline behavior. Rotation uses the verified native remove path, waits for settlement, and revalidates before adding; rate-limited no-change summaries make an idle balancer visible in diagnostics.
- Keep each newly assigned Auto Concept in a settled training session until it reaches the highest effective mastery captured at assignment time or `TrainingPeriodSeconds` elapses, whichever occurs first. The default period is 300 seconds; native setup/settlement time does not consume it.
- Present Auto Concept's mode consistently as `Disabled` or `Active`; active mode still performs mastery balancing.
- Read discovered concepts from the exact runtime `ConceptRecipes` list used by the native UI, migrate the rebalance interval from minutes to seconds with a 300-second default and 10-second minimum, timestamp all Orb Automata messages, and log permanent concept-contract failures once per lifecycle.
- Add disabled-by-default Auto Concept mastery balancing inside Orb Automata. It resolves only the UUID-scoped Scholar concept assets, fills compatible acquired slots breadth-first, batches depth to live native mastery limits, tracks manual and automated quantity separately, shares the suite scheduler, and rolls back only proven automated quantity when the native drain watchdog becomes unsafe.
- Validate every Auto Concept add with an exact prospective native drain vector, live resource-quality conversion, positive-rate reserve, finite-quantity floor, stable UUID/type checks, compatible slots, and final native quantity revalidation. Unknown contracts fail closed and ordinary alchemy remains untouched.
- Add Mentor's `EquippedSpells` source policy as the new default: every equipped spell may share with discovered spells strictly below that source's mastery. Keep `HighestDiscovered` selectable.
- Carry each Mentor event's exclusive source-mastery ceiling through recipient planning, pending consolidation, parking, and final native validation so lower equipped sources cannot grant to equal- or higher-mastery recipients.
- Bump Orb Automata to 0.5.2, Orb Mentor to 0.1.2, Orb Mod Config to 0.5.3, and Orb Modding Common to 0.2.1 for the combined full-charge, coordinator, and Auto Buy performance changes.
- Preserve CPU-sliced Structure repeat groups across coalesced native completion signals, while still settling broad completion effects before selecting the next ranked group.
- Move routine Auto Buy lifecycle probes to fixed 250 ms bounded slices so purchase frequency cannot multiply locked/active reflection work.
- Keep locked Structures out before cost or purchase checks, and park Upgrades rejected by native `CanPurchase()` outside high-frequency resource invalidation until a bounded lifecycle or completion retry.
- Add an opt-in `PrioritizeCostAndQualityStructures` Auto Buy policy. It ranks unlocked, affordable Structures first only when a one-time native effect preview proves a cost reduction or resource-quality increase; unknown effects keep normal cost ordering.
- Give continuously pending Auto Buy work a bounded three-turn coordinator weight before yielding to Mentor, Auto Cast, or UI work.
- Make `DecisionLogLevel=Off` suppress all operational Auto Buy and Auto Cast records, rate-limit summary logging, and reserve per-purchase messages for verbose diagnostics.
- Synchronize clean Mod Config fields with live changes made by native status buttons or shortcuts without overwriting staged edits.
- Add configurable Auto Cast support for charge-capable spells: fully charge through the native hold contract by default, or fire immediately when full charging is disabled.
- Retry Mod Config UI installation on slower Steam Deck/Proton scene startup instead of permanently giving up after one attempt.
- Repair Mod Config when its ScreenContent panel is destroyed even if the Mods button survives, restore a usable native view after failed open/close, and detach the old navigation listeners before reinstalling.
- Throttle missing native-control discovery to avoid scanning the complete Unity object registry every frame before autoqueue unlock.
- Cache Auto Buy's static candidate registry, cap its reflective CPU slice to 1 ms, and poll full queues at 10 Hz instead of every frame.
- Preserve Mentor XP and capture-time recipients when XP arrives during bounded relationship/reconciliation work; use constant-time indexed source exclusion, let active refreshes finish under sustained invalidation, cap and safely compact immutable evidence history, and retain unorderable captures without guessing their route until lifecycle/native-identity cancellation.
- Cache Mentor catalogs and native object lookups, stop repeated inactive-state cleanup, and lower its default grant and CPU budgets.
- Schedule Auto Buy and Auto Cast through the shared suite frame budget, with at most one native automation mutation per frame and resumable multi-level purchase groups.
- Schedule Mentor reconciliation, evidence resolution, planning, and exact native grants through that same frame coordinator; denied or incomplete cooperative work blocks stale grants for that domain, final recipient progression is revalidated inside the mutation lease, and transiently ineligible UUIDs park with exact XP until a later authoritative refresh without retry churn or head-of-line blocking. The parked ledger is bounded and fails the domain closed on overflow. AutoBuy plus Mentor can start only one native mutation in a Unity frame.
- Schedule Mod Config catalog discovery and logging, installation, repair, navigation-event maintenance, and slow integrity checks only when due through the shared cooperative budget.
- Revalidate deferred Auto Cast slots by stable recipe and native identity, and remove Upgrade automation from admission and ranking if native multi-buy restoration cannot be verified while Structures continue independently.
- Move Orb Chronomancer and Orb Achievement Resonance source, tests, and design notes to the dedicated `codex/experimental-chronomancer-resonance` branch; supported `main` builds and archives contain only the allowlisted suite modules.

## Orb Of Creation Mod Suite 0.1.0 Beta 1 — 2026-07-15

- Added the supported suite package with Orb Automata, Orb Mentor, Orb Mod Config, and Orb Modding Common. Experimental plugins remain excluded.
- Bundled Orb Mod Config 0.5.1.
- Made the Mods configuration tab available from the start of a new save instead of requiring the NG+-gated Time tab.
- Kept Mods as the final item when native navigation tabs unlock or reorder.

- Default fresh Auto Cast configurations to a 0% resource-fullness threshold while retaining affordability and reserve checks.
- Write release ZIP entries with portable `/` separators and validate their layout for Linux, SteamOS, and Bazzite extraction.
- Add Orb Mod Config 0.5.0 with feature-oriented tabs, contextual labels, hidden compatibility switches, dependency-aware controls, apply indicators, and optional Steam Deck keyboard input.
- Add the Orb Mentor 0.1.0 spells-only MVP with native mastery grants, guarded recursion suppression, Shared Pool and Per Recipient economies, bounded frame processing, `Alt+M`, status control, live typed configuration, installed-game contracts, portable tests, and packaging support.
- Extend opt-in sharing to created artifacts and available alchemy recipes, using separate domain pools and native grant paths.
- Prevent continuously replenished artifact and alchemy batches from starving later recipients by preserving FIFO pending order.
- Add cohesive Mentor, Auto Buy, and Auto Cast status controls with native hover tooltips.
- Keep the logging probe development-only and fresh installations disabled.

All notable user-facing changes are documented here. The project follows semantic versioning per plugin while the suite remains in beta.

- Public-repository documentation, contribution guidance, CI, and release hygiene.

## Orb Automata 0.4.0

- Removed DryRun, runtime-probe, expert-override, per-session purchase-limit, and deprecated Auto Research settings from the release UI and generated configuration.
- Defaulted Auto Buy to Active and Auto Cast to Disabled.
- Added separate structure and upgrade affordability policies.
- Added optional action-multiplier handling capped to available queue room with per-level resource and reserve validation.
- Changed fresh-install reserves to zero so affordability modes are the default spending margin.
- Continued CPU-sliced scans and prepared queue batches every frame, removing the evaluation-interval gap while work is pending.
- Kept normal operational logs opt-in while retaining startup, warning, and error records.
- Isolated stub-linked test output from deployable Release binaries.

## Orb Automata 0.3.5 Beta 1 — 2026-07-14

- Published queue-aware Auto Buy for native structures and upgrades.
- Added Auto Cast rotation, resource thresholds, targeting, aura/channel handling, keyboard control, and a queue-adjacent status button.
- Included Orb Mod Config 0.4.0 and Orb Modding Common in the recommended archive.
