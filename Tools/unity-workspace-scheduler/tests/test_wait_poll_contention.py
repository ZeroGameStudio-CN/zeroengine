from __future__ import annotations

import sqlite3
import threading
import time
import uuid
from concurrent.futures import ThreadPoolExecutor
from contextlib import contextmanager
from pathlib import Path

import pytest

from unity_workspace_scheduler import coordinator as coordinator_module
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


def test_unrelated_heartbeat_cannot_make_a_blocked_waiter_request_writer_locks(tmp_path):
    scheduler, workspace, owner, waiter, _ = fixture(tmp_path)
    peer = WorkspaceCoordinator(scheduler.paths)
    observe = scheduler._unchanged_queued_wait
    heartbeats = []

    def heartbeat_then_observe(*args):
        for _ in range(3):
            heartbeats.append(peer.heartbeat(workspace, owner, ttl_seconds=60))
        return observe(*args)

    scheduler._unchanged_queued_wait = heartbeat_then_observe
    scheduler.writes = 0
    result = scheduler.acquire_claim(
        workspace, waiter, resources=("unity-live",), wait_seconds=0.8, keep_queued=True
    )
    assert len(heartbeats) >= 3
    assert result["state"] == "queued" and result["timed_out"] is True
    assert scheduler.writes == 2


@pytest.mark.parametrize(
    "writes,resources,freeze",
    [
        (("Assets/file.cs",), (), False),
        ((), ("unity-live",), False),
        ((), (), True),
    ],
)
def test_removed_blocker_triggers_scheduling_even_without_an_epoch_change(
    tmp_path, writes, resources, freeze
):
    scheduler, workspace, owner, waiter, held = fixture(tmp_path)
    scheduler.release_claim(workspace, owner, held["id"])
    held = scheduler.acquire_claim(
        workspace, owner, writes=writes, resources=resources, freeze=freeze
    )
    with ThreadPoolExecutor(max_workers=1) as pool:
        future = pool.submit(
            scheduler.acquire_claim,
            workspace,
            waiter,
            writes=writes,
            resources=resources,
            freeze=freeze,
            wait_seconds=2,
            keep_queued=True,
        )
        assert scheduler.reads.wait(timeout=2)
        with sqlite3.connect(scheduler.paths.database) as connection:
            queued = connection.execute(
                "SELECT id, queue_order FROM claims WHERE state = 'queued'"
            ).fetchone()
            # Model a time-driven change without another epoch-producing API.
            connection.execute(
                "UPDATE claims SET state = 'released', released_at = ? WHERE id = ?",
                (time.time(), held["id"]),
            )
        result = future.result(timeout=3)
    assert result["id"] == queued[0] and result["queue_order"] == queued[1]
    assert result["state"] == "active"


def test_lock_failure_distinguishes_waiting_from_holding_a_writer(tmp_path, monkeypatch):
    scheduler, _, _, _, _ = fixture(tmp_path)
    original_open = coordinator_module.open_database

    def short_timeout(paths):
        connection = original_open(paths)
        connection.execute("PRAGMA busy_timeout = 20")
        return connection

    monkeypatch.setattr(coordinator_module, "open_database", short_timeout)
    blocker = original_open(scheduler.paths)
    try:
        blocker.execute("BEGIN IMMEDIATE")
        with pytest.raises(sqlite3.OperationalError) as caught, scheduler._transaction():
            pytest.fail("The competing writer must not enter its transaction body")
        timing = caught.value.scheduler_transaction_timing
        assert timing["phase"] == "begin"
        assert timing["writer_wait_ms"] >= 0 and timing["writer_held_ms"] == 0
    finally:
        blocker.rollback()
        blocker.close()


def test_database_failure_reports_only_its_own_transaction_occupancy(tmp_path):
    scheduler, _, _, _, _ = fixture(tmp_path)
    with pytest.raises(sqlite3.OperationalError) as caught, scheduler._transaction() as connection:
        connection.execute("SELECT * FROM missing_private_test_table")
    timing = caught.value.scheduler_transaction_timing
    assert timing["phase"] == "body"
    assert timing["writer_wait_ms"] >= 0 and timing["writer_held_ms"] >= 0
