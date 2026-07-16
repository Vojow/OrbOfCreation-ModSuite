# Orb Mod Config

Orb Mod Config is the planned in-game configuration surface for the mod suite and other loaded BepInEx plugins.

The current `0.5.2` build provides a simplified configuration UI:

- feature-oriented presentation groups independent of raw BepInEx sections;
- friendly setting names, hidden compatibility switches, dependency-aware controls, and apply indicators;
- automatic Steam keyboard input for text fields when running on Steam Deck;
- no hard Steamworks dependency, so desktop and non-Steam startup remain unchanged;

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

`0.5.2` keeps all underlying BepInEx section/key names compatible while allowing plugins to supply UI-only grouping, labels, dependencies, and restart metadata. It also makes the Mods tab available before NG+, keeps it last in the native navigation row, and retries installation while slower UI hierarchies finish loading. Shell liveness includes both the button and its ScreenContent panel; losing either host or failing to open the panel restores the prior native view when possible and schedules a clean reinstall.

Set `[Interface] EnableButtonShell = false` as an emergency off switch. Unsupported custom setting types remain read-only. Closing the panel preserves staged values for the current scene; Revert explicitly discards them.
