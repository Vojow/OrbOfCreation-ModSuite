# Versioned configuration schemas

> **Lifecycle: Next beta / runtime validation pending.** Common owns the transactional schema boundary; Automata, Mentor, and Mod Config declare schema version 1 before normal typed binding. Portable coverage is implemented, while interactive UI and installed-game startup validation remain release gates.

[Back to lifecycle index](README.md) · [Configuration and safety](../user-guide/configuration.md)

## Goal

Give every supported plugin an explicit, monotonic configuration schema without guessing how unknown values should migrate. A plugin either loads a reviewed current configuration, completes a reviewed transaction, or fails closed before gameplay or UI behavior starts.

## Shared transaction contract

Each supported plugin owns hidden `[OrbModding] ConfigurationSchemaVersion=1`. Absence means schema zero. A malformed or negative marker fails; a marker greater than the supported version is treated as a future schema and remains read-only.

Migration runs before the plugin's normal typed binds:

1. Snapshot exact original file existence and bytes plus the initially known BepInEx keys.
2. For an existing file, write and flush a uniquely owned sibling temporary, verify exact length, bytes, and SHA-256, then atomically publish the first free backup name (`.pre-schema-v1.bak`, then `.bak.2`, and so on). A write, flush, or verification failure deletes the owned temporary and aborts migration; a genuine destination race retries the next suffix without touching the winner. A fresh file has no original bytes to back up.
3. Disable `SaveOnConfigSet`, consume only the reviewed keys through temporary string binds, and execute every ordered one-version step.
4. Perform the plugin's normal typed binds, bind and set the hidden marker last, then save once.
5. On failure, remove entries added during the attempt, restore the original bytes exactly (or delete a file created by the attempt), reload the configuration, publish a failed status, and return no usable configuration.
6. Restore the caller's original `SaveOnConfigSet` value on every path.

A file already at version 1 performs no migration step, backup, or save. Failure reasons come only from a closed safe-reason set or a generic transaction category; status and logs never include configuration paths or serialized values.

## Automata schema zero to one

Automata performs only these proven transformations:

- `AutoConcept.Mode=BalanceMastery` becomes `Active`; `Disabled` and `Active` are canonicalized; every other value fails.
- `AutoConcept.FallbackEvaluationIntervalSeconds` has precedence over `RebalanceIntervalSeconds`, which has precedence over `RebalanceIntervalMinutes`.
- Seconds require invariant non-negative integers. Minutes require invariant finite non-negative numbers, convert to seconds, and round midpoint values away from zero.
- The resulting interval is clamped to 10-1800 seconds.
- The explicit obsolete-key allowlist is discarded with a diagnostic and without speculative remapping.

Mentor and Mod Config use marker-only zero-to-one steps: their existing values retain their normal typed-bind behavior.

## Status projection

Common publishes an exact-plugin-GUID status with `Current`, `Migrated`, `Failed`, or `Future`, from/to versions, saved and loaded flags, a sanitized reason, and whether a backup was created. Each subscriber is isolated so one throwing observer cannot prevent later observers from receiving the transition. A publisher may run off-thread; Mod Config's callback performs only an atomic dirty mark, while its Unity tick consumes that latch and performs the status projection.

Orb Mod Config projects this in a dedicated band above the existing runtime-health band. Loaded exact-GUID plugins with schema status remain selectable even when failure or future-version refusal left no visible configuration sections; those entries are status-only and expose no editors or Apply action. Third-party plugins with neither visible settings nor schema status remain omitted. Schema state, runtime health, and the staged Apply result are deliberately separate claims.

## Validation gates

- Portable coverage: fresh and current files; every reviewed mapping and precedence branch; malformed, negative, non-finite, and future inputs; backup suffix and race collisions; partial-write and flush cleanup; first and repeated reload faults; exact byte restoration; newly-created-file deletion; observer isolation; worker-thread atomic UI handoff; status-only exact-GUID catalog entries; and path/value privacy.
- Installed-game contracts: all supported plugins compile and bind against the installed BepInEx configuration API.
- Interactive desktop and Steam Deck: the new schema band remains readable at 1280×720, 1280×800, and 1920×1080; Current, Migrated, Failed, and Future states remain distinct from runtime health; a failed/future configuration starts no affected plugin behavior.

Runtime approval must be recorded through the normal [runtime validation](../testing/runtime-validation.md) process before this lifecycle can advance.
