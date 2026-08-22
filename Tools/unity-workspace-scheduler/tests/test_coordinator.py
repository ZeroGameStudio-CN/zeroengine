from __future__ import annotations

import json
import threading
import time
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

import pytest

from unity_workspace_scheduler.coordinator import WorkspaceCoordinator
from unity_workspace_scheduler.errors import AuthorizationError, BusyError, StateError, UsageError
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


def test_urgent_freeze_overtakes_queued_normal_work_but_not_active_work(
    scheduler: WorkspaceCoordinator, workspace: Path
) -> None:
    _, owner_token = start(scheduler, workspace, "owner")
    _, normal_token = start(scheduler, workspace, "normal")
    _, urgent_token = start(scheduler, workspace, "urgent")

    owned = scheduler.acquire_claim(workspace, owner_token, writes=("Assets/Hero.prefab",))
    normal = scheduler.acquire_claim(workspace, normal_token, writes=("Assets/Hero.prefab",))
    urgent = scheduler.acquire_claim(workspace, urgent_token, freeze=True, priority="urgent")

    assert owned["state"] == "active"
    assert normal["state"] == "queued"
    assert urgent["state"] == "queued"
    assert urgent["priority"] == "urgent"
    assert scheduler.heartbeat(workspace, owner_token)["drain_requested"] == {
        "freeze_id": urgent["id"],
        "queue_order": urgent["queue_order"],
        "priority": "urgent",
        "park_ready": True,
    }

    scheduler.park_task(workspace, owner_token)
    status = scheduler.status(workspace)
    by_id = {claim["id"]: claim for claim in status["claims"]}
    assert by_id[urgent["id"]]["state"] == "active"
    assert by_id[normal["id"]]["state"] == "queued"


def test_urgent_freezes_remain_fifo_with_each_other(
    scheduler: WorkspaceCoordinator, workspace: Path
) -> None:
    _, owner_token = start(scheduler, workspace, "owner")
    _, first_token = start(scheduler, workspace, "urgent-first")
    _, second_token = start(scheduler, workspace, "urgent-second")

    scheduler.acquire_claim(workspace, owner_token, writes=("Assets/Hero.prefab",))
    first = scheduler.acquire_claim(workspace, first_token, freeze=True, priority="urgent")
    second = scheduler.acquire_claim(workspace, second_token, freeze=True, priority="urgent")

    scheduler.park_task(workspace, owner_token)
    status = scheduler.status(workspace)
    by_id = {claim["id"]: claim for claim in status["claims"]}
    assert by_id[first["id"]]["state"] == "active"
    assert by_id[second["id"]]["state"] == "queued"

    scheduler.release_claim(workspace, first_token, str(first["id"]))
    status = scheduler.status(workspace)
    by_id = {claim["id"]: claim for claim in status["claims"]}
    assert by_id[second["id"]]["state"] == "active"


def test_urgent_freeze_overtakes_a_queued_normal_freeze(
    scheduler: WorkspaceCoordinator, workspace: Path
) -> None:
    _, owner_token = start(scheduler, workspace, "owner")
    _, normal_token = start(scheduler, workspace, "normal-freeze")
    _, urgent_token = start(scheduler, workspace, "urgent-freeze")

    scheduler.acquire_claim(workspace, owner_token, writes=("Assets/Hero.prefab",))
    normal = scheduler.acquire_claim(workspace, normal_token, freeze=True)
    urgent = scheduler.acquire_claim(workspace, urgent_token, freeze=True, priority="urgent")

    scheduler.park_task(workspace, owner_token)
    status = scheduler.status(workspace)
    by_id = {claim["id"]: claim for claim in status["claims"]}
    assert by_id[urgent["id"]]["state"] == "active"
    assert by_id[normal["id"]]["state"] == "queued"


