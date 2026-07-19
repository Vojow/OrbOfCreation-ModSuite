# In-game mod configuration UI plan

> **Lifecycle: Implemented / evolving.** The optional configuration UI is in public beta; this plan includes later improvements.

[Back to project index](../README.md) · [Project roadmap](roadmap.md)

## Goal

Add a **Mods** entry to the game's main navigation from a new game and keep it last among the currently available top-level tabs. The entry opens a mod-owned Unity UI panel rather than trying to express configuration through the game's existing content panels.

The panel provides one page per loaded mod and category navigation based on each mod's BepInEx configuration sections. BepInEx `ConfigEntry` values and their `.cfg` files remain the authoritative configuration store.

## Product scope

The target product scope supports:

- automatic discovery of loaded BepInEx plugins and their configuration entries;
- a top-level Mods navigation button that follows the main-tab layout;
- a standalone, scrollable configuration panel;
- mod selection followed by category/section selection;
- toggles, bounded numeric sliders with exact-value input, enum dropdowns, strings, and keyboard shortcuts;
- descriptions, acceptable ranges, current/default values, validation feedback, and reset-to-default;
- explicit Apply and Revert actions;
- synchronization when a config file is reloaded or an entry changes outside the panel;
- a visible restart-required marker for settings that cannot safely take effect immediately;
- keyboard/controller navigation, readable focus state, and UI scaling consistent with the game.

The UI does not provide a general-purpose game UI framework, edit arbitrary non-BepInEx data, or infer safe live-reload behavior when a mod has not declared it.

## User experience

```text
Main tabs:  ...  <currently unlocked native tabs> | Mods

Mods panel
├─ Mod list: Automata | Mentor | ...
├─ Categories: General | Auto Buy | Auto Concept | Sharing | Diagnostics | ...
└─ Settings
   ├─ label and description
   ├─ type-appropriate editor
   ├─ validation/default/restart state
   └─ Apply | Revert | Reset category
```

Selecting Mods hides the current game content panel and shows the mod-owned panel. Leaving Mods restores normal game navigation without changing the selected native view. The current implementation preserves staged edits for the scene; Revert explicitly discards them. An unsaved-changes prompt remains optional follow-up work.

## Architecture

The feature ships as the separate `OrbModConfig` BepInEx plugin with four boundaries:

1. **Navigation host** discovers the main-tab container after the `Main` scene is ready, clones only the minimum button styling needed, keeps Mods last among available native tabs, and removes its objects on scene exit or plugin unload.
2. **Panel host** owns a separate Unity canvas subtree and all configuration controls. It does not create or register a synthetic `ViewSO` in the game's serialized registries.
3. **Configuration catalog** reads loaded plugins from BepInEx and projects their `ConfigFile` entries into mod, section, and setting view models.
4. **Editor adapters** map supported setting types to controls and perform parsing, validation, staging, and commit.

The catalog requires no changes or binary dependency in participating mods. Supported suite plugins may provide presentation groups, labels, ordering, visibility, staged dependencies, and restart metadata through `OrbModding.Common`; missing metadata always falls back to plugin name, config section/key, `ConfigDescription`, acceptable values, default value, and the entry's declared type.

## Configuration and transaction model

Opening a setting copies its current serialized value into a staged editor value. Editing controls changes only staged state. Apply validates the affected settings, writes their `ConfigEntry.BoxedValue` values, and calls `ConfigFile.Save()`. Revert discards staged values and reloads the current entries.

This transaction boundary avoids partially applying a page when one field is invalid. A failed write keeps the panel open, reports the affected setting, and leaves unrelated configuration unchanged where rollback is possible. Config files are never edited as raw text by the UI.

External `SettingChanged` events refresh clean controls immediately. If a setting has an unsaved local edit, the panel reports the conflict instead of silently overwriting either value.

## Control mapping

| Configuration shape | Editor |
|---|---|
| `bool` | Toggle |
| Enum | Dropdown |
| Bounded `int`, `float`, or `double` | Slider plus exact numeric input |
| Unbounded numeric value | Numeric input with validation |
| `KeyboardShortcut` | Keybinding capture and clear button |
| `string` | Text input; multiline for declared list-like settings |
| Unsupported/custom type | Read-only serialized value in v1 |

Sliders are an editing convenience, never the only way to enter a numeric value. Numeric controls must preserve the configuration type and must not silently round an exact typed value to the slider's visual step.

## Metadata and live-apply policy

BepInEx supplies section, key, description, acceptable values, default value, and setting type. Additional metadata should eventually support:

- display name and ordering;
- advanced or hidden status;
- multiline/list editing hints;
- restart required;
- sensitive-value masking;
- custom validation and control choice.

Until that contract exists, settings are assumed to be saved immediately but not guaranteed to take effect immediately. Existing mods that subscribe to `SettingChanged` may react live. The UI must distinguish **saved** from **applied at runtime** and must not promise live behavior it cannot verify.

## Runtime integration risks

- **Tab hierarchy changes:** locate the main navigation by component/type relationships and verified anchors such as the Time tab, not by a single absolute scene path.
- **Scene recreation:** make initialization idempotent and destroy all owned objects on teardown.
- **Layout pressure:** verify the additional main button at every supported resolution and UI scale; allow compact labeling if required.
- **Game input leakage:** while a text field, slider, dropdown, or keybinding capture owns focus, suppress relevant gameplay shortcuts without globally disabling input.
- **Paused or accelerated time:** animate and process the panel with unscaled time so game-speed changes do not affect usability.
- **Other UI mods:** modify the smallest possible hierarchy, use unique object names, and tolerate the navigation host already being extended.
- **Config callbacks:** isolate exceptions raised by a mod's `SettingChanged` subscriber and report them without breaking the panel.

## Delivery stages

