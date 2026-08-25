"""Machine-local state and token-file primitives."""

from __future__ import annotations

import ctypes
import errno
import hashlib
import math
import os
import re
import sqlite3
import stat
import subprocess
import tempfile
import time
import unicodedata
from collections.abc import Iterator
from contextlib import contextmanager
from dataclasses import dataclass
from pathlib import Path

from .errors import StateError, UsageError

APP_DIR_NAME = "UnityWorkspaceScheduler"
STATE_ENVIRONMENT_VARIABLE = "UNITY_SCHEDULER_STATE_DIR"
LEGACY_SCHEMA_TWO_VERSION = 2
SCHEMA_VERSION = 3
MAX_TASK_TTL_SECONDS = 86400.0
TERMINAL_CLAIM_RETENTION = 10000
_WAL_RETRY_TIMEOUT_SECONDS = 30.0
_WAL_RETRY_SECONDS = 0.05
_ACL_COMMAND_TIMEOUT_SECONDS = 10.0
_TOKEN_FILE_MAX_BYTES = 4096
_TOKEN_PATH_LOCK_TIMEOUT_SECONDS = 30.0
_TOKEN_PATH_LOCK_SHARD_COUNT = 4096
_WINDOWS_ACCESS_ALLOWED_ACE_TYPE = 0
_WINDOWS_ACCESS_DENIED_ACE_TYPES = frozenset({1, 6, 10, 12})
_WINDOWS_FILE_READ_DATA = 0x0001
_WINDOWS_OWNER_RIGHTS_SID = "S-1-3-4"
_WINDOWS_FILE_LIST_DIRECTORY = 0x0001
_WINDOWS_FILE_READ_ATTRIBUTES = 0x0080
_WINDOWS_GENERIC_READ = 0x80000000
_WINDOWS_GENERIC_WRITE = 0x40000000
_WINDOWS_FILE_FLAG_WRITE_THROUGH = 0x80000000
_WINDOWS_FILE_FLAG_BACKUP_SEMANTICS = 0x02000000
_WINDOWS_ALLOWED_TOKEN_SIDS = frozenset(
    {
        "S-1-5-18",  # LOCAL_SYSTEM
        "S-1-5-32-544",  # BUILTIN\Administrators
    }
)
_BROAD_WINDOWS_TOKEN_SIDS = {
    "S-1-1-0": "Everyone",
    "S-1-5-7": "Anonymous",
    "S-1-5-11": "Authenticated Users",
    "S-1-5-32-545": "BUILTIN\\Users",
    "S-1-5-32-546": "BUILTIN\\Guests",
}

_SCHEMA_TWO_STATEMENTS = (
    """
    CREATE TABLE scheduler_meta (
        key TEXT PRIMARY KEY,
        value TEXT NOT NULL
    )
    """,
    """
    CREATE TABLE workspaces (
        id TEXT PRIMARY KEY,
        root TEXT NOT NULL UNIQUE,
        registered_at REAL NOT NULL,
        epoch INTEGER NOT NULL DEFAULT 1,
        next_queue_order INTEGER NOT NULL DEFAULT 1
    )
    """,
    """
    CREATE TABLE tasks (
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
    )
    """,
    """
    CREATE TABLE claims (
        id TEXT PRIMARY KEY,
        workspace_id TEXT NOT NULL REFERENCES workspaces(id) ON DELETE CASCADE,
        task_id TEXT NOT NULL REFERENCES tasks(id) ON DELETE CASCADE,
        kind TEXT NOT NULL,
        state TEXT NOT NULL,
        queue_order INTEGER NOT NULL,
        created_at REAL NOT NULL,
        granted_at REAL,
        released_at REAL
    )
    """,
    """
    CREATE TABLE claim_scopes (
        claim_id TEXT NOT NULL REFERENCES claims(id) ON DELETE CASCADE,
        scope_type TEXT NOT NULL,
        value TEXT NOT NULL,
        PRIMARY KEY(claim_id, scope_type, value)
    )
    """,
    """
    CREATE TABLE recovery_events (
        id TEXT PRIMARY KEY,
        workspace_id TEXT NOT NULL REFERENCES workspaces(id) ON DELETE CASCADE,
        task_id TEXT NOT NULL REFERENCES tasks(id) ON DELETE CASCADE,
        resolution TEXT NOT NULL,
        evidence TEXT NOT NULL,
        created_at REAL NOT NULL
    )
    """,
)

_SCHEMA_THREE_RECEIPT_STATEMENTS = (
    """
    CREATE TABLE operation_receipts (
        operation_id TEXT PRIMARY KEY,
        workspace_id TEXT NOT NULL,
        action TEXT NOT NULL,
        parameters_json TEXT NOT NULL,
        owner_token_hash TEXT,
        fingerprint TEXT NOT NULL,
        task_id TEXT,
        result_json TEXT NOT NULL,
        terminal_json TEXT,
        token_cleanup_path TEXT,
        token_cleanup_identity TEXT,
        created_at REAL NOT NULL,
        finalized_at REAL,
        delivered_at REAL,
        retired_at REAL
    )
    """,
    """
    CREATE TABLE token_cleanup_jobs (
        task_id TEXT PRIMARY KEY REFERENCES tasks(id) ON DELETE CASCADE,
        workspace_id TEXT NOT NULL REFERENCES workspaces(id) ON DELETE CASCADE,
        token_file_path TEXT NOT NULL UNIQUE,
        token_file_identity TEXT NOT NULL UNIQUE,
        token_hash TEXT NOT NULL UNIQUE,
        reason TEXT NOT NULL,
        created_at REAL NOT NULL,
        completed_at REAL,
        last_attempt_at REAL,
        attempt_count INTEGER NOT NULL DEFAULT 0
    )
    """,
)

_SCHEMA_THREE_TASK_STATEMENTS = (
    "ALTER TABLE tasks ADD COLUMN token_file_path TEXT",
    "ALTER TABLE tasks ADD COLUMN token_file_identity TEXT",
    "ALTER TABLE tasks ADD COLUMN start_operation_id TEXT",
)

_SCHEMA_TWO_INDEX_STATEMENTS = (
    "CREATE INDEX IF NOT EXISTS tasks_workspace_state ON tasks(workspace_id, state)",
    "CREATE INDEX IF NOT EXISTS tasks_state_expires ON tasks(state, expires_at)",
    """
    CREATE INDEX IF NOT EXISTS tasks_workspace_state_expires
        ON tasks(workspace_id, state, expires_at)
    """,
    """
    CREATE INDEX IF NOT EXISTS tasks_workspace_token_created
        ON tasks(workspace_id, token_hash, created_at DESC)
    """,
    """
    CREATE INDEX IF NOT EXISTS tasks_workspace_terminal_recency
        ON tasks(workspace_id, finished_at DESC, created_at DESC, id DESC)
        WHERE state IN ('completed', 'failed', 'expired')
    """,
    """
    CREATE INDEX IF NOT EXISTS claims_workspace_state_order
        ON claims(workspace_id, state, queue_order)
    """,
    "CREATE INDEX IF NOT EXISTS claims_workspace_order ON claims(workspace_id, queue_order)",
    "CREATE INDEX IF NOT EXISTS claims_task_state ON claims(task_id, state)",
    "CREATE INDEX IF NOT EXISTS recovery_events_task_id ON recovery_events(task_id)",
)

_SCHEMA_THREE_INDEX_STATEMENTS = (
    """
    CREATE INDEX IF NOT EXISTS tasks_open_token_hash_global
        ON tasks(token_hash)
        WHERE state IN ('active', 'outcome_unknown')
    """,
    """
    CREATE INDEX IF NOT EXISTS tasks_open_token_file_identity
        ON tasks(token_file_identity)
        WHERE token_file_identity IS NOT NULL
        AND state IN ('active', 'outcome_unknown')
    """,
    """
    CREATE UNIQUE INDEX IF NOT EXISTS tasks_start_operation_id
        ON tasks(start_operation_id)
        WHERE start_operation_id IS NOT NULL
    """,
    """
    CREATE INDEX IF NOT EXISTS claims_open_global
        ON claims(state)
        WHERE state IN ('queued', 'active', 'parked')
    """,
    """
    CREATE INDEX IF NOT EXISTS operation_receipts_delivered_created
        ON operation_receipts(delivered_at DESC, created_at DESC, operation_id DESC)
        WHERE delivered_at IS NOT NULL
    """,
    """
    CREATE INDEX IF NOT EXISTS operation_receipts_retired_created
        ON operation_receipts(retired_at DESC, created_at DESC, operation_id DESC)
        WHERE retired_at IS NOT NULL
    """,
    """
    CREATE INDEX IF NOT EXISTS operation_receipts_workspace_created
        ON operation_receipts(workspace_id, created_at DESC, operation_id DESC)
    """,
    """
    CREATE INDEX IF NOT EXISTS operation_receipts_action_task
        ON operation_receipts(action, task_id)
    """,
    """
    CREATE INDEX IF NOT EXISTS operation_receipts_replay_required
        ON operation_receipts(operation_id)
        WHERE delivered_at IS NULL AND retired_at IS NULL
    """,
    """
    CREATE UNIQUE INDEX IF NOT EXISTS operation_receipts_task_start_unique
        ON operation_receipts(task_id)
        WHERE action = 'task.start'
    """,
    """
    CREATE INDEX IF NOT EXISTS operation_receipts_cleanup_identity
        ON operation_receipts(token_cleanup_identity)
        WHERE token_cleanup_identity IS NOT NULL
    """,
    """
    CREATE INDEX IF NOT EXISTS operation_receipts_cleanup_token_hash
        ON operation_receipts(owner_token_hash)
        WHERE token_cleanup_path IS NOT NULL
    """,
    """
    CREATE INDEX IF NOT EXISTS token_cleanup_jobs_pending_created
        ON token_cleanup_jobs(last_attempt_at, created_at, task_id)
    """,
)


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


