from __future__ import annotations

import logging
import logging.handlers
import os
import subprocess
import sys
import time
import uuid
from pathlib import Path
from typing import Any
from urllib.parse import urlparse

import psutil
from filelock import FileLock, Timeout

from .editor_bootstrap import clear_editor_server_ownership, enforce_editor_prefs
from .errors import EditorControlUnsupportedError, ForeignListenerError, ServiceError
from .locking import lifecycle_gate, live_operation_owners, service_lock
from .rest_client import EndpointKind, RestClient
from .service_state import ServiceRecord, Settings, StateStore, process_alive
from .upstream_cli import server_command, upstream_version

RESTART_BACKOFF_SECONDS = (0.0, 1.0, 3.0, 5.0, 10.0, 30.0)


def _safe_logger(name: str, path: Path) -> logging.Logger:
    logger = logging.getLogger(name)
    logger.setLevel(logging.INFO)
    logger.propagate = False
    if not logger.handlers:
        path.parent.mkdir(parents=True, exist_ok=True)
        handler = logging.handlers.RotatingFileHandler(
            path,
            maxBytes=10 * 1024 * 1024,
            backupCount=5,
            encoding="utf-8",
        )
        handler.setFormatter(logging.Formatter("%(asctime)s %(levelname)s %(message)s"))
        logger.addHandler(handler)
    return logger