@pytest.mark.parametrize("active_kind", ["resource", "freeze"])
def test_urgent_freeze_does_not_preempt_active_resource_or_freeze(
    scheduler: WorkspaceCoordinator, workspace: Path, active_kind: str
) -> None:
    _, owner_token = start(scheduler, workspace, "owner")
    _, urgent_token = start(scheduler, workspace, "urgent")

    if active_kind == "resource":
        active = scheduler.acquire_claim(workspace, owner_token, resources=("exclusive-tool",))
    else:
        active = scheduler.acquire_claim(workspace, owner_token, freeze=True)
    urgent = scheduler.acquire_claim(workspace, urgent_token, freeze=True, priority="urgent")

    assert active["state"] == "active"
    assert urgent["state"] == "queued"
    drain = scheduler.heartbeat(workspace, owner_token)["drain_requested"]
    assert drain["freeze_id"] == urgent["id"]
    assert drain["park_ready"] is False


def test_urgent_drain_ignores_normal_resource_queued_behind_it(
    scheduler: WorkspaceCoordinator, workspace: Path
) -> None:
    _, owner_token = start(scheduler, workspace, "owner")
    _, urgent_token = start(scheduler, workspace, "urgent")

    owned = scheduler.acquire_claim(workspace, owner_token, writes=("Assets/Hero.prefab",))
    urgent = scheduler.acquire_claim(workspace, urgent_token, freeze=True, priority="urgent")
    later_resource = scheduler.acquire_claim(workspace, owner_token, resources=("exclusive-tool",))

    assert later_resource["state"] == "queued"
    assert scheduler.heartbeat(workspace, owner_token)["drain_requested"]["park_ready"] is True
    parked = scheduler.park_task(workspace, owner_token)
    assert parked["claim_ids"] == [owned["id"]]
    status = scheduler.status(workspace)
    by_id = {claim["id"]: claim for claim in status["claims"]}
    assert by_id[urgent["id"]]["state"] == "active"
    assert by_id[later_resource["id"]]["state"] == "queued"


def test_urgent_priority_is_rejected_for_non_freeze_claims(
    scheduler: WorkspaceCoordinator, workspace: Path
) -> None:
    _, token = start(scheduler, workspace, "owner")
    with pytest.raises(UsageError):
        scheduler.acquire_claim(
            workspace,
            token,
            writes=("Assets/Hero.prefab",),
            priority="urgent",
        )


def test_schema_one_state_without_priority_scope_accepts_urgent_freeze(
    scheduler: WorkspaceCoordinator, workspace: Path
) -> None:
    _, blocker_token = start(scheduler, workspace, "blocker")
    _, normal_token = start(scheduler, workspace, "normal")
    blocker = scheduler.acquire_claim(workspace, blocker_token, resources=("exclusive-tool",))
    normal = scheduler.acquire_claim(workspace, normal_token, resources=("exclusive-tool",))
    with open_database(scheduler.paths) as connection:
        schema = connection.execute(
            "SELECT value FROM scheduler_meta WHERE key = 'schema_version'"
        ).fetchone()
        priority_scopes = connection.execute(
            "SELECT value FROM claim_scopes WHERE claim_id = ? AND scope_type = 'priority'",
            (normal["id"],),
        ).fetchall()
    assert schema["value"] == "1"
    assert priority_scopes == []

    reopened = WorkspaceCoordinator(scheduler.paths)
    _, urgent_token = start(reopened, workspace, "urgent")
    urgent = reopened.acquire_claim(workspace, urgent_token, freeze=True, priority="urgent")
    reopened.release_claim(workspace, blocker_token, str(blocker["id"]))
    status = reopened.status(workspace)
    by_id = {claim["id"]: claim for claim in status["claims"]}
    assert by_id[normal["id"]]["priority"] == "normal"
    assert by_id[normal["id"]]["state"] == "queued"
    assert by_id[urgent["id"]]["state"] == "active"


def test_invalid_priority_scope_fails_closed(
    scheduler: WorkspaceCoordinator, workspace: Path
) -> None:
    _, token = start(scheduler, workspace, "owner")
    claim = scheduler.acquire_claim(workspace, token, writes=("Assets/Hero.prefab",))
    with open_database(scheduler.paths) as connection:
        connection.execute(
            "INSERT INTO claim_scopes(claim_id, scope_type, value) VALUES(?, 'priority', 'urgent')",
            (claim["id"],),
        )

    with pytest.raises(StateError):
        scheduler.status(workspace)


