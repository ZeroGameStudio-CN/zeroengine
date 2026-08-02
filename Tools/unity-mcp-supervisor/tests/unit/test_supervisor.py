from __future__ import annotations

import os
from pathlib import Path
from types import SimpleNamespace

import pytest

from unity_mcp_supervisor import supervisor
from unity_mcp_supervisor.rest_client import EndpointKind, EndpointProbe
from unity_mcp_supervisor.service_state import ServiceRecord, Settings
from unity_mcp_supervisor.supervisor import ServiceManager


def test_wait_ready_never_returns_transient_starting_state(
    monkeypatch, tmp_path: Path
) -> None:
    settings = Settings(state_dir=tmp_path, service_start_timeout_seconds=1.0)
    manager = ServiceManager(settings)
    reads = 0

    def read_service() -> ServiceRecord:
        nonlocal reads
        reads += 1
        return ServiceRecord(
            status="starting" if reads <= 2 else "healthy-owned",
            owner="owned",
            supervisor_pid=123,
            supervisor_token="owner",
            server_pid=456,
            server_token="server",
            endpoint=settings.endpoint,
        )

    monkeypatch.setattr(manager.store, "read_service", read_service)
    monkeypatch.setattr(manager, "_daemon_active", lambda _state: True)
    monkeypatch.setattr(
        manager.client,
        "classify",
        lambda: EndpointProbe(EndpointKind.COMPATIBLE, {"status": "healthy"}),
    )

    result = manager._wait_ready("owner")
    assert result["status"] == "healthy-owned"


def test_healthy_ensure_fast_path_takes_no_lifecycle_lock_or_network_probe(
    monkeypatch, tmp_path: Path
) -> None:
    settings = Settings(state_dir=tmp_path)
    manager = ServiceManager(settings)
    state = ServiceRecord(
        status="healthy-owned",
        owner="owned",
        supervisor_pid=123,
        supervisor_token="owner",
        server_pid=456,
        server_token="server",
        endpoint=settings.endpoint,
        server_version="10.1.0",
    )
    monkeypatch.setattr(manager.store, "read_service", lambda: state)
    monkeypatch.setattr(manager, "_daemon_active", lambda _state: True)

    def forbidden(*_args, **_kwargs):
        raise AssertionError("healthy ensure must use the lock-free state fast path")

    monkeypatch.setattr(supervisor, "service_lock", forbidden)
    monkeypatch.setattr(manager.client, "classify", forbidden)

    result = manager.ensure()

    assert result["status"] == "healthy-owned"
    assert result["supervisor_pid"] == 123


def test_editor_prefs_guard_runs_only_for_a_compatible_endpoint(
    monkeypatch, tmp_path: Path
) -> None:
    monkeypatch.setenv("UMCP_TEST_MODE", "0")
    enforced: list[str] = []
    monkeypatch.setattr(
        supervisor,
        "enforce_editor_prefs",
        lambda endpoint: enforced.append(endpoint) or False,
    )
    monkeypatch.setattr(
        supervisor,
        "clear_editor_server_ownership",
        lambda: (_ for _ in ()).throw(
            AssertionError("external-compatible ownership must be preserved")
        ),
    )

    foreign = supervisor.Supervisor(Settings(state_dir=tmp_path / "foreign"), "foreign")

    def classify_foreign() -> EndpointProbe:
        foreign._stopping = True
        return EndpointProbe(EndpointKind.FOREIGN, message="foreign")

    monkeypatch.setattr(foreign.client, "classify", classify_foreign)
    foreign._loop()
    assert enforced == []

    compatible = supervisor.Supervisor(
        Settings(state_dir=tmp_path / "compatible"), "compatible"
    )

    def classify_compatible() -> EndpointProbe:
        compatible._stopping = True
        return EndpointProbe(EndpointKind.COMPATIBLE, {"status": "healthy"})

    monkeypatch.setattr(compatible.client, "classify", classify_compatible)
    compatible._loop()
    assert enforced == [compatible.settings.endpoint]


