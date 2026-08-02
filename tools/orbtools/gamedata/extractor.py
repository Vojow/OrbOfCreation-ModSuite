from __future__ import annotations

import json
import logging
from collections import Counter, defaultdict
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Iterable

from .paths import ASSET_FILES
from .provenance import read_game_metadata
from .typetrees import TypeTreeSupport, UNITY_VERSION


LOGGER = logging.getLogger(__name__)

SKIP_FIELDS = {
    "m_GameObject",
    "m_Enabled",
    "m_Script",
    "m_Name",
    "m_EditorClassIdentifier",
    "m_EditorHideFlags",
    "m_ObjectHideFlags",
    "m_CorrespondingSourceObject",
    "m_PrefabInstance",
    "m_PrefabAsset",
    "references",
}

REQUIREMENT_ENUMS: dict[str, tuple[str, ...]] = {
    "AlchemyRecipeRequirement": ("Discovered", "Visible", "RecipeLevel", "MasteryLevel", "AdvLevel"),
    "ChallengeRequirement": ("OneLevel", "MaxLevel", "ReachLevel"),
    "CraftingStructureRequirements": ("Available", "Quantity"),
    "EquipmentRequirement": ("Created", "Available", "MasteryLv"),
    "GenericRequirement": ("Visible", "Level", "Discovered"),
    "ListRequirement": ("Count", "AnyVisible", "AnyAvailable"),
    "NumberRequirement": ("Value",),
    "PrerequisiteLinkRequirement": ("Base", "Tier"),
    "ResearchRequirement": ("OneLevel", "MaxLevel", "AtLeast", "Visible"),
    "ResearchTypeRequirement": ("PeakLevel", "TotalLevel", "InvestmentLevel", "TotalPurchasedLevel"),
    "ResourceRequirement": ("Visible", "Quantity", "MaxQuantity"),
    "RitualRequirement": ("Discovered", "ReachedLevel"),
    "SpellRequirement": ("Discovered", "Visible", "SpellLevel", "MasteryLevel", "MasteryLevelReady"),
    "StructureRequirement": ("Quantity", "Available"),
    "UpgradeableValueRequirement": ("AtLeast",),
    "UpgradeRequirement": ("OneLevel", "MaxLevel", "AtLeast", "Visible"),
    "ViewRequirement": ("Visible",),
}


def clean_text(value: object) -> str:
    if value is None:
        return ""
    return " ".join(str(value).replace("\t", " ").replace("\r", " ").replace("\n", " ").split())


def guid_from(record: dict[str, Any]) -> str:
    container = record.get("guidContainer")
    if not isinstance(container, dict):
        return ""
    return clean_text(container.get("sg")).lower()


def is_pointer(value: object) -> bool:
    return isinstance(value, dict) and set(value) == {"m_FileID", "m_PathID"}


def is_managed_reference(value: object) -> bool:
    return isinstance(value, dict) and set(value) == {"rid"}


def walk(value: object, path: str = "") -> Iterable[tuple[str, object]]:
    yield path, value
    if isinstance(value, dict):
        for key in sorted(value):
            child_path = f"{path}.{key}" if path else key
            yield from walk(value[key], child_path)
    elif isinstance(value, list):
        for index, child in enumerate(value):
            yield from walk(child, f"{path}[{index}]")


def relationship_kind(path: str) -> str:
    lowered = path.lower()
    if "prerequisite" in lowered:
        return "progression"
    if "cost" in lowered or "usage" in lowered:
        return "cost"
    if "effect" in lowered or "modifier" in lowered:
        return "effect"
    if "type" in lowered:
        return "type-membership"
    if "recipe" in lowered or "glyph" in lowered:
        return "recipe"
    if "view" in lowered:
        return "view"
    if "tutorial" in lowered:
        return "tutorial"
    if "resource" in lowered:
        return "resource"
    return "reference"


def enum_name(requirement_type: str, raw_value: object) -> str:
    values = REQUIREMENT_ENUMS.get(requirement_type, ())
    try:
        index = int(raw_value)
    except (TypeError, ValueError):
        return f"Unknown({raw_value})"
    return values[index] if 0 <= index < len(values) else f"Unknown({index})"


