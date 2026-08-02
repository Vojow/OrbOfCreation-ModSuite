from __future__ import annotations

from pathlib import Path
from typing import Any, Iterable

from .model import GameDataModel


def _scalar(value: object) -> str:
    if value is None:
        return "none"
    if isinstance(value, dict) and set(value) == {"variable"}:
        return str(value["variable"])
    return str(value)


def _write(path: Path, lines: Iterable[str]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8", newline="\n") as handle:
        handle.write("\n".join(lines).rstrip() + "\n")


def discovery_pool_rows(model: GameDataModel) -> list[dict[str, Any]]:
    rows: list[dict[str, Any]] = []
    for tree in model.by_class("DiscoveryTreeSO").values():
        pool_name = str(tree.get("discoverList") or "")
        pools = model.find_name(pool_name)
        if len(pools) != 1:
            raise ValueError(
                f"Discovery tree {tree.get('name')} resolves {pool_name!r} to {len(pools)} lists."
            )
        pool = pools[0]
        entries = pool.get("value")
        if not isinstance(entries, list):
            raise ValueError(f"Discovery pool {pool_name!r} has no authored value list.")
        rows.append(
            {
                "id": tree["id"],
                "name": tree["name"],
                "displayName": tree.get("displayName", ""),
                "pool": {
                    "id": pool["id"],
                    "name": pool["name"],
                    "type": pool["class"],
                    "entries": list(entries),
                },
                "costEntries": tree.get("costEntries", []),
                "costReducingResearches": tree.get("costReducingResearches", []),
                "costReductionModifier": tree.get("costReductionModifier"),
            }
        )
    return sorted(rows, key=lambda row: (row["name"], row["id"]))


def write_discovery_pools(source: Path, output: Path) -> None:
    model = GameDataModel(source)
    rows = discovery_pool_rows(model)
    lines = [
        "# Authored discovery pools",
        "",
        (
            f"Game {model.metadata.get('gameVersion', 'unknown')} contains {len(rows)} authored "
            "discovery trees. Pool membership is the serialized list value; current availability "
            "and progress are runtime state."
        ),
    ]
    for row in rows:
        pool = row["pool"]
        lines.extend(
            [
                "",
                f"## {row['displayName'] or row['name']}",
                "",
                f"- Tree: `{row['name']}` (`{row['id']}`)",
                f"- Pool: `{pool['name']}` / `{pool['type']}` (`{pool['id']}`)",
                f"- Authored entries: {len(pool['entries'])}",
                (
                    "- Cost-reducing researches: "
                    + (", ".join(f"`{name}`" for name in row["costReducingResearches"]) or "none")
                ),
                f"- Cost-reduction variable: `{_scalar(row['costReductionModifier'])}`",
                "- Cost brackets:",
            ]
        )
        for cost in row["costEntries"]:
            bracket = f"first {cost.get('count')}" if cost.get("count") else "remaining"
            resources = ", ".join(
                f"{entry.get('value')} {entry.get('resource')}"
                for entry in (cost.get("baseCost") or {}).get("costs", [])
            )
            lines.append(
                f"  - {bracket}: {resources or 'none'}; "
                f"scaling `{_scalar(cost.get('scaling'))}`"
            )
        lines.extend(["- Pool members:", ""])
        lines.extend(f"  - `{entry}`" for entry in pool["entries"])
    _write(output, lines)


def _negative_cost_nodes(value: object, path: tuple[str, ...] = ()) -> Iterable[tuple[str, dict]]:
    if isinstance(value, dict):
        adjustment = value.get("adjust")
        dotted = ".".join(path)
        if (
            isinstance(adjustment, (int, float))
            and not isinstance(adjustment, bool)
            and adjustment < 0
            and "cost" in dotted.lower()
        ):
            yield dotted, value
        for key, child in value.items():
            yield from _negative_cost_nodes(child, (*path, str(key)))
    elif isinstance(value, list):
        for index, child in enumerate(value):
            yield from _negative_cost_nodes(child, (*path, f"[{index}]"))


def negative_cost_modifier_rows(model: GameDataModel) -> list[dict[str, Any]]:
    rows = []
    for record in model.objects:
        for path, node in _negative_cost_nodes(record):
            rows.append(
                {
                    "id": record["id"],
                    "type": record["class"],
                    "name": record["name"],
                    "displayName": record.get("displayName", ""),
                    "path": path,
                    "adjust": node["adjust"],
                    "operation": node.get("type"),
                    "order": node.get("order"),
                    "group": (node.get("gc") or {}).get("sg"),
                }
            )
    return sorted(rows, key=lambda row: (row["type"], row["name"], row["path"], row["id"]))


def write_cost_curve_census(source: Path, output: Path) -> None:
    model = GameDataModel(source)
    rows = negative_cost_modifier_rows(model)
    entities = {row["id"] for row in rows}
    lines = [
        "# Authored negative cost modifiers",
        "",
        (
            f"Game {model.metadata.get('gameVersion', 'unknown')} contains {len(rows)} negative "
            f"authored adjustments on cost-named fields across {len(entities)} entities. This "
            "census identifies serialized modifiers; runtime fold order and current values still "
            "require running-game evidence."
        ),
        "",
        "| Type | Entity | Field | Adjustment | Operation | Group |",
        "|---|---|---|---:|---:|---|",
    ]
    for row in rows:
        lines.append(
            f"| `{row['type']}` | `{row['name']}` (`{row['id']}`) | `{row['path']}` | "
            f"{row['adjust']} | {row['operation']} | `{row['group']}` |"
        )
    _write(output, lines)
