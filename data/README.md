# Entity mappings

This directory stores the known mapping between Orb of Creation entity UUIDs, internal asset names, and managed types.

## Files

The directory also contains `native-contracts.json`, the audited machine-readable inventory of game assembly hashes and native reflection/Harmony contracts. It records signatures, visibility, feature ownership, and the place each contract is touched. Maintain it through the [native contract workflow](../docs/testing/native-contracts.md); it does not replace runtime fail-closed checks.

- `entity-mappings.tsv` — normalized mapping with `id`, `name`, and `type` columns.
- `entity-display-names.tsv` — `id`, `type`, `name`, and `displayName`: the label the game
  actually shows a player, for the 2,246 entities that carry one. See
  [Display names](#display-names).
- `entity-types.tsv` — mapping count grouped by managed type.
- `known-entities.tsv` — explicit supported-domain subset used to generate production identity declarations.
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

The production subset is generated with `tools/generate-known-entities.ps1` (`tools/generate-known-entities.sh` on POSIX hosts). Every build verifies that the checked-in generated source is reproducible and that each selected UUID, name, and managed type still matches the canonical mapping. Add entities deliberately; the full catalog is not generated into the runtime API.

Runtime diagnostics lazily load the two embedded mapping TSVs through
`EntityUuidTranslator`. The diagnostic union currently contains 2,794 UUIDs: canonical asset/type
identity is authoritative where present, and display-only rows may add a visible label. Formatting
always retains the UUID and, when known, the managed type and player-facing or canonical name. This
facade is diagnostic only and never participates in gameplay identity decisions.

The mapping records identity, not serialized relationships. Type memberships, prerequisite links, attribute-group members, list contents, unlock state, and live runtime instances require assembly inspection, serialized asset inspection, or a runtime probe. See [Entity catalog](../docs/reverse-engineering/entity-catalog.md) and [Entity correlations](../docs/reverse-engineering/entity-correlations.md).

## Display names

Internal asset names are not what a player sees, and they are not a reliable guide to it:
`WizardryMagebloom` is shown as "Witchcraft", and `PNAFruitTreeCollect` is shown as
nothing a screenshot would ever contain. Anything authored from in-game observation —
milestone tables, embargo lists, curated priorities — has to start from the visible label,
so `entity-display-names.tsv` records `TooltipableObject.displayName` per UUID.

**Display name alone is not a key.** 152 labels are shared by more than one entity.
**Display name plus managed type very nearly is:** within `ResourceSO`, `StructureSO`,
`UpgradeSO`, and `ResearchSO` there is not a single collision, so anything a strategy names
resolves uniquely once its category is known. The 18 labels that do collide within one type
split 12 `ViewSO`, 5 `AttributeSO`, and 1 `PlotNodeActionSO`: screen names ("Loadout",
"Upgrade", "Alchemy"), stat labels ("Superior", "Quality", "Size") scoped to different
subjects, and the plot action "Enrich". Each needs its owner to disambiguate.

Resolve labels with `tools/find-entity.py`, which searches both name columns and flags colliding
labels instead of silently picking one. Plain lookups need only the checked-in TSVs; `--costs`
additionally joins against a `game_data.json` extraction, which must be named explicitly (see
[Refreshing the mappings](#refreshing-the-mappings)).

Note that this file is the one place the identity-only limit above is partially lifted: the
extraction it comes from does carry serialized relationships, which is what `--costs`
surfaces. The TSV itself still stores identity only.

## Refreshing the mappings

**Refreshing requires a game-data extraction that this repository does not contain and cannot
produce.** Both refresh paths read a file extracted from an installed copy of the game; neither
tool has a default location, and neither will guess one. The checked-in TSVs are the output of
those extractions, and they are what a contributor without the extraction works from.

The UUID/name/type mapping is imported from a preserved message dump:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\import-entity-mappings.ps1 -SourcePath "C:\path\to\message.txt"
```

The importer validates every line, rejects duplicate UUIDs, and writes deterministic UTF-8 files
without a byte-order mark.

Display names come from a separate source — the game's Unity assets rather than the message
import. Point the importer at a `game_data.json` extraction with `--source`, or set
`ORB_GAME_DATA_JSON`:

```bash
tools/import-entity-display-names.py --source path/to/game_data.json
ORB_GAME_DATA_JSON=path/to/game_data.json tools/import-entity-display-names.py
```

Without one of the two, the importer exits and tells you so rather than reading some
conventional path. `tools/find-entity.py --costs` takes the same `--source` flag and the same
environment variable, for the same reason.

The two extractions are independent, so the importer cross-checks them and reports UUIDs present
in only one, plus any managed-type or asset-name disagreement, rather than reconciling silently.
Rows appearing in only one file mean the two were taken from different game versions; re-run the
extraction before trusting the delta.

## Known example

```text
67acd892-8a8a-455a-aa71-3fb06e75bf38    AlchemicScroll    ResourceSO
```
