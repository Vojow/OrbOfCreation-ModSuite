# Public release checklist

[Runtime validation](../testing/runtime-validation.md) ·
[Release procedure](../releasing.md)

The split publication procedure is documented in
[Releasing Orb Of Creation ModSuite](../releasing.md). `tools/release.sh` runs the private
installed-game and faithfulness gates, proves the committed-reference build is reproducible,
records its SHA-256 in an annotated tag, and pushes the tag. The tag workflow independently
rebuilds those exact public bytes and owns GitHub release creation. This page is the review
checklist that precedes that handoff.

## Supported package

`script/package` additionally packages the canonical committed-reference DLL in a standalone
distribution archive for manual installs:

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
- portable, committed-reference reproducibility, real-game faithfulness,
  installed-contract, and interactive evidence; and
- known limitations.

Build success alone is not publication approval.

## Package gate

Run `./script/package` from a clean commit with `OOC_GAME_DIR` pointing at a game
installation. It refuses a dirty or moved working tree, runs the bounded portable gate and
installed-game contracts, then builds the archive's DLL from the committed metadata-true
references. The hand-written test stubs are never a package input.

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

Create the tag only from the reviewed clean commit through `tools/release.sh`. The annotated
tag records the canonical DLL SHA-256. The `suite-v*` workflow must reproduce that exact hash
before it creates the release and attaches `OrbModSuite.dll`, the supported archive, and its
checksum manifest; a pre-existing release or hash mismatch fails publication. Release notes must state
the supported game/BepInEx baseline, the audited assembly baselines, important behavior
changes, known limitations, and validation scope. A release that changes the plugin GUID or
the configuration schema must say so as a breaking change, because settings do not migrate
across a GUID change.

Replacing or deleting an existing public release or tag requires explicit maintainer
authorization naming that target.
