from __future__ import annotations

import json
import re
import time
import uuid
from dataclasses import dataclass
from pathlib import Path
from typing import Any

import psutil

from .errors import EditorBootstrapError, EditorControlUnavailableError
from .project_resolver import (
    canonical_project_root,
    unity_project_hash_candidate,
)
from .service_state import Settings, _atomic_write, _unlink_with_retry

CONTROL_PACKAGE_NAME = "com.zerogamestudio.unity-mcp-control"
CONTROL_SCHEMA_VERSION = 1
COMPANION_VERSION = "0.3.0"
MAX_CONTROL_FILE_BYTES = 32 * 1024
_HEX_ID = re.compile(r"^[0-9a-f]{32}$")


@dataclass(frozen=True)
class EditorControlResult:
    editor_pid: int
    companion_version: str
    upstream_version: str
    channel: str = "project-mailbox"

    def to_dict(self) -> dict[str, Any]:
        return {
            "editor_pid": self.editor_pid,
            "companion_version": self.companion_version,
            "upstream_version": self.upstream_version,
            "channel": self.channel,
        }


@dataclass(frozen=True)
class _Discovery:
    project_hash: str
    project_root: str
    editor_pid: int
    process_started_at_ms: int
    session_id: str
    token: str
    companion_version: str
    upstream_version: str


def companion_package_path() -> Path:
    return Path(__file__).resolve().parent / "unity_package"


def control_package_declared(project_root: Path) -> bool:
    manifest = project_root / "Packages" / "manifest.json"
    try:
        value = json.loads(manifest.read_text(encoding="utf-8-sig"))
    except (OSError, ValueError, TypeError):
        return False
    dependencies = value.get("dependencies") if isinstance(value, dict) else None
    return isinstance(dependencies, dict) and CONTROL_PACKAGE_NAME in dependencies


def _read_json(path: Path) -> dict[str, Any] | None:
    try:
        if path.stat().st_size > MAX_CONTROL_FILE_BYTES:
            return None
        value = json.loads(path.read_text(encoding="utf-8-sig"))
    except (FileNotFoundError, OSError, ValueError, TypeError):
        return None
    return value if isinstance(value, dict) else None


def _validate_discovery(
    value: dict[str, Any], project_root: Path, expected_editor_pid: int
) -> _Discovery | None:
    expected_hash = unity_project_hash_candidate(project_root)
    try:
        if int(value["schema_version"]) != CONTROL_SCHEMA_VERSION:
            return None
        project_hash = str(value["project_hash"])
        reported_root = str(value["project_root"])
        editor_pid = int(value["editor_pid"])
        started_at_ms = int(value["process_started_at_ms"])
        session_id = str(value["session_id"])
        token = str(value["token"])
        companion_version = str(value["companion_version"])
        upstream_version = str(value.get("upstream_version") or "unknown")
    except (KeyError, TypeError, ValueError):
        return None
    if (
        project_hash != expected_hash
        or canonical_project_root(reported_root) != canonical_project_root(project_root)
        or editor_pid != expected_editor_pid
        or not _HEX_ID.fullmatch(session_id)
        or not _HEX_ID.fullmatch(token)
        or companion_version != COMPANION_VERSION
    ):
        return None
    try:
        actual_started_at_ms = round(psutil.Process(editor_pid).create_time() * 1000)
    except (psutil.Error, OSError, ValueError):
        return None
    if abs(actual_started_at_ms - started_at_ms) > 5000:
        return None
    return _Discovery(
        project_hash=project_hash,
        project_root=reported_root,
        editor_pid=editor_pid,
        process_started_at_ms=started_at_ms,
        session_id=session_id,
        token=token,
        companion_version=companion_version,
        upstream_version=upstream_version,
    )


def _discovery_path(project_root: Path, settings: Settings) -> Path:
    project_hash = unity_project_hash_candidate(project_root)
    return settings.paths.editor_discovery / f"{project_hash}.json"


def _try_discovery(
    project_root: Path, settings: Settings, expected_editor_pid: int
) -> _Discovery | None:
    return _validate_discovery(
        _read_json(_discovery_path(project_root, settings)) or {},
        project_root,
        expected_editor_pid,
    )


def _response_matches(
    value: dict[str, Any], discovery: _Discovery, request_id: str
) -> bool:
    try:
        return (
            int(value["schema_version"]) == CONTROL_SCHEMA_VERSION
            and str(value["request_id"]) == request_id
            and str(value["session_id"]) == discovery.session_id
            and str(value["project_hash"]) == discovery.project_hash
            and int(value["editor_pid"]) == discovery.editor_pid
        )
    except (KeyError, TypeError, ValueError):
        return False


