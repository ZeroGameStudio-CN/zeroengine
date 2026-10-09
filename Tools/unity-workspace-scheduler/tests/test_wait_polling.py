from __future__ import annotations

from contextlib import contextmanager
from pathlib import Path

import pytest

import unity_workspace_scheduler.coordinator as coordinator_module
from unity_workspace_scheduler.coordinator import WorkspaceCoordinator
from unity_workspace_scheduler.state import resolve_state_paths


@pytest.mark.parametrize("kind", ["claim", "freeze", "park"])
def test_idle_waiters_bound_write_attempts_and_preserve_their_scopes(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch, kind: str
) -> None:
    scheduler = WorkspaceCoordinator(resolve_state_paths(tmp_path / "state"))
    workspace = tmp_path / "workspace"
    workspace.mkdir()
    scheduler.register(workspace)
    _, holder = scheduler.start_task(workspace, "holder", "synthetic blocking owner")
    _, waiter = scheduler.start_task(workspace, "waiter", "synthetic retained waiter")
    if kind == "park":
        claimed = scheduler.acquire_claim(workspace, waiter, writes=("Assets/Owned.cs",))
        scheduler.acquire_claim(workspace, holder, freeze=True, keep_queued=True)
    else:
        scheduler.acquire_claim(workspace, holder, resources=("unity-live",))
        claimed = None

    elapsed = 0.0
    sleeps: list[float] = []
    transactions = 0
    original_transaction = WorkspaceCoordinator._transaction

    def sleep(seconds: float) -> None:
        nonlocal elapsed
        sleeps.append(seconds)
        elapsed += seconds

    @contextmanager
    def counted_transaction(self):
        nonlocal transactions
        transactions += 1
        with original_transaction(self) as connection:
            yield connection

    with monkeypatch.context() as scoped:
        scoped.setattr(coordinator_module.time, "monotonic", lambda: elapsed)
        scoped.setattr(coordinator_module.time, "sleep", sleep)
        scoped.setattr(WorkspaceCoordinator, "_transaction", counted_transaction)
        if kind == "park":
            result = scheduler.park_task(workspace, waiter, wait_seconds=4.0)
        else:
            result = scheduler.acquire_claim(
                workspace,
                waiter,
                freeze=kind == "freeze",
                resources=("unity-live",) if kind == "claim" else (),
                wait_seconds=4.0,
                keep_queued=True,
            )

    # Four seconds of no progress must not repeatedly seize the writer lock at
    # 10 Hz. The absolute caller budget still clips the final delay.
    assert 2 <= len(sleeps) <= 8
    assert transactions <= 10
    assert all(0 < delay <= 2.0 for delay in sleeps)
    assert 0 < elapsed <= 4.0
    assert result["timed_out"] is True
    claims = scheduler.status(workspace)["claims"]
    if kind == "park":
        assert result["claim_ids"] == [claimed["id"]]
        assert result["states"] == {claimed["id"]: "parked"}
        assert result["resumed"] is False
    else:
        retained = next(claim for claim in claims if claim["id"] == result["id"])
        assert retained["state"] == result["state"] == "queued"
        assert retained["queue_order"] == result["queue_order"]
        assert result["granted"] is False


def test_waiter_observes_grant_during_backoff_without_replacing_its_claim(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    scheduler = WorkspaceCoordinator(resolve_state_paths(tmp_path / "state"))
    workspace = tmp_path / "workspace"
    workspace.mkdir()
    scheduler.register(workspace)
    _, holder = scheduler.start_task(workspace, "holder", "synthetic blocking owner")
    _, waiter = scheduler.start_task(workspace, "waiter", "synthetic retained waiter")
    blocking = scheduler.acquire_claim(workspace, holder, resources=("unity-live",))
    elapsed = 0.0
    retained = None
    released_at = None

    def sleep(seconds: float) -> None:
        nonlocal elapsed, retained, released_at
        elapsed += seconds
        if retained is None:
            retained = next(
                claim
                for claim in scheduler.status(workspace)["claims"]
                if claim["state"] == "queued"
            )
        if elapsed >= 0.7 and released_at is None:
            scheduler.release_claim(workspace, holder, blocking["id"])
            released_at = elapsed

    with monkeypatch.context() as scoped:
        scoped.setattr(coordinator_module.time, "monotonic", lambda: elapsed)
        scoped.setattr(coordinator_module.time, "sleep", sleep)
        result = scheduler.acquire_claim(
            workspace, waiter, resources=("unity-live",), wait_seconds=10.0, keep_queued=True
        )

    assert result["granted"] is True
    assert result["timed_out"] is False
    assert result["id"] == retained["id"]
    assert result["queue_order"] == retained["queue_order"]
    assert elapsed - released_at <= 2.0