def _ensure_private_directory(
    path: Path,
    *,
    preserve_existing: bool = False,
    require_new: bool = False,
) -> None:
    """Create a private leaf, optionally requiring new or preserving an existing directory."""

    if require_new:
        if os.name == "nt":
            path.mkdir(exist_ok=False)
        else:
            path.mkdir(mode=0o700, exist_ok=False)
            path.chmod(0o700)
        _durable_directory_barrier(path.parent)
        return

    missing: list[Path] = []
    cursor = path
    while not cursor.exists():
        missing.append(cursor)
        parent = cursor.parent
        if parent == cursor:
            raise FileNotFoundError(f"Directory path has no existing ancestor: {path}")
        cursor = parent
    if not cursor.is_dir():
        raise NotADirectoryError(f"Directory path is not a directory: {cursor}")

    for directory in reversed(missing):
        try:
            if os.name == "nt":
                directory.mkdir(exist_ok=False)
            else:
                directory.mkdir(mode=0o700, exist_ok=False)
                directory.chmod(0o700)
        except FileExistsError:
            if not directory.is_dir():
                raise NotADirectoryError(f"Directory path is not a directory: {directory}")
            continue
        _durable_directory_barrier(directory.parent)

    if preserve_existing:
        if not missing:
            if os.name == "nt":
                if not path.is_dir():
                    raise NotADirectoryError(f"Directory path is not a directory: {path}")
                return
            metadata = path.lstat()
            if not stat.S_ISDIR(metadata.st_mode):
                raise NotADirectoryError(f"Directory path is not a directory: {path}")
            if metadata.st_uid != os.geteuid():
                raise PermissionError(f"Directory is not owned by the current user: {path}")
            if stat.S_IMODE(metadata.st_mode) & 0o022:
                raise PermissionError(f"Directory is writable by another user or group: {path}")
            return
        return
    if os.name == "nt":
        return
    path.chmod(0o700)


def resolve_state_paths(override: Path | None = None) -> StatePaths:
    root = (override or default_state_dir()).expanduser().resolve()
    _ensure_private_directory(root)
    return StatePaths(root=root)


def canonical_workspace(path: Path | str) -> str:
    try:
        resolved = Path(path).expanduser().resolve(strict=True)
    except OSError as exc:
        raise UsageError(f"Workspace does not exist: {path}") from exc
    if _has_control_characters(str(resolved)):
        raise UsageError("Workspace path contains control characters.")
    if not resolved.is_dir():
        raise UsageError(f"Workspace is not a directory: {resolved}")
    return str(resolved)


def _platform_case_identity(value: str) -> str:
    """Return the platform's stable case identity for a path-like value."""

    if not isinstance(value, str):
        raise TypeError("Platform case identity requires text.")
    return value.casefold() if os.name == "nt" else value


def _legacy_workspace_id(root: str) -> str:
    """Return the schema 1/2 workspace ID, which case-folded on every platform."""

    identity = os.path.normcase(root).casefold()
    return hashlib.sha256(identity.encode("utf-8")).hexdigest()


def _current_workspace_id(root: str) -> str:
    identity = _platform_case_identity(root)
    return hashlib.sha256(identity.encode("utf-8")).hexdigest()


def _durable_directory_descriptor_barrier(descriptor: int) -> None:
    metadata = os.fstat(descriptor)
    if not stat.S_ISDIR(metadata.st_mode):
        raise OSError("Durable directory barrier requires a directory descriptor.")
    try:
        os.fsync(descriptor)
    except OSError as exc:
        raise OSError("Durable directory metadata flush failed.") from exc


def _durable_windows_directory_barrier(path: Path) -> None:
    from ctypes import wintypes

    directory = path.expanduser().resolve(strict=True)
    if not directory.is_dir() or _is_windows_reparse_point(directory):
        raise OSError("Durable directory barrier requires an existing non-reparse directory.")
    kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
    kernel32.CreateFileW.argtypes = [
        wintypes.LPCWSTR,
        wintypes.DWORD,
        wintypes.DWORD,
        ctypes.c_void_p,
        wintypes.DWORD,
        wintypes.DWORD,
        wintypes.HANDLE,
    ]
    kernel32.CreateFileW.restype = wintypes.HANDLE
    kernel32.FlushFileBuffers.argtypes = [wintypes.HANDLE]
    kernel32.FlushFileBuffers.restype = wintypes.BOOL
    kernel32.CloseHandle.argtypes = [wintypes.HANDLE]
    kernel32.CloseHandle.restype = wintypes.BOOL
    handle = kernel32.CreateFileW(
        str(directory),
        _WINDOWS_GENERIC_READ | _WINDOWS_GENERIC_WRITE,
        0x00000001 | 0x00000002 | 0x00000004,
        None,
        3,
        _WINDOWS_FILE_FLAG_WRITE_THROUGH | _WINDOWS_FILE_FLAG_BACKUP_SEMANTICS,
        None,
    )
    invalid_handle = ctypes.c_void_p(-1).value
    if handle == invalid_handle:
        raise ctypes.WinError(ctypes.get_last_error())
    try:
        if not kernel32.FlushFileBuffers(handle):
            raise ctypes.WinError(ctypes.get_last_error())
    finally:
        kernel32.CloseHandle(handle)


def _durable_directory_barrier(path: Path) -> None:
    """Flush directory metadata after a token directory-entry mutation."""

    if os.name == "nt":
        _durable_windows_directory_barrier(path)
        return
    flags = os.O_RDONLY | getattr(os, "O_DIRECTORY", 0) | getattr(os, "O_CLOEXEC", 0)
    if hasattr(os, "O_NOFOLLOW"):
        flags |= os.O_NOFOLLOW
    descriptor = os.open(Path(path), flags)
    try:
        _durable_directory_descriptor_barrier(descriptor)
    finally:
        os.close(descriptor)


@contextmanager
def task_token_path_lock(
    paths: StatePaths,
    token_path: Path,
    *,
    timeout_seconds: float = _TOKEN_PATH_LOCK_TIMEOUT_SECONDS,
) -> Iterator[None]:
    """Serialize Scheduler token lifecycle transitions for one canonical path."""

    if (
        isinstance(timeout_seconds, bool)
        or not isinstance(timeout_seconds, (int, float))
        or not math.isfinite(float(timeout_seconds))
        or float(timeout_seconds) < 0
    ):
        raise UsageError("Task token path lock timeout must be finite and non-negative.")
    identity = os.path.normcase(str(token_path))
    if os.name == "nt":
        identity = identity.casefold()
    lock_root = paths.root / "token-path-locks"
    _ensure_private_directory(lock_root, preserve_existing=True)
    digest = hashlib.sha256(identity.encode("utf-8")).hexdigest()
    shard = int(digest[:3], 16) % _TOKEN_PATH_LOCK_SHARD_COUNT
    lock_path = lock_root / f"v1-{shard:03x}.lock"
    descriptor = os.open(lock_path, os.O_RDWR | os.O_CREAT, 0o600)
    acquired = False
    try:
        deadline = time.monotonic() + float(timeout_seconds)
        if os.name == "nt":
            import msvcrt

            while True:
                try:
                    os.lseek(descriptor, 0, os.SEEK_SET)
                    msvcrt.locking(descriptor, msvcrt.LK_NBLCK, 1)
                    acquired = True
                    break
                except OSError:
                    if time.monotonic() >= deadline:
                        raise UsageError(
                            "Task token path is busy.",
                            details={"reason": "task-token-path-lock-timeout"},
                        )
                    time.sleep(0.05)
        else:
            import fcntl

            os.fchmod(descriptor, 0o600)
            while True:
                try:
                    fcntl.flock(descriptor, fcntl.LOCK_EX | fcntl.LOCK_NB)
                    acquired = True
                    break
                except OSError as exc:
                    if exc.errno not in {errno.EACCES, errno.EAGAIN}:
                        raise
                    if time.monotonic() >= deadline:
                        raise UsageError(
                            "Task token path is busy.",
                            details={"reason": "task-token-path-lock-timeout"},
                        ) from exc
                    time.sleep(0.05)
        yield
    finally:
        try:
            if acquired and os.name == "nt":
                import msvcrt

                os.lseek(descriptor, 0, os.SEEK_SET)
                msvcrt.locking(descriptor, msvcrt.LK_UNLCK, 1)
            elif acquired:
                import fcntl

                fcntl.flock(descriptor, fcntl.LOCK_UN)
        finally:
            os.close(descriptor)


