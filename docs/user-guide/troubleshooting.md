# Troubleshooting

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