### Current implementation status (0.6.1)

- Added the standalone `OrbModConfig` BepInEx plugin project.
- Added deterministic discovery and grouping of loaded plugins, configuration sections, and entries.
- Added editor-shape classification for booleans, enums, bounded and unbounded numbers, strings, keyboard shortcuts, and unsupported custom types.
- Added a non-mutating runtime probe for `CoreViewManager`, `ViewManager`, `UIViewRadio`, `UIViewRadioButton`, and `ManagedView` objects in the `Main` scene.
- Added portable catalog tests and installed-game metadata contracts for the known navigation types.

Version `0.1.0` logged the catalog and UI hierarchy without modifying the interface. Its runtime evidence was used to proceed to the reversible M0 shell; configuration values were still read-only in that build.

Runtime evidence on 2026-07-14 found the exact main row at `Canvas/ContentArea/MainContentContainer/TopBar/ViewRadio`, using `ViewRadioButtonLong(Clone)`, and confirmed the Workshop, Alchemy, and Time anchors. Version `0.2.0` therefore adds the reversible M0 button-and-empty-panel shell. It clones Time for visual compatibility, disables and removes the cloned game view component, clears inherited click listeners, and owns a separate overlay under `ScreenContent`. No synthetic `ViewSO` is registered. `[Interface] EnableButtonShell=false` removes or prevents the shell without disabling catalog discovery.

The `0.2.0` runtime session confirmed correct placement after Time, repeated open/close behavior, seven native close bindings, and clean teardown. Version `0.3.0` completed the configuration MVP in one iteration: loaded mods are horizontal tabs, config sections are a second tab row, and settings are rendered in a scrollable list. Boolean and enum editors use buttons; numeric, string, and keyboard-shortcut serialization use exact text input. Values remain staged until Apply, with type/range validation, per-setting Default, global Revert, config-file saving, and best-effort rollback after write failures. Unsupported custom types remain read-only. Version `0.3.1` restored the native inactive top-tab sprite; `0.3.2` integrated Mods with native top-level activation. Version `0.4.0` adds explicit public section/setting ordering, hides inert legacy settings, removes the completed navigation probe from runtime, and stops routine UI interaction logging.

Version `0.6.0` adds AND-composed staged dependencies while retaining the original single-dependency metadata contract. Disabled feature modes lock their tuning rows, nested fields require every applicable switch or policy, and enum edits rebuild the visible rows immediately. Re-enable, shortcut, status-button, safety, and diagnostic controls remain available by policy.

Version `0.6.1` adopts the shared lifecycle monitor and generation used by the gameplay plugins. Repeated observations of the same live scene are idempotent, while actual scene recreation disposes stale UI objects and begins a new generation.

The next-beta health pass adds a separate runtime-status band. It joins Common feature snapshots to the selected catalog entry by exact plugin GUID, refreshes only after a status transition, and never changes configuration or dependencies from runtime state. Apply confirms only that configuration was saved; runtime application is reported independently when the plugin supports the shared contract.

### M0 — Runtime UI probe

- Record the main navigation hierarchy, button components, layout group, selection behavior, fonts, colors, and panel activation lifecycle.
- Verify stable anchors for Workshop, Alchemy, and Time at runtime.
- Add and remove a non-functional Mods button without disturbing native tab navigation.

Exit criterion: the button survives scene reloads, resolution changes, and repeated enable/disable cycles with no duplicate objects.

### M1 — Panel shell

- Open and close a standalone panel from the Mods button.
- Implement mod list, category list, scroll area, focus management, and native-view restoration.
- Use unscaled time and block input leakage while editing.

Exit criterion: an empty/sample panel behaves correctly with mouse and keyboard at supported resolutions.

### M2 — Read-only catalog

- Discover loaded BepInEx plugins and config entries.
- Group entries by plugin and section.
- Display descriptions, current/default values, acceptable ranges, and unsupported types.

Exit criterion: Automata, Mentor, and other supported configurable plugins appear without modifications to those plugins.

### M3 — Safe editing

- Implement control adapters, staged values, validation, Apply, Revert, and reset-to-default.
- Save through `ConfigFile` and handle external changes and callback exceptions.
- Add unsaved-change and restart/live-status messaging.

Exit criterion: every supported entry type round-trips without type loss, invalid values cannot be committed, and config files remain loadable after forced error cases.

### M4 — Integration and polish

- Add optional ordering/presentation metadata only if automatic discovery proves insufficient.
- Complete controller support, tooltip/description presentation, compact layout, and accessibility checks.
- Test clean BepInEx, each supported project plugin independently, and the project mod suite.

Exit criterion: the compatibility matrix passes and removing `OrbModConfig` leaves all participating mods fully usable through their normal `.cfg` files.

## Verification matrix

Required scenarios include:

- fresh install, existing configs, malformed external value, and read-only config file;
- `Main` scene entry/exit, save/load, resolution change, fullscreen toggle, and UI-scale change;
- mouse, keyboard-only, and controller navigation;
- normal and accelerated game-speed modes;
- Apply, Revert, category reset, external reload, and a throwing `SettingChanged` subscriber;
- zero configurable mods, one mod, all suite mods, and an unrelated third-party plugin;
- duplicate plugin display names and duplicate section/key labels across different plugins;
- uninstalling the UI plugin without modifying or invalidating any mod configuration.

## Open runtime questions

The M0 probe must answer these before production UI work:

1. Which component owns main-tab selection and native panel activation?
2. Is the Time button a safe clone source, or should its visual children be copied into a new button component?
3. Does the tab row use a layout group that accepts another child at all supported aspect ratios?
4. Which input-manager state or modal mechanism should suppress gameplay actions while editing?
5. Which font/material assets can be referenced safely without retaining scene objects across unload?
