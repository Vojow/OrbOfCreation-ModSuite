# Uninstalling

[Back to documentation](../README.md)

1. Close the game.
2. Remove `BepInEx/plugins/OrbAutomata/OrbAutomata.dll`.
3. Remove `BepInEx/plugins/OrbMentor/OrbMentor.dll` and `OrbModding.Common.dll`.
4. Remove `BepInEx/plugins/OrbModConfig/OrbModConfig.dll`.
5. Search `BepInEx/plugins` for duplicate copies of those four DLL names and remove them as well.
6. Retain or delete their configuration files under `BepInEx/config` as preferred.

The supported mods do not add custom save-file records. Keep a save backup until you have confirmed that the unmodified game loads normally.
