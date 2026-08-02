from pathlib import Path

import pytest

from orbtools.gamedata.paths import ASSET_FILES, resolve_game_data_directory


def make_data_directory(path: Path) -> Path:
    managed = path / "Managed"
    managed.mkdir(parents=True)
    (managed / "Assembly-CSharp.dll").write_bytes(b"main")
    (managed / "Assembly-CSharp-firstpass.dll").write_bytes(b"first")
    (path / "globalgamemanagers").write_bytes(b"metadata")
    for name in ASSET_FILES:
        (path / name).write_bytes(b"asset")
    return path


def test_resolves_windows_install_root(tmp_path: Path) -> None:
    expected = make_data_directory(tmp_path / "Orb of Creation" / "Orb Of Creation_Data")
    assert resolve_game_data_directory(tmp_path / "Orb of Creation") == expected.resolve()


def test_resolves_macos_app(tmp_path: Path) -> None:
    app = tmp_path / "Orb Of Creation.app"
    expected = make_data_directory(app / "Contents" / "Resources" / "Data")
    assert resolve_game_data_directory(app) == expected.resolve()


def test_incomplete_copy_fails_loud(tmp_path: Path) -> None:
    partial = tmp_path / "Orb Of Creation_Data"
    (partial / "Managed").mkdir(parents=True)
    with pytest.raises(ValueError, match="incomplete"):
        resolve_game_data_directory(partial)
