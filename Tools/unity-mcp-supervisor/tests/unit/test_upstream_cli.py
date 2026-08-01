from __future__ import annotations

import json
import os
import subprocess
from pathlib import Path

from unity_mcp_supervisor import upstream_cli
from unity_mcp_supervisor.service_state import Settings


def test_entry_path_uses_current_environment_scripts(
    monkeypatch, tmp_path: Path
) -> None:
    scripts = tmp_path / "tool" / ("Scripts" if os.name == "nt" else "bin")
    scripts.mkdir(parents=True)
    suffix = ".exe" if os.name == "nt" else ""
    entry = scripts / f"mcp-for-unity{suffix}"
    entry.touch()
    monkeypatch.setattr(upstream_cli.sysconfig, "get_path", lambda _name: str(scripts))
    monkeypatch.setattr(
        upstream_cli.sys,
        "executable",
        str(tmp_path / "framework" / f"python{suffix}"),
    )
    monkeypatch.setattr(upstream_cli.shutil, "which", lambda _name: None)

    assert upstream_cli._entry_path("mcp-for-unity") == entry


def test_passthrough_injects_exact_endpoint_hash_and_json(
    monkeypatch, tmp_path: Path
) -> None:
    capture = tmp_path / "capture.json"
    script = Path(__file__).parents[1] / "fixtures" / "fake_upstream_cli.py"
    monkeypatch.setenv("UMCP_TEST_MODE", "1")
    monkeypatch.setenv("UMCP_TEST_CLI_SCRIPT", str(script))
    monkeypatch.setenv("UMCP_TEST_CLI_CAPTURE", str(capture))
    original_run = upstream_cli.subprocess.run
    run_kwargs = {}

    def capture_run(*args, **kwargs):
        run_kwargs.update(kwargs)
        return original_run(*args, **kwargs)

    monkeypatch.setattr(upstream_cli.subprocess, "run", capture_run)
    settings = Settings.load(tmp_path / "state", "http://127.0.0.1:18080")
    result = upstream_cli.run_upstream_cli(settings, "abcdef0123456789", ("status",))
    captured = json.loads(capture.read_text(encoding="utf-8"))
    assert result["exit_code"] == 0
    assert captured["instance"] == "abcdef0123456789"
    assert captured["port"] == "18080"
    assert captured["format"] == "json"
    assert captured["args"][-1] == "status"
    if os.name == "nt":
        assert run_kwargs["creationflags"] == subprocess.CREATE_NO_WINDOW
