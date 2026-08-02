from __future__ import annotations

import argparse
import json
from pathlib import Path

from .extractor import clean_text
from .paths import default_data_directory


HEADER = "id\ttype\tname\tdisplayName"


def records_from_source(source: Path) -> list[tuple[str, str, str, str]]:
    payload = json.loads(source.read_text(encoding="utf-8"))
    objects = payload.get("objects", payload)
    records: dict[str, tuple[str, str, str, str]] = {}
    conflicts: list[str] = []
    for class_name, bucket in sorted(objects.items()):
        if not isinstance(bucket, dict):
            continue
        for internal_name, record in bucket.items():
            if not isinstance(record, dict):
                continue
            entity_id = clean_text(record.get("id")).lower()
            if not entity_id:
                continue
            row = (
                entity_id,
                clean_text(record.get("class") or class_name),
                clean_text(record.get("name") or internal_name.split("#", 1)[0]),
                clean_text(record.get("displayName")),
            )
            if entity_id in records and records[entity_id] != row:
                conflicts.append(f"{entity_id}: {records[entity_id]} vs {row}")
            records[entity_id] = row
    if conflicts:
        raise ValueError("Conflicting UUIDs:\n  " + "\n  ".join(conflicts[:10]))
    return [records[key] for key in sorted(records)]


def main(argv: list[str] | None = None) -> None:
    parser = argparse.ArgumentParser(
        description="Regenerate the display-name TSV from a full game-data model."
    )
    parser.add_argument(
        "--source",
        default=str(default_data_directory() / "game-data.json"),
        help="full-model JSON; defaults to the committed dataset",
    )
    parser.add_argument(
        "--output",
        default=str(default_data_directory() / "entity-display-names.tsv"),
    )
    arguments = parser.parse_args(argv)
    source = Path(arguments.source).expanduser()
    output = Path(arguments.output).expanduser()
    records = records_from_source(source)
    if not records:
        raise SystemExit(f"{source} contains no UUID-backed entities.")
    output.parent.mkdir(parents=True, exist_ok=True)
    with output.open("w", encoding="utf-8", newline="\n") as handle:
        handle.write("\n".join([HEADER, *("\t".join(row).rstrip("\t") for row in records)]) + "\n")
    print(f"Wrote {len(records)} rows to {output}")