def test_park_drains_freeze_and_restores_original_fifo(
    scheduler: WorkspaceCoordinator, workspace: Path
) -> None:
    _, owner_token = start(scheduler, workspace, "owner")
    _, freeze_token = start(scheduler, workspace, "maintenance")
    _, later_token = start(scheduler, workspace, "later")

    owned = scheduler.acquire_claim(workspace, owner_token, writes=("Assets/Hero.prefab",))
    freeze = scheduler.acquire_claim(workspace, freeze_token, freeze=True)
    later = scheduler.acquire_claim(workspace, later_token, writes=("Assets/Hero.prefab",))

    assert freeze["state"] == "queued"
    assert later["state"] == "queued"
    with pytest.raises(BusyError) as drain_error:
        scheduler.assert_claims(workspace, owner_token, writes=("Assets/Hero.prefab",))
    assert drain_error.value.details == {
        "reason": "freeze-drain-requested",
        "freeze_id": freeze["id"],
        "queue_order": freeze["queue_order"],
        "priority": "normal",
        "park_ready": True,
    }

    parked = scheduler.park_task(workspace, owner_token)
    assert parked["claim_ids"] == [owned["id"]]
    assert parked["states"] == {owned["id"]: "parked"}
    status = scheduler.status(workspace)
    by_id = {claim["id"]: claim for claim in status["claims"]}
    assert by_id[owned["id"]]["parked_for"] == freeze["id"]
    assert by_id[freeze["id"]]["state"] == "active"

    scheduler.release_claim(workspace, freeze_token, str(freeze["id"]))
    status = scheduler.status(workspace)
    by_id = {claim["id"]: claim for claim in status["claims"]}
    assert by_id[owned["id"]]["state"] == "active"
    assert by_id[owned["id"]]["parked_for"] is None
    assert by_id[later["id"]]["state"] == "queued"


def test_park_refuses_unsafe_claims_and_requires_a_freeze(
    scheduler: WorkspaceCoordinator, workspace: Path
) -> None:
    _, owner_token = start(scheduler, workspace, "owner")
    scheduler.acquire_claim(workspace, owner_token, writes=("Assets/Hero.prefab",))
    with pytest.raises(StateError) as no_freeze:
        scheduler.park_task(workspace, owner_token)
    assert no_freeze.value.details["reason"] == "freeze-drain-not-requested"

    scheduler.acquire_claim(workspace, owner_token, resources=("unity-live",))
    _, freeze_token = start(scheduler, workspace, "maintenance")
    freeze = scheduler.acquire_claim(workspace, freeze_token, freeze=True)
    heartbeat = scheduler.heartbeat(workspace, owner_token)
    assert heartbeat["drain_requested"] == {
        "freeze_id": freeze["id"],
        "queue_order": freeze["queue_order"],
        "priority": "normal",
        "park_ready": False,
    }
    with pytest.raises(BusyError) as unsafe:
        scheduler.park_task(workspace, owner_token)
    assert unsafe.value.details["reason"] == "task-holds-unsafe-claims"
    with pytest.raises(AuthorizationError):
        scheduler.park_task(workspace, "not-the-owner-token")


