from __future__ import annotations

import argparse
import json
from pathlib import Path

from .model import GameDataModel
from .paths import default_data_directory


def load_catalog(data_directory: Path) -> list[dict[str, str]]:
    path = data_directory / "entity-display-names.tsv"
    if not path.exists():
        raise SystemExit(f"Entity display-name catalog is missing: {path}")
    rows: list[dict[str, str]] = []
    for index, line in enumerate(path.read_text(encoding="utf-8").splitlines()):
        if index == 0 or not line:
            continue
        parts = line.split("\t")
        while len(parts) < 4:
            parts.append("")
        rows.append(
            {"id": parts[0], "type": parts[1], "name": parts[2], "display": parts[3]}
        )
    return rows


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
            lines.append(f"      {field}: {json.dumps(record[field], ensure_ascii=False)[:120]}")
    if record.get("prerequisites"):
        lines.append(
            f"      prerequisites: "
            f"{json.dumps(record['prerequisites'], ensure_ascii=False)[:200]}"
        )
    return lines


def main(argv: list[str] | None = None) -> None:
    parser = argparse.ArgumentParser(description="Find game entities by display or asset name.")
    parser.add_argument("query", nargs="?", default="", help="case-insensitive substring")
    parser.add_argument("-t", "--type", dest="managed_type", help="restrict to one managed type")
    parser.add_argument("--uuid", help="reverse lookup by exact UUID")
    parser.add_argument("--costs", action="store_true", help="show authored costs and prerequisites")
    parser.add_argument(
        "--source",
        help="full-model JSON; defaults to the committed data/game-data.json",
    )
    parser.add_argument("--limit", type=int, default=40, help="maximum rows to print")
    arguments = parser.parse_args(argv)
    rows = load_catalog(default_data_directory())

    if arguments.uuid:
        wanted = arguments.uuid.strip().lower()
        matches = [row for row in rows if row["id"] == wanted]
    else:
        if not arguments.query:
            parser.error("provide a search term or --uuid")
        needle = arguments.query.lower()
        matches = [
            row
            for row in rows
            if needle in row["display"].lower() or needle in row["name"].lower()
        ]
    if arguments.managed_type:
        wanted_type = arguments.managed_type.lower()
        matches = [row for row in matches if row["type"].lower() == wanted_type]
    if not matches:
        print("No match.")
        return

    needle = arguments.query.lower()
    matches.sort(key=lambda row: (row["display"].lower() != needle, row["type"], row["display"]))
    model = GameDataModel(arguments.source) if arguments.costs else None
    display_counts: dict[str, int] = {}
    for row in matches:
        if row["display"]:
            key = row["display"].lower()
            display_counts[key] = display_counts.get(key, 0) + 1

    shown = matches[: arguments.limit]
    for row in shown:
        collision = display_counts.get(row["display"].lower(), 0) > 1
        marker = "  << AMBIGUOUS LABEL" if collision else ""
        print(f"{row['display'] or '(no display name)'}{marker}")
        print(f"      {row['type']}  asset={row['name']}")
        print(f"      {row['id']}")
        if model:
            record = model.find_uuid(row["id"])
            if record:
                for line in describe_costs(record):
                    print(line)
        print()
    if len(matches) > len(shown):
        print(f"... {len(matches) - len(shown)} more (raise --limit)")
