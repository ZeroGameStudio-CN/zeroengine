from __future__ import annotations

import ctypes
import json
import os
import platform
import sys
import tempfile
import time
from dataclasses import asdict, dataclass, field
from pathlib import Path
from typing import Any
from urllib.parse import urlparse

try:
    import tomllib
except ModuleNotFoundError:  # Python 3.10
    import tomli as tomllib

from .errors import UsageError

APP_DIR_NAME = "UnityMcpSupervisor"
DEFAULT_ENDPOINT = "http://127.0.0.1:8080"


def default_state_dir() -> Path:
    override = os.environ.get("UMCP_STATE_DIR")
    if override:
        return Path(override).expanduser().resolve()
    if sys.platform == "win32":
        base = os.environ.get("LOCALAPPDATA")
        if base:
            return Path(base) / APP_DIR_NAME
        return Path.home() / "AppData" / "Local" / APP_DIR_NAME
    if sys.platform == "darwin":
        return Path.home() / "Library" / "Application Support" / APP_DIR_NAME
    base = os.environ.get("XDG_STATE_HOME")
    return (
        Path(base) if base else Path.home() / ".local" / "state"
    ) / "unity-mcp-supervisor"


def validate_endpoint(value: str) -> str:
    parsed = urlparse(value.strip())
    if parsed.scheme != "http" or parsed.hostname != "127.0.0.1":
        raise UsageError("Endpoint must use http://127.0.0.1:<port>.")
    if parsed.username or parsed.password or parsed.query or parsed.fragment:
        raise UsageError("Endpoint must not contain credentials, query, or fragment.")
    if parsed.path not in ("", "/"):
        raise UsageError("Endpoint must be a base URL without a path.")
    try:
        port = parsed.port
    except ValueError as exc:
        raise UsageError(f"Invalid endpoint port: {exc}") from exc
    if port is None or not 1 <= port <= 65535:
        raise UsageError("Endpoint must include a port from 1 to 65535.")
    return f"http://127.0.0.1:{port}"


def ensure_private_directory(path: Path) -> None:
    path.mkdir(parents=True, exist_ok=True)
    if os.name != "nt":
        path.chmod(0o700)


def _atomic_write(path: Path, content: str) -> None:
    ensure_private_directory(path.parent)
    handle, temp_name = tempfile.mkstemp(prefix=f".{path.name}.", dir=path.parent)
    temp_path = Path(temp_name)
    try:
        with os.fdopen(handle, "w", encoding="utf-8", newline="\n") as stream:
            stream.write(content)
            stream.flush()
            os.fsync(stream.fileno())
        if os.name != "nt":
            temp_path.chmod(0o600)
        replace_deadline = time.monotonic() + 1.0
        while True:
            try:
                os.replace(temp_path, path)
                break
            except PermissionError:
                if os.name != "nt" or time.monotonic() >= replace_deadline:
                    raise
                time.sleep(0.01)
    finally:
        if temp_path.exists():
            temp_path.unlink()


def _unlink_with_retry(path: Path) -> None:
    deadline = time.monotonic() + 1.0
    while True:
        try:
            path.unlink()
            return
        except FileNotFoundError:
            return
        except PermissionError:
            if os.name != "nt" or time.monotonic() >= deadline:
                raise
            time.sleep(0.01)


