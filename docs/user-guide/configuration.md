# Configuration and safety

[Back to documentation](../README.md) · [Installation](installation.md)

Open the in-game **Mods** tab to configure Automata and Mentor. Orb Mod Config is optional; the same values remain available through BepInEx configuration files.

Supported plugin files carry a hidden `[OrbModding] ConfigurationSchemaVersion` marker. Automata's current schema is version 3; Mentor and Mod Config remain at version 1. An unversioned file executes every reviewed one-version step before the plugin binds its normal settings. When an existing file requires migration, the transaction creates the first free sibling backup for the target version (`.pre-schema-v3.bak` for Automata or `.pre-schema-v1.bak` for Mentor and Mod Config, then `.bak.2`, and so on) before changing the file; a fresh file has no original bytes to back up. Existing backups are never overwritten. Malformed, negative, or newer unsupported schema versions stop that plugin before it changes gameplay or opens its UI; do not lower a future marker by hand. The Mods panel reports the selected plugin's schema state separately from both runtime health and the Apply result, and never displays the configuration path or saved values in that status.

Automata's 0-to-1 step maps the old `AutoConcept.Mode=BalanceMastery` value to `Active`, gives the current fallback-seconds key precedence over the older seconds and minutes keys, clamps the result to 10-1800 seconds, and removes only an explicit obsolete-key list. The historical 1-to-2 step is a no-op. The 2-to-3 step replaces the legacy Auto Buy multiplier/repeat settings with `PurchaseGrouping` and `FixedGroupSize`, preserving reviewed precedence and failing closed on malformed known values. The retired Auto Harvest runtime-selector key remains inert because it is not bound, parsed, displayed, or used. Mentor and Mod Config retain their existing typed values and add only the version marker.

Important controls:

Disabled feature modes lock their tuning fields in Orb Mod Config. Mode controls, toggle shortcuts, gameplay-button visibility, emergency disable, and diagnostics remain editable. Nested controls unlock only when all of their staged prerequisites are selected; these UI dependencies do not change or erase saved values.

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

### Auto Harvest replay capture

The Advanced **Auto Harvest replay capture** setting (`Diagnostics.EnableAutoHarvestReplayCapture`) writes
one finite diagnostic artifact and is off by default. It is sampled when the Auto Harvest runtime is created,
so save the setting and fully restart the game before capturing. Ordinary operational logging does not need
to be enabled.

Enter a playable save and wait for the BepInEx log to report that capture is armed. The window closes after
the first attempted Auto Harvest action, an accepted gameplay-lifecycle change, or its finite event limit.
The log reports that close reason immediately, but closing is not proof that a file exists: keep the game
running until it reports either a committed artifact or an explicit capture/export failure.

Committed files use the machine-relative path
`BepInEx/config/OrbOfCreation-ModSuite/replay/auto-harvest/auto-harvest-NNNNNN.oscr`. Publication flushes the
file before an atomic rename, and startup retains the newest four artifacts. One artifact ends after its
first attempted action, so record fruit and treasure in separate game processes when both actions need
measurement. Decode a committed file from the repository with:

```bash
./script/trace --profile auto-harvest <artifact.oscr> [report.md]
```

Auto Buy defaults to Active with 100x affordability thresholds. Auto Cast, Auto Concept, and Auto Harvest default to Disabled. Auto Harvest queues quantity one, keeps at most one supported collect active, and runs only through the Common ServiceCycle engine. It may use the final free plot-action entry when the game's native capacity contract also reports room. When enabled, Auto Cast fully charges charge-capable spells by default; turn off `Auto Cast > Full charge` to fire them immediately. Auto Concept uses a 10% positive-rate reserve, 10% finite-resource quantity floor, and 0.95 native drain-ratio watchdog by default. Operational automation logging is off by default and should normally be enabled only for troubleshooting.

Back up saves before risky configuration changes and run only one automatic buyer. The complete scheduling, affordability, reserve, and queue-ownership contract is in the [Orb Automata reference](../../src/OrbAutomata/README.md).
