from __future__ import annotations

import sys
from pathlib import Path

import pytest

from unity_mcp_supervisor import startup
from unity_mcp_supervisor.errors import ServiceError
from unity_mcp_supervisor.service_state import Settings


def test_startup_enable_disable_round_trip(monkeypatch, tmp_path: Path) -> None:
    path = tmp_path / "startup.vbs"
    settings = Settings(state_dir=tmp_path / "state")
    monkeypatch.setattr(startup, "startup_file", lambda _settings: path)

    assert startup.enable_startup(settings) == path
    content = path.read_text(encoding="utf-8")
    assert startup.MARKER in content
    if sys.platform == "win32":
        assert "WScript.Shell" in content
        assert ", 0, False" in content
    assert startup.disable_startup(settings) == path
    assert not path.exists()
    assert startup.disable_startup(settings) == path


def test_startup_enable_never_overwrites_unknown_file(
    monkeypatch, tmp_path: Path
) -> None:
    path = tmp_path / "startup.vbs"
    path.write_text("user-owned\n", encoding="utf-8")
    settings = Settings(state_dir=tmp_path / "state")
    monkeypatch.setattr(startup, "startup_file", lambda _settings: path)

    with pytest.raises(ServiceError, match="Refusing to overwrite"):
        startup.enable_startup(settings)
    assert path.read_text(encoding="utf-8") == "user-owned\n"


@pytest.mark.skipif(sys.platform != "win32", reason="Windows startup contract")
def test_windows_startup_file_is_a_hidden_script(monkeypatch, tmp_path: Path) -> None:
    monkeypatch.setenv("APPDATA", str(tmp_path))
    path = startup.startup_file(Settings(state_dir=tmp_path / "state"))
    assert path.suffix == ".vbs"
