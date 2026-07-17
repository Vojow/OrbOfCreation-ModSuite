# Source-specific agent guidance

This file supplements the repository-root `AGENTS.md` for work under `src/`.

## Before changing a plugin

- Read that plugin's README and applicable file under `docs/plans/`.
- Identify whether the plugin is supported, beta, planned, or experimental.
- Inspect its portable tests and installed-game contract tests before changing reflected calls.
- Keep shared abstractions small. Do not move gameplay ownership into `OrbModding.Common` merely for convenience.

## Native adapters

- Resolve exact overloads and validate return/field types.
- Cache reflection metadata after validation, but invalidate Unity object references on scene, manager, save-load, reset, and NG+ lifecycle changes.
- Treat locked, available, queued, terminal-queued, and completed as different states.
- Retain locked definitions for later reevaluation; keep completed finite upgrades out of hot work until a lifecycle reset.
- A failed adapter must reject mutation and emit a rate-limited diagnostic.

## Hot paths

- Keep `Update`, `LateUpdate`, and Harmony hooks constant-time or CPU-sliced.
- Harmony hooks should capture minimal data and enqueue bounded work instead of rebuilding catalogs inline.
- Do not use scene-wide searches, decompilation-style reflection discovery, or allocation-heavy LINQ inside hot paths.
- Native mutation must be revalidated immediately before invocation.

## Tests

- Add portable tests for policy, scheduling, lifecycle transitions, and regression behavior.
- Add or update installed-game metadata contracts for every new reflected or Harmony target.
- Use real-reference builds before treating a native API change as complete.

