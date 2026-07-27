# Uninstalling

[Back to documentation](../README.md)

1. Close the game.
2. Remove `BepInEx/plugins/OrbModSuite/OrbModSuite.dll`.
3. Search `BepInEx/plugins` for duplicate copies of `OrbModSuite.dll`, and for any `OrbAutomata.dll`, `OrbMentor.dll`, `OrbModConfig.dll`, or `OrbModding.Common.dll` left over from a release before 0.4.0, and remove them as well.
4. Retain or delete the configuration file `BepInEx/config/dev.vojow.orbofcreation.modsuite.cfg` as preferred. Retired `dev.vojow.orbofcreation.{automata,mentor,modconfig}.cfg` files are no longer read by anything; installing the suite moves any it finds into a timestamped folder under `BepInEx/modsuite-backups/`, so look there rather than in `BepInEx/config` if you want to keep them.

The supported mods do not add custom save-file records. Keep a save backup until you have confirmed that the unmodified game loads normally.
