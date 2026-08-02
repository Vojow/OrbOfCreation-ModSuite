# Repository tools

`tools/` is the repository's Python project as well as the home of shell, PowerShell, and .NET
utilities that have not moved into Python. The Python package is managed only through `uv`.

## Game-data charter

`orbtools.gamedata` reads authored serialized data from an installed copy of Orb of Creation. It
never launches the game, opens a save, or writes into the installation. The committed outputs name
the serialized product version and are reproducible from one extraction command.

```bash
cd tools
uv sync --locked
uv run orb-gamedata extract --game-dir "/path/to/Orb of Creation"
uv run orb-gamedata verify
uv run pytest
```

`--game-dir` accepts a Windows install root, a macOS `.app`, or either platform's Unity `Data`
directory. When it is omitted, `OOC_GAME_DIR` and the standard Steam locations are checked.
Provenance uses the product and Unity versions serialized by the game, while the manifest hashes
the produced files. It does not admit one operating system by an assembly hash.

Commands:

- `extract` writes the full content-object model, UUID-backed progression graph and identity views,
  census, provenance manifest, and `message.txt` from one read-only scan. UUID-less content stays
  in the full model without being invented into the identity catalog. Logs go to
  `artifacts/gamedata/logs/`.
- `verify` checks file hashes, build pins, counts, full-model/graph agreement, and every derived
  TSV. Add `--game-dir` to compare the committed outputs byte-for-byte with a fresh scan.
- `query` reads the committed full model by class, exact name, UUID, reverse reference,
  requirement, or effect.
- `report atlas`, `report discovery-pools`, and `report cost-curves` regenerate model-derived
  reports under `artifacts/gamedata/`.

The `find-entity` and `import-entity-display-names` console commands are also available. Their
historical `tools/*.py` paths remain wrappers so existing documentation and scripts keep working.

The full model is authored state, not live state. Runtime quantities, visibility, registry
membership, modifiers, and save progress belong to the running-game tooling.
