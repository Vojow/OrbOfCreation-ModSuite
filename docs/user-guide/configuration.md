# Configuration and safety

[Back to documentation](../README.md) · [Installation](installation.md)

Open the in-game **Mods** tab to configure the suite. The tab itself is optional — `Interface.EnableButtonShell` turns it off — and every value stays editable in the BepInEx configuration file.

The suite is one plugin with one identity (`dev.vojow.orbofcreation.modsuite`), so BepInEx keeps all of it in one configuration file. That file carries a hidden `[OrbModding] ConfigurationSchemaVersion` marker and its current version is 1, which is the first version this file has ever had: the three retired plugins' files carry different names and are never read, so nothing migrates into it and the 0-to-1 step changes nothing. Settings you had under the old plugins do not carry over. Should a future version ever need to rewrite an existing file, the transaction first creates the first free sibling backup (`.pre-schema-v<version>.bak`, then `.bak.2`, and so on) and never overwrites one that exists. Malformed, negative, or newer unsupported schema versions stop the suite before it changes gameplay or opens its UI; do not lower the marker by hand. The Mods panel reports the schema state separately from both runtime health and the Apply result, and never displays the configuration path or saved values in that status.

Important controls:

Disabled feature modes lock their tuning fields in the configuration UI. Mode controls, toggle shortcuts, gameplay-button visibility, emergency disable, and diagnostics remain editable. Nested controls unlock only when all of their staged prerequisites are selected; these UI dependencies do not change or erase saved values.

- `AutoBuy.Mode` and `AutoCast.Mode`: select `Disabled` or `Active`.
- `AutoConcept.Mode`: `Disabled` (default) or `Active` for Scholar Active Concepts.
- `AutoHarvest.Mode`: `Disabled` (default) or `Active` for the exact audited fruit-tree and treasure-tree collection actions. `CollectFruitTrees` and `CollectTreasureTrees` both default to true behind the disabled master switch.
- `AutoHarvest.EvaluationIntervalSeconds`: interval between exact readiness checks while enabled; default 1.0, range 0.25 to 10 seconds. There is no gameplay button or shortcut in the first slice.
- `AutoConcept.SlotManagementMode`: `TimedCycle` (default) rotates compatible concepts only after each has received the complete configured settled-active period; `RotateAll` replaces active concepts to train a compatible strictly lower-mastery concept; `PreserveManual` keeps concepts that were already active when automation started.
- `AutoConcept.ShowToggleButton`: show the `CN ON/OFF` configured-intent button in the native Auto Buy-anchored control strip; runtime health remains in its tooltip; default true.
- `AutoConcept.TrainingPeriodSeconds`: settled active time for one newly assigned concept; default 300, range 10 to 3600. `RotateAll` and `PreserveManual` can resume earlier after mastery catch-up, while `TimedCycle` always waits for the full period.
- `AutoBuy.AutoLevelSpells`: enabled by default while Auto Buy is active. It detects native progression automatically: Locked, Single, then All after the completed level-all Upgrade. It spends the game's live spell-level costs and can be disabled separately.
- `AutoConcept.FallbackEvaluationIntervalSeconds`: Advanced-only maximum idle delay between full plan calculations; default 300, range 10 to 1800. Native signals can evaluate earlier. Previous seconds and legacy minutes values migrate automatically.
- Structure and upgrade affordability modes are configured separately.
- Absolute and relative reserves protect selected resources.
- `LeaveQueueSlots` preserves queue room for manual actions.
- `AutoBuy.PurchaseGrouping` selects `Single`, `Fixed`, `BulkDevelopment` (default), or `ActionMultiplier`; every level is capped to live queue room and revalidated independently.
- Auto Concept rate, quantity, and drain-ratio floors protect continuous resources; zero-resource replacements are skipped so they cannot starve other safe concepts in the cycle. Current concept quantities remain the rollback ownership baseline even when `RotateAll` permits assignment replacement.
- `Safety.EmergencyDisable` immediately stops new automated purchases, casts, concept mutations, spell levels, and harvest submissions.

Auto Buy defaults to Active with 100x affordability thresholds. Auto Cast, Auto Concept, and Auto Harvest default to Disabled. Auto Harvest queues quantity one, keeps at most one supported collect active, and runs only through the Common ServiceCycle engine. It may use the final free plot-action entry when the game's native capacity contract also reports room. When enabled, Auto Cast fully charges charge-capable spells by default; turn off `Auto Cast > Full charge` to fire them immediately. Auto Concept uses a 10% positive-rate reserve, 10% finite-resource quantity floor, and 0.95 native drain-ratio watchdog by default. Operational automation logging is off by default and should normally be enabled only for troubleshooting.

Back up saves before risky configuration changes and run only one automatic buyer. The complete scheduling, affordability, reserve, and queue-ownership contract is in the [automation reference](../../src/Automata/README.md).
