from __future__ import annotations

import os
from pathlib import Path


ASSET_FILES = (
    "resources.assets",
    "sharedassets0.assets",
    "sharedassets1.assets",
    "globalgamemanagers.assets",
    "level0",
    "level1",
)


def repository_root() -> Path:
    return Path(__file__).resolve().parents[3]


def default_data_directory() -> Path:
    return repository_root() / "data"


def default_log_directory() -> Path:
    return repository_root() / "artifacts" / "gamedata" / "logs"


def _data_directory_candidates(path: Path) -> list[Path]:
    candidates = [path]
    candidates.append(path / "Orb Of Creation_Data")
    candidates.append(path / "Contents" / "Resources" / "Data")
    if path.name == "Contents":
        candidates.append(path / "Resources" / "Data")
    return candidates


def _required_game_paths(path: Path) -> list[Path]:
    return [
        path / "Managed" / "Assembly-CSharp.dll",
        path / "Managed" / "Assembly-CSharp-firstpass.dll",
        path / "globalgamemanagers",
        *(path / name for name in ASSET_FILES),
    ]


def validate_game_data_directory(path: Path) -> Path:
    required = _required_game_paths(path)
    missing = [candidate for candidate in required if not candidate.is_file()]
    if missing:
        details = "\n  ".join(str(candidate) for candidate in missing)
        raise ValueError(f"Game data directory is incomplete; missing:\n  {details}")
    return path.resolve()


def resolve_game_data_directory(explicit: str | Path | None = None) -> Path:
    if explicit is not None:
        supplied = Path(explicit).expanduser()
        failures: list[tuple[int, str]] = []
        for candidate in _data_directory_candidates(supplied):
            try:
                return validate_game_data_directory(candidate)
            except ValueError as error:
                present = sum(path.is_file() for path in _required_game_paths(candidate))
                failures.append((present, str(error)))
        raise ValueError(
            f"Could not resolve an Orb of Creation data directory from {supplied}.\n"
            + max(failures, key=lambda item: item[0])[1]
        )

    candidates: list[Path] = []
    configured = os.environ.get("OOC_GAME_DIR")
    if configured:
        candidates.extend(_data_directory_candidates(Path(configured).expanduser()))
    candidates.extend(
        _data_directory_candidates(
            Path.home()
            / "Library"
            / "Application Support"
            / "Steam"
            / "steamapps"
            / "common"
            / "Orb of Creation"
            / "Orb Of Creation.app"
        )
    )
    program_files = os.environ.get("PROGRAMFILES(X86)") or os.environ.get("PROGRAMFILES")
    if program_files:
        candidates.extend(
            _data_directory_candidates(
                Path(program_files)
                / "Steam"
                / "steamapps"
                / "common"
                / "Orb of Creation"
            )
        )

    for candidate in candidates:
        try:
            return validate_game_data_directory(candidate)
        except ValueError:
            continue
    raise ValueError(
        "No complete Orb of Creation installation was found. Pass --game-dir or set OOC_GAME_DIR."
    )
