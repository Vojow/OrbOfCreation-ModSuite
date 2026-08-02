# Installing the supported ModSuite

[Back to documentation](../README.md)

The supported suite is one BepInEx plugin, `OrbModSuite.dll`, containing every feature: Auto Buy,
Auto Harvest, Auto Items, Auto Cast, Auto Concept, Spell Leveling, Mentor, and the in-game
configuration UI. It targets the Windows 64-bit Mono build of Orb of Creation with BepInEx
5.4.23.x. BepInEx 6 and native Linux packages are not supported. Steam Deck is targeted through
the Windows game under Proton and requires separate runtime validation.

The gameplay runtime starts normally only on an audited build. After a game update, a complete unknown assembly pair loads in compatibility quarantine so Mods and differential verification remain available while gameplay patches and services stay emergency-stopped. An incomplete assembly audit refuses the plugin and logs why.

## 1. Back up your save

Close the game and copy its save directory before installing or changing automation. On Windows the
saves live under `%USERPROFILE%\AppData\LocalLow\MarpleGames\Orb of Creation`; under Proton, the same
folder sits inside the game's `compatdata` prefix. Do not run the suite alongside AutobuyOrb or
another automatic buyer.

## 2. Install BepInEx 5

1. Download `BepInEx_win_x64_5.4.23.5.zip` from the [BepInEx releases](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5).
2. In Steam, open **Orb of Creation > Manage > Browse local files**.
3. Extract the archive beside `Orb Of Creation.exe`, not inside `Orb Of Creation_Data`.
4. Start and close the game once. Confirm that `BepInEx/config` and `BepInEx/LogOutput.log` exist.

On Proton, add this Steam launch option:

```text
WINEDLLOVERRIDES="winhttp=n,b" %command%
```

## 3. Install the supported suite

1. Download the recommended archive from the project's [Releases page](https://github.com/Vojow/OrbOfCreation-ModSuite/releases).
2. Extract it into the game directory and merge the included `BepInEx` folder.
3. Confirm the following layout:

```text
Orb of Creation/
|-- Orb Of Creation.exe
|-- winhttp.dll
`-- BepInEx/
    `-- plugins/
        `-- OrbModSuite/
            `-- OrbModSuite.dll
```

Keep exactly one copy of `OrbModSuite.dll` anywhere under `BepInEx/plugins`; duplicate older copies can be loaded instead of the intended build. Delete any `OrbAutomata.dll`, `OrbMentor.dll`, `OrbModConfig.dll`, or `OrbModding.Common.dll` left over from a release before 0.4.0: they load under their own retired plugin GUIDs beside the merged DLL and would run a second copy of the same automation. On startup, `BepInEx/LogOutput.log` should list Orb Of Creation ModSuite once without dependency errors.

Upgrading from a release before 0.4.0 does not carry your settings over. The suite has one configuration file named after its own plugin GUID, and the retired per-plugin files are never read, so the first start writes a fresh file with defaults. Note your old values before upgrading if you want to reapply them.

The suite detects the exact AutobuyOrb BepInEx GUID. If AutobuyOrb is installed, the suite disables only its overlapping Structure and Upgrade automation and leaves Auto Cast, Auto Concept, Spell Leveling, and Mentor available. This is best-effort safety, not universal third-party detection: unknown unregistered automation is not disabled and cannot be proven absent, so prefer one plugin per automated native action family.

Continue with [configuration and safety](configuration.md).