def test_owned_prefs_guard_clears_stale_unity_server_ownership(
    monkeypatch, tmp_path: Path
) -> None:
    monkeypatch.setenv("UMCP_TEST_MODE", "0")
    cleared: list[bool] = []
    enforced: list[str] = []
    owned = supervisor.Supervisor(Settings(state_dir=tmp_path), "owned")

    class Child:
        pid = 456

        @staticmethod
        def poll():
            return None

    owned.child = Child()
    monkeypatch.setattr(owned, "_read_owned_server_pid", lambda: 789)

    def healthy_then_stop() -> None:
        owned._stopping = True

    monkeypatch.setattr(owned.client, "health", healthy_then_stop)
    monkeypatch.setattr(
        supervisor,
        "clear_editor_server_ownership",
        lambda: cleared.append(True) or True,
    )
    monkeypatch.setattr(
        supervisor,
        "enforce_editor_prefs",
        lambda endpoint: enforced.append(endpoint) or False,
    )

    owned._loop()

    assert cleared == [True]
    assert enforced == [owned.settings.endpoint]


def test_owned_worker_pid_requires_matching_token_and_pidfile(
    monkeypatch, tmp_path: Path
) -> None:
    owned = supervisor.Supervisor(Settings(state_dir=tmp_path), "owner")
    owned.server_token = "expected-token"

    class Process:
        @staticmethod
        def cmdline():
            return [
                "mcp-for-unity.exe",
                "--transport",
                "http",
                "--http-host",
                "127.0.0.1",
                "--http-port",
                "8080",
                "--unity-instance-token",
                "expected-token",
                "--pidfile",
                str(owned.paths.server_pid),
                "--project-scoped-tools",
            ]

    monkeypatch.setattr(supervisor.psutil, "Process", lambda _pid: Process())

    assert owned._validate_owned_server_pid(456) is True
    owned.server_token = "different-token"
    assert owned._validate_owned_server_pid(456) is False


def test_previous_owned_orphan_is_adopted_without_restart(
    monkeypatch, tmp_path: Path
) -> None:
    token = "a" * 32
    owned = supervisor.Supervisor(Settings(state_dir=tmp_path), "new-owner")
    owned.previous_record = ServiceRecord(
        status="healthy-owned",
        owner="owned",
        supervisor_pid=123,
        supervisor_token="old-owner",
        server_pid=456,
        server_token=token,
        server_created_at=123.5,
        endpoint=owned.settings.endpoint,
        server_version="10.1.0",
    )
    owned.paths.server_pid.write_text("456\n", encoding="ascii")

    class Process:
        @staticmethod
        def cmdline():
            return [
                "mcp-for-unity.exe",
                "--transport",
                "http",
                "--http-host",
                "127.0.0.1",
                "--http-port",
                "8080",
                "--unity-instance-token",
                token,
                "--pidfile",
                str(owned.paths.server_pid),
                "--project-scoped-tools",
            ]

        @staticmethod
        def create_time():
            return 123.5

    listener = SimpleNamespace(
        pid=456,
        status=supervisor.psutil.CONN_LISTEN,
        laddr=SimpleNamespace(ip="127.0.0.1", port=8080),
    )
    monkeypatch.setattr(supervisor, "process_alive", lambda _pid: False)
    monkeypatch.setattr(supervisor.psutil, "Process", lambda _pid: Process())
    monkeypatch.setattr(
        supervisor.psutil, "net_connections", lambda **_kwargs: [listener]
    )

    assert owned._try_adopt_owned_orphan() is True
    assert owned.child is None
    assert owned.server_pid == 456
    assert owned.server_token == token
    assert owned.server_created_at == 123.5
    assert owned.adopted is True


@pytest.mark.parametrize(
    "broken_proof",
    ("token", "pidfile", "port", "listener", "created-at", "version"),
)
def test_orphan_adoption_rejects_incomplete_ownership_proof(
    monkeypatch, tmp_path: Path, broken_proof: str
) -> None:
    token = "b" * 32
    owned = supervisor.Supervisor(Settings(state_dir=tmp_path), "new-owner")
    owned.previous_record = ServiceRecord(
        status="healthy-owned",
        owner="owned",
        supervisor_pid=123,
        server_pid=456,
        server_token="invalid" if broken_proof == "token" else token,
        server_created_at=100.0,
        endpoint=owned.settings.endpoint,
        server_version="0.0.0" if broken_proof == "version" else "10.1.0",
    )
    owned.paths.server_pid.write_text("456\n", encoding="ascii")

    class Process:
        @staticmethod
        def cmdline():
            return [
                "mcp-for-unity.exe",
                "--transport",
                "http",
                "--http-host",
                "127.0.0.1",
                "--http-port",
                "8081" if broken_proof == "port" else "8080",
                "--unity-instance-token",
                token,
                "--pidfile",
                str(tmp_path / "wrong.pid")
                if broken_proof == "pidfile"
                else str(owned.paths.server_pid),
                "--project-scoped-tools",
            ]

        @staticmethod
        def create_time():
            return 101.0 if broken_proof == "created-at" else 100.0

    listener = SimpleNamespace(
        pid=999 if broken_proof == "listener" else 456,
        status=supervisor.psutil.CONN_LISTEN,
        laddr=SimpleNamespace(ip="127.0.0.1", port=8080),
    )
    monkeypatch.setattr(supervisor, "process_alive", lambda _pid: False)
    monkeypatch.setattr(supervisor.psutil, "Process", lambda _pid: Process())
    monkeypatch.setattr(
        supervisor.psutil, "net_connections", lambda **_kwargs: [listener]
    )

    assert owned._try_adopt_owned_orphan() is False
    assert owned.server_pid is None
    assert owned.adopted is False


