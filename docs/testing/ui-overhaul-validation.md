# UI overhaul live validation

[Testing hub](README.md) · [Mod Config tests](mod-config.md) · [Native UI inventory](ui-native-inventory.md)

This is the player-install checkpoint for the game-native UI overhaul. Portable tests prove
projection, transaction, ordering, migration, and pixel-writer ownership against stubs. The
installed-game contracts prove the audited fields and methods exist in v1.0.5. Only this pass can
prove actual Unity layout, pointer behavior, native sprites, scene reconstruction, and readable
icons.

## Preconditions

1. Merge and install the supported suite artifact through the ordinary packaging path. Do not use
   the old build that supplied the reference screenshots.
2. Back up the save and use a disposable validation save. Remove overlapping automation mods.
3. Record the suite commit, game build, display resolution, window mode, and UI scale.
4. Keep the BepInEx log and the generated Runtime artifacts. Do not edit an active save.
5. Before opening Mods, confirm the log contains exactly one successful install line for each
   surface:
   - `Quick controls: native state frames and icons active`
   - `Mods rail: native visuals active`
   Any `Quick controls: native state frames or icons failed: <reason>` or
   `Mods rail: native visuals failed: <reason>` is a blocking suite defect. A temporary
   informational `Quick controls are not ready; installation will retry: <reason>` or
   `Mod Config UI is not ready; installation will retry: <reason>` is allowed only if it is
   followed by the corresponding success line.

## Native shell and responsive layout

1. Open Mods from each unlocked top-level native view. Confirm Mods remains last and returning to
   Magic, Scholar, or Time restores the previously selected native view.
2. Confirm the title is exactly **Orb Of Creation ModSuite** and the left rail is Runtime,
   General, Auto Buy, Auto Cast, Auto Concept, Auto Harvest, Auto Items, Auto Scribe, Mentor,
   Advanced.
3. Confirm no old Safety/Spells/Artifacts/Alchemy row and no per-feature Mode row remains.
4. Check 1365×768, 1920×1080, the player's highest resolution, and every supported UI-scale step.
   At each size, inspect long descriptions, editors, Default, conflicts, footer, scroll limits,
   the ten rail entries, Runtime grid, diagnostic cards, and graph for clipping or overlap.
5. Confirm all seven feature icons plus separated STOP form one top-left vertical column under the
   native gear and character buttons. The column must not cover those buttons, join the expanding
   spell-slot row, or leave any old control in `RightSidebar/AttributeBar`.

## Profile-build validation navigation

The `perf-debug` MCP server replaces the retired F10/F11/F12 validation shortcuts:

1. On Start, `game_continue` invokes the audited native `SaveStateManager.StartGame` action.
2. On Main, `game_screen_catalog` lists native tabs plus the suite-owned Mods rail entry.
3. `game_navigate` selects Mods and its live page catalog by exact name or index.

The Start screen also carries a large native-styled ModSuite status card beneath the game's version
number in both build modes. A screenshot must distinguish no mod, release (`MCP OFF`), perf-debug
(`MCP READY`), compatibility-blocked or control-plane-failed state, and duplicate processes by PID
without consulting a log. The card must use the native TMP font and the Mods dark-panel/status
palette; a small IMGUI/debug overlay is not an acceptable substitute.

## Pixel ownership and icon states

For Mods, every feature header, Apply/Revert/Default/conflict action, all Runtime actions, the seven
quick icons, and STOP:

1. Capture the idle appearance, then hover, press and hold, drag out, release, keyboard-select where
   supported, disable/re-enable the control, and hover again.
2. Confirm the suite-rendered frame/icon/text does not flicker to a native hover or pressed state
   after the suite paints it. Click actions must still fire exactly once.
