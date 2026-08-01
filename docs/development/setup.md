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

The default build requires only the pinned .NET SDK. The repository commits a
metadata-only, full-surface reference closure generated from the audited Orb of
Creation v1.0.5 assemblies. These references preserve native assembly, type,
and member identities but contain no method bodies. They are not the
hand-written test stubs.

With no `OOC_GAME_DIR` set, both configurations build against the committed
references:

```bash
dotnet build src/OrbModSuite.csproj --configuration Debug
dotnet build src/OrbModSuite.csproj --configuration Release
```

`global.json` pins the exact SDK used for release builds. Install that SDK
version rather than allowing the compiler to float.

The canonical release entry points also set
`ContinuousIntegrationBuild=true`. That normalizes SourceLink document roots
to `/_/`, so the full DLL is byte-identical across checkout locations. A plain
development build remains deterministic within its checkout but is not the
cross-checkout release-byte reproduction command.

Set `OOC_GAME_DIR` only for gates that inspect the full installed assemblies,
including installed contracts, release faithfulness, and perf-debug builds.
The game is never launched by the contract gate.

On Windows, point `OOC_GAME_DIR` at the game root:

```powershell
$env:OOC_GAME_DIR='C:\Program Files (x86)\Steam\steamapps\common\Orb of Creation'
dotnet test tests/OrbModding.GameContractTests/OrbModding.GameContractTests.csproj -c Release
```

On Linux or macOS, `OOC_GAME_DIR` may point to a staged ignored copy with game
assemblies under `Orb Of Creation_Data/Managed` and BepInEx under `BepInEx/core`.

```bash
export OOC_GAME_DIR="$PWD/lib"
dotnet test tests/OrbModding.GameContractTests/OrbModding.GameContractTests.csproj \
  --configuration Release
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

Stub-linked outputs live under `bin-stubs/` and `obj-stubs/`; metadata-true
reference builds use the normal `bin/` tree. The canonical release entry
points add the cross-checkout setting above. Never install a
hand-written-stub-linked DLL. Continue with the
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
and print SHA-256 hashes. `release` installs the same committed-reference build
that ships. `perf-debug` includes ServiceCycle profiling and still resolves its
extra debug-only dependencies from the game. Neither command tags, publishes,
or launches the game.

From a clean commit, rehearse the supported package without installing it:

```bash
./script/package
```

The package rehearsal runs the portable and installed-contract gates, then
builds the canonical Release DLL from committed references before writing an
allowlisted archive and hash manifest under `artifacts/releases/`.