@dataclass(frozen=True)
class ScriptHeader:
    reader: Any
    asset_file: str
    path_id: int
    name: str
    class_name: str
    assembly: str


@dataclass(frozen=True)
class RawEntity:
    reader: Any
    asset_file: str
    path_id: int
    entity_id: str
    class_name: str
    name: str
    display_name: str
    has_display_name: bool
    record: dict[str, Any]


class GameDataExtractor:
    def __init__(self, data_directory: Path, *, debug_type_trees: bool = False):
        self.data_directory = data_directory
        self.managed_directory = data_directory / "Managed"
        self.debug_type_trees = debug_type_trees
        self.environment: Any = None
        self.type_trees: TypeTreeSupport | None = None
        self.headers: list[ScriptHeader] = []
        self.entities: list[RawEntity] = []
        self.entity_by_object: dict[tuple[str, int], RawEntity] = {}
        self.name_by_object: dict[tuple[str, int], str] = {}
        self.metadata = read_game_metadata(data_directory, ASSET_FILES)
        if self.metadata["unityVersion"] != UNITY_VERSION:
            raise ValueError(
                f"Unsupported Unity serialization version: {self.metadata['unityVersion']} "
                f"(extractor supports {UNITY_VERSION})."
            )

    @staticmethod
    def _object_key(asset_file: str, path_id: int) -> tuple[str, int]:
        return asset_file.lower(), path_id

    def load(self) -> None:
        import UnityPy

        paths = [str(self.data_directory / name) for name in ASSET_FILES]
        LOGGER.info("Loading %d Unity asset files from %s", len(paths), self.data_directory)
        self.environment = UnityPy.load(*paths)
        self.type_trees = TypeTreeSupport(
            self.managed_directory,
            debug=self.debug_type_trees,
        )
        self.type_trees.install_managed_reference_reader()

    def scan_headers(self) -> None:
        for reader in self.environment.objects:
            if reader.type.name != "MonoBehaviour":
                continue
            try:
                head = reader.parse_monobehaviour_head()
                script = head.m_Script.deref_parse_as_object()
                class_name = clean_text(getattr(script, "m_ClassName", ""))
                assembly = clean_text(getattr(script, "m_AssemblyName", "")) or "Assembly-CSharp"
                name = clean_text(getattr(head, "m_Name", ""))
            except Exception as error:
                LOGGER.debug(
                    "Skipping unreadable MonoBehaviour header %s:%s: %s",
                    reader.assets_file.name,
                    reader.path_id,
                    error,
                )
                continue
            if not class_name:
                continue
            header = ScriptHeader(
                reader=reader,
                asset_file=reader.assets_file.name,
                path_id=int(reader.path_id),
                name=name,
                class_name=class_name,
                assembly=assembly,
            )
            self.headers.append(header)
            if name:
                self.name_by_object[self._object_key(header.asset_file, header.path_id)] = name
        self.headers.sort(key=lambda item: (item.asset_file.lower(), item.path_id))
        LOGGER.info("Resolved %d scripted object headers", len(self.headers))

    @staticmethod
    def _has_identity_field(nodes: list[dict[str, Any]]) -> bool:
        return any(node.get("m_Name") == "guidContainer" for node in nodes)

    def read_entities(self) -> None:
        assert self.type_trees is not None
        failures: list[str] = []
        empty_identity_counts: Counter[str] = Counter()
        entities_by_id: dict[str, RawEntity] = {}
        for header in self.headers:
            nodes = self.type_trees.nodes_for(header.assembly, header.class_name)
            if not nodes or not self._has_identity_field(nodes):
                continue
            try:
                record = header.reader.read_typetree(nodes)
            except Exception as error:
                failures.append(
                    f"{header.class_name} at {header.asset_file}:{header.path_id}: {error}"
                )
                continue
            entity_id = guid_from(record)
            if not entity_id:
                empty_identity_counts[header.class_name] += 1
                continue
            entity = RawEntity(
                reader=header.reader,
                asset_file=header.asset_file,
                path_id=header.path_id,
                entity_id=entity_id,
                class_name=header.class_name,
                name=clean_text(record.get("m_Name")) or header.name,
                display_name=clean_text(record.get("displayName")),
                has_display_name="displayName" in record,
                record=record,
            )
            key = self._object_key(entity.asset_file, entity.path_id)
            if entity_id in entities_by_id:
                raise ValueError(f"Duplicate serialized entity UUID: {entity_id}")
            self.entities.append(entity)
            entities_by_id[entity_id] = entity
            self.entity_by_object[key] = entity

        if failures:
            details = "\n  ".join(failures[:20])
            raise ValueError(f"Entity deserialization failures ({len(failures)}):\n  {details}")
        if not self.entities:
            raise ValueError("No UUID-backed serialized game entities were extracted.")
        self.entities.sort(key=lambda item: (item.class_name, item.name, item.entity_id))
        ignored = sum(empty_identity_counts.values())
        LOGGER.info(
            "Read %d UUID-backed entities; excluded %d identity-shaped objects with empty UUIDs",
            len(self.entities),
            ignored,
        )
        for class_name, count in sorted(empty_identity_counts.items()):
            LOGGER.debug("Excluded empty identities: %s=%d", class_name, count)

    def resolve_pointer(self, pointer: dict[str, Any], source_file: Any) -> RawEntity | None:
        path_id = int(pointer.get("m_PathID") or 0)
        if path_id == 0:
            return None
        file_id = int(pointer.get("m_FileID") or 0)
        if file_id == 0:
            target_file = source_file.name
        elif 0 < file_id <= len(source_file.externals):
            target_file = source_file.externals[file_id - 1].name
        else:
            return None
        return self.entity_by_object.get(self._object_key(target_file, path_id))

    def resolve_named_pointer(self, pointer: dict[str, Any], source_file: Any) -> str | None:
        entity = self.resolve_pointer(pointer, source_file)
        if entity is not None:
            return entity.name
        path_id = int(pointer.get("m_PathID") or 0)
        if path_id == 0:
            return None
        file_id = int(pointer.get("m_FileID") or 0)
        if file_id == 0:
            target_file = source_file.name
        elif 0 < file_id <= len(source_file.externals):
            target_file = source_file.externals[file_id - 1].name
        else:
            return None
        return self.name_by_object.get(self._object_key(target_file, path_id))

    @staticmethod
    def _registry(record: dict[str, Any]) -> dict[int, dict[str, Any]]:
        references = record.get("references")
        entries = references.get("RefIds", []) if isinstance(references, dict) else []
        return {
            int(entry["rid"]): entry
            for entry in entries
            if isinstance(entry, dict) and entry.get("rid") is not None
        }

    def _resolve_value(
        self,
        value: object,
        registry: dict[int, dict[str, Any]],
        source_file: Any,
        active: frozenset[int] = frozenset(),
    ) -> object:
        if is_pointer(value):
            return self.resolve_named_pointer(value, source_file)
        if is_managed_reference(value):
            rid = int(value["rid"])
            if rid < 0:
                return None
            if rid in active:
                return {"@type": "<cycle>", "rid": rid}
            entry = registry.get(rid)
            if entry is None or "data" not in entry:
                return {"@type": "<missing>", "rid": rid}
            identity = entry.get("type") or {}
            class_name = identity.get("class") if isinstance(identity, dict) else ""
            resolved: dict[str, Any] = {"@type": clean_text(class_name)}
            payload = entry["data"]
            if isinstance(payload, dict):
                resolved.update(
                    self._resolve_value(payload, registry, source_file, active | {rid})
                )
            else:
                resolved["value"] = self._resolve_value(
                    payload, registry, source_file, active | {rid}
                )
            return resolved
        if isinstance(value, dict):
            return {
                key: self._resolve_value(child, registry, source_file, active)
                for key, child in value.items()
            }
        if isinstance(value, list):
            return [self._resolve_value(child, registry, source_file, active) for child in value]
        return value

    def full_model(self) -> dict[str, Any]:
        objects: dict[str, dict[str, Any]] = {}
        for entity in self.entities:
            registry = self._registry(entity.record)
            record: dict[str, Any] = {
                "id": entity.entity_id,
                "name": entity.name,
                "class": entity.class_name,
            }
            for key, value in entity.record.items():
                if key in SKIP_FIELDS:
                    continue
                record[key] = self._resolve_value(value, registry, entity.reader.assets_file)
            bucket = objects.setdefault(entity.class_name, {})
            name_key = entity.name
            if not name_key or name_key in bucket:
                name_key = f"{entity.name}#{entity.entity_id}"
            bucket[name_key] = record

        type_counts = Counter(entity.class_name for entity in self.entities)
        metadata = {
            "formatVersion": 1,
            "source": "read-only serialized Unity asset extraction",
            **self.metadata,
            "objectCount": len(self.entities),
            "classCount": len(type_counts),
            "objectCountsByClass": dict(sorted(type_counts.items())),
        }
        ordered_objects = {
            class_name: dict(sorted(bucket.items()))
            for class_name, bucket in sorted(objects.items())
        }
        return {"metadata": metadata, "objects": ordered_objects}

    def extract_relationships(self) -> list[dict[str, Any]]:
        rows: set[tuple[str, str, str, str]] = set()
        for entity in self.entities:
            for path, value in walk(entity.record):
                if not is_pointer(value):
                    continue
                target = self.resolve_pointer(value, entity.reader.assets_file)
                if target is not None and target.entity_id != entity.entity_id:
                    rows.add(
                        (
                            entity.entity_id,
                            target.entity_id,
                            path,
                            relationship_kind(path),
                        )
                    )
        return [
            {"source": source, "target": target, "path": path, "kind": kind}
            for source, target, path, kind in sorted(rows)
        ]

    def _decode_condition(
        self,
        rid: int,
        registry: dict[int, dict[str, Any]],
        source_file: Any,
        active: frozenset[int] = frozenset(),
    ) -> dict[str, Any]:
        if rid in active:
            return {"type": "Cycle", "rid": rid}
        entry = registry.get(rid)
        if not entry:
            return {"type": "MissingReference", "rid": rid}
        identity = entry.get("type") or {}
        requirement_type = clean_text(identity.get("class"))
        data = entry.get("data") or {}
        result: dict[str, Any] = {"type": requirement_type, "rid": rid}
        child_key = (
            "andConditions"
            if requirement_type == "AndRequirement"
            else "orConditions" if requirement_type == "OrRequirement" else ""
        )
        if child_key:
            result["mode"] = "all" if requirement_type == "AndRequirement" else "any"
            result["conditions"] = [
                self._decode_condition(
                    int(child["rid"]), registry, source_file, active | {rid}
                )
                for child in data.get(child_key, [])
                if isinstance(child, dict) and child.get("rid") is not None
            ]
            return result

        result["operator"] = enum_name(requirement_type, data.get("reqType"))
        target = None
        item = data.get("item")
        if is_pointer(item):
            target_entity = self.resolve_pointer(item, source_file)
            target = target_entity.entity_id if target_entity else None
        if target is None:
            for _path, value in walk(data):
                if not is_pointer(value):
                    continue
                target_entity = self.resolve_pointer(value, source_file)
                if target_entity:
                    target = target_entity.entity_id
                    break
        if target:
            result["target"] = target
        value = data.get("value")
        if isinstance(value, dict):
            result["value"] = {
                "base": value.get("baseValue", 0),
                "perLevel": value.get("perLevel", {}),
                "modifierPerLevel": value.get("modPerLevel", {}),
            }
        return result

    def extract_requirements(self) -> list[dict[str, Any]]:
        gates: list[dict[str, Any]] = []
        for entity in self.entities:
            registry = self._registry(entity.record)
            if not registry:
                continue
            for path, value in walk(entity.record):
                if not isinstance(value, dict):
                    continue
                if set(value) != {"available", "gameId", "prerequisites"}:
                    continue
                references = value.get("prerequisites")
                if not isinstance(references, list) or not references:
                    continue
                conditions = [
                    self._decode_condition(
                        int(reference["rid"]),
                        registry,
                        entity.reader.assets_file,
                    )
                    for reference in references
                    if isinstance(reference, dict) and reference.get("rid") is not None
                ]
                gates.append(
                    {
                        "owner": entity.entity_id,
                        "path": path,
                        "mode": "all",
                        "conditions": conditions,
                    }
                )
        return sorted(gates, key=lambda row: (row["owner"], row["path"]))

    def extract_unlock_links(self, requirements: list[dict[str, Any]]) -> list[dict[str, Any]]:
        owners: dict[tuple[str, int], list[str]] = defaultdict(list)
        link_records: dict[str, RawEntity] = {}
        for entity in self.entities:
            if entity.class_name == "PrerequisiteLinkSO":
                link_records[entity.entity_id] = entity
            for reference in entity.record.get("prerequisiteLinks", []) or []:
                if not isinstance(reference, dict):
                    continue
                pointer = reference.get("prerequisiteLink")
                if not is_pointer(pointer):
                    continue
                link = self.resolve_pointer(pointer, entity.reader.assets_file)
                if link:
                    owners[(link.entity_id, int(reference.get("tier") or 0))].append(
                        entity.entity_id
                    )

        consumers: dict[tuple[str, int], list[dict[str, str]]] = defaultdict(list)

        def visit(condition: dict[str, Any], gate: dict[str, Any]) -> None:
            if condition.get("type") == "PrerequisiteLinkRequirement" and condition.get("target"):
                tier = 0
                if condition.get("operator") == "Tier":
                    tier = int((condition.get("value") or {}).get("base") or 0)
                consumers[(condition["target"], tier)].append(
                    {"entity": gate["owner"], "gate": gate["path"]}
                )
            for child in condition.get("conditions", []):
                visit(child, gate)

        for gate in requirements:
            for condition in gate["conditions"]:
                visit(condition, gate)

        links: list[dict[str, Any]] = []
        for link_id, entity in sorted(link_records.items()):
            registry = self._registry(entity.record)
            tiers = []
            for index, tier in enumerate(entity.record.get("linkTiers", []) or []):
                condition_refs = ((tier.get("prerequisites") or {}).get("prerequisites") or [])
                intrinsic = [
                    self._decode_condition(
                        int(reference["rid"]),
                        registry,
                        entity.reader.assets_file,
                    )
                    for reference in condition_refs
                    if isinstance(reference, dict) and reference.get("rid") is not None
                ]
                tiers.append(
                    {
                        "index": index,
                        "name": clean_text(tier.get("elementName")),
                        "owners": sorted(set(owners.get((link_id, index), []))),
                        "intrinsicConditions": intrinsic,
                        "consumers": sorted(
                            consumers.get((link_id, index), []),
                            key=lambda row: (row["entity"], row["gate"]),
                        ),
                    }
                )
            links.append({"link": link_id, "tiers": tiers})
        return links

    def progression_graph(self) -> dict[str, Any]:
        relationships = self.extract_relationships()
        requirements = self.extract_requirements()
        unlock_links = self.extract_unlock_links(requirements)
        type_counts = Counter(entity.class_name for entity in self.entities)
        metadata = {
            "formatVersion": 1,
            "source": "derived from the committed full serialized model scan",
            **self.metadata,
            "entityCount": len(self.entities),
            "entityTypeCount": len(type_counts),
            "relationshipCount": len(relationships),
            "requirementGateCount": len(requirements),
            "unlockLinkCount": len(unlock_links),
            "entityCountsByType": dict(sorted(type_counts.items())),
        }
        entities = [
            {
                "id": entity.entity_id,
                "type": entity.class_name,
                "name": entity.name,
                "displayName": entity.display_name,
                "hasDisplayName": entity.has_display_name,
            }
            for entity in sorted(self.entities, key=lambda item: item.entity_id)
        ]
        return {
            "metadata": metadata,
            "entities": entities,
            "relationships": relationships,
            "requirements": requirements,
            "unlockLinks": unlock_links,
        }

    def extract(self) -> tuple[dict[str, Any], dict[str, Any]]:
        self.load()
        self.scan_headers()
        self.read_entities()
        model = self.full_model()
        graph = self.progression_graph()
        LOGGER.info(
            "Extraction complete: %d objects, %d classes, %d relationships, %d requirement gates",
            model["metadata"]["objectCount"],
            model["metadata"]["classCount"],
            graph["metadata"]["relationshipCount"],
            graph["metadata"]["requirementGateCount"],
        )
        return model, graph
