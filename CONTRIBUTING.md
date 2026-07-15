# Contributing

Thanks for helping improve OrbOfCreation-ModSuite. This is an unofficial modding project built against the Windows Mono version of Orb of Creation.

## Before opening a change

- Search existing issues and pull requests.
- Keep each pull request focused on one feature or fix.
- For behavior changes, describe the player-visible effect, safety implications, and runtime validation performed.
- Never commit Orb of Creation assemblies, BepInEx binaries, save files, local configuration, decompiler output, or other proprietary game assets.
- Remove usernames, email addresses, local paths, and unrelated save data from logs before attaching them.

## Development setup

Portable tests do not require the game:

```powershell
dotnet test tests/OrbModding.Tests/OrbModding.Tests.csproj -p:UseGameStubs=true
```

Production builds require `OOC_GAME_DIR` to point to a local Orb of Creation installation containing BepInEx 5:

```powershell
$env:OOC_GAME_DIR='C:\Program Files (x86)\Steam\steamapps\common\Orb of Creation'
powershell -NoProfile -ExecutionPolicy Bypass -File tools/test-modsuite.ps1 -GameRoot $env:OOC_GAME_DIR
```

The test-stub build writes to `bin-stubs/` and `obj-stubs/`; deployable game builds write to the normal `bin/` directory. Never install a stub-linked DLL into BepInEx.

`tools/package-automata.ps1` is the maintainer release path. It reruns the complete validation pipeline and packages only the three required DLLs plus public documentation and checksums.

## Runtime testing

- Back up the save directory.
- Test one automatic buyer at a time.
- Begin risky tests with a narrow UUID allowlist or harmless spell loadout.
- Include the game, Unity, BepInEx, and plugin versions in reports.
- Convert reproducible bugs into portable tests or sanitized fixtures whenever possible.

## Pull requests

A pull request should include:

- a concise summary and motivation;
- tests added or updated;
- automated test output;
- runtime evidence when the behavior depends on Unity or native game state;
- documentation changes for public configuration or installation behavior.

By contributing, you confirm that you have the right to submit the contribution under the repository's license.
