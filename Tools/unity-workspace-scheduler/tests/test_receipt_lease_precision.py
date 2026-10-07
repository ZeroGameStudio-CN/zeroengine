from __future__ import annotations

import json
import math
import sqlite3
from pathlib import Path

import pytest

import unity_workspace_scheduler.coordinator as coordinator_module
from unity_workspace_scheduler.coordinator import WorkspaceCoordinator
from unity_workspace_scheduler.errors import StateError
from unity_workspace_scheduler.operations import receipt_delivery_digest
from unity_workspace_scheduler.state import resolve_state_paths
from unity_workspace_scheduler.state_ops import backup_state, verify_state


@pytest.mark.parametrize("action", ["start", "heartbeat"])
@pytest.mark.parametrize("ttl", [0.1, 600.1, 604.792355])
def test_fractional_lease_receipts_can_be_backed_up(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch, action: str, ttl: float
) -> None:
    now = 1_791_391_800.123456
    monkeypatch.setattr(coordinator_module.time, "time", lambda: now)
    workspace = tmp_path / "workspace"
    workspace.mkdir()
    scheduler = WorkspaceCoordinator(resolve_state_paths(tmp_path / "state"))
    scheduler.register(workspace)
    started, token = scheduler.start_task(
        workspace, "owner", "fractional lease", ttl_seconds=ttl if action == "start" else 600
    )
    result = (
        started if action == "start" else scheduler.heartbeat(workspace, token, ttl_seconds=ttl)
    )
    assert result["expires_at"] == now + ttl
    assert not math.isclose(result["expires_at"] - now, ttl, rel_tol=0, abs_tol=1e-9)
    operation = result["operation"]
    if action == "start":
        replay, replay_token = scheduler.start_task(
            workspace,
            "owner",
            "fractional lease",
            ttl_seconds=ttl,
            operation_id=operation["operation_id"],
            token=token,
        )
        assert replay_token == token
    else:
        replay = scheduler.heartbeat(
            workspace, token, ttl_seconds=ttl, operation_id=operation["operation_id"]
        )
    assert replay["operation"]["replayed"] is True
    assert replay["expires_at"] == result["expires_at"]
    assert (
        scheduler.acknowledge_receipt(
            operation["operation_id"], operation["fingerprint"], operation["delivery_digest"]
        )["acknowledged"]
        is True
    )
    backup = tmp_path / "backup.sqlite3"
    backup_state(scheduler.paths, backup, confirm_no_processes=True)
    assert verify_state(backup)["integrity_check"] == "ok"


@pytest.mark.parametrize(
    "action,route", [("start", "backup"), ("heartbeat", "backup"), ("start", "ack")]
)
@pytest.mark.parametrize("direction", [-math.inf, math.inf])
def test_lease_expiry_changed_by_one_float_step_is_rejected(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch, action: str, route: str, direction: float
) -> None:
    monkeypatch.setattr(coordinator_module.time, "time", lambda: 1_791_391_800.123456)
    workspace = tmp_path / "workspace"
    workspace.mkdir()
    scheduler = WorkspaceCoordinator(resolve_state_paths(tmp_path / "state"))
    scheduler.register(workspace)
    started, token = scheduler.start_task(workspace, "owner", "expiry integrity", ttl_seconds=600.1)
    result = (
        started if action == "start" else scheduler.heartbeat(workspace, token, ttl_seconds=600.1)
    )
    operation = result["operation"]
    with sqlite3.connect(scheduler.paths.database) as connection:
        stored = json.loads(
            connection.execute(
                "SELECT result_json FROM operation_receipts WHERE operation_id = ?",
                (operation["operation_id"],),
            ).fetchone()[0]
        )
        stored["expires_at"] = math.nextafter(stored["expires_at"], direction)
        stored_json = json.dumps(stored, sort_keys=True, separators=(",", ":"))
        connection.execute(
            "UPDATE operation_receipts SET result_json = ? WHERE operation_id = ?",
            (stored_json, operation["operation_id"]),
        )
    backup = tmp_path / "backup.sqlite3"
    with pytest.raises(StateError) as invalid:
        if route == "backup":
            backup_state(scheduler.paths, backup, confirm_no_processes=True)
        else:
            scheduler.acknowledge_receipt(
                operation["operation_id"],
                operation["fingerprint"],
                receipt_delivery_digest(stored_json, None),
            )
    assert invalid.value.details["reason"] == "operation-receipt-invalid"
    assert not backup.exists()
