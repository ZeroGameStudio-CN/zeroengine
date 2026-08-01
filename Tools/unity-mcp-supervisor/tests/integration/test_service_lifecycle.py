from __future__ import annotations

import json
import os
import subprocess
import sys
import threading
import time
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

import httpx
import psutil
import pytest

from tests.helpers import fake_http_server, free_port
from unity_mcp_supervisor.errors import ForeignListenerError, ServiceError
from unity_mcp_supervisor.locking import project_lock
from unity_mcp_supervisor.service_state import Settings, StateStore
from unity_mcp_supervisor.supervisor import ServiceManager, Supervisor

FAKE_SERVER = Path(__file__).parents[1] / "fixtures" / "fake_upstream_server.py"
FAKE_LAUNCHER = Path(__file__).parents[1] / "fixtures" / "fake_upstream_launcher.py"
WINDOWLESS_PROCESS_KWARGS = (
    {"creationflags": subprocess.CREATE_NO_WINDOW} if os.name == "nt" else {}
)


def _test_env(state_dir: Path, count_file: Path) -> dict[str, str]:
    env = os.environ.copy()
    env.update(
        {
            "UMCP_TEST_MODE": "1",
            "UMCP_TEST_SERVER_SCRIPT": str(FAKE_SERVER),
            "UMCP_TEST_WORKER_SCRIPT": str(FAKE_SERVER),
            "UMCP_TEST_START_COUNT_FILE": str(count_file),
            "UMCP_STATE_DIR": str(state_dir),
        }
    )
    return env


def _wait_http(endpoint: str, timeout: float = 10.0) -> None:
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        try:
            with httpx.Client(trust_env=False) as client:
                if client.get(f"{endpoint}/health", timeout=0.5).status_code == 200:
                    return
        except httpx.HTTPError:
            pass
        time.sleep(0.1)
    raise AssertionError(f"Endpoint did not become ready: {endpoint}")


def _wait_for(predicate, timeout: float = 20.0) -> None:
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        if predicate():
            return
        time.sleep(0.02)
    raise AssertionError("Condition did not become true before timeout.")


def _owned_process_ids(token: str, pidfile: Path) -> set[int]:
    expected_pidfile = os.path.normcase(os.path.realpath(pidfile))
    matches: set[int] = set()
    for process in psutil.process_iter(["pid", "cmdline"]):
        try:
            args = process.info["cmdline"] or []
        except (psutil.NoSuchProcess, psutil.AccessDenied, psutil.ZombieProcess):
            continue
        if token not in args or "--pidfile" not in args:
            continue
        index = args.index("--pidfile")
        if index + 1 >= len(args):
            continue
        if os.path.normcase(os.path.realpath(args[index + 1])) == expected_pidfile:
            matches.add(process.info["pid"])
    return matches


@pytest.fixture
def owned_service(monkeypatch, tmp_path: Path):
    state_dir = tmp_path / "state"
    count_file = tmp_path / "starts.txt"
    endpoint = f"http://127.0.0.1:{free_port()}"
    env = _test_env(state_dir, count_file)
    for key, value in env.items():
        if key.startswith("UMCP_"):
            monkeypatch.setenv(key, value)
    settings = Settings.load(state_dir, endpoint)
    manager = ServiceManager(settings)
    try:
        yield manager, settings, count_file, env
    finally:
        try:
            manager.stop()
        except ServiceError:
            pass


def test_service_ensure_is_idempotent_and_restart_changes_only_owned_child(
    owned_service,
) -> None:
    manager, _settings, count_file, _env = owned_service
    first = manager.ensure()
    second = manager.ensure()
    assert first["status"] == "healthy-owned"
    assert second["server_pid"] == first["server_pid"]
    restarted = manager.restart()
    assert restarted["status"] == "healthy-owned"
    assert restarted["server_pid"] != first["server_pid"]
    assert len(count_file.read_text(encoding="ascii").splitlines()) == 2


def test_new_supervisor_adopts_its_proven_orphan_without_server_restart(
    owned_service,
) -> None:
    manager, _settings, count_file, _env = owned_service
    first = manager.ensure()
    assert first["status"] == "healthy-owned"
    assert first["adopted"] is False
    first_server_pid = first["server_pid"]
    first_supervisor_pid = first["supervisor_pid"]
    assert first_server_pid
    assert first_supervisor_pid

    old_supervisor = psutil.Process(first_supervisor_pid)
    old_supervisor.terminate()
    old_supervisor.wait(timeout=10)
    _wait_for(lambda: not psutil.pid_exists(first_supervisor_pid))
    assert psutil.pid_exists(first_server_pid)

    adopted = manager.ensure()
    assert adopted["status"] == "healthy-owned"
    assert adopted["adopted"] is True
    assert adopted["server_pid"] == first_server_pid
    assert len(count_file.read_text(encoding="ascii").splitlines()) == 1


