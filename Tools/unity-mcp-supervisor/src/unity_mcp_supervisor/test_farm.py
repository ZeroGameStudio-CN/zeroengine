from __future__ import annotations

import json
import os
import shutil
import sqlite3
import time
import uuid
from collections.abc import Callable, Iterator
from contextlib import contextmanager
from dataclasses import dataclass
from pathlib import Path
from typing import Any

from .errors import ProjectBusyError, UsageError
from .service_state import StatePaths, ensure_private_directory, process_alive

JOB_TERMINAL_STATES = frozenset(
    {"passed", "failed", "infra_failed", "cancelled", "outcome_unknown"}
)
JOB_OPEN_STATES = frozenset({"queued", "running"})
SLOT_ACTIVE_STATES = frozenset({"available", "running", "quarantined"})


@dataclass(frozen=True)
class TestJobRequest:
    project_root: str
    task_id: str
    platform: str
    filters: tuple[str, ...] = ()
    categories: tuple[str, ...] = ()
    assemblies: tuple[str, ...] = ()
    artifact_root: str = ""
    snapshot_id: str = ""
    snapshot_manifest: str = ""
    timeout_seconds: float = 900


@dataclass(frozen=True)
class WorkerResult:
    state: str
    summary: dict[str, Any]
    quarantine: bool = False


