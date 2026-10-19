from __future__ import annotations

import pytest

from unity_workspace_scheduler.coordinator import WorkspaceCoordinator
from unity_workspace_scheduler.errors import BusyError, ClaimAuthorizationError
from unity_workspace_scheduler.state import resolve_state_paths


@pytest.mark.parametrize("priority", ["normal", "urgent"])
def test_earlier_live_queue_finishes_before_later_equal_priority_freeze(tmp_path, priority):
    project = tmp_path / "project"
    project.mkdir()
    source = project / "draft.cs"
    source.write_bytes(b"preserve this unsubmitted draft\n")
    scheduler = WorkspaceCoordinator(resolve_state_paths(tmp_path / "state"))
    scheduler.register(project)
    _, holder = scheduler.start_task(project, "holder", "already running", ttl_seconds=60)
    _, owner = scheduler.start_task(project, "owner", "prepared verification", ttl_seconds=60)
    _, maintenance = scheduler.start_task(project, "maintenance", "later freeze", ttl_seconds=60)
    held = scheduler.acquire_claim(project, holder, resources=("unity-live",))
    saved = scheduler.acquire_claim(project, owner, writes=("draft.cs",))
    queued = scheduler.acquire_claim(project, owner, resources=("unity-live",), priority=priority)
    freeze = scheduler.acquire_claim(project, maintenance, freeze=True, priority=priority)
    assert queued["state"] == "queued" and queued["queue_order"] < freeze["queue_order"]
    assert "drain_requested" not in scheduler.heartbeat(project, owner)
    public = scheduler.status(project)
    assert "drain_requested" not in next(t for t in public["tasks"] if t["owner"] == "owner")
    assert scheduler.assert_claims(project, owner, writes=("draft.cs",))["authorized"]
    with pytest.raises(ClaimAuthorizationError):
        scheduler.assert_claims(project, owner, resources=("unity-live",))

    scheduler.release_claim(project, holder, held["id"])
    granted = next(c for c in scheduler.status(project)["claims"] if c["id"] == queued["id"])
    assert granted["state"] == "active" and granted["queue_order"] == queued["queue_order"]
    assert scheduler.assert_claims(project, owner, resources=("unity-live",))["authorized"]
    with pytest.raises(ClaimAuthorizationError):
        scheduler.assert_claims(project, owner, writes=("other.cs",))
    if priority == "normal":
        with pytest.raises(BusyError, match="freeze is waiting"):
            scheduler.acquire_claim(project, owner, writes=("other.cs",))
    with pytest.raises(BusyError, match="cannot be parked") as unsafe:
        scheduler.park_task(project, owner)
    assert unsafe.value.details["reason"] == "task-holds-unsafe-claims"

    scheduler.release_claim(project, owner, queued["id"])
    assert scheduler.heartbeat(project, owner)["drain_requested"]["park_ready"] is True
    parked = scheduler.park_task(project, owner)
    assert parked["claim_ids"] == [saved["id"]]
    assert (
        next(c for c in scheduler.status(project)["claims"] if c["id"] == freeze["id"])["state"]
        == "active"
    )
    scheduler.release_claim(project, maintenance, freeze["id"])
    restored = next(c for c in scheduler.status(project)["claims"] if c["id"] == saved["id"])
    assert (
        restored["state"] == "active"
        and source.read_bytes() == b"preserve this unsubmitted draft\n"
    )


def test_later_urgent_freeze_cannot_revoke_already_active_normal_live_claim(tmp_path):
    project = tmp_path / "project"
    project.mkdir()
    scheduler = WorkspaceCoordinator(resolve_state_paths(tmp_path / "state"))
    scheduler.register(project)
    _, owner = scheduler.start_task(project, "owner", "bounded existing chain", ttl_seconds=60)
    _, maintenance = scheduler.start_task(project, "maintenance", "urgent freeze", ttl_seconds=60)
    live = scheduler.acquire_claim(project, owner, resources=("unity-live",))
    freeze = scheduler.acquire_claim(project, maintenance, freeze=True, priority="urgent")
    assert "drain_requested" not in scheduler.heartbeat(project, owner)
    assert scheduler.assert_claims(project, owner, resources=("unity-live",))["authorized"]
    with pytest.raises(BusyError):
        scheduler.acquire_claim(project, owner, resources=("another-tool",))
    scheduler.release_claim(project, owner, live["id"])
    assert (
        next(c for c in scheduler.status(project)["claims"] if c["id"] == freeze["id"])["state"]
        == "active"
    )


def test_queued_normal_live_does_not_override_higher_priority_freeze(tmp_path):
    project = tmp_path / "project"
    project.mkdir()
    scheduler = WorkspaceCoordinator(resolve_state_paths(tmp_path / "state"))
    scheduler.register(project)
    _, holder = scheduler.start_task(project, "holder", "running", ttl_seconds=60)
    _, owner = scheduler.start_task(project, "owner", "normal wait", ttl_seconds=60)
    _, maintenance = scheduler.start_task(project, "maintenance", "urgent freeze", ttl_seconds=60)
    scheduler.acquire_claim(project, holder, resources=("unity-live",))
    scheduler.acquire_claim(project, owner, writes=("draft.cs",))
    queued = scheduler.acquire_claim(project, owner, resources=("unity-live",))
    freeze = scheduler.acquire_claim(project, maintenance, freeze=True, priority="urgent")
    drain = scheduler.heartbeat(project, owner)["drain_requested"]
    assert drain["freeze_id"] == freeze["id"] and drain["park_ready"] is True
    with pytest.raises(BusyError):
        scheduler.assert_claims(project, owner, writes=("draft.cs",))
    assert (
        next(c for c in scheduler.status(project)["claims"] if c["id"] == queued["id"])["state"]
        == "queued"
    )
