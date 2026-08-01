# Releasing Orb Of Creation ModSuite

This procedure has two owners. The maintainer's local `tools/release.sh` runs
the private installed-game gates, proves the canonical build, creates an
annotated tag, and pushes it. The tag-triggered GitHub workflow independently
rebuilds the canonical DLL from public inputs and creates the GitHub release.

The release DLL is reproducible from the repository alone: the committed
metadata-true game references plus the exact SDK pinned by `global.json`
produce the release bytes whose SHA-256 is recorded in the tag annotation. The
canonical commands set `ContinuousIntegrationBuild=true`, which maps
SourceLink document roots to `/_/` instead of embedding the checkout path.

To reproduce those public bytes without the private faithfulness gate:

```bash
env -u OOC_GAME_DIR dotnet clean src/OrbModSuite.csproj --configuration Release \
  -p:EnableServiceCycleProfiler=false -p:ContinuousIntegrationBuild=true
env -u OOC_GAME_DIR dotnet restore src/OrbModSuite.csproj --force-evaluate \
  --disable-build-servers -p:EnableServiceCycleProfiler=false \
  -p:ContinuousIntegrationBuild=true
env -u OOC_GAME_DIR dotnet build src/OrbModSuite.csproj --configuration Release \
  --disable-build-servers -m:1 --no-incremental --no-restore \
  -p:EnableServiceCycleProfiler=false -p:ContinuousIntegrationBuild=true
```

## Prerequisites

- The exact .NET SDK selected by `global.json`.
- GitHub CLI installed and authenticated for GitHub (`gh auth login`).
- Orb of Creation installed with a complete BepInEx 5 setup, and the game
  closed.
- A clean checkout of the merged release commit, with `origin` pointing at the
  GitHub repository that should receive the tag.
- `shasum` or `sha256sum`. Git for Windows supplies the other shell tools used
  by the script.

The game-directory lookup follows the supported installer and checks standard
Steam locations. If it does not find the install, set `OOC_GAME_DIR`; Windows
paths are normalized through `cygpath`:

```bash
export OOC_GAME_DIR='C:\Program Files (x86)\Steam\steamapps\common\Orb of Creation'
```

## Recommended sequence

Install and playtest the exact kind of artifact that will ship:

```bash
./script/install release
```

Release mode runs the installed-game gate but builds the installed DLL from
the committed references. Those references are full-surface metadata
derivations of the audited assemblies, not the hand-written test stubs. The
rule against installing stub-linked DLLs remains unchanged.

Rehearse the exact clean commit:

```bash
tools/release.sh 0.5.0 --dry-run
```

Read the printed commit, repository target, tag, title, canonical DLL SHA-256,
faithfulness result, and reproducibility result. If all are correct, run:

```bash
tools/release.sh 0.5.0
```

The real run repeats every check. Before publishing anything it prints the
repository again and asks the maintainer to retype the version. It then:

1. creates annotated tag `suite-v<version>` at the checked-out commit, with
   `OrbModSuite.dll SHA-256: <digest>` in the tag message; and
2. pushes that tag to `origin`.

It stops there. It does not call `gh release create`. The pushed tag starts
`.github/workflows/release.yml`, which owns release creation.

## The two release-build gates

### Faithfulness

The script builds Release once against the audited real game installation and
once against the committed references. Refasmer changes physical reference
images, so Roslyn's portable debug identity changes even when compilation is
semantically identical. The faithfulness verifier therefore zeros only:

- the COFF timestamp and PE checksum;
- the module MVID; and
- the PE debug directory and its payloads, including CodeView/PDB identity.

Every remaining DLL byte must match. A different IL body, metadata row,
AssemblyRef, resource, attribute, or other non-debug byte fails the release.
For the accepted baseline, the refs-built and game-built DLLs differ only in
those debug-identity fields.

### Reproducibility

The script cleans, restores, and builds the committed-reference Release
artifact twice. The two complete DLLs must be byte-identical. The second build
is the canonical artifact, and its full SHA-256 is written into the annotated
tag message.

`global.json` is part of this contract: changing or floating the SDK changes
the release input and is not allowed during a release.

## What the local script verifies

Before the build gates, `tools/release.sh` verifies:

- the version is valid SemVer and all project, assembly, package, loader, and
  in-game versions agree;
- Git, GitHub CLI, the pinned SDK, process-inspection tools, and a SHA-256 tool
  are available;
- GitHub CLI is authenticated;
- the tracked working tree is clean and HEAD stays fixed;
- `origin` resolves through `gh repo view`, and neither the local checkout nor
  `origin` already contains the tag;
- `CHANGELOG.md` contains exactly one matching section with non-empty notes;
- the game directory contains the audited game, Unity, BepInEx, and Harmony
  assemblies; and
- Orb of Creation is not running.

It then runs the source and ordinary test-project stub builds, the complete
portable/profile gate, and installed-game contracts against the real audited
assemblies before running the faithfulness and reproducibility gates above.

## What the tag workflow verifies

For `suite-v*`, GitHub Actions:

1. requires an annotated tag with exactly one canonical DLL SHA-256 line;
2. runs the same hand-written-stub portable lanes as ordinary CI;
3. builds Release with no game, Steam, or private secret, using only the
   committed refs and pinned SDK;
4. fails unless the built DLL SHA-256 exactly matches the tag annotation;
5. packages through `./script/package` in reference-only mode and rechecks the
   packaged DLL hash;
6. extracts the matching changelog section through the same sourced helper as
   `tools/release.sh`; and
7. creates the GitHub release, marks SemVer suffixes as prereleases, attaches
   the DLL and supported package artifacts, and includes the DLL SHA-256 in the
   release body and job output.

The workflow never substitutes for installed-game contracts. Those stay
private and pre-tag because metadata-only references cannot prove the original
assembly hashes. The workflow is the only release creator; an existing release
causes `gh release create` to fail loudly.

## Failure behavior

There are no skip flags. A dirty tracked tree, changed HEAD, version mismatch,
missing changelog section, missing tool or game assembly, unauthenticated
GitHub CLI, running game, existing tag, failed portable or installed gate,
faithfulness difference outside debug identity, non-reproducible refs build, or
mistyped confirmation stops the local flow.

`--dry-run` performs every preflight and build gate but creates no tag or push,
so no workflow runs. If a real run fails after pushing the tag, it leaves that
published tag in place and reports the workflow-owned release as incomplete;
it never deletes or rewrites published state.

For maintainers, check the release helpers without invoking the entry point:

```bash
bash -n tools/release-common.sh
bash -n tools/release.sh
bash -n tools/test-release-helpers.sh
tools/test-release-helpers.sh
dotnet build tools/OrbModding.ReleaseAssemblyCheck/OrbModding.ReleaseAssemblyCheck.csproj \
  --configuration Release
```
