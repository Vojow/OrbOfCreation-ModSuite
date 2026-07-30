# Configuration and safety

[Back to documentation](../README.md) · [Installation](installation.md)

Open the in-game **Mods** tab to configure the suite. The tab itself is optional — `Interface.EnableButtonShell` turns it off — and every value stays editable in the BepInEx configuration file.

The suite is one plugin with one identity (`dev.vojow.orbofcreation.modsuite`), so BepInEx keeps all of it in one configuration file. That file carries a hidden `[OrbModding] ConfigurationSchemaVersion` marker and its current version is 6. Version 3 moved inherited shortcut defaults; version 4 removed Mentor's obsolete per-frame and CPU-budget controls; version 5 removes the retired scan cap, rejection cap, both global logging keys, Mentor detailed logging, mastery event probe, and verifier shortcut. Version 6 removes the four service cadence keys, both Auto Buy and Auto Concept UUID-list pairs, Auto Buy grouping, fixed-group, batch-sizing, fixed-batch and structure-priority keys, and the Auto Concept quantity cap. It also rewrites every serialized Auto Concept training period of 300 seconds — the former default — to 30 seconds, whether inherited or deliberately saved; every other training period is preserved. No schema after 5 shipped, so this is the sole 5→6 migration. Settings from the retired per-plugin files do not carry over. Before migration the transaction creates the first free sibling backup (`.pre-schema-v<version>.bak`, then `.bak.2`, and so on) and never overwrites one that exists. Malformed, negative, or newer unsupported schema versions stop the suite before it changes gameplay or opens its UI; do not lower the marker by hand. The Mods panel reports the schema state separately from both runtime health and the Apply result, and never displays the configuration path or saved values in that status.

Important controls:

The left rail contains Runtime, General, Auto Buy, Auto Cast, Auto Concept, Auto Harvest, Mentor, and Advanced. The old Safety, Spells, Artifacts, Alchemy, and duplicate feature-tab row is gone. Mentor's spell, artifact, alchemy, source, percentage, and economy policies live together on the Mentor page.

Each feature page begins with one status card and an immediate **Turn on/Turn off** command. That command and the matching gameplay quick button are the only two feature-mode controls in the UI, and both publish through the committed configuration store before returning. Mode rows are not repeated in the staged settings list. Disabled feature modes lock their tuning fields. Nested controls unlock only when all of their committed or staged prerequisites are selected; these UI dependencies do not change or erase saved values.

Apply, Revert, unsaved-state, and validation in the Mods page are scoped to the selected mod. If a quick button, hotkey, or file reload changes a value that is also staged, the row shows both values and requires an explicit **Keep mine** or **Take live** choice before Apply.

Keyboard shortcuts are parsed and validated while staged. Text-backed dependencies are re-evaluated when editing ends, so dependent rows update without rebuilding the page on every keystroke.

The Mods catalog is reused across ordinary refreshes and unchanged scene rebuilds. Late plugin/config-definition additions or removals invalidate it at the existing integrity check, rebuild it once, and restore navigation by stable plugin and section identity.

Runtime starts with a two-column summary of all six suite features; failures and attention states sort before waiting and healthy features. Recent events and differential verification follow, then full trace, optional profiling, pump timing, the decision journal, and detailed service cards. The Runtime footer reports whether the Mods refresh is pending and how long ago it last completed. Mods maintenance admits at most one pass per Unity frame and continues pending work on later frames.

The Auto Buy, Auto Cast, Auto Concept, Auto Harvest, and Mentor quick buttons and feature-card commands publish the saved value through the
same configuration store used by every other writer before the click returns, then render that committed
intent. The button decides its next value from committed state, not from a raw file-watcher notification
that the application has not accepted yet. Mods Apply and external edits join the same store at the start
of the next main-thread frame.

Runtime waiting, pauses, blockers, and failures remain a separate health axis in the button tooltip and
Mods Runtime page; a configured-On feature does not pretend to be Off merely because it cannot currently
run. Service diagnostics publish health only. One central presentation join combines it with committed
intent, so a late lifecycle, host-fault, or completed-cycle update cannot repaint a saved setting. The
suite renderer also exclusively owns each cloned button's configured-intent graphic; native hover, press,
release, and selection transitions cannot repaint it as a second state.

The native-sidebar safety control is always present in gameplay, including when the automation master switch is off or the worker host failed. **STOP ALL** engages the suite emergency stop with one click and discards prepared automation work. Desired-On quick controls then say `ON / STOPPED`. Clearing is deliberately two-step: click **STOPPED** to arm **RESUME?**, review the tooltip's exact desired-On service list, then click again. The Mods **General** page also exposes **Automation enabled**, so automation can be turned off and back on without editing the file; the Mods and safety surfaces remain installed while it is off.

After a game update produces a complete but unaudited assembly pair, the suite opens in compatibility quarantine instead of disappearing. Mods and **Run differential verification** remain available, while no Harmony patch, automation service, Mentor service, or feature quick control is installed. **General > Emergency disable** starts engaged. Clearing that switch is the player's explicit acknowledgement for the exact two-file hash pair: it records the pair and permits runtime composition at the player's own risk in the same action. The acknowledgement survives restarts only for that exact pair and automatically resets after either game assembly changes. **Advanced > Allow this unverified game build** remains available as a separate acknowledgement path when the player wants to permit composition but leave STOP engaged for a later two-click resume. Turning that acknowledgement off re-engages STOP immediately; restart the game to unload patches already installed during that session.

