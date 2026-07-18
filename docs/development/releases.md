# Public release checklist

[Back to roadmap](../plans/roadmap.md)

## Current supported component betas

The supported package is an explicit allowlist:

- **Orb Automata 0.8.1:** rejection-aware, queue-filling Auto Buy, Auto Cast, Auto Concept, and progression-aware spell leveling.
- **Orb Mentor 0.3.0:** native mastery-XP sharing for spells, with independently enabled artifact and alchemy domains.
- **Orb Mod Config 0.6.0:** optional in-game configuration UI.
- **Orb Modding Common 0.3.1:** bundled shared dependency with the audited Alchemy/Scholar gameplay-domain classifier, not a separate gameplay mod.

`OrbChronomancer` and `OrbAchievementResonance` live only on the dedicated experimental branch and must not enter a supported archive. Orb Insights and Orb Toolbox remain plans rather than packaged plugins.

## Release gates, in order

### P0 — must pass before publishing

1. **Release review:** record the exact commit, tag, project versions, supported plugin allowlist, archive entries, test evidence, and prerelease/stable status before publication.
2. **Clean-install test:** use a fresh BepInEx profile and fresh generated configs. Verify there are no development backups, duplicate DLLs, experimental DLLs, or copied game assemblies.
3. **Quiet-log acceptance:** run a representative 10-minute Active session with all supported modules enabled. Normal logs should contain lifecycle information plus warnings/errors, not per-action chatter.
4. **Save and removal safety:** back up a normal save, exercise each supported gameplay plugin, save/reload, then remove the suite and verify the game and save still load normally.
5. **Desktop and Steam Deck performance:** test new game and NG+, representative game-speed multipliers, large catalogs, populated queues, and concurrent supported modules. Confirm bounded work does not create sustained frame stalls or crashes.
6. **Auto Buy matrix:** Structures only, Upgrades only, both, allowlist, blocklist, independent affordability modes, reserves, emergency disable, live Bulk Development changes, action multiplier on/off, priority configuration, locked structures, completed upgrades, and progression-aware spell leveling before and after native multi-level unlock.
7. **Auto Cast matrix:** instant, channelled, toggle, and aura behavior; unavailable or unaffordable spells; native queue pressure; emergency disable; and scene/load transitions.
8. **Auto Concept matrix:** disabled and timed-cycle modes, catch-up behavior, ten-second minimum, setup time, one and multiple acquired slots, zero-resource concepts, unavailable concepts, and reset/load transitions without assignment churn.
9. **Mentor matrix:** spell source policies and independent spell, artifact, and alchemy domains; Shared Pool and Per Recipient; disabled-domain silence; native persistence; recursion suppression; and bounded processing.
10. **Mod Config matrix:** at least 1920×1080 and one smaller resolution; Mods available from a new game and last among available tabs; Apply/Revert; scrolling; compound feature locking; and operation without native Auto Queue UI.
11. **Game-build guard:** verify the audited installed-game assembly contracts and ensure a mismatched build fails closed with a clear warning.

Interactive behavior must satisfy [runtime validation](runtime-validation.md); a successful build or package rehearsal is not runtime approval.

### P1 — package quality

1. Build archives only through the supported allowlist. Package generation must fail if an experimental or unknown plugin DLL is present.
2. Rehearse the complete suite package and inspect raw ZIP entries. Reject backslashes, rooted paths, missing `BepInEx/plugins/` entries, unexpected DLLs, or inconsistent versions.
3. Include only the required DLLs, README, changelog, license, and install/uninstall guidance.
4. Verify the first-run documentation covers module defaults, native queue ownership, reserves, emergency controls, concept modes, Mentor domains, and optional Mod Config.
5. Verify troubleshooting covers opt-in diagnostics, duplicate DLLs, conflicting automation plugins, Auto Concept assignment churn, and restoring a previous version.
6. Write release notes with the exact supported game/BepInEx baseline, known limitations, validation evidence, and prerelease/stable status.
7. Create the tag and artifacts from the reviewed clean commit. Replacing or deleting an existing public release or tag requires explicit user authorization naming that target.

### P2 — follow-up after the candidate

1. Add an unsaved-changes prompt when leaving Mods if user testing shows accidental loss is common.
2. Add further caches or aggregation only when profiling identifies a material remaining bottleneck and correctness can be preserved.
3. Promote release-candidate features to stable only after their interactive gates are recorded.

## Recommended release decision

ModSuite 0.3.0 Beta 1 completed its automated, real-reference, package, and desktop gates. The maintainer explicitly authorized prerelease publication with Steam Deck/Proton validation deferred until after release. Release notes must identify Proton as unverified, and the beta must not be promoted to stable until the remaining Proton and extended interactive gates pass.
