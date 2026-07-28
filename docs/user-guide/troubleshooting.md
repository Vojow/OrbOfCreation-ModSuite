# Troubleshooting

[Back to documentation](../README.md) · [Installation](installation.md)

## The mod stopped loading after a game update

This is the expected result of a game update, not a broken install. The suite computes the game's economy math itself, transcribed from one audited pair of game assemblies, so it refuses to run against a build it has not audited rather than produce confident, plausible, incorrect numbers.

`BepInEx/LogOutput.log` will contain one error line beginning:

```text
Refusing to load: the installed game build does not match an audited baseline.
```

The line goes on to name the observed `Assembly-CSharp` and `Assembly-CSharp-firstpass` hashes and the baselines the build was audited against. A second form, `Refusing to load: the game assembly audit could not be completed (...)`, means the audit itself failed to run — usually a game directory the suite could not read.

There is no bypass and no degraded mode: when the suite refuses, it applies no patch, subscribes to no game event, and registers no service, so the game is left completely untouched. Falling back to asking the game for its own numbers is not offered, because a hash mismatch invalidates the reflected member contracts exactly as much as it invalidates the ported math.

Players should report the new game version on the [issue tracker](https://github.com/Vojow/OrbOfCreation-ModSuite/issues) and wait for a release that audits it. Maintainers re-audit a build with `script/re-audit --game-dir <path>` to see what changed, then `--stamp` to record the new baseline once every verification stage passes.

## Steam Deck UI or severe frame-rate loss

Current supported builds throttle native UI discovery while autoqueue is locked, retry the Mods UI while Proton finishes constructing the scene, and cooperatively bound automation, Mentor, and configuration-UI work. If the problem persists, disable Auto Buy, Auto Cast, Auto Concept, and Mentor through their modes or gameplay controls, then attach `BepInEx/LogOutput.log` to the report. Include whether the native autoqueue and NG+ tabs were unlocked and which automation modes were active.

## No `LogOutput.log`

Verify that BepInEx files are beside the game executable. On Proton, recheck the `winhttp` override in the installation guide.

## The configuration UI shows only itself

Verify that BepInEx reports the suite plugin during startup. Remove duplicate DLLs and any test-stub DLLs from the game directory.

## The suite does not load

First check for the refusal line described above. If it is absent, confirm that exactly one `OrbModSuite.dll` is present somewhere under `BepInEx/plugins`. The suite ships as a single assembly, so a leftover `OrbAutomata.dll`, `OrbMentor.dll`, `OrbModConfig.dll`, or `OrbModding.Common.dll` from a release before 0.4.0 is a cause of failure rather than a requirement — delete them. Search `BepInEx/LogOutput.log` for missing dependencies, duplicate plugin GUIDs, and assembly errors.

## My settings are gone after upgrading

Expected on the upgrade to 0.4.0. The suite has one configuration file named after its own plugin GUID, and the four retired per-plugin files are never read or migrated. Reapply your settings in the in-game configuration UI.

## Auto Concept repeatedly removes and re-adds one concept

Current builds reject a positive concept drain when its authoritative resource is at zero, so an unsafe recipe cannot monopolize mutation work or prevent another acquired compatible slot from being filled. If churn remains, enable operational logging briefly and include the `Auto Concept` lines plus the affected resource name.

## Checking the suite against the live game

**Run differential verification** on Mods -> Runtime runs the diagnostic: it compares the suite's own economy math against the game's results for every structure and resource, checks its world collection against the game's own accessors, and checks its verdict on whether each upgrade and structure may be bought at its next level against the game's own per-level prerequisite check. It reports one verdict per pass in `BepInEx/LogOutput.log`, and reads and compares only; it changes nothing in the game.

A requirement pass that reports `INCOMPLETE` names a condition class the suite does not model. That is not a wrong answer — an unmodelled condition already stops the purchase being planned — but it is worth reporting, because it makes Auto Buy skip something the game would have allowed.

It deliberately runs everything inside the single frame the key was pressed in, so **the game will visibly hitch** — that hitch is the acknowledgement that the run happened. Load a save first; with no structures or resources available the passes report as unavailable.

`Diagnostics/VerifyGameMathShortcut` remains hidden only to preserve a player-customized legacy value; the runtime does not listen to it. Schema 3 unbinds inherited `Left Ctrl + Left Alt + Y` and historical Alt+M defaults while preserving customized chords. Mentor's toggle (`General/ToggleShortcut`) remains `Left Alt + M`. Auto Cast defaults to `F8`; schema 3 migrates only the inherited `Left Alt + X` value and leaves a player-selected chord alone.

## Reporting a bug

Include game, Unity, BepInEx, and plugin versions, sanitized reproduction steps, and the relevant log section. Do not attach private saves or logs containing usernames, email addresses, or unrelated local paths.

If recovery requires removing the mod, follow [uninstalling](uninstalling.md).
