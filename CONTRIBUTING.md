# Contributing

Thanks for helping improve OrbOfCreation-ModSuite. This is an unofficial BepInEx
5 project for the Windows Mono build of Orb of Creation.

## Branches and scope

`develop` is the integration branch. Open small, focused pull requests against
`develop`; `main` carries released versions. Release work follows both the
[release procedure](docs/releasing.md) and the
[release review checklist](docs/development/releases.md).

Keep each pull request to one coherent change. Commit subjects use a lowercase
area prefix followed by an imperative summary, for example `autoharvest: validate
prerequisites`, `ui: repair native navigation`, `build: isolate outputs`, or
`docs: remove retired guidance`.

Do not commit game assemblies, BepInEx binaries, save files, local configuration,
decompiler output, credentials, user-specific paths, or other proprietary game
assets. Sanitize logs before attaching them.

## Engineering rules

Read the [engineering doctrine](docs/development/engineering-doctrine.md) and the
[game-boundary doctrine](docs/runtime-architecture/game-boundary-doctrine.md)
before changing runtime behavior. Preserve the game's progression, queues, saves,
and player control. Convert a live defect into a portable red-green regression
whenever its contract can be represented faithfully.

## Pull requests

Use the release PR #101 shape:

- `Why?` — the problem, user impact, and why this scope is the right unit;
- `How?` — the implementation and verification approach;
- `Decisions` — durable rulings and rejected alternatives that future work must
  not re-derive;
- `Callouts` — reviewer attention, runtime evidence, limitations, or follow-up
  work that materially affects review.

Omit `Decisions` or `Callouts` when there is nothing meaningful to say. Include
the exact commands and reconciled counts from the current tree.

## Required gate

Run these lanes serially; do not overlap builds or test processes:

```bash
ORB_TEST_ATTEMPTS=1 ./script/test
OOC_GAME_DIR=/path/to/audited/game dotnet test \
  tests/OrbModding.GameContractTests/OrbModding.GameContractTests.csproj \
  --configuration Release
```

The first command runs ordinary portable tests, the isolated profile tests, and
the profiled trace-tool build. The second verifies the audited assembly pair and
every installed native contract. Reconcile ordinary, profile, installed,
manifest-schema, contract, source-exemption, known-entity, and compiler-warning
counts with the target branch; explain every delta.

Portable success proves behavior against the source-only stubs, not Unity or the
installed game. Follow the [runtime validation protocol](docs/testing/runtime-validation.md)
for behavior that depends on native state, UI, Harmony, save/load, or player
control. Never install a stub-linked DLL.

By contributing, you confirm that you have the right to submit the contribution
under the repository's license.