@dataclass(frozen=True)
class StatePaths:
    root: Path

    @property
    def config(self) -> Path:
        return self.root / "config.toml"

    @property
    def service(self) -> Path:
        return self.root / "service.json"

    @property
    def control(self) -> Path:
        return self.root / "control.json"

    @property
    def supervisor_pid(self) -> Path:
        return self.root / "supervisor.pid"

    @property
    def server_pid(self) -> Path:
        return self.root / "server.pid"

    @property
    def service_lock(self) -> Path:
        return self.root / "service.lock"

    @property
    def daemon_lock(self) -> Path:
        return self.root / "daemon.lock"

    @property
    def operations_gate(self) -> Path:
        return self.root / "operations-gate.lock"

    @property
    def editor_control(self) -> Path:
        return self.root / "editor-control"

    @property
    def editor_discovery(self) -> Path:
        return self.editor_control / "editors"

    @property
    def editor_requests(self) -> Path:
        return self.editor_control / "requests"

    @property
    def editor_responses(self) -> Path:
        return self.editor_control / "responses"

    @property
    def locks(self) -> Path:
        return self.root / "locks"

    @property
    def project_leases(self) -> Path:
        return self.root / "project-leases"

    @property
    def workspace_control(self) -> Path:
        return self.root / "workspace-control.sqlite3"

    @property
    def workspace_registrations(self) -> Path:
        return self.root / "workspace-registrations"

    @property
    def test_farm(self) -> Path:
        return self.root / "test-farm"

    @property
    def test_farm_database(self) -> Path:
        return self.test_farm / "test-farm.sqlite3"

    @property
    def test_farm_slots(self) -> Path:
        return self.test_farm / "slots"

    @property
    def test_farm_artifacts(self) -> Path:
        return self.test_farm / "artifacts"

    @property
    def logs(self) -> Path:
        return self.root / "logs"

    @property
    def supervisor_log(self) -> Path:
        return self.logs / "supervisor.log"

    @property
    def server_log(self) -> Path:
        return self.logs / "server.log"

    def ensure(self) -> None:
        ensure_private_directory(self.root)
        ensure_private_directory(self.locks)
        ensure_private_directory(self.project_leases)
        ensure_private_directory(self.workspace_registrations)
        ensure_private_directory(self.test_farm)
        ensure_private_directory(self.test_farm_artifacts)
        ensure_private_directory(self.logs)
        ensure_private_directory(self.editor_discovery)
        ensure_private_directory(self.editor_requests)
        ensure_private_directory(self.editor_responses)


@dataclass(frozen=True)
class Settings:
    state_dir: Path
    endpoint: str = DEFAULT_ENDPOINT
    connect_timeout_seconds: float = 60.0
    bootstrap_timeout_seconds: float = 300.0
    reconnect_timeout_seconds: float = 300.0
    project_lock_timeout_seconds: float = 600.0
    project_lease_ttl_seconds: float = 1800.0
    service_start_timeout_seconds: float = 30.0
    health_interval_seconds: float = 5.0
    health_failure_limit: int = 3
    command_timeout_seconds: float = 30.0
    approved_plugin_refs: tuple[str, ...] = ()

    @property
    def paths(self) -> StatePaths:
        return StatePaths(self.state_dir)

    @classmethod
    def load(
        cls,
        state_dir: Path | str | None = None,
        endpoint_override: str | None = None,
    ) -> Settings:
        root = (
            Path(state_dir).expanduser().resolve() if state_dir else default_state_dir()
        )
        paths = StatePaths(root)
        config: dict[str, Any] = {}
        if paths.config.exists():
            try:
                with paths.config.open("rb") as stream:
                    config = tomllib.load(stream)
            except (OSError, tomllib.TOMLDecodeError) as exc:
                raise UsageError(f"Invalid config file {paths.config}: {exc}") from exc
        service = config.get("service", {})
        compatibility = config.get("compatibility", {})
        endpoint = (
            endpoint_override
            or os.environ.get("UMCP_ENDPOINT")
            or service.get("endpoint", DEFAULT_ENDPOINT)
        )
        approved = compatibility.get("approved_plugin_refs", [])
        if not isinstance(approved, list) or not all(
            isinstance(item, str) for item in approved
        ):
            raise UsageError(
                "compatibility.approved_plugin_refs must be a string array."
            )
        return cls(
            state_dir=root,
            endpoint=validate_endpoint(str(endpoint)),
            approved_plugin_refs=tuple(approved),
        )

    def save(
        self,
        *,
        endpoint: str | None = None,
        approved_plugin_refs: tuple[str, ...] | None = None,
    ) -> Settings:
        updated = Settings(
            state_dir=self.state_dir,
            endpoint=validate_endpoint(endpoint or self.endpoint),
            connect_timeout_seconds=self.connect_timeout_seconds,
            bootstrap_timeout_seconds=self.bootstrap_timeout_seconds,
            reconnect_timeout_seconds=self.reconnect_timeout_seconds,
            project_lock_timeout_seconds=self.project_lock_timeout_seconds,
            project_lease_ttl_seconds=self.project_lease_ttl_seconds,
            service_start_timeout_seconds=self.service_start_timeout_seconds,
            health_interval_seconds=self.health_interval_seconds,
            health_failure_limit=self.health_failure_limit,
            command_timeout_seconds=self.command_timeout_seconds,
            approved_plugin_refs=approved_plugin_refs
            if approved_plugin_refs is not None
            else self.approved_plugin_refs,
        )
        approved_json = json.dumps(
            list(updated.approved_plugin_refs), ensure_ascii=False
        )
        content = (
            "[service]\n"
            f"endpoint = {json.dumps(updated.endpoint)}\n\n"
            "[compatibility]\n"
            f"approved_plugin_refs = {approved_json}\n"
        )
        _atomic_write(updated.paths.config, content)
        return updated


