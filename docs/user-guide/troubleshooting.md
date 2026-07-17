# Troubleshooting

## Steam Deck UI or severe frame-rate loss

Current supported builds throttle native UI discovery while autoqueue is locked, retry the Mods UI while Proton finishes constructing the scene, and cooperatively bound Automata, Mentor, and Mod Config work. If the problem persists, disable Auto Buy, Auto Cast, Auto Concept, and Mentor through their modes or gameplay controls, then attach `BepInEx/LogOutput.log` to the report. Include whether the native autoqueue and NG+ tabs were unlocked and which automation modes were active.

[Back to documentation](../README.md) · [Installation](installation.md)

## No `LogOutput.log`

Verify that BepInEx files are beside the game executable. On Proton, recheck the `winhttp` override in the installation guide.

## Mod Config shows only itself

Verify that BepInEx reports both plugins during startup. Remove duplicate DLLs and any test-stub DLLs from the game directory.

## Automata does not load

Confirm that `OrbAutomata.dll` and one matching `OrbModding.Common.dll` are present somewhere under `BepInEx/plugins`. Orb Mod Config is optional for Automata runtime behavior. Search `BepInEx/LogOutput.log` for missing dependencies, duplicate plugin GUIDs, and assembly errors.

## Auto Concept repeatedly removes and re-adds one concept

Install Orb Automata 0.7.0 or later. Current builds reject a positive concept drain when its authoritative resource is at zero, so an unsafe recipe cannot monopolize mutation work or prevent another acquired compatible slot from being filled. If churn remains, enable operational logging briefly and include the `Auto Concept` lines plus the affected resource name.

## Reporting a bug

Include game, Unity, BepInEx, and plugin versions, sanitized reproduction steps, and the relevant log section. Do not attach private saves or logs containing usernames, email addresses, or unrelated local paths.

If recovery requires removing the mod, follow [uninstalling](uninstalling.md).
