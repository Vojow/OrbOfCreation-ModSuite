# Public release checklist

[Back to roadmap](../plans/roadmap.md)

## Proposed first public package

Publish a narrow beta before adding another automation domain:

- **Orb Automata 0.4.x:** Auto Buy plus Auto Cast, with release-only Disabled/Active modes and keyboard/queue-adjacent Auto Cast controls.
- **Orb Mod Config 0.4.x:** optional in-game configuration UI.
- **Orb Modding Common:** bundled dependency, not presented as a separate gameplay mod.

Auto Research is deliberately absent from the runtime and public UI. Its legacy keys are removed during config migration. Auto Concept and Auto Harvest remain future modules rather than promises in the first package.

## Release gates, in order

### P0 — must pass before publishing

1. **Clean-install test:** fresh BepInEx profile, fresh generated configs, no development backups or duplicate DLLs, and no game assemblies in the package.
2. **Quiet-log acceptance:** a normal 10-minute Active session writes plugin setup plus warnings/errors only; no per-purchase, batch, queue-wait, UI-open, or config-apply chatter when operational logging is disabled.
3. **Save and removal safety:** back up a normal save, run Auto Buy, save/reload, then remove both mods and verify the game and save still load normally.
4. **Queue/CPU matrix:** test `FillAvailableQueue` at 4, 12, and 16 ms and at representative game-speed multipliers. Confirm the queue stays useful without sustained frame stalls.
5. **Purchase-family matrix:** Structures only, Upgrades only, both, allowlist, blocklist, independent structure/upgrade affordability modes, zero and non-zero reserves, emergency disable, live Bulk Development changes, and action multiplier off/on with queue-room capping.
6. **Supported installation:** clean game, Automata alone, and Automata plus Mod Config. Verify documentation clearly marks concurrent auto-buy plugins as unsupported.
7. **UI smoke matrix:** at least 1920×1080 and one smaller resolution; open/close/restore native tabs, edit/apply/revert, scroll every public section, and verify hidden legacy settings never appear.
8. **Game-build guard:** document the audited assembly hashes and make a mismatched game build warn clearly before Active mode is recommended.

### P1 — package quality

0. Build public archives from an explicit release allowlist. Experimental plugins must remain excluded until their status is formally promoted and the release scope explicitly approves them; package generation must fail if an experimental DLL is present.
1. Produce a versioned zip with only the required DLLs, README, changelog, license, and install/uninstall instructions. Inspect raw ZIP entries and reject backslashes, rooted paths, or missing `BepInEx/plugins/` entries so extraction works on SteamOS/Bazzite.
2. Add a first-run section explaining Disabled/Active modes, affordability, optional reserves, queue slots, Bulk Development, action multiplier behavior, and the emergency stop.
3. Add a troubleshooting section covering log opt-in, duplicate DLLs, conflicting auto-buy plugins, and restoring the previous version.
4. Capture two screenshots: the Mods/Automata configuration page and a correctly maintained native queue.
5. Write release notes that explicitly call the build a beta and list the exact supported game/BepInEx versions.
6. Tag the release commit and build artifacts from a clean working tree.

### P2 — after the first beta

1. Add conditional UI visibility so Fixed-only and verbose-logging-only fields appear only when relevant.
2. Add friendly display labels (`Auto Buy`, `CPU Budget`, `Allowed UUIDs`) without renaming persisted config keys.
3. Add an unsaved-changes prompt when leaving Mods.
4. Add ranked-snapshot caching only if runtime profiling still shows candidate scanning as a material queue bottleneck.
5. Expand Auto Cast to channel-aware scheduling only after beta feedback confirms the instant, aura, and toggle behavior is stable.

## Recommended release decision

Do not add another gameplay feature before P0 passes. The highest-value next work is a clean-install/package rehearsal followed by the 10-minute quiet-log and queue/CPU soak. Those tests validate the actual public experience and are more important than expanding scope.