If the shared automation host cannot start, desired-On features report that runtime fault while desired-Off features remain Off. Configuration changes are retained while the host is absent. One automatic retry is made on the next eligible frame; after that bounded attempt, the fault remains visible instead of retrying indefinitely.

- `AutoBuy.Mode` and `AutoCast.Mode`: saved values are `Disabled` or `Active`; change them with the feature header or quick button.
- `AutoConcept.Mode`: `Disabled` (default) or `Active` for Scholar Active Concepts; change it with the feature header or quick button.
- `AutoHarvest.Mode`: `Disabled` (default) or `Active`; change it with the feature header or quick button. `CollectFruitTrees` and `CollectTreasureTrees` both default to true behind the disabled master switch.
- The world collector publishes a fresh immutable reading every 250 milliseconds. Auto Buy, spell leveling, Auto Cast, Auto Concept, and Auto Harvest evaluate after every world publication and configuration publication. There are no per-service cadence settings or fallback polls; training periods, manual-cast pauses, and fault backoffs remain explicit waits for their own semantics.
- `AutoConcept.SlotManagementMode`: `TimedCycle` (default) rotates through every unlocked concept after each assignment has received the complete configured settled-active period; the game decides whether releasing an assignment opens an appropriate typed or typeless slot. `RotateAll` replaces active concepts to train a same-type strictly lower-mastery concept; `PreserveManual` keeps concepts that were already active when automation started.
- `AutoConcept.ShowToggleButton`: show the `CN ON/OFF` configured-intent button in the native Auto Buy-anchored control strip; runtime health remains in its tooltip; default true.
- `AutoConcept.TrainingPeriodSeconds`: settled active time for one newly assigned concept; default 30, range 10 to 3600. Schema 6 rewrites every serialized 300-second value — the former default — to 30, including a value deliberately saved by a player; all other customized values are preserved. `RotateAll` and `PreserveManual` can resume earlier after mastery catch-up, while `TimedCycle` always waits for the full period.
- Auto Concept considers every discovered (unlocked) concept. While a settled training period is still running, its tooltip and Runtime status say that it is waiting. If training has finished but no other unlocked concept can be assigned, they show that progression-locked reason instead of treating a locked concept as a candidate. If the game refuses an unlocked replacement because its prospective slot or resource drain is unsafe, Auto Concept tries the next candidate against the same published world and reports a native-safety wait only when every replacement has been refused. A newer world or configuration publication makes those candidates eligible again; there is no retry timer or fallback poll. The decision journal and trace dashboard record the same idle reason, while `BepInEx/LogOutput.log` names each verified quantity delta or rejected rotation and its native reason.
- `AutoBuy.AutoLevelSpells`: enabled by default while Auto Buy is active. It detects native progression automatically: Locked, Single, then All after the completed level-all Upgrade. It spends the game's live spell-level costs and can be disabled separately.
- Structure and upgrade affordability modes are configured separately.
- Absolute and relative reserves protect selected resources.
- `LeaveQueueSlots` preserves queue room for manual actions.
- Auto Buy always fills the available queue while preserving `LeaveQueueSlots`. Structures use the live Bulk Development count; upgrades request one level. Candidates are ranked by cost ratio and stable UUID, without UUID filters or a structure-effect priority tier. Every submitted level is capped to live queue room and revalidated independently.
- Auto Concept always deepens an assignment to its native mastery maximum; there is no separate quantity cap.
- Auto Concept rate, quantity, and drain-ratio floors protect continuous resources; zero-resource replacements are skipped so they cannot starve other safe concepts in the cycle. Current concept quantities remain the rollback ownership baseline even when `RotateAll` permits assignment replacement.
- `Safety.EmergencyDisable`: General suite emergency stop. It immediately stops new automated purchases, casts, concept mutations, spell levels, harvest submissions, and mastery sharing. On a quarantined build, explicitly clearing it also accepts the exact observed assembly pair.
- `Compatibility.AllowUnverifiedGameBuild`: Advanced-only risk acknowledgement for the exact unaudited assembly pair currently installed. Default false; a changed pair resets it automatically.

Default input inventory:

- Auto Cast is polled once per Unity frame and defaults to `F8`; the central collision audit verifies that chord has no audited native default.
- Mentor is polled once per Unity frame and defaults to `Left Alt + M`. It intentionally remains configurable and the audit warns that its modifier is also the native More Info modifier.
- Differential verification has no key listener. Run it with **Run differential verification** on Mods -> Runtime.
- Auto Buy, Auto Concept, Auto Harvest, emergency stop, Mods navigation, and Runtime diagnostic actions are buttons, not global key listeners.

Auto Buy defaults to Active with 100x affordability thresholds. Auto Cast, Auto Concept, and Auto Harvest default to Disabled. Auto Harvest queues quantity one, keeps at most one supported collect active, and runs only through the Common ServiceCycle engine. It may use the final free plot-action entry when the game's native capacity contract also reports room. When enabled, Auto Cast fully charges charge-capable spells by default; turn off `Auto Cast > Full charge` to fire them immediately. Auto Concept uses a 10% positive-rate reserve, 10% finite-resource quantity floor, and 0.95 native drain-ratio watchdog by default. Warnings and errors are always emitted. Use the Runtime page's explicit trace, event, journal, and verification actions when deeper evidence is needed; there is no global detailed-logging mode.

Back up saves before risky configuration changes and run only one automatic buyer. The complete scheduling, affordability, reserve, and queue-ownership contract is in the [automation reference](../../src/Automata/README.md).