def test_drain_targets_only_blockers_and_reports_queued_unsafe_claims(
    scheduler: WorkspaceCoordinator, workspace: Path
) -> None:
    _, owner_token = start(scheduler, workspace, "owner")
    _, resource_blocker_token = start(scheduler, workspace, "resource-blocker")
    _, maintenance_token = start(scheduler, workspace, "maintenance")
    _, claimless_token = start(scheduler, workspace, "claimless")
    _, later_token = start(scheduler, workspace, "later")

    scheduler.acquire_claim(workspace, owner_token, writes=("Assets/Hero.prefab",))
    scheduler.acquire_claim(workspace, resource_blocker_token, resources=("exclusive-tool",))
    queued_resource = scheduler.acquire_claim(workspace, owner_token, resources=("exclusive-tool",))
    freeze = scheduler.acquire_claim(workspace, maintenance_token, freeze=True)
    scheduler.acquire_claim(workspace, later_token, writes=("Assets/Hero.prefab",))

    drain = scheduler.heartbeat(workspace, owner_token)["drain_requested"]
    assert drain == {
        "freeze_id": freeze["id"],
        "queue_order": freeze["queue_order"],
        "priority": "normal",
        "park_ready": False,
    }
    assert "drain_requested" not in scheduler.heartbeat(workspace, claimless_token)
    assert "drain_requested" not in scheduler.heartbeat(workspace, later_token)

    with pytest.raises(BusyError) as asserted:
        scheduler.assert_claims(workspace, owner_token, writes=("Assets/Hero.prefab",))
    assert asserted.value.details == {"reason": "freeze-drain-requested", **drain}
    with pytest.raises(BusyError) as unsafe:
        scheduler.park_task(workspace, owner_token)
    assert unsafe.value.details["reason"] == "task-holds-unsafe-claims"

    scheduler.release_claim(workspace, owner_token, str(queued_resource["id"]))
    assert scheduler.heartbeat(workspace, owner_token)["drain_requested"]["park_ready"] is True
    with pytest.raises(StateError) as non_blocker:
        scheduler.park_task(workspace, later_token)
    assert non_blocker.value.details["reason"] == "freeze-drain-not-requested"


def test_park_wait_resumes_without_reacquiring_claims(
    scheduler: WorkspaceCoordinator, workspace: Path
) -> None:
    _, owner_token = start(scheduler, workspace, "owner")
    _, freeze_token = start(scheduler, workspace, "maintenance")
    owned = scheduler.acquire_claim(workspace, owner_token, writes=("Assets/Hero.prefab",))
    freeze = scheduler.acquire_claim(workspace, freeze_token, freeze=True)

    with ThreadPoolExecutor(max_workers=1) as pool:
        waiting = pool.submit(scheduler.park_task, workspace, owner_token, wait_seconds=2)
        deadline = time.monotonic() + 1
        while time.monotonic() < deadline:
            status = scheduler.status(workspace)
            freeze_state = next(
                claim["state"] for claim in status["claims"] if claim["id"] == freeze["id"]
            )
            if freeze_state == "active":
                break
            time.sleep(0.01)
        else:
            pytest.fail("Freeze did not become active after owner parked.")
        scheduler.release_claim(workspace, freeze_token, str(freeze["id"]))
        resumed = waiting.result(timeout=2)

    assert resumed["resumed"] is True
    assert resumed["timed_out"] is False
    assert resumed["claim_ids"] == [owned["id"]]
    assert resumed["states"] == {owned["id"]: "active"}


def test_park_timeout_preserves_claim_until_freeze_owner_releases_task(
    scheduler: WorkspaceCoordinator, workspace: Path
) -> None:
    _, owner_token = start(scheduler, workspace, "owner")
    _, freeze_token = start(scheduler, workspace, "maintenance")
    owned = scheduler.acquire_claim(workspace, owner_token, writes=("Assets/Hero.prefab",))
    scheduler.acquire_claim(workspace, freeze_token, freeze=True)

    timed_out = scheduler.park_task(workspace, owner_token, wait_seconds=0.01)
    assert timed_out["timed_out"] is True
    assert timed_out["states"] == {owned["id"]: "parked"}

    scheduler.release_task(workspace, freeze_token, result="completed")
    status = scheduler.status(workspace)
    restored = next(claim for claim in status["claims"] if claim["id"] == owned["id"])
    assert restored["state"] == "active"
    assert restored["parked_for"] is None


