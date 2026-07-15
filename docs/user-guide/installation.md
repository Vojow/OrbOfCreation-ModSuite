# Installation

[Back to documentation](../README.md)

Orb Automata supports the Windows 64-bit Mono build of Orb of Creation with BepInEx 5.4.23.x. BepInEx 6 and native Linux packages are not supported. Steam Deck is supported through the Windows game under Proton.

## 1. Back up your save

Close the game and copy its save directory before installing or changing automation. Do not run Orb Automata with AutobuyOrb or another automatic buyer.

## 2. Install BepInEx 5

1. Download `BepInEx_win_x64_5.4.23.5.zip` from the [BepInEx releases](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5).
2. In Steam, open **Orb of Creation > Manage > Browse local files**.
3. Extract the archive beside `Orb Of Creation.exe`, not inside `Orb Of Creation_Data`.
4. Start and close the game once. Confirm that `BepInEx/config` and `BepInEx/LogOutput.log` exist.

On Proton, add this Steam launch option:

```text
WINEDLLOVERRIDES="winhttp=n,b" %command%
```

## 3. Install Orb Automata

1. Download the recommended archive from the project's [Releases page](https://github.com/Vojow/OrbOfCreation-ModSuite/releases).
2. Extract it into the game directory and merge the included `BepInEx` folder.
3. Confirm the following layout:

```text
Orb of Creation/
|-- Orb Of Creation.exe
|-- winhttp.dll
`-- BepInEx/
    `-- plugins/
        `-- OrbAutomata/
            |-- OrbAutomata.dll
            |-- OrbModConfig.dll
            `-- OrbModding.Common.dll
```

Keep one copy of each DLL. On startup, `BepInEx/LogOutput.log` should list Orb Automata and Orb Mod Config once each without dependency errors.

Continue with [configuration and safety](configuration.md).
