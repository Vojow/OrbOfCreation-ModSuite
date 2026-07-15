# Orb Mod Config

Orb Mod Config is the planned in-game configuration surface for the mod suite and other loaded BepInEx plugins.

The current `0.4.0` build provides the publication-oriented configuration MVP:

- discovers loaded plugins and their typed BepInEx configuration entries;
- groups entries deterministically by mod and section, honoring optional public order/visibility metadata;
- classifies the editor each setting will require;
- clones the native Time button visuals into a Mods button immediately after Time;
- opens a mod-owned overlay panel and closes it from Mods or any native top-level tab;
- presents loaded mods as a horizontal tab row and BepInEx sections as a second tab row;
- renders a scrollable settings list with descriptions, ranges, current values, and defaults;
- edits booleans, enums, bounded/unbounded numbers, strings, and keyboard-shortcut serialization;
- stages all changes until Apply, supports per-setting Default and global Revert, validates before writing, and saves through each owning `ConfigFile`;
- rolls back already-written entries when an Apply operation throws;
- removes every owned object and native close listener on scene exit or plugin unload.

`0.3.1` also restores the Mods button's native inactive sprite when the panel is closed, instead of retaining the highlighted Time sprite copied during cloning.

`0.3.2` makes Mods participate in the top-level navigation state: opening it temporarily deactivates the selected native tab, toggling Mods closed restores that tab, and choosing another native tab closes Mods without restoring the prior selection.

`0.4.0` adds explicit section/setting ordering and hidden legacy-setting metadata. The completed navigation probe is no longer run or exposed in public configuration, and routine open/close/apply events no longer write log chatter.

Set `[Interface] EnableButtonShell = false` as an emergency off switch. Unsupported custom setting types remain read-only. Closing the panel preserves staged values for the current scene; Revert explicitly discards them.
