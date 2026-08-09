from __future__ import annotations

import getpass
import hashlib
import json
import math
import os
import re
import secrets
import sqlite3
import subprocess
import time
import uuid
from collections.abc import Iterator, Sequence
from contextlib import contextmanager
from dataclasses import dataclass
from pathlib import Path, PurePosixPath
from typing import Any
from urllib.parse import urlparse

from .errors import IncompatibleError, ProjectBusyError, UsageError
from .locking import project_lock
from .project_lease import (
    create_project_lease_unlocked,
    release_project_lease_unlocked,
    require_project_lease,
)
from .service_state import StatePaths, ensure_private_directory

WORKSPACE_SCHEMA_VERSION = 2
POLICY_SCHEMA_MIN = 1
POLICY_SCHEMA_MAX = 1
POLICY_RELATIVE_PATH = Path("Tools/Coordination/workspace-control.json")
TOKEN_ENVIRONMENT_VARIABLE = "UMCP_WORKSPACE_TASK_TOKEN"
POLL_INTERVAL_SECONDS = 0.05
RESOURCE_RANKS = {"vcs-maintenance": 3, "unity-live": 4}
MUTATING_RESOURCE_NAMES = frozenset(RESOURCE_RANKS)
TASK_ACTIVE_STATES = ("active", "outcome_unknown")
CLAIM_OPEN_STATES = ("queued", "granted")
DISPOSITIONS = frozenset(
    {"adopt", "protect", "resolved-clean", "submitted", "legacy-unowned"}
)


@dataclass(frozen=True)
class WorkspacePolicy:
    enforcement: str
    schema_version: int | None
    valid: bool
    path: str
    error: str | None = None
    unity_meta_pairing: bool = True

    @property
    def enabled(self) -> bool:
        return self.enforcement in {"audit", "required"}

    def public_payload(self) -> dict[str, Any]:
        return {
            "enforcement": self.enforcement,
            "schema_version": self.schema_version,
            "valid": self.valid,
            "path": self.path,
            "error": self.error,
            "supported_schema": {
                "minimum": POLICY_SCHEMA_MIN,
                "maximum": POLICY_SCHEMA_MAX,
            },
            "unity_meta_pairing": self.unity_meta_pairing,
        }


def load_workspace_policy(project_root: Path) -> WorkspacePolicy:
    policy_path = project_root / POLICY_RELATIVE_PATH
    if not policy_path.is_file():
        return WorkspacePolicy(
            enforcement="disabled",
            schema_version=None,
            valid=True,
            path=str(policy_path),
            unity_meta_pairing=False,
        )
    try:
        value = json.loads(policy_path.read_text(encoding="utf-8"))
        if not isinstance(value, dict):
            raise TypeError("policy root must be an object")
        schema_version = value["schemaVersion"]
        enforcement = value["enforcement"]
        unity_meta_pairing = value.get("unityMetaPairing", True)
        if isinstance(schema_version, bool) or not isinstance(schema_version, int):
            raise TypeError("schemaVersion must be an integer")
        if not isinstance(enforcement, str):
            raise TypeError("enforcement must be a string")
        if not isinstance(unity_meta_pairing, bool):
            raise TypeError("unityMetaPairing must be a boolean")
    except (KeyError, OSError, TypeError, ValueError) as exc:
        return WorkspacePolicy(
            enforcement="invalid",
            schema_version=None,
            valid=False,
            path=str(policy_path),
            error=f"Invalid workspace policy: {exc}",
        )
    if enforcement not in {"audit", "required"}:
        return WorkspacePolicy(
            enforcement=enforcement,
            schema_version=schema_version,
            valid=False,
            path=str(policy_path),
            error="enforcement must be audit or required",
            unity_meta_pairing=unity_meta_pairing,
        )
    if not POLICY_SCHEMA_MIN <= schema_version <= POLICY_SCHEMA_MAX:
        return WorkspacePolicy(
            enforcement=enforcement,
            schema_version=schema_version,
            valid=False,
            path=str(policy_path),
            error="workspace policy schema is not supported by this tool",
            unity_meta_pairing=unity_meta_pairing,
        )
    return WorkspacePolicy(
        enforcement=enforcement,
        schema_version=schema_version,
        valid=True,
        path=str(policy_path),
        unity_meta_pairing=unity_meta_pairing,
    )


def require_usable_policy(policy: WorkspacePolicy) -> None:
    if policy.enforcement == "required" and not policy.valid:
        raise IncompatibleError(
            "Required workspace policy is invalid or unsupported.",
            details={"policy": policy.public_payload()},
        )


def _validate_duration(value: float, label: str, *, allow_zero: bool = False) -> None:
    if not math.isfinite(value) or value < 0 or (value == 0 and not allow_zero):
        qualifier = "zero or greater" if allow_zero else "greater than zero"
        raise UsageError(f"{label} must be {qualifier}.")


def _token_hash(token: str) -> str:
    return hashlib.sha256(token.encode("utf-8")).hexdigest()


def _public_id(prefix: str) -> str:
    return f"{prefix}-{uuid.uuid4().hex[:16]}"


def _validate_plain_text(value: str, label: str, maximum: int) -> str:
    cleaned = value.strip()
    if not cleaned:
        raise UsageError(f"{label} must not be empty.")
    if len(cleaned) > maximum or any(ord(character) < 32 for character in cleaned):
        raise UsageError(f"{label} is too long or contains control characters.")
    return cleaned


def _normalize_scope(project_root: Path, value: str) -> str:
    raw = value.strip().replace("\\", "/")
    if not raw:
        raise UsageError("Workspace path must not be empty.")
    path = Path(raw)
    candidate = path if path.is_absolute() else project_root / path
    try:
        relative = candidate.resolve(strict=False).relative_to(project_root.resolve())
    except ValueError as exc:
        raise UsageError(f"Workspace path escapes the project root: {value}") from exc
    raw = relative.as_posix()
    normalized = PurePosixPath(raw)
    if normalized.is_absolute() or ".." in normalized.parts:
        raise UsageError(f"Workspace path escapes the project root: {value}")
    result = normalized.as_posix().rstrip("/")
    result = result.removeprefix("./")
    if not result or result == ".":
        raise UsageError("The complete project root cannot be claimed as a path.")
    return result.casefold() if os.name == "nt" else result


def _expand_write_scopes(
    project_root: Path,
    values: Sequence[str],
    *,
    unity_meta_pairing: bool,
) -> tuple[str, ...]:
    scopes: set[str] = set()
    for value in values:
        normalized = _normalize_scope(project_root, value)
        scopes.add(normalized)
        if not unity_meta_pairing or not normalized.casefold().startswith("assets/"):
            continue
        if normalized.casefold().endswith(".meta"):
            base = normalized[:-5]
            if base:
                scopes.add(base)
        else:
            scopes.add(f"{normalized}.meta")
    return tuple(sorted(scopes))


def _path_overlaps(left: str, right: str) -> bool:
    left_parts = PurePosixPath(left).parts
    right_parts = PurePosixPath(right).parts
    shared = min(len(left_parts), len(right_parts))
    return left_parts[:shared] == right_parts[:shared]


def _path_covers(container: str, candidate: str) -> bool:
    container_parts = PurePosixPath(container).parts
    candidate_parts = PurePosixPath(candidate).parts
    return (
        len(container_parts) <= len(candidate_parts)
        and container_parts == candidate_parts[: len(container_parts)]
    )


def _scope_payload(rows: Sequence[sqlite3.Row]) -> dict[str, list[str]]:
    result = {"write": [], "resource": []}
    for row in rows:
        result[str(row["scope_kind"])].append(str(row["scope"]))
    return result


