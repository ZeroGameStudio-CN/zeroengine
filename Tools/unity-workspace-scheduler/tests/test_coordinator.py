from __future__ import annotations

import json
import threading
import time
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

import pytest

import unity_workspace_scheduler.coordinator as coordinator_module
from unity_workspace_scheduler.coordinator import WorkspaceCoordinator
from unity_workspace_scheduler.errors import (
    AuthorizationError,
    BusyError,
    ClaimAuthorizationError,
    StateError,
    UsageError,
)
from unity_workspace_scheduler.state import open_database, resolve_state_paths
from unity_workspace_scheduler.state_ops import inspect_state


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


def queued_restoration_with_active_claim(
    scheduler: WorkspaceCoordinator, workspace: Path
) -> tuple[dict[str, object], str, dict[str, object], dict[str, object]]:
    owner_task, owner_token = start(scheduler, workspace, "owner")
    _, maintenance_token = start(scheduler, workspace, "maintenance")
    first = scheduler.acquire_claim(workspace, owner_token, writes=("Assets/Hero.prefab",))
    second = scheduler.acquire_claim(workspace, owner_token, writes=("Assets/Villain.prefab",))
    freeze = scheduler.acquire_claim(workspace, maintenance_token, freeze=True)
    scheduler.park_task(workspace, owner_token)
    scheduler.acquire_claim(
        workspace,
        maintenance_token,
        writes=("Assets/Villain.prefab",),
    )
    scheduler.release_claim(workspace, maintenance_token, str(freeze["id"]))
    by_id = {claim["id"]: claim for claim in scheduler.status(workspace)["claims"]}
    assert by_id[first["id"]]["state"] == "active"
    assert by_id[second["id"]]["state"] == "queued"
    assert by_id[second["id"]]["parked_for"] == freeze["id"]
    return owner_task, owner_token, first, second


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


