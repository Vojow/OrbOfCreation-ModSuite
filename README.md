# OrbOfCreation ModSuite

[![CI](https://github.com/Vojow/OrbOfCreation-ModSuite/actions/workflows/ci.yml/badge.svg)](https://github.com/Vojow/OrbOfCreation-ModSuite/actions/workflows/ci.yml)
[![Latest release](https://img.shields.io/github/v/release/Vojow/OrbOfCreation-ModSuite?include_prereleases)](https://github.com/Vojow/OrbOfCreation-ModSuite/releases)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

Unofficial BepInEx mods, tests, and reverse-engineering notes for the Windows Mono build of [Orb of Creation](https://store.steampowered.com/app/1910680/Orb_of_Creation/).

The current public beta centers on Orb Automata: queue-aware Auto Buy, safe Auto Cast, and an optional native-styled configuration screen. Keep a save backup while using beta automation.

## Project status

| Component | Status | Description |
|---|---|---|
| **Orb Automata 0.4.0** | Beta | Auto Buy and Auto Cast using native game actions, configurable affordability, reserves, and queue ownership. |
| **Orb Mod Config 0.5.0** | Beta | Simplified in-game Mods tab with typed editors and Steam Deck keyboard support. |
| **Orb Chronomancer** | Experimental | Simulation-speed controls; not included in the Automata release archive. |
| **Orb Achievement Resonance** | Experimental | Achievement Strength extension; native mutation remains disabled by default. |
| **Orb Mentor 0.1.0** | Beta candidate | Highest-mastery spells share final native mastery XP with lower-level discovered spells; interactive validation remains before release. |
| **Orb Insights / Toolbox** | Planned | Design and reverse-engineering notes only. |

Supported baseline:

- Windows 64-bit Orb of Creation Mono build
- Unity `6000.0.70`
- BepInEx `5.4.23.x`
- .NET target `netstandard2.1`

BepInEx 6 and native Linux BepInEx packages are not supported. Steam Deck is supported only by running the Windows game through Proton with BepInEx 5.

## Install

### 1. Back up the save

Close the game and copy the Orb of Creation save directory before installing or changing automation. Do not run Automata alongside AutobuyOrb or another automatic buyer; independent buyers can race for resources and queue capacity.

### 2. Install BepInEx 5

1. Download [`BepInEx_win_x64_5.4.23.5.zip`](https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.5/BepInEx_win_x64_5.4.23.5.zip).
2. In Steam, open **Orb of Creation → Manage → Browse local files**.
3. Extract the archive beside `Orb Of Creation.exe`. Do not place it inside `Orb Of Creation_Data`.
4. Start and close the game once. A successful installation creates `BepInEx/config` and `BepInEx/LogOutput.log`.

See the [official BepInEx installation guide](https://docs.bepinex.dev/articles/user_guide/installation/index.html) for general troubleshooting.

For Steam Deck or another Proton system, add this Steam launch option:

```text
WINEDLLOVERRIDES="winhttp=n,b" %command%
```

The [BepInEx Proton/Wine guide](https://docs.bepinex.dev/articles/advanced/proton_wine.html) explains the equivalent Wine configuration.

### 3. Install Orb Automata

1. Download the recommended archive from the [Releases page](https://github.com/Vojow/OrbOfCreation-ModSuite/releases).
2. Extract it into the game directory and merge the included `BepInEx` folder.
3. Confirm this layout:

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

Release ZIPs use portable `/` entry separators and can be extracted directly on Windows, SteamOS, or Bazzite. If an older archive creates root-level filenames containing `BepInEx\plugins`, delete those files and download a corrected archive.

`OrbAutomata.dll` is the gameplay plugin. `OrbModConfig.dll` provides the optional in-game **Mods** tab. `OrbModding.Common.dll` is required. Keep only one copy of each DLL.

After starting the game, `BepInEx/LogOutput.log` should list Orb Automata and Orb Mod Config once each without missing-dependency errors. Auto Buy defaults to Active with 100× affordability thresholds; Auto Cast defaults to Disabled.

## Configuration and safety

Open the in-game **Mods** tab to configure Automata. Important controls:

- `AutoBuy.Mode` and `AutoCast.Mode`: `Disabled` or `Active`.
- Separate structure and upgrade affordability modes.
- Optional absolute and relative resource reserves.
- `LeaveQueueSlots` to preserve room for manual actions.
- Optional action-multiplier handling, capped to available queue room and revalidated per level.
- `Safety.EmergencyDisable` to stop new automated purchases and casts immediately.

Operational purchase/cast logging is off by default. Enable it only while troubleshooting. See the [Automata documentation](src/OrbAutomata/README.md) for the complete behavior and scheduling contract.

## Troubleshooting and removal

- No `LogOutput.log`: verify BepInEx files are beside the game executable and recheck the Proton override when applicable.
- Mod Config shows no configurable mods: verify BepInEx reports the suite plugins during startup and remove duplicate or test-stub DLLs.
- Automata does not load: confirm all three release DLLs are present and search the log for dependency or assembly errors.
- Bug reports: include game, BepInEx, and plugin versions plus sanitized reproduction steps. Do not attach private saves or unredacted logs.
- Uninstall: close the game and remove the three DLLs from `BepInEx/plugins/OrbAutomata`. Configuration files may be retained or deleted separately. The mods do not add custom save-file records.

## Build from source

Production builds reference BepInEx, Unity, Harmony, and game assemblies from your local installation. Those binaries are never stored in this repository.

```powershell
$env:OOC_GAME_DIR='C:\Program Files (x86)\Steam\steamapps\common\Orb of Creation'
dotnet build src/OrbAutomata/OrbAutomata.csproj -c Release
dotnet build src/OrbModConfig/OrbModConfig.csproj -c Release
```

Run the full local validation pipeline on a game computer:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/test-modsuite.ps1 -GameRoot $env:OOC_GAME_DIR
```

Maintainers can create the validated release archive and SHA-256 checksums with `tools/package-automata.ps1`.

## Tests

Game-independent tests use committed API stubs and require no game installation:

```powershell
dotnet test tests/OrbModding.Tests/OrbModding.Tests.csproj -p:UseGameStubs=true
```

Stub-linked outputs are isolated under `bin-stubs/` and cannot overwrite deployable Release DLLs. Installed-game metadata contracts are documented in [Compatibility and testing](docs/compatibility-and-testing.md).

## Reverse-engineering documentation

The repository contains reproducible notes and normalized identity mappings, not game binaries:

- [Knowledge map](docs/README.md)
- [Reverse-engineering audit](docs/reverse-engineering-audit.md)
- [Entity catalog](docs/entity-catalog.md)
- [Entity correlations](docs/entity-correlations.md)
- [Architecture](docs/architecture.md)
- [Roadmap](docs/roadmap.md)
- [Public release checklist](docs/public-release-checklist.md)

## Contributing and security

Read [CONTRIBUTING.md](CONTRIBUTING.md) before submitting code or runtime evidence. Report save-corruption, code-execution, or local-data vulnerabilities privately according to [SECURITY.md](SECURITY.md).

Source code in this repository is available under the [MIT License](LICENSE).

## Disclaimer and acknowledgements

This project is not affiliated with or endorsed by MarpleGames or the publishers of Orb of Creation. Game names and assets belong to their respective owners. See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for dependencies and acknowledgements.
