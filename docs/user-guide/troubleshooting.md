# Troubleshooting

## Steam Deck UI or severe frame-rate loss

Version 0.1.1 throttles native UI discovery while autoqueue is locked, retries the Mods UI while Proton finishes constructing the scene, and applies conservative CPU limits to Auto Buy and Mentor. If an older configuration is present, Automata's CPU budget is clamped to 1 ms and Mentor is clamped to two grants and 1 ms per frame (0.5 ms by default). If the problem persists, disable Auto Buy and Mentor with their shortcuts or configuration modes and attach `BepInEx/LogOutput.log` to the report; include whether the native autoqueue and NG+ tabs were unlocked.

[Back to documentation](../README.md) · [Installation](installation.md)

## No `LogOutput.log`

Verify that BepInEx files are beside the game executable. On Proton, recheck the `winhttp` override in the installation guide.

## Mod Config shows only itself

Verify that BepInEx reports both plugins during startup. Remove duplicate DLLs and any test-stub DLLs from the game directory.

## Automata does not load

Confirm that `OrbAutomata.dll`, `OrbModConfig.dll`, and `OrbModding.Common.dll` are present. Search `BepInEx/LogOutput.log` for missing dependencies and assembly errors.

## Reporting a bug

Include game, Unity, BepInEx, and plugin versions, sanitized reproduction steps, and the relevant log section. Do not attach private saves or logs containing usernames, email addresses, or unrelated local paths.

If recovery requires removing the mod, follow [uninstalling](uninstalling.md).
