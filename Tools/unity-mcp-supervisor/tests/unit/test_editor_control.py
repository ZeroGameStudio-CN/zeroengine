from __future__ import annotations

import json
import os
import threading
import time
from pathlib import Path

import psutil
import pytest

from tests.helpers import create_unity_project
from unity_mcp_supervisor import __version__
from unity_mcp_supervisor.editor_control import (
    CONTROL_PACKAGE_NAME,
    EditorControlResult,
    companion_package_path,
    control_package_declared,
    editor_control_diagnostics,
    request_editor_connect,
)
from unity_mcp_supervisor.errors import EditorControlUnavailableError
from unity_mcp_supervisor.project_resolver import unity_project_hash_candidate
from unity_mcp_supervisor.service_state import Settings, _atomic_write


def _declare_companion(project: Path) -> None:
    manifest_path = project / "Packages" / "manifest.json"
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    manifest["dependencies"][CONTROL_PACKAGE_NAME] = "file:control-package"
    manifest_path.write_text(json.dumps(manifest), encoding="utf-8")


def _write_discovery(
    project: Path,
    settings: Settings,
    *,
    project_root: Path | None = None,
    process_started_at_ms: int | None = None,
    session_id: str = "1" * 32,
    token: str = "2" * 32,
) -> dict:
    project_hash = unity_project_hash_candidate(project)
    value = {
        "schema_version": 1,
        "project_hash": project_hash,
        "project_root": str(project_root or project),
        "editor_pid": os.getpid(),
        "process_started_at_ms": process_started_at_ms
        if process_started_at_ms is not None
        else round(psutil.Process().create_time() * 1000),
        "session_id": session_id,
        "token": token,
        "companion_version": "0.3.0",
        "upstream_version": "10.1.0",
    }
    _atomic_write(
        settings.paths.editor_discovery / f"{project_hash}.json",
        json.dumps(value),
    )
    return value


def test_companion_package_is_included_without_upstream_compile_reference() -> None:
    package = companion_package_path()
    package_manifest = json.loads(
        (package / "package.json").read_text(encoding="utf-8")
    )
    assert package_manifest["name"] == CONTROL_PACKAGE_NAME
    assert package_manifest["version"] == __version__
    asmdef = json.loads(
        (package / "Editor" / "ZeroGameStudio.UnityMcpControl.Editor.asmdef").read_text(
            encoding="utf-8"
        )
    )
    source = (package / "Editor" / "EditorControlMailbox.cs").read_text(
        encoding="utf-8"
    )
    assert asmdef["references"] == []
    assert "using MCPForUnity" not in source
    assert f'CompanionVersion = "{__version__}"' in source
    assert "AssetDatabase.IsAssetImportWorkerProcess()" in source
    assert "if (!OwnsDiscovery())" in source
    assert 'request.command != "connect" && request.command != "status"' in source


def test_missing_companion_declaration_fails_without_waiting(tmp_path: Path) -> None:
    project = create_unity_project(tmp_path / "Project")
    settings = Settings.load(tmp_path / "state", "http://127.0.0.1:18080")
    started = time.monotonic()
    with pytest.raises(EditorControlUnavailableError, match="does not declare"):
        request_editor_connect(project, settings, os.getpid(), 30.0)
    assert time.monotonic() - started < 1.0
    assert control_package_declared(project) is False


