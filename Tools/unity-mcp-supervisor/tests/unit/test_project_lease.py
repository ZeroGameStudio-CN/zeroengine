from __future__ import annotations

import multiprocessing
import time
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

import pytest

from unity_mcp_supervisor.errors import ProjectBusyError, ServiceError
from unity_mcp_supervisor.project_lease import (
    acquire_project_lease,
    inspect_project_lease,
    inspect_project_lease_queue,
    release_project_lease,
    require_project_lease,
)
from unity_mcp_supervisor.service_state import Settings, StatePaths
from unity_mcp_supervisor.supervisor import ServiceManager


def _wait_for_queue(
    paths: StatePaths,
    project_root: str,
    expected_owners: list[str],
    timeout: float = 10.0,
) -> None:
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        owners = [
            waiter.owner
            for waiter in inspect_project_lease_queue(paths, project_root)
        ]
        if owners == expected_owners:
            return
        time.sleep(0.02)
    raise AssertionError(f"Lease queue did not become {expected_owners!r}.")


def _waiting_lease_process(state_dir: str, project_root: str, owner: str) -> None:
    acquire_project_lease(
        StatePaths(Path(state_dir)), project_root, owner, 60, 30
    )


def test_lease_lifecycle_and_owner_enforcement(tmp_path: Path) -> None:
    paths = StatePaths(tmp_path)
    lease = acquire_project_lease(paths, "project", "task-a", 60, 0)

    inspected = inspect_project_lease(paths, "project")
    assert inspected is not None
    assert inspected.owner == "task-a"
    assert "lease_id" not in inspected.public_payload()

    with pytest.raises(ProjectBusyError) as error:
        require_project_lease(paths, "project", None, 60)
    assert error.value.details["owner"] == "task-a"
    assert "lease_id" not in error.value.details

    renewed = require_project_lease(paths, "project", lease.lease_id, 60)
    assert renewed is not None
    assert renewed.lease_id == lease.lease_id
    assert release_project_lease(paths, "project", lease.lease_id) is True
    assert release_project_lease(paths, "project", lease.lease_id) is False


def test_expired_lease_can_be_reclaimed(monkeypatch, tmp_path: Path) -> None:
    paths = StatePaths(tmp_path)
    monkeypatch.setattr("unity_mcp_supervisor.project_lease.time.time", lambda: 100.0)
    first = acquire_project_lease(paths, "project", "task-a", 2, 0)

    monkeypatch.setattr("unity_mcp_supervisor.project_lease.time.time", lambda: 103.0)
    second = acquire_project_lease(paths, "project", "task-b", 60, 0)

    assert second.lease_id != first.lease_id
    assert second.owner == "task-b"


def test_owner_command_renews_lease(monkeypatch, tmp_path: Path) -> None:
    paths = StatePaths(tmp_path)
    monkeypatch.setattr("unity_mcp_supervisor.project_lease.time.time", lambda: 100.0)
    lease = acquire_project_lease(paths, "project", "task-a", 10, 0)

    monkeypatch.setattr("unity_mcp_supervisor.project_lease.time.time", lambda: 105.0)
    renewed = require_project_lease(paths, "project", lease.lease_id, 10)

    assert renewed is not None
    assert renewed.renewed_at == 105.0
    assert renewed.expires_at == 115.0


def test_wait_timeout_reports_current_owner(tmp_path: Path) -> None:
    paths = StatePaths(tmp_path)
    acquire_project_lease(paths, "project", "task-a", 60, 0)

    with pytest.raises(ProjectBusyError) as error:
        acquire_project_lease(paths, "project", "task-b", 60, 0.05)

    assert error.value.details["owner"] == "task-a"
    assert error.value.retryable is True


def test_waiting_acquire_does_not_block_current_owner_release(tmp_path: Path) -> None:
    paths = StatePaths(tmp_path)
    first = acquire_project_lease(paths, "project", "task-a", 60, 0)
    with ThreadPoolExecutor(max_workers=1) as pool:
        waiting = pool.submit(
            acquire_project_lease, paths, "project", "task-b", 60, 10
        )
        time.sleep(0.2)
        assert not waiting.done()

        assert release_project_lease(paths, "project", first.lease_id) is True
        assert waiting.result(timeout=15).owner == "task-b"


