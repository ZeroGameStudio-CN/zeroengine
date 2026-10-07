from __future__ import annotations

import time
from pathlib import Path

import pytest

import unity_workspace_scheduler.coordinator as coordinator_module
from unity_workspace_scheduler.coordinator import WorkspaceCoordinator
from unity_workspace_scheduler.state import resolve_state_paths


@pytest.fixture
def queue(tmp_path: Path, monkeypatch: pytest.MonkeyPatch):
    clock = [time.time()]
    monkeypatch.setattr(coordinator_module.time, "time", lambda: clock[0])
    scheduler = WorkspaceCoordinator(resolve_state_paths(tmp_path / "state"))
    workspace = tmp_path / "workspace"
    workspace.mkdir()
    scheduler.register(workspace)
    return scheduler, workspace, clock


def owner(scheduler, workspace, name):
    return scheduler.start_task(workspace, name, f"{name} work")[1]


def claims(scheduler, workspace):
    return {claim["id"]: claim for claim in scheduler.status(workspace)["claims"]}


@pytest.mark.parametrize("elapsed,normal_first", [(299.999, False), (300.0, True), (301, True)])
def test_normal_wait_eventually_wins_without_changing_priority_receipt(
    queue, elapsed, normal_first
):
    scheduler, workspace, clock = queue
    holder = owner(scheduler, workspace, "holder")
    normal = owner(scheduler, workspace, "normal")
    urgent = owner(scheduler, workspace, "urgent")
    active = scheduler.acquire_claim(workspace, holder, resources=("unity-live",))
    waiting = scheduler.acquire_claim(workspace, normal, resources=("unity-live",))
    clock[0] += elapsed
    later = scheduler.acquire_claim(workspace, urgent, resources=("unity-live",), priority="urgent")
    before = claims(scheduler, workspace)
    assert before[active["id"]]["state"] == "active"
    assert before[waiting["id"]]["state"] == before[later["id"]]["state"] == "queued"
    assert before[waiting["id"]]["scheduling_rank"] == (0 if normal_first else 1)
    assert before[later["id"]]["scheduling_rank"] == 0
    assert "scheduling_rank" not in waiting  # Acquire receipts never acquire time-varying fields.

    scheduler.release_claim(workspace, holder, active["id"])
    after = claims(scheduler, workspace)
    winner, loser = (waiting, later) if normal_first else (later, waiting)
    assert after[winner["id"]]["state"] == "active"
    assert after[loser["id"]]["state"] == "queued"
    assert after[waiting["id"]]["priority"] == "normal"
    assert after[waiting["id"]]["queue_order"] == waiting["queue_order"]
    assert after[later["id"]]["priority"] == "urgent"


def test_continuing_urgent_arrivals_cannot_starve_normal_live(queue):
    scheduler, workspace, clock = queue
    holder = owner(scheduler, workspace, "holder")
    normal = owner(scheduler, workspace, "normal")
    active = scheduler.acquire_claim(workspace, holder, resources=("unity-live",))
    waiting = scheduler.acquire_claim(workspace, normal, resources=("unity-live",))
    initial = clock[0]
    for index, elapsed in enumerate((100, 200, 300)):
        clock[0] = initial + elapsed
        urgent = owner(scheduler, workspace, f"urgent-{index}")
        candidate = scheduler.acquire_claim(
            workspace, urgent, resources=("unity-live",), priority="urgent"
        )
        scheduler.release_claim(workspace, holder, active["id"])
        state = claims(scheduler, workspace)
        if elapsed < 300:
            assert state[candidate["id"]]["state"] == "active"
            assert state[waiting["id"]]["state"] == "queued"
            holder, active = urgent, candidate
        else:
            assert state[waiting["id"]]["state"] == "active"
            assert state[candidate["id"]]["state"] == "queued"


