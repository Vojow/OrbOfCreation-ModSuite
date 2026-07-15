# Orb Mentor 0.1.0 beta candidate

Orb Mentor shares configurable percentages of native mastery XP from the highest-mastery source with lower-mastery recipients in three independent domains: discovered spells, created artifacts, and available alchemy recipes. Artifact and alchemy sharing are opt-in.

Fresh installs start in `General.Mode=Disabled`. Set it to `Active`, press `Alt+M`, or use the compact `M ON/OFF/BLOCKED` gameplay control. `SharedPool` (default, 10%) bounds the total bonus to the configured percentage. `PerRecipient` grants that percentage to each eligible spell and scales with collection size.

The plugin uses each domain's native mastery path and suppresses its own grant callbacks. It never subtracts source XP or changes loadouts, recipe activity, costs, or discovery state. A contract failure blocks sharing and discards pending bonus work.

Automated and static installed-game validation is complete. Interactive gameplay/save validation remains required before a production-ready release; see [the implementation plan](../../docs/mastery-sharing-plan.md).
