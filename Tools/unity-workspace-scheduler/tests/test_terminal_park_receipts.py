from __future__ import annotations

import json
import sqlite3
import uuid
from contextlib import closing

import pytest
from test_cli import private_token_root  # noqa: F401 - shared private test-parent fixture

import unity_workspace_scheduler.coordinator as coordinator_module
from unity_workspace_scheduler.coordinator import WorkspaceCoordinator
from unity_workspace_scheduler.errors import StateError
from unity_workspace_scheduler.operations import canonical_json
from unity_workspace_scheduler.state import create_token_file, resolve_state_paths
from unity_workspace_scheduler.state_ops import backup_state, inspect_state, verify_state


class LostParkReply(RuntimeError):
    pass


def terminal_park_case(request, monkeypatch, result):
    root = request.getfixturevalue("private_token_root").resolve()
    workspace = root / "project"
    workspace.mkdir()
    scheduler = WorkspaceCoordinator(resolve_state_paths(root / "state"))
    scheduler.register(workspace)
    token_path = root / "owner.token"
    owner, token = scheduler.start_task(
        workspace, "owner", "terminal park", token_file_path=str(token_path)
    )
    create_token_file(token_path, token)
    _, maintenance = scheduler.start_task(workspace, "maintenance", "restore conflict")
    first = scheduler.acquire_claim(workspace, token, writes=["first.txt"])
    second = scheduler.acquire_claim(workspace, token, writes=["second.txt"])
    freeze = scheduler.acquire_claim(workspace, maintenance, freeze=True, keep_queued=True)
    operation_id = str(uuid.uuid4())

    def lose_reply(_delay):
        raise LostParkReply("Client stopped after the pending park receipt committed.")

    with monkeypatch.context() as patch:
        patch.setattr(coordinator_module.time, "sleep", lose_reply)
        with pytest.raises(LostParkReply):
            scheduler.park_task(workspace, token, operation_id=operation_id, wait_seconds=60)

    # Maintenance may retain an exact write after releasing its global freeze.
    # One original write restores; the other remains queued and is cancelled by its owner.
    scheduler.acquire_claim(workspace, maintenance, writes=["second.txt"])
    scheduler.release_claim(workspace, maintenance, str(freeze["id"]))
    scheduler.cancel_claim(workspace, token, str(second["id"]))
    terminal = scheduler.release_task(
        workspace, token, result=result, token_cleanup_path=str(token_path)
    )
    scheduler.acknowledge_receipt(
        terminal["operation"]["operation_id"],
        terminal["operation"]["fingerprint"],
        terminal["operation"]["delivery_digest"],
    )
    with closing(sqlite3.connect(scheduler.paths.database)) as connection:
        stored = connection.execute(
            "SELECT result_json, delivered_at, retired_at FROM operation_receipts "
            "WHERE operation_id = ?",
            (operation_id,),
        ).fetchone()
    payload = json.loads(stored[0])
    assert payload["reason"] == "task-released"
    assert payload["aborted"] is True
    assert payload["states"] == {str(first["id"]): "released", str(second["id"]): "cancelled"}
    assert stored[1] is None and stored[2] is not None
    return scheduler, root, operation_id, str(owner["id"]), payload


@pytest.mark.parametrize("result", ["completed", "failed"])
def test_terminal_park_with_owner_cancelled_restore_survives_state_backup(
    request, monkeypatch, result
):
    scheduler, root, _, _, _ = terminal_park_case(request, monkeypatch, result)
    before = inspect_state(scheduler.paths.database)
    assert verify_state(scheduler.paths.database)["integrity_check"] == "ok"
    backup = root / "terminal-park-backup.sqlite3"
    report = backup_state(scheduler.paths, backup, confirm_no_processes=True)
    assert report["backup"]["integrity_check"] == "ok"
    assert inspect_state(backup)["counts"] == before["counts"]


@pytest.mark.parametrize(
    "tamper", ["cancelled_to_released", "released_to_cancelled", "foreign_claim"]
)
def test_terminal_park_backup_rejects_forged_terminal_claim_states(request, monkeypatch, tamper):
    scheduler, _, operation_id, _, payload = terminal_park_case(request, monkeypatch, "failed")
    states = payload["states"]
    released = next(key for key, value in states.items() if value == "released")
    cancelled = next(key for key, value in states.items() if value == "cancelled")
    if tamper == "cancelled_to_released":
        states[cancelled] = "released"
    elif tamper == "released_to_cancelled":
        states[released] = "cancelled"
    else:
        # Keep a real matching cancelled state, but substitute another task's claim.
        workspace = scheduler.paths.root.parent / "project"
        _, foreign_token = scheduler.start_task(workspace, "foreign", "unrelated claim")
        foreign_claim = scheduler.acquire_claim(
            workspace, foreign_token, writes=["second.txt"], keep_queued=True
        )
        assert foreign_claim["state"] == "queued"
        foreign = str(foreign_claim["id"])
        scheduler.cancel_claim(workspace, foreign_token, foreign)
        payload["claim_ids"][payload["claim_ids"].index(cancelled)] = foreign
        states[foreign] = states.pop(cancelled)
    with closing(sqlite3.connect(scheduler.paths.database)) as connection:
        connection.execute(
            "UPDATE operation_receipts SET result_json = ? WHERE operation_id = ?",
            (canonical_json(payload), operation_id),
        )
        connection.commit()
    with pytest.raises(StateError):
        verify_state(scheduler.paths.database)
