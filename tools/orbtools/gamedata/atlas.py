from __future__ import annotations

import json
import re
from collections import Counter, defaultdict
from pathlib import Path
from typing import Any


VALUE_OPERATORS = {
    "AdvLevel",
    "AtLeast",
    "Count",
    "InvestmentLevel",
    "Level",
    "MasteryLevel",
    "MasteryLevelReady",
    "MasteryLv",
    "MaxQuantity",
    "PeakLevel",
    "Quantity",
    "ReachLevel",
    "ReachedLevel",
    "RecipeLevel",
    "SpellLevel",
    "Tier",
    "TotalLevel",
    "TotalPurchasedLevel",
    "Value",
}


def _markdown(value: object) -> str:
    return str(value).replace("|", "\\|").replace("\r", " ").replace("\n", " ")


def _normalized_path(path: str) -> str:
    return re.sub(r"\[\d+\]", "[]", path)


class AtlasWriter:
    def __init__(self, graph: dict[str, Any]):
        self.graph = graph
        self.entities = {entity["id"]: entity for entity in graph["entities"]}
        self.link_tiers: dict[tuple[str, int], str] = {}
        for link in graph["unlockLinks"]:
            for tier in link["tiers"]:
                self.link_tiers[(link["link"], int(tier["index"]))] = tier["name"]

    def entity_label(self, entity_id: str, *, include_type: bool = False) -> str:
        entity = self.entities.get(entity_id)
        if not entity:
            return f"unknown `{entity_id}`"
        visible = entity["displayName"] or entity["name"]
        label = _markdown(visible)
        if visible != entity["name"]:
            label += f" (`{_markdown(entity['name'])}`)"
        if include_type:
            label += f" — `{entity['type']}`"
        return label

    @staticmethod
    def _number(value: object) -> str:
        if isinstance(value, float) and value.is_integer():
            return str(int(value))
        return str(value)

    @staticmethod
    def _modifier_is_empty(value: object) -> bool:
        if not isinstance(value, dict):
            return True
        try:
            return float(value.get("adjust") or 0) == 0
        except (TypeError, ValueError):
            return False

    def value_text(self, condition: dict[str, Any]) -> str:
        value = condition.get("value")
        if not isinstance(value, dict):
            return ""
        base = self._number(value.get("base", 0))
        per_level = value.get("perLevel") or {}
        modifier_per_level = value.get("modifierPerLevel") or {}
        if self._modifier_is_empty(per_level) and self._modifier_is_empty(modifier_per_level):
            return base
        return (
            f"base {base}; `perLevel(type={per_level.get('type')}, "
            f"adjust={self._number(per_level.get('adjust', 0))})`; "
            f"`modifierPerLevel(type={modifier_per_level.get('type')}, "
            f"adjust={self._number(modifier_per_level.get('adjust', 0))})`"
        )

    def condition_text(self, condition: dict[str, Any]) -> str:
        children = condition.get("conditions")
        if isinstance(children, list):
            operator = " AND " if condition.get("mode") == "all" else " OR "
            return "(" + operator.join(self.condition_text(child) for child in children) + ")"
        requirement_type = condition.get("type", "UnknownRequirement")
        operator = condition.get("operator", "Unknown")
        target_id = condition.get("target")
        target = self.entity_label(target_id) if target_id else "unresolved target"
        value = self.value_text(condition)
        if requirement_type == "PrerequisiteLinkRequirement" and target_id:
            tier = 0 if operator == "Base" else int((condition.get("value") or {}).get("base") or 0)
            tier_name = self.link_tiers.get((target_id, tier), f"tier {tier}")
            return f"unlock link {target} / **{_markdown(tier_name)}**"
        if operator in VALUE_OPERATORS:
            return f"{target}: **{operator} {value}**"
        return f"{target}: **{operator}**"

    def conditions_text(self, conditions: list[dict[str, Any]]) -> str:
        return " AND ".join(self.condition_text(condition) for condition in conditions) or "none"

    def render(self) -> str:
        metadata = self.graph["metadata"]
        lines = [
            "# Exhaustive progression atlas",
            "",
            "> Generated from `data/progression-graph.json` by `uv run orb-gamedata report atlas`.",
            "> This file is a local report; regenerate it instead of committing it.",
            "",
            f"Game version: **{metadata['gameVersion']}**; Unity **{metadata['unityVersion']}**.",
            "",
            "| Measure | Count |",
            "|---|---:|",
            f"| Serialized entities | {metadata['entityCount']:,} |",
            f"| Entity-to-entity references | {metadata['relationshipCount']:,} |",
            f"| Requirement gates | {metadata['requirementGateCount']:,} |",
            f"| Reusable prerequisite links | {metadata['unlockLinkCount']:,} |",
            "",
            "## Reusable prerequisite links",
            "",
            "A tier combines all bound owners at level 1+ with every intrinsic condition.",
            "",
        ]
        for link in sorted(self.graph["unlockLinks"], key=lambda row: self.entities[row["link"]]["name"]):
            entity = self.entities[link["link"]]
            lines.extend(
                [
                    f"### {_markdown(entity['name'])}",
                    "",
                    f"`{entity['id']}`",
                    "",
                    "| Tier | Becomes enabled when | Direct consumers |",
                    "|---|---|---|",
                ]
            )
            for tier in link["tiers"]:
                owners = ", ".join(self.entity_label(owner) for owner in tier["owners"])
                owner_text = f"all bound owners are level 1+: {owners}; AND " if owners else ""
                activation = owner_text + self.conditions_text(tier["intrinsicConditions"])
                consumers = ", ".join(
                    self.entity_label(consumer["entity"], include_type=True)
                    + f" (`{_markdown(consumer['gate'])}`)"
                    for consumer in tier["consumers"]
                ) or "none"
                lines.append(
                    f"| {tier['index']}: {_markdown(tier['name'])} | {activation} | {consumers} |"
                )
            lines.append("")

        lines.extend(["## Every serialized requirement gate", ""])
        grouped: dict[str, list[dict[str, Any]]] = defaultdict(list)
        for gate in self.graph["requirements"]:
            grouped[self.entities[gate["owner"]]["type"]].append(gate)
        for managed_type in sorted(grouped):
            lines.extend(
                [
                    f"### {managed_type}",
                    "",
                    "| Entity | Gate field | Required condition |",
                    "|---|---|---|",
                ]
            )
            for gate in sorted(grouped[managed_type], key=lambda row: (row["owner"], row["path"])):
                lines.append(
                    f"| {self.entity_label(gate['owner'])} | `{_markdown(gate['path'])}` | "
                    f"{self.conditions_text(gate['conditions'])} |"
                )
            lines.append("")

        path_counts = Counter(_normalized_path(gate["path"]) for gate in self.graph["requirements"])
        kind_counts = Counter(relation["kind"] for relation in self.graph["relationships"])
        lines.extend(["## Coverage summaries", "", "| Requirement field | Gates |", "|---|---:|"])
        for path, count in sorted(path_counts.items(), key=lambda item: (-item[1], item[0])):
            lines.append(f"| `{_markdown(path)}` | {count:,} |")
        lines.extend(["", "| Relationship kind | Edges |", "|---|---:|"])
        for kind, count in sorted(kind_counts.items(), key=lambda item: (-item[1], item[0])):
            lines.append(f"| {kind} | {count:,} |")
        lines.append("")
        return "\n".join(lines)


def write_atlas(source: Path, output: Path) -> None:
    graph = json.loads(source.read_text(encoding="utf-8"))
    rendered = AtlasWriter(graph).render()
    output.parent.mkdir(parents=True, exist_ok=True)
    with output.open("w", encoding="utf-8", newline="\n") as handle:
        handle.write(rendered)
