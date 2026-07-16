# Orb Mentor 0.1.1 beta candidate

Orb Mentor shares configurable percentages of native mastery XP from the highest-mastery source with lower-mastery recipients in three independent domains: discovered spells, created artifacts, and available alchemy recipes. Artifact and alchemy sharing are opt-in.

Fresh installs start in `General.Mode=Disabled`. Set it to `Active`, press `Alt+M`, or use the compact `M ON/OFF/BLOCKED` gameplay control. `SharedPool` (default, 10%) bounds the total bonus to the configured percentage. `PerRecipient` grants that percentage to each eligible spell and scales with collection size.

The plugin uses each domain's native mastery path and suppresses its own grant callbacks. It never subtracts source XP or changes loadouts, recipe activity, costs, or discovery state. A contract failure blocks sharing and discards pending bonus work.

Performance scheduling keeps Harmony callbacks bounded: callbacks only capture cached source identity, mastery/discovery evidence, and XP into a finite coalescing queue. Catalog reconciliation, relationship refresh, recipient planning, and native grants run later under CPU and operation limits. Budget deferral preserves pending amounts. A captured event whose progression epoch became stale, an overflowed capture, or a delayed recipient that no longer passes native identity/eligibility checks is deliberately rejected and counted rather than banked or force-granted. Permanent native-contract quarantine survives scene and save lifecycle resets; transient grant failures may retry only after a lifecycle reset.

Automated and static installed-game validation is complete. Interactive gameplay/save validation remains required before a production-ready release; see [the implementation plan](../../docs/plans/mentor.md) and [runtime checklist](../../docs/development/mentor-runtime-validation.md).