def test_adopted_launcher_tree_fully_exits_on_owned_stop(
    monkeypatch, tmp_path: Path
) -> None:
    state_dir = tmp_path / "state"
    count_file = tmp_path / "starts.txt"
    endpoint = f"http://127.0.0.1:{free_port()}"
    env = _test_env(state_dir, count_file)
    env["UMCP_TEST_SERVER_SCRIPT"] = str(FAKE_LAUNCHER)
    for key, value in env.items():
        if key.startswith("UMCP_"):
            monkeypatch.setenv(key, value)
    settings = Settings.load(state_dir, endpoint)
    manager = ServiceManager(settings)
    store = StateStore(settings.paths)
    try:
        first = manager.ensure()
        record = store.read_service()
        assert record is not None
        assert record.server_token
        token = record.server_token
        assert len(_owned_process_ids(token, settings.paths.server_pid)) >= 2

        old_supervisor = psutil.Process(first["supervisor_pid"])
        old_supervisor.terminate()
        old_supervisor.wait(timeout=10)
        adopted = manager.ensure()
        assert adopted["adopted"] is True

        manager.stop()
        _wait_for(lambda: not _owned_process_ids(token, settings.paths.server_pid))
    finally:
        try:
            manager.stop()
        except ServiceError:
            pass


def test_restart_tracks_and_terminates_the_real_worker_pid(
    monkeypatch, tmp_path: Path
) -> None:
    state_dir = tmp_path / "state"
    count_file = tmp_path / "starts.txt"
    endpoint = f"http://127.0.0.1:{free_port()}"
    env = _test_env(state_dir, count_file)
    env["UMCP_TEST_SERVER_SCRIPT"] = str(FAKE_LAUNCHER)
    for key, value in env.items():
        if key.startswith("UMCP_"):
            monkeypatch.setenv(key, value)
    manager = ServiceManager(Settings.load(state_dir, endpoint))
    try:
        first = manager.ensure()
        first_worker = first["server_pid"]
        assert first_worker
        restarted = manager.restart()
        assert restarted["server_pid"] != first_worker
        assert not psutil.pid_exists(first_worker)
    finally:
        try:
            manager.stop()
        except ServiceError:
            pass


def test_running_supervisor_rejects_endpoint_change(owned_service) -> None:
    manager, settings, _count_file, _env = owned_service
    manager.ensure()
    other_endpoint = f"http://127.0.0.1:{free_port()}"
    mismatched = ServiceManager(Settings.load(settings.state_dir, other_endpoint))
    assert mismatched.status()["status"] == "service-endpoint-mismatch"
    with pytest.raises(ServiceError, match="stop it before changing endpoint"):
        mismatched.ensure()


def test_lifecycle_mutation_is_refused_during_live_operation(owned_service) -> None:
    manager, settings, _count_file, _env = owned_service
    manager.ensure()
    with project_lock(settings.paths, "project", "long-call", 2):
        with pytest.raises(ServiceError, match="live operations are active"):
            manager.restart()
        with pytest.raises(ServiceError, match="live operations are active"):
            manager.stop()
    assert manager.status()["status"] == "healthy-owned"