@dataclass
class ServiceRecord:
    schema_version: int = 1
    status: str = "starting"
    owner: str = "owned"
    supervisor_pid: int | None = None
    supervisor_token: str | None = None
    server_pid: int | None = None
    server_token: str | None = None
    server_created_at: float | None = None
    adopted: bool = False
    endpoint: str = DEFAULT_ENDPOINT
    server_version: str = "unknown"
    restart_count: int = 0
    started_at: float = field(default_factory=time.time)
    updated_at: float = field(default_factory=time.time)
    message: str = ""
    supervisor_log: str = ""
    server_log: str = ""

    @classmethod
    def from_dict(cls, value: dict[str, Any]) -> ServiceRecord:
        allowed = cls.__dataclass_fields__.keys()
        return cls(**{key: value[key] for key in allowed if key in value})

    def to_dict(self) -> dict[str, Any]:
        return asdict(self)


class StateStore:
    def __init__(self, paths: StatePaths) -> None:
        self.paths = paths
        self.paths.ensure()

    def read_service(self) -> ServiceRecord | None:
        try:
            value = json.loads(self.paths.service.read_text(encoding="utf-8"))
            return ServiceRecord.from_dict(value)
        except (FileNotFoundError, OSError, ValueError, TypeError):
            return None

    def write_service(self, record: ServiceRecord) -> None:
        record.updated_at = time.time()
        _atomic_write(
            self.paths.service,
            json.dumps(record.to_dict(), indent=2, sort_keys=True) + "\n",
        )
        if record.supervisor_pid:
            _atomic_write(self.paths.supervisor_pid, f"{record.supervisor_pid}\n")

    def write_control(self, *, token: str, action: str, request_id: str) -> None:
        payload = {
            "supervisor_token": token,
            "action": action,
            "request_id": request_id,
        }
        _atomic_write(self.paths.control, json.dumps(payload, sort_keys=True) + "\n")

    def read_control(self) -> dict[str, Any] | None:
        try:
            return json.loads(self.paths.control.read_text(encoding="utf-8"))
        except (FileNotFoundError, OSError, ValueError, TypeError):
            return None

    def clear_control(self) -> None:
        _unlink_with_retry(self.paths.control)


def process_alive(pid: int | None) -> bool:
    try:
        process_id = int(pid or 0)
    except (TypeError, ValueError):
        return False
    if process_id <= 0:
        return False
    if os.name != "nt":
        try:
            os.kill(process_id, 0)
            return True
        except ProcessLookupError:
            return False
        except PermissionError:
            return True

    process_query_limited_information = 0x1000
    still_active = 259
    from ctypes import wintypes

    kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
    open_process = kernel32.OpenProcess
    open_process.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
    open_process.restype = wintypes.HANDLE
    get_exit_code = kernel32.GetExitCodeProcess
    get_exit_code.argtypes = [wintypes.HANDLE, ctypes.POINTER(wintypes.DWORD)]
    get_exit_code.restype = wintypes.BOOL
    close_handle = kernel32.CloseHandle
    close_handle.argtypes = [wintypes.HANDLE]
    close_handle.restype = wintypes.BOOL

    handle = open_process(process_query_limited_information, False, process_id)
    if not handle:
        return False
    try:
        exit_code = wintypes.DWORD()
        if not get_exit_code(handle, ctypes.byref(exit_code)):
            return False
        return exit_code.value == still_active
    finally:
        close_handle(handle)


def runtime_fingerprint() -> dict[str, str]:
    return {
        "python": platform.python_version(),
        "platform": platform.platform(),
    }
