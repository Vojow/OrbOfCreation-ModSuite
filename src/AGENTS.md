# Source-specific agent guidance

Supplements the root `AGENTS.md` for work under `src/`.

- Read the plugin's README and applicable plan under `docs/plans/` before
  changing behavior; check the plugin's lifecycle status first.
- `OrbModding.Common` owns gameplay-neutral contracts only; do not move domain
  policy into it for convenience.
- Native adapters resolve exact overloads and validate return/field types; a
  failed adapter rejects mutation.
- Keep `Update`, `LateUpdate`, and Harmony hooks bounded; capture minimal data
  and enqueue work instead of rebuilding catalogs inline.
- Add installed-game metadata contracts for every new reflected or Harmony
  target.
