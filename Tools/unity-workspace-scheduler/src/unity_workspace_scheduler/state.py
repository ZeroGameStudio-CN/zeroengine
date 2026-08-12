"""Machine-local state and token-file primitives."""

from __future__ import annotations

import os
import sqlite3
import subprocess
from dataclasses import dataclass
from pathlib import Path

from .errors import UsageError

APP_DIR_NAME = "UnityWorkspaceScheduler"
STATE_ENVIRONMENT_VARIABLE = "UNITY_SCHEDULER_STATE_DIR"
SCHEMA_VERSION = 1


@dataclass(frozen=True)
class StatePaths:
    root: Path

    @property
    def database(self) -> Path:
        return self.root / "scheduler.sqlite3"


def default_state_dir() -> Path:
    override = os.environ.get(STATE_ENVIRONMENT_VARIABLE)
    if override:
        return Path(override).expanduser().resolve()
    if os.name == "nt":
        base = os.environ.get("LOCALAPPDATA")
        if not base:
            raise UsageError("LOCALAPPDATA is unavailable.")
        return Path(base) / APP_DIR_NAME
    xdg_state = os.environ.get("XDG_STATE_HOME")
    if xdg_state:
        return Path(xdg_state).expanduser() / "unity-workspace-scheduler"
    return Path.home() / ".local" / "state" / "unity-workspace-scheduler"


def resolve_state_paths(override: Path | None = None) -> StatePaths:
    root = (override or default_state_dir()).expanduser().resolve()
    root.mkdir(mode=0o700, parents=True, exist_ok=True)
    if os.name != "nt":
        root.chmod(0o700)
    return StatePaths(root=root)


def canonical_workspace(path: Path | str) -> str:
    try:
        resolved = Path(path).expanduser().resolve(strict=True)
    except OSError as exc:
        raise UsageError(f"Workspace does not exist: {path}") from exc
    if not resolved.is_dir():
        raise UsageError(f"Workspace is not a directory: {resolved}")
    return str(resolved)


def open_database(paths: StatePaths) -> sqlite3.Connection:
    connection = sqlite3.connect(paths.database, timeout=30.0)
    connection.row_factory = sqlite3.Row
    connection.execute("PRAGMA foreign_keys = ON")
    connection.execute("PRAGMA busy_timeout = 30000")
    connection.execute("PRAGMA journal_mode = WAL")
    connection.executescript(
        """
        CREATE TABLE IF NOT EXISTS scheduler_meta (
            key TEXT PRIMARY KEY,
            value TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS workspaces (
            id TEXT PRIMARY KEY,
            root TEXT NOT NULL UNIQUE,
            registered_at REAL NOT NULL,
            epoch INTEGER NOT NULL DEFAULT 1
        );

        CREATE TABLE IF NOT EXISTS tasks (
            id TEXT PRIMARY KEY,
            workspace_id TEXT NOT NULL REFERENCES workspaces(id) ON DELETE CASCADE,
            owner TEXT NOT NULL,
            summary TEXT NOT NULL,
            token_hash TEXT NOT NULL,
            state TEXT NOT NULL,
            created_at REAL NOT NULL,
            heartbeat_at REAL NOT NULL,
            expires_at REAL NOT NULL,
            finished_at REAL,
            result TEXT,
            note TEXT
        );

        CREATE INDEX IF NOT EXISTS tasks_workspace_state
            ON tasks(workspace_id, state);

        CREATE TABLE IF NOT EXISTS claims (
            id TEXT PRIMARY KEY,
            workspace_id TEXT NOT NULL REFERENCES workspaces(id) ON DELETE CASCADE,
            task_id TEXT NOT NULL REFERENCES tasks(id) ON DELETE CASCADE,
            kind TEXT NOT NULL,
            state TEXT NOT NULL,
            queue_order INTEGER NOT NULL,
            created_at REAL NOT NULL,
            granted_at REAL,
            released_at REAL
        );

        CREATE INDEX IF NOT EXISTS claims_workspace_state_order
            ON claims(workspace_id, state, queue_order);

        CREATE TABLE IF NOT EXISTS claim_scopes (
            claim_id TEXT NOT NULL REFERENCES claims(id) ON DELETE CASCADE,
            scope_type TEXT NOT NULL,
            value TEXT NOT NULL,
            PRIMARY KEY(claim_id, scope_type, value)
        );

        CREATE TABLE IF NOT EXISTS recovery_events (
            id TEXT PRIMARY KEY,
            workspace_id TEXT NOT NULL REFERENCES workspaces(id) ON DELETE CASCADE,
            task_id TEXT NOT NULL REFERENCES tasks(id) ON DELETE CASCADE,
            resolution TEXT NOT NULL,
            evidence TEXT NOT NULL,
            created_at REAL NOT NULL
        );
        """
    )
    existing = connection.execute(
        "SELECT value FROM scheduler_meta WHERE key = 'schema_version'"
    ).fetchone()
    if existing is None:
        connection.execute(
            "INSERT INTO scheduler_meta(key, value) VALUES('schema_version', ?)",
            (str(SCHEMA_VERSION),),
        )
        connection.commit()
    elif existing["value"] != str(SCHEMA_VERSION):
        connection.close()
        raise UsageError(
            f"Unsupported scheduler schema {existing['value']}; expected {SCHEMA_VERSION}."
        )
    return connection


def create_token_file(path: Path, token: str) -> Path:
    resolved = path.expanduser().resolve()
    created = False
    try:
        resolved.parent.mkdir(mode=0o700, parents=True, exist_ok=True)
        descriptor = os.open(resolved, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
        created = True
        os.close(descriptor)
        if os.name == "nt":
            domain = os.environ.get("USERDOMAIN")
            username = os.environ.get("USERNAME")
            if not domain or not username:
                raise OSError("Current Windows identity is unavailable.")
            secured = subprocess.run(
                [
                    "icacls",
                    str(resolved),
                    "/inheritance:r",
                    "/grant:r",
                    f"{domain}\\{username}:(F)",
                ],
                check=False,
                capture_output=True,
                text=True,
                creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0),
            )
            if secured.returncode != 0:
                raise OSError("Windows owner-only ACL could not be applied.")
        else:
            resolved.chmod(0o600)
        with resolved.open("w", encoding="utf-8", newline="\n") as stream:
            stream.write(token + "\n")
            stream.flush()
            os.fsync(stream.fileno())
    except OSError as exc:
        if created:
            resolved.unlink(missing_ok=True)
        raise UsageError(f"Cannot create task token file: {exc}") from exc
    return resolved


def read_token_file(path: Path) -> str:
    try:
        token = path.expanduser().read_text(encoding="utf-8").strip()
    except OSError as exc:
        raise UsageError(f"Cannot read task token file: {exc}") from exc
    if not token:
        raise UsageError("Task token file is empty.")
    return token


def remove_matching_token_file(path: Path, token: str) -> bool:
    resolved = path.expanduser().resolve()
    try:
        if resolved.read_text(encoding="utf-8").strip() != token:
            return False
        resolved.unlink()
        return True
    except FileNotFoundError:
        return True
    except OSError as exc:
        raise UsageError(f"Cannot remove task token file: {exc}") from exc
