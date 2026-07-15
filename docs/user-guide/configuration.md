# Configuration and safety

[Back to documentation](../README.md) · [Installation](installation.md)

Open the in-game **Mods** tab to configure Automata. Orb Mod Config is optional; the same values remain available through BepInEx configuration files.

Important controls:

- `AutoBuy.Mode` and `AutoCast.Mode`: select `Disabled` or `Active`.
- Structure and upgrade affordability modes are configured separately.
- Absolute and relative reserves protect selected resources.
- `LeaveQueueSlots` preserves queue room for manual actions.
- Action multipliers are capped to available queue room and revalidated per level.
- `Safety.EmergencyDisable` immediately stops new automated purchases and casts.

Auto Buy defaults to Active with 100x affordability thresholds. Auto Cast defaults to Disabled. Operational purchase and cast logging is off by default and should normally be enabled only for troubleshooting.

Back up saves before risky configuration changes and run only one automatic buyer. The complete scheduling, affordability, reserve, and queue-ownership contract is in the [Orb Automata reference](../../src/OrbAutomata/README.md).