class WorkspaceCoordinator:
    def __init__(
        self,
        paths: StatePaths,
        project_root: Path,
        canonical_project_root: str,
        *,
        lease_ttl_seconds: float,
    ) -> None:
        self.paths = paths
        self.project_root = project_root.resolve()
        self.canonical_project_root = canonical_project_root
        self.lease_ttl_seconds = lease_ttl_seconds
        self.policy = load_workspace_policy(self.project_root)
        require_usable_policy(self.policy)
        self.paths.ensure()
        ensure_private_directory(self.paths.workspace_control.parent)
        try:
            self._initialize()
        except sqlite3.DatabaseError as exc:
            raise IncompatibleError(
                "Workspace state database is unreadable or incompatible.",
                details={
                    "database": str(self.paths.workspace_control),
                    "backup_source": str(self.paths.workspace_control),
                    "reason": str(exc),
                },
            ) from exc

    def _connect(self) -> sqlite3.Connection:
        connection = sqlite3.connect(
            self.paths.workspace_control,
            timeout=30,
            isolation_level=None,
        )
        connection.row_factory = sqlite3.Row
        connection.execute("PRAGMA foreign_keys = ON")
        connection.execute("PRAGMA busy_timeout = 30000")
        return connection

    def _initialize(self) -> None:
        with self._connect() as connection:
            connection.executescript(
                """
                PRAGMA journal_mode = WAL;
                CREATE TABLE IF NOT EXISTS workspace_meta (
                    key TEXT PRIMARY KEY,
                    value TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS projects (
                    project_root TEXT PRIMARY KEY,
                    epoch INTEGER NOT NULL DEFAULT 1,
                    created_at REAL NOT NULL,
                    updated_at REAL NOT NULL
                );
                CREATE TABLE IF NOT EXISTS tasks (
                    task_id TEXT PRIMARY KEY,
                    project_root TEXT NOT NULL,
                    token_hash TEXT NOT NULL UNIQUE,
                    owner TEXT NOT NULL,
                    summary TEXT NOT NULL,
                    task_uri TEXT,
                    phase TEXT NOT NULL,
                    note TEXT,
                    state TEXT NOT NULL,
                    epoch INTEGER NOT NULL,
                    created_at REAL NOT NULL,
                    heartbeat_at REAL NOT NULL,
                    expires_at REAL NOT NULL,
                    ended_at REAL
                );
                CREATE INDEX IF NOT EXISTS tasks_project_state
                    ON tasks(project_root, state);
                CREATE TABLE IF NOT EXISTS claims (
                    claim_id TEXT PRIMARY KEY,
                    project_root TEXT NOT NULL,
                    task_id TEXT NOT NULL REFERENCES tasks(task_id),
                    kind TEXT NOT NULL,
                    state TEXT NOT NULL,
                    queue_order INTEGER NOT NULL,
                    created_at REAL NOT NULL,
                    granted_at REAL,
                    released_at REAL,
                    epoch INTEGER,
                    blocker_json TEXT,
                    legacy_lease_id TEXT
                );
                CREATE INDEX IF NOT EXISTS claims_project_state_queue
                    ON claims(project_root, state, queue_order);
                CREATE TABLE IF NOT EXISTS claim_scopes (
                    claim_id TEXT NOT NULL REFERENCES claims(claim_id)
                        ON DELETE CASCADE,
                    scope_kind TEXT NOT NULL,
                    scope TEXT NOT NULL,
                    PRIMARY KEY(claim_id, scope_kind, scope)
                );
                CREATE TABLE IF NOT EXISTS vcs_observations (
                    observation_id TEXT PRIMARY KEY,
                    project_root TEXT NOT NULL,
                    observed_at REAL NOT NULL,
                    command TEXT NOT NULL,
                    pending_count INTEGER NOT NULL
                );
                CREATE INDEX IF NOT EXISTS vcs_observations_project_time
                    ON vcs_observations(project_root, observed_at DESC);
                CREATE TABLE IF NOT EXISTS vcs_pending (
                    project_root TEXT NOT NULL,
                    path TEXT NOT NULL,
                    status TEXT NOT NULL,
                    observation_id TEXT NOT NULL,
                    PRIMARY KEY(project_root, path)
                );
                CREATE TABLE IF NOT EXISTS vcs_dispositions (
                    project_root TEXT NOT NULL,
                    path TEXT NOT NULL,
                    kind TEXT NOT NULL,
                    task_id TEXT,
                    evidence TEXT,
                    updated_at REAL NOT NULL,
                    PRIMARY KEY(project_root, path)
                );
                CREATE TABLE IF NOT EXISTS recovery_events (
                    recovery_id TEXT PRIMARY KEY,
                    project_root TEXT NOT NULL,
                    task_id TEXT NOT NULL,
                    disposition TEXT NOT NULL,
                    evidence TEXT NOT NULL,
                    approved_by TEXT NOT NULL,
                    created_at REAL NOT NULL,
                    new_epoch INTEGER NOT NULL
                );
                """
            )
            version = connection.execute(
                "SELECT value FROM workspace_meta WHERE key = 'schema_version'"
            ).fetchone()
            if version is None:
                connection.execute(
                    "INSERT INTO workspace_meta(key, value) VALUES(?, ?)",
                    ("schema_version", str(WORKSPACE_SCHEMA_VERSION)),
                )
            else:
                try:
                    current_version = int(version["value"])
                except (TypeError, ValueError) as exc:
                    raise IncompatibleError(
                        "Workspace state schema version is invalid.",
                        details={"schema_version": version["value"]},
                    ) from exc
            if version is not None and current_version == 1:
                columns = {
                    str(row["name"])
                    for row in connection.execute(
                        "PRAGMA table_info(vcs_dispositions)"
                    ).fetchall()
                }
                if "task_id" not in columns:
                    connection.execute(
                        "ALTER TABLE vcs_dispositions ADD COLUMN task_id TEXT"
                    )
                connection.execute(
                    "UPDATE workspace_meta SET value = ? WHERE key = 'schema_version'",
                    (str(WORKSPACE_SCHEMA_VERSION),),
                )
                connection.execute(
                    """
                    UPDATE vcs_dispositions
                    SET kind = 'protect', task_id = NULL,
                        evidence = COALESCE(
                            evidence,
                            'legacy adoption owner unavailable during schema migration'
                        )
                    WHERE kind = 'adopt' AND task_id IS NULL
                    """
                )
            elif version is not None and current_version != WORKSPACE_SCHEMA_VERSION:
                raise IncompatibleError(
                    "Workspace state schema is not supported by this tool.",
                    details={"schema_version": current_version},
                )
            now = time.time()
            connection.execute(
                """
                INSERT INTO projects(project_root, epoch, created_at, updated_at)
                VALUES(?, 1, ?, ?)
                ON CONFLICT(project_root) DO NOTHING
                """,
                (self.canonical_project_root, now, now),
            )
            self._validate_records(connection)
        if os.name != "nt":
            self.paths.workspace_control.chmod(0o600)

    def _validate_records(self, connection: sqlite3.Connection) -> None:
        checks = (
            (
                "tasks",
                "task_id",
                """
                SELECT task_id FROM tasks
                WHERE project_root = ? AND (
                    state NOT IN (
                        'active', 'queued', 'outcome_unknown', 'orphaned_unknown',
                        'completed', 'failed', 'expired'
                    ) OR epoch < 1
                ) LIMIT 1
                """,
            ),
            (
                "claims",
                "claim_id",
                """
                SELECT c.claim_id FROM claims c
                JOIN tasks t ON t.task_id = c.task_id
                WHERE c.project_root = ? AND (
                    c.kind NOT IN ('mutation', 'freeze')
                    OR c.state NOT IN ('queued', 'granted', 'released', 'cancelled')
                    OR c.project_root <> t.project_root
                ) LIMIT 1
                """,
            ),
            (
                "claim_scopes",
                "claim_id",
                """
                SELECT s.claim_id FROM claim_scopes s
                JOIN claims c ON c.claim_id = s.claim_id
                WHERE c.project_root = ? AND (
                    s.scope_kind NOT IN ('write', 'resource')
                    OR (s.scope_kind = 'resource'
                        AND s.scope NOT IN ('vcs-maintenance', 'unity-live'))
                    OR (c.kind = 'freeze')
                ) LIMIT 1
                """,
            ),
            (
                "vcs_dispositions",
                "path",
                """
                SELECT d.path FROM vcs_dispositions d
                LEFT JOIN tasks t ON t.task_id = d.task_id
                WHERE d.project_root = ? AND (
                    d.kind NOT IN (
                        'adopt', 'protect', 'resolved-clean', 'submitted',
                        'legacy-unowned'
                    )
                    OR (d.kind = 'adopt' AND (
                        d.task_id IS NULL OR t.task_id IS NULL
                        OR t.project_root <> d.project_root
                        OR t.state NOT IN (
                            'active', 'outcome_unknown', 'orphaned_unknown'
                        )
                    ))
                    OR (d.kind <> 'adopt' AND d.task_id IS NOT NULL)
                ) LIMIT 1
                """,
            ),
        )
        for table, identifier, query in checks:
            invalid = connection.execute(
                query, (self.canonical_project_root,)
            ).fetchone()
            if invalid is not None:
                raise IncompatibleError(
                    "Workspace state contains an invalid coordination record.",
                    details={
                        "reason": "invalid-record",
                        "table": table,
                        "record": invalid[identifier],
                    },
                )

        empty_mutation = connection.execute(
            """
            SELECT c.claim_id FROM claims c
            WHERE c.project_root = ? AND c.kind = 'mutation'
                AND NOT EXISTS (
                    SELECT 1 FROM claim_scopes s WHERE s.claim_id = c.claim_id
                )
            LIMIT 1
            """,
            (self.canonical_project_root,),
        ).fetchone()
        if empty_mutation is not None:
            raise IncompatibleError(
                "Workspace state contains an invalid coordination record.",
                details={
                    "reason": "invalid-record",
                    "table": "claims",
                    "record": empty_mutation["claim_id"],
                },
            )

    @contextmanager
    def _transaction(self) -> Iterator[sqlite3.Connection]:
        connection = self._connect()
        try:
            connection.execute("BEGIN IMMEDIATE")
            yield connection
            connection.execute("COMMIT")
        except Exception:
            if connection.in_transaction:
                connection.execute("ROLLBACK")
            raise
        finally:
            connection.close()

    def _epoch(self, connection: sqlite3.Connection) -> int:
        row = connection.execute(
            "SELECT epoch FROM projects WHERE project_root = ?",
            (self.canonical_project_root,),
        ).fetchone()
        if row is None:
            raise IncompatibleError("Workspace project state is missing.")
        return int(row["epoch"])

    def _cleanup(self, connection: sqlite3.Connection, now: float) -> None:
        expired = connection.execute(
            """
            SELECT task_id, state FROM tasks
            WHERE project_root = ? AND state IN ('active', 'outcome_unknown')
                AND expires_at <= ?
            """,
            (self.canonical_project_root, now),
        ).fetchall()
        for task in expired:
            task_id = str(task["task_id"])
            if task["state"] == "outcome_unknown":
                connection.execute(
                    """
                    UPDATE tasks SET state = 'orphaned_unknown', ended_at = ?
                    WHERE task_id = ?
                    """,
                    (now, task_id),
                )
                connection.execute(
                    """
                    UPDATE claims SET blocker_json = ?
                    WHERE task_id = ? AND state = 'granted'
                    """,
                    (json.dumps({"reason": "orphaned-unknown"}), task_id),
                )
            else:
                connection.execute(
                    "UPDATE tasks SET state = 'expired', ended_at = ? WHERE task_id = ?",
                    (now, task_id),
                )
                connection.execute(
                    """
                    UPDATE vcs_dispositions
                    SET kind = 'protect', task_id = NULL,
                        evidence = COALESCE(evidence, 'adopting task expired'),
                        updated_at = ?
                    WHERE project_root = ? AND kind = 'adopt' AND task_id = ?
                    """,
                    (now, self.canonical_project_root, task_id),
                )
                connection.execute(
                    """
                    UPDATE claims SET state = 'released', released_at = ?
                    WHERE task_id = ? AND state IN ('queued', 'granted')
                    """,
                    (now, task_id),
                )

    def _task_for_token(
        self,
        connection: sqlite3.Connection,
        token: str | None,
        *,
        allow_outcome_unknown: bool = False,
    ) -> sqlite3.Row:
        if not token:
            raise UsageError(
                f"Workspace task token is required via {TOKEN_ENVIRONMENT_VARIABLE}, "
                "token file, or stdin."
            )
        row = connection.execute(
            """
            SELECT * FROM tasks
            WHERE project_root = ? AND token_hash = ?
            """,
            (self.canonical_project_root, _token_hash(token)),
        ).fetchone()
        allowed = {"active"}
        if allow_outcome_unknown:
            allowed.add("outcome_unknown")
        if row is None or row["state"] not in allowed:
            raise UsageError("Workspace task token is invalid, expired, or released.")
        return row

    def start_task(
        self,
        *,
        owner: str,
        summary: str,
        task_uri: str | None,
        ttl_seconds: float,
    ) -> dict[str, Any]:
        owner = _validate_plain_text(owner, "Task owner", 128)
        summary = _validate_plain_text(summary, "Task summary", 512)
        if task_uri:
            if len(task_uri) > 2048:
                raise UsageError("Task URI exceeds its length limit.")
            parsed_uri = urlparse(task_uri)
            if (
                parsed_uri.scheme not in {"codex", "https"}
                or not parsed_uri.netloc
                or parsed_uri.username
                or parsed_uri.password
                or parsed_uri.query
                or parsed_uri.fragment
            ):
                raise UsageError(
                    "Task URI must be a credential-free codex or HTTPS link."
                )
        _validate_duration(ttl_seconds, "Task TTL")
        if (self.project_root / ".plastic").is_dir():
            self.reconcile_plastic()
        now = time.time()
        token = secrets.token_urlsafe(32)
        task_id = _public_id("task")
        with self._transaction() as connection:
            self._cleanup(connection, now)
            epoch = self._epoch(connection)
            connection.execute(
                """
                INSERT INTO tasks(
                    task_id, project_root, token_hash, owner, summary, task_uri,
                    phase, state, epoch, created_at, heartbeat_at, expires_at
                ) VALUES(?, ?, ?, ?, ?, ?, 'starting', 'active', ?, ?, ?, ?)
                """,
                (
                    task_id,
                    self.canonical_project_root,
                    _token_hash(token),
                    owner,
                    summary,
                    task_uri,
                    epoch,
                    now,
                    now,
                    now + ttl_seconds,
                ),
            )
        return {
            "task_id": task_id,
            "task_token": token,
            "owner": owner,
            "summary": summary,
            "task_uri": task_uri,
            "state": "active",
            "phase": "starting",
            "epoch": epoch,
            "created_at": now,
            "heartbeat_at": now,
            "expires_at": now + ttl_seconds,
        }

    def heartbeat(
        self,
        token: str | None,
        *,
        phase: str,
        note: str | None,
        ttl_seconds: float,
    ) -> dict[str, Any]:
        phase = _validate_plain_text(phase, "Task phase", 128)
        if note is not None:
            note = _validate_plain_text(note, "Task note", 1024)
        _validate_duration(ttl_seconds, "Task TTL")
        now = time.time()
        with (
            project_lock(
                self.paths,
                self.canonical_project_root,
                "workspace-heartbeat",
                30,
            ),
            self._transaction() as connection,
        ):
            self._cleanup(connection, now)
            task = self._task_for_token(connection, token, allow_outcome_unknown=True)
            lease_rows = connection.execute(
                """
                SELECT legacy_lease_id FROM claims
                WHERE task_id = ? AND state = 'granted'
                    AND legacy_lease_id IS NOT NULL
                """,
                (task["task_id"],),
            ).fetchall()
            for lease_row in lease_rows:
                renewed = require_project_lease(
                    self.paths,
                    self.canonical_project_root,
                    str(lease_row["legacy_lease_id"]),
                    self.lease_ttl_seconds,
                )
                if renewed is None:
                    raise ProjectBusyError(
                        "The workspace unity-live claim lost its Unity lease binding.",
                        details={"reason": "unity-lease-binding-expired"},
                    )
            state = "outcome_unknown" if phase == "outcome_unknown" else task["state"]
            connection.execute(
                """
                UPDATE tasks SET phase = ?, note = ?, state = ?, heartbeat_at = ?,
                    expires_at = ? WHERE task_id = ?
                """,
                (phase, note, state, now, now + ttl_seconds, task["task_id"]),
            )
            return {
                "task_id": task["task_id"],
                "state": state,
                "phase": phase,
                "heartbeat_at": now,
                "expires_at": now + ttl_seconds,
            }

    def _claim_scopes(
        self, connection: sqlite3.Connection, claim_id: str
    ) -> dict[str, list[str]]:
        rows = connection.execute(
            """
            SELECT scope_kind, scope FROM claim_scopes
            WHERE claim_id = ? ORDER BY scope_kind, scope
            """,
            (claim_id,),
        ).fetchall()
        return _scope_payload(rows)

    def _claim_rank(self, kind: str, scopes: dict[str, list[str]]) -> int:
        if kind == "freeze":
            return 2
        rank = 1 if scopes["write"] else 0
        for resource in scopes["resource"]:
            rank = max(rank, RESOURCE_RANKS[resource])
        return rank

    def _claim_min_rank(self, kind: str, scopes: dict[str, list[str]]) -> int:
        if kind == "freeze":
            return 2
        ranks = ([1] if scopes["write"] else []) + [
            RESOURCE_RANKS[resource] for resource in scopes["resource"]
        ]
        return min(ranks)

    def _validate_order(
        self,
        connection: sqlite3.Connection,
        task_id: str,
        request_rank: int,
    ) -> None:
        active = connection.execute(
            """
            SELECT claim_id, kind FROM claims
            WHERE task_id = ? AND state = 'granted'
            """,
            (task_id,),
        ).fetchall()
        if any(row["kind"] == "freeze" for row in active):
            return
        ranks = [
            self._claim_rank(
                str(row["kind"]),
                self._claim_scopes(connection, str(row["claim_id"])),
            )
            for row in active
        ]
        if ranks and request_rank < max(ranks):
            raise UsageError(
                "Claim order violation: acquire path, freeze, vcs-maintenance, "
                "then unity-live."
            )

    def _same_claim(
        self,
        connection: sqlite3.Connection,
        task_id: str,
        kind: str,
        scopes: dict[str, list[str]],
    ) -> sqlite3.Row | None:
        rows = connection.execute(
            """
            SELECT * FROM claims
            WHERE task_id = ? AND kind = ? AND state IN ('queued', 'granted')
            ORDER BY queue_order
            """,
            (task_id, kind),
        ).fetchall()
        expected = {
            "write": sorted(scopes["write"]),
            "resource": sorted(scopes["resource"]),
        }
        for row in rows:
            if self._claim_scopes(connection, str(row["claim_id"])) == expected:
                return row
        return None

    def _claims_conflict(
        self,
        candidate_kind: str,
        candidate_scopes: dict[str, list[str]],
        other_kind: str,
        other_scopes: dict[str, list[str]],
    ) -> bool:
        if candidate_kind == "freeze" or other_kind == "freeze":
            return True
        for left in candidate_scopes["write"]:
            if any(_path_overlaps(left, right) for right in other_scopes["write"]):
                return True
        return bool(set(candidate_scopes["resource"]) & set(other_scopes["resource"]))

    def _pending_freeze_blocker(
        self,
        connection: sqlite3.Connection,
        task_id: str,
        queue_order: int,
    ) -> dict[str, Any] | None:
        row = connection.execute(
            """
            SELECT c.claim_id, c.task_id, t.owner
            FROM claims c JOIN tasks t ON t.task_id = c.task_id
            WHERE c.project_root = ? AND c.kind = 'freeze'
                AND c.state = 'queued' AND c.queue_order < ? AND c.task_id <> ?
            ORDER BY c.queue_order LIMIT 1
            """,
            (self.canonical_project_root, queue_order, task_id),
        ).fetchone()
        if row is None:
            return None
        return {
            "reason": "freeze-barrier",
            "claim_id": row["claim_id"],
            "task_id": row["task_id"],
            "owner": row["owner"],
        }

    def _legacy_freeze_blocker(
        self, connection: sqlite3.Connection, task_id: str
    ) -> dict[str, Any] | None:
        row = connection.execute(
            """
            SELECT p.path, COALESCE(d.kind, 'legacy-unowned') AS disposition,
                d.task_id
            FROM vcs_pending p
            LEFT JOIN vcs_dispositions d
                ON d.project_root = p.project_root AND d.path = p.path
            WHERE p.project_root = ?
                AND (
                    COALESCE(d.kind, 'legacy-unowned') IN ('legacy-unowned', 'protect')
                    OR (d.kind = 'adopt' AND COALESCE(d.task_id, '') <> ?)
                )
            ORDER BY p.path LIMIT 1
            """,
            (self.canonical_project_root, task_id),
        ).fetchone()
        if row is None:
            return None
        return {
            "reason": "vcs-pending-not-cleared-for-freeze",
            "path": row["path"],
            "disposition": row["disposition"],
        }

    def _legacy_path_blocker(
        self,
        connection: sqlite3.Connection,
        task_id: str,
        write_scopes: Sequence[str],
    ) -> dict[str, Any] | None:
        if not write_scopes:
            return None
        rows = connection.execute(
            """
            SELECT p.path, COALESCE(d.kind, 'legacy-unowned') AS disposition,
                d.task_id
            FROM vcs_pending p
            LEFT JOIN vcs_dispositions d
                ON d.project_root = p.project_root AND d.path = p.path
            WHERE p.project_root = ?
                AND (
                    COALESCE(d.kind, 'legacy-unowned') IN ('legacy-unowned', 'protect')
                    OR (d.kind = 'adopt' AND COALESCE(d.task_id, '') <> ?)
                )
            ORDER BY p.path
            """,
            (self.canonical_project_root, task_id),
        ).fetchall()
        for row in rows:
            if any(_path_overlaps(scope, str(row["path"])) for scope in write_scopes):
                return {
                    "reason": "vcs-pending-protected",
                    "path": row["path"],
                    "disposition": row["disposition"],
                }
        return None

    def _blocker_for(
        self,
        connection: sqlite3.Connection,
        claim: sqlite3.Row,
        scopes: dict[str, list[str]],
    ) -> dict[str, Any] | None:
        task_id = str(claim["task_id"])
        project_epoch = self._epoch(connection)
        task = connection.execute(
            "SELECT epoch FROM tasks WHERE task_id = ?", (task_id,)
        ).fetchone()
        if task is None or int(task["epoch"]) != project_epoch:
            return {
                "reason": "stale-epoch",
                "task_epoch": None if task is None else int(task["epoch"]),
                "workspace_epoch": project_epoch,
            }
        barrier = self._pending_freeze_blocker(
            connection, task_id, int(claim["queue_order"])
        )
        if barrier is not None and claim["kind"] != "freeze":
            return barrier
        if claim["kind"] == "freeze":
            legacy = self._legacy_freeze_blocker(connection, task_id)
            if legacy is not None:
                return legacy
        else:
            legacy = self._legacy_path_blocker(connection, task_id, scopes["write"])
            if legacy is not None:
                return legacy
        others = connection.execute(
            """
            SELECT c.*, t.owner, t.state AS task_state
            FROM claims c JOIN tasks t ON t.task_id = c.task_id
            WHERE c.project_root = ? AND c.task_id <> ?
                AND (
                    c.state = 'granted'
                    OR (c.state = 'queued' AND c.queue_order < ?)
                )
            ORDER BY CASE c.state WHEN 'granted' THEN 0 ELSE 1 END,
                c.queue_order
            """,
            (
                self.canonical_project_root,
                task_id,
                int(claim["queue_order"]),
            ),
        ).fetchall()
        for other in others:
            other_scopes = self._claim_scopes(connection, str(other["claim_id"]))
            if self._claims_conflict(
                str(claim["kind"]),
                scopes,
                str(other["kind"]),
                other_scopes,
            ):
                return {
                    "reason": "active-conflict"
                    if other["state"] == "granted"
                    else "fifo-queue",
                    "claim_id": other["claim_id"],
                    "task_id": other["task_id"],
                    "owner": other["owner"],
                    "state": other["state"],
                    "task_state": other["task_state"],
                }
        return None

    def _grant(
        self,
        connection: sqlite3.Connection,
        claim: sqlite3.Row,
        scopes: dict[str, list[str]],
        now: float,
    ) -> sqlite3.Row:
        epoch = self._epoch(connection)
        if claim["kind"] == "freeze":
            epoch += 1
            connection.execute(
                """
                UPDATE projects SET epoch = ?, updated_at = ?
                WHERE project_root = ?
                """,
                (epoch, now, self.canonical_project_root),
            )
            connection.execute(
                "UPDATE tasks SET epoch = ? WHERE task_id = ?",
                (epoch, claim["task_id"]),
            )
            connection.execute(
                """
                UPDATE claims SET epoch = ?
                WHERE task_id = ? AND state = 'granted'
                """,
                (epoch, claim["task_id"]),
            )
        legacy_lease_id: str | None = None
        if "unity-live" in scopes["resource"]:
            task = connection.execute(
                "SELECT owner, task_id FROM tasks WHERE task_id = ?",
                (claim["task_id"],),
            ).fetchone()
            lease = create_project_lease_unlocked(
                self.paths,
                self.canonical_project_root,
                f"workspace:{task['owner']}:{task['task_id']}",
                self.lease_ttl_seconds,
            )
            legacy_lease_id = lease.lease_id
        connection.execute(
            """
            UPDATE claims SET state = 'granted', granted_at = ?, epoch = ?,
                blocker_json = NULL, legacy_lease_id = ? WHERE claim_id = ?
            """,
            (now, epoch, legacy_lease_id, claim["claim_id"]),
        )
        return connection.execute(
            "SELECT * FROM claims WHERE claim_id = ?", (claim["claim_id"],)
        ).fetchone()

    def _public_claim(
        self, connection: sqlite3.Connection, claim: sqlite3.Row
    ) -> dict[str, Any]:
        scopes = self._claim_scopes(connection, str(claim["claim_id"]))
        blocker = json.loads(claim["blocker_json"]) if claim["blocker_json"] else None
        queue_position = None
        if claim["state"] == "queued":
            row = connection.execute(
                """
                SELECT COUNT(*) AS position FROM claims
                WHERE project_root = ? AND state = 'queued' AND queue_order <= ?
                """,
                (self.canonical_project_root, claim["queue_order"]),
            ).fetchone()
            queue_position = int(row["position"])
        return {
            "claim_id": claim["claim_id"],
            "task_id": claim["task_id"],
            "kind": claim["kind"],
            "state": claim["state"],
            "write": scopes["write"],
            "resources": scopes["resource"],
            "queue_order": claim["queue_order"],
            "queue_position": queue_position,
            "created_at": claim["created_at"],
            "granted_at": claim["granted_at"],
            "released_at": claim["released_at"],
            "epoch": claim["epoch"],
            "blocked_by": blocker,
            "next_condition": self._next_condition(blocker),
        }

    def advance_queues(self) -> int:
        """Promote every currently grantable queued claim in FIFO order."""
        now = time.time()
        granted_count = 0
        with (
            project_lock(
                self.paths,
                self.canonical_project_root,
                "workspace-queue-advance",
                30,
            ),
            self._transaction() as connection,
        ):
            self._cleanup(connection, now)
            stale_bindings = connection.execute(
                """
                SELECT claim_id, legacy_lease_id FROM claims
                WHERE project_root = ? AND state = 'released'
                    AND legacy_lease_id IS NOT NULL
                """,
                (self.canonical_project_root,),
            ).fetchall()
            for binding in stale_bindings:
                try:
                    release_project_lease_unlocked(
                        self.paths,
                        self.canonical_project_root,
                        str(binding["legacy_lease_id"]),
                    )
                except ProjectBusyError as exc:
                    if exc.details.get("reason"):
                        raise
                connection.execute(
                    """
                    UPDATE claims SET legacy_lease_id = NULL WHERE claim_id = ?
                    """,
                    (binding["claim_id"],),
                )
            queued = connection.execute(
                """
                SELECT * FROM claims
                WHERE project_root = ? AND state = 'queued'
                ORDER BY queue_order
                """,
                (self.canonical_project_root,),
            ).fetchall()
            for claim in queued:
                task = connection.execute(
                    "SELECT state FROM tasks WHERE task_id = ?",
                    (claim["task_id"],),
                ).fetchone()
                if task is None or task["state"] != "active":
                    continue
                scopes = self._claim_scopes(connection, str(claim["claim_id"]))
                blocker = self._blocker_for(connection, claim, scopes)
                if blocker is None:
                    self._grant(connection, claim, scopes, now)
                    granted_count += 1
                else:
                    connection.execute(
                        "UPDATE claims SET blocker_json = ? WHERE claim_id = ?",
                        (json.dumps(blocker, sort_keys=True), claim["claim_id"]),
                    )
        return granted_count

    @staticmethod
    def _next_condition(blocker: dict[str, Any] | None) -> str | None:
        if blocker is None:
            return None
        reasons = {
            "active-conflict": "blocking claim is released",
            "fifo-queue": "earlier overlapping queued claim is granted or cancelled",
            "freeze-barrier": "queued freeze completes or is cancelled",
            "stale-epoch": "task restarts on the current workspace epoch",
            "vcs-pending-not-cleared-for-freeze": (
                "pending path is resolved or receives a non-blocking disposition"
            ),
            "vcs-pending-protected": (
                "overlapping pending path is adopted, submitted, or resolved clean"
            ),
            "orphaned-unknown": "workspace owner resolves unknown outcome with evidence",
        }
        return reasons.get(str(blocker.get("reason")), "blocker is resolved")

    def acquire_claim(
        self,
        token: str | None,
        *,
        writes: Sequence[str] = (),
        resources: Sequence[str] = (),
        freeze: bool = False,
        wait_seconds: float = 0,
        keep_queued: bool = False,
    ) -> dict[str, Any]:
        _validate_duration(wait_seconds, "Claim wait", allow_zero=True)
        normalized_resources = tuple(sorted(set(resources)))
        unknown = set(normalized_resources) - MUTATING_RESOURCE_NAMES
        if unknown:
            raise UsageError(
                f"Unknown workspace resource: {', '.join(sorted(unknown))}"
            )
        write_scopes = _expand_write_scopes(
            self.project_root,
            writes,
            unity_meta_pairing=self.policy.unity_meta_pairing,
        )
        if freeze and (write_scopes or normalized_resources):
            raise UsageError("Freeze must be acquired as a standalone claim.")
        if not freeze and not write_scopes and not normalized_resources:
            raise UsageError("At least one write path or resource is required.")
        if (self.project_root / ".plastic").is_dir():
            force_reconcile = freeze or "vcs-maintenance" in normalized_resources
            if write_scopes and not force_reconcile:
                with self._transaction() as connection:
                    self._cleanup(connection, time.time())
                    task = self._task_for_token(connection, token)
                    open_count = connection.execute(
                        """
                        SELECT COUNT(*) AS count FROM claims
                        WHERE task_id = ? AND state IN ('queued', 'granted')
                        """,
                        (task["task_id"],),
                    ).fetchone()
                    force_reconcile = int(open_count["count"]) == 0
            if force_reconcile:
                self.reconcile_plastic()
        self.advance_queues()
        kind = "freeze" if freeze else "mutation"
        scopes = {
            "write": list(write_scopes),
            "resource": list(normalized_resources),
        }
        deadline = time.monotonic() + wait_seconds
        claim_id: str | None = None
        while True:
            now = time.time()
            with (
                project_lock(
                    self.paths,
                    self.canonical_project_root,
                    "workspace-claim",
                    min(max(wait_seconds, 0.2), 30),
                ),
                self._transaction() as connection,
            ):
                self._cleanup(connection, now)
                task = self._task_for_token(connection, token)
                request_rank = self._claim_min_rank(kind, scopes)
                existing = self._same_claim(
                    connection, str(task["task_id"]), kind, scopes
                )
                if existing is None:
                    self._validate_order(connection, str(task["task_id"]), request_rank)
                    queued_freeze = connection.execute(
                        """
                            SELECT claim_id FROM claims
                            WHERE project_root = ? AND kind = 'freeze'
                                AND state = 'queued' AND task_id <> ?
                            LIMIT 1
                            """,
                        (self.canonical_project_root, task["task_id"]),
                    ).fetchone()
                    if queued_freeze is not None and kind != "freeze":
                        raise ProjectBusyError(
                            "A queued workspace freeze blocks scope expansion.",
                            details={
                                "reason": "freeze-barrier",
                                "claim_id": queued_freeze["claim_id"],
                            },
                        )
                    claim_id = _public_id("claim")
                    queue_order = time.monotonic_ns()
                    connection.execute(
                        """
                            INSERT INTO claims(
                                claim_id, project_root, task_id, kind, state,
                                queue_order, created_at
                            ) VALUES(?, ?, ?, ?, 'queued', ?, ?)
                            """,
                        (
                            claim_id,
                            self.canonical_project_root,
                            task["task_id"],
                            kind,
                            queue_order,
                            now,
                        ),
                    )
                    for scope_kind, values in scopes.items():
                        connection.executemany(
                            """
                                INSERT INTO claim_scopes(
                                    claim_id, scope_kind, scope
                                ) VALUES(?, ?, ?)
                                """,
                            [(claim_id, scope_kind, value) for value in values],
                        )
                    claim = connection.execute(
                        "SELECT * FROM claims WHERE claim_id = ?", (claim_id,)
                    ).fetchone()
                else:
                    claim = existing
                    claim_id = str(existing["claim_id"])
                if claim["state"] == "queued":
                    blocker = self._blocker_for(connection, claim, scopes)
                    if blocker is None:
                        claim = self._grant(connection, claim, scopes, now)
                    else:
                        connection.execute(
                            "UPDATE claims SET blocker_json = ? WHERE claim_id = ?",
                            (json.dumps(blocker, sort_keys=True), claim_id),
                        )
                        claim = connection.execute(
                            "SELECT * FROM claims WHERE claim_id = ?", (claim_id,)
                        ).fetchone()
                payload = self._public_claim(connection, claim)
            if payload["state"] == "granted":
                return payload
            remaining = deadline - time.monotonic()
            if wait_seconds == 0 or remaining <= 0:
                if not keep_queued:
                    self.cancel_claim(token, str(claim_id))
                    payload["state"] = "cancelled"
                return payload
            time.sleep(min(POLL_INTERVAL_SECONDS, remaining))

    def dry_run(
        self,
        token: str | None,
        *,
        writes: Sequence[str] = (),
        resources: Sequence[str] = (),
        freeze: bool = False,
    ) -> dict[str, Any]:
        normalized_resources = tuple(sorted(set(resources)))
        unknown = set(normalized_resources) - MUTATING_RESOURCE_NAMES
        if unknown:
            raise UsageError(
                f"Unknown workspace resource: {', '.join(sorted(unknown))}"
            )
        scopes = {
            "write": list(
                _expand_write_scopes(
                    self.project_root,
                    writes,
                    unity_meta_pairing=self.policy.unity_meta_pairing,
                )
            ),
            "resource": list(normalized_resources),
        }
        kind = "freeze" if freeze else "mutation"
        now = time.time()
        with self._transaction() as connection:
            self._cleanup(connection, now)
            task = self._task_for_token(connection, token)
            synthetic = {
                "claim_id": "dry-run",
                "project_root": self.canonical_project_root,
                "task_id": task["task_id"],
                "kind": kind,
                "state": "queued",
                "queue_order": time.monotonic_ns(),
            }
            blocker = self._blocker_for(connection, synthetic, scopes)  # type: ignore[arg-type]
            return {
                "can_start": blocker is None,
                "blocked_by": blocker,
                "next_condition": self._next_condition(blocker),
                "write": scopes["write"],
                "resources": scopes["resource"],
                "freeze": freeze,
                "workspace_epoch": self._epoch(connection),
            }

    def assert_claims(
        self,
        token: str | None,
        *,
        writes: Sequence[str] = (),
        resources: Sequence[str] = (),
        freeze: bool = False,
    ) -> dict[str, Any]:
        requested_writes = _expand_write_scopes(
            self.project_root,
            writes,
            unity_meta_pairing=self.policy.unity_meta_pairing,
        )
        requested_resources = tuple(sorted(set(resources)))
        unknown = set(requested_resources) - MUTATING_RESOURCE_NAMES
        if unknown:
            raise UsageError(
                f"Unknown workspace resource: {', '.join(sorted(unknown))}"
            )
        now = time.time()
        with self._transaction() as connection:
            self._cleanup(connection, now)
            task = self._task_for_token(connection, token)
            epoch = self._epoch(connection)
            if int(task["epoch"]) != epoch:
                raise ProjectBusyError(
                    "Workspace task is fenced by a newer epoch.",
                    details={
                        "reason": "stale-epoch",
                        "task_epoch": task["epoch"],
                        "workspace_epoch": epoch,
                    },
                )
            claims = connection.execute(
                """
                SELECT * FROM claims
                WHERE task_id = ? AND state = 'granted' AND epoch = ?
                """,
                (task["task_id"], epoch),
            ).fetchall()
            held_writes: list[str] = []
            held_resources: set[str] = set()
            resource_claim_ids: dict[str, str] = {}
            claim_ids: list[str] = []
            legacy_lease_id: str | None = None
            holds_freeze = False
            for claim in claims:
                claim_ids.append(str(claim["claim_id"]))
                scopes = self._claim_scopes(connection, str(claim["claim_id"]))
                holds_freeze = holds_freeze or claim["kind"] == "freeze"
                held_writes.extend(scopes["write"])
                held_resources.update(scopes["resource"])
                for resource in scopes["resource"]:
                    resource_claim_ids[resource] = str(claim["claim_id"])
                if "unity-live" in scopes["resource"]:
                    legacy_lease_id = claim["legacy_lease_id"]
            missing_writes = [
                scope
                for scope in requested_writes
                if not any(_path_covers(held, scope) for held in held_writes)
            ]
            missing_resources = sorted(set(requested_resources) - held_resources)
            if missing_writes or missing_resources or (freeze and not holds_freeze):
                raise ProjectBusyError(
                    "Workspace task does not hold the requested claim scope.",
                    details={
                        "reason": "claim-required",
                        "missing_write": missing_writes,
                        "missing_resources": missing_resources,
                        "missing_freeze": freeze and not holds_freeze,
                    },
                )
            return {
                "valid": True,
                "task_id": task["task_id"],
                "epoch": epoch,
                "claim_ids": claim_ids,
                "write": list(requested_writes),
                "resources": list(requested_resources),
                "freeze": freeze,
                "resource_claim_ids": resource_claim_ids,
                "legacy_lease_id": legacy_lease_id,
            }

    def _owned_claim(
        self,
        connection: sqlite3.Connection,
        token: str | None,
        claim_id: str,
        allowed_states: Sequence[str],
    ) -> tuple[sqlite3.Row, sqlite3.Row]:
        task = self._task_for_token(connection, token, allow_outcome_unknown=True)
        placeholders = ",".join("?" for _ in allowed_states)
        claim = connection.execute(
            f"""
            SELECT * FROM claims
            WHERE claim_id = ? AND task_id = ? AND state IN ({placeholders})
            """,
            (claim_id, task["task_id"], *allowed_states),
        ).fetchone()
        if claim is None:
            raise UsageError("Claim is not owned by this task or is already closed.")
        return task, claim

    def release_claim(self, token: str | None, claim_id: str) -> dict[str, Any]:
        now = time.time()
        with (
            project_lock(
                self.paths,
                self.canonical_project_root,
                "workspace-claim-release",
                30,
            ),
            self._transaction() as connection,
        ):
            self._cleanup(connection, now)
            task, claim = self._owned_claim(connection, token, claim_id, ("granted",))
            if task["state"] == "outcome_unknown":
                raise ProjectBusyError(
                    "Outcome-unknown claims require evidence-backed recovery.",
                    details={"reason": "outcome-unknown-recovery-required"},
                )
            if claim["legacy_lease_id"]:
                release_project_lease_unlocked(
                    self.paths,
                    self.canonical_project_root,
                    str(claim["legacy_lease_id"]),
                )
            connection.execute(
                """
                    UPDATE claims SET state = 'released', released_at = ?,
                        legacy_lease_id = NULL WHERE claim_id = ?
                    """,
                (now, claim_id),
            )
            claim = connection.execute(
                "SELECT * FROM claims WHERE claim_id = ?", (claim_id,)
            ).fetchone()
            return self._public_claim(connection, claim)

    def cancel_claim(self, token: str | None, claim_id: str) -> dict[str, Any]:
        now = time.time()
        with self._transaction() as connection:
            self._cleanup(connection, now)
            _, claim = self._owned_claim(connection, token, claim_id, ("queued",))
            connection.execute(
                """
                UPDATE claims SET state = 'cancelled', released_at = ?
                WHERE claim_id = ?
                """,
                (now, claim_id),
            )
            claim = connection.execute(
                "SELECT * FROM claims WHERE claim_id = ?", (claim_id,)
            ).fetchone()
            return self._public_claim(connection, claim)

    def release_task(self, token: str | None, *, result: str) -> dict[str, Any]:
        if result not in {"completed", "failed"}:
            raise UsageError("Task result must be completed or failed.")
        now = time.time()
        with (
            project_lock(
                self.paths,
                self.canonical_project_root,
                "workspace-task-release",
                30,
            ),
            self._transaction() as connection,
        ):
            self._cleanup(connection, now)
            task = self._task_for_token(connection, token, allow_outcome_unknown=True)
            if task["state"] == "outcome_unknown":
                raise ProjectBusyError(
                    "Outcome-unknown tasks require evidence-backed recovery.",
                    details={"reason": "outcome-unknown-recovery-required"},
                )
            claims = connection.execute(
                """
                    SELECT * FROM claims
                    WHERE task_id = ? AND state IN ('queued', 'granted')
                    """,
                (task["task_id"],),
            ).fetchall()
            for claim in claims:
                if claim["legacy_lease_id"]:
                    release_project_lease_unlocked(
                        self.paths,
                        self.canonical_project_root,
                        str(claim["legacy_lease_id"]),
                    )
            connection.execute(
                """
                    UPDATE claims SET state = CASE state
                        WHEN 'queued' THEN 'cancelled' ELSE 'released' END,
                        released_at = ?, legacy_lease_id = NULL
                    WHERE task_id = ? AND state IN ('queued', 'granted')
                    """,
                (now, task["task_id"]),
            )
            connection.execute(
                """
                    UPDATE tasks SET state = ?, ended_at = ?, expires_at = ?
                    WHERE task_id = ?
                    """,
                (result, now, now, task["task_id"]),
            )
            connection.execute(
                """
                UPDATE vcs_dispositions
                SET kind = 'protect', task_id = NULL,
                    evidence = COALESCE(evidence, 'adopting task released'),
                    updated_at = ?
                WHERE project_root = ? AND kind = 'adopt' AND task_id = ?
                """,
                (now, self.canonical_project_root, task["task_id"]),
            )
            return {
                "task_id": task["task_id"],
                "state": result,
                "released_claim_count": len(claims),
                "ended_at": now,
            }

    def resolve_unknown(
        self,
        *,
        task_id: str,
        disposition: str,
        evidence: str,
    ) -> dict[str, Any]:
        if disposition not in {"applied", "not-applied", "contained"}:
            raise UsageError("Unknown outcome disposition is invalid.")
        evidence = _validate_plain_text(evidence, "Recovery evidence", 4096)
        now = time.time()
        with (
            project_lock(
                self.paths,
                self.canonical_project_root,
                "workspace-recovery",
                30,
            ),
            self._transaction() as connection,
        ):
            self._cleanup(connection, now)
            task = connection.execute(
                """
                    SELECT * FROM tasks WHERE task_id = ? AND project_root = ?
                        AND state IN ('outcome_unknown', 'orphaned_unknown')
                    """,
                (task_id, self.canonical_project_root),
            ).fetchone()
            if task is None:
                raise UsageError("Task is not awaiting unknown-outcome recovery.")
            claims = connection.execute(
                "SELECT * FROM claims WHERE task_id = ? AND state = 'granted'",
                (task_id,),
            ).fetchall()
            for claim in claims:
                if claim["legacy_lease_id"]:
                    release_project_lease_unlocked(
                        self.paths,
                        self.canonical_project_root,
                        str(claim["legacy_lease_id"]),
                    )
            new_epoch = self._epoch(connection) + 1
            connection.execute(
                """
                    UPDATE projects SET epoch = ?, updated_at = ?
                    WHERE project_root = ?
                    """,
                (new_epoch, now, self.canonical_project_root),
            )
            connection.execute(
                """
                    UPDATE claims SET state = 'released', released_at = ?,
                        legacy_lease_id = NULL
                    WHERE task_id = ? AND state = 'granted'
                    """,
                (now, task_id),
            )
            connection.execute(
                """
                    UPDATE tasks SET state = 'failed', ended_at = ?, expires_at = ?
                    WHERE task_id = ?
                    """,
                (now, now, task_id),
            )
            recovery_id = _public_id("recovery")
            connection.execute(
                """
                    INSERT INTO recovery_events(
                        recovery_id, project_root, task_id, disposition, evidence,
                        approved_by, created_at, new_epoch
                    ) VALUES(?, ?, ?, ?, ?, ?, ?, ?)
                    """,
                (
                    recovery_id,
                    self.canonical_project_root,
                    task_id,
                    disposition,
                    evidence,
                    getpass.getuser(),
                    now,
                    new_epoch,
                ),
            )
            return {
                "recovery_id": recovery_id,
                "task_id": task_id,
                "disposition": disposition,
                "evidence": evidence,
                "new_epoch": new_epoch,
                "released_claim_count": len(claims),
            }

    def reconcile_plastic(self) -> dict[str, Any]:
        command = [
            "cm",
            "status",
            "--short",
            "--machinereadable",
            "--fieldseparator=|",
            "--nomergesinfo",
        ]
        try:
            completed = subprocess.run(
                command,
                cwd=self.project_root,
                text=True,
                encoding="utf-8",
                errors="strict",
                capture_output=True,
                check=False,
                timeout=60,
            )
        except (OSError, UnicodeError, subprocess.TimeoutExpired) as exc:
            raise UsageError(f"Cannot inspect Plastic pending changes: {exc}") from exc
        if completed.returncode != 0:
            raise UsageError(
                "Plastic pending inspection failed.",
                details={"stderr": completed.stderr.strip()},
            )
        pending: dict[str, str] = {}
        for line in completed.stdout.splitlines():
            for status, path in self._parse_plastic_status_line(line):
                pending[_normalize_scope(self.project_root, path)] = status
        observation_id = _public_id("vcs")
        now = time.time()
        with self._transaction() as connection:
            connection.execute(
                "DELETE FROM vcs_pending WHERE project_root = ?",
                (self.canonical_project_root,),
            )
            connection.executemany(
                """
                INSERT INTO vcs_pending(project_root, path, status, observation_id)
                VALUES(?, ?, ?, ?)
                """,
                [
                    (self.canonical_project_root, path, status, observation_id)
                    for path, status in sorted(pending.items())
                ],
            )
            connection.execute(
                """
                DELETE FROM vcs_dispositions
                WHERE project_root = ? AND NOT EXISTS (
                    SELECT 1 FROM vcs_pending p
                    WHERE p.project_root = vcs_dispositions.project_root
                        AND p.path = vcs_dispositions.path
                )
                """,
                (self.canonical_project_root,),
            )
            connection.execute(
                """
                INSERT INTO vcs_observations(
                    observation_id, project_root, observed_at, command,
                    pending_count
                ) VALUES(?, ?, ?, ?, ?)
                """,
                (
                    observation_id,
                    self.canonical_project_root,
                    now,
                    " ".join(command),
                    len(pending),
                ),
            )
        return {
            "observation_id": observation_id,
            "observed_at": now,
            "pending_count": len(pending),
            "command": " ".join(command),
        }

    @staticmethod
    def _parse_plastic_status_line(line: str) -> list[tuple[str, str]]:
        value = line.strip()
        if not value:
            return []
        fields = value.split("|")
        status = fields[0].strip()
        if len(fields) >= 5 and "MV" in status:
            source = fields[-3].strip().strip('"')
            destination = fields[-2].strip().strip('"')
            return [
                (f"{status}-source", source),
                (f"{status}-destination", destination),
            ]
        if len(fields) >= 3:
            path = fields[1].strip().strip('"')
            return [(status, path)] if path else []
        match = re.match(r"^(?P<status>[^\s;]+)[;\s]+(?P<path>.+)$", value)
        if match is None:
            return []
        path = match.group("path").strip().strip('"')
        return [(match.group("status").strip(), path)] if path else []

    def import_plastic_baseline(self) -> dict[str, Any]:
        observation = self.reconcile_plastic()
        now = time.time()
        with self._transaction() as connection:
            paths = connection.execute(
                "SELECT path FROM vcs_pending WHERE project_root = ?",
                (self.canonical_project_root,),
            ).fetchall()
            connection.executemany(
                """
                INSERT INTO vcs_dispositions(
                    project_root, path, kind, task_id, evidence, updated_at
                ) VALUES(?, ?, 'legacy-unowned', NULL, ?, ?)
                ON CONFLICT(project_root, path) DO NOTHING
                """,
                [
                    (
                        self.canonical_project_root,
                        row["path"],
                        f"Imported from {observation['observation_id']}",
                        now,
                    )
                    for row in paths
                ],
            )
        return observation

    def set_disposition(
        self,
        token: str | None,
        *,
        kind: str,
        writes: Sequence[str],
        evidence: str | None,
    ) -> dict[str, Any]:
        if kind not in DISPOSITIONS - {"legacy-unowned"}:
            raise UsageError("Plastic baseline disposition is invalid.")
        if evidence is not None:
            evidence = _validate_plain_text(evidence, "Disposition evidence", 4096)
        paths = [_normalize_scope(self.project_root, value) for value in writes]
        if not paths:
            raise UsageError("At least one Plastic path is required.")
        if (self.project_root / ".plastic").is_dir():
            self.reconcile_plastic()
        now = time.time()
        with self._transaction() as connection:
            self._cleanup(connection, now)
            task = self._task_for_token(connection, token)
            epoch = self._epoch(connection)
            if int(task["epoch"]) != epoch:
                raise ProjectBusyError(
                    "Workspace task is fenced by a newer epoch.",
                    details={
                        "reason": "stale-epoch",
                        "task_epoch": task["epoch"],
                        "workspace_epoch": epoch,
                    },
                )
            placeholders = ",".join("?" for _ in paths)
            existing = connection.execute(
                f"""
                SELECT path FROM vcs_pending
                WHERE project_root = ? AND path IN ({placeholders})
                """,
                (self.canonical_project_root, *paths),
            ).fetchall()
            existing_paths = {str(row["path"]) for row in existing}
            missing = sorted(set(paths) - existing_paths)
            if missing:
                raise UsageError(
                    "Plastic baseline disposition requires currently pending paths.",
                    details={"missing_write": missing},
                )
            disposition_task_id = str(task["task_id"]) if kind == "adopt" else None
            connection.executemany(
                """
                INSERT INTO vcs_dispositions(
                    project_root, path, kind, task_id, evidence, updated_at
                ) VALUES(?, ?, ?, ?, ?, ?)
                ON CONFLICT(project_root, path) DO UPDATE SET
                    kind = excluded.kind, task_id = excluded.task_id,
                    evidence = excluded.evidence,
                    updated_at = excluded.updated_at
                """,
                [
                    (
                        self.canonical_project_root,
                        path,
                        kind,
                        disposition_task_id,
                        evidence,
                        now,
                    )
                    for path in paths
                ],
            )
        return {
            "kind": kind,
            "write": paths,
            "task_id": disposition_task_id,
            "evidence": evidence,
        }

    def status(self, *, vcs_stale_seconds: float = 30) -> dict[str, Any]:
        self.advance_queues()
        now = time.time()
        with self._transaction() as connection:
            self._cleanup(connection, now)
            epoch = self._epoch(connection)
            task_rows = connection.execute(
                """
                SELECT * FROM tasks
                WHERE project_root = ? AND state IN (
                    'active', 'outcome_unknown', 'orphaned_unknown'
                ) ORDER BY created_at
                """,
                (self.canonical_project_root,),
            ).fetchall()
            claim_rows = connection.execute(
                """
                SELECT * FROM claims
                WHERE project_root = ? AND state IN ('queued', 'granted')
                ORDER BY queue_order
                """,
                (self.canonical_project_root,),
            ).fetchall()
            claims = []
            for claim in claim_rows:
                if claim["state"] == "queued":
                    scopes = self._claim_scopes(connection, str(claim["claim_id"]))
                    blocker = self._blocker_for(connection, claim, scopes)
                    connection.execute(
                        "UPDATE claims SET blocker_json = ? WHERE claim_id = ?",
                        (
                            None if blocker is None else json.dumps(blocker),
                            claim["claim_id"],
                        ),
                    )
                    claim = connection.execute(
                        "SELECT * FROM claims WHERE claim_id = ?",
                        (claim["claim_id"],),
                    ).fetchone()
                claims.append(self._public_claim(connection, claim))
            tasks = [
                {
                    "task_id": row["task_id"],
                    "owner": row["owner"],
                    "summary": row["summary"],
                    "task_uri": row["task_uri"],
                    "phase": row["phase"],
                    "note": row["note"],
                    "state": row["state"],
                    "epoch": row["epoch"],
                    "created_at": row["created_at"],
                    "heartbeat_at": row["heartbeat_at"],
                    "expires_at": row["expires_at"],
                    "claims": [
                        claim["claim_id"]
                        for claim in claims
                        if claim["task_id"] == row["task_id"]
                    ],
                }
                for row in task_rows
            ]
            observation = connection.execute(
                """
                SELECT * FROM vcs_observations
                WHERE project_root = ? ORDER BY observed_at DESC LIMIT 1
                """,
                (self.canonical_project_root,),
            ).fetchone()
            pending_rows = connection.execute(
                """
                SELECT p.path, p.status,
                    COALESCE(d.kind, 'legacy-unowned') AS disposition,
                    d.task_id, d.evidence
                FROM vcs_pending p
                LEFT JOIN vcs_dispositions d
                    ON d.project_root = p.project_root AND d.path = p.path
                WHERE p.project_root = ? ORDER BY p.path
                """,
                (self.canonical_project_root,),
            ).fetchall()
            owner_by_task = {
                str(row["task_id"]): str(row["owner"]) for row in task_rows
            }
            pending = []
            for row in pending_rows:
                item = dict(row)
                if item["disposition"] == "adopt" and item["task_id"]:
                    item["owner"] = owner_by_task.get(str(item["task_id"]))
                covering = next(
                    (
                        claim
                        for claim in claims
                        if claim["state"] == "granted"
                        and any(
                            _path_overlaps(str(item["path"]), scope)
                            for scope in claim["write"]
                        )
                    ),
                    None,
                )
                if covering is not None and item["disposition"] == "legacy-unowned":
                    item["disposition"] = "active-claim"
                    item["task_id"] = covering["task_id"]
                    item["owner"] = owner_by_task.get(str(covering["task_id"]))
                pending.append(item)
            vcs = {
                "observation_id": None
                if observation is None
                else observation["observation_id"],
                "observed_at": None
                if observation is None
                else observation["observed_at"],
                "pending_count": len(pending),
                "legacy_unowned_count": sum(
                    item["disposition"] == "legacy-unowned" for item in pending
                ),
                "protected_count": sum(
                    item["disposition"] == "protect" for item in pending
                ),
                "stale": observation is None
                or now - float(observation["observed_at"]) > vcs_stale_seconds,
                "pending": pending,
            }
            freeze = next(
                (claim for claim in claims if claim["kind"] == "freeze"), None
            )
            return {
                "schema_version": WORKSPACE_SCHEMA_VERSION,
                "project_root": str(self.project_root),
                "policy": self.policy.public_payload(),
                "workspace_epoch": epoch,
                "observed_at": now,
                "tasks": tasks,
                "claims": claims,
                "freeze": freeze,
                "vcs": vcs,
                "coordination_error": self.policy.error,
            }
