# Development setup

[Back to documentation](../README.md) · [Contributing](../../CONTRIBUTING.md) ·
[Engineering doctrine](engineering-doctrine.md)

## Repository flow

Branch from `develop` and keep the pull request focused. Pull requests target
`develop`; `main` is updated by the release flow in
[the release procedure](../releasing.md) and
[release review checklist](releases.md). Use an area-prefixed commit subject such
as `autobuy: ...`, `ui: ...`, `build: ...`, or `docs: ...`.

## Dependencies

Portable tests require only the .NET SDK. Production builds and installed
contracts require local Orb of Creation managed assemblies and BepInEx 5
references because those binaries are not committed.

On Windows, point `OOC_GAME_DIR` at the game root:

```powershell
$env:OOC_GAME_DIR='C:\Program Files (x86)\Steam\steamapps\common\Orb of Creation'
dotnet build src/OrbModSuite.csproj -c Release
```

On Linux or macOS, stage the same ignored layout under `lib/`: the game managed
assemblies at `lib/Orb Of Creation_Data/Managed` and the official BepInEx 5 core
files at `lib/BepInEx/core`.

```bash
export OOC_GAME_DIR="$PWD/lib"
dotnet build src/OrbModSuite.csproj -c Release
```

Keep absolute local paths out of tracked files. Building does not authorize an
installation into the game.

## Development gate

Run the portable/profile and installed-contract lanes one after the other:

```bash
ORB_TEST_ATTEMPTS=1 ./script/test
OOC_GAME_DIR="$PWD/lib" dotnet test \
  tests/OrbModding.GameContractTests/OrbModding.GameContractTests.csproj \
  --configuration Release
```

The first command has a 60-second wall-clock limit and runs the ordinary suite,
the compile-time profile suite, and the profiled trace-tool build. The second
reads PE metadata from the audited game copy without launching Unity. Record and
reconcile all test and manifest counts; when compiler-warning counts matter, use
the serial, non-incremental build commands from `tools/release.sh` so caching
cannot hide a delta.

Stub-linked outputs live under `bin-stubs/` and `obj-stubs/`; deployable builds
use the normal `bin/` tree. Never install a stub-linked DLL. Continue with the
[testing hub](../testing/README.md) and
[runtime validation protocol](../testing/runtime-validation.md).

## Supported operator commands

Only after the user explicitly authorizes a local installation:

```bash
./script/install release
./script/install perf-debug
```

Both modes refuse to run while the game is open, run the gates, back up saves and
installed DLLs, reject duplicate or retired DLLs, install the verified output,
and print SHA-256 hashes. `perf-debug` includes ServiceCycle profiling; `release`
does not. Neither command tags, publishes, or launches the game.

From a clean commit, rehearse the supported package without installing it:

```bash
./script/package
```

The package rehearsal runs the portable, installed-contract, and real-reference
Release gates before writing an allowlisted archive and hash manifest under
`artifacts/releases/`.