def _has_control_characters(value: str) -> bool:
    return any(unicodedata.category(character) == "Cc" for character in value)


def _has_disallowed_evidence_controls(value: str) -> bool:
    return any(
        unicodedata.category(character) == "Cc" and character not in {"\t", "\n", "\r"}
        for character in value
    )


def _is_normalized_recovery_evidence(value: object) -> bool:
    return (
        isinstance(value, str)
        and bool(value)
        and value.strip() == value
        and not _has_disallowed_evidence_controls(value)
    )


def _canonical_schema_version(value: object, storage_type: str) -> int | None:
    if storage_type != "text" or not isinstance(value, str):
        return None
    if value == "1":
        return 1
    if value == str(LEGACY_SCHEMA_TWO_VERSION):
        return LEGACY_SCHEMA_TWO_VERSION
    if value == str(SCHEMA_VERSION):
        return SCHEMA_VERSION
    return None


def _is_windows_reparse_point(path: Path) -> bool:
    if os.name != "nt":
        return False
    try:
        attributes = path.lstat().st_file_attributes
    except FileNotFoundError:
        return False
    except OSError:
        return True
    return bool(attributes & getattr(stat, "FILE_ATTRIBUTE_REPARSE_POINT", 0x400))


class _WindowsTokenMissingError(FileNotFoundError):
    """The token was absent when its Windows handle was first opened."""


def open_database(paths: StatePaths) -> sqlite3.Connection:
    connection = sqlite3.connect(paths.database, timeout=30.0)
    connection.row_factory = sqlite3.Row
    connection.execute("PRAGMA foreign_keys = ON")
    connection.execute("PRAGMA busy_timeout = 1000")
    try:
        version = _read_schema_version(connection)
        if version == str(SCHEMA_VERSION):
            _enable_wal(connection)
            connection.execute("PRAGMA busy_timeout = 30000")
            return connection
        if version not in {None, "1", str(LEGACY_SCHEMA_TWO_VERSION)}:
            raise UsageError(f"Unsupported scheduler schema {version}; expected {SCHEMA_VERSION}.")
        connection.execute("BEGIN IMMEDIATE")
        version = _read_schema_version(connection)
        if version is None:
            _create_schema_three(connection)
        elif version == "1":
            _migrate_schema_one_to_three(connection, paths.database)
        elif version == str(LEGACY_SCHEMA_TWO_VERSION):
            _migrate_schema_two_to_three(connection, paths.database)
        elif version != str(SCHEMA_VERSION):
            raise UsageError(f"Unsupported scheduler schema {version}; expected {SCHEMA_VERSION}.")
        connection.commit()
        _enable_wal(connection)
        connection.execute("PRAGMA busy_timeout = 30000")
        return connection
    except Exception:
        connection.rollback()
        connection.close()
        raise


def _enable_wal(connection: sqlite3.Connection) -> None:
    deadline = time.monotonic() + _WAL_RETRY_TIMEOUT_SECONDS
    while True:
        try:
            mode = connection.execute("PRAGMA journal_mode = WAL").fetchone()[0]
            if str(mode).casefold() != "wal":
                raise UsageError(f"Scheduler database did not enter WAL mode: {mode}.")
            return
        except sqlite3.OperationalError as exc:
            remaining = deadline - time.monotonic()
            if "locked" not in str(exc).casefold() or remaining <= 0:
                raise
            time.sleep(min(_WAL_RETRY_SECONDS, remaining))


def _read_schema_version(connection: sqlite3.Connection) -> str | None:
    meta_table = connection.execute(
        "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'scheduler_meta'"
    ).fetchone()
    if meta_table is None:
        return None
    existing = connection.execute(
        "SELECT value, typeof(value) AS storage_type FROM scheduler_meta "
        "WHERE key = 'schema_version'"
    ).fetchone()
    if existing is None:
        raise UsageError("Scheduler schema metadata is missing.")
    value = existing["value"]
    if _canonical_schema_version(value, existing["storage_type"]) is None:
        if (
            existing["storage_type"] == "text"
            and isinstance(value, str)
            and value.isascii()
            and value.isdecimal()
            and not value.startswith("0")
        ):
            raise UsageError(f"Unsupported scheduler schema {value}; expected {SCHEMA_VERSION}.")
        raise UsageError(
            "Scheduler schema metadata is invalid; expected canonical TEXT '1', '2', or '3'."
        )
    return value


def _create_schema_three(connection: sqlite3.Connection) -> None:
    for statement in _SCHEMA_TWO_STATEMENTS:
        connection.execute(statement)
    for statement in _SCHEMA_THREE_TASK_STATEMENTS:
        connection.execute(statement)
    for statement in _SCHEMA_TWO_INDEX_STATEMENTS:
        connection.execute(statement)
    for statement in _SCHEMA_THREE_RECEIPT_STATEMENTS:
        connection.execute(statement)
    for statement in _SCHEMA_THREE_INDEX_STATEMENTS:
        connection.execute(statement)
    connection.execute(
        "INSERT INTO scheduler_meta(key, value) VALUES('schema_version', ?)",
        (str(SCHEMA_VERSION),),
    )


def _validate_schema_one_migration_source(connection: sqlite3.Connection, database: Path) -> None:
    _validate_schema_version_source(connection, database, schema_version=1)


def _install_schema_three_extensions(connection: sqlite3.Connection) -> None:
    for statement in _SCHEMA_THREE_TASK_STATEMENTS:
        connection.execute(statement)
    for statement in _SCHEMA_THREE_RECEIPT_STATEMENTS:
        connection.execute(statement)
    for statement in _SCHEMA_THREE_INDEX_STATEMENTS:
        connection.execute(statement)
    connection.execute(
        "DELETE FROM claims WHERE id IN ("
        "SELECT id FROM claims WHERE state IN ('released', 'cancelled') "
        "ORDER BY COALESCE(released_at, created_at) DESC, created_at DESC, id DESC "
        "LIMIT -1 OFFSET ?) AND state IN ('released', 'cancelled')",
        (TERMINAL_CLAIM_RETENTION,),
    )


def _validate_schema_version_source(
    connection: sqlite3.Connection,
    database: Path,
    *,
    schema_version: int,
) -> None:
    integrity_rows = [str(row[0]) for row in connection.execute("PRAGMA integrity_check")]
    if integrity_rows != ["ok"]:
        raise StateError(
            "Scheduler state failed SQLite integrity_check before migration.",
            details={
                "path": str(database.resolve()),
                "reason": "integrity-check-failed",
                "results": integrity_rows,
            },
        )
    foreign_key_rows = connection.execute("PRAGMA foreign_key_check").fetchall()
    if foreign_key_rows:
        raise StateError(
            "Scheduler state contains foreign-key violations before migration.",
            details={
                "path": str(database.resolve()),
                "reason": "foreign-key-check-failed",
                "violation_count": len(foreign_key_rows),
            },
        )

    from .state_ops import (
        _legacy_open_write_scope_migration_count,
        _validate_declared_schema_structure,
        _validate_relational_schema_signatures,
        _validate_schema_two_index_signatures,
        _validate_semantics,
    )

    _validate_semantics(connection, database.resolve(), schema_version=schema_version)
    legacy_open_write_scope_count = _legacy_open_write_scope_migration_count(
        connection,
        schema_version,
    )
    if legacy_open_write_scope_count:
        raise StateError(
            "Legacy open write claims cannot be migrated safely on this platform because their "
            "original path case cannot be reconstructed.",
            details={
                "path": str(database.resolve()),
                "reason": "legacy-open-write-scope-migration-blocked",
                "schema_version": schema_version,
                "open_write_scope_count": legacy_open_write_scope_count,
            },
        )
    _validate_relational_schema_signatures(
        connection,
        database.resolve(),
        schema_version=schema_version,
    )
    if schema_version == LEGACY_SCHEMA_TWO_VERSION:
        _validate_schema_two_index_signatures(connection, database.resolve())
    _validate_declared_schema_structure(
        connection,
        database.resolve(),
        schema_version=schema_version,
    )


