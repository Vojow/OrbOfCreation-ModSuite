# Orb Mod Config

Orb Mod Config is the optional in-game configuration surface for the mod suite and other loaded BepInEx plugins.

The current `0.6.3` build provides a simplified configuration UI:

- feature-oriented presentation groups independent of raw BepInEx sections;
- friendly setting names, hidden compatibility switches, dependency-aware controls, and apply indicators;
- automatic Steam keyboard input for text fields when running on Steam Deck;
- no hard Steamworks dependency, so desktop and non-Steam startup remain unchanged;
- live synchronization of clean, unstaged fields when native controls or shortcuts change them;
- staged multi-condition dependencies, so disabled modules and inactive subfeatures lock their tuning fields immediately while re-enable, safety, and diagnostic controls remain usable;
- a distinct transition-driven configuration-schema band for plugins that publish Common migration status, joined by exact plugin GUID and kept separate from runtime health;
- a distinct transition-driven runtime-status band for plugins that publish Common feature health, also joined by exact plugin GUID and kept separate from staged or saved configuration;
- variable-height setting rows that keep complete descriptions and saved-versus-runtime guidance readable;
- absolute same-page scroll retention across staged edits, defaults, Apply, Revert, and external refreshes;
- responsive row remeasurement after resolution, window-size, or UI-scale width changes;

The underlying editor continues to provide:

- discovers loaded plugins and their typed BepInEx configuration entries;
- groups entries deterministically by mod and presentation group, honoring optional public metadata;
- classifies the editor each setting will require;
- clones the last available native top-tab visuals into a Mods button that is available from the start and remains last;
- opens a mod-owned overlay panel and closes it from Mods or any native top-level tab;
- presents loaded mods as a horizontal tab row and simplified feature groups as a second tab row;
- renders a scrollable settings list with descriptions, ranges, current values, and defaults;
- edits booleans, enums, bounded/unbounded numbers, strings, and keyboard-shortcut serialization;
- stages all changes until Apply, supports per-setting Default and global Revert, validates before writing, and saves through each owning `ConfigFile`;
- rolls back already-written entries when an Apply operation throws;
- removes every owned object and native close listener on scene exit or plugin unload.

`0.3.1` also restores the Mods button's native inactive sprite when the panel is closed, instead of retaining the highlighted Time sprite copied during cloning.

`0.3.2` makes Mods participate in the top-level navigation state: opening it temporarily deactivates the selected native tab, toggling Mods closed restores that tab, and choosing another native tab closes Mods without restoring the prior selection.

`0.5.2` keeps all underlying BepInEx section/key names compatible while allowing plugins to supply UI-only grouping, labels, dependencies, and restart metadata. It also makes the Mods tab available before NG+, keeps it last in the native navigation row, and refreshes unstaged values when native controls or shortcuts change them.

`0.5.3` retries installation while slower UI hierarchies finish loading. Shell liveness includes both the button and its ScreenContent panel; losing either host or failing to open/close the panel restores the prior native view (or another surviving native view), detaches the old shell listeners, and schedules a clean reinstall. Loaded-plugin catalog discovery and logging, UI installation, repair, native-tab event maintenance, and the five-second integrity check run only when due through the shared cooperative frame budget. The catalog is enumerated and logged once, after the first admitted installation lease. Budget denial retains pending work without enumerating plugins, logging the catalog, scanning the scene, or rebinding listeners; disabled and non-gameplay scenes remain idle, and scene exit or unload unregisters the work and removes owned listeners.

`0.6.0` lets one setting require multiple staged values and evaluates all requirements without writing configuration early. Enum changes rebuild the current settings rows immediately, matching the existing boolean behavior, so enabling or disabling a module updates its dependent editors in the same interaction.

`0.6.1` consumes the suite's shared lifecycle generation so scene recreation and late plugin initialization use the same idempotent readiness boundary as Automata and Mentor.

`0.6.2` sizes each setting row from its rendered description, preserves the current absolute scroll offset when the same page rebuilds, and remeasures rows when the available content width changes. Selecting another mod or feature section still begins at the top.

`0.6.3` claims schema version 1 before binding its own settings and adds a separate status band for the selected plugin's exact-GUID schema result: current, migrated, failed, future, saved, loaded, and whether a backup was created. Its status callback only marks an atomic latch, leaving Unity text access to the normal UI tick. Failed or future Automata/Mentor instances with no visible bound settings remain selectable as read-only status-only tabs with no Apply action; unreported empty third-party plugins remain omitted. It never exposes a configuration path or serialized value. This status remains independent from feature runtime health and from the Apply footer; Mod Config's own schema failure is logged before the UI starts.

A fully successful Apply now publishes exact plugin GUID plus source section/key invalidations through Common's bounded completed-frame bus. Validation, save, or `SettingChanged` rollback publishes nothing. The existing 0.1-second clean-field polling remains the compatibility path for native controls and third-party plugins, and staged edits retain their conflict behavior.

The next-beta health pass stops treating a successful config save as proof that runtime behavior applied immediately. The footer reports configuration transaction results, while the separate runtime band shows each selected plugin's published feature state and structured reason. Plugins that do not publish health remain supported and are shown as not reporting runtime status.

Set `[Interface] EnableButtonShell = false` as an emergency off switch. Unsupported custom setting types remain read-only. Closing the panel preserves staged values for the current scene; Revert explicitly discards them.
