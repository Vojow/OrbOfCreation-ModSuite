from __future__ import annotations

import argparse
import json
import logging
import tempfile
from datetime import UTC, datetime
from pathlib import Path

from .atlas import write_atlas
from .extractor import GameDataExtractor
from .model import GameDataModel
from .outputs import GRAPH_FILE, write_generated_outputs
from .paths import default_data_directory, default_log_directory, repository_root, resolve_game_data_directory
from .reports import write_cost_curve_census, write_discovery_pools
from .verify import compare_generated_directories, verify_committed


def configure_logging(command: str, log_directory: Path) -> Path:
    log_directory.mkdir(parents=True, exist_ok=True)
    stamp = datetime.now(UTC).strftime("%Y%m%dT%H%M%S.%fZ")
    path = log_directory / f"{command}-{stamp}.log"
    logging.basicConfig(
        level=logging.INFO,
        format="%(asctime)s %(levelname)s %(name)s: %(message)s",
        handlers=[logging.FileHandler(path, encoding="utf-8"), logging.StreamHandler()],
        force=True,
    )
    return path


def _extract(data_directory: Path, output_directory: Path, *, debug_type_trees: bool) -> None:
    model, graph = GameDataExtractor(
        data_directory,
        debug_type_trees=debug_type_trees,
    ).extract()
    write_generated_outputs(output_directory, model, graph)


def extract_command(arguments: argparse.Namespace) -> None:
    log_path = configure_logging("extract", Path(arguments.log_dir))
    try:
        data_directory = resolve_game_data_directory(arguments.game_dir)
        _extract(data_directory, Path(arguments.output_dir), debug_type_trees=arguments.debug_type_trees)
        summary = verify_committed(Path(arguments.output_dir))
    except Exception:
        logging.exception("Extraction failed")
        raise
    print(
        f"Extracted {summary['objects']} content objects across {summary['classes']} classes "
        f"and {summary['entities']} UUID-backed entities across "
        f"{summary['entityTypes']} types "
        f"to {Path(arguments.output_dir)}"
    )
    print(f"Log: {log_path}")


def verify_command(arguments: argparse.Namespace) -> None:
    log_path = configure_logging("verify", Path(arguments.log_dir))
    data_directory = Path(arguments.data_dir)
    try:
        summary = verify_committed(data_directory)
        if arguments.game_dir:
            installed = resolve_game_data_directory(arguments.game_dir)
            with tempfile.TemporaryDirectory(prefix="orb-gamedata-verify-") as temporary:
                fresh = Path(temporary)
                _extract(installed, fresh, debug_type_trees=arguments.debug_type_trees)
                verify_committed(fresh)
                compare_generated_directories(data_directory, fresh)
    except Exception:
        logging.exception("Verification failed")
        raise
    suffix = " and fresh installed-game scan" if arguments.game_dir else ""
    print(
        f"Verified {summary['objects']} content objects across {summary['classes']} classes and "
        f"{summary['entities']} UUID-backed entities across {summary['entityTypes']} types"
        f" against committed provenance{suffix}."
    )
    print(f"Log: {log_path}")


def query_command(arguments: argparse.Namespace) -> None:
    model = GameDataModel(arguments.source)
    if arguments.uuid:
        result: object = model.find_uuid(arguments.uuid)
    elif arguments.name:
        result = model.find_name(arguments.name, arguments.managed_type)
    elif arguments.managed_type:
        result = model.by_class(arguments.managed_type)
    elif arguments.references_to:
        result = model.references_to(arguments.references_to)
    else:
        result = model.metadata
    if arguments.requirements or arguments.effects:
        if not isinstance(result, dict) or "id" not in result:
            raise SystemExit("--requirements and --effects require a unique --uuid result.")
        result = model.requirements(result) if arguments.requirements else model.effects(result)
    if arguments.requirement_chain:
        if not arguments.uuid:
            raise SystemExit("--requirement-chain requires --uuid.")
        result = model.requirement_chain(arguments.uuid, max_depth=arguments.max_depth)
    print(json.dumps(result, indent=2, ensure_ascii=False, sort_keys=True))


def report_command(arguments: argparse.Namespace) -> None:
    defaults = {
        "atlas": (GRAPH_FILE, "progression-atlas.md", write_atlas),
        "discovery-pools": ("game-data.json", "discovery-pools.md", write_discovery_pools),
        "cost-curves": ("game-data.json", "cost-curve-census.md", write_cost_curve_census),
    }
    source_name, output_name, writer = defaults[arguments.report_name]
    source = Path(arguments.source) if arguments.source else default_data_directory() / source_name
    output = (
        Path(arguments.output)
        if arguments.output
        else repository_root() / "artifacts" / "gamedata" / output_name
    )
    writer(source, output)
    print(f"Wrote {output}")


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(prog="orb-gamedata")
    subparsers = parser.add_subparsers(dest="command", required=True)

    extract_parser = subparsers.add_parser("extract", help="scan a read-only installed game")
    extract_parser.add_argument("--game-dir", help="install root, .app, or Data directory")
    extract_parser.add_argument("--output-dir", default=str(default_data_directory()))
    extract_parser.add_argument("--log-dir", default=str(default_log_directory()))
    extract_parser.add_argument("--debug-type-trees", action="store_true")
    extract_parser.set_defaults(handler=extract_command)

    verify_parser = subparsers.add_parser("verify", help="verify committed provenance and views")
    verify_parser.add_argument("--data-dir", default=str(default_data_directory()))
    verify_parser.add_argument(
        "--game-dir",
        help="also compare byte-for-byte with a fresh installed-game scan",
    )
    verify_parser.add_argument("--log-dir", default=str(default_log_directory()))
    verify_parser.add_argument("--debug-type-trees", action="store_true")
    verify_parser.set_defaults(handler=verify_command)

    query_parser = subparsers.add_parser("query", help="query the committed full model")
    query_parser.add_argument("--source", help="alternate game-data.json")
    query_parser.add_argument("--uuid")
    query_parser.add_argument("--name")
    query_parser.add_argument("--class", dest="managed_type")
    query_parser.add_argument("--references-to")
    query_parser.add_argument("--requirements", action="store_true")
    query_parser.add_argument("--requirement-chain", action="store_true")
    query_parser.add_argument("--max-depth", type=int, default=12)
    query_parser.add_argument("--effects", action="store_true")
    query_parser.set_defaults(handler=query_command)

    report_parser = subparsers.add_parser("report", help="generate a model-derived report")
    report_parser.add_argument(
        "report_name",
        choices=("atlas", "discovery-pools", "cost-curves"),
    )
    report_parser.add_argument(
        "--source",
        help="alternate model or graph JSON appropriate to the selected report",
    )
    report_parser.add_argument(
        "--output",
        help="alternate report destination",
    )
    report_parser.set_defaults(handler=report_command)
    return parser


def main(argv: list[str] | None = None) -> None:
    arguments = build_parser().parse_args(argv)
    arguments.handler(arguments)


if __name__ == "__main__":
    main()