def test_waiters_acquire_in_fifo_order(tmp_path: Path) -> None:
    paths = StatePaths(tmp_path)
    first = acquire_project_lease(paths, "project", "task-a", 60, 0)

    with ThreadPoolExecutor(max_workers=2) as pool:
        waiting_b = pool.submit(
            acquire_project_lease, paths, "project", "task-b", 60, 10
        )
        _wait_for_queue(paths, "project", ["task-b"])
        waiting_c = pool.submit(
            acquire_project_lease, paths, "project", "task-c", 60, 10
        )
        _wait_for_queue(paths, "project", ["task-b", "task-c"])

        assert release_project_lease(paths, "project", first.lease_id) is True
        with pytest.raises(ProjectBusyError):
            acquire_project_lease(paths, "project", "task-d", 60, 0)
        second = waiting_b.result(timeout=15)
        assert second.owner == "task-b"
        assert not waiting_c.done()

        assert release_project_lease(paths, "project", second.lease_id) is True
        third = waiting_c.result(timeout=15)
        assert third.owner == "task-c"
        assert release_project_lease(paths, "project", third.lease_id) is True


def test_abandoned_waiter_is_skipped(tmp_path: Path) -> None:
    paths = StatePaths(tmp_path)
    first = acquire_project_lease(paths, "project", "task-a", 60, 0)
    context = multiprocessing.get_context("spawn")
    abandoned = context.Process(
        target=_waiting_lease_process,
        args=(str(tmp_path), "project", "abandoned"),
    )
    abandoned.start()
    try:
        _wait_for_queue(paths, "project", ["abandoned"])
        abandoned.terminate()
        abandoned.join(timeout=10)
        assert not abandoned.is_alive()

        with ThreadPoolExecutor(max_workers=1) as pool:
            waiting = pool.submit(
                acquire_project_lease, paths, "project", "task-b", 60, 10
            )
            _wait_for_queue(paths, "project", ["task-b"])
            assert release_project_lease(paths, "project", first.lease_id) is True
            second = waiting.result(timeout=15)
            assert second.owner == "task-b"
            assert release_project_lease(paths, "project", second.lease_id) is True
    finally:
        if abandoned.is_alive():
            abandoned.terminate()
            abandoned.join(timeout=10)


def test_different_project_leases_are_independent(tmp_path: Path) -> None:
    paths = StatePaths(tmp_path)
    first = acquire_project_lease(paths, "project-a", "task-a", 60, 0)
    second = acquire_project_lease(paths, "project-b", "task-b", 60, 0)

    assert first.lease_id != second.lease_id
    assert inspect_project_lease(paths, "project-a").owner == "task-a"
    assert inspect_project_lease(paths, "project-b").owner == "task-b"


def test_wrong_owner_cannot_release_replacement_lease(tmp_path: Path) -> None:
    paths = StatePaths(tmp_path)
    first = acquire_project_lease(paths, "project", "task-a", 0.05, 0)
    time.sleep(0.1)
    second = acquire_project_lease(paths, "project", "task-b", 60, 0)

    with pytest.raises(ProjectBusyError):
        release_project_lease(paths, "project", first.lease_id)
    assert inspect_project_lease(paths, "project").lease_id == second.lease_id


def test_active_lease_blocks_manual_service_lifecycle(tmp_path: Path) -> None:
    settings = Settings(state_dir=tmp_path)
    lease = acquire_project_lease(settings.paths, "project", "task-a", 60, 0)
    manager = ServiceManager(settings)

    with pytest.raises(ServiceError, match="live operations are active"):
        manager._refuse_while_live_operations()

    assert release_project_lease(settings.paths, "project", lease.lease_id) is True
    manager._refuse_while_live_operations()