def request_editor_connect(
    project_root: Path,
    settings: Settings,
    expected_editor_pid: int,
    timeout_seconds: float,
) -> EditorControlResult:
    if timeout_seconds <= 0:
        raise EditorControlUnavailableError("Editor control budget has expired.")
    if not control_package_declared(project_root):
        raise EditorControlUnavailableError(
            "The project does not declare the Unity MCP control companion package.",
            details={
                "package": CONTROL_PACKAGE_NAME,
                "hint": "Install the pinned companion package, let Unity compile it, then rerun the command.",
            },
        )

    settings.paths.ensure()
    project_hash = unity_project_hash_candidate(project_root)
    deadline = time.monotonic() + min(timeout_seconds, 60.0)
    saw_discovery = False
    while time.monotonic() < deadline:
        discovery = _try_discovery(project_root, settings, expected_editor_pid)
        if discovery is None:
            time.sleep(0.1)
            continue
        saw_discovery = True

        request_id = uuid.uuid4().hex
        request_path = (
            settings.paths.editor_requests / project_hash / f"{request_id}.json"
        )
        response_path = (
            settings.paths.editor_responses / project_hash / f"{request_id}.json"
        )
        expires_at_ms = round(
            (time.time() + max(1.0, deadline - time.monotonic())) * 1000
        )
        request = {
            "schema_version": CONTROL_SCHEMA_VERSION,
            "request_id": request_id,
            "session_id": discovery.session_id,
            "token": discovery.token,
            "command": "connect",
            "project_hash": discovery.project_hash,
            "project_root": str(project_root.resolve()),
            "editor_pid": discovery.editor_pid,
            "endpoint": settings.endpoint,
            "expires_at_ms": expires_at_ms,
        }
        _atomic_write(
            request_path,
            json.dumps(request, ensure_ascii=False, sort_keys=True) + "\n",
        )
        session_changed = False
        try:
            while time.monotonic() < deadline:
                response = _read_json(response_path)
                if response is not None:
                    if not _response_matches(response, discovery, request_id):
                        raise EditorControlUnavailableError(
                            "The companion returned a mismatched control response.",
                            details={"project_hash": project_hash},
                        )
                    if not bool(response.get("ok")):
                        code = str(response.get("code") or "editor_control_failed")
                        message = str(
                            response.get("message") or "Editor control failed."
                        )
                        error_type = (
                            EditorControlUnavailableError
                            if code == "upstream_api_unavailable"
                            else EditorBootstrapError
                        )
                        raise error_type(
                            message,
                            details={
                                "project_hash": project_hash,
                                "editor_pid": discovery.editor_pid,
                                "control_code": code,
                            },
                        )
                    return EditorControlResult(
                        editor_pid=discovery.editor_pid,
                        companion_version=str(
                            response.get("companion_version")
                            or discovery.companion_version
                        ),
                        upstream_version=str(
                            response.get("upstream_version")
                            or discovery.upstream_version
                        ),
                    )
                current = _try_discovery(project_root, settings, expected_editor_pid)
                if current is None or current.session_id != discovery.session_id:
                    session_changed = True
                    break
                time.sleep(0.05)
        finally:
            _unlink_with_retry(request_path)
            _unlink_with_retry(response_path)
        if session_changed:
            time.sleep(0.05)

    message = (
        "Timed out waiting for the Unity companion Connect response."
        if saw_discovery
        else "The open Unity Editor did not publish a valid companion control session."
    )
    raise EditorControlUnavailableError(
        message,
        details={
            "project_hash": project_hash,
            "editor_pid": expected_editor_pid,
            "hint": "Wait for Unity compilation to finish and check Console for companion package errors.",
        },
    )


def editor_control_diagnostics(
    project_root: Path, settings: Settings, expected_editor_pid: int | None
) -> dict[str, Any]:
    declared = control_package_declared(project_root)
    discovery = (
        _try_discovery(project_root, settings, expected_editor_pid)
        if expected_editor_pid is not None
        else None
    )
    return {
        "package": CONTROL_PACKAGE_NAME,
        "package_declared": declared,
        "available": discovery is not None,
        "channel": "project-mailbox" if discovery is not None else None,
        "editor_pid": discovery.editor_pid if discovery is not None else None,
        "companion_version": (
            discovery.companion_version if discovery is not None else None
        ),
        "upstream_version": discovery.upstream_version
        if discovery is not None
        else None,
    }