def test_aged_normal_freeze_drains_and_parks_before_later_urgent_freeze(queue):
    scheduler, workspace, clock = queue
    writer = owner(scheduler, workspace, "writer")
    normal = owner(scheduler, workspace, "maintenance")
    urgent = owner(scheduler, workspace, "urgent-maintenance")
    source = scheduler.acquire_claim(workspace, writer, writes=("Assets/Hero.cs",))
    freeze = scheduler.acquire_claim(workspace, normal, freeze=True)
    clock[0] += 300
    later = scheduler.acquire_claim(workspace, urgent, freeze=True, priority="urgent")
    drain = scheduler.heartbeat(workspace, writer)["drain_requested"]
    assert drain["freeze_id"] == freeze["id"]
    assert drain["priority"] == "normal"
    assert drain["park_ready"] is True
    scheduler.park_task(workspace, writer)
    state = claims(scheduler, workspace)
    assert state[source["id"]]["state"] == "parked"
    assert state[freeze["id"]]["state"] == "active"
    assert state[later["id"]]["state"] == "queued"
    scheduler.release_claim(workspace, normal, freeze["id"])
    # Restored source keeps its original FIFO age, so it can resume before the
    # later urgent maintenance request and cooperatively park for it next.
    restored = claims(scheduler, workspace)[source["id"]]
    assert restored["state"] == "active"
    assert restored["queue_order"] == source["queue_order"]
    assert scheduler.heartbeat(workspace, writer)["drain_requested"]["freeze_id"] == later["id"]
    scheduler.park_task(workspace, writer)
    assert claims(scheduler, workspace)[later["id"]]["state"] == "active"


def test_aged_freeze_does_not_interrupt_active_urgent_live(queue):
    scheduler, workspace, clock = queue
    writer = owner(scheduler, workspace, "writer")
    normal = owner(scheduler, workspace, "maintenance")
    urgent = owner(scheduler, workspace, "urgent")
    scheduler.acquire_claim(workspace, writer, writes=("Assets/Hero.cs",))
    freeze = scheduler.acquire_claim(workspace, normal, freeze=True)
    active = scheduler.acquire_claim(
        workspace, urgent, resources=("unity-live",), priority="urgent"
    )
    clock[0] += 300
    assert scheduler.heartbeat(workspace, urgent).get("drain_requested") is None
    scheduler.park_task(workspace, writer)
    assert claims(scheduler, workspace)[freeze["id"]]["state"] == "queued"
    scheduler.release_claim(workspace, urgent, active["id"])
    assert claims(scheduler, workspace)[freeze["id"]]["state"] == "active"


def test_aged_freeze_keeps_unknown_fence_and_disjoint_concurrency(queue):
    scheduler, workspace, clock = queue
    unknown = owner(scheduler, workspace, "unknown")
    normal = owner(scheduler, workspace, "maintenance")
    independent = owner(scheduler, workspace, "independent")
    uncertain = scheduler.acquire_claim(workspace, unknown, writes=("Assets/Hero.cs",))
    freeze = scheduler.acquire_claim(workspace, normal, freeze=True)
    scheduler.release_task(workspace, unknown, result="outcome-unknown")
    clock[0] += 300
    source = scheduler.acquire_claim(workspace, independent, writes=("Assets/Other.cs",))
    live = scheduler.acquire_claim(workspace, independent, resources=("unity-live",))
    state = claims(scheduler, workspace)
    assert state[uncertain["id"]]["state"] == "active"
    assert state[freeze["id"]]["state"] == "queued"
    assert state[source["id"]]["state"] == state[live["id"]]["state"] == "active"
    assert scheduler.heartbeat(workspace, independent).get("drain_requested") is None


def test_backward_clock_does_not_give_unearned_aging_credit(queue):
    scheduler, workspace, clock = queue
    holder = owner(scheduler, workspace, "holder")
    normal = owner(scheduler, workspace, "normal")
    urgent = owner(scheduler, workspace, "urgent")
    clock[0] += 600
    active = scheduler.acquire_claim(workspace, holder, resources=("unity-live",))
    waiting = scheduler.acquire_claim(workspace, normal, resources=("unity-live",))
    clock[0] -= 300
    later = scheduler.acquire_claim(workspace, urgent, resources=("unity-live",), priority="urgent")
    scheduler.release_claim(workspace, holder, active["id"])
    state = claims(scheduler, workspace)
    assert state[later["id"]]["state"] == "active"
    assert state[waiting["id"]]["state"] == "queued"