def test_cancelling_queued_freeze_restores_parked_owner_before_later_claim(
    scheduler: WorkspaceCoordinator, workspace: Path
) -> None:
    _, first_token = start(scheduler, workspace, "first")
    _, blocker_token = start(scheduler, workspace, "blocker")
    _, freeze_token = start(scheduler, workspace, "maintenance")
    _, later_token = start(scheduler, workspace, "later")
    first = scheduler.acquire_claim(workspace, first_token, writes=("Assets/Hero.prefab",))
    scheduler.acquire_claim(workspace, blocker_token, writes=("Assets/Villain.prefab",))
    freeze = scheduler.acquire_claim(workspace, freeze_token, freeze=True)
    later = scheduler.acquire_claim(workspace, later_token, writes=("Assets/Hero.prefab",))

    scheduler.park_task(workspace, first_token)
    status = scheduler.status(workspace)
    by_id = {claim["id"]: claim for claim in status["claims"]}
    assert by_id[freeze["id"]]["state"] == "queued"
    assert by_id[first["id"]]["state"] == "parked"

    scheduler.release_claim(workspace, freeze_token, str(freeze["id"]))
    status = scheduler.status(workspace)
    by_id = {claim["id"]: claim for claim in status["claims"]}
    assert by_id[first["id"]]["state"] == "active"
    assert by_id[later["id"]]["state"] == "queued"


def test_parked_task_ttl_expiry_cancels_its_claim_without_unknown_fence(
    scheduler: WorkspaceCoordinator, workspace: Path
) -> None:
    owner_task, owner_token = start(scheduler, workspace, "owner")
    _, freeze_token = start(scheduler, workspace, "maintenance")
    owned = scheduler.acquire_claim(workspace, owner_token, writes=("Assets/Hero.prefab",))
    scheduler.acquire_claim(workspace, freeze_token, freeze=True)
    scheduler.park_task(workspace, owner_token)
    with open_database(scheduler.paths) as connection:
        connection.execute(
            "UPDATE tasks SET expires_at = ? WHERE id = ?",
            (time.time() - 1, owner_task["id"]),
        )

    status = scheduler.status(workspace)
    assert status["blocked"] is False
    assert not [task for task in status["tasks"] if task["id"] == owner_task["id"]]
    assert not [claim for claim in status["claims"] if claim["id"] == owned["id"]]


def test_unknown_active_freeze_keeps_parked_claims_until_recovery(
    scheduler: WorkspaceCoordinator, workspace: Path
) -> None:
    _, owner_token = start(scheduler, workspace, "owner")
    freeze_task, freeze_token = start(scheduler, workspace, "maintenance")
    owned = scheduler.acquire_claim(workspace, owner_token, writes=("Assets/Hero.prefab",))
    scheduler.acquire_claim(workspace, freeze_token, freeze=True)
    scheduler.park_task(workspace, owner_token)
    with open_database(scheduler.paths) as connection:
        connection.execute(
            "UPDATE tasks SET expires_at = ? WHERE id = ?",
            (time.time() - 1, freeze_task["id"]),
        )

    status = scheduler.status(workspace)
    assert status["blocked"] is True
    still_parked = next(claim for claim in status["claims"] if claim["id"] == owned["id"])
    assert still_parked["state"] == "parked"

    scheduler.resolve_unknown(
        workspace,
        str(freeze_task["id"]),
        resolution="failed",
        evidence="Maintenance process stopped before touching the workspace.",
    )
    status = scheduler.status(workspace)
    restored = next(claim for claim in status["claims"] if claim["id"] == owned["id"])
    assert restored["state"] == "active"


def test_expired_owner_blocks_until_evidence_recovery(
    scheduler: WorkspaceCoordinator, workspace: Path
) -> None:
    task, token = start(scheduler, workspace, "expiring")
    _, urgent_token = start(scheduler, workspace, "urgent")
    scheduler.acquire_claim(workspace, token, resources=("unity-live",))
    with open_database(scheduler.paths) as connection:
        connection.execute(
            "UPDATE tasks SET expires_at = ? WHERE id = ?",
            (time.time() - 1, task["id"]),
        )

    status = scheduler.status(workspace)
    assert status["blocked"] is True
    expired = next(task_item for task_item in status["tasks"] if task_item["id"] == task["id"])
    assert expired["state"] == "outcome_unknown"
    with pytest.raises(BusyError):
        start(scheduler, workspace, "blocked")

    with pytest.raises(BusyError):
        scheduler.acquire_claim(workspace, urgent_token, freeze=True, priority="urgent")

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
