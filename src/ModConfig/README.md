# Mod Config UI

This folder is the in-game configuration and runtime-diagnostics surface of the suite. It is not a separate plugin and carries no version of its own; everything here compiles into `OrbModSuite.dll` and loads under the suite's single plugin GUID.

The page edits the suite's own settings and, because it discovers loaded plugins generically, the typed settings of other BepInEx plugins installed beside it.

## Settings editor

- Discovers loaded plugins and typed BepInEx configuration entries.
- Groups settings by mod and feature while preserving the original section/key contract.
- Supports booleans, enums, bounded and unbounded numbers, strings, and keyboard shortcuts.
- Stages edits until Apply, supports per-setting Default and global Revert, and rolls back earlier writes if Apply fails.
- Honors optional presentation metadata for labels, dependencies, restart guidance, and hidden compatibility keys.
- Keeps unstaged fields synchronized with external changes.
- Preserves same-page scroll position and remeasures variable-height rows when the available width changes.
- Removes its owned Unity objects and listeners on scene exit or plugin unload.

The Mods button is cloned from native top-level navigation, remains last, and opens a mod-owned overlay rather than modifying native content panels. `[Interface] EnableButtonShell = false` disables that integration if the game UI changes incompatibly.

## Runtime page

Runtime is separate from staged configuration. It joins evidence by exact plugin GUID and service ID and does not treat a successful config save as proof that behavior applied immediately.

It presents:

- configuration-schema and feature-health status;
- per-service capability state and current reason;
- latest scheduling and cycle evidence;
- explicit start/stop controls for manual full traces;
- start/stop controls for profiling builds;
- read-only rolling decision-journal health; and
- a bounded 1,200-frame ServiceCycle pump chart rendered directly into the available plot as one exact-frame
  mesh, without paging or creating one Unity object per frame.

The page receives neutral status and command ports only. It has no pump, trace buffer, writer, storage adapter, or filesystem authority. Cards update through the existing open-page cadence, and pre-created chart bars are reused rather than allocated per sample.

## Configuration behavior

A successful Apply publishes exact plugin GUID plus section/key invalidations through Common. Validation failure, save failure, or rollback publishes nothing. A failed or future suite schema remains selectable as a read-only status-only tab without exposing configuration paths or serialized values.

Unsupported custom setting types remain read-only. Closing the panel preserves staged values for the current scene; Revert explicitly discards them.