def _validate_schema_three(connection: sqlite3.Connection, database: Path) -> None:
    from .state_ops import (
        _validate_declared_schema_structure,
        _validate_relational_schema_signatures,
        _validate_schema_three_index_signatures,
        _validate_semantics,
    )

    _validate_semantics(
        connection,
        database.resolve(),
        schema_version=SCHEMA_VERSION,
        allow_legacy_capacity_overflow=True,
    )
    _validate_relational_schema_signatures(
        connection,
        database.resolve(),
        schema_version=SCHEMA_VERSION,
    )
    _validate_schema_three_index_signatures(connection, database.resolve())
    _validate_declared_schema_structure(
        connection,
        database.resolve(),
        schema_version=SCHEMA_VERSION,
    )


def _normalize_legacy_open_task_times(connection: sqlite3.Connection) -> None:
    migration_now = time.time()
    if not math.isfinite(migration_now):
        raise UsageError("System clock is invalid; scheduler migration cannot continue safely.")
    maximum_open_expiry = migration_now + MAX_TASK_TTL_SECONDS
    open_tasks = connection.execute(
        "SELECT id, state, heartbeat_at, expires_at FROM tasks "
        "WHERE state IN ('active', 'outcome_unknown')"
    ).fetchall()
    for task in open_tasks:
        try:
            heartbeat_at = float(task["heartbeat_at"])
            expires_at = float(task["expires_at"])
        except (TypeError, ValueError) as exc:
            raise UsageError(
                f"Open task {task['id']} has invalid timing metadata; migration was rolled back."
            ) from exc
        if math.isnan(expires_at) or expires_at == -math.inf:
            expires_at = migration_now
        elif expires_at == math.inf or expires_at > maximum_open_expiry:
            expires_at = maximum_open_expiry
        if not math.isfinite(heartbeat_at):
            heartbeat_at = migration_now
        if task["state"] == "active" and expires_at > migration_now:
            lease_duration = expires_at - heartbeat_at
            if (
                heartbeat_at > migration_now
                or not math.isfinite(lease_duration)
                or lease_duration <= 0
                or lease_duration > MAX_TASK_TTL_SECONDS
            ):
                heartbeat_at = migration_now
        connection.execute(
            "UPDATE tasks SET heartbeat_at = ?, expires_at = ? WHERE id = ?",
            (heartbeat_at, expires_at, task["id"]),
        )


def _remap_legacy_workspace_ids(connection: sqlite3.Connection, database: Path) -> None:
    """Remap case-folded legacy workspace IDs before schema 3 validation."""

    rows = connection.execute("SELECT id, root FROM workspaces ORDER BY id").fetchall()
    mappings: list[tuple[str, str]] = []
    existing_ids = {str(row["id"]) for row in rows}
    target_ids: dict[str, str] = {}
    for row in rows:
        legacy_id = str(row["id"])
        root = row["root"]
        if not isinstance(root, str):
            raise StateError(
                "Legacy workspace identity remapping requires text roots.",
                details={
                    "path": str(database.resolve()),
                    "reason": "legacy-workspace-id-remap-invalid-root",
                },
            )
        current_id = _current_workspace_id(root)
        previous = target_ids.get(current_id)
        if previous is not None and previous != legacy_id:
            raise StateError(
                "Legacy workspace identities would collide after migration.",
                details={
                    "path": str(database.resolve()),
                    "reason": "legacy-workspace-id-collision",
                    "workspace_id": current_id,
                },
            )
        target_ids[current_id] = legacy_id
        if legacy_id != current_id:
            mappings.append((legacy_id, current_id))

    for legacy_id, current_id in mappings:
        if current_id in existing_ids:
            raise StateError(
                "Legacy workspace identity remapping would overwrite an existing workspace.",
                details={
                    "path": str(database.resolve()),
                    "reason": "legacy-workspace-id-collision",
                    "workspace_id": current_id,
                },
            )

    if not mappings:
        return

    connection.execute("PRAGMA defer_foreign_keys = ON")
    for legacy_id, current_id in mappings:
        connection.execute(
            "UPDATE workspaces SET id = ? WHERE id = ?",
            (current_id, legacy_id),
        )
        for table in ("tasks", "claims", "recovery_events"):
            connection.execute(
                f"UPDATE {table} SET workspace_id = ? WHERE workspace_id = ?",
                (current_id, legacy_id),
            )

    foreign_key_rows = connection.execute("PRAGMA foreign_key_check").fetchall()
    integrity_rows = [str(row[0]) for row in connection.execute("PRAGMA integrity_check")]
    if foreign_key_rows or integrity_rows != ["ok"]:
        raise StateError(
            "Legacy workspace identity remapping failed its integrity proof.",
            details={
                "path": str(database.resolve()),
                "reason": "legacy-workspace-id-remap-integrity-failed",
                "foreign_key_violation_count": len(foreign_key_rows),
                "integrity_results": integrity_rows,
            },
        )


def _migrate_schema_one_to_three(connection: sqlite3.Connection, database: Path) -> None:
    _validate_schema_one_migration_source(connection, database)
    _remap_legacy_workspace_ids(connection, database)
    unsafe_claim_rows = connection.execute(
        "SELECT state, COUNT(*) AS count FROM claims "
        "WHERE state IN ('queued', 'parked') GROUP BY state ORDER BY state"
    ).fetchall()
    if unsafe_claim_rows:
        raise UsageError(
            "Schema 1 contains queued or parked claims whose restoration lineage cannot be "
            "proven. Stop all scheduler entry, finish or cancel those claims with version 1.2, "
            "verify zero scheduler processes, and retry migration.",
            details={
                "reason": "schema-one-open-claim-migration-blocked",
                "claim_states": {str(row["state"]): int(row["count"]) for row in unsafe_claim_rows},
            },
        )
    _normalize_legacy_open_task_times(connection)
    connection.execute(
        "DELETE FROM claim_scopes WHERE scope_type = 'parked_for' "
        "AND claim_id IN (SELECT id FROM claims WHERE state IN ('released', 'cancelled'))"
    )
    columns = {
        row["name"] for row in connection.execute("PRAGMA table_info(workspaces)").fetchall()
    }
    if "next_queue_order" in columns:
        raise UsageError("Scheduler schema 1 contains a partial queue-counter migration.")
    connection.execute(
        "ALTER TABLE workspaces ADD COLUMN next_queue_order INTEGER NOT NULL DEFAULT 1"
    )
    for statement in _SCHEMA_TWO_INDEX_STATEMENTS:
        connection.execute(statement)
    connection.execute(
        "UPDATE workspaces SET next_queue_order = "
        "(SELECT COALESCE(MAX(queue_order), 0) + 1 FROM claims "
        "WHERE claims.workspace_id = workspaces.id)"
    )
    _install_schema_three_extensions(connection)
    connection.execute(
        "UPDATE scheduler_meta SET value = ? WHERE key = 'schema_version'",
        (str(SCHEMA_VERSION),),
    )
    _validate_schema_three(connection, database)


def _migrate_schema_two_to_three(connection: sqlite3.Connection, database: Path) -> None:
    _validate_schema_version_source(
        connection,
        database,
        schema_version=LEGACY_SCHEMA_TWO_VERSION,
    )
    _remap_legacy_workspace_ids(connection, database)
    _normalize_legacy_open_task_times(connection)
    _install_schema_three_extensions(connection)
    connection.execute(
        "UPDATE scheduler_meta SET value = ? WHERE key = 'schema_version'",
        (str(SCHEMA_VERSION),),
    )
    _validate_schema_three(connection, database)


def _safe_acl_diagnostic(stderr: str, path: Path, identity: str) -> str:
    sanitized = re.sub(re.escape(str(path)), "<token-file>", stderr, flags=re.IGNORECASE)
    sanitized = re.sub(re.escape(identity), "<current-user>", sanitized, flags=re.IGNORECASE)
    sanitized = " ".join(sanitized.split())
    return sanitized[:200] or "no diagnostic"