def test_resource_identity_is_casefolded_across_platforms(
    scheduler: WorkspaceCoordinator, workspace: Path
) -> None:
    _, owner_token = start(scheduler, workspace, "owner")

    claim = scheduler.acquire_claim(
        workspace,
        owner_token,
        resources=(" Unity-Live ", "UNITY-LIVE"),
    )

    assert claim["resources"] == ["unity-live"]
    status_claim = next(
        item for item in scheduler.status(workspace)["claims"] if item["id"] == claim["id"]
    )
    assert status_claim["resources"] == ["unity-live"]


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
    drain = scheduler.heartbeat(workspace, owner_token)["drain_requested"]
    assert drain == {
        "freeze_id": urgent["id"],
        "queue_order": urgent["queue_order"],
        "priority": "urgent",
        "park_ready": True,
    }
    with pytest.raises(BusyError) as blocked_acquire:
        scheduler.acquire_claim(workspace, owner_token, freeze=True, priority="urgent")
    assert blocked_acquire.value.details == {
        "reason": "freeze-drain-requested",
        **drain,
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

    owned = scheduler.acquire_claim(workspace, owner_token, writes=("Assets/Hero.prefab",))
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
    assert by_id[owned["id"]]["state"] == "queued"
    assert scheduler.heartbeat(workspace, owner_token)["restoration_pending_claim_ids"] == [
        owned["id"]
    ]
    with pytest.raises(BusyError) as pending:
        scheduler.assert_claims(workspace, owner_token, writes=("Assets/Hero.prefab",))
    assert pending.value.details == {
        "reason": "task-restoration-pending",
        "claim_ids": [owned["id"]],
    }
    with pytest.raises(BusyError) as pending_acquire:
        scheduler.acquire_claim(workspace, owner_token, resources=("unity-live",))
    assert pending_acquire.value.details == {
        "reason": "task-restoration-pending",
        "claim_ids": [owned["id"]],
    }

    scheduler.release_claim(workspace, second_token, str(second["id"]))
    status = scheduler.status(workspace)
    by_id = {claim["id"]: claim for claim in status["claims"]}
    assert by_id[owned["id"]]["state"] == "active"
    assert scheduler.heartbeat(workspace, owner_token)["restoration_pending_claim_ids"] == []


def test_two_urgent_freezes_then_normal_freeze_complete_restoration_state_machine(
    scheduler: WorkspaceCoordinator, workspace: Path
) -> None:
    _, owner_token = start(scheduler, workspace, "owner")
    _, first_token = start(scheduler, workspace, "urgent-first")
    _, second_token = start(scheduler, workspace, "urgent-second")
    _, normal_token = start(scheduler, workspace, "normal-maintenance")

    owned = scheduler.acquire_claim(workspace, owner_token, writes=("Assets/Hero.prefab",))
    first = scheduler.acquire_claim(workspace, first_token, freeze=True, priority="urgent")
    second = scheduler.acquire_claim(workspace, second_token, freeze=True, priority="urgent")
    normal = scheduler.acquire_claim(workspace, normal_token, freeze=True)

    scheduler.park_task(workspace, owner_token)
    scheduler.release_claim(workspace, first_token, str(first["id"]))
    status = scheduler.status(workspace)
    by_id = {claim["id"]: claim for claim in status["claims"]}
    assert by_id[second["id"]]["state"] == "active"
    assert by_id[normal["id"]]["state"] == "queued"
    assert by_id[owned["id"]]["state"] == "queued"

    heartbeat = scheduler.heartbeat(workspace, owner_token)
    assert heartbeat["restoration_pending_claim_ids"] == [owned["id"]]
    assert "drain_requested" not in heartbeat
    with pytest.raises(BusyError) as pending:
        scheduler.assert_claims(workspace, owner_token, writes=("Assets/Hero.prefab",))
    assert pending.value.details == {
        "reason": "task-restoration-pending",
        "claim_ids": [owned["id"]],
    }

    scheduler.release_claim(workspace, second_token, str(second["id"]))
    heartbeat = scheduler.heartbeat(workspace, owner_token)
    assert heartbeat["restoration_pending_claim_ids"] == []
    assert heartbeat["drain_requested"] == {
        "freeze_id": normal["id"],
        "queue_order": normal["queue_order"],
        "priority": "normal",
        "park_ready": True,
    }
    with pytest.raises(BusyError) as draining:
        scheduler.assert_claims(workspace, owner_token, writes=("Assets/Hero.prefab",))
    assert draining.value.details == {
        "reason": "freeze-drain-requested",
        **heartbeat["drain_requested"],
    }

    scheduler.park_task(workspace, owner_token)
    status = scheduler.status(workspace)
    by_id = {claim["id"]: claim for claim in status["claims"]}
    assert by_id[normal["id"]]["state"] == "active"
    assert by_id[owned["id"]]["state"] == "parked"

    scheduler.release_claim(workspace, normal_token, str(normal["id"]))
    assert (
        scheduler.assert_claims(workspace, owner_token, writes=("Assets/Hero.prefab",))[
            "authorized"
        ]
        is True
    )


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


def test_urgent_drain_blocks_new_claim_before_it_can_queue(
    scheduler: WorkspaceCoordinator, workspace: Path
) -> None:
    _, owner_token = start(scheduler, workspace, "owner")
    _, urgent_token = start(scheduler, workspace, "urgent")

    owned = scheduler.acquire_claim(workspace, owner_token, writes=("Assets/Hero.prefab",))
    urgent = scheduler.acquire_claim(workspace, urgent_token, freeze=True, priority="urgent")
    drain = scheduler.heartbeat(workspace, owner_token)["drain_requested"]
    with pytest.raises(BusyError) as blocked:
        scheduler.acquire_claim(workspace, owner_token, resources=("exclusive-tool",))
    assert blocked.value.details == {"reason": "freeze-drain-requested", **drain}

    assert drain["park_ready"] is True
    parked = scheduler.park_task(workspace, owner_token)
    assert parked["claim_ids"] == [owned["id"]]
    status = scheduler.status(workspace)
    by_id = {claim["id"]: claim for claim in status["claims"]}
    assert by_id[urgent["id"]]["state"] == "active"
    assert set(by_id) == {owned["id"], urgent["id"]}


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


def test_schema_three_state_without_priority_scope_accepts_urgent_freeze(
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
    assert schema["value"] == "3"
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


def test_resource_only_drain_becomes_claimless_after_release(
    scheduler: WorkspaceCoordinator, workspace: Path
) -> None:
    _, owner_token = start(scheduler, workspace, "resource-owner")
    _, freeze_token = start(scheduler, workspace, "maintenance")
    resource = scheduler.acquire_claim(workspace, owner_token, resources=("unity-live",))
    freeze = scheduler.acquire_claim(workspace, freeze_token, freeze=True)

    assert scheduler.heartbeat(workspace, owner_token)["drain_requested"]["park_ready"] is False
    scheduler.release_claim(workspace, owner_token, str(resource["id"]))
    rechecked = scheduler.heartbeat(workspace, owner_token)
    assert rechecked["restoration_pending_claim_ids"] == []
    assert "drain_requested" not in rechecked
    status = scheduler.status(workspace)
    promoted = next(claim for claim in status["claims"] if claim["id"] == freeze["id"])
    assert promoted["state"] == "active"


def test_resource_and_path_drain_rechecks_before_parking(
    scheduler: WorkspaceCoordinator, workspace: Path
) -> None:
    _, owner_token = start(scheduler, workspace, "mixed-owner")
    _, freeze_token = start(scheduler, workspace, "maintenance")
    path_claim = scheduler.acquire_claim(workspace, owner_token, writes=("Assets/Hero.prefab",))
    resource = scheduler.acquire_claim(workspace, owner_token, resources=("unity-live",))
    freeze = scheduler.acquire_claim(workspace, freeze_token, freeze=True)

    assert scheduler.heartbeat(workspace, owner_token)["drain_requested"]["park_ready"] is False
    scheduler.release_claim(workspace, owner_token, str(resource["id"]))
    rechecked = scheduler.heartbeat(workspace, owner_token)
    assert rechecked["drain_requested"]["park_ready"] is True
    parked = scheduler.park_task(workspace, owner_token)
    assert parked["claim_ids"] == [path_claim["id"]]
    status = scheduler.status(workspace)
    claims = {claim["id"]: claim for claim in status["claims"]}
    assert claims[path_claim["id"]]["state"] == "parked"
    assert claims[freeze["id"]]["state"] == "active"


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
    assert scheduler.heartbeat(workspace, owner_token)["restoration_pending_claim_ids"] == [
        owned["id"]
    ]

    scheduler.release_task(workspace, freeze_token, result="completed")
    assert scheduler.heartbeat(workspace, owner_token)["restoration_pending_claim_ids"] == []
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
    assert still_parked["parked_for"] is not None
    assert inspect_state(scheduler.paths.database)["counts"]["parked_claims"] == 1

    scheduler.resolve_unknown(
        workspace,
        str(freeze_task["id"]),
        resolution="failed",
        evidence="Maintenance process stopped before touching the workspace.",
    )
    status = scheduler.status(workspace)
    restored = next(claim for claim in status["claims"] if claim["id"] == owned["id"])
    assert restored["state"] == "active"


def test_release_unknown_clears_cancelled_queued_restoration_marker(
    scheduler: WorkspaceCoordinator, workspace: Path
) -> None:
    _, owner_token, active, queued = queued_restoration_with_active_claim(scheduler, workspace)

    scheduler.release_task(workspace, owner_token, result="outcome-unknown")

    by_id = {claim["id"]: claim for claim in scheduler.status(workspace)["claims"]}
    assert by_id[active["id"]]["state"] == "active"
    with open_database(scheduler.paths) as connection:
        cancelled = connection.execute(
            "SELECT claim.state, COUNT(marker.value) AS marker_count FROM claims AS claim "
            "LEFT JOIN claim_scopes AS marker ON marker.claim_id = claim.id "
            "AND marker.scope_type = 'parked_for' WHERE claim.id = ? GROUP BY claim.id",
            (queued["id"],),
        ).fetchone()
    assert dict(cancelled) == {"state": "cancelled", "marker_count": 0}
    assert inspect_state(scheduler.paths.database)["counts"]["active_claims"] == 2


def test_expiry_unknown_clears_cancelled_queued_restoration_marker(
    scheduler: WorkspaceCoordinator, workspace: Path
) -> None:
    owner_task, _, active, queued = queued_restoration_with_active_claim(scheduler, workspace)
    with open_database(scheduler.paths) as connection:
        connection.execute(
            "UPDATE tasks SET expires_at = ? WHERE id = ?",
            (time.time() - 1, owner_task["id"]),
        )

    status = scheduler.status(workspace)

    by_id = {claim["id"]: claim for claim in status["claims"]}
    task = next(item for item in status["tasks"] if item["id"] == owner_task["id"])
    assert task["state"] == "outcome_unknown"
    assert task["result"] == "expired-with-active-claim"
    assert by_id[active["id"]]["state"] == "active"
    with open_database(scheduler.paths) as connection:
        cancelled = connection.execute(
            "SELECT claim.state, COUNT(marker.value) AS marker_count FROM claims AS claim "
            "LEFT JOIN claim_scopes AS marker ON marker.claim_id = claim.id "
            "AND marker.scope_type = 'parked_for' WHERE claim.id = ? GROUP BY claim.id",
            (queued["id"],),
        ).fetchone()
    assert dict(cancelled) == {"state": "cancelled", "marker_count": 0}
    assert inspect_state(scheduler.paths.database)["counts"]["active_claims"] == 2


def test_resolve_unknown_rejects_disallowed_evidence_control(
    scheduler: WorkspaceCoordinator, workspace: Path
) -> None:
    task, token = start(scheduler, workspace, "unknown")
    scheduler.release_task(workspace, token, result="outcome-unknown")

    with pytest.raises(UsageError, match="non-empty normalized text"):
        scheduler.resolve_unknown(
            workspace,
            str(task["id"]),
            resolution="failed",
            evidence="unsafe\x00evidence",
        )

    assert scheduler.status(workspace)["blocked"] is True
    assert inspect_state(scheduler.paths.database)["counts"]["recovery_events"] == 0


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


def test_expired_active_path_claim_preserves_an_unknown_outcome_fence(
    scheduler: WorkspaceCoordinator, workspace: Path
) -> None:
    task, token = start(scheduler, workspace, "path-owner")
    claim = scheduler.acquire_claim(workspace, token, writes=("Assets/Hero.prefab",))
    with open_database(scheduler.paths) as connection:
        connection.execute(
            "UPDATE tasks SET expires_at = ? WHERE id = ?",
            (time.time() - 1, task["id"]),
        )

    status = scheduler.status(workspace)
    expired = next(item for item in status["tasks"] if item["id"] == task["id"])
    preserved = next(item for item in status["claims"] if item["id"] == claim["id"])
    assert status["blocked"] is True
    assert expired["state"] == "outcome_unknown"
    assert preserved["state"] == "active"


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
    with pytest.raises(ClaimAuthorizationError):
        scheduler.assert_claims(workspace, token, writes=("Assets/Scenes",))


def test_status_never_exposes_task_secret(scheduler: WorkspaceCoordinator, workspace: Path) -> None:
    _, token = start(scheduler, workspace, "private")
    payload = json.dumps(scheduler.status(workspace))
    assert token not in payload
    assert "token_hash" not in payload


def test_open_task_tokens_are_workspace_unique_and_reuse_authenticates_active(
    scheduler: WorkspaceCoordinator, workspace: Path
) -> None:
    shared_token = "fixed-owner-token"
    first, _ = scheduler.start_task(
        workspace,
        "first",
        "first work",
        token=shared_token,
    )
    with pytest.raises(UsageError) as duplicate:
        scheduler.start_task(
            workspace,
            "duplicate",
            "must be rejected",
            token=shared_token,
        )
    assert duplicate.value.details["reason"] == "open-task-token-conflict"
    assert duplicate.value.details["task_id"] == first["id"]
    assert duplicate.value.details["workspace_id"] == scheduler.status(workspace)["workspace"]["id"]

    other_workspace = workspace.parent / "other-workspace"
    other_workspace.mkdir()
    scheduler.register(other_workspace)
    with pytest.raises(UsageError) as other_duplicate:
        scheduler.start_task(
            other_workspace,
            "other",
            "same token in another workspace",
            token=shared_token,
        )
    assert other_duplicate.value.details["reason"] == "open-task-token-conflict"
    assert other_duplicate.value.details["task_id"] == first["id"]
    assert (
        other_duplicate.value.details["workspace_id"]
        == scheduler.status(workspace)["workspace"]["id"]
    )

    released = scheduler.release_task(workspace, shared_token, result="completed")
    acknowledged = scheduler.acknowledge_receipt(
        str(released["operation"]["operation_id"]),
        str(released["operation"]["fingerprint"]),
        str(released["operation"]["delivery_digest"]),
    )
    assert acknowledged["acknowledged"] is True
    assert acknowledged["token_file_removed"] is True
    active, _ = scheduler.start_task(
        workspace,
        "replacement",
        "terminal token reuse",
        token=shared_token,
    )
    with open_database(scheduler.paths) as connection:
        connection.execute(
            "UPDATE tasks SET created_at = ? WHERE id = ?",
            (float(active["created_at"]) + 1000.0, first["id"]),
        )
    renewed = scheduler.heartbeat(workspace, shared_token)
    assert renewed["id"] == active["id"]


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


def test_one_task_cannot_hold_duplicate_active_resource_claims(
    scheduler: WorkspaceCoordinator, workspace: Path
) -> None:
    _, token = start(scheduler, workspace, "owner")

    first = scheduler.acquire_claim(workspace, token, resources=("unity-live",))
    second = scheduler.acquire_claim(workspace, token, resources=("unity-live",))

    assert first["state"] == "active"
    assert second["state"] == "queued"


@pytest.mark.parametrize("ttl", [float("nan"), float("inf"), float("-inf"), 0, -1, 86400.1])
def test_task_start_rejects_non_finite_and_out_of_range_ttl(
    scheduler: WorkspaceCoordinator, workspace: Path, ttl: float
) -> None:
    with pytest.raises(UsageError, match="finite"):
        start(scheduler, workspace, "invalid-ttl", ttl=ttl)


@pytest.mark.parametrize("ttl", [float("nan"), float("inf"), float("-inf"), 0, -1, 86400.1])
def test_heartbeat_rejects_non_finite_and_out_of_range_ttl(
    scheduler: WorkspaceCoordinator, workspace: Path, ttl: float
) -> None:
    _, token = start(scheduler, workspace, "owner")
    with pytest.raises(UsageError, match="finite"):
        scheduler.heartbeat(workspace, token, ttl_seconds=ttl)


def test_task_ttl_accepts_the_documented_upper_bound(
    scheduler: WorkspaceCoordinator, workspace: Path
) -> None:
    _, token = start(scheduler, workspace, "owner", ttl=86400)
    renewed = scheduler.heartbeat(workspace, token, ttl_seconds=86400)
    assert renewed["expires_at"] > renewed["heartbeat_at"]


def test_heartbeat_without_ttl_preserves_the_configured_lease_duration(
    scheduler: WorkspaceCoordinator, workspace: Path
) -> None:
    _, token = start(scheduler, workspace, "owner", ttl=7200)

    renewed = scheduler.heartbeat(workspace, token)

    assert renewed["expires_at"] - renewed["heartbeat_at"] == pytest.approx(7200)


def test_clock_rollback_rebases_only_target_active_tasks_and_preserves_unknown_fence(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    first_workspace = tmp_path / "first"
    second_workspace = tmp_path / "second"
    first_workspace.mkdir()
    second_workspace.mkdir()
    scheduler = WorkspaceCoordinator(resolve_state_paths(tmp_path / "state"))
    first_registered = scheduler.register(first_workspace)
    second_registered = scheduler.register(second_workspace)
    first_task, _ = start(scheduler, first_workspace, "first")
    oversized_task, _ = start(scheduler, first_workspace, "oversized")
    unknown_task, unknown_token = start(scheduler, first_workspace, "unknown")
    scheduler.release_task(first_workspace, unknown_token, result="outcome-unknown")
    second_task, _ = start(scheduler, second_workspace, "second")

    current_time = (
        max(
            float(first_task["created_at"]),
            float(oversized_task["created_at"]),
            float(second_task["created_at"]),
        )
        + 1000.0
    )
    future_heartbeat = current_time + 100_000.0
    with open_database(scheduler.paths) as connection:
        connection.execute(
            "UPDATE tasks SET heartbeat_at = ?, expires_at = ? WHERE id = ?",
            (future_heartbeat, future_heartbeat + 1800.0, first_task["id"]),
        )
        connection.execute(
            "UPDATE tasks SET heartbeat_at = ?, expires_at = ? WHERE id = ?",
            (current_time - 10.0, current_time + 200_000.0, oversized_task["id"]),
        )
        connection.execute(
            "UPDATE tasks SET heartbeat_at = ?, expires_at = ? WHERE id = ?",
            (future_heartbeat, future_heartbeat + 1800.0, unknown_task["id"]),
        )
        connection.execute(
            "UPDATE tasks SET heartbeat_at = ?, expires_at = ? WHERE id = ?",
            (future_heartbeat, future_heartbeat + 1800.0, second_task["id"]),
        )
        second_epoch_before = connection.execute(
            "SELECT epoch FROM workspaces WHERE id = ?", (second_registered["id"],)
        ).fetchone()[0]

    monkeypatch.setattr(coordinator_module.time, "time", lambda: current_time)
    status = scheduler.status(first_workspace)
    tasks = {task["id"]: task for task in status["tasks"]}
    assert tasks[first_task["id"]]["heartbeat_at"] == current_time
    assert tasks[first_task["id"]]["expires_at"] == current_time + 1800.0
    assert tasks[oversized_task["id"]]["heartbeat_at"] == current_time - 10.0
    assert tasks[oversized_task["id"]]["expires_at"] == current_time + 86400.0
    assert tasks[unknown_task["id"]]["heartbeat_at"] == future_heartbeat
    assert tasks[unknown_task["id"]]["expires_at"] == future_heartbeat + 1800.0

    with open_database(scheduler.paths) as connection:
        untouched = connection.execute(
            "SELECT heartbeat_at, expires_at FROM tasks WHERE id = ?", (second_task["id"],)
        ).fetchone()
        second_epoch_after = connection.execute(
            "SELECT epoch FROM workspaces WHERE id = ?", (second_registered["id"],)
        ).fetchone()[0]
    assert untouched["heartbeat_at"] == future_heartbeat
    assert untouched["expires_at"] == future_heartbeat + 1800.0
    assert second_epoch_after == second_epoch_before
    assert first_registered["id"] != second_registered["id"]


def test_clock_rollback_with_nonpositive_lease_fails_safe_to_unknown(
    scheduler: WorkspaceCoordinator,
    workspace: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    task, token = start(scheduler, workspace, "owner")
    claim = scheduler.acquire_claim(workspace, token, resources=("unity-live",))
    current_time = float(task["created_at"]) + 1000.0
    with open_database(scheduler.paths) as connection:
        connection.execute(
            "UPDATE tasks SET heartbeat_at = ?, expires_at = ? WHERE id = ?",
            (current_time + 1000.0, current_time + 500.0, task["id"]),
        )

    monkeypatch.setattr(coordinator_module.time, "time", lambda: current_time)
    status = scheduler.status(workspace)
    preserved_task = next(item for item in status["tasks"] if item["id"] == task["id"])
    preserved_claim = next(item for item in status["claims"] if item["id"] == claim["id"])
    assert preserved_task["state"] == "outcome_unknown"
    assert preserved_task["heartbeat_at"] == current_time
    assert preserved_task["expires_at"] == current_time
    assert preserved_claim["state"] == "active"
    assert status["blocked"] is True


def test_invalid_system_clock_fails_closed_before_maintenance(
    scheduler: WorkspaceCoordinator,
    workspace: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    task, _ = start(scheduler, workspace, "owner")
    monkeypatch.setattr(coordinator_module.time, "time", lambda: float("nan"))
    with pytest.raises(StateError) as invalid_clock:
        scheduler.status(workspace)
    assert invalid_clock.value.details == {"reason": "system-clock-invalid"}
    with open_database(scheduler.paths) as connection:
        state = connection.execute(
            "SELECT state FROM tasks WHERE id = ?", (task["id"],)
        ).fetchone()[0]
    assert state == "active"


@pytest.mark.parametrize("wait", [float("nan"), float("inf"), float("-inf"), -0.1, 86400.1])
def test_claim_and_park_reject_non_finite_or_negative_waits(
    scheduler: WorkspaceCoordinator, workspace: Path, wait: float
) -> None:
    _, token = start(scheduler, workspace, "owner")
    with pytest.raises(UsageError, match="finite"):
        scheduler.acquire_claim(
            workspace,
            token,
            writes=("Assets/Hero.prefab",),
            wait_seconds=wait,
        )
    with pytest.raises(UsageError, match="finite"):
        scheduler.park_task(workspace, token, wait_seconds=wait)


def test_acquire_timeout_final_transaction_observes_a_concurrent_grant(
    scheduler: WorkspaceCoordinator, workspace: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    _, blocker_token = start(scheduler, workspace, "blocker")
    _, waiter_token = start(scheduler, workspace, "waiter")
    blocker = scheduler.acquire_claim(workspace, blocker_token, resources=("unity-live",))
    monotonic_calls = 0

    def race_monotonic() -> float:
        nonlocal monotonic_calls
        monotonic_calls += 1
        if monotonic_calls == 1:
            return 0.0
        if monotonic_calls == 2:
            scheduler.release_claim(workspace, blocker_token, str(blocker["id"]))
        return 2.0

    monkeypatch.setattr(coordinator_module.time, "monotonic", race_monotonic)
    result = scheduler.acquire_claim(
        workspace,
        waiter_token,
        resources=("unity-live",),
        wait_seconds=1.0,
    )

    assert result["state"] == "active"
    assert result["granted"] is True
    assert result["timed_out"] is False


def test_park_timeout_final_read_never_marks_a_resumed_claim_timed_out(
    scheduler: WorkspaceCoordinator, workspace: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    _, owner_token = start(scheduler, workspace, "owner")
    _, freeze_token = start(scheduler, workspace, "maintenance")
    owned = scheduler.acquire_claim(workspace, owner_token, writes=("Assets/Hero.prefab",))
    freeze = scheduler.acquire_claim(workspace, freeze_token, freeze=True)
    monotonic_calls = 0

    def race_monotonic() -> float:
        nonlocal monotonic_calls
        monotonic_calls += 1
        if monotonic_calls == 1:
            return 0.0
        if monotonic_calls == 2:
            scheduler.release_claim(workspace, freeze_token, str(freeze["id"]))
        return 2.0

    monkeypatch.setattr(coordinator_module.time, "monotonic", race_monotonic)
    result = scheduler.park_task(workspace, owner_token, wait_seconds=1.0)

    assert result["states"] == {owned["id"]: "active"}
    assert result["resumed"] is True
    assert result["parked"] is False
    assert result["timed_out"] is False


def test_registration_is_idempotent_and_unregister_refuses_active_task(
    scheduler: WorkspaceCoordinator, workspace: Path
) -> None:
    assert scheduler.register(workspace)["created"] is False
    _, token = start(scheduler, workspace, "owner")
    with pytest.raises(BusyError):
        scheduler.unregister(workspace)
    released = scheduler.release_task(workspace, token, result="completed")
    acknowledged = scheduler.acknowledge_receipt(
        str(released["operation"]["operation_id"]),
        str(released["operation"]["fingerprint"]),
        str(released["operation"]["delivery_digest"]),
    )
    assert acknowledged["acknowledged"] is True
    assert acknowledged["token_file_removed"] is True
    with open_database(scheduler.paths) as connection:
        assert (
            connection.execute(
                "SELECT COUNT(*) FROM operation_receipts "
                "WHERE token_cleanup_path IS NOT NULL AND delivered_at IS NULL"
            ).fetchone()[0]
            == 0
        )
        assert connection.execute("SELECT COUNT(*) FROM token_cleanup_jobs").fetchone()[0] == 0
    assert scheduler.unregister(workspace)["removed"] is True


def test_queue_cancel_never_releases_active_or_parked_claims(
    scheduler: WorkspaceCoordinator, workspace: Path
) -> None:
    _, owner_token = start(scheduler, workspace, "owner")
    _, waiter_token = start(scheduler, workspace, "waiter")
    _, freeze_token = start(scheduler, workspace, "maintenance")
    active = scheduler.acquire_claim(workspace, owner_token, writes=("Assets/Hero.prefab",))
    queued = scheduler.acquire_claim(workspace, waiter_token, writes=("Assets/Hero.prefab",))

    with pytest.raises(StateError, match="exact queued claim"):
        scheduler.cancel_claim(workspace, owner_token, str(active["id"]))
    cancelled = scheduler.cancel_claim(workspace, waiter_token, str(queued["id"]))
    assert cancelled["state"] == "cancelled"

    freeze = scheduler.acquire_claim(workspace, freeze_token, freeze=True)
    scheduler.park_task(workspace, owner_token)
    with pytest.raises(StateError, match="exact queued claim"):
        scheduler.cancel_claim(workspace, owner_token, str(active["id"]))
    status = scheduler.status(workspace)
    preserved = next(claim for claim in status["claims"] if claim["id"] == active["id"])
    assert preserved["state"] == "parked"
    scheduler.release_claim(workspace, freeze_token, str(freeze["id"]))
