# Public release checklist

[Back to roadmap](../plans/roadmap.md) · [Runtime validation](../testing/runtime-validation.md) ·
[Release procedure](../releasing.md)

The publication procedure itself — building, tagging, and creating the GitHub release with
`tools/release.sh` — is documented in [Releasing Orb Of Creation ModSuite](../releasing.md). The
published GitHub release attaches the single `OrbModSuite.dll` asset. This page is the review
checklist that precedes it.

## Supported package

`script/package` additionally builds a standalone distribution archive for manual installs:

```text
OrbOfCreation-ModSuite-<SuiteVersion>.zip
|-- BepInEx/plugins/OrbModSuite/OrbModSuite.dll
|-- README.md
|-- CHANGELOG.md
|-- LICENSE
`-- THIRD_PARTY_NOTICES.md
```

There is one version. `SuiteVersion` in `Directory.Build.props` names the archive;
`<Version>` in `src/OrbModSuite.csproj` and `PluginIds.Version` name the assembly and the
loaded plugin, and the packaging script fails if the two disagree. There are no
per-component versions to reconcile.

## Candidate review

Before publishing, record:

- exact clean commit and intended tag;
- prerelease or stable status;
- suite version and plugin GUID;
- installed game, BepInEx, and audited assembly hashes;
- exact archive entries and SHA-256 checksums;
- portable, real-reference, installed-contract, and interactive evidence; and
- known limitations.

Build success alone is not publication approval.

## Package gate

Run `./script/package` from a clean commit with `OOC_GAME_DIR` pointing at a game
installation. It refuses a dirty or moved working tree, then runs the bounded portable
gate, the installed-game contracts, and a real-reference Release build before staging the
archive.

It fails the release when:

- the built output is missing `OrbModSuite.dll`, or game/loader assemblies leaked beside it;
- the archive entries differ from the five expected paths above in either direction;
- the project version and `PluginIds.Version` disagree; or
- an output archive or checksum file for that version already exists.

Confirm by inspection that no game, Unity, Harmony, BepInEx, test-stub, debug-symbol, save,
configuration, trace, or experimental artifact reached the archive, and verify the generated
checksums against the final file.

## Runtime gate

Use a clean BepInEx profile and a freshly generated configuration file. Complete the
applicable V0–V7 [runtime validation](../testing/runtime-validation.md), including:

- save backup, reload, removal, and restoration;
- representative automation and Mentor behavior;
- emergency, lifecycle, ownership, queue, reserve, and native postcondition checks;
- the configuration UI's Apply/Revert and Runtime-page behavior;
- a load-gate check: confirm the suite loads against the audited baseline, enters control-plane-only
  quarantine against an unknown complete pair, resets stale acknowledgement after either hash
  changes, and refuses an incomplete assembly audit;
- quiet-log acceptance; and
- representative desktop and Steam Deck/Proton performance where claimed.

## Publication

Create the tag and artifacts only from the reviewed clean commit. Release notes must state
the supported game/BepInEx baseline, the audited assembly baselines, important behavior
changes, known limitations, and validation scope. A release that changes the plugin GUID or
the configuration schema must say so as a breaking change, because settings do not migrate
across a GUID change.

Replacing or deleting an existing public release or tag requires explicit maintainer
authorization naming that target.