def _trusted_icacls_executable() -> Path:
    if os.name != "nt":
        raise OSError("The trusted Windows system directory is unavailable on this platform.")
    buffer = ctypes.create_unicode_buffer(32768)
    length = ctypes.windll.kernel32.GetSystemDirectoryW(buffer, len(buffer))
    if length <= 0 or length >= len(buffer):
        raise OSError("The trusted Windows system directory is unavailable.")
    executable = (Path(buffer.value) / "icacls.exe").resolve(strict=True)
    if not executable.is_absolute() or not executable.is_file():
        raise OSError("The trusted Windows icacls executable is unavailable.")
    return executable


def _run_icacls_readonly(path: Path, identity: str, *arguments: str) -> subprocess.CompletedProcess:
    try:
        inspected = subprocess.run(
            [str(_trusted_icacls_executable()), str(path), *arguments],
            check=False,
            capture_output=True,
            text=True,
            creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0),
            timeout=_ACL_COMMAND_TIMEOUT_SECONDS,
        )
    except subprocess.TimeoutExpired as exc:
        raise OSError(
            f"Windows token ACL verification timed out after "
            f"{int(_ACL_COMMAND_TIMEOUT_SECONDS)} seconds."
        ) from exc
    if inspected.returncode != 0:
        diagnostic = _safe_acl_diagnostic(inspected.stderr, path, identity)
        raise OSError(
            f"Windows token ACL verification failed "
            f"(icacls exit {inspected.returncode}: {diagnostic})."
        )
    return inspected


def _validate_windows_token_location(path: Path) -> None:
    try:
        user_temp = Path(tempfile.gettempdir()).resolve(strict=True)
        path.resolve(strict=False).relative_to(user_temp)
    except (OSError, RuntimeError, ValueError) as exc:
        raise OSError(
            "Windows task token files must be inside the current-user temporary directory."
        ) from exc


def _windows_sid_string(sid: ctypes.c_void_p) -> str:
    from ctypes import wintypes

    if not sid:
        raise OSError("Windows security metadata contains a null SID.")
    advapi32 = ctypes.WinDLL("advapi32", use_last_error=True)
    kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
    converted = wintypes.LPWSTR()
    advapi32.ConvertSidToStringSidW.argtypes = [ctypes.c_void_p, ctypes.POINTER(wintypes.LPWSTR)]
    advapi32.ConvertSidToStringSidW.restype = wintypes.BOOL
    kernel32.LocalFree.argtypes = [ctypes.c_void_p]
    kernel32.LocalFree.restype = ctypes.c_void_p
    if not advapi32.ConvertSidToStringSidW(sid, ctypes.byref(converted)):
        raise ctypes.WinError(ctypes.get_last_error())
    try:
        value = converted.value
        if not value:
            raise OSError("Windows security metadata contains an empty SID.")
        return value.upper()
    finally:
        kernel32.LocalFree(ctypes.cast(converted, ctypes.c_void_p))


def _current_windows_user_sid() -> str:
    from ctypes import wintypes

    class _SidAndAttributes(ctypes.Structure):
        _fields_ = [("sid", ctypes.c_void_p), ("attributes", wintypes.DWORD)]

    class _TokenUser(ctypes.Structure):
        _fields_ = [("user", _SidAndAttributes)]

    advapi32 = ctypes.WinDLL("advapi32", use_last_error=True)
    kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
    advapi32.OpenProcessToken.argtypes = [
        wintypes.HANDLE,
        wintypes.DWORD,
        ctypes.POINTER(wintypes.HANDLE),
    ]
    advapi32.OpenProcessToken.restype = wintypes.BOOL
    advapi32.GetTokenInformation.argtypes = [
        wintypes.HANDLE,
        ctypes.c_int,
        ctypes.c_void_p,
        wintypes.DWORD,
        ctypes.POINTER(wintypes.DWORD),
    ]
    advapi32.GetTokenInformation.restype = wintypes.BOOL
    kernel32.GetCurrentProcess.argtypes = []
    kernel32.GetCurrentProcess.restype = wintypes.HANDLE
    kernel32.CloseHandle.argtypes = [wintypes.HANDLE]
    kernel32.CloseHandle.restype = wintypes.BOOL

    token = wintypes.HANDLE()
    if not advapi32.OpenProcessToken(kernel32.GetCurrentProcess(), 0x0008, ctypes.byref(token)):
        raise ctypes.WinError(ctypes.get_last_error())
    try:
        required = wintypes.DWORD()
        advapi32.GetTokenInformation(token, 1, None, 0, ctypes.byref(required))
        if not required.value:
            raise ctypes.WinError(ctypes.get_last_error())
        buffer = ctypes.create_string_buffer(required.value)
        if not advapi32.GetTokenInformation(
            token,
            1,
            buffer,
            required.value,
            ctypes.byref(required),
        ):
            raise ctypes.WinError(ctypes.get_last_error())
        token_user = ctypes.cast(buffer, ctypes.POINTER(_TokenUser)).contents
        return _windows_sid_string(token_user.user.sid)
    finally:
        kernel32.CloseHandle(token)


def _windows_token_acl_snapshot(descriptor: int) -> tuple[str, str, list[tuple[int, int, str]]]:
    import msvcrt
    from ctypes import wintypes

    class _Acl(ctypes.Structure):
        _fields_ = [
            ("revision", ctypes.c_ubyte),
            ("reserved_one", ctypes.c_ubyte),
            ("size", wintypes.WORD),
            ("ace_count", wintypes.WORD),
            ("reserved_two", wintypes.WORD),
        ]

    class _AceHeader(ctypes.Structure):
        _fields_ = [
            ("ace_type", ctypes.c_ubyte),
            ("ace_flags", ctypes.c_ubyte),
            ("ace_size", wintypes.WORD),
        ]

    advapi32 = ctypes.WinDLL("advapi32", use_last_error=True)
    kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
    advapi32.GetSecurityInfo.argtypes = [
        wintypes.HANDLE,
        ctypes.c_int,
        wintypes.DWORD,
        ctypes.POINTER(ctypes.c_void_p),
        ctypes.POINTER(ctypes.c_void_p),
        ctypes.POINTER(ctypes.c_void_p),
        ctypes.POINTER(ctypes.c_void_p),
        ctypes.POINTER(ctypes.c_void_p),
    ]
    advapi32.GetSecurityInfo.restype = wintypes.DWORD
    advapi32.GetAce.argtypes = [
        ctypes.c_void_p,
        wintypes.DWORD,
        ctypes.POINTER(ctypes.c_void_p),
    ]
    advapi32.GetAce.restype = wintypes.BOOL
    kernel32.LocalFree.argtypes = [ctypes.c_void_p]
    kernel32.LocalFree.restype = ctypes.c_void_p

    handle_value = msvcrt.get_osfhandle(descriptor)
    if handle_value == -1:
        raise OSError("Windows token descriptor is invalid.")
    owner = ctypes.c_void_p()
    dacl = ctypes.c_void_p()
    security_descriptor = ctypes.c_void_p()
    result = advapi32.GetSecurityInfo(
        wintypes.HANDLE(handle_value),
        1,
        0x00000001 | 0x00000004,
        ctypes.byref(owner),
        None,
        ctypes.byref(dacl),
        None,
        ctypes.byref(security_descriptor),
    )
    if result != 0:
        raise OSError(result, "Windows token security inspection failed.")
    try:
        if not dacl:
            raise OSError("Windows token file has a null DACL.")
        owner_sid = _windows_sid_string(owner)
        current_sid = _current_windows_user_sid()
        acl = ctypes.cast(dacl, ctypes.POINTER(_Acl)).contents
        entries: list[tuple[int, int, str]] = []
        for index in range(acl.ace_count):
            ace = ctypes.c_void_p()
            if not advapi32.GetAce(dacl, index, ctypes.byref(ace)):
                raise ctypes.WinError(ctypes.get_last_error())
            header = ctypes.cast(ace, ctypes.POINTER(_AceHeader)).contents
            ace_type = int(header.ace_type)
            if ace_type == _WINDOWS_ACCESS_ALLOWED_ACE_TYPE:
                if header.ace_size < 12:
                    raise OSError("Windows token DACL contains a truncated allow entry.")
                address = int(ace.value)
                mask = ctypes.c_uint32.from_address(address + 4).value
                sid = _windows_sid_string(ctypes.c_void_p(address + 8))
                entries.append((ace_type, mask, sid))
            else:
                entries.append((ace_type, 0, ""))
        return owner_sid, current_sid, entries
    finally:
        if security_descriptor:
            kernel32.LocalFree(security_descriptor)