class TestFarmStore:
    def __init__(self, paths: StatePaths) -> None:
        self.paths = paths
        self.paths.ensure()
        self._initialize()

    @contextmanager
    def _connection(self, *, immediate: bool = False) -> Iterator[sqlite3.Connection]:
        connection = sqlite3.connect(self.paths.test_farm_database, timeout=30)
        connection.row_factory = sqlite3.Row
        connection.execute("PRAGMA foreign_keys = ON")
        connection.execute("PRAGMA busy_timeout = 30000")
        try:
            if immediate:
                connection.execute("BEGIN IMMEDIATE")
            yield connection
            connection.commit()
        except Exception:
            connection.rollback()
            raise
        finally:
            connection.close()

    def _initialize(self) -> None:
        with self._connection() as connection:
            connection.executescript(
                """
                CREATE TABLE IF NOT EXISTS farm_config(
                    key TEXT PRIMARY KEY,
                    value TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS slots(
                    slot_id INTEGER PRIMARY KEY,
                    root TEXT NOT NULL UNIQUE,
                    state TEXT NOT NULL,
                    job_id TEXT,
                    worker_pid INTEGER,
                    last_error TEXT,
                    updated_at REAL NOT NULL
                );
                CREATE TABLE IF NOT EXISTS jobs(
                    job_id TEXT PRIMARY KEY,
                    project_root TEXT NOT NULL,
                    task_id TEXT NOT NULL,
                    platform TEXT NOT NULL,
                    filters_json TEXT NOT NULL,
                    categories_json TEXT NOT NULL,
                    assemblies_json TEXT NOT NULL,
                    artifact_root TEXT NOT NULL,
                    snapshot_id TEXT NOT NULL,
                    snapshot_manifest TEXT NOT NULL,
                    timeout_seconds REAL NOT NULL,
                    state TEXT NOT NULL,
                    queue_order INTEGER NOT NULL UNIQUE,
                    slot_id INTEGER,
                    worker_pid INTEGER,
                    cancel_requested INTEGER NOT NULL DEFAULT 0,
                    created_at REAL NOT NULL,
                    started_at REAL,
                    finished_at REAL,
                    result_json TEXT,
                    error TEXT,
                    FOREIGN KEY(slot_id) REFERENCES slots(slot_id)
                );
                CREATE INDEX IF NOT EXISTS jobs_state_queue
                    ON jobs(state, queue_order);
                """
            )

    def _config(self, connection: sqlite3.Connection) -> dict[str, str]:
        return {
            str(row["key"]): str(row["value"])
            for row in connection.execute("SELECT key, value FROM farm_config")
        }

    def provision(self, workers: int, slot_root: Path | None = None) -> dict[str, Any]:
        if workers < 1:
            raise UsageError("Test farm workers must be at least one.")
        root = (slot_root or self.paths.test_farm_slots).expanduser().resolve()
        ensure_private_directory(root)
        now = time.time()
        with self._connection(immediate=True) as connection:
            config = self._config(connection)
            active = connection.execute(
                "SELECT slot_id FROM slots WHERE state = 'running'"
            ).fetchall()
            configured_root = config.get("slot_root")
            if active and configured_root and Path(configured_root) != root:
                raise ProjectBusyError(
                    "Cannot move the test farm slot root while jobs are running.",
                    details={"slot_ids": [row["slot_id"] for row in active]},
                )
            running = connection.execute(
                "SELECT slot_id FROM slots WHERE state = 'running' AND slot_id > ?",
                (workers,),
            ).fetchall()
            if running:
                raise ProjectBusyError(
                    "Cannot shrink the test farm while removed slots are running.",
                    details={"slot_ids": [row["slot_id"] for row in running]},
                )
            for slot_id in range(1, workers + 1):
                slot = root / f"slot-{slot_id:02d}"
                existing = connection.execute(
                    "SELECT root, state FROM slots WHERE slot_id = ?", (slot_id,)
                ).fetchone()
                if (
                    existing is not None
                    and existing["state"] == "quarantined"
                    and Path(existing["root"]) == slot
                ):
                    if slot.is_symlink():
                        raise UsageError(
                            "Refusing to repair a symbolic-link test slot."
                        )
                    try:
                        slot.resolve(strict=False).relative_to(root)
                    except ValueError as exc:
                        raise UsageError(
                            "Refusing to repair a test slot outside its configured root."
                        ) from exc
                    if slot.is_dir():
                        shutil.rmtree(slot)
                    elif slot.exists():
                        slot.unlink()
                ensure_private_directory(slot)
                connection.execute(
                    """
                    INSERT INTO slots(slot_id, root, state, updated_at)
                    VALUES(?, ?, 'available', ?)
                    ON CONFLICT(slot_id) DO UPDATE SET
                        root = excluded.root,
                        state = CASE
                            WHEN slots.state = 'running' THEN slots.state
                            ELSE 'available'
                        END,
                        job_id = CASE
                            WHEN slots.state = 'running' THEN slots.job_id
                            ELSE NULL
                        END,
                        worker_pid = CASE
                            WHEN slots.state = 'running' THEN slots.worker_pid
                            ELSE NULL
                        END,
                        last_error = CASE
                            WHEN slots.state = 'running' THEN slots.last_error
                            ELSE NULL
                        END,
                        updated_at = excluded.updated_at
                    """,
                    (slot_id, str(slot), now),
                )
            connection.execute(
                "DELETE FROM slots WHERE slot_id > ? AND state <> 'running'", (workers,)
            )
            for key, value in (
                ("workers", str(workers)),
                ("slot_root", str(root)),
                ("updated_at", str(now)),
            ):
                connection.execute(
                    "INSERT OR REPLACE INTO farm_config(key, value) VALUES(?, ?)",
                    (key, value),
                )
        return self.status()

    def is_provisioned(self) -> bool:
        with self._connection() as connection:
            return "workers" in self._config(connection)

    def submit(self, request: TestJobRequest) -> dict[str, Any]:
        if request.platform not in {"EditMode", "PlayMode"}:
            raise UsageError("Unity test platform must be EditMode or PlayMode.")
        if not (request.filters or request.categories or request.assemblies):
            raise UsageError("An exact test, category, or assembly filter is required.")
        if request.timeout_seconds <= 0:
            raise UsageError("Unity test timeout must be greater than zero.")
        with self._connection(immediate=True) as connection:
            if "workers" not in self._config(connection):
                raise UsageError("Test farm is not provisioned.")
            job_id = f"test-{uuid.uuid4().hex[:16]}"
            queue_order = int(
                connection.execute(
                    "SELECT COALESCE(MAX(queue_order), 0) + 1 FROM jobs"
                ).fetchone()[0]
            )
            connection.execute(
                """
                INSERT INTO jobs(
                    job_id, project_root, task_id, platform, filters_json,
                    categories_json, assemblies_json, artifact_root, snapshot_id,
                    snapshot_manifest, timeout_seconds, state, queue_order, created_at
                ) VALUES(?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, 'queued', ?, ?)
                """,
                (
                    job_id,
                    request.project_root,
                    request.task_id,
                    request.platform,
                    json.dumps(request.filters),
                    json.dumps(request.categories),
                    json.dumps(request.assemblies),
                    request.artifact_root,
                    request.snapshot_id,
                    request.snapshot_manifest,
                    request.timeout_seconds,
                    queue_order,
                    time.time(),
                ),
            )
        return self.job(job_id)

    def claim_next(self, worker_pid: int | None = None) -> dict[str, Any] | None:
        pid = worker_pid or os.getpid()
        with self._connection(immediate=True) as connection:
            slot = connection.execute(
                "SELECT * FROM slots WHERE state = 'available' ORDER BY slot_id LIMIT 1"
            ).fetchone()
            job = connection.execute(
                "SELECT * FROM jobs WHERE state = 'queued' ORDER BY queue_order LIMIT 1"
            ).fetchone()
            if slot is None or job is None:
                return None
            now = time.time()
            connection.execute(
                """
                UPDATE slots SET state = 'running', job_id = ?, worker_pid = ?,
                    last_error = NULL, updated_at = ? WHERE slot_id = ?
                """,
                (job["job_id"], pid, now, slot["slot_id"]),
            )
            connection.execute(
                """
                UPDATE jobs SET state = 'running', slot_id = ?, worker_pid = ?,
                    started_at = ? WHERE job_id = ?
                """,
                (slot["slot_id"], pid, now, job["job_id"]),
            )
            claimed = connection.execute(
                "SELECT * FROM jobs WHERE job_id = ?", (job["job_id"],)
            ).fetchone()
            return self._public_job(connection, claimed)

    def finish(self, job_id: str, result: WorkerResult) -> dict[str, Any]:
        if result.state not in JOB_TERMINAL_STATES - {"cancelled"}:
            raise UsageError(f"Invalid test job result state: {result.state}")
        with self._connection(immediate=True) as connection:
            job = connection.execute(
                "SELECT * FROM jobs WHERE job_id = ?", (job_id,)
            ).fetchone()
            if job is None:
                raise UsageError(f"Unknown test job: {job_id}")
            if job["state"] != "running":
                raise ProjectBusyError("Only a running test job can be finished.")
            final_state = "cancelled" if job["cancel_requested"] else result.state
            now = time.time()
            connection.execute(
                """
                UPDATE jobs SET state = ?, finished_at = ?, result_json = ?,
                    error = ? WHERE job_id = ?
                """,
                (
                    final_state,
                    now,
                    json.dumps(result.summary, sort_keys=True),
                    result.summary.get("error"),
                    job_id,
                ),
            )
            if job["slot_id"] is not None:
                connection.execute(
                    """
                    UPDATE slots SET state = ?, job_id = NULL, worker_pid = NULL,
                        last_error = ?, updated_at = ? WHERE slot_id = ?
                    """,
                    (
                        "quarantined" if result.quarantine else "available",
                        result.summary.get("error") if result.quarantine else None,
                        now,
                        job["slot_id"],
                    ),
                )
            updated = connection.execute(
                "SELECT * FROM jobs WHERE job_id = ?", (job_id,)
            ).fetchone()
            return self._public_job(connection, updated)

    def cancel(self, job_id: str, task_id: str) -> dict[str, Any]:
        with self._connection(immediate=True) as connection:
            job = connection.execute(
                "SELECT * FROM jobs WHERE job_id = ?", (job_id,)
            ).fetchone()
            if job is None:
                raise UsageError(f"Unknown test job: {job_id}")
            if job["task_id"] != task_id:
                raise UsageError(
                    "Only the submitting workspace task can cancel this job."
                )
            if job["state"] == "queued":
                connection.execute(
                    "UPDATE jobs SET state = 'cancelled', finished_at = ? WHERE job_id = ?",
                    (time.time(), job_id),
                )
            elif job["state"] == "running":
                connection.execute(
                    "UPDATE jobs SET cancel_requested = 1 WHERE job_id = ?", (job_id,)
                )
            updated = connection.execute(
                "SELECT * FROM jobs WHERE job_id = ?", (job_id,)
            ).fetchone()
            return self._public_job(connection, updated)

    def recover_dead_workers(self) -> list[str]:
        recovered: list[str] = []
        with self._connection(immediate=True) as connection:
            rows = connection.execute(
                "SELECT * FROM jobs WHERE state = 'running'"
            ).fetchall()
            for job in rows:
                if process_alive(job["worker_pid"]):
                    continue
                now = time.time()
                connection.execute(
                    """
                    UPDATE jobs SET state = 'outcome_unknown', finished_at = ?,
                        error = 'worker process exited without a terminal result'
                    WHERE job_id = ?
                    """,
                    (now, job["job_id"]),
                )
                connection.execute(
                    """
                    UPDATE slots SET state = 'quarantined', job_id = NULL,
                        worker_pid = NULL,
                        last_error = 'worker process exited without a terminal result',
                        updated_at = ? WHERE slot_id = ?
                    """,
                    (now, job["slot_id"]),
                )
                recovered.append(str(job["job_id"]))
        return recovered

    def job(self, job_id: str) -> dict[str, Any]:
        with self._connection() as connection:
            row = connection.execute(
                "SELECT * FROM jobs WHERE job_id = ?", (job_id,)
            ).fetchone()
            if row is None:
                raise UsageError(f"Unknown test job: {job_id}")
            return self._public_job(connection, row)

    def wait(
        self, job_id: str, timeout_seconds: float, poll_seconds: float = 0.1
    ) -> dict[str, Any]:
        if timeout_seconds < 0:
            raise UsageError("Test wait timeout cannot be negative.")
        deadline = time.monotonic() + timeout_seconds
        while True:
            self.recover_dead_workers()
            value = self.job(job_id)
            if value["state"] in JOB_TERMINAL_STATES:
                return value
            if time.monotonic() >= deadline:
                raise ProjectBusyError(
                    "Test job did not finish within the wait budget.",
                    details={"job_id": job_id, "state": value["state"]},
                )
            time.sleep(min(poll_seconds, max(0.0, deadline - time.monotonic())))

    def status(self) -> dict[str, Any]:
        with self._connection() as connection:
            config = self._config(connection)
            slots = [
                self._public_slot(row)
                for row in connection.execute("SELECT * FROM slots ORDER BY slot_id")
            ]
            queued = int(
                connection.execute(
                    "SELECT COUNT(*) FROM jobs WHERE state = 'queued'"
                ).fetchone()[0]
            )
            running = int(
                connection.execute(
                    "SELECT COUNT(*) FROM jobs WHERE state = 'running'"
                ).fetchone()[0]
            )
            return {
                "schema_version": 1,
                "provisioned": "workers" in config,
                "workers": int(config.get("workers", "0")),
                "slot_root": config.get("slot_root"),
                "queued": queued,
                "running": running,
                "slots": slots,
            }

    @staticmethod
    def _public_slot(row: sqlite3.Row) -> dict[str, Any]:
        return {
            "slot_id": row["slot_id"],
            "root": row["root"],
            "state": row["state"],
            "job_id": row["job_id"],
            "worker_pid": row["worker_pid"],
            "last_error": row["last_error"],
            "updated_at": row["updated_at"],
        }

    @staticmethod
    def _public_job(connection: sqlite3.Connection, row: sqlite3.Row) -> dict[str, Any]:
        queue_position = None
        if row["state"] == "queued":
            queue_position = int(
                connection.execute(
                    """
                SELECT COUNT(*) FROM jobs
                WHERE state = 'queued' AND queue_order <= ?
                """,
                    (row["queue_order"],),
                ).fetchone()[0]
            )
        return {
            "schema_version": 1,
            "job_id": row["job_id"],
            "project_root": row["project_root"],
            "task_id": row["task_id"],
            "platform": row["platform"],
            "filters": json.loads(row["filters_json"]),
            "categories": json.loads(row["categories_json"]),
            "assemblies": json.loads(row["assemblies_json"]),
            "artifact_root": row["artifact_root"],
            "snapshot_id": row["snapshot_id"],
            "snapshot_manifest": row["snapshot_manifest"],
            "timeout_seconds": row["timeout_seconds"],
            "state": row["state"],
            "queue_position": queue_position,
            "slot_id": row["slot_id"],
            "worker_pid": row["worker_pid"],
            "cancel_requested": bool(row["cancel_requested"]),
            "created_at": row["created_at"],
            "started_at": row["started_at"],
            "finished_at": row["finished_at"],
            "result": json.loads(row["result_json"]) if row["result_json"] else None,
            "error": row["error"],
        }


