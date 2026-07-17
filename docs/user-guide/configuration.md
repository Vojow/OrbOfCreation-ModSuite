# Configuration and safety

[Back to documentation](../README.md) · [Installation](installation.md)

Open the in-game **Mods** tab to configure Automata. Orb Mod Config is optional; the same values remain available through BepInEx configuration files.

Important controls:

- `AutoBuy.Mode` and `AutoCast.Mode`: select `Disabled` or `Active`.
- `AutoConcept.Mode`: `Disabled` (default) or `Active` for Scholar Active Concepts.
- `AutoConcept.SlotManagementMode`: `RotateAll` (default) replaces active concepts to train a compatible strictly lower-mastery concept; `PreserveManual` keeps concepts that were already active when automation started.
- `AutoConcept.TrainingPeriodSeconds`: maximum settled active time for one newly assigned concept; default 300, range 10 to 3600. Rotation resumes earlier if the concept catches the highest effective mastery captured when its session began.
- `AutoConcept.RebalanceIntervalSeconds`: ordinary rebalance cadence from 10 to 1800 seconds; default 300. Legacy minute values migrate automatically.
- Structure and upgrade affordability modes are configured separately.
- Absolute and relative reserves protect selected resources.
- `LeaveQueueSlots` preserves queue room for manual actions.
- Action multipliers are capped to available queue room and revalidated per level.
- Auto Concept rate, quantity, and drain-ratio floors protect continuous resources; current concept quantities remain the rollback ownership baseline even when `RotateAll` permits assignment replacement.
- `Safety.EmergencyDisable` immediately stops new automated purchases, casts, and concept mutations.

Auto Buy defaults to Active with 100x affordability thresholds. Auto Cast and Auto Concept default to Disabled. When enabled, Auto Cast fully charges charge-capable spells by default; turn off `Auto Cast > Full charge` to fire them immediately. Auto Concept uses a 10% positive-rate reserve, 10% finite-resource quantity floor, and 0.95 native drain-ratio watchdog by default. Operational automation logging is off by default and should normally be enabled only for troubleshooting.

Back up saves before risky configuration changes and run only one automatic buyer. The complete scheduling, affordability, reserve, and queue-ownership contract is in the [Orb Automata reference](../../src/OrbAutomata/README.md).
