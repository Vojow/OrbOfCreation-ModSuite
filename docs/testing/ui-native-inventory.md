# Native UI inventory for the suite overhaul

[Testing hub](README.md) · [Mod Config tests](mod-config.md)

This inventory was captured from the running Orb of Creation 1.0.5 UI before the suite shell was
restyled. The PNG evidence is intentionally an untracked local validation artifact under
`artifacts/ui-overhaul-evidence/`; the final implementation report embeds it.

## Nested navigation vocabulary

| Native surface | Observed second level | Runtime component or primitive | Decision |
|---|---|---|---|
| Magic and Scholar | A second `SubviewRadio` level inside the active screen; live list items use the native subview frame and text vocabulary | Runtime `UIViewRadioButton` items are direct children of `Canvas/ContentArea/MainContentContainer/SubviewRadio`. Their root owns `buttonImage`; `baseImage` and `activeImage` are populated; `viewImage` is null. | Reuse the native subview frame pair as a vertical suite rail, with exact native icons rather than pretending the source list items contain icons. |
| Settings | `Main`, `Game`, and `Graphics` horizontal text tabs inside a native framed modal | Native text-tab and framed-panel vocabulary | Retain as evidence that horizontal text tabs are native, but do not use them for nine suite sections. |
| Main navigation | `Magic`, `Scholar`, `Time`, and `Mods` horizontal `ViewRadio` controls | Direct children of `Canvas/ContentArea/MainContentContainer/TopBar/ViewRadio`; these `UIViewRadioButton` instances populate `viewImage.sprite` and provide audited Time, Magic, Scholar, Alchemy, World, and Workshop icons. | Keep the Mods entry as the outer level and reuse named top-bar icons for Runtime, General, Concept, Advanced, Auto Items, and Auto Scribe. |
| Help buttons | Gear and character buttons in one top-left group | The declared scene-bound `UIContentArea.canvas` field resolves the exact direct child `Canvas/HelpButtons`, whose direct `SettingsButton` and `PlayerStatsButton` children prove the expected anchor structure. | Parent exactly two closed suite buttons below the native pair: emergency stop and a disclosure whose transient feature drawer opens to the right. No exact anchor means no controls. |

Evidence:

- `artifacts/ui-overhaul-evidence/native-magic-secondary-tabs.png`
- `artifacts/ui-overhaul-evidence/native-settings-nested-tabs.png`
- `artifacts/ui-overhaul-evidence/native-vertical-icon-rail.png`

The chosen feature-rail/detail-pane shape uses the game's two-level view vocabulary without
claiming that the source hierarchy is itself vertical. The suite stacks cloned
`ViewNoIconRadioButtonSub(Clone)` frames vertically and supplies exact native icons. Sampling is
exact and fail-closed: frame sources must be direct `UIViewRadioButton` children of
`Canvas/ContentArea/MainContentContainer/SubviewRadio`, and every matching prototype must agree on
both frame sprites. `Resources.FindObjectsOfTypeAll` deliberately includes them while their source
views are inactive: Mods is the active view when the panel is constructed, so requiring
`activeInHierarchy` would make capture impossible in production.

Live census proved that these runtime list items exist while inactive and are not destroyed on view
close. It also proved their real field population: owned `buttonImage`, populated `baseImage` and
`activeImage`, and null `viewImage`. The named top-bar icon instances are direct children of
`Canvas/ContentArea/MainContentContainer/TopBar/ViewRadio`; they are created shortly after Main
loads, so an initial absence is startup timing and receives a bounded retry. Once present, inactive
objects such as `ScreenAlchemy` remain valid sources.

## Quick-button frame and icon comparison

The two-button shell and its transient feature drawer speak the same selected/unselected vocabulary as the native
`UIViewRadioButton` family used by the Mods rail. The populated `baseImage` is the dim,
recessed/hollow OFF frame; `activeImage` is the solid, raised ON frame. The suite creates each
button and icon itself, releases `Selectable.targetGraphic`, and swaps the audited frame sprite
before applying its own gray, green, red, or orange glyph color. Thus configured intent cannot be
color-only. The disclosure uses recessed/raised frames plus different closed/open chevrons, and a
contained fault or block adds a separate exclamation marker plus red color while closed.

Frame capture requires every exact direct-child SubviewRadio candidate to own `buttonImage` and
agree on both sprites. A failed capture includes the typed candidate census. A control is created
only after the pair and its audited feature sprite are available, so a missing primitive cannot
leave a clickable stateless object. The emergency stop uses a separately spaced suite-owned
exclamation glyph, not a feature sprite.

The live mastery-level and mastery-XP marks are readable next to their numbers on a full Scholar
card, but their silhouettes collapse into similar small progression marks at quick-button size.
The runtime `SubviewRadio` items have no icon at all, so central feature icon resolution uses the
audited `ScreenScholar` top-bar book for Auto Concept, `ScreenWorld` for Auto Items, and
`ScreenWorkshop` for Auto Scribe. Advanced keeps `ScreenAlchemy`; the rail rejects any duplicate
page sprite. Auto Buy, Auto Harvest, and Mentor retain their distinct audited native tooltipable
glyphs. Auto Cast uses the static audited Casting Speed attribute glyph from
`GlobalVariables.GetCastingSpeedAttr().GetIcon()`. The same resolver serves each feature's rail
entry and quick control, so neither surface depends on the equipped-spell loadout.

Evidence: `artifacts/ui-overhaul-evidence/native-mastery-icon-context.png`.

## Audited fields

The native-contract manifest declares six visual/anchor fields and five icon accessors:

- `UIContentArea`: `canvas`.
- `UIViewRadioButton`: `viewText`, `viewImage`, `activeImage`, `buttonImage`, and `baseImage`.
- `GlobalVariables.GetGlobalStructureType`, `GlobalVariables.GetCastingSpeedAttr`,
  `GlobalVariables.GetHarvestSpeedAttr`,
  `GlobalVariables.GetMasteryExpAttr`, and
  `TooltipableObject.GetIcon`.

The Auto Items temporary-item picker reuses the captured subview-radio base/active frame pair and
the already-declared `TooltipableObject.GetIcon()` capture for each discovered item. Its one added
capture contract is `TooltipableObject.GetName()` in the picker boundary; identity, discovery,
family, registry, and stock reads reuse the existing world-capture contracts. The picker creates
its own row and `Image` objects and never captures a native UI object.

The installed gate also pins the inheritance that makes those accessor results tooltipable
(`AttributeSO -> TooltipableObject` and
`StructureTypeSO -> UpgradeableObject -> TooltipableObject`) plus Unity's UI-construction
contract: `GameObject.transform` is declared as `Transform`,
`RectTransform` derives from it, and `GameObject(string, Type[])` is the constructor used to
request `RectTransform` for every suite-created UI node.

All are capture-only UI contracts. Manifest schema 3 remains unchanged.