3. Check each quick icon in Off, On, unhealthy, and emergency-stopped states. OFF must use the
   recessed native view-button frame and configured ON the raised frame independent of color.
   Auto Concept must use `ScreenScholar`, Auto Items `ScreenWorld`, Auto Scribe `ScreenWorkshop`,
   and Advanced `ScreenAlchemy`; each feature's quick and rail glyph must match. Mentor must use
   mastery XP and Auto Harvest the harvest-speed glyph.
4. Confirm tooltips identify the feature, configured intent, runtime health, and reason without
   clipped text.

## Commands, staging, and conflicts

1. For every registered automation feature, toggle once in the feature header and once in the
   quick column. Confirm the other surface updates from the same committed
   value and no duplicate mode row exists.
2. Stage policy changes on at least two feature pages without applying. Navigate through Runtime,
   close/reopen Mods, and confirm staged values and each remembered scroll position survive.
3. Apply and confirm the complete selected-plugin transaction persists together. Revert another
   staged set and confirm no saved value changes.
4. While a policy field is staged, change the same field through an external config reload. Confirm
   Apply blocks, the row shows Mine and Live, **Keep mine** and **Take live** each resolve only that
   conflict, and the footer reports the remaining conflict/staged count.
5. Exercise invalid numeric text and Default. Confirm validation blocks Apply and no invalidation
   publication or partial save occurs.

## Emergency stop and scene rebuild

0. With an unknown complete assembly pair, confirm Mods and differential verification load, STOP is engaged,
   no feature quick control or gameplay service starts, and General shows an engaged **Emergency disable**
   switch. Clear it and Apply; confirm the exact pair is accepted and the runtime composes without the switch
   being forced on again. Separately confirm **Allow this unverified game build** composes the runtime while
   STOP stays engaged until the ordinary two-click resume. Change either test hash and confirm the
   acknowledgement resets on next launch.
1. Configure a resume set containing Auto Buy/Spell Leveling, Auto Cast, Auto Concept, Auto Harvest,
   and Mentor. Hover STOP and confirm its tooltip lists that exact desired-On set.
2. Click STOP once. Confirm prepared work is discarded, no new native action starts, every desired-On
   quick icon reads stopped, and feature headers/Runtime agree.
3. Click STOP once to arm resume. Confirm automation remains stopped and the tooltip still lists the
   resume set. Click again to resume and confirm only the listed configured-On features recover.
4. Rebuild the game UI through an ordinary supported scene/UI transition, then return to Main.
   Confirm exactly one Mods entry, one seven-feature column, and one STOP exist; ordering, gap, tooltips,
   listeners, staged navigation bookmark, and native-view restoration still work.

## Runtime actions

1. Confirm the seven-feature grid appears first and orders failure/attention before waiting/healthy
   states, with configured intent, runtime state, and one readable reason.
2. Confirm the **Suite UI** diagnostic card contains healthy **Quick controls column native visuals** and
   **Mods rail native visuals** capabilities. If either failed, confirm the exact BepInEx error
   reason is repeated there.
3. Click **Run verifier**. Confirm it becomes queued, prevents a duplicate request, completes, and
   reports its result without changing gameplay configuration.
4. Click **Dump recent events**. Confirm the request is accepted once and the reported artifact
   exists and is readable.
5. Start and stop **Manual full trace**. Confirm button labels/status advance correctly and the
   accepted trace prefix is flushed to the reported run directory.
6. In a profiling build, start and stop **Performance profile** and verify its artifact. In a normal
   build, confirm the profile action is absent rather than disabled or misleading.
7. Observe the pump graph while automation is idle and active; confirm it updates without covering
   adjacent cards. Check decision-journal accepted/written counts and terminal health.
8. Inspect every detailed runtime card. Confirm schema, feature, service, capability, implementation,
   latest evidence, and reasons are readable; cards with failures or attention appear before healthy
   cards.

Record screenshots for the top-left quick-control matrix, Mods rail at minimum resolution, staged conflict,
emergency armed state, Runtime grid, and every failed checklist item. A visual failure blocks the
install even when portable and contract gates are green.
