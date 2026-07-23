# OrbOfCreation ModSuite

[![CI](https://github.com/Vojow/OrbOfCreation-ModSuite/actions/workflows/ci.yml/badge.svg)](https://github.com/Vojow/OrbOfCreation-ModSuite/actions/workflows/ci.yml)
[![Latest release](https://img.shields.io/github/v/release/Vojow/OrbOfCreation-ModSuite?include_prereleases)](https://github.com/Vojow/OrbOfCreation-ModSuite/releases)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

Unofficial BepInEx mods, tests, and reverse-engineering notes for the Windows Mono build of [Orb of Creation](https://store.steampowered.com/app/1910680/Orb_of_Creation/).

The current beta provides grouped queue-aware Auto Buy, progression-aware Spell Leveling, Auto Cast, opt-in Auto Concept, disabled-by-default Auto Harvest, Mentor progression sharing, and an optional native-styled configuration UI. Back up your save before using beta automation.

## Project status

| Component | Status | Description |
|---|---|---|
| **Orb Automata 0.9.0** | Beta | Auto Buy, Auto Cast, Auto Concept, Spell Leveling, and ServiceCycle Auto Harvest. |
| **Orb Mod Config 0.7.0** | Beta | Staged settings editor plus live runtime health, tracing controls, and recent pump timing. |
| **Orb Mentor 0.3.8** | Beta | Progression-gated mastery sharing for spells, artifacts, and alchemy. |
| **Orb Modding Common 0.4.0** | Bundled | Shared runtime used by the suite, including ServiceCycle, diagnostics, replay, and tracing. |
| **Orb Insights / Toolbox** | Planned | Design and reverse-engineering notes only. |

The supported baseline is Windows 64-bit Mono, Unity `6000.0.70`, BepInEx `5.4.23.x`, and .NET `netstandard2.1`. Steam Deck is targeted through the Windows game under Proton and requires separate runtime validation.

Experimental Orb Chronomancer and Orb Achievement Resonance are excluded from this supported branch, build, and package scope.

## Runtime foundation

[ServiceCycle](docs/runtime-architecture/README.md) is the new shared engine for automation features. It reads game state on Unity's main thread, makes decisions in the background without holding game objects, and checks the current game state again before performing an action. Auto Harvest is the first feature using it; [Auto Buy is planned next](docs/plans/autobuy-service-cycle-port.md).

Three separate diagnostics help investigate different problems:

- manual full traces show exactly what happened and in what order;
- the rolling decision journal summarizes what services decided; and
- opt-in performance profiles show where time was spent.

Decode sessions with `./script/trace --full`, `--journal`, `--performance`, or `--dashboard`. Diagnostic failure does not change gameplay behavior.

## Get started

- Players: [install the supported suite](docs/user-guide/installation.md), then review [configuration and safety](docs/user-guide/configuration.md).
- Contributors: read the [development setup](docs/development/setup.md) and [contributing guidelines](CONTRIBUTING.md).
- Researchers: start with the [reverse-engineering knowledge map](docs/reverse-engineering/README.md).
- Maintainers: use the [testing](docs/development/testing.md), [runtime validation](docs/development/runtime-validation.md), and [release](docs/development/releases.md) guides.

The [documentation hub](docs/README.md) indexes the remaining player, contributor, architecture, research, and planning material.

## Build and test

Production builds use assemblies from a local game installation; those binaries are never stored here.

```powershell
$env:OOC_GAME_DIR='C:\Program Files (x86)\Steam\steamapps\common\Orb of Creation'
dotnet build src/OrbAutomata/OrbAutomata.csproj -c Release
```

Run the complete portable development suite with:

```sh
./script/test
```

The portable gate runs on Windows, Linux, and macOS with .NET SDK 10 and has a hard 60-second deadline. See [development setup](docs/development/setup.md) for real-reference and packaging workflows.

## Contributing, security, and licensing

Read [CONTRIBUTING.md](CONTRIBUTING.md) before submitting code or runtime evidence. Report save-corruption, code-execution, or local-data vulnerabilities privately according to [SECURITY.md](SECURITY.md).

Source code is available under the [MIT License](LICENSE). This project is not affiliated with or endorsed by MarpleGames or the publishers of Orb of Creation. Game names and assets belong to their respective owners; see [third-party notices](THIRD_PARTY_NOTICES.md).
