# Entity mappings

This directory stores the known mapping between Orb of Creation entity UUIDs, internal asset names, and managed types.

## Files

The directory also contains `native-contracts.json`, the audited machine-readable inventory of game assembly hashes and native reflection/Harmony contracts. It records signatures, visibility, feature ownership, and selecting source files. Maintain it through the [native contract workflow](../docs/development/native-contract-manifest.md); it does not replace runtime fail-closed checks.

- `entity-mappings.tsv` — normalized mapping with `id`, `name`, and `type` columns.
- `entity-types.tsv` — mapping count grouped by managed type.
- `known-entities.tsv` — explicit supported-domain subset used to generate production identity declarations.
- `autobuy-performance-baseline.json` — reviewed deterministic queue-performance history used by CI; update it only through the policy in [Headless E2E simulation](../docs/development/headless-e2e.md#historical-reports).
- `suite-performance-profile-v1.json` — the SHA-256-bound observational policy
  for the twelve work identities still owned by the legacy suite coordinator.
  It is an input contract for sanitized start/end evidence, not captured test
  output or runtime configuration. ServiceCycle services are measured through
  their independent profile product and do not appear here. See
  [suite coordinator performance evidence](../docs/development/testing.md#suite-coordinator-performance-evidence).
- `source/message.txt` — preserved UTF-8 source used for the current import.

The TSV format is used because it is simple to diff, search, and consume from scripts without quoting the entity names unnecessarily.

## Dataset guarantees and limits

Current validated totals:

- 2,792 entity rows.
- 2,792 unique UUIDs.
- 141 managed runtime types.
- 2,751 unique internal names.
- 39 duplicated name labels covering 80 rows.

The importer guarantees UUID uniqueness, not name uniqueness. Consumers must resolve by `id`, validate `type`, and use `name` only for display or diagnostics.

The production subset is generated with `tools/generate-known-entities.ps1`. Every build verifies that the checked-in generated source is reproducible and that each selected UUID, name, and managed type still matches the canonical mapping. Add entities deliberately; the full catalog is not generated into the runtime API.

The mapping records identity, not serialized relationships. Type memberships, prerequisite links, attribute-group members, list contents, unlock state, and live runtime instances require assembly inspection, serialized asset inspection, or a runtime probe. See [Entity catalog](../docs/reverse-engineering/entity-catalog.md) and [Entity correlations](../docs/reverse-engineering/entity-correlations.md).

## Refreshing the mappings

From the repository root, run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\import-entity-mappings.ps1 -SourcePath "C:\path\to\message.txt"
```

The importer validates every line, rejects duplicate UUIDs and writes deterministic UTF-8 files without a byte-order mark.

## Known example

```text
67acd892-8a8a-455a-aa71-3fb06e75bf38    AlchemicScroll    ResourceSO
```
