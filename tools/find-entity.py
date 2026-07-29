#!/usr/bin/env python3
"""Look up game entities by the name a player actually sees.

Everything the suite stores keys on UUID. But nobody authoring a milestone, an embargo, or
a priority reads UUIDs off a screen; they read "Improved Alchemy". This bridges the two
directions.

Display name alone is not a key — 152 labels are shared by more than one entity. Display
name *plus managed type* very nearly is: within ResourceSO, StructureSO, UpgradeSO, and
ResearchSO there is not one collision, so anything a strategy names resolves uniquely once
the category is known. The 18 that do collide within a type are all AttributeSO, where the
same stat label ('Superior', 'Quality') is scoped to different subjects. Collisions are
flagged in the output rather than silently resolved.

    tools/find-entity.py alchemy                 # any display or asset name containing it
    tools/find-entity.py "improved" -t UpgradeSO # restrict to one managed type
    tools/find-entity.py --uuid b1cf414e-...     # reverse: what is this UUID?
    tools/find-entity.py mana --costs            # include resource costs and prerequisites

`--costs` needs the extraction project's game_data.json, named with `--source` or
ORB_GAME_DATA_JSON (see tools/import-entity-display-names.py); everything else runs off the
committed TSV.
"""

from __future__ import annotations

import argparse
import json
import os
from pathlib import Path

REPOSITORY_ROOT = Path(__file__).resolve().parent.parent
CATALOG_PATH = REPOSITORY_ROOT / "data" / "entity-display-names.tsv"

SOURCE_REQUIRED = (
    "--costs needs the extracted game data: pass --source path/to/game_data.json "
    "or set ORB_GAME_DATA_JSON."
)


def load_catalog() -> list[dict[str, str]]:
    if not CATALOG_PATH.exists():
        raise SystemExit(
            f"{CATALOG_PATH.relative_to(REPOSITORY_ROOT)} is missing. "
            "Run tools/import-entity-display-names.py first."
        )
    rows: list[dict[str, str]] = []
    with CATALOG_PATH.open(encoding="utf-8") as handle:
        for index, line in enumerate(handle):
            if index == 0 or not line.strip():
                continue
            parts = line.rstrip("\n").split("\t")
            while len(parts) < 4:
                parts.append("")
            rows.append({"id": parts[0], "type": parts[1], "name": parts[2], "display": parts[3]})
    return rows


def load_game_data(explicit_source: str | None) -> dict:
    source = explicit_source or os.environ.get("ORB_GAME_DATA_JSON")
    if not source:
        raise SystemExit(SOURCE_REQUIRED)
    path = Path(source).expanduser()
    if not path.exists():
        raise SystemExit(f"Extracted game data not found at {path}.")
    with path.open(encoding="utf-8") as handle:
        return json.load(handle)


def describe_costs(record: dict) -> list[str]:
    lines: list[str] = []
    cost = record.get("resourceCost") or {}
    entries = cost.get("costs") if isinstance(cost, dict) else None
    if entries:
        rendered = ", ".join(
            f"{item.get('value')} {item.get('resource')}"
            for item in entries
            if isinstance(item, dict)
        )
        lines.append(f"      cost: {rendered}")
    for field in ("baseCost", "costPerQuantity", "maxLevel", "baseLevel", "developmentTime"):
        if record.get(field) not in (None, "", [], {}):
            lines.append(f"      {field}: {json.dumps(record[field])[:120]}")
    prerequisites = record.get("prerequisites")
    if prerequisites:
        lines.append(f"      prerequisites: {json.dumps(prerequisites)[:200]}")
    return lines


def main() -> None:
    parser = argparse.ArgumentParser(description="Find game entities by display or asset name.")
    parser.add_argument("query", nargs="?", default="", help="substring to match (case-insensitive)")
    parser.add_argument("-t", "--type", dest="managed_type", help="restrict to one managed type")
    parser.add_argument("--uuid", help="reverse lookup by exact UUID")
    parser.add_argument("--costs", action="store_true", help="show costs and prerequisites")
    parser.add_argument(
        "--source",
        help="game_data.json for --costs; ORB_GAME_DATA_JSON is read when omitted")
    parser.add_argument("--limit", type=int, default=40, help="maximum rows to print")
    arguments = parser.parse_args()

    rows = load_catalog()

    if arguments.uuid:
        wanted = arguments.uuid.strip().lower()
        matches = [row for row in rows if row["id"] == wanted]
    else:
        if not arguments.query:
            parser.error("provide a search term or --uuid")
        needle = arguments.query.lower()
        matches = [
            row for row in rows
            if needle in row["display"].lower() or needle in row["name"].lower()
        ]

    if arguments.managed_type:
        wanted_type = arguments.managed_type.lower()
        matches = [row for row in matches if row["type"].lower() == wanted_type]

    if not matches:
        print("No match.")
        return

    # Sort exact display-name matches first: when a label collides, the one a player would
    # have typed should not be buried under longer names that merely contain it.
    needle = (arguments.query or "").lower()
    matches.sort(key=lambda row: (row["display"].lower() != needle, row["type"], row["display"]))

    game_data = load_game_data(arguments.source) if arguments.costs else {}
    shown = matches[: arguments.limit]

    display_counts: dict[str, int] = {}
    for row in matches:
        if row["display"]:
            display_counts[row["display"].lower()] = display_counts.get(row["display"].lower(), 0) + 1

    for row in shown:
        collision = display_counts.get(row["display"].lower(), 0) > 1
        marker = "  << AMBIGUOUS LABEL" if collision else ""
        print(f"{row['display'] or '(no display name)'}{marker}")
        print(f"      {row['type']}  asset={row['name']}")
        print(f"      {row['id']}")
        if arguments.costs:
            record = (game_data.get(row["type"]) or {}).get(row["name"])
            if isinstance(record, dict):
                for line in describe_costs(record):
                    print(line)
        print()

    if len(matches) > len(shown):
        print(f"... {len(matches) - len(shown)} more (raise --limit)")


if __name__ == "__main__":
    main()
