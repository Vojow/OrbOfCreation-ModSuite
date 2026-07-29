# Development setup

[Back to documentation](../README.md) · [Contributing](../../CONTRIBUTING.md)

Portable tests require only the .NET SDK. Production builds also require local Orb of Creation managed assemblies and BepInEx 5 build references because those binaries are not committed. BepInEx does not need to be installed into the game for a build: a gitignored staging root may provide the expected layout.

```powershell
$env:OOC_GAME_DIR='C:\Program Files (x86)\Steam\steamapps\common\Orb of Creation'
dotnet build src/OrbModSuite.csproj -c Release
```

Expected external references are documented in the [source layout](../../src/README.md). Run portable tests with:

```powershell
dotnet test tests/OrbModding.Tests/OrbModding.Tests.csproj -p:UseGameStubs=true
```

Stub-linked outputs are isolated under `bin-stubs/` and `obj-stubs/`. Never deploy them to BepInEx. For installed-game checks, continue with the [testing hub](../testing/README.md) and the [runtime validation protocol](../testing/runtime-validation.md).

On Linux or macOS, stage the same layout under ignored `lib/`, point
`lib/Orb Of Creation_Data/Managed` at the platform's real Managed directory, and place the official
BepInEx 5 `core` files under `lib/BepInEx/core`. Then build without PowerShell:

```bash
export OOC_GAME_DIR="$PWD/lib"
dotnet build src/OrbModSuite.csproj -c Release
```

Keep user-specific absolute paths out of tracked files. Staging and building do not authorize copying
anything into the game installation.

For an explicitly authorized local smoke-test installation, use one of the supported installer modes:

```bash
./script/install release
./script/install perf-debug
```

`release` builds the ordinary Release assemblies without ServiceCycle profiling probes. `perf-debug` builds
Debug assemblies with `EnableServiceCycleProfiler=true`. Both modes refuse to run while the game is open,
run the complete portable gate and installed-game contracts, build the one supported assembly,
reject duplicate ModSuite DLLs and any retired per-plugin DLL still installed, back up active top-level saves and installed DLLs, install
the verified outputs, and print their SHA-256 hashes. Set `OOC_GAME_DIR` or `OOC_SAVE_DIR` when local Steam
discovery does not match the installation. The command never packages, tags, publishes, or launches the game.

From a clean commit, rehearse the complete supported-suite package on macOS or Linux with the same ignored
staging root:

```bash
./script/package
```

The command runs the bounded portable gate, installed contracts, and all real-reference Release builds
before creating an allowlisted ZIP and SHA-256 manifest under `artifacts/releases/`. It never writes to the
game installation and refuses to overwrite an existing rehearsal for the same suite version.