def test_health_restart_waits_for_live_operation(monkeypatch, tmp_path: Path) -> None:
    state_dir = tmp_path / "state"
    count_file = tmp_path / "starts.txt"
    health_fail_file = tmp_path / "fail-health"
    endpoint = f"http://127.0.0.1:{free_port()}"
    env = _test_env(state_dir, count_file)
    env["UMCP_TEST_HEALTH_FAIL_FILE"] = str(health_fail_file)
    for key, value in env.items():
        if key.startswith("UMCP_"):
            monkeypatch.setenv(key, value)

    settings = Settings(
        state_dir=state_dir,
        endpoint=endpoint,
        service_start_timeout_seconds=1.0,
        health_interval_seconds=0.1,
        health_failure_limit=1,
        command_timeout_seconds=1.0,
    )
    supervisor = Supervisor(settings, "test-supervisor")
    thread = threading.Thread(target=supervisor.run, daemon=True)
    store = StateStore(settings.paths)
    manager = ServiceManager(settings)
    thread.start()
    try:
        _wait_for(
            lambda: (
                (record := store.read_service()) is not None
                and record.status == "healthy-owned"
            )
        )
        first = store.read_service()
        assert first is not None

        health_fail_file.touch()
        recovery = threading.Timer(0.15, health_fail_file.unlink)
        recovery.start()
        assert manager.ensure()["status"] == "healthy-owned"
        recovery.join(timeout=2)
        assert not recovery.is_alive()

        startup_remaining = 1.1 - (time.time() - first.started_at)
        if startup_remaining > 0:
            time.sleep(startup_remaining)
        with project_lock(settings.paths, "project", "long-call", 2):
            health_fail_file.touch()
            _wait_for(
                lambda: (
                    (record := store.read_service()) is not None
                    and record.status == "degraded-owned"
                )
            )
            time.sleep(0.3)
            current = store.read_service()
            assert current is not None
            assert current.server_pid == first.server_pid
            assert len(count_file.read_text(encoding="ascii").splitlines()) == 1

        _wait_for(
            lambda: (
                count_file.exists()
                and len(count_file.read_text(encoding="ascii").splitlines()) >= 2
            )
        )
        health_fail_file.unlink()
        _wait_for(
            lambda: (
                (record := store.read_service()) is not None
                and record.status == "healthy-owned"
                and record.server_token != first.server_token
            )
        )
    finally:
        health_fail_file.unlink(missing_ok=True)
        try:
            manager.stop()
        except ServiceError:
            supervisor._stopping = True
        thread.join(timeout=10)
        assert not thread.is_alive()


def test_four_cli_processes_create_one_supervisor_and_one_server(owned_service) -> None:
    manager, settings, count_file, env = owned_service
    command = [
        sys.executable,
        "-m",
        "unity_mcp_supervisor.cli",
        "--state-dir",
        str(settings.state_dir),
        "--endpoint",
        settings.endpoint,
        "service",
        "ensure",
    ]

    def invoke() -> subprocess.CompletedProcess[str]:
        return subprocess.run(
            command,
            env=env,
            capture_output=True,
            text=True,
            timeout=60,
            check=False,
            **WINDOWLESS_PROCESS_KWARGS,
        )

    with ThreadPoolExecutor(max_workers=4) as pool:
        results = list(pool.map(lambda _index: invoke(), range(4)))
    assert all(result.returncode == 0 for result in results)
    payloads = [json.loads(result.stdout) for result in results]
    assert len({payload["result"]["supervisor_pid"] for payload in payloads}) == 1
    assert len({payload["result"]["server_pid"] for payload in payloads}) == 1
    assert len(count_file.read_text(encoding="ascii").splitlines()) == 1
    assert manager.status()["status"] == "healthy-owned"


def test_compatible_external_server_is_used_but_never_stopped(
    monkeypatch, tmp_path: Path
) -> None:
    state_dir = tmp_path / "state"
    count_file = tmp_path / "starts.txt"
    endpoint = f"http://127.0.0.1:{free_port()}"
    port = endpoint.rsplit(":", 1)[1]
    env = _test_env(state_dir, count_file)
    process = subprocess.Popen(
        [
            sys.executable,
            str(FAKE_SERVER),
            "--http-host",
            "127.0.0.1",
            "--http-port",
            port,
        ],
        env=env,
        stdin=subprocess.DEVNULL,
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
        **WINDOWLESS_PROCESS_KWARGS,
    )
    try:
        _wait_http(endpoint)
        for key, value in env.items():
            if key.startswith("UMCP_"):
                monkeypatch.setenv(key, value)
        manager = ServiceManager(Settings.load(state_dir, endpoint))
        status = manager.ensure()
        assert status["status"] == "external-compatible"
        with pytest.raises(ServiceError, match="external"):
            manager.restart()
        manager.stop()
        assert process.poll() is None
    finally:
        process.terminate()
        process.wait(timeout=10)


def test_foreign_listener_fails_closed_and_stays_alive(tmp_path: Path) -> None:
    with fake_http_server(foreign=True) as endpoint:
        manager = ServiceManager(Settings.load(tmp_path / "state", endpoint))
        with pytest.raises(ForeignListenerError):
            manager.ensure()
        with httpx.Client(trust_env=False) as client:
            assert client.get(f"{endpoint}/health", timeout=2).status_code == 200
