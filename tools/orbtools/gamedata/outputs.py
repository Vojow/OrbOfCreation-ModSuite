from __future__ import annotations

import hashlib
import json
from collections import Counter
from pathlib import Path
from typing import Any


MODEL_FILE = "game-data.json"
GRAPH_FILE = "progression-graph.json"
CENSUS_FILE = "game-data-census.json"
MANIFEST_FILE = "game-data-manifest.json"
GENERATED_FILES = (
    MODEL_FILE,
    GRAPH_FILE,
    CENSUS_FILE,
    "entity-mappings.tsv",
    "entity-types.tsv",
    "entity-display-names.tsv",
    "source/message.txt",
)


def canonical_json(value: object) -> str:
    return json.dumps(value, indent=2, ensure_ascii=False, sort_keys=True) + "\n"


def _write_text(path: Path, content: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8", newline="\n") as handle:
        handle.write(content)


def _file_sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


def render_catalogs(graph: dict[str, Any]) -> dict[str, str]:
    entities = graph["entities"]
    mapping_order = sorted(
        entities,
        key=lambda row: (row["type"], row["name"], row["id"]),
    )
    mapping_lines = ["id\tname\ttype"]
    source_lines: list[str] = []
    for entity in mapping_order:
        mapping_lines.append(f"{entity['id']}\t{entity['name']}\t{entity['type']}")
        source_lines.append(f"{entity['id']} → {entity['name']} → {entity['type']}")

    type_counts = Counter(entity["type"] for entity in entities)
    type_lines = ["type\tcount"]
    type_lines.extend(f"{name}\t{count}" for name, count in sorted(type_counts.items()))

    display_lines = ["id\ttype\tname\tdisplayName"]
    for entity in sorted(entities, key=lambda row: row["id"]):
        row = f"{entity['id']}\t{entity['type']}\t{entity['name']}\t{entity['displayName']}"
        display_lines.append(row.rstrip("\t"))
    return {
        "entity-mappings.tsv": "\n".join(mapping_lines) + "\n",
        "entity-types.tsv": "\n".join(type_lines) + "\n",
        "entity-display-names.tsv": "\n".join(display_lines) + "\n",
        "source/message.txt": "\n".join(source_lines) + "\n",
    }


def build_census(model: dict[str, Any], graph: dict[str, Any]) -> dict[str, Any]:
    entities = graph["entities"]
    internal_names = Counter(entity["name"] for entity in entities)
    repeated = {name: count for name, count in internal_names.items() if count > 1}
    display_names = Counter(entity["displayName"] for entity in entities if entity["displayName"])
    repeated_display = {name: count for name, count in display_names.items() if count > 1}
    return {
        "metadata": {
            "formatVersion": 1,
            **{
                key: model["metadata"][key]
                for key in (
                    "gameVersion",
                    "unityVersion",
                    "assetFiles",
                )
            },
        },
        "counts": {
            "objects": model["metadata"]["objectCount"],
            "classes": model["metadata"]["classCount"],
            "uniqueUuids": len({entity["id"] for entity in entities}),
            "uniqueInternalNames": len(internal_names),
            "duplicatedInternalNames": len(repeated),
            "rowsWithDuplicatedInternalNames": sum(repeated.values()),
            "rowsWithDisplayNames": sum(bool(entity["displayName"]) for entity in entities),
            "duplicatedDisplayNames": len(repeated_display),
            "rowsWithDuplicatedDisplayNames": sum(repeated_display.values()),
            "relationships": graph["metadata"]["relationshipCount"],
            "requirementGates": graph["metadata"]["requirementGateCount"],
            "prerequisiteLinks": graph["metadata"]["unlockLinkCount"],
        },
        "objectsByClass": model["metadata"]["objectCountsByClass"],
    }


def write_generated_outputs(
    output_directory: Path,
    model: dict[str, Any],
    graph: dict[str, Any],
) -> dict[str, Any]:
    output_directory.mkdir(parents=True, exist_ok=True)
    census = build_census(model, graph)
    contents = {
        MODEL_FILE: canonical_json(model),
        GRAPH_FILE: canonical_json(graph),
        CENSUS_FILE: canonical_json(census),
        **render_catalogs(graph),
    }
    for relative, content in contents.items():
        _write_text(output_directory / relative, content)

    manifest = {
        "formatVersion": 1,
        "build": {
            key: model["metadata"][key]
            for key in (
                "gameVersion",
                "unityVersion",
                "assetFiles",
                "objectCount",
                "classCount",
            )
        },
        "files": {
            relative: {
                "sha256": _file_sha256(output_directory / relative),
                "bytes": (output_directory / relative).stat().st_size,
            }
            for relative in GENERATED_FILES
        },
    }
    _write_text(output_directory / MANIFEST_FILE, canonical_json(manifest))
    return manifest