def test_known_worker_loss_restarts_without_waiting_for_health_failures(
    monkeypatch, tmp_path: Path
) -> None:
    owned = supervisor.Supervisor(Settings(state_dir=tmp_path), "owner")
    events: list[tuple[str, float]] = []

    class Child:
        pid = 123

        @staticmethod
        def poll():
            return None

    owned.child = Child()
    owned.server_pid = 456
    owned.server_token = "token"
    monkeypatch.setattr(owned, "_read_owned_server_pid", lambda: None)

    def stop(reason: str, *, launcher_wait_seconds: float = 2.0) -> None:
        events.append((reason, launcher_wait_seconds))
        owned.child = None
        owned.server_pid = None
        owned.server_token = None
        owned._stopping = True

    monkeypatch.setattr(owned, "_stop_owned_child", stop)

    owned._loop()

    assert events == [("process exit", 0)]
    assert owned.restart_count == 1


@pytest.mark.skipif(os.name != "nt", reason="Windows hidden process contract")
def test_daemon_spawn_uses_no_console_window(monkeypatch, tmp_path: Path) -> None:
    manager = ServiceManager(Settings(state_dir=tmp_path))
    captured: dict = {}
    executable = tmp_path / "python.exe"
    windowless_executable = tmp_path / "pythonw.exe"
    windowless_executable.touch()

    monkeypatch.setattr(supervisor.sys, "executable", str(executable))
    monkeypatch.setattr(
        supervisor.subprocess,
        "Popen",
        lambda command, **kwargs: captured.update(command=command, **kwargs),
    )
    manager._spawn_daemon("token")

    expected_flags = (
        supervisor.subprocess.CREATE_NEW_PROCESS_GROUP
        | supervisor.subprocess.DETACHED_PROCESS
        | supervisor.subprocess.CREATE_NO_WINDOW
        | supervisor.subprocess.CREATE_BREAKAWAY_FROM_JOB
    )
    assert captured["creationflags"] == expected_flags
    assert captured["command"][0] == str(windowless_executable)
    assert captured["stdin"] is supervisor.subprocess.DEVNULL
    assert captured["stdout"] is supervisor.subprocess.DEVNULL
    assert captured["stderr"] is supervisor.subprocess.DEVNULL
    assert captured["env"]["NO_PROXY"] == "127.0.0.1,localhost"
    assert [key for key in captured["env"] if key.lower() == "no_proxy"] == ["NO_PROXY"]


@pytest.mark.skipif(os.name != "nt", reason="Windows hidden process contract")
def test_owned_server_spawn_uses_no_console_window(monkeypatch, tmp_path: Path) -> None:
    owned = supervisor.Supervisor(Settings(state_dir=tmp_path), "owner")
    captured: dict = {}

    class Child:
        pid = 1234

        @staticmethod
        def poll():
            return None

    monkeypatch.setattr(
        supervisor, "server_command", lambda _settings, _token: ["server"]
    )
    monkeypatch.setattr(
        supervisor.subprocess,
        "Popen",
        lambda command, **kwargs: captured.update(command=command, **kwargs) or Child(),
    )
    owned._start_owned_child()

    assert captured["creationflags"] == supervisor.subprocess.CREATE_NO_WINDOW
    assert captured["stdin"] is supervisor.subprocess.DEVNULL
    assert captured["stdout"] is supervisor.subprocess.DEVNULL
    assert captured["stderr"] is supervisor.subprocess.DEVNULL