def test_connect_uses_exact_project_mailbox_and_validated_response(
    tmp_path: Path,
) -> None:
    project = create_unity_project(tmp_path / "Project")
    _declare_companion(project)
    settings = Settings.load(tmp_path / "state", "http://127.0.0.1:18080")
    discovery = _write_discovery(project, settings)
    captured: dict = {}

    def respond() -> None:
        request_dir = settings.paths.editor_requests / discovery["project_hash"]
        deadline = time.monotonic() + 3.0
        request_path = None
        while time.monotonic() < deadline and request_path is None:
            request_path = next(iter(request_dir.glob("*.json")), None)
            time.sleep(0.01)
        assert request_path is not None
        request = json.loads(request_path.read_text(encoding="utf-8"))
        captured.update(request)
        response = {
            "schema_version": 1,
            "request_id": request["request_id"],
            "session_id": discovery["session_id"],
            "project_hash": discovery["project_hash"],
            "editor_pid": os.getpid(),
            "ok": True,
            "code": "ok",
            "message": "connected",
            "companion_version": "0.3.0",
            "upstream_version": "10.1.0",
        }
        _atomic_write(
            settings.paths.editor_responses
            / discovery["project_hash"]
            / f"{request['request_id']}.json",
            json.dumps(response),
        )

    worker = threading.Thread(target=respond)
    worker.start()
    result = request_editor_connect(project, settings, os.getpid(), 3.0)
    worker.join(timeout=3)

    assert result == EditorControlResult(os.getpid(), "0.3.0", "10.1.0")
    assert captured["project_hash"] == unity_project_hash_candidate(project)
    assert captured["project_root"] == str(project.resolve())
    assert captured["endpoint"] == "http://127.0.0.1:18080"
    assert captured["command"] == "connect"
    assert captured["token"] == discovery["token"]
    assert (
        list(
            (settings.paths.editor_requests / discovery["project_hash"]).glob("*.json")
        )
        == []
    )
    assert (
        list(
            (settings.paths.editor_responses / discovery["project_hash"]).glob("*.json")
        )
        == []
    )


def test_connect_retries_after_companion_domain_reload(tmp_path: Path) -> None:
    project = create_unity_project(tmp_path / "Project")
    _declare_companion(project)
    settings = Settings.load(tmp_path / "state", "http://127.0.0.1:18080")
    first = _write_discovery(project, settings)
    seen_request_ids: list[str] = []

    def reload_then_respond() -> None:
        request_dir = settings.paths.editor_requests / first["project_hash"]
        deadline = time.monotonic() + 3.0
        while time.monotonic() < deadline and not seen_request_ids:
            request = next(iter(request_dir.glob("*.json")), None)
            if request is not None:
                seen_request_ids.append(request.stem)
                break
            time.sleep(0.01)
        second = _write_discovery(
            project,
            settings,
            session_id="3" * 32,
            token="4" * 32,
        )
        second_request = None
        while time.monotonic() < deadline and second_request is None:
            second_request = next(
                (
                    path
                    for path in request_dir.glob("*.json")
                    if path.stem not in seen_request_ids
                ),
                None,
            )
            time.sleep(0.01)
        assert second_request is not None
        request_value = json.loads(second_request.read_text(encoding="utf-8"))
        seen_request_ids.append(request_value["request_id"])
        response = {
            "schema_version": 1,
            "request_id": request_value["request_id"],
            "session_id": second["session_id"],
            "project_hash": second["project_hash"],
            "editor_pid": os.getpid(),
            "ok": True,
            "code": "ok",
            "message": "connected after reload",
            "companion_version": "0.3.0",
            "upstream_version": "10.1.0",
        }
        _atomic_write(
            settings.paths.editor_responses
            / second["project_hash"]
            / f"{request_value['request_id']}.json",
            json.dumps(response),
        )

    worker = threading.Thread(target=reload_then_respond)
    worker.start()
    result = request_editor_connect(project, settings, os.getpid(), 3.0)
    worker.join(timeout=3)

    assert result.companion_version == "0.3.0"
    assert len(seen_request_ids) == 2
    assert seen_request_ids[0] != seen_request_ids[1]


@pytest.mark.parametrize(
    ("wrong_root", "started_at_delta"),
    [(True, 0), (False, 60_000)],
)
def test_discovery_must_match_project_path_and_process_start(
    tmp_path: Path, wrong_root: bool, started_at_delta: int
) -> None:
    project = create_unity_project(tmp_path / "Project")
    _declare_companion(project)
    settings = Settings.load(tmp_path / "state", "http://127.0.0.1:18080")
    actual_started = round(psutil.Process().create_time() * 1000)
    _write_discovery(
        project,
        settings,
        project_root=(tmp_path / "Other") if wrong_root else project,
        process_started_at_ms=actual_started + started_at_delta,
    )
    with pytest.raises(EditorControlUnavailableError, match="valid companion"):
        request_editor_connect(project, settings, os.getpid(), 0.15)


def test_diagnostics_never_returns_control_token(tmp_path: Path) -> None:
    project = create_unity_project(tmp_path / "Project")
    _declare_companion(project)
    settings = Settings.load(tmp_path / "state", "http://127.0.0.1:18080")
    _write_discovery(project, settings)
    value = editor_control_diagnostics(project, settings, os.getpid())
    assert value["available"] is True
    assert "token" not in json.dumps(value).lower()
