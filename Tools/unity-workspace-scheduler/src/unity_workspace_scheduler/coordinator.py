"""Atomic workspace task and resource scheduling."""

from __future__ import annotations

import hashlib
import os
import secrets
import sqlite3
import time
import uuid
from collections.abc import Iterator, Sequence
from contextlib import contextmanager
from pathlib import Path, PurePosixPath
from typing import Any

from .errors import AuthorizationError, BusyError, StateError, UsageError
from .state import StatePaths, canonical_workspace, open_database

ACTIVE_TASK_STATES = ("active", "outcome_unknown")
OPEN_CLAIM_STATES = ("queued", "active", "parked")
TERMINAL_TASK_STATES = ("completed", "failed", "expired")
DEFAULT_TASK_TTL_SECONDS = 1800.0


def _token_hash(token: str) -> str:
    return hashlib.sha256(token.encode("utf-8")).hexdigest()


def _workspace_id(root: str) -> str:
    key = os.path.normcase(root).casefold()
    return hashlib.sha256(key.encode("utf-8")).hexdigest()


def _path_conflicts(left: str, right: str) -> bool:
    left_unit = left.removesuffix(".meta")
    right_unit = right.removesuffix(".meta")
    return (
        left_unit == right_unit
        or left_unit == "."
        or right_unit == "."
        or left_unit.startswith(right_unit + "/")
        or right_unit.startswith(left_unit + "/")
    )


