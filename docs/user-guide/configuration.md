# Configuration and safety

[Back to documentation](../README.md) · [Installation](installation.md)

Open the in-game **Mods** tab to configure Automata. Orb Mod Config is optional; the same values remain available through BepInEx configuration files.

Important controls:

- `AutoBuy.Mode` and `AutoCast.Mode`: select `Disabled` or `Active`.
- `AutoConcept.Mode`: `Disabled` (default) or `Active` for Scholar Active Concepts.
- Structure and upgrade affordability modes are configured separately.
- Absolute and relative reserves protect selected resources.
- `LeaveQueueSlots` preserves queue room for manual actions.
- Action multipliers are capped to available queue room and revalidated per level.
- Auto Concept rate, quantity, and drain-ratio floors protect continuous resources; current concept quantities become the preserved manual baseline when it starts.
- `Safety.EmergencyDisable` immediately stops new automated purchases, casts, and concept mutations.

Auto Buy defaults to Active with 100x affordability thresholds. Auto Cast and Auto Concept default to Disabled. When enabled, Auto Cast fully charges charge-capable spells by default; turn off `Auto Cast > Full charge` to fire them immediately. Auto Concept uses a 10% positive-rate reserve, 10% finite-resource quantity floor, and 0.95 native drain-ratio watchdog by default. Operational automation logging is off by default and should normally be enabled only for troubleshooting.

Back up saves before risky configuration changes and run only one automatic buyer. The complete scheduling, affordability, reserve, and queue-ownership contract is in the [Orb Automata reference](../../src/OrbAutomata/README.md).