def _verify_windows_token_acl(descriptor: int) -> None:
    owner_sid, current_sid, entries = _windows_token_acl_snapshot(descriptor)
    owner_sid = owner_sid.upper()
    current_sid = current_sid.upper()
    if owner_sid not in {current_sid, *_WINDOWS_ALLOWED_TOKEN_SIDS}:
        raise OSError("Windows token file is not owned by the current identity.")
    allowed_sids = set(_WINDOWS_ALLOWED_TOKEN_SIDS)
    allowed_sids.add(current_sid)
    current_user_can_read = False
    for ace_type, mask, sid in entries:
        if ace_type in _WINDOWS_ACCESS_DENIED_ACE_TYPES:
            continue
        if ace_type != _WINDOWS_ACCESS_ALLOWED_ACE_TYPE:
            raise OSError("Windows token DACL contains an unsupported access-control entry.")
        if sid == _WINDOWS_OWNER_RIGHTS_SID:
            current_user_can_read |= bool(mask & _WINDOWS_FILE_READ_DATA)
            continue
        if sid not in allowed_sids:
            raise OSError("Windows token DACL grants an unapproved principal.")
        if sid == current_sid:
            current_user_can_read |= bool(mask & _WINDOWS_FILE_READ_DATA)
    if not current_user_can_read:
        raise OSError("Windows token DACL does not grant read access to the current identity.")


def _windows_maintenance_acl_snapshot(
    path: Path,
) -> tuple[str, str, list[tuple[int, int, str]]]:
    """Read a named file/ directory owner and DACL without changing its ACL."""

    from ctypes import wintypes

    class _Acl(ctypes.Structure):
        _fields_ = [
            ("revision", ctypes.c_ubyte),
            ("reserved_one", ctypes.c_ubyte),
            ("size", wintypes.WORD),
            ("ace_count", wintypes.WORD),
            ("reserved_two", wintypes.WORD),
        ]

    class _AceHeader(ctypes.Structure):
        _fields_ = [
            ("ace_type", ctypes.c_ubyte),
            ("ace_flags", ctypes.c_ubyte),
            ("ace_size", wintypes.WORD),
        ]

    advapi32 = ctypes.WinDLL("advapi32", use_last_error=True)
    kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
    advapi32.GetNamedSecurityInfoW.argtypes = [
        wintypes.LPWSTR,
        ctypes.c_int,
        wintypes.DWORD,
        ctypes.POINTER(ctypes.c_void_p),
        ctypes.POINTER(ctypes.c_void_p),
        ctypes.POINTER(ctypes.c_void_p),
        ctypes.POINTER(ctypes.c_void_p),
        ctypes.POINTER(ctypes.c_void_p),
    ]
    advapi32.GetNamedSecurityInfoW.restype = wintypes.DWORD
    advapi32.GetAce.argtypes = [
        ctypes.c_void_p,
        wintypes.DWORD,
        ctypes.POINTER(ctypes.c_void_p),
    ]
    advapi32.GetAce.restype = wintypes.BOOL
    kernel32.LocalFree.argtypes = [ctypes.c_void_p]
    kernel32.LocalFree.restype = ctypes.c_void_p

    owner = ctypes.c_void_p()
    dacl = ctypes.c_void_p()
    security_descriptor = ctypes.c_void_p()
    result = advapi32.GetNamedSecurityInfoW(
        str(path),
        1,
        0x00000001 | 0x00000004,
        ctypes.byref(owner),
        None,
        ctypes.byref(dacl),
        None,
        ctypes.byref(security_descriptor),
    )
    if result != 0:
        raise OSError(result, "Windows maintenance security inspection failed.")
    try:
        if not owner:
            raise OSError("Windows maintenance ACL has no owner.")
        if not dacl:
            raise OSError("Windows maintenance ACL has a null DACL.")
        owner_sid = _windows_sid_string(owner)
        current_sid = _current_windows_user_sid()
        acl = ctypes.cast(dacl, ctypes.POINTER(_Acl)).contents
        entries: list[tuple[int, int, str]] = []
        for index in range(acl.ace_count):
            ace = ctypes.c_void_p()
            if not advapi32.GetAce(dacl, index, ctypes.byref(ace)):
                raise ctypes.WinError(ctypes.get_last_error())
            header = ctypes.cast(ace, ctypes.POINTER(_AceHeader)).contents
            ace_type = int(header.ace_type)
            if ace_type == _WINDOWS_ACCESS_ALLOWED_ACE_TYPE:
                if header.ace_size < 12:
                    raise OSError("Windows maintenance ACL contains a truncated allow entry.")
                address = int(ace.value)
                mask = ctypes.c_uint32.from_address(address + 4).value
                sid = _windows_sid_string(ctypes.c_void_p(address + 8))
                entries.append((ace_type, mask, sid))
            else:
                entries.append((ace_type, 0, ""))
        return owner_sid, current_sid, entries
    finally:
        if security_descriptor:
            kernel32.LocalFree(security_descriptor)


def _verify_windows_maintenance_acl(path: Path) -> None:
    owner_sid, current_sid, entries = _windows_maintenance_acl_snapshot(path)
    owner_sid = owner_sid.upper()
    current_sid = current_sid.upper()
    if owner_sid not in {current_sid, *_WINDOWS_ALLOWED_TOKEN_SIDS}:
        raise OSError("Windows maintenance path is not owned by the current identity.")
    allowed_sids = {
        current_sid,
        *_WINDOWS_ALLOWED_TOKEN_SIDS,
        _WINDOWS_OWNER_RIGHTS_SID,
    }
    current_user_can_read = False
    for ace_type, mask, sid in entries:
        sid = sid.upper()
        if ace_type != _WINDOWS_ACCESS_ALLOWED_ACE_TYPE:
            raise OSError("Windows maintenance ACL contains a deny or unsupported entry.")
        if sid not in allowed_sids:
            raise OSError("Windows maintenance ACL grants an unapproved principal.")
        if sid in {current_sid, _WINDOWS_OWNER_RIGHTS_SID}:
            current_user_can_read |= bool(mask & _WINDOWS_FILE_READ_DATA)
    if not current_user_can_read:
        raise OSError("Windows maintenance ACL does not grant read access to the current identity.")


def _same_regular_file(descriptor: int, path: Path) -> bool:
    descriptor_metadata = os.fstat(descriptor)
    path_metadata = path.lstat()
    return (
        stat.S_ISREG(descriptor_metadata.st_mode)
        and stat.S_ISREG(path_metadata.st_mode)
        and descriptor_metadata.st_dev == path_metadata.st_dev
        and descriptor_metadata.st_ino == path_metadata.st_ino
    )


def _validate_windows_token_descriptor(descriptor: int) -> os.stat_result:
    metadata = os.fstat(descriptor)
    if not stat.S_ISREG(metadata.st_mode):
        raise OSError("Task token is not a regular file.")
    if metadata.st_nlink != 1:
        raise OSError("Task token must not have hard-link aliases.")
    return metadata


def _open_validated_posix_token_parent(path: Path) -> tuple[int, Path, str]:
    expanded = path.expanduser()
    if expanded.is_symlink():
        raise OSError("Symbolic links are not allowed.")
    parent = expanded.parent.resolve(strict=True)
    name = expanded.name
    if not name or name in {".", ".."}:
        raise OSError("Task token filename is invalid.")
    flags = os.O_RDONLY | getattr(os, "O_DIRECTORY", 0) | getattr(os, "O_CLOEXEC", 0)
    parent_descriptor = os.open(parent, flags)
    try:
        metadata = os.fstat(parent_descriptor)
        if not stat.S_ISDIR(metadata.st_mode):
            raise OSError("Task token parent is not a directory.")
        if metadata.st_uid != os.geteuid():
            raise OSError("Task token parent is not owned by the current user.")
        if stat.S_IMODE(metadata.st_mode) & 0o022:
            raise OSError("Task token parent is writable by another user or group.")
        return parent_descriptor, parent / name, name
    except Exception:
        os.close(parent_descriptor)
        raise


def _validate_posix_token_descriptor(descriptor: int) -> os.stat_result:
    metadata = os.fstat(descriptor)
    if not stat.S_ISREG(metadata.st_mode):
        raise OSError("Task token is not a regular file.")
    if metadata.st_uid != os.geteuid():
        raise OSError("Task token is not owned by the current user.")
    if stat.S_IMODE(metadata.st_mode) != 0o600:
        raise OSError("Task token mode must be exactly 0600.")
    if metadata.st_nlink != 1:
        raise OSError("Task token must not have hard-link aliases.")
    return metadata


