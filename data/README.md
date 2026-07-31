# Entity mappings

This directory stores the known mapping between Orb of Creation entity UUIDs, internal asset names, and managed types.

## Files

The directory also contains `native-contracts.json`, the audited machine-readable inventory of game assembly hashes and native reflection/Harmony contracts. It records signatures, visibility, feature ownership, and the place each contract is touched. Maintain it through the [native contract workflow](../docs/testing/native-contracts.md); it does not replace runtime fail-closed checks.

- `entity-mappings.tsv` — normalized mapping with `id`, `name`, and `type` columns.
- `entity-display-names.tsv` — `id`, `type`, `name`, and `displayName`: the label the game
  actually shows a player. It covers all 2,818 entities; 2,274 carry a non-empty label. See
  [Display names](#display-names).
- `entity-types.tsv` — mapping count grouped by managed type.
- `known-entities.tsv` — explicit supported-domain subset used to generate production identity declarations.
- `source/message.txt` — preserved UTF-8 source used for the current import.

The raw `progression-graph.json` extraction and generated exhaustive progression atlas are
intentionally not checked in. They reproduce the complete authored dependency dataset and must be
generated locally from a contributor-owned game installation. The repository keeps the extractor,
generator, and the reviewed [progression mind map](../docs/reverse-engineering/progression-map.md).

The TSV format is used because it is simple to diff, search, and consume from scripts without quoting the entity names unnecessarily.

## Dataset guarantees and limits

Current validated totals:

- 2,818 entity rows.
- 2,818 unique UUIDs.
- 141 managed runtime types.
- 2,777 unique internal names.
- 39 duplicated name labels covering 80 rows.

The importer guarantees UUID uniqueness, not name uniqueness. Consumers must resolve by `id`, validate `type`, and use `name` only for display or diagnostics.

The production subset is generated with `tools/generate-known-entities.ps1` (`tools/generate-known-entities.sh` on POSIX hosts). Every build verifies that the checked-in generated source is reproducible and that each selected UUID, name, and managed type still matches the canonical mapping. Add entities deliberately; the full catalog is not generated into the runtime API.

The TSV mapping records identity, not serialized relationships. Exact authored type memberships,
prerequisite links, list contents, and other references can be inspected in a locally generated
`progression-graph.json`. Live unlock state, runtime registry membership, and active instances still
require a runtime probe. See [Entity catalog](../docs/reverse-engineering/entity-catalog.md),
[Entity correlations](../docs/reverse-engineering/entity-correlations.md), and the
[progression map](../docs/reverse-engineering/progression-map.md).

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
[Refreshing the mappings and local progression graph](#refreshing-the-mappings-and-local-progression-graph)).

Note that this file is the one place the identity-only limit above is partially lifted: the
extraction it comes from does carry serialized relationships, which is what `--costs`
surfaces. The TSV itself still stores identity only.

## Refreshing the mappings and local progression graph

The repository can perform a read-only scan directly from an installed Windows game. Install the
optional `UnityPy` and `TypeTreeGeneratorAPI` packages into a disposable Python environment, name
the game directory explicitly, and request catalog synchronization:

```powershell
python -m pip install UnityPy TypeTreeGeneratorAPI
python tools/extract-progression-graph.py `
  --game-dir "C:\path\to\Orb Of Creation" `
  --sync-entity-catalog
python tools/generate-progression-atlas.py
python tools/generate-progression-atlas.py --verify
```

The extractor reads the fixed managed/assets layout, generates MonoBehaviour type trees from the
installed assemblies, decodes Unity managed-reference prerequisite objects, and writes deterministic
outputs. `data/progression-graph.json` and `docs/reverse-engineering/progression-atlas.md` are ignored
local artifacts; do not commit them. The extractor does not launch the game, edit a save, or establish
live runtime evidence. Run the normal game-build audit before accepting output from a new assembly pair.

The older import paths remain useful for independently produced extractions. Neither guesses a
source location.

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
