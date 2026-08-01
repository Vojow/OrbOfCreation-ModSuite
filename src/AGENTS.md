# Source guidance

Supplements the root `AGENTS.md` for work under `src/`.

- Read the affected feature README and its current testing guide before changing
  behavior.
- `Common` owns gameplay-neutral contracts and infrastructure only; feature
  policy stays with the feature.
- Add no alternate scheduler, configuration authority, native mutation path, or
  feature-local copy of shared lifecycle, binding, verification, freshness, or
  diagnostics infrastructure.
- Resolve complete native binding sets at lifecycle scope. Exact overload,
  owner, parameter, field, and return types must be known before mutation.
- Keep `Update`, `LateUpdate`, and Harmony hooks bounded. Capture native-free
  facts or enqueue work; policy runs against immutable publications.
- Add installed-game metadata contracts for every new reflected member, Harmony
  target, native type, or UI asset contract.
- A refusal carries an exact reason and stable result code; an exception, no-op,
  partial result, or unverified postcondition never becomes success.
