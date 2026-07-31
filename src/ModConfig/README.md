# Mod Config UI

This folder is the in-game configuration and runtime-diagnostics surface of the suite. It is not a separate plugin and carries no version of its own; everything here compiles into `OrbModSuite.dll` and loads under the suite's single plugin GUID.

The page edits the suite's own settings and, because it discovers loaded plugins generically, the typed settings of other BepInEx plugins installed beside it. The registered plugin title is displayed verbatim; the UI does not shorten or rewrite it.

## Settings editor

- Discovers loaded plugins and typed BepInEx configuration entries.
- Groups settings into Runtime, General, seven automation feature pages, and Advanced while preserving the original section/key contract.
- Renders feature mode once in the page header as an immediate committed-store command; mode is not repeated in the staged setting list.
- Supports booleans, enums, bounded and unbounded numbers, strings, and keyboard shortcuts.
- Replaces the Auto Items temporary allowlist's generic string editor with a whitelist-only picker
  of discovered native items while retaining the same staged string setting and Apply/Revert path.
- Stages edits until Apply, supports per-setting Default and selected-mod Revert, and rolls back earlier writes if Apply fails.
- Honors optional presentation metadata for labels, dependencies, restart guidance, and hidden compatibility keys.
- Keeps unstaged fields synchronized with external changes.
- Detects a staged/live conflict and requires **Keep mine** or **Take live** before Apply.
- Generation-stamps catalog definitions, preserving navigation across one rebuild when plugins or entries change.
- Preserves same-page scroll position and remeasures variable-height rows when the available width changes.
- Removes its owned Unity objects and listeners on scene exit or plugin unload.

The Mods button is cloned from native top-level navigation, remains last, and opens a mod-owned overlay rather than modifying native content panels. `[Interface] EnableButtonShell = false` disables that integration if the game UI changes incompatibly.

Restyled suite controls sample the audited native `UIViewRadioButton` subview frame pair. OFF uses
`baseImage`; configured ON uses `activeImage`, so intent differs structurally before the
suite-owned gray/green/red/orange icon color is applied. Quick controls close to an emergency-stop
button and one disclosure under `Canvas/HelpButtons`. The disclosure opens the seven registered
feature controls to the right; when closed, faults and blocks add an exclamation marker as well as
red color. General invokes the same immediate STOP/resume command instead of staging
`EmergencyDisable`. The anchor is resolved through the declared scene-bound `UIContentArea.canvas`
contract. Missing required types, fields, sprites, or structural paths are
suite defects on the matching audited baseline: installation retries bounded startup timing, then
logs the exact terminal reason and a typed-candidate census at error level and publishes it to
Runtime. There is no cloned native toggle, text quick-control, or alternate shell fallback. See the
[native UI inventory](../../docs/testing/ui-native-inventory.md).

Suite-owned UI objects request `RectTransform` explicitly from the native
`GameObject(string, Type[])` constructor. Both the quick-control surface and Mods rail use the same
five-second retry cadence and three-attempt waiting-to-terminal diagnostic policy; the first
failure logs that installation will retry, and type failures name the member plus expected and
actual managed types.

The Mods shell uses the subview-radio sample for a left-hand navigation rail,
active and inactive page frames, the outer detail frame, settings controls, and footer controls.
The cloned native behavior is removed from Mods navigation, and suite buttons have no
`Selectable.targetGraphic`; suite rendering remains the only pixel writer.
Native-skinned suite panel frames remain raycast targets: the settings viewport therefore routes
wheel input to its own `ScrollRect` across rows, text, gutters, and padding, while the quick-control
drawer frame owns its complete rectangle so input cannot pass through its grid gaps to native UI.
Decorative text, icons, and inset fills remain non-raycasting.

The Auto Items picker uses those same captured base/active frames for raised/recessed approval
rows. Its suite-owned images display icon sprites captured from the item's audited
`TooltipableObject.GetIcon()` contract; no live native UI object is retained, cloned, or mutated.
Item identity, discovery visibility, family, name, and stock are validated as one complete read
before a healthy catalog is rendered. A missing contract, invalid item, or read exception produces
the picker's explicit failure panel instead of an empty list or a fallback label/icon.

At installation, the BepInEx log reports exactly:

- `Quick controls: native state frames and icons active`
- `Mods rail: native visuals active`

A terminal failure replaces the corresponding success line with
`Quick controls: native state frames or icons failed: <reason>` or
`Mods rail: native visuals failed: <reason>`. Both surfaces are also registered as Suite UI
capabilities on Runtime.

## Runtime page

Runtime is separate from staged configuration. It joins evidence by exact plugin GUID and service ID and does not treat a successful config save as proof that behavior applied immediately.

It presents:

- a compact feature-health grid with failures and attention states first;
- configuration-schema and feature-health status;
- per-service capability state and current reason;
- latest scheduling and cycle evidence;
- explicit start/stop controls for manual full traces;
- start/stop controls for profiling builds;
- read-only rolling decision-journal health; and
- a bounded 1,200-frame ServiceCycle pump chart rendered directly into the available plot as one exact-frame
  mesh, without paging or creating one Unity object per frame.

The page receives neutral status and command ports only. It has no pump, trace buffer, writer, storage adapter, or filesystem authority. The health grid is a presentation over the same joined feature snapshots used elsewhere; detailed plugin cards remain sorted by severity. Cards update through the existing open-page cadence. Pending refresh work has a hard 30-frame admission bound and the Runtime footer exposes pending and last-refresh age. Pre-created chart bars are reused rather than allocated per sample.

## Configuration behavior

A successful Apply publishes exact plugin GUID plus section/key invalidations through Common. Validation failure, save failure, or rollback publishes nothing. A failed or future suite schema remains selectable as a read-only status-only tab without exposing configuration paths or serialized values.

Unsupported custom setting types remain read-only. Closing the panel preserves staged values for the current scene; Revert explicitly discards only the selected mod's staged values. The Mods shell and General safety settings remain available when `General.Enabled` is false.
