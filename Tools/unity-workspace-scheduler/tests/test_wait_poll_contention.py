from __future__ import annotations

import sqlite3
import threading
import time
import uuid
from concurrent.futures import ThreadPoolExecutor
from contextlib import contextmanager
from pathlib import Path

from unity_workspace_scheduler.coordinator import WorkspaceCoordinator
from unity_workspace_scheduler.state import open_database, resolve_state_paths


class ObservedCoordinator(WorkspaceCoordinator):
    def __init__(self, paths):
        super().__init__(paths)
        self.writes = 0
        self.reads = threading.Event()

    @contextmanager
    def _transaction(self):
        self.writes += 1
        with super()._transaction() as connection:
            yield connection

    def _unchanged_queued_wait(self, *args):
        unchanged = super()._unchanged_queued_wait(*args)
        if unchanged:
            self.reads.set()
        return unchanged


def fixture(tmp_path: Path):
    scheduler = ObservedCoordinator(resolve_state_paths(tmp_path / "state"))
    workspace = tmp_path / "project"
    workspace.mkdir()
    scheduler.register(workspace)
    _, owner = scheduler.start_task(workspace, "owner", "private writer", ttl_seconds=60)
    _, waiter = scheduler.start_task(workspace, "waiter", "private waiter", ttl_seconds=60)
    held = scheduler.acquire_claim(workspace, owner, resources=("unity-live",))
    return scheduler, workspace, owner, waiter, held


def test_unchanged_wait_keeps_original_queue_without_repeated_writer_transactions(tmp_path):
    scheduler, workspace, _, waiter, _ = fixture(tmp_path)
    scheduler.writes = 0
    operation = str(uuid.uuid4())
    result = scheduler.acquire_claim(
        workspace,
        waiter,
        resources=("unity-live",),
        wait_seconds=0.8,
        keep_queued=True,
        operation_id=operation,
    )
    assert result["state"] == "queued" and result["timed_out"] is True
    assert scheduler.reads.is_set()
    assert scheduler.writes == 2  # Initial admission and the original deadline only.
    replay = scheduler.acquire_claim(
        workspace,
        waiter,
        resources=("unity-live",),
        wait_seconds=0.8,
        keep_queued=True,
        operation_id=operation,
    )
    assert replay["id"] == result["id"]
    assert replay["queue_order"] == result["queue_order"]


def test_wal_wait_observation_needs_no_writer_lock_and_handoff_keeps_same_claim(tmp_path):
    scheduler, workspace, owner, waiter, held = fixture(tmp_path)
    with ThreadPoolExecutor(max_workers=1) as pool:
        future = pool.submit(
            scheduler.acquire_claim,
            workspace,
            waiter,
            resources=("unity-live",),
            wait_seconds=3,
            keep_queued=True,
            operation_id=str(uuid.uuid4()),
        )
        assert scheduler.reads.wait(timeout=2)
        connection = open_database(scheduler.paths)
        try:
            queued = connection.execute("SELECT id FROM claims WHERE state = 'queued'").fetchone()[
                "id"
            ]
            connection.execute("BEGIN IMMEDIATE")
            scheduler.reads.clear()
            assert scheduler.reads.wait(
                timeout=1
            )  # A held writer cannot block this unchanged read.
            assert not future.done()
            connection.rollback()
        finally:
            connection.close()
        scheduler.release_claim(workspace, owner, held["id"])
        result = future.result(timeout=3)
    assert result["id"] == queued and result["state"] == "active"


def test_expired_waiter_still_gets_its_original_terminal_receipt(tmp_path):
    scheduler, workspace, _, _, _ = fixture(tmp_path)
    task, token = scheduler.start_task(workspace, "short", "private expiry", ttl_seconds=0.15)
    result = scheduler.acquire_claim(
        workspace,
        token,
        resources=("unity-live",),
        wait_seconds=1,
        keep_queued=True,
        operation_id=str(uuid.uuid4()),
    )
    assert result["task_id"] == task["id"]
    assert result["aborted"] is True
    assert result["reason"] == "task-ttl-expired"


def test_invalid_clock_evidence_cannot_hide_behind_readonly_poll(tmp_path):
    scheduler, workspace, _, waiter, _ = fixture(tmp_path)
    with ThreadPoolExecutor(max_workers=1) as pool:
        future = pool.submit(
            scheduler.acquire_claim,
            workspace,
            waiter,
            resources=("unity-live",),
            wait_seconds=1,
            keep_queued=True,
            operation_id=str(uuid.uuid4()),
        )
        assert scheduler.reads.wait(timeout=2)
        with sqlite3.connect(scheduler.paths.database) as connection:
            connection.execute(
                "UPDATE tasks SET expires_at = ? WHERE owner = 'waiter'", (time.time() - 1,)
            )
        result = future.result(timeout=3)
    assert result["aborted"] is True and result["reason"] == "task-ttl-expired"
