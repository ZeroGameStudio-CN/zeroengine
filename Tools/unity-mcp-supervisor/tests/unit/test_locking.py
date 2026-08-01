from __future__ import annotations

import multiprocessing
import os
import time
from itertools import pairwise
from pathlib import Path

from unity_mcp_supervisor.locking import live_operation_owners, project_lock
from unity_mcp_supervisor.service_state import StatePaths


def _lock_worker(
    state_dir: str, root: str, start_event, queue, hold_seconds: float
) -> None:
    paths = StatePaths(Path(state_dir))
    start_event.wait(10)
    with project_lock(paths, root, "worker", 10):
        started = time.monotonic()
        time.sleep(hold_seconds)
        queue.put((started, time.monotonic()))


def _crash_worker(state_dir: str, root: str, acquired_event) -> None:
    paths = StatePaths(Path(state_dir))
    with project_lock(paths, root, "crash-worker", 10):
        acquired_event.set()
        os._exit(23)


def _spawn_context():
    return multiprocessing.get_context("spawn")


def test_same_project_processes_are_serialized(tmp_path: Path) -> None:
    ctx = _spawn_context()
    start = ctx.Event()
    queue = ctx.Queue()
    processes = [
        ctx.Process(
            target=_lock_worker, args=(str(tmp_path), "same-root", start, queue, 0.2)
        )
        for _ in range(4)
    ]
    for process in processes:
        process.start()
    start.set()
    intervals = [queue.get(timeout=15) for _ in processes]
    for process in processes:
        process.join(timeout=15)
        assert process.exitcode == 0
    intervals.sort()
    for previous, current in pairwise(intervals):
        assert previous[1] <= current[0]


def test_different_projects_can_overlap(tmp_path: Path) -> None:
    ctx = _spawn_context()
    start = ctx.Event()
    queue = ctx.Queue()
    processes = [
        ctx.Process(target=_lock_worker, args=(str(tmp_path), root, start, queue, 0.5))
        for root in ("project-a", "project-b")
    ]
    for process in processes:
        process.start()
    start.set()
    first, second = queue.get(timeout=15), queue.get(timeout=15)
    for process in processes:
        process.join(timeout=15)
        assert process.exitcode == 0
    assert max(first[0], second[0]) < min(first[1], second[1])


def test_process_crash_releases_os_lock(tmp_path: Path) -> None:
    ctx = _spawn_context()
    acquired = ctx.Event()
    process = ctx.Process(
        target=_crash_worker, args=(str(tmp_path), "project", acquired)
    )
    process.start()
    assert acquired.wait(10)
    process.join(timeout=10)
    assert process.exitcode == 23
    with project_lock(StatePaths(tmp_path), "project", "recovery", 2):
        pass


def test_live_operation_owner_exists_only_while_lock_is_held(tmp_path: Path) -> None:
    paths = StatePaths(tmp_path)
    assert live_operation_owners(paths) == []
    with project_lock(paths, "project", "long-call", 2):
        owners = live_operation_owners(paths)
        assert owners == [
            {
                "pid": os.getpid(),
                "started_at": owners[0]["started_at"],
                "command_type": "long-call",
            }
        ]
    assert live_operation_owners(paths) == []
