#!/usr/bin/env python3
"""Extract Orb of Creation's serialized progression graph from an installed game.

The game keeps exact prerequisites and most entity relationships in Unity assets, not in
Assembly-CSharp.dll.  This tool reads those assets without modifying the installation and
writes one deterministic JSON graph suitable for review, documentation, and diffing.

Optional extraction dependencies are intentionally not vendored::

    python -m pip install UnityPy TypeTreeGeneratorAPI
    python tools/extract-progression-graph.py --game-dir "C:/.../Orb Of Creation"

Unity 6 omits ordinary MonoBehaviour type trees and stores requirement implementations as
managed references.  TypeTreeGeneratorAPI reconstructs the former from the installed managed
assemblies.  Its generated nodes currently prepend the four MonoBehaviour header fields to
plain serializable managed-reference types; the bounded adapter below removes exactly that
known prefix before reading a requirement payload.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
from collections import Counter, defaultdict
from pathlib import Path
from typing import Any

try:
    import UnityPy
    import UnityPy.helpers.TypeTreeHelper as type_tree_helper
    from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator
except ImportError as error:  # pragma: no cover - exercised only on contributor machines
    raise SystemExit(
        "Missing extraction dependency. Install UnityPy and TypeTreeGeneratorAPI into the "
        "active Python environment."
    ) from error


REPOSITORY_ROOT = Path(__file__).resolve().parent.parent
DEFAULT_OUTPUT = REPOSITORY_ROOT / "data" / "progression-graph.json"
CATALOG_PATH = REPOSITORY_ROOT / "data" / "entity-mappings.tsv"
DISPLAY_NAME_PATH = REPOSITORY_ROOT / "data" / "entity-display-names.tsv"
TYPE_SUMMARY_PATH = REPOSITORY_ROOT / "data" / "entity-types.tsv"
MAPPING_SOURCE_PATH = REPOSITORY_ROOT / "data" / "source" / "message.txt"
NATIVE_CONTRACT_PATH = REPOSITORY_ROOT / "data" / "native-contracts.json"
ASSET_FILES = (
    "resources.assets",
    "sharedassets0.assets",
    "sharedassets1.assets",
    "globalgamemanagers.assets",
)
MONO_HEADER = ("m_GameObject", "m_Enabled", "m_Script", "m_Name")

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


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for block in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest().upper()


def clean_text(value: object) -> str:
    if value is None:
        return ""
    return " ".join(str(value).replace("\t", " ").replace("\r", " ").replace("\n", " ").split())


def matching_audit_baseline(main_hash: str, firstpass_hash: str) -> dict[str, Any] | None:
    if not NATIVE_CONTRACT_PATH.exists():
        return None
    manifest = json.loads(NATIVE_CONTRACT_PATH.read_text(encoding="utf-8"))
    for baseline in manifest.get("baselines", []):
        hashes = {
            assembly.get("assembly"): str(assembly.get("sha256", "")).upper()
            for assembly in baseline.get("assemblies", [])
        }
        if (
            hashes.get("assembly-csharp") == main_hash
            and hashes.get("assembly-csharp-firstpass") == firstpass_hash
        ):
            return {
                "id": baseline.get("id"),
                "platform": baseline.get("platform"),
                "auditedAt": baseline.get("auditedAt"),
                "gameBuild": baseline.get("gameBuild"),
            }
    return None


def guid_from(record: dict[str, Any]) -> str:
    container = record.get("guidContainer")
    if not isinstance(container, dict):
        return ""
    return clean_text(container.get("sg")).lower()


def is_pointer(value: object) -> bool:
    return isinstance(value, dict) and set(value) == {"m_FileID", "m_PathID"}


def walk(value: object, path: str = ""):
    """Yield every nested value with a stable dotted/indexed path."""
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


class ProgressionExtractor:
    def __init__(self, game_root: Path):
        self.game_root = game_root
        self.data_root = game_root / "Orb Of Creation_Data"
        self.managed_root = self.data_root / "Managed"
        self.environment = None
        self.generator = None
        self.reference_nodes: dict[tuple[str, str, str], Any] = {}
        self.objects: list[dict[str, Any]] = []
        self.entities: dict[str, dict[str, Any]] = {}
        self.entity_by_object: dict[tuple[str, int], str] = {}
        self.file_by_name: dict[str, Any] = {}

    def validate(self) -> None:
        required = [
            self.managed_root / "Assembly-CSharp.dll",
            self.managed_root / "Assembly-CSharp-firstpass.dll",
            *(self.data_root / name for name in ASSET_FILES),
        ]
        missing = [path for path in required if not path.is_file()]
        if missing:
            raise SystemExit("Missing installed-game inputs:\n  " + "\n  ".join(str(path) for path in missing))

    def load(self) -> None:
        asset_paths = [str(self.data_root / name) for name in ASSET_FILES]
        self.environment = UnityPy.load(*asset_paths)
        self.generator = TypeTreeGenerator("6000.0.70f1")
        self.generator.load_local_game(str(self.game_root))
        self.environment.typetree_generator = self.generator
        self.file_by_name = {
            file.name.lower(): file
            for file in self.environment.files.values()
            if hasattr(file, "objects")
        }

        # The accelerated reader cannot obtain Unity 6 managed-reference nodes from these
        # stripped asset files.  The pure reader lets the bounded resolver below supply them.
        type_tree_helper.read_typetree_boost = None
        type_tree_helper.get_ref_type_node = self._managed_reference_node

    def _managed_reference_node(self, reference: dict[str, Any], _asset_file: Any):
        identity = reference["type"]
        class_name = identity["class"]
        if not class_name:
            return None
        namespace = identity["ns"]
        assembly = identity["asm"]
        key = (assembly, namespace, class_name)
        if key not in self.reference_nodes:
            full_name = f"{namespace}.{class_name}" if namespace else class_name
            node = self.generator.get_nodes_up(assembly, full_name)
            children = node.m_Children
            if tuple(child.m_Name for child in children[:4]) == MONO_HEADER:
                node.m_Children = children[4:]
            self.reference_nodes[key] = node
        return self.reference_nodes[key]

    def extract_entities(self) -> None:
        known_entity_types = {
            line.split("\t", 1)[0]
            for line in TYPE_SUMMARY_PATH.read_text(encoding="utf-8").splitlines()[1:]
            if line
        } if TYPE_SUMMARY_PATH.exists() else set()
        entity_parse_failures: list[str] = []
        for object_reader in self.environment.objects:
            if object_reader.type.name != "MonoBehaviour":
                continue
            try:
                head = object_reader.parse_monobehaviour_head()
                script = head.m_Script.deref_parse_as_object()
            except Exception:
                continue
            try:
                record = object_reader.read_typetree()
            except Exception as error:
                if script.m_ClassName in known_entity_types:
                    entity_parse_failures.append(
                        f"{script.m_ClassName} at {object_reader.assets_file.name}:"
                        f"{object_reader.path_id}: {error}"
                    )
                continue
            entity_id = guid_from(record)
            if not entity_id:
                continue
            if entity_id in self.entities:
                raise SystemExit(f"Duplicate serialized entity UUID: {entity_id}")
            entity = {
                "id": entity_id,
                "type": script.m_ClassName,
                "name": clean_text(record.get("m_Name")),
                "displayName": clean_text(record.get("displayName")),
                "hasDisplayName": "displayName" in record,
                "assetFile": object_reader.assets_file.name,
                "pathId": object_reader.path_id,
            }
            self.entities[entity_id] = entity
            self.entity_by_object[(object_reader.assets_file.name.lower(), object_reader.path_id)] = entity_id
            self.objects.append({"entity": entity, "record": record, "reader": object_reader})

        if entity_parse_failures:
            detail = "\n  ".join(entity_parse_failures[:10])
            raise SystemExit(f"Mapped entity deserialization failures:\n  {detail}")
        if not self.entities:
            raise SystemExit("No serialized entities were extracted.")

    def resolve_pointer(self, pointer: dict[str, Any], source_file: Any) -> str | None:
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
        return self.entity_by_object.get((target_file.lower(), path_id))

    def extract_relationships(self) -> list[dict[str, Any]]:
        rows: set[tuple[str, str, str, str]] = set()
        for item in self.objects:
            source = item["entity"]["id"]
            source_file = item["reader"].assets_file
            for path, value in walk(item["record"]):
                if not is_pointer(value):
                    continue
                target = self.resolve_pointer(value, source_file)
                if target and target != source:
                    rows.add((source, target, path, relationship_kind(path)))
        return [
            {"source": source, "target": target, "path": path, "kind": kind}
            for source, target, path, kind in sorted(rows)
        ]

    @staticmethod
    def _registry(record: dict[str, Any]) -> dict[int, dict[str, Any]]:
        references = record.get("references")
        entries = references.get("RefIds", []) if isinstance(references, dict) else []
        return {
            int(entry["rid"]): entry
            for entry in entries
            if isinstance(entry, dict) and entry.get("rid") is not None
        }

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
        child_key = "andConditions" if requirement_type == "AndRequirement" else (
            "orConditions" if requirement_type == "OrRequirement" else ""
        )
        if child_key:
            result["mode"] = "all" if requirement_type == "AndRequirement" else "any"
            result["conditions"] = [
                self._decode_condition(int(child["rid"]), registry, source_file, active | {rid})
                for child in data.get(child_key, [])
                if isinstance(child, dict) and child.get("rid") is not None
            ]
            return result

        result["operator"] = enum_name(requirement_type, data.get("reqType"))
        target = None
        item = data.get("item")
        if is_pointer(item):
            target = self.resolve_pointer(item, source_file)
        if target is None:
            for _path, value in walk(data):
                if is_pointer(value):
                    target = self.resolve_pointer(value, source_file)
                    if target:
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
        for item in self.objects:
            record = item["record"]
            registry = self._registry(record)
            if not registry:
                continue
            for path, value in walk(record):
                if not isinstance(value, dict) or set(value) != {"available", "gameId", "prerequisites"}:
                    continue
                condition_refs = value.get("prerequisites")
                if not isinstance(condition_refs, list) or not condition_refs:
                    continue
                conditions = [
                    self._decode_condition(int(reference["rid"]), registry, item["reader"].assets_file)
                    for reference in condition_refs
                    if isinstance(reference, dict) and reference.get("rid") is not None
                ]
                gates.append({
                    "owner": item["entity"]["id"],
                    "path": path,
                    "mode": "all",
                    "conditions": conditions,
                })
        return sorted(gates, key=lambda row: (row["owner"], row["path"]))

    def extract_unlock_links(self, requirements: list[dict[str, Any]]) -> list[dict[str, Any]]:
        owners: dict[tuple[str, int], list[str]] = defaultdict(list)
        link_records: dict[str, dict[str, Any]] = {}
        link_files: dict[str, Any] = {}
        for item in self.objects:
            source = item["entity"]["id"]
            record = item["record"]
            if item["entity"]["type"] == "PrerequisiteLinkSO":
                link_records[source] = record
                link_files[source] = item["reader"].assets_file
            for reference in record.get("prerequisiteLinks", []) or []:
                if not isinstance(reference, dict):
                    continue
                pointer = reference.get("prerequisiteLink")
                if not is_pointer(pointer):
                    continue
                link = self.resolve_pointer(pointer, item["reader"].assets_file)
                if link:
                    owners[(link, int(reference.get("tier") or 0))].append(source)

        consumers: dict[tuple[str, int], list[dict[str, str]]] = defaultdict(list)

        def visit(condition: dict[str, Any], gate: dict[str, Any]) -> None:
            if condition.get("type") == "PrerequisiteLinkRequirement" and condition.get("target"):
                tier = 0
                if condition.get("operator") == "Tier":
                    tier = int((condition.get("value") or {}).get("base") or 0)
                consumers[(condition["target"], tier)].append({
                    "entity": gate["owner"],
                    "gate": gate["path"],
                })
            for child in condition.get("conditions", []):
                visit(child, gate)

        for gate in requirements:
            for condition in gate["conditions"]:
                visit(condition, gate)

        links: list[dict[str, Any]] = []
        for link_id, record in sorted(link_records.items()):
            registry = self._registry(record)
            tiers = []
            for index, tier in enumerate(record.get("linkTiers", []) or []):
                condition_refs = ((tier.get("prerequisites") or {}).get("prerequisites") or [])
                intrinsic = [
                    self._decode_condition(int(reference["rid"]), registry, link_files[link_id])
                    for reference in condition_refs
                    if isinstance(reference, dict) and reference.get("rid") is not None
                ]
                tiers.append({
                    "index": index,
                    "name": clean_text(tier.get("elementName")),
                    "activation": "all owners at level 1+ and all intrinsic conditions",
                    "owners": sorted(set(owners.get((link_id, index), []))),
                    "intrinsicConditions": intrinsic,
                    "consumers": sorted(
                        consumers.get((link_id, index), []),
                        key=lambda row: (row["entity"], row["gate"]),
                    ),
                })
            links.append({"link": link_id, "tiers": tiers})
        return links

    def graph(self) -> dict[str, Any]:
        self.validate()
        self.load()
        self.extract_entities()
        relationships = self.extract_relationships()
        requirements = self.extract_requirements()
        unlock_links = self.extract_unlock_links(requirements)
        main = self.managed_root / "Assembly-CSharp.dll"
        firstpass = self.managed_root / "Assembly-CSharp-firstpass.dll"
        main_hash = sha256(main)
        firstpass_hash = sha256(firstpass)
        type_counts = Counter(entity["type"] for entity in self.entities.values())
        return {
            "metadata": {
                "formatVersion": 1,
                "source": "read-only serialized Unity asset extraction",
                "unityVersion": "6000.0.70f1",
                "assemblyCSharpSha256": main_hash,
                "assemblyCSharpFirstpassSha256": firstpass_hash,
                "auditBaseline": matching_audit_baseline(main_hash, firstpass_hash),
                "assetFiles": list(ASSET_FILES),
                "entityCount": len(self.entities),
                "entityTypeCount": len(type_counts),
                "relationshipCount": len(relationships),
                "requirementGateCount": len(requirements),
                "unlockLinkCount": len(unlock_links),
                "entityCountsByType": dict(sorted(type_counts.items())),
            },
            "entities": [self.entities[key] for key in sorted(self.entities)],
            "relationships": relationships,
            "requirements": requirements,
            "unlockLinks": unlock_links,
        }


def sync_entity_catalog(graph: dict[str, Any]) -> None:
    """Refresh the checked-in identity views while preserving stable row order where possible."""
    current = {entity["id"]: entity for entity in graph["entities"]}
    prior_order: list[str] = []
    if CATALOG_PATH.exists():
        for index, line in enumerate(CATALOG_PATH.read_text(encoding="utf-8").splitlines()):
            if index == 0 or not line:
                continue
            prior_order.append(line.split("\t", 1)[0].lower())
    retained = [entity_id for entity_id in prior_order if entity_id in current]
    additions = sorted(
        current.keys() - set(retained),
        key=lambda entity_id: (
            current[entity_id]["type"],
            current[entity_id]["name"],
            entity_id,
        ),
    )
    ordered = retained + additions

    mapping_lines = ["id\tname\ttype"]
    source_lines = []
    for entity_id in ordered:
        entity = current[entity_id]
        mapping_lines.append(f"{entity_id}\t{entity['name']}\t{entity['type']}")
        source_lines.append(f"{entity_id} → {entity['name']} → {entity['type']}")
    CATALOG_PATH.write_text("\n".join(mapping_lines) + "\n", encoding="utf-8", newline="\n")
    MAPPING_SOURCE_PATH.write_text("\n".join(source_lines) + "\n", encoding="utf-8", newline="\n")

    type_counts = Counter(entity["type"] for entity in current.values())
    type_lines = ["type\tcount", *(f"{name}\t{count}" for name, count in sorted(type_counts.items()))]
    TYPE_SUMMARY_PATH.write_text("\n".join(type_lines) + "\n", encoding="utf-8", newline="\n")

    display_lines = ["id\ttype\tname\tdisplayName"]
    for entity_id in sorted(current):
        entity = current[entity_id]
        display_lines.append(
            f"{entity_id}\t{entity['type']}\t{entity['name']}\t{clean_text(entity['displayName'])}"
        )
    DISPLAY_NAME_PATH.write_text("\n".join(display_lines) + "\n", encoding="utf-8", newline="\n")
    print(f"Synchronized {len(current)} entity rows in data/*.tsv and data/source/message.txt")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--game-dir", required=True, type=Path, help="Orb of Creation installation root")
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT, help="destination JSON path")
    parser.add_argument(
        "--sync-entity-catalog",
        action="store_true",
        help="also refresh entity mappings, type counts, display names, and preserved source",
    )
    arguments = parser.parse_args()

    graph = ProgressionExtractor(arguments.game_dir.expanduser().resolve()).graph()
    output = arguments.output.expanduser().resolve()
    output.parent.mkdir(parents=True, exist_ok=True)
    serialized = json.dumps(graph, indent=2, ensure_ascii=False, sort_keys=False) + "\n"
    output.write_text(serialized, encoding="utf-8", newline="\n")
    if arguments.sync_entity_catalog:
        sync_entity_catalog(graph)
    metadata = graph["metadata"]
    print(f"Wrote {output}")
    print(f"  entities          : {metadata['entityCount']}")
    print(f"  relationships     : {metadata['relationshipCount']}")
    print(f"  requirement gates : {metadata['requirementGateCount']}")
    print(f"  unlock links      : {metadata['unlockLinkCount']}")


if __name__ == "__main__":
    main()
