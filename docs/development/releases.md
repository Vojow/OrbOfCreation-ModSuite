# Public release checklist

[Back to roadmap](../plans/roadmap.md) · [Runtime validation](../testing/runtime-validation.md)

## Supported package

The supported suite is an explicit allowlist:

- Orb Automata `0.9.0`
- Orb Mentor `0.3.8`
- Orb Mod Config `0.7.0`
- Orb Modding Common `0.4.0`
- suite archive `0.4.0`

`OrbChronomancer` and `OrbAchievementResonance` are experimental and must not enter a supported archive. Orb Insights and Orb Toolbox are plans, not plugins.

## Candidate review

Before publishing, record:

- exact clean commit and intended tag;
- prerelease or stable status;
- project, plugin, and suite-package versions;
- installed game, BepInEx, and audited assembly hashes;
- supported plugin allowlist;
- exact archive entries and SHA-256 checksums;
- portable, real-reference, installed-contract, and interactive evidence; and
- known limitations.

Build success alone is not publication approval.

## Package gate

1. Run the supported package rehearsal from a clean commit.
2. Reject rooted paths, backslashes, missing `BepInEx/plugins/` entries, unexpected DLLs, duplicate shared dependencies, or inconsistent versions.
3. Confirm no game, Unity, Harmony, BepInEx, test-stub, debug-symbol, save, configuration, trace, or experimental artifact entered the archive.
4. Include only the required DLLs, README, changelog, license, and third-party notices.
5. Verify generated checksums against the final archive.

On POSIX hosts, `./script/package` runs the bounded portable gate, installed contracts, real-reference builds, archive inspection, and checksum generation. The PowerShell package path remains available on Windows.

## Runtime gate

Use a clean BepInEx profile and fresh generated configurations. Complete the applicable V0–V7 [runtime validation](../testing/runtime-validation.md), including:

- save backup, reload, removal, and restoration;
- representative Automata and Mentor behavior;
- emergency, lifecycle, ownership, queue, reserve, and native postcondition checks;
- Mod Config Apply/Revert and Runtime-page behavior;
- quiet-log acceptance; and
- representative desktop and Steam Deck/Proton performance where claimed.

## Publication

Create the tag and artifacts only from the reviewed clean commit. Release notes must state the supported game/BepInEx baseline, included versions, important behavior changes, known limitations, and validation scope.

Replacing or deleting an existing public release or tag requires explicit owner authorization naming that target.
