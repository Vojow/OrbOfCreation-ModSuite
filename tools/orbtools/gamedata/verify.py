from __future__ import annotations

import hashlib
import json
from collections import Counter
from pathlib import Path
from typing import Any

from .outputs import (
    CENSUS_FILE,
    GENERATED_FILES,
    GRAPH_FILE,
    MANIFEST_FILE,
    MODEL_FILE,
    build_census,
    canonical_json,
    render_catalogs,
)


class VerificationError(ValueError):
    pass


def _sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


def _read_json(path: Path) -> dict[str, Any]:
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        raise VerificationError(f"Cannot read {path}: {error}") from error
    if not isinstance(value, dict):
        raise VerificationError(f"Expected a JSON object in {path}.")
    return value


def verify_committed(data_directory: Path) -> dict[str, int]:
    errors: list[str] = []
    manifest = _read_json(data_directory / MANIFEST_FILE)
    file_entries = manifest.get("files")
    if not isinstance(file_entries, dict):
        raise VerificationError(f"{MANIFEST_FILE} has no files map.")
    if set(file_entries) != set(GENERATED_FILES):
        errors.append(
            f"manifest file set differs: expected {sorted(GENERATED_FILES)}, "
            f"actual {sorted(file_entries)}"
        )
    for relative in GENERATED_FILES:
        path = data_directory / relative
        if not path.is_file():
            errors.append(f"missing generated file: {relative}")
            continue
        entry = file_entries.get(relative, {})
        actual_hash = _sha256(path)
        actual_size = path.stat().st_size
        if entry.get("sha256") != actual_hash:
            errors.append(
                f"hash drift for {relative}: manifest={entry.get('sha256')}, actual={actual_hash}"
            )
        if entry.get("bytes") != actual_size:
            errors.append(
                f"size drift for {relative}: manifest={entry.get('bytes')}, actual={actual_size}"
            )

    model = _read_json(data_directory / MODEL_FILE)
    graph = _read_json(data_directory / GRAPH_FILE)
    census = _read_json(data_directory / CENSUS_FILE)
    model_metadata = model.get("metadata", {})
    graph_metadata = graph.get("metadata", {})
    objects = model.get("objects")
    entities = graph.get("entities")
    if not isinstance(objects, dict):
        errors.append(f"{MODEL_FILE} has no objects map")
        objects = {}
    if not isinstance(entities, list):
        errors.append(f"{GRAPH_FILE} has no entities list")
        entities = []

    actual_object_count = sum(len(bucket) for bucket in objects.values() if isinstance(bucket, dict))
    actual_class_count = len(objects)
    if model_metadata.get("objectCount") != actual_object_count:
        errors.append(
            f"model object count drift: metadata={model_metadata.get('objectCount')}, "
            f"actual={actual_object_count}"
        )
    if model_metadata.get("classCount") != actual_class_count:
        errors.append(
            f"model class count drift: metadata={model_metadata.get('classCount')}, "
            f"actual={actual_class_count}"
        )
    actual_model_types = {
        class_name: len(bucket)
        for class_name, bucket in sorted(objects.items())
        if isinstance(bucket, dict)
    }
    if model_metadata.get("objectCountsByClass") != actual_model_types:
        errors.append("model per-class counts differ from the objects map")
    graph_ids = [str(entity.get("id", "")).lower() for entity in entities]
    if len(graph_ids) != len(set(graph_ids)):
        errors.append("progression graph contains duplicate entity UUIDs")
    if graph_metadata.get("entityCount") != len(entities):
        errors.append(
            f"graph entity count drift: metadata={graph_metadata.get('entityCount')}, "
            f"actual={len(entities)}"
        )
    if graph_metadata.get("entityTypeCount") != len({entity.get("type") for entity in entities}):
        errors.append("graph entity-type count differs from graph entities")
    actual_graph_types = dict(
        sorted(Counter(entity.get("type") for entity in entities).items())
    )
    if graph_metadata.get("entityCountsByType") != actual_graph_types:
        errors.append("graph per-type counts differ from graph entities")
    graph_collections = (
        ("relationshipCount", "relationships"),
        ("requirementGateCount", "requirements"),
        ("unlockLinkCount", "unlockLinks"),
    )
    for metadata_key, collection_key in graph_collections:
        collection = graph.get(collection_key)
        if not isinstance(collection, list):
            errors.append(f"{GRAPH_FILE} has no {collection_key} list")
            continue
        if graph_metadata.get(metadata_key) != len(collection):
            errors.append(
                f"graph {collection_key} count drift: "
                f"metadata={graph_metadata.get(metadata_key)}, actual={len(collection)}"
            )

    model_ids = {
        str(record.get("id", "")).lower()
        for bucket in objects.values()
        if isinstance(bucket, dict)
        for record in bucket.values()
        if isinstance(record, dict) and record.get("id")
    }
    if model_ids != set(graph_ids):
        only_model = sorted(model_ids - set(graph_ids))[:10]
        only_graph = sorted(set(graph_ids) - model_ids)[:10]
        errors.append(
            f"model/graph UUID mismatch: only-model={only_model}, only-graph={only_graph}"
        )

    provenance_keys = (
        "gameVersion",
        "unityVersion",
        "assetFiles",
    )
    for key in provenance_keys:
        if model_metadata.get(key) != graph_metadata.get(key):
            errors.append(f"model/graph provenance mismatch for {key}")
        if model_metadata.get(key) != census.get("metadata", {}).get(key):
            errors.append(f"model/census provenance mismatch for {key}")

    expected_census = build_census(model, graph)
    if census != expected_census:
        errors.append(f"{CENSUS_FILE} is inconsistent with the full model and graph")
    expected_catalogs = render_catalogs(graph)
    for relative, expected in expected_catalogs.items():
        path = data_directory / relative
        if path.is_file() and path.read_text(encoding="utf-8") != expected:
            errors.append(f"TSV/source view is inconsistent with the dataset: {relative}")

    expected_build = {
        key: model_metadata.get(key)
        for key in (
            *provenance_keys,
            "objectCount",
            "classCount",
        )
    }
    if manifest.get("build") != expected_build:
        errors.append("manifest build pin differs from the full model metadata")
    if errors:
        raise VerificationError("Game-data verification failed:\n  - " + "\n  - ".join(errors))
    return {
        "objects": actual_object_count,
        "classes": actual_class_count,
        "relationships": int(graph_metadata.get("relationshipCount", 0)),
        "requirementGates": int(graph_metadata.get("requirementGateCount", 0)),
    }


def compare_generated_directories(committed: Path, fresh: Path) -> None:
    drift: list[str] = []
    for relative in (*GENERATED_FILES, MANIFEST_FILE):
        committed_path = committed / relative
        fresh_path = fresh / relative
        if not committed_path.is_file() or not fresh_path.is_file():
            drift.append(f"missing comparison input: {relative}")
            continue
        if committed_path.read_bytes() != fresh_path.read_bytes():
            drift.append(relative)
    if drift:
        raise VerificationError(
            "Committed dataset differs from a fresh installed-game scan:\n  - "
            + "\n  - ".join(drift)
        )
