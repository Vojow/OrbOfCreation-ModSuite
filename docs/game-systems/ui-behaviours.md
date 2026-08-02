# Interface behaviours

Small behaviours that are easy to mistake for bugs.

- **The mouse wheel has no owner.** The game contains no wheel handler at all; the engine delivers
  the wheel to whatever scrollable element happens to be on top under the cursor. If scrolling goes
  somewhere unexpected, move the pointer rather than hunting for a setting.
- **Tab clicks reselect, they never toggle.** Clicking the screen you are already on re-selects it;
  there is no close gesture and no way to click a screen "off".
- **Top-bar buttons arrive late.** They are built a couple of seconds after the scene loads (2–4 s
  observed, depending on the machine) and the game emits no signal when that finishes. A top bar that
  looks empty right after loading is still starting up.
- **Screens cross-fade.** The incoming page settles a beat after the click, so what is on screen
  during the transition is a blend of both.
- **Tooltips nest, recursively.** Inspect a term inside a tooltip and you get that term's own
  tooltip. A modifier-key mode shows the calculated sums of the effects rather than their parts.
- **Tooltips are not a complete model.** They give the terms but not always the order of operations.
  When a displayed number and your arithmetic disagree, the fold order is usually the reason; see
  [modifiers.md](modifiers.md).
- **Red and grey mean different things.** Red is "you cannot pay for this, or it will not fit"; grey
  is "this is not available to you at all".
