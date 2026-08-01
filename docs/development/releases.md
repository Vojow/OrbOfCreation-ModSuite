# Public release checklist

[Runtime validation](../testing/runtime-validation.md) ·
[Release procedure](../releasing.md)

`main` owns publication. An ordinary main merge becomes a game-free beta;
changing `VERSION` in a direct maintainer `release:` commit becomes a full
release. GitHub Actions alone creates annotated tags and GitHub releases.

## Candidate review

Before a full promotion, record:

- the exact clean commit and intended stable version;
- the audited game, BepInEx, and source assembly hashes;
- portable, profile, refs-contract, installed-contract, and interactive evidence;
- the Release build's zero-profile-string result;
- exact Release and perf-debug asset names and SHA-256 values;
- configuration schema and reconciled test, contract, exemption, entity, and
  compiler-warning counts; and
- known limitations and validation scope.

Build success alone is not publication approval.

## Draft and curate

On clean, current `main`, run:

```bash
./script/promote <new-stable-version>
```

The helper updates all version surfaces and drafts a new topmost changelog
section from merged pull-request titles and bodies since the tag named by the
old `VERSION`. Review every entry, remove implementation detail, group changes
for players, and add supported-game and compatibility information where needed.
The helper creates no commit, tag, push, or release.

PRs do not edit `CHANGELOG.md` or `VERSION`, and the changelog has no
`Unreleased` section. After local gates and review, the maintainer commits the
draft directly with a subject beginning `release:` and pushes it to `main`.

## Local installed-game review

Run the portable gate first and installed contracts second, never in parallel.
Install and playtest with `./script/install release`; this installs a DLL built
from the committed full-surface refs, exactly as CI does. The hand-written test
stubs are never an install or publication input.

Use a clean BepInEx profile and freshly generated configuration. Complete the
applicable V0–V7 [runtime validation](../testing/runtime-validation.md),
including save backup and restoration, representative automation, emergency
and lifecycle behavior, native postconditions, configuration Apply/Revert,
compatibility quarantine, quiet logs, and claimed desktop/Steam Deck behavior.

Installed contracts remain the private proof against the real audited game.
The ReleaseAssemblyCheck faithfulness comparison is unchanged but runs only
when refs are regenerated for another game version; see
[the release procedure](../releasing.md#reference-change-faithfulness).

## CI publication review

For every main push, confirm the publication workflow:

- passed portable, profile, refs-contract, promotion-policy, and manifest-based
  repository-hygiene gates without a game installation;
- built unmistakably named `OrbModSuite-release.dll` and
  `OrbModSuite-perf-debug.dll` assets from committed refs;
- proved the Release flavor contains zero profiling components; and
- refused any pre-existing target tag or GitHub release.

For a beta, verify tag `suite-v<VERSION>+main.<N>`, prerelease status, and notes
from the associated merged PR body (or merge-message fallback). For a full
release, verify tag `suite-v<VERSION>`, notes from the matching changelog
section, and byte-identical Ubuntu/Windows rebuilds of both flavors.

`script/package` remains the supported local archive rehearsal for manual
installation. It is not a publication owner and its archive is not one of the
two automatic release assets.

Replacing or deleting an existing public release or tag requires explicit
maintainer authorization naming that target.
