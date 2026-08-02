import json
from pathlib import Path

import pytest

from orbtools.gamedata.model import GameDataModel
from orbtools.gamedata.outputs import write_generated_outputs
from orbtools.gamedata.reports import negative_cost_modifier_rows
from orbtools.gamedata.verify import VerificationError, compare_generated_directories, verify_committed


def fixture_payloads() -> tuple[dict, dict]:
    provenance = {
        "gameVersion": "1.0.5-2",
        "unityVersion": "6000.0.70f1",
        "assetFiles": ["resources.assets"],
    }
    model = {
        "metadata": {
            "formatVersion": 1,
            **provenance,
            "objectCount": 2,
            "classCount": 2,
            "objectCountsByClass": {"ResourceSO": 1, "UpgradeSO": 1},
        },
        "objects": {
            "ResourceSO": {
                "Ink": {
                    "id": "00000000-0000-0000-0000-000000000001",
                    "name": "Ink",
                    "class": "ResourceSO",
                    "displayName": "Ink",
                }
            },
            "UpgradeSO": {
                "Scribing": {
                    "id": "00000000-0000-0000-0000-000000000002",
                    "name": "Scribing",
                    "class": "UpgradeSO",
                    "displayName": "Scribing",
                    "prerequisites": {"@type": "ResourceRequirement", "item": "Ink"},
                    "value": {"purchaseCost": {"adjust": -0.25, "type": 2}},
                }
            },
        },
    }
    graph = {
        "metadata": {
            "formatVersion": 1,
            **provenance,
            "entityCount": 2,
            "entityTypeCount": 2,
            "relationshipCount": 1,
            "requirementGateCount": 0,
            "unlockLinkCount": 0,
            "entityCountsByType": {"ResourceSO": 1, "UpgradeSO": 1},
        },
        "entities": [
            {
                "id": "00000000-0000-0000-0000-000000000001",
                "type": "ResourceSO",
                "name": "Ink",
                "displayName": "Ink",
            },
            {
                "id": "00000000-0000-0000-0000-000000000002",
                "type": "UpgradeSO",
                "name": "Scribing",
                "displayName": "Scribing",
            },
        ],
        "relationships": [
            {
                "source": "00000000-0000-0000-0000-000000000002",
                "target": "00000000-0000-0000-0000-000000000001",
                "path": "prerequisites.item",
                "kind": "progression",
            }
        ],
        "requirements": [],
        "unlockLinks": [],
    }
    return model, graph


def test_outputs_verify_and_query(tmp_path: Path) -> None:
    model, graph = fixture_payloads()
    write_generated_outputs(tmp_path, model, graph)
    assert verify_committed(tmp_path)["objects"] == 2
    loaded = GameDataModel(tmp_path / "game-data.json")
    scribing = loaded.find_uuid("00000000-0000-0000-0000-000000000002")
    assert scribing is not None
    assert loaded.requirements(scribing)[0]["item"] == "Ink"
    assert negative_cost_modifier_rows(loaded)[0]["path"] == "value.purchaseCost"
    chain = loaded.requirement_chain("00000000-0000-0000-0000-000000000002")
    assert chain["entity"]["name"] == "Scribing"


def test_verify_rejects_tsv_drift(tmp_path: Path) -> None:
    model, graph = fixture_payloads()
    write_generated_outputs(tmp_path, model, graph)
    (tmp_path / "entity-types.tsv").write_text("type\tcount\nResourceSO\t2\n")
    with pytest.raises(VerificationError, match="entity-types.tsv"):
        verify_committed(tmp_path)


def test_verify_rejects_count_drift_even_with_rehashed_manifest(tmp_path: Path) -> None:
    model, graph = fixture_payloads()
    write_generated_outputs(tmp_path, model, graph)
    path = tmp_path / "game-data.json"
    payload = json.loads(path.read_text())
    payload["metadata"]["objectCount"] = 3
    path.write_text(json.dumps(payload, indent=2, sort_keys=True) + "\n")
    with pytest.raises(VerificationError, match="object count drift"):
        verify_committed(tmp_path)


def test_verify_rejects_graph_collection_count_drift(tmp_path: Path) -> None:
    model, graph = fixture_payloads()
    graph["metadata"]["relationshipCount"] = 2
    write_generated_outputs(tmp_path, model, graph)
    with pytest.raises(VerificationError, match="relationships count drift"):
        verify_committed(tmp_path)


def test_fresh_comparison_is_byte_exact(tmp_path: Path) -> None:
    model, graph = fixture_payloads()
    committed = tmp_path / "committed"
    fresh = tmp_path / "fresh"
    write_generated_outputs(committed, model, graph)
    write_generated_outputs(fresh, model, graph)
    compare_generated_directories(committed, fresh)
    (fresh / "entity-mappings.tsv").write_text("drift\n")
    with pytest.raises(VerificationError, match="entity-mappings.tsv"):
        compare_generated_directories(committed, fresh)
