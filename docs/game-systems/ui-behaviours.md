# Interface behaviours

Small behaviours that are easy to mistake for bugs.

- **The mouse wheel has no owner.** The game contains no wheel handler at all; the engine delivers
  the wheel to whatever scrollable element happens to be on top under the cursor. If scrolling goes
  somewhere unexpected, move the pointer rather than hunting for a setting.
- **Tooltips nest, recursively.** Inspect a term inside a tooltip and you get that term's own
  tooltip. A modifier-key mode shows the calculated sums of the effects rather than their parts.
- **Tooltips are not a complete model.** They give the terms but not always the order of operations.
  When a displayed number and your arithmetic disagree, the fold order is usually the reason; see
  [modifiers.md](modifiers.md).
- **Red and grey mean different things.** Red is "you cannot pay for this, or it will not fit"; grey
  is "this is not available to you at all".
