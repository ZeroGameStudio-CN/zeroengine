from __future__ import annotations

import json
import threading
import time
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

import pytest

from unity_workspace_scheduler.coordinator import WorkspaceCoordinator
from unity_workspace_scheduler.errors import AuthorizationError, BusyError
from unity_workspace_scheduler.state import open_database, resolve_state_paths


@pytest.fixture
def scheduler(tmp_path: Path) -> WorkspaceCoordinator:
    return WorkspaceCoordinator(resolve_state_paths(tmp_path / "state"))


@pytest.fixture
def workspace(tmp_path: Path, scheduler: WorkspaceCoordinator) -> Path:
    root = tmp_path / "workspace"
    root.mkdir()
    scheduler.register(root)
    return root


def start(
    scheduler: WorkspaceCoordinator, workspace: Path, owner: str, *, ttl: float = 1800
) -> tuple[dict[str, object], str]:
    return scheduler.start_task(workspace, owner, f"{owner} work", ttl_seconds=ttl)


def test_conflicting_paths_queue_fifo_and_non_conflicting_paths_run(
    scheduler: WorkspaceCoordinator, workspace: Path
) -> None:
    _, first_token = start(scheduler, workspace, "first")
    _, second_token = start(scheduler, workspace, "second")
    _, third_token = start(scheduler, workspace, "third")

    first = scheduler.acquire_claim(workspace, first_token, writes=("Assets/Hero.prefab",))
    second = scheduler.acquire_claim(workspace, second_token, writes=("Assets/Hero.prefab.meta",))
    third = scheduler.acquire_claim(workspace, third_token, writes=("Assets/Villain.prefab",))

    assert first["state"] == "active"
    assert second["state"] == "queued"
    assert third["state"] == "active"

    scheduler.release_claim(workspace, first_token, str(first["id"]))
    status = scheduler.status(workspace)
    promoted = next(claim for claim in status["claims"] if claim["id"] == second["id"])
    assert promoted["state"] == "active"


def test_freeze_is_fair_barrier(scheduler: WorkspaceCoordinator, workspace: Path) -> None:
    _, first_token = start(scheduler, workspace, "first")
    _, freeze_token = start(scheduler, workspace, "freeze")
    _, later_token = start(scheduler, workspace, "later")

    first = scheduler.acquire_claim(workspace, first_token, resources=("unity-live",))
    freeze = scheduler.acquire_claim(workspace, freeze_token, freeze=True)
    later = scheduler.acquire_claim(workspace, later_token, resources=("other",))

    assert first["state"] == "active"
    assert freeze["state"] == "queued"
    assert later["state"] == "queued"

    scheduler.release_claim(workspace, first_token, str(first["id"]))
    status = scheduler.status(workspace)
    by_id = {claim["id"]: claim for claim in status["claims"]}
    assert by_id[freeze["id"]]["state"] == "active"
    assert by_id[later["id"]]["state"] == "queued"

    scheduler.release_claim(workspace, freeze_token, str(freeze["id"]))
    status = scheduler.status(workspace)
    promoted = next(claim for claim in status["claims"] if claim["id"] == later["id"])
    assert promoted["state"] == "active"


def test_expired_owner_blocks_until_evidence_recovery(
    scheduler: WorkspaceCoordinator, workspace: Path
) -> None:
    task, token = start(scheduler, workspace, "expiring")
    scheduler.acquire_claim(workspace, token, resources=("unity-live",))
    with open_database(scheduler.paths) as connection:
        connection.execute(
            "UPDATE tasks SET expires_at = ? WHERE id = ?",
            (time.time() - 1, task["id"]),
        )

    status = scheduler.status(workspace)
    assert status["blocked"] is True
    assert status["tasks"][0]["state"] == "outcome_unknown"
    with pytest.raises(BusyError):
        start(scheduler, workspace, "blocked")

    recovered = scheduler.resolve_unknown(
        workspace,
        str(task["id"]),
        resolution="failed",
        evidence="Editor process ended before handoff; logs preserved.",
    )
    assert recovered["state"] == "failed"
    assert scheduler.status(workspace)["ready"] is True


def test_claim_assertion_requires_owned_scope(
    scheduler: WorkspaceCoordinator, workspace: Path
) -> None:
    _, token = start(scheduler, workspace, "owner")
    scheduler.acquire_claim(
        workspace,
        token,
        writes=("Assets/Scripts",),
        resources=("unity-live",),
    )

    authorized = scheduler.assert_claims(
        workspace,
        token,
        writes=("Assets/Scripts/Feature.cs",),
        resources=("unity-live",),
    )
    assert authorized["authorized"] is True
    with pytest.raises(AuthorizationError):
        scheduler.assert_claims(workspace, token, writes=("Assets/Scenes",))


def test_status_never_exposes_task_secret(scheduler: WorkspaceCoordinator, workspace: Path) -> None:
    _, token = start(scheduler, workspace, "private")
    payload = json.dumps(scheduler.status(workspace))
    assert token not in payload
    assert "token_hash" not in payload


def test_two_concurrent_clients_cannot_both_own_same_resource(
    scheduler: WorkspaceCoordinator, workspace: Path
) -> None:
    _, first_token = start(scheduler, workspace, "first")
    _, second_token = start(scheduler, workspace, "second")
    barrier = threading.Barrier(2)

    def acquire(token: str) -> dict[str, object]:
        barrier.wait()
        return scheduler.acquire_claim(workspace, token, resources=("unity-live",))

    with ThreadPoolExecutor(max_workers=2) as pool:
        results = list(pool.map(acquire, (first_token, second_token)))

    assert sorted(result["state"] for result in results) == ["active", "queued"]


def test_registration_is_idempotent_and_unregister_refuses_active_task(
    scheduler: WorkspaceCoordinator, workspace: Path
) -> None:
    assert scheduler.register(workspace)["created"] is False
    _, token = start(scheduler, workspace, "owner")
    with pytest.raises(BusyError):
        scheduler.unregister(workspace)
    scheduler.release_task(workspace, token, result="completed")
    assert scheduler.unregister(workspace)["removed"] is True
