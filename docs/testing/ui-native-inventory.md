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
| Main navigation | `Magic`, `Scholar`, `Time`, and `Mods` horizontal `ViewRadio` controls | Direct children of `Canvas/ContentArea/MainContentContainer/TopBar/ViewRadio`; these `UIViewRadioButton` instances populate `viewImage.sprite` and provide audited Time, Magic, Scholar, and Alchemy icons. | Keep the Mods entry as the outer level and reuse named top-bar icons for Runtime, General, Concept, and Advanced. |

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

The bottom spell bar is the reference button vocabulary. `UISpellButton` supplies its base and
icon image, background image, and `UIImageEffects`. The serialized main scene leaves
`insufficientBackground` null on both gameplay-strip buttons; `UISpellButton.RenderContent` only
uses that optional sprite when it exists. `Awake` captures the root Image as `background` and its
sprite as `baseBackground`, while the scene puts a real `UIImageEffects` component on each button.
The scene contains several `isForCasting` families: the gameplay strip's direct children live at
`Canvas/ContentArea/MainContentContainer/CastingBar/SmallSpellList/SpellButtonIconOnly{ (1)}`,
while the Magic view separately contains `ArcaneCasting/.../SpellList/SpellButton{ (1)}` and
`ArcaneSpellBook/.../BigSpellList/SpellButtonBigIconOnly{ (1)}`. Sampling is therefore scoped to
the audited direct-child gameplay-strip path and deterministically takes the first prototype with
the populated root Image, icon Image, and `baseBackground` that the game actually supplies. It
never requires the null optional insufficient sprite or compares unrelated spellbook and
casting-list frames. Suite states use icon color over the one native base frame.

Any failed rail or spell capture includes a census of every object of the audited component type:
full path, active state, loaded-scene membership, selector result, and a verdict for every sampled
field. A supported baseline can therefore fail loudly without another selector guessing loop.

The live mastery-level and mastery-XP marks are readable next to their numbers on a full Scholar
card, but their silhouettes collapse into similar small progression marks at quick-button size.
The runtime `SubviewRadio` items have no icon at all, so the Concept control uses the audited
`ScreenScholar` top-bar book glyph. Mentor uses the distinct mastery-XP glyph.

Evidence: `artifacts/ui-overhaul-evidence/native-mastery-icon-context.png`.

## Audited fields

The native-contract manifest declares eleven visual fields and four icon accessors:

- `UISpellButton`: `icon`, `insufficientBackground`, `isForCasting`, `background`,
  `baseBackground`, and `effects`.
- `UIViewRadioButton`: `viewText`, `viewImage`, `activeImage`, `buttonImage`, and `baseImage`.
- `GlobalVariables.GetGlobalStructureType`, `GlobalVariables.GetHarvestSpeedAttr`,
  `GlobalVariables.GetMasteryExpAttr`, and
  `TooltipableObject.GetIcon`.

All are capture-only UI contracts. Manifest schema 3 and the sole legacy allowlist entry
`spell.get-icon` remain unchanged.
