from __future__ import annotations

from pathlib import Path
from typing import Any


def read_game_metadata(data_directory: Path, asset_files: tuple[str, ...]) -> dict[str, Any]:
    """Read platform-neutral product metadata from Unity's global manager file."""
    import UnityPy

    environment = UnityPy.load(str(data_directory / "globalgamemanagers"))
    game_version = ""
    unity_version = ""
    product_name = ""
    for reader in environment.objects:
        if reader.type.name not in {"PlayerSettings", "BuildSettings"}:
            continue
        record = reader.read(check_read=False)
        if reader.type.name == "PlayerSettings":
            game_version = str(getattr(record, "bundleVersion", "")).strip()
            product_name = str(getattr(record, "productName", "")).strip()
        else:
            unity_version = str(getattr(record, "m_Version", "")).strip()
    if product_name.lower() != "orb of creation":
        raise ValueError(f"Unexpected Unity product name: {product_name!r}")
    if not game_version:
        raise ValueError("PlayerSettings.bundleVersion is missing from globalgamemanagers.")
    if not unity_version:
        raise ValueError("BuildSettings.m_Version is missing from globalgamemanagers.")
    return {
        "gameVersion": game_version,
        "unityVersion": unity_version,
        "assetFiles": list(asset_files),
    }
