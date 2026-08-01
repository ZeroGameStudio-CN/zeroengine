from __future__ import annotations

import json
import os
import sys
from pathlib import Path

import pytest

from tests.helpers import create_unity_project
from unity_mcp_supervisor import editor_bootstrap
from unity_mcp_supervisor.editor_bootstrap import (
    BootstrapEvidence,
    EditorInstance,
    _binary_pref,
    bootstrap_diagnostics,
    clear_editor_server_ownership,
    configure_editor_prefs,
    enforce_editor_prefs,
    ensure_project_connection,
    read_editor_instance,
    restart_editor,
)
from unity_mcp_supervisor.editor_control import EditorControlResult
from unity_mcp_supervisor.errors import (
    EditorControlUnavailableError,
    EditorNotOpenError,
    EditorRestartRequiredError,
)
from unity_mcp_supervisor.project_resolver import unity_project_hash_candidate
from unity_mcp_supervisor.service_state import Settings


def _write_editor_instance(project: Path, executable: str = sys.executable) -> None:
    library = project / "Library"
    library.mkdir()
    (library / "EditorInstance.json").write_text(
        json.dumps(
            {
                "process_id": os.getpid(),
                "version": "2022.3.62f3",
                "app_path": executable,
            }
        ),
        encoding="utf-8",
    )


def test_binary_editor_pref_matches_unity_string_storage() -> None:
    assert _binary_pref("local") == b"local\0"
    assert _binary_pref("http://127.0.0.1:8080").endswith(b"\0")


