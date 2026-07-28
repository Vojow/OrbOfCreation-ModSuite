# OrbOfCreation ModSuite

[![CI](https://github.com/Vojow/OrbOfCreation-ModSuite/actions/workflows/ci.yml/badge.svg)](https://github.com/Vojow/OrbOfCreation-ModSuite/actions/workflows/ci.yml)
[![Latest release](https://img.shields.io/github/v/release/Vojow/OrbOfCreation-ModSuite?include_prereleases)](https://github.com/Vojow/OrbOfCreation-ModSuite/releases)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

Unofficial BepInEx mods, tests, and reverse-engineering notes for the Windows Mono build of [Orb of Creation](https://store.steampowered.com/app/1910680/Orb_of_Creation/).

The current beta provides grouped queue-aware Auto Buy, progression-aware Spell Leveling, Auto Cast, opt-in Auto Concept, disabled-by-default Auto Harvest, Mentor progression sharing, and an optional native-styled configuration UI. Back up your save before using beta automation.

## Project status

The suite ships as one BepInEx 5 plugin — `OrbModSuite.dll`, plugin GUID `dev.vojow.orbofcreation.modsuite`, version `0.4.0` beta — with one configuration file. The earlier separately versioned Orb Automata, Orb Mod Config, Orb Mentor, and Orb Modding Common plugins are retired and no longer exist as loadable identities.

| Feature area | Status | Description |
|---|---|---|
| **Automation** | Beta | Auto Buy, Auto Cast, Auto Concept, Spell Leveling, and Auto Harvest. |
| **Mentor** | Beta | Progression-gated mastery sharing for spells, artifacts, and alchemy. |
| **Mod Config UI** | Beta | Staged settings editor plus live runtime health, tracing controls, and recent pump timing. |
| **Shared runtime** | Bundled | ServiceCycle, world collection, diagnostics, and tracing behind every feature. |
| **Orb Insights / Toolbox** | Planned | Design and reverse-engineering notes only. |

The supported baseline is Windows 64-bit Mono, Unity `6000.0.70`, BepInEx `5.4.23.x`, and .NET `netstandard2.1`. Steam Deck is targeted through the Windows game under Proton and requires separate runtime validation.

The suite computes the game's economy math itself, transcribed from one audited pair of game assemblies, so it refuses to load against a game build it has not audited. A game update therefore disables the suite until that build is re-audited; this is deliberate and has no bypass.

## Runtime foundation

[ServiceCycle](docs/runtime-architecture/README.md) is the shared engine for automation features. One main-thread pass reads raw game state and publishes it as an immutable world snapshot; features decide in the background against that snapshot without holding game objects, then revalidate the live game immediately before each action. Auto Harvest, Auto Buy, Spell Leveling, Auto Cast, Auto Concept, and world collection are registered services today; Mentor remains on the older per-feature path.

Three separate diagnostics help investigate different problems:

- manual full traces show exactly what happened and in what order;
- the rolling decision journal summarizes what services decided; and
- opt-in performance profiles show where time was spent.

Decode sessions with `./script/trace --full`, `--journal`, `--performance`, or `--dashboard`. Diagnostic failure does not change gameplay behavior.

## Get started

- Players: [install the supported suite](docs/user-guide/installation.md), then review [configuration and safety](docs/user-guide/configuration.md).
- Contributors: read the [development setup](docs/development/setup.md) and [contributing guidelines](CONTRIBUTING.md).
- Researchers: start with the [reverse-engineering knowledge map](docs/reverse-engineering/README.md).
- Maintainers: use the [testing](docs/testing/README.md), [runtime validation](docs/testing/runtime-validation.md), and [release](docs/development/releases.md) guides.

The [documentation hub](docs/README.md) indexes the remaining player, contributor, architecture, research, and planning material.

## Build and test

Production builds use assemblies from a local game installation; those binaries are never stored here.

```powershell
$env:OOC_GAME_DIR='C:\Program Files (x86)\Steam\steamapps\common\Orb of Creation'
dotnet build src/OrbModSuite.csproj -c Release
```

Run the complete portable development suite with:

```sh
./script/test
```

The portable gate runs on Windows, Linux, and macOS with .NET SDK 10 and has a hard 60-second deadline. See [development setup](docs/development/setup.md) for real-reference and packaging workflows.

## Contributing, security, and licensing

Read [CONTRIBUTING.md](CONTRIBUTING.md) before submitting code or runtime evidence. Report save-corruption, code-execution, or local-data vulnerabilities privately according to [SECURITY.md](SECURITY.md).

Source code is available under the [MIT License](LICENSE). This project is not affiliated with or endorsed by MarpleGames or the publishers of Orb of Creation. Game names and assets belong to their respective owners; see [third-party notices](THIRD_PARTY_NOTICES.md).
