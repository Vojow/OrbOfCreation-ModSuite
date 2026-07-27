#!/usr/bin/env python3
"""Import in-game display names for every extracted entity.

`data/entity-mappings.tsv` records identity only: UUID, internal asset name, and managed
type. Nothing in it is what a player sees on screen — `PNAFruitTreeCollect` is not a label
anyone can read off a screenshot. Authoring anything from in-game observation (milestone
tables, embargo lists, curated priorities) needs the other direction: display name -> UUID.

Display names live on `TooltipableObject.displayName`, a plain serialized string with no
localization indirection, so they are recoverable from the game's Unity assets. The
sibling `orb-of-creation` project already solves that extraction — it generates type trees
from `Assembly-CSharp.dll`, wires them into UnityPy, and resolves every content object.
This script consumes that output rather than reimplementing any of it.

The generated TSV is committed so this repository's tooling works without the extraction
project present; the extraction project's own derived data is gitignored and regenerated
from a local install on demand.

The extraction project lives outside this repository and nothing here can know where a
given machine keeps it, so its output is named rather than guessed at:

    tools/import-entity-display-names.py --source path/to/game_data.json
    ORB_GAME_DATA_JSON=path/to/game_data.json tools/import-entity-display-names.py
"""

from __future__ import annotations

import argparse
import json
import os
from pathlib import Path

REPOSITORY_ROOT = Path(__file__).resolve().parent.parent
OUTPUT_PATH = REPOSITORY_ROOT / "data" / "entity-display-names.tsv"
IDENTITY_PATH = REPOSITORY_ROOT / "data" / "entity-mappings.tsv"

HEADER = "id\ttype\tname\tdisplayName"

SOURCE_REQUIRED = (
    "No extracted game data: pass --source path/to/game_data.json or set ORB_GAME_DATA_JSON."
)


def resolve_source() -> Path:
    parser = argparse.ArgumentParser(
        description="Import in-game display names for every extracted entity.")
    parser.add_argument(
        "--source",
        help="the extraction project's game_data.json; ORB_GAME_DATA_JSON is read when omitted")
    arguments = parser.parse_args()
    source = arguments.source or os.environ.get("ORB_GAME_DATA_JSON")
    if not source:
        raise SystemExit(SOURCE_REQUIRED)
    return Path(source).expanduser()


def clean(value: object) -> str:
    """Collapse a field to one safe TSV cell.

    Display names are authored content and may contain anything; a stray tab or newline
    would silently shift every following column, so they are normalized rather than
    trusted. Empty is legitimate — plenty of internal objects carry no player-facing name.
    """
    if value is None:
        return ""
    text = str(value)
    for bad in ("\t", "\r\n", "\r", "\n"):
        text = text.replace(bad, " ")
    return " ".join(text.split())


def read_records(source: Path) -> list[tuple[str, str, str, str]]:
    with source.open(encoding="utf-8") as handle:
        data = json.load(handle)

    records: dict[str, tuple[str, str, str, str]] = {}
    collisions: list[str] = []
    for class_name, objects in sorted(data.items()):
        if not isinstance(objects, dict):
            continue
        for internal_name, record in objects.items():
            if not isinstance(record, dict):
                continue
            identity = clean(record.get("id")).lower()
            if not identity:
                continue
            row = (identity, clean(class_name), clean(internal_name), clean(record.get("displayName")))
            existing = records.get(identity)
            if existing is not None and existing != row:
                collisions.append(f"{identity}: {existing} vs {row}")
                continue
            records[identity] = row

    if collisions:
        raise SystemExit(
            "Refusing to write a mapping with conflicting UUIDs:\n  " + "\n  ".join(collisions[:10])
        )
    return [records[key] for key in sorted(records)]


def report_coverage(records: list[tuple[str, str, str, str]]) -> None:
    """Cross-check against the identity mapping already in this repository.

    The two files come from independent extractions, so agreement is real evidence that
    the display names are attached to the right entities rather than a plausible-looking
    table. Disagreement is reported, never silently reconciled.
    """
    if not IDENTITY_PATH.exists():
        print(f"note: {IDENTITY_PATH.name} is absent; skipping the cross-check.")
        return

    identity: dict[str, tuple[str, str]] = {}
    with IDENTITY_PATH.open(encoding="utf-8") as handle:
        for index, line in enumerate(handle):
            if index == 0 or not line.strip():
                continue
            parts = line.rstrip("\n").split("\t")
            if len(parts) >= 3:
                identity[parts[0].lower()] = (parts[1], parts[2])

    imported = {row[0]: row for row in records}
    shared = identity.keys() & imported.keys()
    type_conflicts = [key for key in shared if identity[key][1] != imported[key][1]]
    name_conflicts = [key for key in shared if identity[key][0] != imported[key][2]]

    print(f"  identity rows      : {len(identity)}")
    print(f"  imported rows      : {len(imported)}")
    print(f"  shared UUIDs       : {len(shared)}")
    print(f"  only in identity   : {len(identity.keys() - imported.keys())}")
    print(f"  only in imported   : {len(imported.keys() - identity.keys())}")
    print(f"  managed-type clashes: {len(type_conflicts)}")
    print(f"  asset-name clashes  : {len(name_conflicts)}")
    for key in type_conflicts[:5]:
        print(f"    type  {key}: identity={identity[key][1]} imported={imported[key][1]}")
    for key in name_conflicts[:5]:
        print(f"    name  {key}: identity={identity[key][0]} imported={imported[key][2]}")

    named = sum(1 for row in records if row[3])
    print(f"  rows with a display name: {named} of {len(records)}")


def main() -> None:
    source = resolve_source()
    if not source.exists():
        raise SystemExit(
            f"Extracted game data not found at {source}.\n"
            "Generate it in the extraction project (uv run python scripts/extract_all.py), "
            "then point --source or ORB_GAME_DATA_JSON at its game_data.json."
        )

    records = read_records(source)
    if not records:
        raise SystemExit(f"{source} contained no identifiable entities.")

    OUTPUT_PATH.parent.mkdir(parents=True, exist_ok=True)
    with OUTPUT_PATH.open("w", encoding="utf-8", newline="\n") as handle:
        handle.write(HEADER + "\n")
        for row in records:
            handle.write("\t".join(row) + "\n")

    print(f"Wrote {len(records)} rows to {OUTPUT_PATH.relative_to(REPOSITORY_ROOT)}")
    report_coverage(records)


if __name__ == "__main__":
    main()