class Supervisor:
    def __init__(self, settings: Settings, supervisor_token: str) -> None:
        self.settings = settings
        self.paths = settings.paths
        self.paths.ensure()
        self.store = StateStore(self.paths)
        self.previous_record = self.store.read_service()
        self.client = RestClient(settings.endpoint, settings.command_timeout_seconds)
        self.supervisor_token = supervisor_token
        suffix = str(abs(hash(str(self.paths.root))))
        self.logger = _safe_logger(
            f"umcp.supervisor.{suffix}", self.paths.supervisor_log
        )
        self.server_logger = _safe_logger(
            f"umcp.server.{suffix}", self.paths.server_log
        )
        self.child: subprocess.Popen | None = None
        self.server_pid: int | None = None
        self.server_token: str | None = None
        self.server_created_at: float | None = None
        self.adopted = False
        self.restart_count = 0
        self.started_at = time.time()
        self._stopping = False

    def run(self) -> int:
        daemon_lock = FileLock(self.paths.daemon_lock)
        try:
            daemon_lock.acquire(timeout=0)
        except Timeout:
            return 0
        try:
            self.logger.info(
                "Supervisor started pid=%s endpoint=%s",
                os.getpid(),
                self.settings.endpoint,
            )
            return self._loop()
        finally:
            try:
                try:
                    self._stop_owned_child("supervisor exit")
                finally:
                    self._write_record(
                        "stopped", owner="none", message="Supervisor stopped."
                    )
            finally:
                daemon_lock.release()
                self.logger.info("Supervisor stopped")

    def _loop(self) -> int:
        next_health = 0.0
        next_start = 0.0
        next_prefs_enforcement = 0.0
        prefs_guard_enabled = os.environ.get("UMCP_TEST_MODE") != "1"
        prefs_authoritative = False
        health_failures = 0
        startup_deadline = 0.0

        while not self._stopping:
            action = self._consume_control()
            if action == "stop":
                self._stopping = True
                break
            if action == "restart":
                if self._has_owned_server():
                    self._stop_owned_child("requested restart")
                    self.restart_count += 1
                    next_start = time.monotonic()
                else:
                    self._write_record(
                        "external-compatible",
                        owner="external",
                        message="Restart refused because the compatible server is externally owned.",
                    )

            now = time.monotonic()
            if self._has_owned_server():
                return_code = self.child.poll() if self.child is not None else None
                worker_pid = self._read_owned_server_pid()
                if worker_pid is not None:
                    self.server_pid = worker_pid
                    if self.server_created_at is None:
                        self.server_created_at = self._process_created_at(worker_pid)
                worker_lost = self.server_pid is not None and worker_pid is None
                launcher_lost = (
                    self.child is not None
                    and return_code is not None
                    and self.server_pid is None
                )
                if worker_lost or launcher_lost:
                    self.server_logger.warning(
                        "Owned server exited worker_pid=%s launcher_code=%s",
                        self.server_pid,
                        return_code,
                    )
                    self._stop_owned_child("process exit", launcher_wait_seconds=0)
                    self.restart_count += 1
                    delay = RESTART_BACKOFF_SECONDS[
                        min(self.restart_count - 1, len(RESTART_BACKOFF_SECONDS) - 1)
                    ]
                    next_start = now + delay
                    self._write_record(
                        "restarting",
                        owner="owned",
                        message=f"Server exited; retrying in {delay:g}s.",
                    )
                elif now >= next_health:
                    next_health = now + self.settings.health_interval_seconds
                    try:
                        self.client.health()
                        worker_pid = self._read_owned_server_pid()
                        if worker_pid is None:
                            raise ServiceError(
                                "Healthy endpoint does not match the owned server PID/token handshake."
                            )
                        if self.server_pid != worker_pid:
                            self.server_logger.info(
                                "Owned server worker ready pid=%s", worker_pid
                            )
                        self.server_pid = worker_pid
                        if self.server_created_at is None:
                            self.server_created_at = self._process_created_at(
                                worker_pid
                            )
                        prefs_authoritative = True
                        health_failures = 0
                        self._write_record(
                            "healthy-owned",
                            owner="owned",
                            message="Owned Unity MCP server is healthy.",
                        )
                    except ServiceError:
                        if now < startup_deadline:
                            self._write_record(
                                "starting",
                                owner="owned",
                                message="Waiting for owned server health.",
                            )
                        else:
                            health_failures += 1
                            self.server_logger.warning(
                                "Owned server health failure %s/%s",
                                health_failures,
                                self.settings.health_failure_limit,
                            )
                            if health_failures >= self.settings.health_failure_limit:
                                deferred = True
                                try:
                                    with lifecycle_gate(self.paths, 0):
                                        if not live_operation_owners(self.paths):
                                            deferred = False
                                            self._stop_owned_child(
                                                "health failure limit"
                                            )
                                except ServiceError:
                                    pass
                                if deferred:
                                    health_failures = self.settings.health_failure_limit
                                    self._write_record(
                                        "degraded-owned",
                                        owner="owned",
                                        message="Health is degraded; restart deferred for an active live operation.",
                                    )
                                else:
                                    self.restart_count += 1
                                    delay = RESTART_BACKOFF_SECONDS[
                                        min(
                                            self.restart_count - 1,
                                            len(RESTART_BACKOFF_SECONDS) - 1,
                                        )
                                    ]
                                    next_start = now + delay
                                    health_failures = 0
                                    self._write_record(
                                        "restarting",
                                        owner="owned",
                                        message=f"Health failed; retrying in {delay:g}s.",
                                    )
            elif now >= next_start:
                probe = self.client.classify()
                if probe.kind == EndpointKind.COMPATIBLE:
                    prefs_authoritative = True
                    if self._try_adopt_owned_orphan():
                        health_failures = 0
                        next_health = now + self.settings.health_interval_seconds
                        self._write_record(
                            "healthy-owned",
                            owner="owned",
                            message="Previously owned Unity MCP server was adopted without restart.",
                        )
                    else:
                        self._write_record(
                            "external-compatible",
                            owner="external",
                            message="Compatible external Unity MCP server is in use; lifecycle is not owned.",
                        )
                    next_start = now + self.settings.health_interval_seconds
                elif probe.kind == EndpointKind.FOREIGN:
                    prefs_authoritative = False
                    self._write_record(
                        "foreign-listener", owner="none", message=probe.message
                    )
                    next_start = now + self.settings.health_interval_seconds
                else:
                    try:
                        self._start_owned_child()
                    except ServiceError as exc:
                        self.restart_count += 1
                        delay = RESTART_BACKOFF_SECONDS[
                            min(
                                self.restart_count - 1, len(RESTART_BACKOFF_SECONDS) - 1
                            )
                        ]
                        next_start = now + delay
                        self._write_record(
                            "restarting",
                            owner="owned",
                            message=f"Server launch failed; retrying in {delay:g}s.",
                        )
                        self.server_logger.error("Owned server launch failed: %s", exc)
                    else:
                        startup_deadline = (
                            now + self.settings.service_start_timeout_seconds
                        )
                        next_health = now
                        health_failures = 0

            if (
                prefs_guard_enabled
                and prefs_authoritative
                and now >= next_prefs_enforcement
            ):
                next_prefs_enforcement = now + 0.5
                try:
                    if self._has_owned_server() and clear_editor_server_ownership():
                        self.logger.warning(
                            "Cleared stale Unity-owned server handshake"
                        )
                    if enforce_editor_prefs(self.settings.endpoint):
                        self.logger.warning(
                            "Restored authoritative Unity MCP EditorPrefs endpoint"
                        )
                except EditorControlUnsupportedError as exc:
                    self.logger.warning("Cannot enforce Unity MCP EditorPrefs: %s", exc)

            time.sleep(0.2)
        return 0

    def _has_owned_server(self) -> bool:
        return self.child is not None or (
            self.server_pid is not None and self.server_token is not None
        )

    def _try_adopt_owned_orphan(self) -> bool:
        previous = self.previous_record
        if not previous or previous.owner != "owned":
            return False
        if previous.status not in {"healthy-owned", "degraded-owned"}:
            return False
        if (
            previous.endpoint != self.settings.endpoint
            or previous.server_version != upstream_version()
            or not previous.server_pid
            or not previous.server_token
            or previous.server_created_at is None
            or process_alive(previous.supervisor_pid)
        ):
            return False
        try:
            pid = int(self.paths.server_pid.read_text(encoding="ascii").strip())
        except (FileNotFoundError, OSError, ValueError):
            return False
        if pid != previous.server_pid:
            return False
        if not self._valid_server_token(previous.server_token):
            return False
        if not self._process_matches_owned_server(pid, previous.server_token):
            return False
        created_at = self._process_created_at(pid)
        if created_at is None or abs(created_at - previous.server_created_at) > 0.01:
            return False
        if not self._pid_owns_endpoint_listener(pid):
            return False
        self.child = None
        self.server_pid = pid
        self.server_token = previous.server_token
        self.server_created_at = created_at
        self.adopted = True
        self.previous_record = None
        self.server_logger.info("Adopted previously owned server worker pid=%s", pid)
        return True

    @staticmethod
    def _valid_server_token(token: str) -> bool:
        try:
            return len(token) == 32 and uuid.UUID(token).hex == token
        except (ValueError, AttributeError):
            return False

    @staticmethod
    def _argument_value(args: list[str], name: str) -> str | None:
        if args.count(name) != 1:
            return None
        index = args.index(name)
        return args[index + 1] if index + 1 < len(args) else None

    def _process_matches_owned_server(self, pid: int, token: str) -> bool:
        try:
            args = psutil.Process(pid).cmdline()
        except (
            psutil.NoSuchProcess,
            psutil.AccessDenied,
            psutil.ZombieProcess,
            OSError,
        ):
            return False
        parsed = urlparse(self.settings.endpoint)
        pidfile = self._argument_value(args, "--pidfile")
        identity_matches = any(
            Path(argument).name.casefold() in {"mcp-for-unity", "mcp-for-unity.exe"}
            for argument in args
        )
        if os.environ.get("UMCP_TEST_MODE") == "1" and os.environ.get(
            "UMCP_TEST_SERVER_SCRIPT"
        ):
            expected_scripts = {
                os.path.normcase(os.path.realpath(value))
                for variable in (
                    "UMCP_TEST_SERVER_SCRIPT",
                    "UMCP_TEST_WORKER_SCRIPT",
                )
                if (value := os.environ.get(variable))
            }
            identity_matches = any(
                os.path.normcase(os.path.realpath(argument)) in expected_scripts
                for argument in args
            )
        return bool(
            identity_matches
            and self._argument_value(args, "--transport") == "http"
            and self._argument_value(args, "--http-host") == "127.0.0.1"
            and self._argument_value(args, "--http-port") == str(parsed.port)
            and self._argument_value(args, "--unity-instance-token") == token
            and pidfile
            and os.path.normcase(os.path.realpath(pidfile))
            == os.path.normcase(os.path.realpath(self.paths.server_pid))
            and args.count("--project-scoped-tools") == 1
        )

    @staticmethod
    def _process_created_at(pid: int) -> float | None:
        try:
            return psutil.Process(pid).create_time()
        except (
            psutil.NoSuchProcess,
            psutil.AccessDenied,
            psutil.ZombieProcess,
            OSError,
        ):
            return None

    def _pid_owns_endpoint_listener(self, pid: int) -> bool:
        parsed = urlparse(self.settings.endpoint)
        try:
            connections = psutil.net_connections(kind="tcp")
        except (psutil.AccessDenied, OSError):
            return False
        return any(
            connection.pid == pid
            and connection.status == psutil.CONN_LISTEN
            and connection.laddr
            and connection.laddr.ip == "127.0.0.1"
            and connection.laddr.port == parsed.port
            for connection in connections
        )

    def _start_owned_child(self) -> None:
        if self._has_owned_server():
            return
        try:
            self.paths.server_pid.unlink(missing_ok=True)
        except OSError as exc:
            raise ServiceError("Cannot clear the stale owned server PID file.") from exc
        self.server_token = uuid.uuid4().hex
        self.server_pid = None
        self.server_created_at = None
        self.adopted = False
        command = server_command(self.settings, self.server_token)
        env = os.environ.copy()
        for key in [key for key in env if key.lower() == "no_proxy"]:
            env.pop(key)
        env["NO_PROXY"] = "127.0.0.1,localhost"
        env["UNITY_MCP_TELEMETRY_ENABLED"] = "false"
        env["PYTHONUNBUFFERED"] = "1"
        kwargs: dict[str, Any] = {
            "cwd": str(self.paths.root),
            "env": env,
            "stdin": subprocess.DEVNULL,
            "stdout": subprocess.DEVNULL,
            "stderr": subprocess.DEVNULL,
            "close_fds": True,
        }
        if os.name == "nt":
            kwargs["creationflags"] = subprocess.CREATE_NO_WINDOW
        self.child = subprocess.Popen(command, **kwargs)
        self.server_logger.info(
            "Owned server started pid=%s version=%s", self.child.pid, upstream_version()
        )
        self._write_record(
            "starting", owner="owned", message="Owned Unity MCP server is starting."
        )

    def _stop_owned_child(
        self, reason: str, *, launcher_wait_seconds: float = 2.0
    ) -> None:
        child = self.child
        if not self._has_owned_server():
            return
        self.server_logger.info(
            "Stopping owned server pid=%s launcher_pid=%s reason=%s",
            self.server_pid,
            child.pid if child is not None else None,
            reason,
        )
        worker_pid = self.server_pid or self._read_owned_server_pid()
        if worker_pid is not None and self._validate_owned_server_pid(worker_pid):
            try:
                worker = psutil.Process(worker_pid)
                worker.terminate()
                try:
                    worker.wait(timeout=5)
                except psutil.TimeoutExpired:
                    worker.kill()
                    worker.wait(timeout=5)
            except (psutil.NoSuchProcess, psutil.ZombieProcess):
                pass
            except (psutil.AccessDenied, OSError) as exc:
                self.server_logger.warning(
                    "Cannot terminate owned server worker pid=%s: %s",
                    worker_pid,
                    exc,
                )
        if child is not None and child.poll() is None and launcher_wait_seconds > 0:
            try:
                child.wait(timeout=launcher_wait_seconds)
            except subprocess.TimeoutExpired:
                pass
            except OSError:
                pass
        if child is not None and child.poll() is None:
            try:
                child.terminate()
                child.wait(timeout=5)
            except subprocess.TimeoutExpired:
                child.kill()
                child.wait(timeout=5)
            except OSError:
                pass
        try:
            self.paths.server_pid.unlink(missing_ok=True)
        except OSError:
            pass
        self.child = None
        self.server_pid = None
        self.server_token = None
        self.server_created_at = None
        self.adopted = False

    def _read_owned_server_pid(self) -> int | None:
        try:
            pid = int(self.paths.server_pid.read_text(encoding="ascii").strip())
        except (FileNotFoundError, OSError, ValueError):
            return None
        return pid if self._validate_owned_server_pid(pid) else None

    def _validate_owned_server_pid(self, pid: int) -> bool:
        if pid <= 0 or not self.server_token:
            return False
        return self._process_matches_owned_server(pid, self.server_token)

    def _consume_control(self) -> str | None:
        request = self.store.read_control()
        if not request:
            return None
        if request.get("supervisor_token") != self.supervisor_token:
            self.store.clear_control()
            return None
        action = str(request.get("action") or "")
        self.store.clear_control()
        return action if action in {"stop", "restart"} else None

    def _write_record(self, status: str, *, owner: str, message: str) -> None:
        record = ServiceRecord(
            status=status,
            owner=owner,
            supervisor_pid=os.getpid(),
            supervisor_token=self.supervisor_token,
            server_pid=self.server_pid
            if self.server_pid is not None and process_alive(self.server_pid)
            else None,
            server_token=self.server_token,
            server_created_at=self.server_created_at,
            adopted=self.adopted,
            endpoint=self.settings.endpoint,
            server_version=upstream_version(),
            restart_count=self.restart_count,
            started_at=self.started_at,
            message=message,
            supervisor_log=str(self.paths.supervisor_log),
            server_log=str(self.paths.server_log),
        )
        self.store.write_service(record)


