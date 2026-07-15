# Orb Mentor 0.1.0 beta candidate

Orb Mentor shares a configurable percentage of final native spell-mastery XP from the current highest-mastery spell tier with every discovered lower-mastery spell.

Fresh installs start in `General.Mode=Disabled`. Set it to `Active`, press `Alt+M`, or use the compact `M ON/OFF/BLOCKED` gameplay control. `SharedPool` (default, 10%) bounds the total bonus to the configured percentage. `PerRecipient` grants that percentage to each eligible spell and scales with collection size.

The plugin grants only through `SpellRecipeSO.GainMasteryExp`. It never changes source XP, levels, loadouts, saves, or spell-type XP directly. A contract failure blocks sharing and discards pending bonus work.

Automated and static installed-game validation is complete. Interactive gameplay/save validation remains required before a production-ready release; see [the implementation plan](../../docs/mastery-sharing-plan.md).
