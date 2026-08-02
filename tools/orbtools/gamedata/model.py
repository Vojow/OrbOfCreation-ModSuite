from __future__ import annotations

import json
from collections import defaultdict
from pathlib import Path
from typing import Any, Iterable

from .outputs import GRAPH_FILE, MODEL_FILE
from .paths import default_data_directory


class GameDataModel:
    """Query API over the committed authored game model.

    This model never reads a save or a running process. It describes only the
    build-pinned serialized asset state.
    """

    def __init__(self, source: str | Path | None = None):
        path = Path(source) if source else default_data_directory() / MODEL_FILE
        payload = json.loads(path.expanduser().read_text(encoding="utf-8"))
        if "objects" not in payload or "metadata" not in payload:
            payload = {"metadata": {}, "objects": payload}
        self.source = path
        self.metadata: dict[str, Any] = payload["metadata"]
        self.classes: dict[str, dict[str, dict[str, Any]]] = payload["objects"]
        self.objects: list[dict[str, Any]] = [
            record
            for bucket in self.classes.values()
            for record in bucket.values()
        ]
        self.by_uuid = {
            str(record.get("id", "")).lower(): record
            for record in self.objects
            if record.get("id")
        }
        self.by_name: dict[str, list[dict[str, Any]]] = defaultdict(list)
        for record in self.objects:
            self.by_name[str(record.get("name", ""))].append(record)
        graph_path = path.expanduser().parent / GRAPH_FILE
        self.graph: dict[str, Any] = (
            json.loads(graph_path.read_text(encoding="utf-8")) if graph_path.is_file() else {}
        )
        self.graph_entities = {
            entity["id"]: entity for entity in self.graph.get("entities", [])
        }
        self.graph_requirements: dict[str, list[dict[str, Any]]] = defaultdict(list)
        for gate in self.graph.get("requirements", []):
            self.graph_requirements[gate["owner"]].append(gate)
        self.graph_links = {
            link["link"]: link for link in self.graph.get("unlockLinks", [])
        }

    def by_class(self, class_name: str) -> dict[str, dict[str, Any]]:
        return self.classes.get(class_name, {})

    def find_uuid(self, entity_id: str) -> dict[str, Any] | None:
        return self.by_uuid.get(entity_id.lower())

    def find_name(self, name: str, class_name: str | None = None) -> list[dict[str, Any]]:
        matches = list(self.by_name.get(name, []))
        if class_name:
            matches = [record for record in matches if record.get("class") == class_name]
        return matches

    @staticmethod
    def _walk(value: object) -> Iterable[dict[str, Any]]:
        if isinstance(value, dict):
            yield value
            for child in value.values():
                yield from GameDataModel._walk(child)
        elif isinstance(value, list):
            for child in value:
                yield from GameDataModel._walk(child)

    def requirements(self, record: dict[str, Any]) -> list[dict[str, Any]]:
        return [
            node
            for node in self._walk(record)
            if str(node.get("@type", "")).endswith("Requirement")
        ]

    def effects(self, record: dict[str, Any]) -> list[dict[str, Any]]:
        return [
            node
            for node in self._walk(record)
            if str(node.get("@type", "")).endswith("Effect")
            or str(node.get("@type", "")).endswith("EffectModifier")
        ]

    def references_to(self, name: str) -> list[dict[str, Any]]:
        matches: list[dict[str, Any]] = []

        def contains(value: object) -> bool:
            if value == name:
                return True
            if isinstance(value, dict):
                return any(contains(child) for child in value.values())
            if isinstance(value, list):
                return any(contains(child) for child in value)
            return False

        for record in self.objects:
            if contains(record):
                matches.append(record)
        return matches

    def requirement_chain(self, entity_id: str, *, max_depth: int = 12) -> dict[str, Any]:
        if not self.graph:
            raise ValueError(f"Progression graph is missing beside {self.source}.")

        def condition_chain(
            condition: dict[str, Any],
            active: frozenset[str],
            depth: int,
        ) -> dict[str, Any]:
            expanded = dict(condition)
            if "conditions" in condition:
                expanded["conditions"] = [
                    condition_chain(child, active, depth)
                    for child in condition["conditions"]
                ]
            target = condition.get("target")
            if not target:
                return expanded
            expanded["targetEntity"] = self.graph_entities.get(target)
            if condition.get("type") == "PrerequisiteLinkRequirement":
                link = self.graph_links.get(target)
                if link:
                    index = 0
                    if condition.get("operator") == "Tier":
                        index = int((condition.get("value") or {}).get("base") or 0)
                    tier = next(
                        (item for item in link["tiers"] if int(item["index"]) == index),
                        None,
                    )
                    if tier:
                        expanded["linkTier"] = {
                            **tier,
                            "intrinsicConditions": [
                                condition_chain(child, active, depth)
                                for child in tier["intrinsicConditions"]
                            ],
                            "ownerRequirements": [
                                entity_chain(owner, active, depth + 1)
                                for owner in tier["owners"]
                            ],
                        }
                return expanded
            expanded["targetRequirements"] = entity_chain(target, active, depth + 1)
            return expanded

        def entity_chain(
            current_id: str,
            active: frozenset[str],
            depth: int,
        ) -> dict[str, Any]:
            entity = self.graph_entities.get(current_id)
            if current_id in active:
                return {"entity": entity, "cycle": True}
            if depth > max_depth:
                return {"entity": entity, "depthLimit": max_depth}
            gates = []
            for gate in self.graph_requirements.get(current_id, []):
                gates.append(
                    {
                        **gate,
                        "conditions": [
                            condition_chain(child, active | {current_id}, depth)
                            for child in gate["conditions"]
                        ],
                    }
                )
            return {"entity": entity, "gates": gates}

        normalized = entity_id.lower()
        if normalized not in self.graph_entities:
            raise KeyError(f"Unknown entity UUID: {entity_id}")
        return entity_chain(normalized, frozenset(), 0)
