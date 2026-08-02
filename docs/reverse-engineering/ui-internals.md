# UI internals

[Back to index](README.md)

The UI layer is where most "that should have worked" bugs come from: it builds itself lazily,
publishes no completion signal, keeps objects alive that look destroyed, and in at least one case
owns a composite operation that has no data-layer equivalent.

The player-visible symptoms of all this are listed in
[`game-systems/ui-behaviours.md`](../game-systems/ui-behaviours.md). This page is the mechanism.

## Nothing is built when the scene loads

Screen contents are constructed on first render, several seconds after scene entry:

```text
UIRenderGroupElement.Update
  → DelayedRegisterStart()          // one-shot virtual
    → UIGenericPlainList override → EnsureRendered()
      → RenderChildren()            // instantiates each button
        → UIGenericItem.Setup() / UIViewRadioButton.RenderContent()
          → populates the item's ViewSO and viewImage.sprite, synchronously
```

The game publishes **no event** when that finishes. Observed birth of the top-bar icons is roughly
two to four seconds after scene entry, and the exact delay is machine- and load-dependent — it is
not a fixed frame count you can wait out. Anything that needs a constructed UI object has to poll
for it on a bounded window and treat a partial set as still-loading rather than as a failure.

## Constructed objects outlive their screen

Runtime list items are **not destroyed when their view closes**. They persist inactive, which has
two consequences:

- `Resources.FindObjectsOfTypeAll` finds them while their source view is inactive. Requiring
  `activeInHierarchy` makes capture impossible whenever the screen you want is not the screen the
  player is on.
- An object you captured from a closed screen stays valid. Staleness here comes from lifecycle
  boundaries, not from view changes.

## View switching latches, it does not toggle

`UIViewRadio.PostSetupItem` wires each button to `SwapToView`, and
`ViewListVariable.SwapToView` writes the selected `ViewSO` active and every sibling inactive.
Selecting the already-active tab therefore re-selects it — there is no close gesture and no "off"
state to drive. Anything modelling the UI as a toggle will be wrong about exactly one case.

## Hierarchy anchors and field population

`UIContentArea.canvas` is a declared scene-bound field, so it resolves the exact `Canvas` root
rather than a search. Below it:

| Path | Contents |
|---|---|
| `Canvas/ContentArea/MainContentContainer/TopBar/ViewRadio` | the main `UIViewRadioButton` screens |
| `Canvas/ContentArea/MainContentContainer/SubviewRadio` | the second-level `UIViewRadioButton` items inside the active screen |
| `Canvas/HelpButtons` | `SettingsButton` and `PlayerStatsButton` |

The two `UIViewRadioButton` populations do not have the same fields filled. Top-bar items populate
`viewImage.sprite`; `SubviewRadio` items own `buttonImage`, have `baseImage` and `activeImage`
populated, and leave `viewImage` **null**. Sampling either population without checking which one
you have produces null icons. `baseImage` is the recessed/hollow unselected frame and `activeImage`
is the solid raised selected frame.

Named loaded sprites are reachable directly — the emergency-stop glyph `power-lightning` is a
loaded `UnityEngine.Sprite` audited in `sharedassets0.assets`. Match on the exact name and require
exactly one result; zero and duplicates are both defects, not fallbacks.

## UI-only composites

Some operations exist **only** as a UI event handler, and the data layer has no equivalent entry
point. Crafting is the clearest case:

```text
UICraftingPage.QueueCraft(recipe, quantity)
  → recipe.PurchaseQuantity(quantity, existingQuantity)
  → existing stack.AddQuantity(quantity)   or
    new CraftingInstance(recipe, quantity)
      → Initiate()
      → CheckInstantCraft() → InstantCraft()  or  ActiveScribeInstances.Add(instance)
```

There is no non-UI one-shot composite. Automating this means re-driving that data-layer sequence
below the UI handler, step by step, and owning every intermediate failure yourself. Before
assuming a convenience method exists, check whether the only caller is a page.

The mirror-image trap is that a screen's own preflight is not the model's admission check. The
page decides what to grey out; the model decides what it accepts. They are written separately and
they disagree — most usefully, `CanPurchase()` folds in live requirements and queue admission but
neither availability nor cost (see [native-action-surfaces.md](native-action-surfaces.md)). Reading
a control's enabled state tells you what the UI thinks, not what the call will do.

## Cached answers with UI-shaped lifetimes

A few reads are computed for display and then held. Treat any value that a screen shows as
stale-capable, and re-run the authored native chain that produced it immediately before acting on
it. Scroll target selection is the audited example: `TargetSelectOptions.GetTargeting()` and
`TargetStructure.GetRandomList(ScalingInfo)` must be re-invoked rather than trusted from a
previous computation, and an empty result is a normal outcome rather than an error.
