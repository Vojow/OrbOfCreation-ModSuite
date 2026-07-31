# Releasing Orb Of Creation ModSuite

This is the release procedure for Vojow, the upstream repository owner. Run it from Git Bash on
Windows, in a clean checkout of the merged release commit. The script builds and publishes the
checked-out commit; it does not select another branch or commit for you.

## Prerequisites

- .NET SDK 10 (`dotnet --version` must begin with `10.`).
- GitHub CLI installed and authenticated for GitHub (`gh auth login`).
- Orb of Creation installed with a complete BepInEx 5 setup, and the game closed.
- A clean checkout of the merged release commit, with `origin` pointing at the GitHub repository
  that should receive the tag and release.
- `shasum` or `sha256sum`. Git for Windows supplies the other shell tools used by the script.

The game-directory lookup follows the supported installer and also checks the two standard Windows
Steam locations. If it does not find the install, set `OOC_GAME_DIR`; Windows paths are normalized
through `cygpath`:

```bash
export OOC_GAME_DIR='C:\Program Files (x86)\Steam\steamapps\common\Orb of Creation'
```

## Recommended sequence

Always rehearse the exact commit first:

```bash
tools/release.sh 0.5.0 --dry-run
```

Read the printed commit, repository target, tag, title, asset path, and DLL SHA-256. If all are
correct, run the real release:

```bash
tools/release.sh 0.5.0
```

The real run repeats every check, gate, and build. Before anything irreversible it prints the
GitHub repository again and asks you to retype the version. It then:

1. creates annotated tag `suite-v<version>` at the checked-out commit;
2. pushes that tag to `origin`; and
3. creates the GitHub release in the repository resolved from `origin`, using the matching
   changelog heading as the title and that section's text as the notes.

`OrbModSuite.dll` is the attached asset. A version with a prerelease suffix is marked as a GitHub
prerelease.

## What the script verifies

Before the gate, the script verifies:

- the version is valid SemVer;
- Git, GitHub CLI, .NET 10, process-inspection tools, and a SHA-256 tool are available;
- GitHub CLI is authenticated;
- the tracked working tree is clean and HEAD can be fixed as the release source;
- `origin` resolves through `gh repo view`, and the exact repository is printed;
- the local checkout and `origin` do not already contain `suite-v<version>`;
- `CHANGELOG.md` contains exactly one matching section with non-empty notes;
- the project, package, informational, loader, file, and in-game release versions all agree;
- the game directory contains the required game, Unity, BepInEx, and Harmony assemblies; and
- Orb of Creation is not running. On Windows the native PowerShell process query runs before any
  POSIX fallback and fails closed if inspection itself fails.

The gate runs the source and ordinary test-project stub builds, then `./script/test` for the full
portable and profile suites, followed by installed-game contracts against the located game. The
script then performs the same non-profile Release build used by the supported installer, rejects
profiling components in the output, and prints the DLL SHA-256. It checks that HEAD and every tracked
file stayed unchanged after both the gate and the build.

## What it refuses to do

There are no skip flags. A dirty tracked tree, changed HEAD, version mismatch, missing changelog
section, missing tool or game assembly, unauthenticated GitHub CLI, running game, existing tag,
failed gate, failed build, or mistyped confirmation stops the release. The script never forces a
tag or push and never replaces an existing release.

`--dry-run` performs every preflight, gate, and build, but creates no tag, push, or GitHub release.
If a real run fails after pushing the tag, the script leaves that tag in place and reports the
failed publish step; it does not delete or rewrite published state.

For maintainers, the release helpers are checked without invoking the release entry point:

```bash
bash -n tools/release.sh
bash -n tools/test-release-helpers.sh
tools/test-release-helpers.sh
```

SteamCMD-based CI release automation is the planned follow-up so future releases no longer depend
on any one person's game installation or machine.
