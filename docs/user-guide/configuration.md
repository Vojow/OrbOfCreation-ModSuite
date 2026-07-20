# Configuration and safety

[Back to documentation](../README.md) · [Installation](installation.md)

Open the in-game **Mods** tab to configure Automata and Mentor. Orb Mod Config is optional; the same values remain available through BepInEx configuration files.

Supported plugin files carry a hidden `[OrbModding] ConfigurationSchemaVersion` marker. An unversioned file is upgraded to version 1 before the plugin binds its normal settings. When an existing file requires migration, the transaction creates the first free sibling backup (`.pre-schema-v1.bak`, then `.bak.2`, and so on) before changing the file; a fresh file has no original bytes to back up. Existing backups are never overwritten. Malformed, negative, or newer unsupported schema versions stop that plugin before it changes gameplay or opens its UI; do not lower a future marker by hand. The Mods panel reports the selected plugin's schema state separately from both runtime health and the Apply result, and never displays the configuration path or saved values in that status.

Automata's reviewed upgrade maps the old `AutoConcept.Mode=BalanceMastery` value to `Active`, gives the current fallback-seconds key precedence over the older seconds and minutes keys, clamps the result to 10-1800 seconds, and removes only an explicit obsolete-key list. Mentor and Mod Config retain their existing typed values and add only the version marker. See the [configuration-schema contract](../plans/configuration-schema.md) for the exact transaction and validation rules.

Important controls:

Disabled feature modes lock their tuning fields in Orb Mod Config. Mode controls, toggle shortcuts, gameplay-button visibility, emergency disable, and diagnostics remain editable. Nested controls unlock only when all of their staged prerequisites are selected; these UI dependencies do not change or erase saved values.

- `AutoBuy.Mode` and `AutoCast.Mode`: select `Disabled` or `Active`.
- `AutoConcept.Mode`: `Disabled` (default) or `Active` for Scholar Active Concepts.
- `AutoConcept.SlotManagementMode`: `TimedCycle` (default) rotates compatible concepts only after each has received the complete configured settled-active period; `RotateAll` replaces active concepts to train a compatible strictly lower-mastery concept; `PreserveManual` keeps concepts that were already active when automation started.
- `AutoConcept.ShowToggleButton`: show the `CN ON/OFF` configured-intent button in the native Auto Buy-anchored control strip; runtime waiting or blocking remains in its tooltip; default true.
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
