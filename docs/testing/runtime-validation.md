# Local runtime validation protocol

[Back to testing hub](README.md) · [Repository strategy](strategy.md) · [Module test guides](README.md#module-guides)

## Purpose

Portable tests cannot prove that a plugin compiles against, loads into, or behaves correctly with the installed game. Runtime validation therefore proceeds through ordered gates. A failure stops the affected module at that gate; a later success never excuses an earlier failure.

## Safety rules

- Close the game before copying, restoring, or hashing saves and installed DLLs.
- Never edit an active save file.
- Use a disposable save for active automation checks.
- Back up the save, configuration, installed plugins, and log before mutation testing.
- Install only the supported plugin allowlist and remove overlapping automation mods.
- Begin with gameplay modules disabled and `Safety.EmergencyDisable = true`.
- A rendered game without successful BepInEx chainloader records is not a mod load.

## Gate V0 — repository and real-reference build

1. Run `./script/test`.
2. Stage the installed game and BepInEx references under ignored `lib/` or set `OOC_GAME_DIR`.
3. Run installed-game contract tests.
4. Build every supported project in Release against those references.
5. Rehearse the supported-suite package and inspect its explicit DLL allowlist.

Record the commit, game version, assembly hashes, BepInEx version, project versions, and command results.

## Gate V1 — static contract audit

Review every changed reflection or Harmony boundary against the admitted game assemblies. Confirm:

- member names, signatures, and declaring types;
- stable UUID plus expected native type identity;
- main-thread ownership for Unity objects and game APIs;
- queue, cost, availability, completion, and mutation postconditions;
- lifecycle invalidation of cached native references; and
- restoration of native global state on every exit.

Unknown or contradictory evidence must fail closed.

## Gate V2 — load smoke test

With gameplay disabled and emergency stop engaged:

1. launch to the title screen;
2. confirm BepInEx and each expected plugin load exactly once;
3. confirm configuration generation/migration succeeds;
4. open Orb Mod Config and its Runtime page;
5. load a disposable save and return to the title screen; and
6. close the game and inspect the complete log.

Loader, Harmony, duplicate-plugin, schema, lifecycle, or construction errors fail this gate.

## Gate V3 — read-only behavior probes

Keep emergency stop engaged. Verify:

- controls and Runtime cards reflect configured intent separately from runtime health;
- lifecycle transitions settle after title, load, scene, and reset boundaries;
- disabled modules do not scan or rebuild catalogs;
- service diagnostics update without per-frame log noise;
- queue, resource, registry, and feature-health views match visible game state; and
- opening and closing Mod Config does not change gameplay or discard staged settings unexpectedly.

## Gate V4 — isolated active gameplay tests

Enable one capability at a time on a disposable save. Exercise its normal action, a native-not-ready state, an insufficient-resource or capacity state, emergency stop, and a lifecycle transition. Verify the visible game effect, native postcondition, quiet log, and absence of repeated stale actions.

### Auto Harvest

- Test fruit and treasure independently, then together.
- Use naturally unlocked content; do not edit save fields to manufacture readiness.
- Verify one exact native plot action, quantity-one postconditions, sibling isolation, and no replanting or destructive plot behavior.
- Confirm emergency stop prevents new actions and releasing it resumes only when native readiness permits.

### Auto Buy

- Test Structure and Upgrade purchases separately.
- Cover allow/block lists, reserves, queue reservation, each purchase grouping mode, native multi-buy restoration, and live rising costs.
- Confirm one accepted mutation never exceeds queue room or crosses a newly observed reserve/emergency/lifecycle boundary.
- Verify definite pre-call rejection does not starve healthy lower-ranked candidates.

### Auto Cast, Auto Concept, and Spell Leveling

- Verify native readiness, resource, queue, and ownership gates.
- Confirm concept assignment does not churn compatible slots.
- Confirm Spell Leveling remains governed by Auto Buy configuration and uses the audited native purchase behavior.

### Orb Mentor

- Test spells, artifacts, and alchemy independently before combining them.
- Confirm exact configured XP, native progression, recursion suppression, disabled-domain silence, and feature-local failure isolation.

## Optional observability capture

Observability is diagnostic and must not alter gameplay.

- Start and stop manual full traces and performance profiles from the Runtime page; neither uses a fixed capture duration.
- Profiler-enabled debug builds start both sessions automatically when the
  ServiceCycle runtime is created. Closing the game stops them with a
  runtime-shutdown terminal reason and flushes the accepted prefix; manual
  controls remain available for deliberate mid-session windows.
- Let the rolling decision journal run normally.
- After the desired action occurs, stop finite sessions, wait for `Complete`, close the game, and copy the session directories.
- Decode with `./script/trace --full`, `--performance`, `--journal`, or `--dashboard` as appropriate.
- Treat missing manifests, incomplete status, invalid checksums, writer faults, gameplay changes, or main-thread stalls as failures of that diagnostic product.

Manual traces may begin mid-session, so they are not rooted at a known initial state and their manifest marks them `DiagnosticOnly`.

## Gate V5 — persistence and rollback

1. Save and reload after each active module has performed a verified action.
2. Confirm native progression and configuration persist.
3. Exercise configuration Apply/Revert and schema migration using copied configuration files.
4. Close the game, restore the original save/configuration/plugin backups, and verify their hashes.
5. Remove the suite and confirm the unmodded game still loads the save.

## Gate V6 — combined compatibility

Install only the supported suite and enable representative Automata and Mentor features together. Confirm:

- one suite mutation owner per frame;
- no starvation under sustained queue activity;
- action-family ownership prevents overlapping native mutations;
- lifecycle replacement cancels stale work across modules;
- Mod Config remains responsive; and
- logs remain bounded during an extended representative session.

Use the checked performance profile when the change affects scheduling or hot paths. Modeled portable performance remains separate from observed desktop/runtime timing.

## Gate V7 — release candidate

From the exact clean candidate commit:

1. repeat V0;
2. install the exact packaged archive into a clean BepInEx profile;
3. repeat the affected V2–V6 gates;
4. inspect archive entries, versions, hashes, logs, and backup restoration; and
5. record known limitations and prerelease/stable status.

Tagging, publishing, replacing a release, or installing into another game profile requires the owner’s explicit authorization.
