# Entity mappings

This directory stores the known mapping between Orb of Creation entity UUIDs, internal asset names, and managed types.

## Files

- `entity-mappings.tsv` — normalized mapping with `id`, `name`, and `type` columns.
- `entity-types.tsv` — mapping count grouped by managed type.
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

The mapping records identity, not serialized relationships. Type memberships, prerequisite links, attribute-group members, list contents, unlock state, and live runtime instances require assembly inspection, serialized asset inspection, or a runtime probe. See [Entity catalog](../docs/entity-catalog.md) and [Entity correlations](../docs/entity-correlations.md).

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