def test_configure_windows_editor_prefs_writes_only_four_values(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    written: dict[str, tuple[int, object]] = {}

    class Key:
        def __enter__(self):
            return self

        def __exit__(self, *_args):
            return False

    class FakeWinreg:
        HKEY_CURRENT_USER = object()
        KEY_SET_VALUE = 1
        REG_DWORD = 4
        REG_BINARY = 3

        @staticmethod
        def CreateKeyEx(*_args):
            return Key()

        @staticmethod
        def SetValueEx(_key, name, _reserved, kind, value):
            written[name] = (kind, value)

    monkeypatch.setattr(editor_bootstrap.sys, "platform", "win32")
    monkeypatch.setitem(sys.modules, "winreg", FakeWinreg)
    configure_editor_prefs("http://127.0.0.1:18080")
    assert len(written) == 4
    assert written["MCPForUnity.UseHttpTransport_h3850471145"] == (4, 1)
    assert written["MCPForUnity.HttpTransportScope_h2378119776"] == (
        3,
        b"local\0",
    )
    assert written["MCPForUnity.HttpUrl_h1802602754"] == (
        3,
        b"http://127.0.0.1:18080\0",
    )
    assert written["MCPForUnity.AutoStartOnLoad_h2539145689"] == (4, 1)


def test_clear_editor_server_ownership_deletes_only_handshake_values(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    deleted: list[str] = []

    class Key:
        def __enter__(self):
            return self

        def __exit__(self, *_args):
            return False

    class FakeWinreg:
        HKEY_CURRENT_USER = object()
        KEY_SET_VALUE = 1

        @staticmethod
        def CreateKeyEx(*_args):
            return Key()

        @staticmethod
        def DeleteValue(_key, name):
            deleted.append(name)

    monkeypatch.setattr(editor_bootstrap.sys, "platform", "win32")
    monkeypatch.setitem(sys.modules, "winreg", FakeWinreg)

    assert clear_editor_server_ownership() is True
    assert deleted == [
        "MCPForUnity.LocalHttpServer.LastPidFilePath_h2108398323",
        "MCPForUnity.LocalHttpServer.LastInstanceToken_h3764283543",
    ]


def test_editor_prefs_enforcement_writes_only_after_drift(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    configured = False
    writes: list[str] = []

    def read_prefs():
        return {
            "use_http": True,
            "scope": "local",
            "endpoint": "http://127.0.0.1:8080"
            if configured
            else "http://127.0.0.1:8088",
            "auto_start": True,
        }

    def configure(endpoint: str):
        nonlocal configured
        configured = True
        writes.append(endpoint)

    monkeypatch.setattr(editor_bootstrap.sys, "platform", "win32")
    monkeypatch.setattr(editor_bootstrap, "_read_windows_prefs", read_prefs)
    monkeypatch.setattr(editor_bootstrap, "configure_editor_prefs", configure)

    assert enforce_editor_prefs("http://127.0.0.1:8080") is True
    assert enforce_editor_prefs("http://127.0.0.1:8080") is False
    assert writes == ["http://127.0.0.1:8080"]


def test_editor_instance_requires_live_matching_process(
    monkeypatch: pytest.MonkeyPatch, tmp_path: Path
) -> None:
    project = create_unity_project(tmp_path / "Project")
    _write_editor_instance(project, "C:/Unity/Editor/Unity.exe")
    monkeypatch.setattr(editor_bootstrap, "process_alive", lambda _pid: True)
    monkeypatch.setattr(
        editor_bootstrap, "_process_executable", lambda _pid: "C:/Other/other.exe"
    )
    with pytest.raises(EditorNotOpenError):
        read_editor_instance(project)


def test_existing_session_never_restarts_editor(tmp_path: Path) -> None:
    project = create_unity_project(tmp_path / "Project")
    project_hash = unity_project_hash_candidate(project)

    class Client:
        def instances(self):
            return [
                {
                    "hash": project_hash,
                    "project": "Project",
                    "unity_version": "2022.3.62f3",
                    "connected_at": "now",
                }
            ]

        def command(self, _kind, _params, _hash, **_kwargs):
            return {"data": {"projectRoot": str(project)}}

    resolved, evidence = ensure_project_connection(
        project,
        Settings.load(tmp_path / "state", "http://127.0.0.1:18080"),
        Client(),
        1.0,
    )
    assert resolved.project_hash == project_hash
    assert evidence == BootstrapEvidence(
        mode="existing-session",
        editor_pid=0,
        previous_editor_pid=None,
        unity_version="2022.3.62f3",
        prefs_configured=False,
        restarted=False,
    )


def test_unconnected_editor_without_companion_fails_closed(
    monkeypatch: pytest.MonkeyPatch, tmp_path: Path
) -> None:
    project = create_unity_project(tmp_path / "Project")
    _write_editor_instance(project)
    monkeypatch.setattr(editor_bootstrap, "_read_windows_prefs", lambda: None)
    monkeypatch.setattr(
        editor_bootstrap, "_process_executable", lambda _pid: sys.executable
    )

    class Client:
        def instances(self):
            return []

    with pytest.raises(EditorControlUnavailableError):
        ensure_project_connection(
            project,
            Settings.load(tmp_path / "state", "http://127.0.0.1:18080"),
            Client(),
            1.0,
        )


def test_open_editor_uses_companion_hot_connect(
    monkeypatch: pytest.MonkeyPatch, tmp_path: Path
) -> None:
    project = create_unity_project(tmp_path / "Project")
    _write_editor_instance(project)
    project_hash = unity_project_hash_candidate(project)
    monkeypatch.setattr(
        editor_bootstrap, "_process_executable", lambda _pid: sys.executable
    )

    class Client:
        reads = 0

        def instances(self):
            self.reads += 1
            if self.reads == 1:
                return []
            return [
                {
                    "hash": project_hash,
                    "project": "Project",
                    "unity_version": "2022.3.62f3",
                    "connected_at": "reconnected",
                }
            ]

        def command(self, _kind, _params, _hash, **_kwargs):
            return {"data": {"projectRoot": str(project)}}

    monkeypatch.setattr(
        "unity_mcp_supervisor.editor_control.request_editor_connect",
        lambda _root, _settings, pid, _budget: EditorControlResult(
            pid, "0.3.0", "10.1.0"
        ),
    )
    resolved, evidence = ensure_project_connection(
        project,
        Settings(
            state_dir=tmp_path / "state",
            endpoint="http://127.0.0.1:18080",
            reconnect_timeout_seconds=1.0,
        ),
        Client(),
        1.0,
    )

    assert resolved.project_hash == project_hash
    assert evidence.mode == "companion-hot-connect"
    assert evidence.editor_pid == os.getpid()
    assert evidence.restarted is False
    assert evidence.control_channel == "project-mailbox"
    assert evidence.companion_version == "0.3.0"


def test_explicit_restart_waits_for_exact_project(
    monkeypatch: pytest.MonkeyPatch, tmp_path: Path
) -> None:
    project = create_unity_project(tmp_path / "Project")
    _write_editor_instance(project)
    project_hash = unity_project_hash_candidate(project)

    class Client:
        connected = False

        def instances(self):
            if not self.connected:
                return []
            return [
                {
                    "hash": project_hash,
                    "project": "Project",
                    "unity_version": "2022.3.62f3",
                    "connected_at": "after-restart",
                }
            ]

        def command(self, _kind, _params, _hash, **_kwargs):
            return {"data": {"projectRoot": str(project)}}

    client = Client()

    def restart(_root, _settings, _deadline):
        client.connected = True
        return EditorInstance(2222, "2022.3.62f3", sys.executable)

    monkeypatch.setattr(editor_bootstrap, "restart_editor", restart)
    resolved, evidence = ensure_project_connection(
        project,
        Settings.load(tmp_path / "state", "http://127.0.0.1:18080"),
        client,
        5.0,
        allow_editor_restart=True,
    )
    assert resolved.project_hash == project_hash
    assert evidence.restarted is True
    assert evidence.previous_editor_pid == os.getpid()
    assert evidence.editor_pid == 2222


def test_explicit_restart_relaunches_an_already_connected_editor(
    monkeypatch: pytest.MonkeyPatch, tmp_path: Path
) -> None:
    project = create_unity_project(tmp_path / "Project")
    _write_editor_instance(project)
    project_hash = unity_project_hash_candidate(project)
    restart_calls = 0

    class Client:
        def instances(self):
            return [
                {
                    "hash": project_hash,
                    "project": "Project",
                    "unity_version": "2022.3.62f3",
                    "connected_at": "connected",
                }
            ]

        def command(self, _kind, _params, _hash, **_kwargs):
            return {"data": {"projectRoot": str(project)}}

    def restart(_root, _settings, _deadline):
        nonlocal restart_calls
        restart_calls += 1
        return EditorInstance(2222, "2022.3.62f3", sys.executable)

    monkeypatch.setattr(editor_bootstrap, "restart_editor", restart)
    resolved, evidence = ensure_project_connection(
        project,
        Settings.load(tmp_path / "state", "http://127.0.0.1:18080"),
        Client(),
        5.0,
        allow_editor_restart=True,
    )

    assert resolved.project_hash == project_hash
    assert restart_calls == 1
    assert evidence.mode == "configured-editor-restart"
    assert evidence.previous_editor_pid == os.getpid()
    assert evidence.editor_pid == 2222


def test_restart_writes_prefs_only_after_normal_editor_exit(
    monkeypatch: pytest.MonkeyPatch, tmp_path: Path
) -> None:
    project = create_unity_project(tmp_path / "Project")
    _write_editor_instance(project)
    events: list[str] = []

    monkeypatch.setattr(editor_bootstrap, "process_alive", lambda _pid: True)
    monkeypatch.setattr(
        editor_bootstrap, "_process_executable", lambda _pid: sys.executable
    )
    monkeypatch.setattr(
        editor_bootstrap,
        "_request_windows_close",
        lambda _pid, _project_name: events.append("close"),
    )
    monkeypatch.setattr(
        editor_bootstrap,
        "_wait_for_exit",
        lambda _pid, _deadline: events.append("exited"),
    )
    monkeypatch.setattr(
        editor_bootstrap,
        "configure_editor_prefs",
        lambda _endpoint: events.append("prefs"),
    )
    launched = object()
    monkeypatch.setattr(
        editor_bootstrap,
        "_launch_editor",
        lambda _root, _record: events.append("launch") or launched,
    )
    monkeypatch.setattr(
        editor_bootstrap,
        "_wait_for_new_editor",
        lambda _root, _pid, value, _deadline: (
            events.append("ready")
            or EditorInstance(2222, "2022.3.62f3", sys.executable)
            if value is launched
            else None
        ),
    )

    current = restart_editor(
        project,
        Settings.load(tmp_path / "state", "http://127.0.0.1:18080"),
        editor_bootstrap.time.monotonic() + 5.0,
    )

    assert current.pid == 2222
    assert events == ["close", "exited", "prefs", "launch", "ready"]


def test_restart_does_not_write_prefs_when_normal_exit_is_blocked(
    monkeypatch: pytest.MonkeyPatch, tmp_path: Path
) -> None:
    project = create_unity_project(tmp_path / "Project")
    _write_editor_instance(project)
    prefs_written = False

    monkeypatch.setattr(editor_bootstrap, "process_alive", lambda _pid: True)
    monkeypatch.setattr(
        editor_bootstrap, "_process_executable", lambda _pid: sys.executable
    )
    monkeypatch.setattr(
        editor_bootstrap, "_request_windows_close", lambda _pid, _name: None
    )

    def blocked(_pid, _deadline):
        raise EditorRestartRequiredError("blocked")

    def write_prefs(_endpoint):
        nonlocal prefs_written
        prefs_written = True

    monkeypatch.setattr(editor_bootstrap, "_wait_for_exit", blocked)
    monkeypatch.setattr(editor_bootstrap, "configure_editor_prefs", write_prefs)

    with pytest.raises(EditorRestartRequiredError):
        restart_editor(
            project,
            Settings.load(tmp_path / "state", "http://127.0.0.1:18080"),
            editor_bootstrap.time.monotonic() + 5.0,
        )
    assert prefs_written is False


def test_relaunch_safe_mode_prompt_fails_immediately(
    monkeypatch: pytest.MonkeyPatch, tmp_path: Path
) -> None:
    project = create_unity_project(tmp_path / "Project")

    class Launched:
        pid = 2222

        @staticmethod
        def poll():
            return None

    monkeypatch.setattr(editor_bootstrap.sys, "platform", "win32")
    monkeypatch.setattr(
        editor_bootstrap,
        "read_editor_instance",
        lambda _root: (_ for _ in ()).throw(EditorNotOpenError("not ready")),
    )
    monkeypatch.setattr(
        editor_bootstrap,
        "_visible_top_level_windows",
        lambda _pid: [(123, "Enter Safe Mode?")],
    )

    with pytest.raises(EditorRestartRequiredError, match="Safe Mode") as error:
        editor_bootstrap._wait_for_new_editor(
            project,
            1111,
            Launched(),
            editor_bootstrap.time.monotonic() + 5.0,
        )

    assert error.value.details["editor_pid"] == 2222
    assert error.value.details["window_titles"] == ["Enter Safe Mode?"]


def test_restart_without_editor_record_does_not_touch_project(tmp_path: Path) -> None:
    project = create_unity_project(tmp_path / "Project")
    original = (project / "Packages" / "manifest.json").read_bytes()

    class Client:
        def instances(self):
            return []

    with pytest.raises(EditorNotOpenError):
        ensure_project_connection(
            project,
            Settings.load(tmp_path / "state", "http://127.0.0.1:18080"),
            Client(),
            1.0,
            allow_editor_restart=True,
        )
    assert (project / "Packages" / "manifest.json").read_bytes() == original


def test_restart_with_stale_editor_record_does_not_launch_or_write_prefs(
    monkeypatch: pytest.MonkeyPatch, tmp_path: Path
) -> None:
    project = create_unity_project(tmp_path / "Project")
    _write_editor_instance(project)
    prefs_written = False
    editor_launched = False

    monkeypatch.setattr(editor_bootstrap, "process_alive", lambda _pid: False)

    def write_prefs(_endpoint):
        nonlocal prefs_written
        prefs_written = True

    def launch(_root, _record):
        nonlocal editor_launched
        editor_launched = True

    monkeypatch.setattr(editor_bootstrap, "configure_editor_prefs", write_prefs)
    monkeypatch.setattr(editor_bootstrap, "_launch_editor", launch)

    with pytest.raises(EditorNotOpenError):
        restart_editor(
            project,
            Settings.load(tmp_path / "state", "http://127.0.0.1:18080"),
            editor_bootstrap.time.monotonic() + 5.0,
        )

    assert prefs_written is False
    assert editor_launched is False


@pytest.mark.skipif(os.name != "nt", reason="Windows hidden process contract")
def test_editor_relaunch_uses_no_console_window(
    monkeypatch: pytest.MonkeyPatch, tmp_path: Path
) -> None:
    project = create_unity_project(tmp_path / "Project")
    captured: dict = {}
    launched = object()

    def popen(command, **kwargs):
        captured["command"] = command
        captured.update(kwargs)
        return launched

    monkeypatch.setattr(editor_bootstrap.subprocess, "Popen", popen)
    result = editor_bootstrap._launch_editor(
        project,
        EditorInstance(1234, "2022.3.62f3", sys.executable),
    )

    expected_flags = (
        editor_bootstrap.subprocess.CREATE_NEW_PROCESS_GROUP
        | editor_bootstrap.subprocess.DETACHED_PROCESS
        | editor_bootstrap.subprocess.CREATE_NO_WINDOW
        | editor_bootstrap.subprocess.CREATE_BREAKAWAY_FROM_JOB
    )
    assert result is launched
    assert captured["creationflags"] == expected_flags
    assert captured["stdin"] is editor_bootstrap.subprocess.DEVNULL
    assert captured["stdout"] is editor_bootstrap.subprocess.DEVNULL
    assert captured["stderr"] is editor_bootstrap.subprocess.DEVNULL


def test_diagnostics_reports_expected_endpoint_match(
    monkeypatch: pytest.MonkeyPatch, tmp_path: Path
) -> None:
    project = create_unity_project(tmp_path / "Project")
    monkeypatch.setattr(editor_bootstrap.sys, "platform", "win32")
    monkeypatch.setattr(
        editor_bootstrap,
        "_read_windows_prefs",
        lambda: {
            "use_http": True,
            "scope": "local",
            "endpoint": "http://127.0.0.1:8080",
            "auto_start": True,
        },
    )
    value = bootstrap_diagnostics(project, "http://127.0.0.1:8080")
    assert value["prefs_configured"] is True
    assert value["control_supported"] is True