class WorkspaceCoordinator:
    def __init__(self, paths: StatePaths) -> None:
        self.paths = paths

    @contextmanager
    def _transaction(self) -> Iterator[sqlite3.Connection]:
        connection = open_database(self.paths)
        try:
            connection.execute("BEGIN IMMEDIATE")
            yield connection
            connection.commit()
        except Exception:
            connection.rollback()
            raise
        finally:
            connection.close()

    @staticmethod
    def _workspace(connection: sqlite3.Connection, root: str) -> sqlite3.Row:
        workspace = connection.execute(
            "SELECT * FROM workspaces WHERE id = ? AND root = ?",
            (_workspace_id(root), root),
        ).fetchone()
        if workspace is None:
            raise StateError(
                "Workspace is not registered.",
                details={"workspace": root, "reason": "workspace-unregistered"},
            )
        return workspace

    @staticmethod
    def _touch(connection: sqlite3.Connection, workspace_id: str) -> None:
        connection.execute("UPDATE workspaces SET epoch = epoch + 1 WHERE id = ?", (workspace_id,))

    @staticmethod
    def _unknown_exists(connection: sqlite3.Connection, workspace_id: str) -> bool:
        return (
            connection.execute(
                "SELECT 1 FROM tasks WHERE workspace_id = ? AND state = 'outcome_unknown' LIMIT 1",
                (workspace_id,),
            ).fetchone()
            is not None
        )

    @staticmethod
    def _expire_tasks(connection: sqlite3.Connection, now: float) -> None:
        expired = connection.execute(
            "SELECT id, workspace_id FROM tasks WHERE state = 'active' AND expires_at <= ?",
            (now,),
        ).fetchall()
        for task in expired:
            queued_freezes = connection.execute(
                "SELECT id FROM claims WHERE task_id = ? AND kind = 'freeze' AND state = 'queued'",
                (task["id"],),
            ).fetchall()
            active_claim = connection.execute(
                "SELECT 1 FROM claims WHERE task_id = ? AND state = 'active' LIMIT 1",
                (task["id"],),
            ).fetchone()
            connection.execute(
                "UPDATE claims SET state = 'cancelled', released_at = ? "
                "WHERE task_id = ? AND state = 'queued'",
                (now, task["id"]),
            )
            WorkspaceCoordinator._resume_parked_for_freezes(
                connection, [row["id"] for row in queued_freezes]
            )
            if active_claim is not None:
                connection.execute(
                    "UPDATE tasks SET state = 'outcome_unknown', finished_at = ?, "
                    "result = 'expired-with-active-claim', "
                    "note = 'Task TTL expired while resources were still owned.' "
                    "WHERE id = ?",
                    (now, task["id"]),
                )
            else:
                connection.execute(
                    "UPDATE claims SET state = 'cancelled', released_at = ? "
                    "WHERE task_id = ? AND state = 'parked'",
                    (now, task["id"]),
                )
                connection.execute(
                    "DELETE FROM claim_scopes WHERE scope_type = 'parked_for' "
                    "AND claim_id IN (SELECT id FROM claims WHERE task_id = ?)",
                    (task["id"],),
                )
                connection.execute(
                    "UPDATE tasks SET state = 'expired', finished_at = ?, "
                    "result = 'expired' WHERE id = ?",
                    (now, task["id"]),
                )
            WorkspaceCoordinator._touch(connection, task["workspace_id"])

    @staticmethod
    def _claim_scopes(connection: sqlite3.Connection, claim_id: str) -> dict[str, tuple[str, ...]]:
        rows = connection.execute(
            "SELECT scope_type, value FROM claim_scopes "
            "WHERE claim_id = ? ORDER BY scope_type, value",
            (claim_id,),
        ).fetchall()
        return {
            "write": tuple(row["value"] for row in rows if row["scope_type"] == "write"),
            "resource": tuple(row["value"] for row in rows if row["scope_type"] == "resource"),
            "parked_for": tuple(row["value"] for row in rows if row["scope_type"] == "parked_for"),
            "priority": tuple(row["value"] for row in rows if row["scope_type"] == "priority"),
        }

    @staticmethod
    def _claim_priority(claim: sqlite3.Row, scopes: dict[str, tuple[str, ...]]) -> str:
        priorities = scopes["priority"]
        if not priorities:
            return "normal"
        if claim["kind"] != "freeze" or priorities != ("urgent",):
            raise StateError("Claim priority state is invalid.")
        return "urgent"

    @staticmethod
    def _claim_sort_key(claim: sqlite3.Row, scopes: dict[str, tuple[str, ...]]) -> tuple[int, int]:
        rank = 0 if WorkspaceCoordinator._claim_priority(claim, scopes) == "urgent" else 1
        return rank, claim["queue_order"]

    @staticmethod
    def _resume_parked_for_freezes(
        connection: sqlite3.Connection, freeze_ids: Sequence[str]
    ) -> None:
        for freeze_id in freeze_ids:
            parked = connection.execute(
                "SELECT claims.id FROM claims "
                "JOIN claim_scopes ON claim_scopes.claim_id = claims.id "
                "WHERE claims.state = 'parked' "
                "AND claim_scopes.scope_type = 'parked_for' "
                "AND claim_scopes.value = ?",
                (freeze_id,),
            ).fetchall()
            claim_ids = [row["id"] for row in parked]
            if not claim_ids:
                continue
            placeholders = ", ".join("?" for _ in claim_ids)
            connection.execute(
                f"UPDATE claims SET state = 'queued', granted_at = NULL "
                f"WHERE id IN ({placeholders}) AND state = 'parked'",
                claim_ids,
            )
            connection.execute(
                f"DELETE FROM claim_scopes WHERE scope_type = 'parked_for' "
                f"AND claim_id IN ({placeholders})",
                claim_ids,
            )

    @staticmethod
    def _task_drain_request(
        connection: sqlite3.Connection, workspace_id: str, task_id: str
    ) -> dict[str, Any] | None:
        freezes = connection.execute(
            "SELECT * FROM claims WHERE workspace_id = ? "
            "AND task_id != ? AND kind = 'freeze' AND state = 'queued'",
            (workspace_id, task_id),
        ).fetchall()
        if not freezes:
            return None
        freeze_scopes = {
            freeze["id"]: WorkspaceCoordinator._claim_scopes(connection, freeze["id"])
            for freeze in freezes
        }
        freeze = min(
            freezes,
            key=lambda candidate: WorkspaceCoordinator._claim_sort_key(
                candidate, freeze_scopes[candidate["id"]]
            ),
        )
        freeze_key = WorkspaceCoordinator._claim_sort_key(freeze, freeze_scopes[freeze["id"]])
        owned_claims = connection.execute(
            "SELECT * FROM claims WHERE workspace_id = ? AND task_id = ? "
            "AND state IN ('queued', 'active')",
            (workspace_id, task_id),
        ).fetchall()
        blocking_claims = [
            claim
            for claim in owned_claims
            if (
                claim["state"] == "active"
                or WorkspaceCoordinator._claim_sort_key(
                    claim, WorkspaceCoordinator._claim_scopes(connection, claim["id"])
                )
                < freeze_key
            )
        ]
        if not blocking_claims:
            return None
        unsafe_claim = any(
            claim["kind"] == "freeze"
            or WorkspaceCoordinator._claim_scopes(connection, claim["id"])["resource"]
            for claim in blocking_claims
        )
        return {
            "freeze_id": freeze["id"],
            "queue_order": freeze["queue_order"],
            "priority": WorkspaceCoordinator._claim_priority(freeze, freeze_scopes[freeze["id"]]),
            "park_ready": not unsafe_claim,
        }

    @staticmethod
    def _claims_conflict(
        left: sqlite3.Row,
        left_scopes: dict[str, tuple[str, ...]],
        right: sqlite3.Row,
        right_scopes: dict[str, tuple[str, ...]],
    ) -> bool:
        if left["task_id"] == right["task_id"]:
            return False
        if left["kind"] == "freeze" or right["kind"] == "freeze":
            return True
        if set(left_scopes["resource"]) & set(right_scopes["resource"]):
            return True
        return any(
            _path_conflicts(left_path, right_path)
            for left_path in left_scopes["write"]
            for right_path in right_scopes["write"]
        )

    @staticmethod
    def _schedule_workspace(connection: sqlite3.Connection, workspace_id: str, now: float) -> None:
        if WorkspaceCoordinator._unknown_exists(connection, workspace_id):
            return
        active = list(
            connection.execute(
                "SELECT * FROM claims WHERE workspace_id = ? AND state = 'active' "
                "ORDER BY queue_order",
                (workspace_id,),
            ).fetchall()
        )
        active_scopes = {
            claim["id"]: WorkspaceCoordinator._claim_scopes(connection, claim["id"])
            for claim in active
        }
        queued = list(
            connection.execute(
                "SELECT * FROM claims WHERE workspace_id = ? AND state = 'queued'",
                (workspace_id,),
            ).fetchall()
        )
        queued_scopes = {
            claim["id"]: WorkspaceCoordinator._claim_scopes(connection, claim["id"])
            for claim in queued
        }
        queued.sort(
            key=lambda claim: WorkspaceCoordinator._claim_sort_key(
                claim, queued_scopes[claim["id"]]
            )
        )
        blocked_earlier: list[sqlite3.Row] = []
        blocked_scopes: dict[str, dict[str, tuple[str, ...]]] = {}
        for candidate in queued:
            candidate_scopes = queued_scopes[candidate["id"]]
            if candidate["kind"] == "freeze":
                other_active = [
                    claim for claim in active if claim["task_id"] != candidate["task_id"]
                ]
                if not other_active and not blocked_earlier:
                    connection.execute(
                        "UPDATE claims SET state = 'active', granted_at = ? WHERE id = ?",
                        (now, candidate["id"]),
                    )
                    active.append(candidate)
                    active_scopes[candidate["id"]] = candidate_scopes
                break
            conflicts_active = any(
                WorkspaceCoordinator._claims_conflict(
                    candidate,
                    candidate_scopes,
                    existing,
                    active_scopes[existing["id"]],
                )
                for existing in active
            )
            conflicts_queued = any(
                WorkspaceCoordinator._claims_conflict(
                    candidate,
                    candidate_scopes,
                    earlier,
                    blocked_scopes[earlier["id"]],
                )
                for earlier in blocked_earlier
            )
            if conflicts_active or conflicts_queued:
                blocked_earlier.append(candidate)
                blocked_scopes[candidate["id"]] = candidate_scopes
                continue
            connection.execute(
                "UPDATE claims SET state = 'active', granted_at = ? WHERE id = ?",
                (now, candidate["id"]),
            )
            active.append(candidate)
            active_scopes[candidate["id"]] = candidate_scopes

    @staticmethod
    def _maintain(connection: sqlite3.Connection) -> None:
        now = time.time()
        WorkspaceCoordinator._expire_tasks(connection, now)
        workspace_ids = connection.execute("SELECT id FROM workspaces").fetchall()
        for workspace in workspace_ids:
            WorkspaceCoordinator._schedule_workspace(connection, workspace["id"], now)

    @staticmethod
    def _authenticate_task(
        connection: sqlite3.Connection,
        workspace_id: str,
        token: str,
        *,
        require_active: bool = True,
    ) -> sqlite3.Row:
        task = connection.execute(
            "SELECT * FROM tasks WHERE workspace_id = ? AND token_hash = ? "
            "ORDER BY created_at DESC LIMIT 1",
            (workspace_id, _token_hash(token)),
        ).fetchone()
        if task is None:
            raise AuthorizationError("Task token is invalid for this workspace.")
        if require_active and task["state"] != "active":
            raise AuthorizationError(
                f"Task is not active: {task['state']}.",
                details={"task_id": task["id"], "state": task["state"]},
            )
        return task

    @staticmethod
    def _normalize_writes(root: str, writes: Sequence[str]) -> tuple[str, ...]:
        root_path = Path(root)
        normalized: set[str] = set()
        for value in writes:
            if not value or not value.strip():
                raise UsageError("Write scopes cannot be empty.")
            candidate = Path(value)
            absolute = (
                candidate.expanduser().resolve(strict=False)
                if candidate.is_absolute()
                else (root_path / candidate).resolve(strict=False)
            )
            try:
                relative = absolute.relative_to(root_path)
            except ValueError as exc:
                raise UsageError(f"Write scope is outside the workspace: {value}") from exc
            text = PurePosixPath(relative).as_posix() or "."
            normalized.add(text.casefold())
        return tuple(sorted(normalized))

    @staticmethod
    def _normalize_resources(resources: Sequence[str]) -> tuple[str, ...]:
        normalized = {resource.strip().casefold() for resource in resources}
        if "" in normalized:
            raise UsageError("Resource names cannot be empty.")
        return tuple(sorted(normalized))

    @staticmethod
    def _public_task(task: sqlite3.Row) -> dict[str, Any]:
        return {
            "id": task["id"],
            "owner": task["owner"],
            "summary": task["summary"],
            "state": task["state"],
            "created_at": task["created_at"],
            "heartbeat_at": task["heartbeat_at"],
            "expires_at": task["expires_at"],
            "finished_at": task["finished_at"],
            "result": task["result"],
            "note": task["note"],
        }

    @staticmethod
    def _public_claim(connection: sqlite3.Connection, claim: sqlite3.Row) -> dict[str, Any]:
        scopes = WorkspaceCoordinator._claim_scopes(connection, claim["id"])
        return {
            "id": claim["id"],
            "task_id": claim["task_id"],
            "kind": claim["kind"],
            "state": claim["state"],
            "queue_order": claim["queue_order"],
            "writes": list(scopes["write"]),
            "resources": list(scopes["resource"]),
            "priority": WorkspaceCoordinator._claim_priority(claim, scopes),
            "parked_for": scopes["parked_for"][0] if scopes["parked_for"] else None,
            "created_at": claim["created_at"],
            "granted_at": claim["granted_at"],
        }

    def register(self, workspace: Path | str) -> dict[str, Any]:
        root = canonical_workspace(workspace)
        now = time.time()
        identifier = _workspace_id(root)
        with self._transaction() as connection:
            existing = connection.execute(
                "SELECT * FROM workspaces WHERE id = ?", (identifier,)
            ).fetchone()
            if existing is not None and existing["root"] != root:
                raise StateError("Workspace identity collision detected.")
            connection.execute(
                "INSERT OR IGNORE INTO workspaces(id, root, registered_at, epoch) "
                "VALUES(?, ?, ?, 1)",
                (identifier, root, now),
            )
            registered = connection.execute(
                "SELECT * FROM workspaces WHERE id = ?", (identifier,)
            ).fetchone()
            assert registered is not None
            return {
                "id": registered["id"],
                "root": registered["root"],
                "registered_at": registered["registered_at"],
                "epoch": registered["epoch"],
                "created": existing is None,
            }

    def unregister(self, workspace: Path | str) -> dict[str, Any]:
        root = canonical_workspace(workspace)
        with self._transaction() as connection:
            self._maintain(connection)
            registered = self._workspace(connection, root)
            open_task = connection.execute(
                "SELECT 1 FROM tasks WHERE workspace_id = ? "
                "AND state IN ('active', 'outcome_unknown') LIMIT 1",
                (registered["id"],),
            ).fetchone()
            open_claim = connection.execute(
                "SELECT 1 FROM claims WHERE workspace_id = ? "
                "AND state IN ('queued', 'active', 'parked') LIMIT 1",
                (registered["id"],),
            ).fetchone()
            if open_task is not None or open_claim is not None:
                raise BusyError("Workspace still has open tasks or claims.")
            connection.execute("DELETE FROM workspaces WHERE id = ?", (registered["id"],))
            return {"id": registered["id"], "root": root, "removed": True}

    def list_workspaces(self) -> dict[str, Any]:
        with self._transaction() as connection:
            self._maintain(connection)
            rows = connection.execute("SELECT * FROM workspaces ORDER BY root").fetchall()
            values = []
            for row in rows:
                active_tasks = connection.execute(
                    "SELECT COUNT(*) AS count FROM tasks WHERE workspace_id = ? "
                    "AND state IN ('active', 'outcome_unknown')",
                    (row["id"],),
                ).fetchone()["count"]
                open_claims = connection.execute(
                    "SELECT COUNT(*) AS count FROM claims WHERE workspace_id = ? "
                    "AND state IN ('queued', 'active', 'parked')",
                    (row["id"],),
                ).fetchone()["count"]
                values.append(
                    {
                        "id": row["id"],
                        "root": row["root"],
                        "registered_at": row["registered_at"],
                        "epoch": row["epoch"],
                        "active_tasks": active_tasks,
                        "open_claims": open_claims,
                    }
                )
            return {"schema_version": 1, "workspaces": values}

    def status(self, workspace: Path | str) -> dict[str, Any]:
        root = canonical_workspace(workspace)
        with self._transaction() as connection:
            self._maintain(connection)
            registered = self._workspace(connection, root)
            tasks = connection.execute(
                "SELECT * FROM tasks WHERE workspace_id = ? "
                "AND state IN ('active', 'outcome_unknown') ORDER BY created_at",
                (registered["id"],),
            ).fetchall()
            claims = connection.execute(
                "SELECT * FROM claims WHERE workspace_id = ? "
                "AND state IN ('queued', 'active', 'parked') ORDER BY queue_order",
                (registered["id"],),
            ).fetchall()
            blocked = any(task["state"] == "outcome_unknown" for task in tasks)
            public_tasks = []
            for task in tasks:
                public_task = self._public_task(task)
                drain = self._task_drain_request(connection, registered["id"], task["id"])
                if drain is not None:
                    public_task["drain_requested"] = drain
                public_tasks.append(public_task)
            return {
                "schema_version": 1,
                "coordination_mode": "required",
                "ready": not blocked,
                "blocked": blocked,
                "workspace": {
                    "id": registered["id"],
                    "root": registered["root"],
                    "registered_at": registered["registered_at"],
                    "epoch": registered["epoch"],
                },
                "tasks": public_tasks,
                "claims": [self._public_claim(connection, claim) for claim in claims],
            }

    def start_task(
        self,
        workspace: Path | str,
        owner: str,
        summary: str,
        *,
        ttl_seconds: float = DEFAULT_TASK_TTL_SECONDS,
        token: str | None = None,
    ) -> tuple[dict[str, Any], str]:
        root = canonical_workspace(workspace)
        if not owner.strip():
            raise UsageError("Task owner cannot be empty.")
        if not summary.strip():
            raise UsageError("Task summary cannot be empty.")
        if ttl_seconds <= 0:
            raise UsageError("Task TTL must be greater than zero.")
        secret = token or secrets.token_urlsafe(32)
        now = time.time()
        with self._transaction() as connection:
            self._maintain(connection)
            registered = self._workspace(connection, root)
            if self._unknown_exists(connection, registered["id"]):
                raise BusyError("Workspace is blocked by an unknown task outcome.")
            task_id = uuid.uuid4().hex
            connection.execute(
                "INSERT INTO tasks(id, workspace_id, owner, summary, token_hash, state, "
                "created_at, heartbeat_at, expires_at) "
                "VALUES(?, ?, ?, ?, ?, 'active', ?, ?, ?)",
                (
                    task_id,
                    registered["id"],
                    owner.strip(),
                    summary.strip(),
                    _token_hash(secret),
                    now,
                    now,
                    now + ttl_seconds,
                ),
            )
            self._touch(connection, registered["id"])
            task = connection.execute("SELECT * FROM tasks WHERE id = ?", (task_id,)).fetchone()
            assert task is not None
            return self._public_task(task), secret

    def heartbeat(
        self,
        workspace: Path | str,
        token: str,
        *,
        ttl_seconds: float = DEFAULT_TASK_TTL_SECONDS,
        note: str | None = None,
    ) -> dict[str, Any]:
        root = canonical_workspace(workspace)
        if ttl_seconds <= 0:
            raise UsageError("Task TTL must be greater than zero.")
        now = time.time()
        with self._transaction() as connection:
            self._maintain(connection)
            registered = self._workspace(connection, root)
            task = self._authenticate_task(connection, registered["id"], token)
            connection.execute(
                "UPDATE tasks SET heartbeat_at = ?, expires_at = ?, note = COALESCE(?, note) "
                "WHERE id = ?",
                (now, now + ttl_seconds, note, task["id"]),
            )
            self._touch(connection, registered["id"])
            updated = connection.execute(
                "SELECT * FROM tasks WHERE id = ?", (task["id"],)
            ).fetchone()
            assert updated is not None
            result = self._public_task(updated)
            drain = self._task_drain_request(connection, registered["id"], task["id"])
            if drain is not None:
                result["drain_requested"] = drain
            return result

    def release_task(
        self,
        workspace: Path | str,
        token: str,
        *,
        result: str,
        note: str | None = None,
    ) -> dict[str, Any]:
        root = canonical_workspace(workspace)
        if result not in {"completed", "failed", "outcome-unknown"}:
            raise UsageError("Task result must be completed, failed, or outcome-unknown.")
        now = time.time()
        with self._transaction() as connection:
            self._maintain(connection)
            registered = self._workspace(connection, root)
            task = self._authenticate_task(connection, registered["id"], token)
            open_freezes = connection.execute(
                "SELECT id, state FROM claims WHERE task_id = ? AND kind = 'freeze' "
                "AND state IN ('queued', 'active')",
                (task["id"],),
            ).fetchall()
            if result == "outcome-unknown":
                connection.execute(
                    "UPDATE claims SET state = 'cancelled', released_at = ? "
                    "WHERE task_id = ? AND state = 'queued'",
                    (now, task["id"]),
                )
                self._resume_parked_for_freezes(
                    connection,
                    [row["id"] for row in open_freezes if row["state"] == "queued"],
                )
                task_state = "outcome_unknown"
            else:
                connection.execute(
                    "UPDATE claims SET state = 'released', released_at = ? "
                    "WHERE task_id = ? AND state IN ('queued', 'active', 'parked')",
                    (now, task["id"]),
                )
                connection.execute(
                    "DELETE FROM claim_scopes WHERE scope_type = 'parked_for' "
                    "AND claim_id IN (SELECT id FROM claims WHERE task_id = ?)",
                    (task["id"],),
                )
                self._resume_parked_for_freezes(connection, [row["id"] for row in open_freezes])
                task_state = result
            connection.execute(
                "UPDATE tasks SET state = ?, finished_at = ?, result = ?, note = ? WHERE id = ?",
                (task_state, now, result, note, task["id"]),
            )
            self._touch(connection, registered["id"])
            self._schedule_workspace(connection, registered["id"], now)
            updated = connection.execute(
                "SELECT * FROM tasks WHERE id = ?", (task["id"],)
            ).fetchone()
            assert updated is not None
            return self._public_task(updated)

    def acquire_claim(
        self,
        workspace: Path | str,
        token: str,
        *,
        writes: Sequence[str] = (),
        resources: Sequence[str] = (),
        freeze: bool = False,
        priority: str = "normal",
        wait_seconds: float = 0.0,
        keep_queued: bool = False,
    ) -> dict[str, Any]:
        root = canonical_workspace(workspace)
        normalized_writes = self._normalize_writes(root, writes)
        normalized_resources = self._normalize_resources(resources)
        if freeze and (normalized_writes or normalized_resources):
            raise UsageError("A freeze claim cannot include write or resource scopes.")
        if not freeze and not normalized_writes and not normalized_resources:
            raise UsageError("A claim needs at least one write path or resource.")
        if priority not in {"normal", "urgent"}:
            raise UsageError("Claim priority must be normal or urgent.")
        if not freeze and priority != "normal":
            raise UsageError("Urgent priority is only supported for freeze claims.")
        if wait_seconds < 0:
            raise UsageError("Claim wait must not be negative.")
        now = time.time()
        with self._transaction() as connection:
            self._maintain(connection)
            registered = self._workspace(connection, root)
            if self._unknown_exists(connection, registered["id"]):
                raise BusyError("Workspace is blocked by an unknown task outcome.")
            task = self._authenticate_task(connection, registered["id"], token)
            parked = connection.execute(
                "SELECT id FROM claims WHERE task_id = ? AND state = 'parked' LIMIT 1",
                (task["id"],),
            ).fetchone()
            if parked is not None:
                raise BusyError(
                    "Task claims are parked for workspace maintenance.",
                    details={"reason": "task-parked", "claim_id": parked["id"]},
                )
            order = connection.execute(
                "SELECT COALESCE(MAX(queue_order), 0) + 1 AS next_order FROM claims"
            ).fetchone()["next_order"]
            claim_id = uuid.uuid4().hex
            connection.execute(
                "INSERT INTO claims(id, workspace_id, task_id, kind, state, queue_order, created_at) "
                "VALUES(?, ?, ?, ?, 'queued', ?, ?)",
                (
                    claim_id,
                    registered["id"],
                    task["id"],
                    "freeze" if freeze else "normal",
                    order,
                    now,
                ),
            )
            connection.executemany(
                "INSERT INTO claim_scopes(claim_id, scope_type, value) VALUES(?, 'write', ?)",
                ((claim_id, value) for value in normalized_writes),
            )
            connection.executemany(
                "INSERT INTO claim_scopes(claim_id, scope_type, value) VALUES(?, 'resource', ?)",
                ((claim_id, value) for value in normalized_resources),
            )
            if priority == "urgent":
                connection.execute(
                    "INSERT INTO claim_scopes(claim_id, scope_type, value) "
                    "VALUES(?, 'priority', 'urgent')",
                    (claim_id,),
                )
            self._touch(connection, registered["id"])
            self._schedule_workspace(connection, registered["id"], now)
            claim = connection.execute("SELECT * FROM claims WHERE id = ?", (claim_id,)).fetchone()
            assert claim is not None
            result = self._public_claim(connection, claim)
        if result["state"] != "queued" or wait_seconds == 0:
            result["granted"] = result["state"] == "active"
            return result
        deadline = time.monotonic() + wait_seconds
        while time.monotonic() < deadline:
            time.sleep(min(0.1, max(0.0, deadline - time.monotonic())))
            with self._transaction() as connection:
                self._maintain(connection)
                registered = self._workspace(connection, root)
                self._authenticate_task(connection, registered["id"], token)
                claim = connection.execute(
                    "SELECT * FROM claims WHERE id = ?", (claim_id,)
                ).fetchone()
                if claim is None:
                    raise StateError("Claim disappeared from scheduler state.")
                result = self._public_claim(connection, claim)
            if result["state"] != "queued":
                result["granted"] = result["state"] == "active"
                return result
        if not keep_queued:
            with self._transaction() as connection:
                registered = self._workspace(connection, root)
                self._authenticate_task(connection, registered["id"], token)
                connection.execute(
                    "UPDATE claims SET state = 'cancelled', released_at = ? "
                    "WHERE id = ? AND state = 'queued'",
                    (time.time(), claim_id),
                )
                if freeze:
                    self._resume_parked_for_freezes(connection, (claim_id,))
                self._touch(connection, registered["id"])
                self._schedule_workspace(connection, registered["id"], time.time())
                claim = connection.execute(
                    "SELECT * FROM claims WHERE id = ?", (claim_id,)
                ).fetchone()
                assert claim is not None
                result = self._public_claim(connection, claim)
        result["granted"] = False
        result["timed_out"] = True
        return result

    def park_task(
        self,
        workspace: Path | str,
        token: str,
        *,
        wait_seconds: float = 0.0,
    ) -> dict[str, Any]:
        root = canonical_workspace(workspace)
        if wait_seconds < 0:
            raise UsageError("Task park wait must not be negative.")
        with self._transaction() as connection:
            self._maintain(connection)
            registered = self._workspace(connection, root)
            task = self._authenticate_task(connection, registered["id"], token)
            existing_parked = connection.execute(
                "SELECT * FROM claims WHERE workspace_id = ? AND task_id = ? "
                "AND state = 'parked' ORDER BY queue_order",
                (registered["id"], task["id"]),
            ).fetchall()
            if existing_parked:
                parked_for = {
                    self._claim_scopes(connection, claim["id"])["parked_for"]
                    for claim in existing_parked
                }
                if len(parked_for) != 1 or len(next(iter(parked_for))) != 1:
                    raise StateError("Parked claims have inconsistent freeze ownership.")
                freeze_id = next(iter(next(iter(parked_for))))
                target_freeze = connection.execute(
                    "SELECT * FROM claims WHERE id = ? AND workspace_id = ? "
                    "AND kind = 'freeze' AND state IN ('queued', 'active')",
                    (freeze_id, registered["id"]),
                ).fetchone()
                if target_freeze is None:
                    raise StateError("Parked claims reference a closed freeze.")
                parked_claims = existing_parked
            else:
                drain = self._task_drain_request(connection, registered["id"], task["id"])
                if drain is None:
                    raise StateError(
                        "No queued freeze is requesting this task to drain.",
                        details={"reason": "freeze-drain-not-requested"},
                    )
                target_freeze = connection.execute(
                    "SELECT * FROM claims WHERE id = ?",
                    (drain["freeze_id"],),
                ).fetchone()
                assert target_freeze is not None
                owned = connection.execute(
                    "SELECT * FROM claims WHERE workspace_id = ? AND task_id = ? "
                    "AND state IN ('queued', 'active') ORDER BY queue_order",
                    (registered["id"], task["id"]),
                ).fetchall()
                if not owned:
                    raise StateError(
                        "Task has no open claims to park.",
                        details={"reason": "task-claimless"},
                    )
                target_scopes = self._claim_scopes(connection, target_freeze["id"])
                target_key = self._claim_sort_key(target_freeze, target_scopes)
                parked_claims = [
                    claim
                    for claim in owned
                    if claim["state"] == "active"
                    or self._claim_sort_key(claim, self._claim_scopes(connection, claim["id"]))
                    < target_key
                ]
                unsafe: list[dict[str, Any]] = []
                for claim in parked_claims:
                    scopes = self._claim_scopes(connection, claim["id"])
                    if claim["kind"] == "freeze" or scopes["resource"]:
                        unsafe.append(
                            {
                                "claim_id": claim["id"],
                                "kind": claim["kind"],
                                "resources": list(scopes["resource"]),
                            }
                        )
                if unsafe:
                    raise BusyError(
                        "Task still owns claims that cannot be parked safely.",
                        details={"reason": "task-holds-unsafe-claims", "claims": unsafe},
                    )
                freeze_id = target_freeze["id"]
                claim_ids = [claim["id"] for claim in parked_claims]
                placeholders = ", ".join("?" for _ in claim_ids)
                connection.execute(
                    f"UPDATE claims SET state = 'parked' WHERE id IN ({placeholders})",
                    claim_ids,
                )
                connection.executemany(
                    "INSERT INTO claim_scopes(claim_id, scope_type, value) "
                    "VALUES(?, 'parked_for', ?)",
                    ((claim_id, freeze_id) for claim_id in claim_ids),
                )
                self._touch(connection, registered["id"])
                self._schedule_workspace(connection, registered["id"], time.time())
            claim_ids = [claim["id"] for claim in parked_claims]

        def result_payload(*, timed_out: bool = False) -> dict[str, Any]:
            with self._transaction() as connection:
                self._maintain(connection)
                registered = self._workspace(connection, root)
                self._authenticate_task(connection, registered["id"], token)
                placeholders = ", ".join("?" for _ in claim_ids)
                claims = connection.execute(
                    f"SELECT * FROM claims WHERE id IN ({placeholders}) ORDER BY queue_order",
                    claim_ids,
                ).fetchall()
                states = {claim["id"]: claim["state"] for claim in claims}
                resumed = len(claims) == len(claim_ids) and all(
                    state == "active" for state in states.values()
                )
                return {
                    "task_id": task["id"],
                    "freeze_id": freeze_id,
                    "claim_ids": claim_ids,
                    "states": states,
                    "parked": not resumed,
                    "resumed": resumed,
                    "timed_out": timed_out,
                }

        result = result_payload()
        if wait_seconds == 0 or result["resumed"]:
            return result
        deadline = time.monotonic() + wait_seconds
        while time.monotonic() < deadline:
            time.sleep(min(0.1, max(0.0, deadline - time.monotonic())))
            result = result_payload()
            if result["resumed"]:
                return result
        return result_payload(timed_out=True)

    def release_claim(self, workspace: Path | str, token: str, claim_id: str) -> dict[str, Any]:
        root = canonical_workspace(workspace)
        now = time.time()
        with self._transaction() as connection:
            self._maintain(connection)
            registered = self._workspace(connection, root)
            task = self._authenticate_task(connection, registered["id"], token)
            claim = connection.execute(
                "SELECT * FROM claims WHERE id = ? AND workspace_id = ?",
                (claim_id, registered["id"]),
            ).fetchone()
            if claim is None or claim["task_id"] != task["id"]:
                raise AuthorizationError("Claim is not owned by this task.")
            if claim["state"] not in OPEN_CLAIM_STATES:
                raise StateError(f"Claim is already {claim['state']}.")
            next_state = "cancelled" if claim["state"] == "queued" else "released"
            connection.execute(
                "UPDATE claims SET state = ?, released_at = ? WHERE id = ?",
                (next_state, now, claim_id),
            )
            if claim["state"] == "parked":
                connection.execute(
                    "DELETE FROM claim_scopes WHERE claim_id = ? AND scope_type = 'parked_for'",
                    (claim_id,),
                )
            if claim["kind"] == "freeze":
                self._resume_parked_for_freezes(connection, (claim_id,))
            self._touch(connection, registered["id"])
            self._schedule_workspace(connection, registered["id"], now)
            updated = connection.execute(
                "SELECT * FROM claims WHERE id = ?", (claim_id,)
            ).fetchone()
            assert updated is not None
            return self._public_claim(connection, updated)

    def assert_claims(
        self,
        workspace: Path | str,
        token: str,
        *,
        writes: Sequence[str] = (),
        resources: Sequence[str] = (),
        freeze: bool = False,
    ) -> dict[str, Any]:
        root = canonical_workspace(workspace)
        normalized_writes = self._normalize_writes(root, writes)
        normalized_resources = self._normalize_resources(resources)
        with self._transaction() as connection:
            self._maintain(connection)
            registered = self._workspace(connection, root)
            task = self._authenticate_task(connection, registered["id"], token)
            drain = self._task_drain_request(connection, registered["id"], task["id"])
            if drain is not None:
                raise BusyError(
                    "Workspace freeze is waiting for this task to park its claims.",
                    details={"reason": "freeze-drain-requested", **drain},
                )
            claims = connection.execute(
                "SELECT * FROM claims WHERE workspace_id = ? AND task_id = ? AND state = 'active'",
                (registered["id"], task["id"]),
            ).fetchall()
            scopes = [self._claim_scopes(connection, claim["id"]) for claim in claims]
            claimed_writes = [value for scope in scopes for value in scope["write"]]
            claimed_resources = {value for scope in scopes for value in scope["resource"]}
            missing_writes = [
                requested
                for requested in normalized_writes
                if not any(
                    claimed == "." or requested == claimed or requested.startswith(claimed + "/")
                    for claimed in claimed_writes
                )
            ]
            missing_resources = [
                requested
                for requested in normalized_resources
                if requested not in claimed_resources
            ]
            has_freeze = any(claim["kind"] == "freeze" for claim in claims)
            if missing_writes or missing_resources or (freeze and not has_freeze):
                raise AuthorizationError(
                    "Task does not own all requested claims.",
                    details={
                        "missing_writes": missing_writes,
                        "missing_resources": missing_resources,
                        "missing_freeze": freeze and not has_freeze,
                    },
                )
            return {
                "task_id": task["id"],
                "authorized": True,
                "writes": list(normalized_writes),
                "resources": list(normalized_resources),
                "freeze": freeze,
            }

    def resolve_unknown(
        self,
        workspace: Path | str,
        task_id: str,
        *,
        resolution: str,
        evidence: str,
    ) -> dict[str, Any]:
        root = canonical_workspace(workspace)
        if resolution not in {"completed", "failed"}:
            raise UsageError("Recovery resolution must be completed or failed.")
        if not evidence.strip():
            raise UsageError("Recovery evidence cannot be empty.")
        now = time.time()
        with self._transaction() as connection:
            self._maintain(connection)
            registered = self._workspace(connection, root)
            task = connection.execute(
                "SELECT * FROM tasks WHERE id = ? AND workspace_id = ?",
                (task_id, registered["id"]),
            ).fetchone()
            if task is None or task["state"] != "outcome_unknown":
                raise StateError("Task is not waiting for unknown-outcome recovery.")
            open_freezes = connection.execute(
                "SELECT id FROM claims WHERE task_id = ? AND kind = 'freeze' "
                "AND state IN ('queued', 'active')",
                (task_id,),
            ).fetchall()
            connection.execute(
                "UPDATE claims SET state = 'released', released_at = ? "
                "WHERE task_id = ? AND state IN ('queued', 'active', 'parked')",
                (now, task_id),
            )
            connection.execute(
                "DELETE FROM claim_scopes WHERE scope_type = 'parked_for' "
                "AND claim_id IN (SELECT id FROM claims WHERE task_id = ?)",
                (task_id,),
            )
            self._resume_parked_for_freezes(connection, [row["id"] for row in open_freezes])
            connection.execute(
                "UPDATE tasks SET state = ?, result = ?, finished_at = ?, note = ? WHERE id = ?",
                (resolution, f"recovered-{resolution}", now, evidence.strip(), task_id),
            )
            connection.execute(
                "INSERT INTO recovery_events(id, workspace_id, task_id, resolution, evidence, created_at) "
                "VALUES(?, ?, ?, ?, ?, ?)",
                (
                    uuid.uuid4().hex,
                    registered["id"],
                    task_id,
                    resolution,
                    evidence.strip(),
                    now,
                ),
            )
            self._touch(connection, registered["id"])
            self._schedule_workspace(connection, registered["id"], now)
            updated = connection.execute("SELECT * FROM tasks WHERE id = ?", (task_id,)).fetchone()
            assert updated is not None
            return self._public_task(updated)
