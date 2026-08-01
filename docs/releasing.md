# Releasing Orb Of Creation ModSuite

`main` is continuously publishable. Every push to `main` runs the game-free
publication workflow from the committed, SHA-locked game references and the
SDK pinned by `global.json`. An ordinary merge publishes a beta. A maintainer's
`release:` commit that changes `VERSION` publishes a full release.

There is no local tag or release command. GitHub Actions creates the annotated
tag and the GitHub release after every required gate passes.

## Publication inputs

`VERSION` contains the last released stable version. It does not predict the
next release. The same version must appear in:

- `Directory.Build.props` as `SuiteVersion`;
- `src/OrbModSuite.csproj` as the package and assembly versions; and
- `src/Common/PluginIds.cs` as the BepInEx and displayed release versions.

`tools/build-release-assets.sh` refuses inconsistent version surfaces. Both
published flavors compile from `lib/game-refs/v1.0.5`; neither Steam nor a game
installation is a CI input:

- `OrbModSuite-release.dll` is the normal Release build and must contain no
  ServiceCycle profiling components.
- `OrbModSuite-perf-debug.dll` is the Debug profiling build intended for the
  supported `./script/install perf-debug` diagnostic flow.

These references are full-surface metadata derivations of audited assemblies,
not the hand-written test stubs. Test stubs remain portable runtime and compile
doubles and are never installed or published.

## Beta on every main merge

When a push leaves `VERSION` unchanged, `.github/workflows/release.yml`:

1. runs the portable, profile, refs-contract, release-policy, and repository
   hygiene gates;
2. builds both publication flavors from committed refs on Ubuntu;
3. derives the commit count from
   `git describe --long --match suite-v<VERSION>`;
4. creates annotated tag `suite-v<VERSION>+main.<N>`; and
5. creates a GitHub prerelease with both DLLs and their checksum file.

The prerelease notes are the body of the pull request associated with the main
commit. If GitHub reports no usable pull-request body, the merge commit message
is used. A rerun cannot overwrite publication state: an existing tag or release
fails loudly.

## Full release by VERSION bump

Start from an up-to-date, clean `main` checkout and ask the helper to draft the
next stable version:

```bash
./script/promote 0.6.0
```

The helper reads merged pull-request titles and bodies since
`suite-v<VERSION>`, adds a dated release section at the top of `CHANGELOG.md`,
and updates `VERSION` plus every maintained version surface. It changes only
the working tree. It never commits, tags, pushes, or creates a GitHub release.

Curate the drafted changelog into player-facing release notes. Pull requests
never edit released changelog sections and there is no `Unreleased` section.
Run the private gates below, then make one direct promotion commit whose subject
starts with `release:`:

```bash
git commit -am "release: promote 0.6.0"
git push origin main
```

The push changes `VERSION`, so the main workflow takes the full-release path.
It runs the game-free gates, independently builds both flavors on Ubuntu and
Windows, and requires exact byte equality for each DLL. Only then does it create
annotated tag `suite-v<new VERSION>` and a full GitHub release whose notes come
from the matching `CHANGELOG.md` section. There is no SHA stored in the tag:
the trust anchor is the reproducibility comparison, the committed reference
manifest, and the pinned SDK.

The release-policy CI job rejects any pull request that changes `VERSION` or
`CHANGELOG.md`. On main, any such change must belong to a commit whose subject
starts with `release:`. It also rejects an `Unreleased` heading. The one-time
bootstrap that introduced `VERSION=0.5.0` is accepted only because
`suite-v0.5.0` already exists in that commit's history.

## Private pre-promotion gates

Before pushing a full-release commit, run the complete local gate serially:

```bash
PATH=/usr/local/share/dotnet:$PATH \
  ORB_TEST_TIMEOUT_SECONDS=600 ORB_TEST_ATTEMPTS=1 ./script/test

PATH=/usr/local/share/dotnet:$PATH \
  OOC_GAME_DIR=/absolute/path/to/audited/game-v105 \
  dotnet test tests/OrbModding.GameContractTests/OrbModding.GameContractTests.csproj \
  --configuration Release

PATH=/usr/local/share/dotnet:$PATH \
  env -u OOC_GAME_DIR dotnet build src/OrbModSuite.csproj \
  --configuration Release --disable-build-servers -m:1 --no-incremental \
  -p:EnableServiceCycleProfiler=false -p:ContinuousIntegrationBuild=true
```

Install and playtest the refs-built Release artifact—the bytes built from the
committed refs are what ship:

```bash
./script/install release
```

The installed-contract gate still pins the real audited game. CI does not
pretend metadata-only refs repeat that private evidence. Complete the applicable
V0–V7 [runtime validation protocol](testing/runtime-validation.md) and record
the installed game, BepInEx, assembly hashes, configuration schema, test and
contract counts, warnings, and known limitations before promotion.

## Reference-change faithfulness

Real-game versus refs-built faithfulness is a reference-generation gate, not a
per-release ceremony. Whenever `lib/game-refs` is regenerated for a new audited
game version, run `tools/make-game-refs.sh`, clean-build the suite once against
the real audited closure and once against the regenerated refs, retain both
DLLs, and invoke:

```bash
dotnet run \
  --project tools/OrbModding.ReleaseAssemblyCheck/OrbModding.ReleaseAssemblyCheck.csproj \
  --configuration Release -- \
  /path/to/refs-built/OrbModSuite.dll \
  /path/to/game-built/OrbModSuite.dll
```

`ReleaseAssemblyCheck` permits only the known compiler debug-identity regions
(COFF timestamp, PE checksum, module MVID, and debug-directory payloads) to
differ. Any IL, metadata, reference identity, resource, attribute, or other byte
difference blocks the refs update. This check belongs in the same reviewed
change as regenerated refs and their manifest; routine releases do not rerun it.

## Failure and recovery

There are no get-or-create or overwrite paths. Invalid promotion ownership,
version inconsistency, missing changelog notes, a failed test or hygiene gate,
profile leakage, a non-reproducible stable build, or an existing tag or release
stops publication.

If tag creation succeeds but GitHub release creation later fails, do not delete,
replace, or move the public tag without explicit maintainer authorization naming
that target. Diagnose the failed workflow and choose recovery deliberately.

Local helper checks are safe and publication-free:

```bash
bash -n tools/release-common.sh
bash -n tools/check-release-policy.sh
bash -n tools/build-release-assets.sh
bash -n script/promote
tools/test-release-helpers.sh
```