class ServiceManager:
    def __init__(self, settings: Settings) -> None:
        self.settings = settings
        self.paths = settings.paths
        self.paths.ensure()
        self.store = StateStore(self.paths)
        self.client = RestClient(settings.endpoint, settings.command_timeout_seconds)

    def ensure(self) -> dict[str, Any]:
        state = self.store.read_service()
        if state and self._daemon_active(state):
            return self._ready_existing(state)

        with service_lock(self.paths):
            state = self.store.read_service()
            if state and self._daemon_active(state):
                return self._ready_existing(state)

            probe = self.client.classify()
            if probe.kind == EndpointKind.FOREIGN:
                raise ForeignListenerError(
                    "Configured endpoint is occupied by a non-Unity-MCP listener.",
                    details={
                        "endpoint": self.settings.endpoint,
                        "probe": probe.message,
                    },
                )

            token = uuid.uuid4().hex
            self._spawn_daemon(token)
            return self._wait_ready(token)

    start = ensure

    def _ready_existing(self, state: ServiceRecord) -> dict[str, Any]:
        if state.endpoint != self.settings.endpoint:
            raise ServiceError(
                f"A supervisor is already running on {state.endpoint}; stop it before changing endpoint."
            )
        if state.status in {"healthy-owned", "external-compatible"}:
            return {
                "status": state.status,
                "owner": state.owner,
                "healthy": True,
                "supervisor_alive": True,
                "supervisor_pid": state.supervisor_pid,
                "server_pid": state.server_pid,
                "adopted": state.adopted,
                "endpoint": state.endpoint,
                "server_version": state.server_version,
                "restart_count": state.restart_count,
                "message": state.message,
                "supervisor_log": str(self.paths.supervisor_log),
                "server_log": str(self.paths.server_log),
            }
        return self._wait_ready(state.supervisor_token)

    def status(self) -> dict[str, Any]:
        state = self.store.read_service()
        supervisor_alive = bool(state and self._daemon_active(state))
        probe = self.client.classify()
        if supervisor_alive and state:
            if state.endpoint != self.settings.endpoint:
                status = "service-endpoint-mismatch"
                owner = state.owner
            else:
                status = state.status
                owner = state.owner
        elif probe.kind == EndpointKind.COMPATIBLE:
            status = "external-compatible"
            owner = "external"
        elif probe.kind == EndpointKind.FOREIGN:
            status = "foreign-listener"
            owner = "none"
        else:
            status = "server-down"
            owner = "none"
        return {
            "status": status,
            "owner": owner,
            "healthy": probe.kind == EndpointKind.COMPATIBLE,
            "supervisor_alive": supervisor_alive,
            "supervisor_pid": state.supervisor_pid if state else None,
            "server_pid": state.server_pid if state else None,
            "adopted": state.adopted if state else False,
            "endpoint": self.settings.endpoint,
            "server_version": state.server_version if state else upstream_version(),
            "restart_count": state.restart_count if state else 0,
            "message": state.message if supervisor_alive and state else probe.message,
            "supervisor_log": str(self.paths.supervisor_log),
            "server_log": str(self.paths.server_log),
        }

    def stop(self) -> dict[str, Any]:
        with service_lock(self.paths):
            state = self.store.read_service()
            if not state or not self._daemon_active(state):
                probe = self.client.classify()
                if probe.kind == EndpointKind.COMPATIBLE:
                    raise ServiceError(
                        "Compatible server is external; there is no owned supervisor to stop."
                    )
                return self.status()
            with lifecycle_gate(self.paths):
                self._refuse_while_live_operations()
                self._request_control(state, "stop")
                deadline = time.monotonic() + 15.0
                while self._daemon_active(state) and time.monotonic() < deadline:
                    time.sleep(0.1)
                if self._daemon_active(state):
                    raise ServiceError(
                        "Supervisor did not stop within 15 seconds; no process was force-killed."
                    )
                return self.status()

    def restart(self) -> dict[str, Any]:
        with service_lock(self.paths):
            state = self.store.read_service()
            if not state or not self._daemon_active(state):
                probe = self.client.classify()
                if probe.kind == EndpointKind.COMPATIBLE:
                    raise ServiceError(
                        "Compatible server is external and cannot be restarted by this tool."
                    )
                token = uuid.uuid4().hex
                self._spawn_daemon(token)
                return self._wait_ready(token)
            if state.owner != "owned":
                raise ServiceError(
                    "Compatible server is external and cannot be restarted by this tool."
                )
            with lifecycle_gate(self.paths):
                self._refuse_while_live_operations()
                previous_token = state.server_token
                self._request_control(state, "restart")
                deadline = (
                    time.monotonic() + self.settings.service_start_timeout_seconds
                )
                while time.monotonic() < deadline:
                    current = self.store.read_service()
                    probe = self.client.classify()
                    if (
                        current
                        and current.supervisor_token == state.supervisor_token
                        and current.server_token
                        and current.server_token != previous_token
                        and current.status == "healthy-owned"
                        and probe.kind == EndpointKind.COMPATIBLE
                    ):
                        return self.status()
                    time.sleep(0.2)
                raise ServiceError(
                    "Owned server did not restart within the configured startup budget."
                )

    def _request_control(self, state: ServiceRecord, action: str) -> None:
        if not state.supervisor_token:
            raise ServiceError(
                "Supervisor ownership token is missing; refusing lifecycle mutation."
            )
        self.store.write_control(
            token=state.supervisor_token, action=action, request_id=uuid.uuid4().hex
        )

    def _daemon_active(self, state: ServiceRecord) -> bool:
        if not process_alive(state.supervisor_pid):
            return False
        lock = FileLock(self.paths.daemon_lock)
        try:
            lock.acquire(timeout=0)
        except Timeout:
            return True
        lock.release()
        return False

    def _refuse_while_live_operations(self) -> None:
        owners = live_operation_owners(self.paths)
        if owners:
            raise ServiceError(
                "Lifecycle mutation refused while live operations are active.",
                details={"owners": owners},
            )

    def _spawn_daemon(self, token: str) -> None:
        command = [
            sys.executable,
            "-m",
            "unity_mcp_supervisor.cli",
            "--state-dir",
            str(self.settings.state_dir),
            "--endpoint",
            self.settings.endpoint,
            "_daemon",
            "--token",
            token,
        ]
        env = os.environ.copy()
        for key in [key for key in env if key.lower() == "no_proxy"]:
            env.pop(key)
        env["NO_PROXY"] = "127.0.0.1,localhost"
        env["PYTHONUNBUFFERED"] = "1"
        kwargs: dict[str, Any] = {
            "cwd": str(self.settings.state_dir),
            "env": env,
            "stdin": subprocess.DEVNULL,
            "stdout": subprocess.DEVNULL,
            "stderr": subprocess.DEVNULL,
            "close_fds": True,
        }
        if os.name == "nt":
            kwargs["creationflags"] = (
                subprocess.CREATE_NEW_PROCESS_GROUP
                | subprocess.DETACHED_PROCESS
                | subprocess.CREATE_NO_WINDOW
                | subprocess.CREATE_BREAKAWAY_FROM_JOB
            )
        else:
            kwargs["start_new_session"] = True
        subprocess.Popen(command, **kwargs)

    def _wait_ready(self, token: str | None) -> dict[str, Any]:
        deadline = time.monotonic() + self.settings.service_start_timeout_seconds
        while time.monotonic() < deadline:
            state = self.store.read_service()
            probe = self.client.classify()
            if state and state.supervisor_token == token and self._daemon_active(state):
                if state.status == "foreign-listener":
                    raise ForeignListenerError(
                        state.message or "Configured endpoint is occupied."
                    )
                if probe.kind == EndpointKind.COMPATIBLE and state.status in {
                    "healthy-owned",
                    "external-compatible",
                }:
                    return self.status()
            time.sleep(0.2)
        raise ServiceError(
            "Unity MCP supervisor did not become ready within the startup budget."
        )


def run_daemon(settings: Settings, token: str) -> int:
    return Supervisor(settings, token).run()