class TestFarmWorker:
    def __init__(
        self,
        store: TestFarmStore,
        execute: Callable[[dict[str, Any], dict[str, Any]], WorkerResult],
    ) -> None:
        self.store = store
        self.execute = execute

    def run_once(self, worker_pid: int | None = None) -> dict[str, Any] | None:
        job = self.store.claim_next(worker_pid)
        if job is None:
            return None
        slot = next(
            value
            for value in self.store.status()["slots"]
            if value["slot_id"] == job["slot_id"]
        )
        try:
            result = self.execute(job, slot)
        except Exception as exc:  # noqa: BLE001 - worker boundary must contain plugins.
            result = WorkerResult(
                "infra_failed", {"error": f"worker exception: {exc}"}, quarantine=True
            )
        return self.store.finish(job["job_id"], result)


def open_test_jobs(paths: StatePaths, project_root: str) -> list[dict[str, Any]]:
    if not paths.test_farm_database.is_file():
        return []
    connection: sqlite3.Connection | None = None
    try:
        connection = sqlite3.connect(paths.test_farm_database, timeout=5)
        connection.row_factory = sqlite3.Row
        rows = connection.execute(
            """
            SELECT job_id, state, task_id FROM jobs
            WHERE project_root = ? AND state IN ('queued', 'running')
            ORDER BY queue_order
            """,
            (project_root,),
        ).fetchall()
    except sqlite3.DatabaseError as exc:
        raise UsageError("Cannot inspect active Unity test farm jobs.") from exc
    finally:
        if connection is not None:
            connection.close()
    return [dict(row) for row in rows]