def _open_validated_posix_token(path: Path) -> tuple[int, int, Path, str]:
    parent_descriptor, resolved, name = _open_validated_posix_token_parent(path)
    descriptor: int | None = None
    try:
        flags = os.O_RDONLY | getattr(os, "O_CLOEXEC", 0)
        if hasattr(os, "O_NOFOLLOW"):
            flags |= os.O_NOFOLLOW
        descriptor = os.open(name, flags, dir_fd=parent_descriptor)
        descriptor_metadata = _validate_posix_token_descriptor(descriptor)
        path_metadata = os.stat(name, dir_fd=parent_descriptor, follow_symlinks=False)
        if (
            not stat.S_ISREG(path_metadata.st_mode)
            or descriptor_metadata.st_dev != path_metadata.st_dev
            or descriptor_metadata.st_ino != path_metadata.st_ino
        ):
            raise OSError("Task token path no longer identifies the opened regular file.")
        return descriptor, parent_descriptor, resolved, name
    except Exception:
        if descriptor is not None:
            os.close(descriptor)
        os.close(parent_descriptor)
        raise


def _windows_descriptor_final_path(descriptor: int) -> Path:
    import msvcrt
    from ctypes import wintypes

    kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
    kernel32.GetFinalPathNameByHandleW.argtypes = [
        wintypes.HANDLE,
        wintypes.LPWSTR,
        wintypes.DWORD,
        wintypes.DWORD,
    ]
    kernel32.GetFinalPathNameByHandleW.restype = wintypes.DWORD
    handle_value = msvcrt.get_osfhandle(descriptor)
    if handle_value == -1:
        raise OSError("Windows token descriptor is invalid.")
    size = 32768
    buffer = ctypes.create_unicode_buffer(size)
    length = kernel32.GetFinalPathNameByHandleW(wintypes.HANDLE(handle_value), buffer, size, 0)
    if length <= 0 or length >= size:
        raise ctypes.WinError(ctypes.get_last_error())
    value = buffer.value
    if value.startswith("\\\\?\\UNC\\"):
        value = "\\\\" + value[8:]
    elif value.startswith("\\\\?\\"):
        value = value[4:]
    return Path(value)


def _open_validated_windows_token(path: Path, *, delete_access: bool) -> tuple[int, Path]:
    import msvcrt
    from ctypes import wintypes

    expanded = path.expanduser()
    _validate_windows_token_location(expanded)
    if expanded.is_symlink() or _is_windows_reparse_point(expanded):
        raise OSError("Symbolic links and reparse points are not allowed.")
    resolved = expanded.parent.resolve(strict=True) / expanded.name
    kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
    kernel32.CreateFileW.argtypes = [
        wintypes.LPCWSTR,
        wintypes.DWORD,
        wintypes.DWORD,
        ctypes.c_void_p,
        wintypes.DWORD,
        wintypes.DWORD,
        wintypes.HANDLE,
    ]
    kernel32.CreateFileW.restype = wintypes.HANDLE
    kernel32.CloseHandle.argtypes = [wintypes.HANDLE]
    kernel32.CloseHandle.restype = wintypes.BOOL
    desired_access = 0x80000000 | (0x00010000 if delete_access else 0)
    share_mode = 0 if delete_access else 0x00000001 | 0x00000002 | 0x00000004
    handle = kernel32.CreateFileW(
        str(resolved),
        desired_access,
        share_mode,
        None,
        3,
        0x00200000,
        None,
    )
    invalid_handle = ctypes.c_void_p(-1).value
    if handle == invalid_handle:
        error = ctypes.get_last_error()
        if error in {2, 3}:
            raise _WindowsTokenMissingError(error, "Task token file does not exist.")
        raise ctypes.WinError(error)
    descriptor: int | None = None
    try:
        descriptor = msvcrt.open_osfhandle(int(handle), os.O_RDONLY | getattr(os, "O_BINARY", 0))
        handle = None
        _validate_windows_token_descriptor(descriptor)
        final_path = _windows_descriptor_final_path(descriptor).resolve(strict=True)
        user_temp = Path(tempfile.gettempdir()).resolve(strict=True)
        final_path.relative_to(user_temp)
        if _is_windows_reparse_point(resolved) or not _same_regular_file(descriptor, resolved):
            raise OSError("Task token path no longer identifies the opened regular file.")
        _verify_windows_token_acl(descriptor)
        _validate_windows_token_descriptor(descriptor)
        if _is_windows_reparse_point(resolved) or not _same_regular_file(descriptor, resolved):
            raise OSError("Task token path changed during security verification.")
        return descriptor, final_path
    except (OSError, RuntimeError, ValueError):
        if descriptor is not None:
            os.close(descriptor)
        raise
    finally:
        if handle not in {None, invalid_handle}:
            kernel32.CloseHandle(handle)


def _read_token_descriptor(descriptor: int) -> str:
    os.lseek(descriptor, 0, os.SEEK_SET)
    content = bytearray()
    while len(content) <= _TOKEN_FILE_MAX_BYTES:
        chunk = os.read(descriptor, min(1024, _TOKEN_FILE_MAX_BYTES + 1 - len(content)))
        if not chunk:
            break
        content.extend(chunk)
    if len(content) > _TOKEN_FILE_MAX_BYTES:
        raise OSError("Task token file exceeds the supported size limit.")
    try:
        token = bytes(content).decode("utf-8").strip()
    except UnicodeDecodeError as exc:
        raise OSError("Task token file is not valid UTF-8.") from exc
    if not token:
        raise OSError("Task token file is empty.")
    return token


def _delete_windows_token_descriptor(descriptor: int) -> None:
    import msvcrt
    from ctypes import wintypes

    class _FileDispositionInfo(ctypes.Structure):
        _fields_ = [("delete_file", wintypes.BOOL)]

    kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
    kernel32.SetFileInformationByHandle.argtypes = [
        wintypes.HANDLE,
        ctypes.c_int,
        ctypes.c_void_p,
        wintypes.DWORD,
    ]
    kernel32.SetFileInformationByHandle.restype = wintypes.BOOL
    handle_value = msvcrt.get_osfhandle(descriptor)
    if handle_value == -1:
        raise OSError("Windows token descriptor is invalid.")
    disposition = _FileDispositionInfo(True)
    if not kernel32.SetFileInformationByHandle(
        wintypes.HANDLE(handle_value),
        4,
        ctypes.byref(disposition),
        ctypes.sizeof(disposition),
    ):
        raise ctypes.WinError(ctypes.get_last_error())


def create_token_file(path: Path, token: str) -> Path:
    expanded = path.expanduser()
    if expanded.is_symlink() or _is_windows_reparse_point(expanded):
        raise UsageError("Cannot create task token file: symbolic links are not allowed.")
    if os.name == "nt":
        try:
            _validate_windows_token_location(expanded)
        except OSError as exc:
            raise UsageError(f"Cannot create task token file: {exc}") from exc
    resolved = expanded.parent.resolve() / expanded.name
    created = False
    descriptor: int | None = None
    failure_reason = "token-create-failed"
    cleanup_error: Exception | None = None
    try:
        _ensure_private_directory(resolved.parent, preserve_existing=True)
        if resolved.is_symlink() or _is_windows_reparse_point(resolved):
            raise OSError("Symbolic links are not allowed.")
        flags = os.O_RDWR | os.O_CREAT | os.O_EXCL
        if os.name != "nt" and hasattr(os, "O_NOFOLLOW"):
            flags |= os.O_NOFOLLOW
        descriptor = os.open(resolved, flags, 0o600)
        created = True
        if os.name == "nt":
            _verify_windows_token_acl(descriptor)
            _validate_windows_token_descriptor(descriptor)
        else:
            os.fchmod(descriptor, 0o600)
        if not _same_regular_file(descriptor, resolved):
            raise OSError("Created token path no longer identifies the opened regular file.")
        with os.fdopen(descriptor, "w", encoding="utf-8", newline="\n") as stream:
            descriptor = None
            stream.write(token + "\n")
            stream.flush()
            os.fsync(stream.fileno())
            if os.name == "nt":
                _validate_windows_token_descriptor(stream.fileno())
        try:
            _durable_directory_barrier(resolved.parent)
        except (OSError, RuntimeError):
            failure_reason = "token-create-durable-barrier-failed"
            raise
    except (OSError, subprocess.SubprocessError) as exc:
        if descriptor is not None:
            os.close(descriptor)
        if created:
            try:
                resolved.unlink(missing_ok=True)
            except OSError as unlink_error:
                cleanup_error = unlink_error
            else:
                try:
                    _durable_directory_barrier(resolved.parent)
                except (OSError, RuntimeError) as barrier_error:
                    cleanup_error = barrier_error
        raise UsageError(
            f"Cannot create task token file: {exc}",
            details={
                "reason": failure_reason,
                "recovery_required": cleanup_error is not None,
            },
        ) from exc
    return resolved


