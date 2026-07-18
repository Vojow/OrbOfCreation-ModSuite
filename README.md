# OrbOfCreation ModSuite

[![CI](https://github.com/Vojow/OrbOfCreation-ModSuite/actions/workflows/ci.yml/badge.svg)](https://github.com/Vojow/OrbOfCreation-ModSuite/actions/workflows/ci.yml)
[![Latest release](https://img.shields.io/github/v/release/Vojow/OrbOfCreation-ModSuite?include_prereleases)](https://github.com/Vojow/OrbOfCreation-ModSuite/releases)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

Unofficial BepInEx mods, tests, and reproducible reverse-engineering notes for the Windows Mono build of [Orb of Creation](https://store.steampowered.com/app/1910680/Orb_of_Creation/).

The current beta centers on Orb Automata: queue-aware Auto Buy with progression-aware spell leveling, safe Auto Cast, opt-in Auto Concept rotation, and a native-styled configuration screen. Back up your save before using beta automation.

## Project status

| Component | Status | Description |
|---|---|---|
| **Orb Automata 0.8.2** | Beta | Rejection-aware, completion-responsive queue-filling Auto Buy; coordinated Auto Cast; timed or catch-up Concept rotation; and progression-aware spell leveling with bounded shared scheduling. |
| **Orb Mod Config 0.6.0** | Beta | Mods tab available from the start, with typed editors, staged multi-condition feature locking, Steam Deck keyboard support, and coordinated UI recovery. |
| **Orb Mentor 0.3.1** | Beta | Progression-gated spell, artifact, and alchemy mastery sharing with independent opt-in domains, equipped-spell sources, and a selectable highest-only spell policy. |
| **Orb Insights / Toolbox** | Planned | Design and reverse-engineering notes only. |

Supported baseline: Windows 64-bit Mono, Unity `6000.0.70`, BepInEx `5.4.23.x`, and .NET `netstandard2.1`. Steam Deck is targeted through the Windows game under Proton with BepInEx 5, but ModSuite 0.3.0 Beta 1 still requires post-release Proton validation.

Experimental Orb Chronomancer and Orb Achievement Resonance work is isolated on the `codex/experimental-chronomancer-resonance` branch and is not part of supported `main` builds or packages.

## Get started

- Players: [install the supported suite](docs/user-guide/installation.md), then review [configuration and safety](docs/user-guide/configuration.md).
- Contributors: read the [development setup](docs/development/setup.md) and [contributing guidelines](CONTRIBUTING.md).
- Researchers: start with the [reverse-engineering knowledge map](docs/reverse-engineering/README.md).
- Maintainers: use the [testing](docs/development/testing.md), [runtime validation](docs/development/runtime-validation.md), and [release](docs/development/releases.md) guides.

The [documentation hub](docs/README.md) indexes all player, contributor, research, and planning material.

## Quick build and test

Production builds use assemblies from a local game installation; those binaries are never stored here.

```powershell
$env:OOC_GAME_DIR='C:\Program Files (x86)\Steam\steamapps\common\Orb of Creation'
dotnet build src/OrbAutomata/OrbAutomata.csproj -c Release
dotnet test tests/OrbModding.Tests/OrbModding.Tests.csproj -p:UseGameStubs=true
```

See [development setup](docs/development/setup.md) for the complete workflow.

## Contributing, security, and licensing

Read [CONTRIBUTING.md](CONTRIBUTING.md) before submitting code or runtime evidence. Report save-corruption, code-execution, or local-data vulnerabilities privately according to [SECURITY.md](SECURITY.md).

Source code is available under the [MIT License](LICENSE). This project is not affiliated with or endorsed by MarpleGames or the publishers of Orb of Creation. Game names and assets belong to their respective owners; see [third-party notices](THIRD_PARTY_NOTICES.md).
