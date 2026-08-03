# Troubleshooting

[Back to documentation](../README.md) · [Installation](installation.md)

## The mod entered compatibility quarantine after a game update

This is the expected result of a game update, not a broken install. The suite computes the game's economy math itself, so an unknown complete assembly pair loads only the Mods configuration and verifier while gameplay patches and services remain emergency-stopped.

`BepInEx/LogOutput.log` will contain one warning line beginning:

```text
Gameplay runtime quarantined: the installed game build does not match an audited baseline.
```

The line names the observed `Assembly-CSharp` and `Assembly-CSharp-firstpass` hashes and the audited baselines. A refusal beginning `Refusing to load even the diagnostic control plane` means the pair could not be discovered completely, so even a hash-bound acknowledgement would be unsafe.

Run **Mods > Runtime > Check game math** while quarantined and report the results. If a player chooses to proceed before an audited release, press **Resume all** on General or the top-left STOP button. That immediate action accepts only the exact observed pair and resumes in the same step. This is an explicit risk acknowledgement, not audit evidence, and either assembly changing resets it. **Advanced > Allow this unverified game build** is the alternative when the player wants to acknowledge the pair but keep STOP engaged for a later one-click resume. Turning that acknowledgement off immediately re-engages STOP; restart to unload patches already installed during that session.

Players should still report the new game version on the [issue tracker](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/issues). Maintainers re-audit a build with `script/re-audit --game-dir <path>` to see what changed, then `--stamp` to record the new baseline once every verification stage passes.

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

Current builds reject a positive concept drain when its authoritative resource is at zero, so an unsafe recipe cannot monopolize mutation work or prevent another acquired compatible slot from being filled. If churn remains, press **Create bug report** immediately after it happens and include the resulting zip plus the affected resource name.

If Auto Concept repeatedly logs `Auto Concept did not complete`, the line names the active and
proposed replacement UUIDs and says whether the live slot, quantity, or prospective resource drain
refused the rotation. That rejection ends the current world round. The same candidate may be proposed
and rejected again after each 250-millisecond collection; this is deliberate evidence that the world
snapshot is missing a native constraint, not a reason to skip ahead or add a retry timer.

## Checking the suite against the live game

**Check game math** on Mods -> Runtime runs the diagnostic: it compares the suite's own economy math against the game's results for every structure and resource, checks its world collection against the game's own accessors, and checks its verdict on whether each upgrade and structure may be bought at its next level against the game's own per-level prerequisite check. It reports one verdict per pass in `BepInEx/LogOutput.log`, and reads and compares only; it changes nothing in the game.

A requirement pass that reports `INCOMPLETE` names a condition class the suite does not model. That is not a wrong answer — an unmodelled condition already stops the purchase being planned — but it is worth reporting, because it makes Auto Buy skip something the game would have allowed.

It deliberately runs everything inside the single frame the key was pressed in, so **the game will visibly hitch** — that hitch is the acknowledgement that the run happened. Load a save first; with no structures or resources available the passes report as unavailable.

Schema 5 removes the retired `Diagnostics/VerifyGameMathShortcut`; differential verification is an explicit Runtime action. Mentor's toggle (`General/ToggleShortcut`) remains `Left Alt + M`. Auto Cast defaults to `F8`; schema 3 migrates only the inherited `Left Alt + X` value and leaves a player-selected chord alone.

## Reporting a bug

Press **Create bug report** on Mods -> Runtime immediately after the problem. The suite flushes the
evidence it already holds and creates one timestamped zip no larger than 10 MiB. Text files are
redacted for usernames and absolute paths; the included save remains a byte-exact private game file,
so share the zip only with a recipient you trust. Include sanitized reproduction steps with it.

If recovery requires removing the mod, follow [uninstalling](uninstalling.md).