def read_token_file_with_path(path: Path) -> tuple[str, Path]:
    if os.name == "nt":
        descriptor: int | None = None
        try:
            descriptor, resolved = _open_validated_windows_token(path, delete_access=False)
            return _read_token_descriptor(descriptor), resolved
        except (OSError, RuntimeError, ValueError) as exc:
            raise UsageError(f"Cannot read task token file: {exc}") from exc
        finally:
            if descriptor is not None:
                os.close(descriptor)
    descriptor: int | None = None
    parent_descriptor: int | None = None
    try:
        descriptor, parent_descriptor, resolved, _ = _open_validated_posix_token(path)
        return _read_token_descriptor(descriptor), resolved
    except (OSError, RuntimeError, ValueError) as exc:
        raise UsageError(f"Cannot read task token file: {exc}") from exc
    finally:
        if descriptor is not None:
            os.close(descriptor)
        if parent_descriptor is not None:
            os.close(parent_descriptor)


def read_token_file(path: Path) -> str:
    token, _ = read_token_file_with_path(path)
    return token


def canonical_token_file_path(path: Path) -> Path:
    """Resolve a caller-selected token path without reading or creating its secret."""

    try:
        expanded = path.expanduser()
        if expanded.is_symlink() or _is_windows_reparse_point(expanded):
            raise OSError("Symbolic links and reparse points are not allowed.")
        if os.name == "nt":
            if os.path.lexists(expanded):
                descriptor, resolved = _open_validated_windows_token(
                    expanded,
                    delete_access=False,
                )
                os.close(descriptor)
                descriptor = None
                return resolved
            _validate_windows_token_location(expanded)
            resolved = expanded.resolve(strict=False)
        else:
            candidate_parent = expanded.parent
            existing_parent = candidate_parent
            while not os.path.lexists(existing_parent):
                next_parent = existing_parent.parent
                if next_parent == existing_parent:
                    raise OSError("Task token path has no existing parent directory.")
                existing_parent = next_parent
            if existing_parent.is_symlink():
                raise OSError("Symbolic links are not allowed in the task token parent path.")
            existing_metadata = existing_parent.lstat()
            if not stat.S_ISDIR(existing_metadata.st_mode):
                raise OSError("Task token parent ancestor is not a directory.")
            if existing_metadata.st_uid != os.geteuid():
                raise OSError("Task token parent ancestor is not owned by the current user.")
            if stat.S_IMODE(existing_metadata.st_mode) & 0o022:
                raise OSError("Task token parent ancestor is writable by another user or group.")
            resolved = candidate_parent.resolve(strict=False) / expanded.name
        if (
            not resolved.is_absolute()
            or _has_control_characters(str(resolved))
            or os.path.normpath(str(resolved)) != str(resolved)
        ):
            raise OSError("Task token path is not canonical.")
        return resolved
    except (OSError, RuntimeError, ValueError) as exc:
        raise UsageError(f"Cannot resolve task token file path: {exc}") from exc


def canonical_missing_token_file_path(path: Path) -> Path:
    """Resolve an absent token path without weakening the normal token location boundary."""

    try:
        resolved = canonical_token_file_path(path)
        if os.path.lexists(resolved):
            raise OSError("Task token file exists and must be authenticated normally.")
        return resolved
    except (OSError, RuntimeError, ValueError, UsageError) as exc:
        raise UsageError(f"Cannot resolve missing task token file: {exc}") from exc


def _durable_token_cleanup_barrier(path: Path) -> None:
    try:
        _durable_directory_barrier(Path(path).expanduser().parent.resolve(strict=True))
    except (OSError, RuntimeError) as exc:
        raise UsageError(
            "Cannot remove task token file: directory metadata durability is uncertain.",
            details={
                "reason": "token-cleanup-durable-barrier-failed",
                "recovery_required": True,
            },
        ) from exc


def _verify_windows_token_absent_after_close(path: Path) -> None:
    """Require a delete-marked token to be absent before flushing its parent."""

    try:
        path.lstat()
    except FileNotFoundError:
        return
    except OSError as exc:
        raise OSError("Cannot verify that the Windows task token was deleted.") from exc
    raise OSError("Windows task token still exists after its delete handle was closed.")


def remove_matching_token_file(path: Path, token: str) -> bool:
    if os.name == "nt":
        descriptor: int | None = None
        try:
            descriptor, resolved = _open_validated_windows_token(path, delete_access=True)
            if _read_token_descriptor(descriptor) != token:
                return False
            _validate_windows_token_descriptor(descriptor)
            _delete_windows_token_descriptor(descriptor)
            os.close(descriptor)
            descriptor = None
            _verify_windows_token_absent_after_close(resolved)
            _durable_token_cleanup_barrier(resolved)
            return True
        except _WindowsTokenMissingError:
            _durable_token_cleanup_barrier(path)
            return True
        except UsageError:
            raise
        except (OSError, RuntimeError, ValueError) as exc:
            raise UsageError(f"Cannot remove task token file: {exc}") from exc
        finally:
            if descriptor is not None:
                os.close(descriptor)
    descriptor: int | None = None
    parent_descriptor: int | None = None
    try:
        descriptor, parent_descriptor, _, name = _open_validated_posix_token(path)
        if _read_token_descriptor(descriptor) != token:
            return False
        descriptor_metadata = _validate_posix_token_descriptor(descriptor)
        path_metadata = os.stat(name, dir_fd=parent_descriptor, follow_symlinks=False)
        if (
            descriptor_metadata.st_dev != path_metadata.st_dev
            or descriptor_metadata.st_ino != path_metadata.st_ino
        ):
            raise OSError("Task token path changed during security verification.")
        os.unlink(name, dir_fd=parent_descriptor)
        _durable_token_cleanup_barrier(path)
        return True
    except FileNotFoundError:
        _durable_token_cleanup_barrier(path)
        return True
    except UsageError:
        raise
    except (OSError, RuntimeError, ValueError) as exc:
        raise UsageError(f"Cannot remove task token file: {exc}") from exc
    finally:
        if descriptor is not None:
            os.close(descriptor)
        if parent_descriptor is not None:
            os.close(parent_descriptor)


def remove_matching_token_hash_file(path: Path, expected_hash: str) -> bool:
    """Verify a token hash and delete through the same validated handle on Windows."""

    if (
        not isinstance(expected_hash, str)
        or len(expected_hash) != 64
        or expected_hash.casefold() != expected_hash
        or any(character not in "0123456789abcdef" for character in expected_hash)
    ):
        raise UsageError("Expected task token hash is malformed.")
    if os.name == "nt":
        descriptor: int | None = None
        try:
            descriptor, resolved = _open_validated_windows_token(path, delete_access=True)
            token_hash = hashlib.sha256(
                _read_token_descriptor(descriptor).encode("utf-8")
            ).hexdigest()
            if token_hash != expected_hash:
                return False
            _validate_windows_token_descriptor(descriptor)
            _delete_windows_token_descriptor(descriptor)
            os.close(descriptor)
            descriptor = None
            _verify_windows_token_absent_after_close(resolved)
            _durable_token_cleanup_barrier(resolved)
            return True
        except _WindowsTokenMissingError:
            _durable_token_cleanup_barrier(path)
            return True
        except UsageError:
            raise
        except (OSError, RuntimeError, ValueError) as exc:
            raise UsageError(f"Cannot remove task token file: {exc}") from exc
        finally:
            if descriptor is not None:
                os.close(descriptor)
    descriptor: int | None = None
    parent_descriptor: int | None = None
    try:
        descriptor, parent_descriptor, _, name = _open_validated_posix_token(path)
        token_hash = hashlib.sha256(_read_token_descriptor(descriptor).encode("utf-8")).hexdigest()
        if token_hash != expected_hash:
            return False
        descriptor_metadata = _validate_posix_token_descriptor(descriptor)
        path_metadata = os.stat(name, dir_fd=parent_descriptor, follow_symlinks=False)
        if (
            descriptor_metadata.st_dev != path_metadata.st_dev
            or descriptor_metadata.st_ino != path_metadata.st_ino
        ):
            raise OSError("Task token path changed during security verification.")
        os.unlink(name, dir_fd=parent_descriptor)
        _durable_token_cleanup_barrier(path)
        return True
    except FileNotFoundError:
        _durable_token_cleanup_barrier(path)
        return True
    except UsageError:
        raise
    except (OSError, RuntimeError, ValueError) as exc:
        raise UsageError(f"Cannot remove task token file: {exc}") from exc
    finally:
        if descriptor is not None:
            os.close(descriptor)
        if parent_descriptor is not None:
            os.close(parent_descriptor)
