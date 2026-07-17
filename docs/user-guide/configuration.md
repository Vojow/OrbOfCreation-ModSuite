# Configuration and safety

[Back to documentation](../README.md) · [Installation](installation.md)

Open the in-game **Mods** tab to configure Automata and Mentor. Orb Mod Config is optional; the same values remain available through BepInEx configuration files.

Important controls:

Disabled feature modes lock their tuning fields in Orb Mod Config. Mode controls, toggle shortcuts, gameplay-button visibility, emergency disable, and diagnostics remain editable. Nested controls unlock only when all of their staged prerequisites are selected; these UI dependencies do not change or erase saved values.

- `AutoBuy.Mode` and `AutoCast.Mode`: select `Disabled` or `Active`.
- `AutoConcept.Mode`: `Disabled` (default) or `Active` for Scholar Active Concepts.
- `AutoConcept.SlotManagementMode`: `TimedCycle` (default) rotates compatible concepts only after each has received the complete configured settled-active period; `RotateAll` replaces active concepts to train a compatible strictly lower-mastery concept; `PreserveManual` keeps concepts that were already active when automation started.
- `AutoConcept.ShowToggleButton`: show the `CN ON/OFF/!` gameplay button in the native Auto Buy-anchored control strip; default true.
- `AutoConcept.TrainingPeriodSeconds`: settled active time for one newly assigned concept; default 300, range 10 to 3600. `RotateAll` and `PreserveManual` can resume earlier after mastery catch-up, while `TimedCycle` always waits for the full period.
- `AutoBuy.AutoLevelSpells`: enabled by default while Auto Buy is active. It detects native progression automatically: Locked, Single, then All after the completed level-all Upgrade. It spends the game's live spell-level costs and can be disabled separately.
- `AutoConcept.FallbackEvaluationIntervalSeconds`: Advanced-only maximum idle delay between full plan calculations; default 300, range 10 to 1800. Native signals can evaluate earlier. Previous seconds and legacy minutes values migrate automatically.
- Structure and upgrade affordability modes are configured separately.
- Absolute and relative reserves protect selected resources.
- `LeaveQueueSlots` preserves queue room for manual actions.
- Action multipliers are capped to available queue room and revalidated per level.
- Auto Concept rate, quantity, and drain-ratio floors protect continuous resources; zero-resource replacements are skipped so they cannot starve other safe concepts in the cycle. Current concept quantities remain the rollback ownership baseline even when `RotateAll` permits assignment replacement.
- `Safety.EmergencyDisable` immediately stops new automated purchases, casts, concept mutations, and spell levels.

Auto Buy defaults to Active with 100x affordability thresholds. Auto Cast and Auto Concept default to Disabled. When enabled, Auto Cast fully charges charge-capable spells by default; turn off `Auto Cast > Full charge` to fire them immediately. Auto Concept uses a 10% positive-rate reserve, 10% finite-resource quantity floor, and 0.95 native drain-ratio watchdog by default. Operational automation logging is off by default and should normally be enabled only for troubleshooting.

Back up saves before risky configuration changes and run only one automatic buyer. The complete scheduling, affordability, reserve, and queue-ownership contract is in the [Orb Automata reference](../../src/OrbAutomata/README.md).
