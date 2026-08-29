from __future__ import annotations

import json
import os
import shutil
import sqlite3
import tempfile
import threading
import time
import uuid
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

import pytest

import unity_workspace_scheduler.coordinator as coordinator_module
import unity_workspace_scheduler.state as state_module
from unity_workspace_scheduler.coordinator import WorkspaceCoordinator, _remaining_operation_wait
from unity_workspace_scheduler.errors import AuthorizationError, BusyError, StateError, UsageError
from unity_workspace_scheduler.state import open_database, resolve_state_paths
from unity_workspace_scheduler.state_ops import (
    _validate_lifecycle_terminal_proof,
    backup_state,
    inspect_state,
    restore_state,
    verify_state,
)


def operation_id() -> str:
    return str(uuid.uuid4())


class _Receipt(dict[str, object]):
    """Small row-shaped fixture for the two independent proof validators."""

    def __getitem__(self, key: str) -> object:
        return super().__getitem__(key)


def _resolution_receipt(action: str, proof: dict[str, object]) -> _Receipt:
    return _Receipt(
        action=action,
        operation_id=operation_id(),
        parameters_json="{}",
        terminal_json=json.dumps(proof, sort_keys=True, separators=(",", ":")),
        created_at=10.0,
        finalized_at=12.0,
        retired_at=30.0,
    )


def _recovery_proof(*, cleanup: bool) -> dict[str, object]:
    proof: dict[str, object] = {
        "resolution_reason": "task-recovery-resolved",
        "terminal_finished_at": 20.0,
        "terminal_result": "recovered-completed",
        "terminal_state": "completed",
    }
    if cleanup:
        proof["token_cleanup_completed"] = True
    return proof


@pytest.mark.parametrize(
    ("action", "stored_result", "cleanup"),
    (
        ("task.heartbeat", {"aborted": True, "reason": "task-released-outcome-unknown"}, False),
        ("claim.acquire", {"aborted": True, "reason": "task-ttl-expired-with-active-claim"}, False),
        (
            "freeze.acquire",
            {"aborted": True, "reason": "task-ttl-expired-with-active-claim"},
            False,
        ),
        ("task.park", {"aborted": True, "reason": "task-ttl-expired-with-active-claim"}, False),
        ("claim.release", {"id": "claim", "task_id": "task", "state": "released"}, True),
        ("claim.release", {"id": "claim", "task_id": "task", "state": "cancelled"}, True),
        ("queue.cancel", {"id": "claim", "task_id": "task", "state": "cancelled"}, True),
        (
            "task.release",
            {"state": "outcome_unknown", "result": "outcome-unknown", "created_at": 10.0},
            True,
        ),
    ),
)
def test_coordinator_and_state_ops_share_recovery_proof_matrix(
    action: str,
    stored_result: dict[str, object],
    cleanup: bool,
) -> None:
    proof = _recovery_proof(cleanup=cleanup)
    receipt = _resolution_receipt(action, proof)
    _validate_lifecycle_terminal_proof(action, proof, 10.0, stored_result)
    assert WorkspaceCoordinator._operation_terminal_proof(receipt, stored_result) == proof


def test_recovery_proof_matrix_rejects_missing_cleanup_and_wrong_queue_state() -> None:
    new_action = "queue.cancel"
    missing_cleanup = _recovery_proof(cleanup=False)
    stored_cancel = {"id": "claim", "task_id": "task", "state": "cancelled"}
    with pytest.raises(ValueError):
        _validate_lifecycle_terminal_proof(new_action, missing_cleanup, 10.0, stored_cancel)
    with pytest.raises(StateError):
        WorkspaceCoordinator._operation_terminal_proof(
            _resolution_receipt(new_action, missing_cleanup), stored_cancel
        )

    wrong_queue_state = {"id": "claim", "task_id": "task", "state": "released"}
    valid_cleanup = _recovery_proof(cleanup=True)
    with pytest.raises(ValueError):
        _validate_lifecycle_terminal_proof(new_action, valid_cleanup, 10.0, wrong_queue_state)
    with pytest.raises(StateError):
        WorkspaceCoordinator._operation_terminal_proof(
            _resolution_receipt(new_action, valid_cleanup), wrong_queue_state
        )


def test_wait_budget_does_not_extend_when_wall_clock_moves_backward(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setattr(coordinator_module.time, "time", lambda: 99.0)

    assert _remaining_operation_wait(100.0, 30.0, 29.0, "claim") == 0.0


@pytest.fixture
def registered(tmp_path: Path) -> tuple[WorkspaceCoordinator, Path]:
    workspace = tmp_path / "workspace"
    workspace.mkdir()
    coordinator = WorkspaceCoordinator(resolve_state_paths(tmp_path / "state"))
    coordinator.register(workspace, operation_id=operation_id())
    return coordinator, workspace


def start(
    coordinator: WorkspaceCoordinator,
    workspace: Path,
    owner: str,
) -> tuple[dict[str, object], str]:
    return coordinator.start_task(
        workspace,
        owner,
        f"{owner} work",
        operation_id=operation_id(),
    )


@pytest.mark.skipif(state_module.os.name != "nt", reason="Windows short-path behavior only")
def test_direct_task_lifecycle_canonicalizes_short_temp_alias(
    registered: tuple[WorkspaceCoordinator, Path],
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    coordinator, workspace = registered
    canonical_temp = tmp_path / "runneradmin" / "AppData" / "Local" / "Temp"
    canonical_temp.mkdir(parents=True)
    alias_temp = tmp_path / "RUNNER~1" / "AppData" / "Local" / "Temp"
    alias_path = alias_temp / "router" / "owner.token"
    canonical_path = canonical_temp / "router" / "owner.token"
    original_resolve = Path.resolve

    def resolve_alias(path: Path, strict: bool = False) -> Path:
        if path == alias_temp:
            return canonical_temp
        if path == alias_path:
            return canonical_path
        return original_resolve(path, strict=strict)

    monkeypatch.setattr(state_module.tempfile, "gettempdir", lambda: str(alias_temp))
    monkeypatch.setattr(Path, "resolve", resolve_alias)

    task, token = coordinator.start_task(
        workspace,
        "alias-owner",
        "short path identity",
        operation_id=operation_id(),
        token_file_path=str(alias_path),
        token="alias-owner-secret",
    )
    with open_database(coordinator.paths) as connection:
        stored = connection.execute(
            "SELECT token_file_path, token_file_identity FROM tasks WHERE id = ?",
            (task["id"],),
        ).fetchone()
    assert stored is not None
    assert stored["token_file_path"] == str(canonical_path)
    assert stored["token_file_identity"] == str(canonical_path).casefold()

    release = coordinator.release_task(
        workspace,
        token,
        operation_id=operation_id(),
        result="completed",
        token_cleanup_path=str(alias_path),
    )
    assert release["operation"]["replayed"] is False


def resolved_pending_claim(
    coordinator: WorkspaceCoordinator,
    workspace: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> dict[str, object]:
    blocker, blocker_token = start(coordinator, workspace, "resolution-blocker")
    waiter, waiter_token = start(coordinator, workspace, "resolution-waiter")
    blocker_claim = coordinator.acquire_claim(
        workspace,
        blocker_token,
        operation_id=operation_id(),
        resources=("unity-live",),
    )
    mutation_id = operation_id()

    class SimulatedCrash(RuntimeError):
        pass

    with monkeypatch.context() as scoped:
        scoped.setattr(
            coordinator_module.time,
            "sleep",
            lambda _seconds: (_ for _ in ()).throw(SimulatedCrash()),
        )
        with pytest.raises(SimulatedCrash):
            coordinator.acquire_claim(
                workspace,
                waiter_token,
                operation_id=mutation_id,
                resources=("unity-live",),
                wait_seconds=30.0,
                requested_wait_seconds=30.0,
                keep_queued=True,
            )
    coordinator.release_claim(
        workspace,
        blocker_token,
        str(blocker_claim["id"]),
        operation_id=operation_id(),
    )
    with open_database(coordinator.paths) as connection:
        connection.execute("UPDATE tasks SET expires_at = 0 WHERE id = ?", (waiter["id"],))
        connection.commit()
    coordinator.status(workspace)
    coordinator.resolve_unknown(
        workspace,
        str(waiter["id"]),
        operation_id=operation_id(),
        resolution="completed",
        evidence="resolved pending claim retention proof",
    )
    replay = coordinator.acquire_claim(
        workspace,
        waiter_token,
        operation_id=mutation_id,
        receipt_only=True,
        resources=("unity-live",),
        wait_seconds=0.0,
        requested_wait_seconds=30.0,
        keep_queued=True,
    )
    assert replay["resolution_reason"] == "task-recovery-resolved"
    return {
        "blocker": blocker,
        "blocker_token": blocker_token,
        "waiter": waiter,
        "waiter_token": waiter_token,
        "operation_id": mutation_id,
        "replay": replay,
    }


def test_same_operation_replays_without_second_mutation_and_conflict_is_rejected(
    registered: tuple[WorkspaceCoordinator, Path],
) -> None:
    coordinator, workspace = registered
    _, token = start(coordinator, workspace, "owner")
    mutation_id = operation_id()

    first = coordinator.acquire_claim(
        workspace,
        token,
        operation_id=mutation_id,
        resources=("unity-live",),
    )
    replay = coordinator.acquire_claim(
        workspace,
        token,
        operation_id=mutation_id,
        resources=("unity-live",),
        receipt_only=True,
    )

    assert replay["id"] == first["id"]
    assert replay["queue_order"] == first["queue_order"]
    assert first["operation"]["replayed"] is False
    assert replay["operation"]["replayed"] is True
    with pytest.raises(UsageError) as conflict:
        coordinator.acquire_claim(
            workspace,
            token,
            operation_id=mutation_id,
            resources=("different",),
            receipt_only=True,
        )
    assert conflict.value.details["reason"] == "operation-id-conflict"
    _, other_token = start(coordinator, workspace, "other-owner")
    with pytest.raises(UsageError) as owner_conflict:
        coordinator.acquire_claim(
            workspace,
            other_token,
            operation_id=mutation_id,
            resources=("unity-live",),
            receipt_only=True,
        )
    assert owner_conflict.value.details["reason"] == "operation-id-conflict"
    with pytest.raises(UsageError) as action_conflict:
        coordinator.heartbeat(
            workspace,
            token,
            operation_id=mutation_id,
            receipt_only=True,
        )
    assert action_conflict.value.details["reason"] == "operation-id-conflict"
    other_workspace = workspace.parent / "other-workspace"
    other_workspace.mkdir()
    coordinator.register(other_workspace, operation_id=operation_id())
    with pytest.raises(UsageError) as workspace_conflict:
        coordinator.acquire_claim(
            other_workspace,
            token,
            operation_id=mutation_id,
            resources=("unity-live",),
            receipt_only=True,
        )
    assert workspace_conflict.value.details["reason"] == "operation-id-conflict"
    with open_database(coordinator.paths) as connection:
        claim_count = connection.execute(
            "SELECT COUNT(*) FROM claims WHERE task_id = ?",
            (first["task_id"],),
        ).fetchone()[0]
    assert claim_count == 1


def test_open_task_token_hash_is_globally_unique_across_workspaces(tmp_path: Path) -> None:
    first_workspace = tmp_path / "first-workspace"
    second_workspace = tmp_path / "second-workspace"
    first_workspace.mkdir()
    second_workspace.mkdir()
    coordinator = WorkspaceCoordinator(resolve_state_paths(tmp_path / "state"))
    coordinator.register(first_workspace, operation_id=operation_id())
    coordinator.register(second_workspace, operation_id=operation_id())
    coordinator.start_task(
        first_workspace,
        "first",
        "first task",
        operation_id=operation_id(),
        token="shared-secret",
    )

    with pytest.raises(UsageError) as duplicate:
        coordinator.start_task(
            second_workspace,
            "second",
            "second task",
            operation_id=operation_id(),
            token="shared-secret",
        )
    assert duplicate.value.details["reason"] == "open-task-token-conflict"


def test_delivered_active_start_receipt_is_protected_until_claimless_cleanup(
    registered: tuple[WorkspaceCoordinator, Path],
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    coordinator, workspace = registered
    start_id = operation_id()
    token_path = os.path.normpath(str((workspace / "protected-start.token").resolve()))
    task, _ = coordinator.start_task(
        workspace,
        "owner",
        "protected active start",
        operation_id=start_id,
        token_file_path=token_path,
        token="protected-secret",
    )
    fingerprint = str(task["operation"]["fingerprint"])
    coordinator.acknowledge_receipt(
        start_id,
        fingerprint,
        str(task["operation"]["delivery_digest"]),
    )
    monkeypatch.setattr(coordinator_module, "DELIVERED_OPERATION_RETENTION", 0)
    with coordinator._transaction() as connection:
        coordinator._prune_delivered_operations(connection)
    with open_database(coordinator.paths) as connection:
        assert (
            connection.execute(
                "SELECT COUNT(*) FROM operation_receipts WHERE operation_id = ?",
                (start_id,),
            ).fetchone()[0]
            == 1
        )
        connection.execute("UPDATE tasks SET expires_at = 0 WHERE id = ?", (task["id"],))
        connection.commit()

    monkeypatch.setattr(coordinator_module, "DELIVERED_OPERATION_RETENTION", 10_000)
    coordinator.status(workspace)
    drained = coordinator.drain_token_cleanup_jobs()
    assert drained["completed"] == 1
    with open_database(coordinator.paths) as connection:
        receipt = connection.execute(
            "SELECT terminal_json, retired_at FROM operation_receipts WHERE operation_id = ?",
            (start_id,),
        ).fetchone()
        assert connection.execute("SELECT COUNT(*) FROM token_cleanup_jobs").fetchone()[0] == 0
    assert receipt is not None
    assert receipt["terminal_json"] is not None
    assert receipt["retired_at"] is not None


def test_terminal_start_receipt_detaches_before_global_retention_delete(
    registered: tuple[WorkspaceCoordinator, Path],
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    coordinator, workspace = registered
    start_id = operation_id()
    task, _ = coordinator.start_task(
        workspace,
        "owner",
        "detachable terminal start",
        operation_id=start_id,
    )
    coordinator.acknowledge_receipt(
        start_id,
        str(task["operation"]["fingerprint"]),
        str(task["operation"]["delivery_digest"]),
    )
    with open_database(coordinator.paths) as connection:
        connection.execute("UPDATE tasks SET expires_at = 0 WHERE id = ?", (task["id"],))
        connection.commit()
    coordinator.status(workspace)

    monkeypatch.setattr(coordinator_module, "DELIVERED_OPERATION_RETENTION", 0)
    with coordinator._transaction() as connection:
        coordinator._prune_delivered_operations(connection)
    with open_database(coordinator.paths) as connection:
        stored_task = connection.execute(
            "SELECT start_operation_id FROM tasks WHERE id = ?", (task["id"],)
        ).fetchone()
        receipt_count = connection.execute(
            "SELECT COUNT(*) FROM operation_receipts WHERE operation_id = ?", (start_id,)
        ).fetchone()[0]
    assert stored_task is not None and stored_task["start_operation_id"] is None
    assert receipt_count == 0
    assert inspect_state(coordinator.paths.database)["schema_version"] == 3


def test_protected_start_does_not_consume_deletable_receipt_retention_window(
    registered: tuple[WorkspaceCoordinator, Path],
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    coordinator, workspace = registered
    task, token = start(coordinator, workspace, "owner")
    coordinator.acknowledge_receipt(
        str(task["operation"]["operation_id"]),
        str(task["operation"]["fingerprint"]),
        str(task["operation"]["delivery_digest"]),
    )
    heartbeats = [
        coordinator.heartbeat(
            workspace,
            token,
            operation_id=operation_id(),
            note=f"retention-{index}",
        )
        for index in range(2)
    ]
    for heartbeat in heartbeats:
        coordinator.acknowledge_receipt(
            str(heartbeat["operation"]["operation_id"]),
            str(heartbeat["operation"]["fingerprint"]),
            str(heartbeat["operation"]["delivery_digest"]),
        )
    monkeypatch.setattr(coordinator_module, "DELIVERED_OPERATION_RETENTION", 1)
    with coordinator._transaction() as connection:
        coordinator._prune_delivered_operations(connection)
        first_count = connection.execute(
            "SELECT COUNT(*) FROM operation_receipts WHERE action = 'task.heartbeat'"
        ).fetchone()[0]
        coordinator._prune_delivered_operations(connection)
        second_count = connection.execute(
            "SELECT COUNT(*) FROM operation_receipts WHERE action = 'task.heartbeat'"
        ).fetchone()[0]
        protected_count = connection.execute(
            "SELECT COUNT(*) FROM operation_receipts WHERE operation_id = ?",
            (task["operation"]["operation_id"],),
        ).fetchone()[0]
    assert (first_count, second_count, protected_count) == (1, 1, 1)


def test_pending_task_start_cleanup_ack_does_not_delete_or_deliver(
    registered: tuple[WorkspaceCoordinator, Path],
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    coordinator, workspace = registered
    token_path = os.path.normpath(str((workspace / "pending-start.token").resolve()))
    task, _ = coordinator.start_task(
        workspace,
        "owner",
        "pending start cleanup",
        operation_id=operation_id(),
        token_file_path=token_path,
        token="pending-start-secret",
    )
    with open_database(coordinator.paths) as connection:
        connection.execute("UPDATE tasks SET expires_at = 0 WHERE id = ?", (task["id"],))
        connection.commit()
    coordinator.status(workspace)
    cleanup_called = False

    def unexpected_cleanup(_path: Path, _expected_hash: str) -> bool:
        nonlocal cleanup_called
        cleanup_called = True
        return True

    monkeypatch.setattr(
        coordinator_module,
        "remove_matching_token_hash_file",
        unexpected_cleanup,
    )
    with pytest.raises(BusyError) as pending:
        coordinator.acknowledge_receipt(
            str(task["operation"]["operation_id"]),
            str(task["operation"]["fingerprint"]),
            str(task["operation"]["delivery_digest"]),
        )
    assert pending.value.details["reason"] == "operation-recovery-pending"
    assert cleanup_called is False
    with open_database(coordinator.paths) as connection:
        job = connection.execute(
            "SELECT completed_at FROM token_cleanup_jobs WHERE task_id = ?", (task["id"],)
        ).fetchone()
        delivered_at = connection.execute(
            "SELECT delivered_at FROM operation_receipts WHERE operation_id = ?",
            (task["operation"]["operation_id"],),
        ).fetchone()[0]
    assert job is not None and job["completed_at"] is None
    assert delivered_at is None


def test_delivered_start_ack_retry_does_not_run_later_cleanup_job(
    registered: tuple[WorkspaceCoordinator, Path],
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    coordinator, workspace = registered
    token_path = os.path.normpath(str((workspace / "delivered-start.token").resolve()))
    task, _ = coordinator.start_task(
        workspace,
        "owner",
        "delivered start retry",
        operation_id=operation_id(),
        token_file_path=token_path,
        token="delivered-start-secret",
    )
    operation = task["operation"]
    first_ack = coordinator.acknowledge_receipt(
        str(operation["operation_id"]),
        str(operation["fingerprint"]),
        str(operation["delivery_digest"]),
    )
    assert first_ack["operation"]["replayed"] is False
    with open_database(coordinator.paths) as connection:
        connection.execute("UPDATE tasks SET expires_at = 0 WHERE id = ?", (task["id"],))
        connection.commit()
    coordinator.status(workspace)
    cleanup_called = False

    def unexpected_cleanup(_path: Path, _expected_hash: str) -> bool:
        nonlocal cleanup_called
        cleanup_called = True
        return True

    monkeypatch.setattr(
        coordinator_module,
        "remove_matching_token_hash_file",
        unexpected_cleanup,
    )
    replayed_ack = coordinator.acknowledge_receipt(
        str(operation["operation_id"]),
        str(operation["fingerprint"]),
        str(operation["delivery_digest"]),
    )
    assert replayed_ack["operation"]["replayed"] is True
    assert cleanup_called is False
    with open_database(coordinator.paths) as connection:
        job = connection.execute(
            "SELECT completed_at FROM token_cleanup_jobs WHERE task_id = ?", (task["id"],)
        ).fetchone()
    assert job is not None and job["completed_at"] is None


def test_cleanup_batch_runs_global_receipt_prune_once(
    registered: tuple[WorkspaceCoordinator, Path],
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    coordinator, workspace = registered
    task_ids: list[str] = []
    for index in range(8):
        token_path = os.path.normpath(str((workspace / f"cleanup-batch-{index}.token").resolve()))
        task, _ = coordinator.start_task(
            workspace,
            f"owner-{index}",
            f"cleanup batch {index}",
            operation_id=operation_id(),
            token_file_path=token_path,
            token=f"cleanup-batch-secret-{index}",
        )
        task_ids.append(str(task["id"]))
    with open_database(coordinator.paths) as connection:
        connection.executemany(
            "UPDATE tasks SET expires_at = 0 WHERE id = ?",
            [(task_id,) for task_id in task_ids],
        )
        connection.commit()
    coordinator.status(workspace)
    monkeypatch.setattr(
        coordinator_module,
        "remove_matching_token_hash_file",
        lambda _path, _expected_hash: True,
    )
    original_prune = WorkspaceCoordinator._prune_delivered_operations
    prune_calls = 0

    def counted_prune(connection: sqlite3.Connection) -> None:
        nonlocal prune_calls
        prune_calls += 1
        original_prune(connection)

    monkeypatch.setattr(
        WorkspaceCoordinator,
        "_prune_delivered_operations",
        staticmethod(counted_prune),
    )
    drained = coordinator.drain_token_cleanup_jobs(limit=8)
    assert drained["processed"] == 8
    assert drained["completed"] == 8
    assert prune_calls == 1


def test_ack_without_external_cleanup_prunes_in_delivery_transaction(
    registered: tuple[WorkspaceCoordinator, Path],
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    coordinator, workspace = registered
    _task, token = start(coordinator, workspace, "owner")
    heartbeat = coordinator.heartbeat(
        workspace,
        token,
        operation_id=operation_id(),
        note="atomic acknowledgement",
    )

    class SimulatedCrash(RuntimeError):
        pass

    monkeypatch.setattr(
        WorkspaceCoordinator,
        "_prune_delivered_operations",
        staticmethod(lambda _connection: (_ for _ in ()).throw(SimulatedCrash())),
    )
    with pytest.raises(SimulatedCrash):
        coordinator.acknowledge_receipt(
            str(heartbeat["operation"]["operation_id"]),
            str(heartbeat["operation"]["fingerprint"]),
            str(heartbeat["operation"]["delivery_digest"]),
        )
    with open_database(coordinator.paths) as connection:
        delivered_at = connection.execute(
            "SELECT delivered_at FROM operation_receipts WHERE operation_id = ?",
            (heartbeat["operation"]["operation_id"],),
        ).fetchone()[0]
    assert delivered_at is None


def test_terminal_start_receipt_is_not_pruned_before_release_token_cleanup_ack(
    registered: tuple[WorkspaceCoordinator, Path],
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    coordinator, workspace = registered
    token_path = os.path.normpath(str((workspace / "release-protected.token").resolve()))
    start_id = operation_id()
    task, token = coordinator.start_task(
        workspace,
        "owner",
        "release protected start",
        operation_id=start_id,
        token_file_path=token_path,
        token="release-protected-secret",
    )
    release_id = operation_id()
    coordinator.release_task(
        workspace,
        token,
        operation_id=release_id,
        result="completed",
        token_cleanup_path=token_path,
    )

    monkeypatch.setattr(coordinator_module, "DELIVERED_OPERATION_RETENTION", 0)
    with coordinator._transaction() as connection:
        coordinator._prune_delivered_operations(connection)

    with open_database(coordinator.paths) as connection:
        start_receipt = connection.execute(
            "SELECT terminal_json, retired_at FROM operation_receipts WHERE operation_id = ?",
            (start_id,),
        ).fetchone()
        cleanup_receipt = connection.execute(
            "SELECT token_cleanup_path FROM operation_receipts WHERE operation_id = ?",
            (release_id,),
        ).fetchone()
    assert start_receipt is not None
    assert start_receipt["terminal_json"] is not None
    assert start_receipt["retired_at"] is not None
    assert cleanup_receipt is not None
    assert cleanup_receipt["token_cleanup_path"] == token_path
    assert task["id"]


def test_terminal_task_is_not_pruned_before_release_token_cleanup_ack(
    registered: tuple[WorkspaceCoordinator, Path],
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    coordinator, workspace = registered
    token_path = os.path.normpath(str((workspace / "task-prune-protected.token").resolve()))
    task, token = coordinator.start_task(
        workspace,
        "owner",
        "task prune protected",
        operation_id=operation_id(),
        token_file_path=token_path,
        token="task-prune-protected-secret",
    )
    coordinator.release_task(
        workspace,
        token,
        operation_id=operation_id(),
        result="completed",
        token_cleanup_path=token_path,
    )

    monkeypatch.setattr(coordinator_module, "TERMINAL_TASK_RETENTION", 0)
    with coordinator._transaction() as connection:
        coordinator._prune_terminal_tasks(
            connection,
            coordinator_module._workspace_id(str(workspace.resolve())),
        )

    with open_database(coordinator.paths) as connection:
        retained = connection.execute(
            "SELECT state FROM tasks WHERE id = ?",
            (task["id"],),
        ).fetchone()
    assert retained is not None
    assert retained["state"] == "completed"


def test_unregister_rejects_unacknowledged_release_token_cleanup(
    registered: tuple[WorkspaceCoordinator, Path],
) -> None:
    coordinator, workspace = registered
    token_path = os.path.normpath(str((workspace / "unregister-protected.token").resolve()))
    task, token = coordinator.start_task(
        workspace,
        "owner",
        "unregister protected",
        operation_id=operation_id(),
        token_file_path=token_path,
        token="unregister-protected-secret",
    )
    coordinator.release_task(
        workspace,
        token,
        operation_id=operation_id(),
        result="completed",
        token_cleanup_path=token_path,
    )

    with pytest.raises(BusyError) as blocked:
        coordinator.unregister(workspace, operation_id=operation_id())

    assert blocked.value.details == {
        "reason": "workspace-token-cleanup-pending",
        "token_cleanup_jobs": 0,
        "token_cleanup_receipts": 1,
    }
    with open_database(coordinator.paths) as connection:
        assert (
            connection.execute(
                "SELECT COUNT(*) FROM workspaces WHERE id = ?",
                (coordinator_module._workspace_id(str(workspace.resolve())),),
            ).fetchone()[0]
            == 1
        )
        assert (
            connection.execute(
                "SELECT COUNT(*) FROM operation_receipts WHERE task_id = ? "
                "AND token_cleanup_path IS NOT NULL",
                (task["id"],),
            ).fetchone()[0]
            == 1
        )


@pytest.mark.parametrize("conflict_kind", ["open-task", "cleanup-job", "cleanup-receipt"])
def test_pathless_task_release_rejects_every_existing_token_identity_obligation(
    registered: tuple[WorkspaceCoordinator, Path],
    conflict_kind: str,
) -> None:
    coordinator, workspace = registered
    current, current_token = start(coordinator, workspace, "current")
    token_path = os.path.normpath(str((workspace / f"{conflict_kind}.token").resolve()))
    other, other_token = coordinator.start_task(
        workspace,
        "other",
        f"{conflict_kind} owner",
        operation_id=operation_id(),
        token_file_path=token_path,
        token=f"{conflict_kind}-secret",
    )
    if conflict_kind == "cleanup-job":
        with open_database(coordinator.paths) as connection:
            connection.execute("UPDATE tasks SET expires_at = 0 WHERE id = ?", (other["id"],))
            connection.commit()
        coordinator.status(workspace)
    elif conflict_kind == "cleanup-receipt":
        coordinator.release_task(
            workspace,
            other_token,
            operation_id=operation_id(),
            result="completed",
            token_cleanup_path=token_path,
        )

    release_id = operation_id()
    with pytest.raises(BusyError) as blocked:
        coordinator.release_task(
            workspace,
            current_token,
            operation_id=release_id,
            result="completed",
            token_cleanup_path=token_path,
        )

    assert blocked.value.details["reason"] in {
        "task-token-cleanup-pending",
        "task-token-path-in-use",
    }
    with open_database(coordinator.paths) as connection:
        current_state = connection.execute(
            "SELECT state, token_file_path FROM tasks WHERE id = ?",
            (current["id"],),
        ).fetchone()
        receipt_count = connection.execute(
            "SELECT COUNT(*) FROM operation_receipts WHERE operation_id = ?",
            (release_id,),
        ).fetchone()[0]
    assert current_state is not None
    assert tuple(current_state) == ("active", None)
    assert receipt_count == 0


def test_release_records_cleanup_before_future_terminal_retention_prune(
    registered: tuple[WorkspaceCoordinator, Path],
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    coordinator, workspace = registered
    token_path = os.path.normpath(str((workspace / "retention-order.token").resolve()))
    task, token = coordinator.start_task(
        workspace,
        "owner",
        "retention order",
        operation_id=operation_id(),
        token_file_path=token_path,
        token="retention-order-secret",
    )
    workspace_id = coordinator_module._workspace_id(str(workspace.resolve()))
    future = time.time() + 1_000_000.0
    with open_database(coordinator.paths) as connection:
        connection.execute(
            "INSERT INTO tasks(id, workspace_id, owner, summary, token_hash, state, "
            "created_at, heartbeat_at, expires_at, finished_at, result) "
            "VALUES('future-terminal', ?, 'legacy', 'future terminal', ?, 'completed', "
            "?, ?, ?, ?, 'completed')",
            (workspace_id, "f" * 64, future - 10, future - 10, future - 5, future),
        )
        connection.commit()
    monkeypatch.setattr(coordinator_module, "TERMINAL_TASK_RETENTION", 1)

    released = coordinator.release_task(
        workspace,
        token,
        operation_id=operation_id(),
        result="completed",
        token_cleanup_path=token_path,
    )

    assert released["id"] == task["id"]
    with open_database(coordinator.paths) as connection:
        assert (
            connection.execute(
                "SELECT COUNT(*) FROM tasks WHERE id = ?",
                (task["id"],),
            ).fetchone()[0]
            == 1
        )
        assert (
            connection.execute(
                "SELECT COUNT(*) FROM operation_receipts WHERE task_id = ? "
                "AND action = 'task.release' AND token_cleanup_path IS NOT NULL",
                (task["id"],),
            ).fetchone()[0]
            == 1
        )


def test_global_capacity_reservations_leave_terminal_drain_lane_available(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    workspace = tmp_path / "capacity-workspace"
    workspace.mkdir()
    coordinator = WorkspaceCoordinator(resolve_state_paths(tmp_path / "capacity-state"))
    registered = coordinator.register(workspace, operation_id=operation_id())
    coordinator.acknowledge_receipt(
        str(registered["operation"]["operation_id"]),
        str(registered["operation"]["fingerprint"]),
        str(registered["operation"]["delivery_digest"]),
    )
    monkeypatch.setattr(coordinator_module, "REPLAY_REQUIRED_OPERATION_LIMIT", 6)
    task, token = coordinator.start_task(
        workspace,
        "owner",
        "capacity owner",
        operation_id=operation_id(),
    )
    coordinator.acknowledge_receipt(
        str(task["operation"]["operation_id"]),
        str(task["operation"]["fingerprint"]),
        str(task["operation"]["delivery_digest"]),
    )
    for index in range(2):
        coordinator.heartbeat(
            workspace,
            token,
            operation_id=operation_id(),
            note=f"capacity-{index}",
        )
    with pytest.raises(BusyError) as full:
        coordinator.heartbeat(
            workspace,
            token,
            operation_id=operation_id(),
            note="capacity-full",
        )
    assert full.value.details["reason"] == "operation-receipt-backlog-full"
    assert full.value.details["reserved_capacity"] == 6

    unknown = coordinator.release_task(
        workspace,
        token,
        operation_id=operation_id(),
        result="outcome-unknown",
    )
    assert unknown["state"] == "outcome_unknown"
    recovered = coordinator.resolve_unknown(
        workspace,
        str(task["id"]),
        operation_id=operation_id(),
        resolution="completed",
        evidence="verified executor never dispatched",
    )
    assert recovered["state"] == "completed"


def test_cleanup_job_rotation_is_fair_across_wall_clock_rollback(
    registered: tuple[WorkspaceCoordinator, Path],
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    coordinator, workspace = registered
    task_ids: list[str] = []
    for index in range(9):
        task, _ = coordinator.start_task(
            workspace,
            f"owner-{index}",
            f"cleanup job {index}",
            operation_id=operation_id(),
            token=f"secret-{index}",
            token_file_path=os.path.normpath(str((workspace / f"cleanup-{index}.token").resolve())),
        )
        task_ids.append(str(task["id"]))
    with open_database(coordinator.paths) as connection:
        connection.executemany(
            "UPDATE tasks SET expires_at = 0 WHERE id = ?",
            ((task_id,) for task_id in task_ids),
        )
        connection.commit()
    coordinator.status(workspace)

    def fail_cleanup(_path: Path, _expected_hash: str) -> bool:
        raise OSError("injected persistent cleanup failure")

    monkeypatch.setattr(coordinator_module, "remove_matching_token_hash_file", fail_cleanup)
    monkeypatch.setattr(coordinator_module.time, "time", lambda: 9_000_000_000.0)
    first = coordinator.drain_token_cleanup_jobs(limit=8)
    assert first == {"processed": 8, "completed": 0, "retained": 0, "failed": 8}
    with open_database(coordinator.paths) as connection:
        first_counts = {
            row["task_id"]: row["attempt_count"]
            for row in connection.execute(
                "SELECT task_id, attempt_count FROM token_cleanup_jobs"
            ).fetchall()
        }
    unattempted = [task_id for task_id, count in first_counts.items() if count == 0]
    assert len(unattempted) == 1

    monkeypatch.setattr(coordinator_module.time, "time", lambda: 1.0)
    second = coordinator.drain_token_cleanup_jobs(limit=8)
    assert second == {"processed": 8, "completed": 0, "retained": 0, "failed": 8}
    with open_database(coordinator.paths) as connection:
        second_counts = sorted(
            row["attempt_count"]
            for row in connection.execute("SELECT attempt_count FROM token_cleanup_jobs").fetchall()
        )
    assert first_counts[unattempted[0]] == 0
    assert second_counts == [1, 1, 2, 2, 2, 2, 2, 2, 2]


def test_workspace_cleanup_drain_is_exact_and_bounded(
    registered: tuple[WorkspaceCoordinator, Path],
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    coordinator, workspace = registered
    other_workspace = workspace.parent / "other-workspace"
    other_workspace.mkdir()
    coordinator.register(other_workspace, operation_id=operation_id())
    task_ids: list[str] = []
    for target, label in ((workspace, "selected"), (other_workspace, "other")):
        task, _ = coordinator.start_task(
            target,
            label,
            f"{label} cleanup",
            operation_id=operation_id(),
            token=f"{label}-secret",
            token_file_path=os.path.normpath(str((target / f"{label}.token").resolve())),
        )
        task_ids.append(str(task["id"]))
        with open_database(coordinator.paths) as connection:
            connection.execute("UPDATE tasks SET expires_at = 0 WHERE id = ?", (task["id"],))
            connection.commit()
        coordinator.status(target)

    monkeypatch.setattr(
        coordinator_module,
        "remove_matching_token_hash_file",
        lambda *_args: False,
    )
    drained = coordinator.drain_token_cleanup_jobs(limit=8, workspace=workspace)
    assert drained == {"processed": 1, "completed": 0, "retained": 0, "failed": 1}
    with open_database(coordinator.paths) as connection:
        attempts = {
            row["task_id"]: row["attempt_count"]
            for row in connection.execute(
                "SELECT task_id, attempt_count FROM token_cleanup_jobs"
            ).fetchall()
        }
    assert attempts[task_ids[0]] == 1
    assert attempts[task_ids[1]] == 0


def test_every_nonwaiting_public_mutation_replays_its_durable_result(tmp_path: Path) -> None:
    workspace = tmp_path / "workspace"
    workspace.mkdir()
    coordinator = WorkspaceCoordinator(resolve_state_paths(tmp_path / "state"))

    register_id = operation_id()
    registered = coordinator.register(workspace, operation_id=register_id)
    assert (
        coordinator.register(workspace, operation_id=register_id, receipt_only=True)["operation"][
            "replayed"
        ]
        is True
    )

    start_id = operation_id()
    owner, owner_token = coordinator.start_task(
        workspace,
        "owner",
        "owner work",
        operation_id=start_id,
        token="owner-secret",
    )
    replayed_owner, replayed_token = coordinator.start_task(
        workspace,
        "owner",
        "owner work",
        operation_id=start_id,
        token="owner-secret",
        receipt_only=True,
    )
    assert replayed_owner["id"] == owner["id"]
    assert replayed_owner["operation"]["replayed"] is True
    assert replayed_token == owner_token

    heartbeat_id = operation_id()
    heartbeat = coordinator.heartbeat(
        workspace,
        owner_token,
        operation_id=heartbeat_id,
        ttl_seconds=120.0,
        note="still working",
    )
    heartbeat_replay = coordinator.heartbeat(
        workspace,
        owner_token,
        operation_id=heartbeat_id,
        ttl_seconds=120.0,
        note="still working",
        receipt_only=True,
    )
    assert heartbeat_replay["heartbeat_at"] == heartbeat["heartbeat_at"]
    assert heartbeat_replay["operation"]["replayed"] is True

    blocker = coordinator.acquire_claim(
        workspace,
        owner_token,
        operation_id=operation_id(),
        resources=("exclusive",),
    )
    queued = coordinator.acquire_claim(
        workspace,
        owner_token,
        operation_id=operation_id(),
        resources=("exclusive",),
        keep_queued=True,
    )
    assert queued["state"] == "queued"
    cancel_id = operation_id()
    cancelled = coordinator.cancel_claim(
        workspace,
        owner_token,
        str(queued["id"]),
        operation_id=cancel_id,
    )
    cancel_replay = coordinator.cancel_claim(
        workspace,
        owner_token,
        str(queued["id"]),
        operation_id=cancel_id,
        receipt_only=True,
    )
    assert cancel_replay["id"] == cancelled["id"]
    assert cancel_replay["operation"]["replayed"] is True

    freeze_task, freeze_token = coordinator.start_task(
        workspace,
        "freeze",
        "freeze work",
        operation_id=operation_id(),
    )
    freeze_id = operation_id()
    freeze = coordinator.acquire_claim(
        workspace,
        freeze_token,
        operation_id=freeze_id,
        freeze=True,
        keep_queued=True,
    )
    freeze_replay = coordinator.acquire_claim(
        workspace,
        freeze_token,
        operation_id=freeze_id,
        freeze=True,
        keep_queued=True,
        receipt_only=True,
    )
    assert freeze["state"] == "queued"
    assert freeze_replay["id"] == freeze["id"]
    assert freeze_replay["operation"]["replayed"] is True

    freeze_release_id = operation_id()
    released_freeze = coordinator.release_claim(
        workspace,
        freeze_token,
        str(freeze["id"]),
        operation_id=freeze_release_id,
    )
    freeze_release_replay = coordinator.release_claim(
        workspace,
        freeze_token,
        str(freeze["id"]),
        operation_id=freeze_release_id,
        receipt_only=True,
    )
    assert freeze_release_replay["state"] == released_freeze["state"]
    assert freeze_release_replay["operation"]["replayed"] is True

    claim_release_id = operation_id()
    released_claim = coordinator.release_claim(
        workspace,
        owner_token,
        str(blocker["id"]),
        operation_id=claim_release_id,
    )
    claim_release_replay = coordinator.release_claim(
        workspace,
        owner_token,
        str(blocker["id"]),
        operation_id=claim_release_id,
        receipt_only=True,
    )
    assert claim_release_replay["state"] == released_claim["state"]
    assert claim_release_replay["operation"]["replayed"] is True

    unknown_task, unknown_token = coordinator.start_task(
        workspace,
        "recovery",
        "recovery work",
        operation_id=operation_id(),
    )
    coordinator.acquire_claim(
        workspace,
        unknown_token,
        operation_id=operation_id(),
        writes=("Assets/Unknown.asset",),
    )
    unknown_release_id = operation_id()
    unknown = coordinator.release_task(
        workspace,
        unknown_token,
        operation_id=unknown_release_id,
        result="outcome-unknown",
    )
    unknown_replay = coordinator.release_task(
        workspace,
        unknown_token,
        operation_id=unknown_release_id,
        result="outcome-unknown",
        receipt_only=True,
    )
    assert unknown_replay["id"] == unknown["id"]
    assert unknown_replay["operation"]["replayed"] is True

    recovery_id = operation_id()
    recovered = coordinator.resolve_unknown(
        workspace,
        str(unknown_task["id"]),
        operation_id=recovery_id,
        resolution="completed",
        evidence="verified executor never dispatched",
    )
    recovery_replay = coordinator.resolve_unknown(
        workspace,
        str(unknown_task["id"]),
        operation_id=recovery_id,
        resolution="completed",
        evidence="verified executor never dispatched",
        receipt_only=True,
    )
    assert recovery_replay["id"] == recovered["id"]
    assert recovery_replay["operation"]["replayed"] is True

    released_operations: list[dict[str, object]] = []
    for task, token in ((owner, owner_token), (freeze_task, freeze_token)):
        release_id = operation_id()
        cleanup_path = os.path.normpath(str((workspace / f"{task['id']}.token").resolve()))
        released = coordinator.release_task(
            workspace,
            token,
            operation_id=release_id,
            result="completed",
            token_cleanup_path=cleanup_path,
        )
        replay = coordinator.release_task(
            workspace,
            token,
            operation_id=release_id,
            result="completed",
            token_cleanup_path=cleanup_path,
            receipt_only=True,
        )
        assert replay["id"] == released["id"]
        assert replay["operation"]["replayed"] is True
        released_operations.append(released["operation"])

    for operation in released_operations:
        acknowledged = coordinator.acknowledge_receipt(
            str(operation["operation_id"]),
            str(operation["fingerprint"]),
            str(operation["delivery_digest"]),
        )
        assert acknowledged["acknowledged"] is True
        assert acknowledged["token_file_removed"] is True
    with open_database(coordinator.paths) as connection:
        assert (
            connection.execute(
                "SELECT COUNT(*) FROM operation_receipts "
                "WHERE token_cleanup_path IS NOT NULL AND delivered_at IS NULL"
            ).fetchone()[0]
            == 0
        )
        assert connection.execute("SELECT COUNT(*) FROM token_cleanup_jobs").fetchone()[0] == 0

    unregister_id = operation_id()
    unregistered = coordinator.unregister(workspace, operation_id=unregister_id)
    unregister_replay = coordinator.unregister(
        workspace,
        operation_id=unregister_id,
        receipt_only=True,
    )
    assert unregister_replay["id"] == registered["id"] == unregistered["id"]
    assert unregister_replay["operation"]["replayed"] is True


def test_receipt_only_missing_does_not_run_maintenance(
    registered: tuple[WorkspaceCoordinator, Path],
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    coordinator, workspace = registered
    task, token = start(coordinator, workspace, "owner")
    with open_database(coordinator.paths) as connection:
        before = connection.execute(
            "SELECT epoch FROM workspaces WHERE root = ?", (str(workspace.resolve()),)
        ).fetchone()[0]

    def forbidden(*_args: object, **_kwargs: object) -> None:
        raise AssertionError("receipt-only miss must not maintain")

    monkeypatch.setattr(coordinator, "_maintain", forbidden)
    with pytest.raises(StateError) as missing:
        coordinator.heartbeat(
            workspace,
            token,
            operation_id=operation_id(),
            ttl_seconds=60,
            receipt_only=True,
        )
    assert missing.value.details["reason"] == "operation-receipt-missing"
    with open_database(coordinator.paths) as connection:
        after = connection.execute(
            "SELECT epoch FROM workspaces WHERE root = ?", (str(workspace.resolve()),)
        ).fetchone()[0]
        heartbeat = connection.execute(
            "SELECT heartbeat_at FROM tasks WHERE id = ?", (task["id"],)
        ).fetchone()[0]
    assert after == before
    assert heartbeat == task["heartbeat_at"]


def test_pending_claim_cannot_be_delivered_and_retry_finalizes_same_claim(
    registered: tuple[WorkspaceCoordinator, Path],
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    coordinator, workspace = registered
    _, blocker_token = start(coordinator, workspace, "blocker")
    _, waiter_token = start(coordinator, workspace, "waiter")
    blocker = coordinator.acquire_claim(
        workspace,
        blocker_token,
        operation_id=operation_id(),
        resources=("unity-live",),
    )
    mutation_id = operation_id()

    class SimulatedCrash(RuntimeError):
        pass

    monkeypatch.setattr(
        coordinator_module.time,
        "sleep",
        lambda _seconds: (_ for _ in ()).throw(SimulatedCrash()),
    )
    with pytest.raises(SimulatedCrash):
        coordinator.acquire_claim(
            workspace,
            waiter_token,
            operation_id=mutation_id,
            resources=("unity-live",),
            wait_seconds=9.9,
            requested_wait_seconds=9.9,
            keep_queued=True,
        )
    monkeypatch.undo()

    with pytest.raises(BusyError) as pending:
        coordinator.acquire_claim(
            workspace,
            waiter_token,
            operation_id=mutation_id,
            resources=("unity-live",),
            wait_seconds=9.8,
            requested_wait_seconds=9.9,
            keep_queued=True,
            receipt_only=True,
        )
    assert pending.value.details["reason"] == "operation-in-progress"
    with pytest.raises(BusyError) as early_ack:
        coordinator.acknowledge_receipt(
            mutation_id,
            str(pending.value.details["fingerprint"]),
            "0" * 64,
        )
    assert early_ack.value.details["reason"] == "operation-in-progress"

    finalized = coordinator.acquire_claim(
        workspace,
        waiter_token,
        operation_id=mutation_id,
        resources=("unity-live",),
        wait_seconds=0.0,
        requested_wait_seconds=9.9,
        keep_queued=True,
    )
    replay = coordinator.acquire_claim(
        workspace,
        waiter_token,
        operation_id=mutation_id,
        resources=("unity-live",),
        wait_seconds=9.8,
        requested_wait_seconds=9.9,
        keep_queued=True,
        receipt_only=True,
    )
    assert finalized["id"] == replay["id"]
    assert finalized["id"] != blocker["id"]
    assert finalized["state"] == "queued"
    assert finalized["timed_out"] is True
    assert replay["operation"]["fingerprint"] == finalized["operation"]["fingerprint"]
    assert replay["operation"]["finalized"] is True


@pytest.mark.parametrize("freeze", [False, True])
def test_task_expiry_finalizes_pending_claim_or_freeze_receipt(
    registered: tuple[WorkspaceCoordinator, Path],
    monkeypatch: pytest.MonkeyPatch,
    freeze: bool,
) -> None:
    coordinator, workspace = registered
    _, blocker_token = start(coordinator, workspace, "blocker")
    waiter, waiter_token = start(coordinator, workspace, "waiter")
    coordinator.acquire_claim(
        workspace,
        blocker_token,
        operation_id=operation_id(),
        resources=("unity-live",),
    )
    mutation_id = operation_id()
    acquire_arguments: dict[str, object] = (
        {"freeze": True} if freeze else {"resources": ("unity-live",)}
    )

    class SimulatedCrash(RuntimeError):
        pass

    original_sleep = coordinator_module.time.sleep
    monkeypatch.setattr(
        coordinator_module.time,
        "sleep",
        lambda _seconds: (_ for _ in ()).throw(SimulatedCrash()),
    )
    with pytest.raises(SimulatedCrash):
        coordinator.acquire_claim(
            workspace,
            waiter_token,
            operation_id=mutation_id,
            wait_seconds=30.0,
            requested_wait_seconds=30.0,
            keep_queued=True,
            **acquire_arguments,
        )
    monkeypatch.setattr(coordinator_module.time, "sleep", original_sleep)

    with open_database(coordinator.paths) as connection:
        connection.execute("UPDATE tasks SET expires_at = 0 WHERE id = ?", (waiter["id"],))
        connection.commit()
    coordinator.status(workspace)

    replay = coordinator.acquire_claim(
        workspace,
        waiter_token,
        operation_id=mutation_id,
        receipt_only=True,
        wait_seconds=0.0,
        requested_wait_seconds=30.0,
        keep_queued=True,
        **acquire_arguments,
    )
    assert replay["state"] == "cancelled"
    assert replay["granted"] is False
    assert replay["timed_out"] is False
    assert replay["aborted"] is True
    assert replay["reason"] == "task-ttl-expired"
    assert replay["operation"]["finalized"] is True
    acknowledged = coordinator.acknowledge_receipt(
        mutation_id,
        str(replay["operation"]["fingerprint"]),
        str(replay["operation"]["delivery_digest"]),
    )
    assert acknowledged["acknowledged"] is True
    with open_database(coordinator.paths) as connection:
        receipt = connection.execute(
            "SELECT finalized_at, delivered_at FROM operation_receipts WHERE operation_id = ?",
            (mutation_id,),
        ).fetchone()
        task_state = connection.execute(
            "SELECT state FROM tasks WHERE id = ?", (waiter["id"],)
        ).fetchone()[0]
    assert receipt["finalized_at"] is not None
    assert receipt["delivered_at"] is not None
    assert task_state == "expired"
    verified = inspect_state(coordinator.paths.database)
    assert verified["counts"]["pending_operation_receipts"] == 0


def test_task_expiry_aborts_a_pending_receipt_even_if_its_claim_became_active(
    registered: tuple[WorkspaceCoordinator, Path],
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    coordinator, workspace = registered
    _, blocker_token = start(coordinator, workspace, "blocker")
    waiter, waiter_token = start(coordinator, workspace, "waiter")
    blocker = coordinator.acquire_claim(
        workspace,
        blocker_token,
        operation_id=operation_id(),
        resources=("unity-live",),
    )
    mutation_id = operation_id()

    class SimulatedCrash(RuntimeError):
        pass

    original_sleep = coordinator_module.time.sleep
    monkeypatch.setattr(
        coordinator_module.time,
        "sleep",
        lambda _seconds: (_ for _ in ()).throw(SimulatedCrash()),
    )
    with pytest.raises(SimulatedCrash):
        coordinator.acquire_claim(
            workspace,
            waiter_token,
            operation_id=mutation_id,
            resources=("unity-live",),
            wait_seconds=30.0,
            requested_wait_seconds=30.0,
            keep_queued=True,
        )
    monkeypatch.setattr(coordinator_module.time, "sleep", original_sleep)
    coordinator.release_claim(
        workspace,
        blocker_token,
        str(blocker["id"]),
        operation_id=operation_id(),
    )
    with open_database(coordinator.paths) as connection:
        connection.execute("UPDATE tasks SET expires_at = 0 WHERE id = ?", (waiter["id"],))
        connection.commit()
    coordinator.status(workspace)

    replay = coordinator.acquire_claim(
        workspace,
        waiter_token,
        operation_id=mutation_id,
        receipt_only=True,
        resources=("unity-live",),
        wait_seconds=0.0,
        requested_wait_seconds=30.0,
        keep_queued=True,
    )
    assert replay["state"] == "active"
    assert replay["granted"] is False
    assert replay["aborted"] is True
    assert replay["reason"] == "task-ttl-expired-with-active-claim"
    status = coordinator.status(workspace)
    unknown = next(task for task in status["tasks"] if task["id"] == waiter["id"])
    assert unknown["state"] == "outcome_unknown"
    assert status["blocked"] is True
    with pytest.raises(BusyError) as unresolved_ack:
        coordinator.acknowledge_receipt(
            mutation_id,
            str(replay["operation"]["fingerprint"]),
            str(replay["operation"]["delivery_digest"]),
        )
    assert unresolved_ack.value.details["reason"] == "operation-recovery-pending"
    original_digest = replay["operation"]["delivery_digest"]
    coordinator.resolve_unknown(
        workspace,
        str(waiter["id"]),
        operation_id=operation_id(),
        resolution="completed",
        evidence="active pending claim recovered",
    )
    recovered = coordinator.acquire_claim(
        workspace,
        waiter_token,
        operation_id=mutation_id,
        receipt_only=True,
        resources=("unity-live",),
        wait_seconds=0.0,
        requested_wait_seconds=30.0,
        keep_queued=True,
    )
    assert recovered["state"] == "active"
    assert recovered["granted"] is False
    assert recovered["reason"] == "task-ttl-expired-with-active-claim"
    assert recovered["resolution_reason"] == "task-recovery-resolved"
    assert recovered["terminal_state"] == "completed"
    assert recovered["terminal_result"] == "recovered-completed"
    assert recovered["operation"]["delivery_digest"] != original_digest
    with pytest.raises(UsageError) as stale_delivery:
        coordinator.acknowledge_receipt(
            mutation_id,
            str(recovered["operation"]["fingerprint"]),
            str(original_digest),
        )
    assert stale_delivery.value.details["reason"] == "operation-delivery-digest-mismatch"
    acknowledged = coordinator.acknowledge_receipt(
        mutation_id,
        str(recovered["operation"]["fingerprint"]),
        str(recovered["operation"]["delivery_digest"]),
    )
    assert acknowledged["acknowledged"] is True
    retry = coordinator.acknowledge_receipt(
        mutation_id,
        str(recovered["operation"]["fingerprint"]),
        str(original_digest),
    )
    assert retry["operation"]["replayed"] is True
    assert retry["operation"]["delivery_digest"] == original_digest


def test_concurrent_same_operation_creates_one_claim(
    registered: tuple[WorkspaceCoordinator, Path],
) -> None:
    coordinator, workspace = registered
    task, token = start(coordinator, workspace, "owner")
    mutation_id = operation_id()
    barrier = threading.Barrier(2)

    def acquire() -> dict[str, object]:
        barrier.wait()
        return coordinator.acquire_claim(
            workspace,
            token,
            operation_id=mutation_id,
            resources=("exclusive",),
        )

    with ThreadPoolExecutor(max_workers=2) as pool:
        results = list(pool.map(lambda _index: acquire(), range(2)))

    assert results[0]["id"] == results[1]["id"]
    assert sorted(result["operation"]["replayed"] for result in results) == [False, True]
    with open_database(coordinator.paths) as connection:
        assert (
            connection.execute(
                "SELECT COUNT(*) FROM claims WHERE task_id = ?", (task["id"],)
            ).fetchone()[0]
            == 1
        )


def test_park_pending_survives_crash_and_reuses_exact_freeze(
    registered: tuple[WorkspaceCoordinator, Path],
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    coordinator, workspace = registered
    _, owner_token = start(coordinator, workspace, "owner")
    _, freeze_token = start(coordinator, workspace, "freeze")
    owned = coordinator.acquire_claim(
        workspace,
        owner_token,
        operation_id=operation_id(),
        writes=("Assets/Hero.prefab",),
    )
    freeze = coordinator.acquire_claim(
        workspace,
        freeze_token,
        operation_id=operation_id(),
        freeze=True,
    )
    mutation_id = operation_id()

    class SimulatedCrash(RuntimeError):
        pass

    monkeypatch.setattr(
        coordinator_module.time,
        "sleep",
        lambda _seconds: (_ for _ in ()).throw(SimulatedCrash()),
    )
    with pytest.raises(SimulatedCrash):
        coordinator.park_task(
            workspace,
            owner_token,
            operation_id=mutation_id,
            wait_seconds=9.9,
            requested_wait_seconds=9.9,
        )
    monkeypatch.undo()

    with pytest.raises(BusyError) as pending:
        coordinator.park_task(
            workspace,
            owner_token,
            operation_id=mutation_id,
            wait_seconds=9.8,
            requested_wait_seconds=9.9,
            receipt_only=True,
        )
    assert pending.value.details["reason"] == "operation-in-progress"
    coordinator.release_claim(
        workspace,
        freeze_token,
        str(freeze["id"]),
        operation_id=operation_id(),
    )
    resumed = coordinator.park_task(
        workspace,
        owner_token,
        operation_id=mutation_id,
        wait_seconds=9.8,
        requested_wait_seconds=9.9,
    )
    assert resumed["freeze_id"] == freeze["id"]
    assert resumed["claim_ids"] == [owned["id"]]
    assert resumed["resumed"] is True
    assert resumed["states"] == {owned["id"]: "active"}


def test_task_expiry_finalizes_pending_park_receipt(
    registered: tuple[WorkspaceCoordinator, Path],
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    coordinator, workspace = registered
    owner, owner_token = start(coordinator, workspace, "owner")
    _, freeze_token = start(coordinator, workspace, "freeze")
    owned = coordinator.acquire_claim(
        workspace,
        owner_token,
        operation_id=operation_id(),
        writes=("Assets/Hero.prefab",),
    )
    freeze = coordinator.acquire_claim(
        workspace,
        freeze_token,
        operation_id=operation_id(),
        freeze=True,
    )
    mutation_id = operation_id()

    class SimulatedCrash(RuntimeError):
        pass

    original_sleep = coordinator_module.time.sleep
    monkeypatch.setattr(
        coordinator_module.time,
        "sleep",
        lambda _seconds: (_ for _ in ()).throw(SimulatedCrash()),
    )
    with pytest.raises(SimulatedCrash):
        coordinator.park_task(
            workspace,
            owner_token,
            operation_id=mutation_id,
            wait_seconds=30.0,
            requested_wait_seconds=30.0,
        )
    monkeypatch.setattr(coordinator_module.time, "sleep", original_sleep)

    with open_database(coordinator.paths) as connection:
        connection.execute("UPDATE tasks SET expires_at = 0 WHERE id = ?", (owner["id"],))
        connection.commit()
    coordinator.status(workspace)

    replay = coordinator.park_task(
        workspace,
        owner_token,
        operation_id=mutation_id,
        receipt_only=True,
        wait_seconds=0.0,
        requested_wait_seconds=30.0,
    )
    assert replay["freeze_id"] == freeze["id"]
    assert replay["claim_ids"] == [owned["id"]]
    assert replay["states"] == {owned["id"]: "cancelled"}
    assert replay["parked"] is False
    assert replay["resumed"] is False
    assert replay["timed_out"] is False
    assert replay["aborted"] is True
    assert replay["reason"] == "task-ttl-expired"
    acknowledged = coordinator.acknowledge_receipt(
        mutation_id,
        str(replay["operation"]["fingerprint"]),
        str(replay["operation"]["delivery_digest"]),
    )
    assert acknowledged["acknowledged"] is True


def test_pending_park_receipt_upgrades_after_unknown_recovery(
    registered: tuple[WorkspaceCoordinator, Path],
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    coordinator, workspace = registered
    owner, owner_token = start(coordinator, workspace, "owner")
    _, freeze_token = start(coordinator, workspace, "freeze")
    owned = coordinator.acquire_claim(
        workspace,
        owner_token,
        operation_id=operation_id(),
        writes=("Assets/Hero.prefab",),
    )
    freeze = coordinator.acquire_claim(
        workspace,
        freeze_token,
        operation_id=operation_id(),
        freeze=True,
    )
    mutation_id = operation_id()

    class SimulatedCrash(RuntimeError):
        pass

    original_sleep = coordinator_module.time.sleep
    monkeypatch.setattr(
        coordinator_module.time,
        "sleep",
        lambda _seconds: (_ for _ in ()).throw(SimulatedCrash()),
    )
    with pytest.raises(SimulatedCrash):
        coordinator.park_task(
            workspace,
            owner_token,
            operation_id=mutation_id,
            wait_seconds=30.0,
            requested_wait_seconds=30.0,
        )
    monkeypatch.setattr(coordinator_module.time, "sleep", original_sleep)
    coordinator.release_claim(
        workspace,
        freeze_token,
        str(freeze["id"]),
        operation_id=operation_id(),
    )
    with open_database(coordinator.paths) as connection:
        connection.execute("UPDATE tasks SET expires_at = 0 WHERE id = ?", (owner["id"],))
        connection.commit()
    coordinator.status(workspace)

    unresolved = coordinator.park_task(
        workspace,
        owner_token,
        operation_id=mutation_id,
        receipt_only=True,
        wait_seconds=0.0,
        requested_wait_seconds=30.0,
    )
    assert unresolved["states"] == {owned["id"]: "active"}
    assert unresolved["reason"] == "task-ttl-expired-with-active-claim"
    with pytest.raises(BusyError) as unresolved_ack:
        coordinator.acknowledge_receipt(
            mutation_id,
            str(unresolved["operation"]["fingerprint"]),
            str(unresolved["operation"]["delivery_digest"]),
        )
    assert unresolved_ack.value.details["reason"] == "operation-recovery-pending"

    coordinator.resolve_unknown(
        workspace,
        str(owner["id"]),
        operation_id=operation_id(),
        resolution="failed",
        evidence="pending park recovered",
    )
    recovered = coordinator.park_task(
        workspace,
        owner_token,
        operation_id=mutation_id,
        receipt_only=True,
        wait_seconds=0.0,
        requested_wait_seconds=30.0,
    )
    assert recovered["states"] == {owned["id"]: "active"}
    assert recovered["parked"] is False
    assert recovered["resumed"] is False
    assert recovered["timed_out"] is False
    assert recovered["aborted"] is True
    assert recovered["reason"] == "task-ttl-expired-with-active-claim"
    assert recovered["resolution_reason"] == "task-recovery-resolved"
    assert recovered["terminal_state"] == "failed"
    assert recovered["terminal_result"] == "recovered-failed"
    acknowledged = coordinator.acknowledge_receipt(
        mutation_id,
        str(recovered["operation"]["fingerprint"]),
        str(recovered["operation"]["delivery_digest"]),
    )
    assert acknowledged["acknowledged"] is True
    assert inspect_state(coordinator.paths.database)["counts"]["outcome_unknown_tasks"] == 0


def test_terminal_task_prune_deletes_bound_recovery_resolution_receipt(
    registered: tuple[WorkspaceCoordinator, Path],
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    coordinator, workspace = registered
    scenario = resolved_pending_claim(coordinator, workspace, monkeypatch)
    monkeypatch.setattr(coordinator_module, "TERMINAL_TASK_RETENTION", 0)

    coordinator.status(workspace)
    with open_database(coordinator.paths) as connection:
        task_count = connection.execute(
            "SELECT COUNT(*) FROM tasks WHERE id = ?", (scenario["waiter"]["id"],)
        ).fetchone()[0]
        receipt_count = connection.execute(
            "SELECT COUNT(*) FROM operation_receipts WHERE operation_id = ?",
            (scenario["operation_id"],),
        ).fetchone()[0]
    assert (task_count, receipt_count) == (0, 0)
    with pytest.raises(StateError) as missing:
        coordinator.acquire_claim(
            workspace,
            str(scenario["waiter_token"]),
            operation_id=str(scenario["operation_id"]),
            receipt_only=True,
            resources=("unity-live",),
            wait_seconds=0.0,
            requested_wait_seconds=30.0,
            keep_queued=True,
        )
    assert missing.value.details["reason"] == "operation-receipt-missing"
    assert inspect_state(coordinator.paths.database)["schema_version"] == 3


def test_terminal_task_prune_deletes_all_three_resolution_receipts_but_not_normal_revocation(
    registered: tuple[WorkspaceCoordinator, Path],
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    coordinator, workspace = registered
    blocker_path = (
        Path(tempfile.gettempdir()) / f"scheduler-retention-blocker-{uuid.uuid4().hex}.token"
    )
    waiter_path = (
        Path(tempfile.gettempdir()) / f"scheduler-retention-waiter-{uuid.uuid4().hex}.token"
    )
    normal_path = (
        Path(tempfile.gettempdir()) / f"scheduler-retention-normal-{uuid.uuid4().hex}.token"
    )
    blocker, blocker_token = coordinator.start_task(
        workspace,
        "retention-blocker",
        "retention blocker",
        operation_id=operation_id(),
        token="retention-blocker-secret",
        token_file_path=str(blocker_path),
    )
    waiter, waiter_token = coordinator.start_task(
        workspace,
        "retention-waiter",
        "retention waiter",
        operation_id=operation_id(),
        token="retention-waiter-secret",
        token_file_path=str(waiter_path),
    )
    coordinator.acquire_claim(
        workspace,
        blocker_token,
        operation_id=operation_id(),
        resources=("retention-shared",),
    )
    queued = coordinator.acquire_claim(
        workspace,
        waiter_token,
        operation_id=operation_id(),
        resources=("retention-shared",),
        keep_queued=True,
    )
    queue_cancel = coordinator.cancel_claim(
        workspace,
        waiter_token,
        str(queued["id"]),
        operation_id=operation_id(),
    )
    active = coordinator.acquire_claim(
        workspace,
        waiter_token,
        operation_id=operation_id(),
        resources=("retention-private",),
    )
    claim_release = coordinator.release_claim(
        workspace,
        waiter_token,
        str(active["id"]),
        operation_id=operation_id(),
    )
    unknown_release = coordinator.release_task(
        workspace,
        waiter_token,
        operation_id=operation_id(),
        result="outcome-unknown",
    )
    coordinator.resolve_unknown(
        workspace,
        str(waiter["id"]),
        operation_id=operation_id(),
        resolution="completed",
        evidence="retention resolution",
    )

    def remove_token(path: Path, _expected_hash: str) -> bool:
        path.unlink(missing_ok=True)
        return True

    monkeypatch.setattr(coordinator_module, "remove_matching_token_hash_file", remove_token)
    coordinator.drain_token_cleanup_jobs(limit=8, workspace=workspace)
    for receipt, action, kwargs in (
        (claim_release, "claim.release", {"claim_id": str(active["id"])}),
        (queue_cancel, "queue.cancel", {"claim_id": str(queued["id"])}),
        (unknown_release, "task.release", {"result": "outcome-unknown", "note": None}),
    ):
        coordinator.replay_terminal_lifecycle_without_token(
            workspace,
            action=action,
            operation_id=str(receipt["operation"]["operation_id"]),
            token_file_path=str(waiter_path),
            **kwargs,
        )

    _normal, normal_token = coordinator.start_task(
        workspace,
        "retention-normal",
        "retention normal",
        operation_id=operation_id(),
        token="retention-normal-secret",
        token_file_path=str(normal_path),
    )
    normal_claim = coordinator.acquire_claim(
        workspace,
        normal_token,
        operation_id=operation_id(),
        resources=("retention-normal-resource",),
    )
    normal_release = coordinator.release_claim(
        workspace,
        normal_token,
        str(normal_claim["id"]),
        operation_id=operation_id(),
    )
    normal_task_release = coordinator.release_task(
        workspace,
        normal_token,
        operation_id=operation_id(),
        result="completed",
        token_cleanup_path=str(normal_path),
    )
    coordinator.acknowledge_receipt(
        str(normal_task_release["operation"]["operation_id"]),
        str(normal_task_release["operation"]["fingerprint"]),
        str(normal_task_release["operation"]["delivery_digest"]),
    )
    coordinator.drain_token_cleanup_jobs(limit=8, workspace=workspace)

    monkeypatch.setattr(coordinator_module, "TERMINAL_TASK_RETENTION", 0)
    coordinator.status(workspace)
    with open_database(coordinator.paths) as connection:
        recovery_receipts = connection.execute(
            "SELECT action FROM operation_receipts WHERE task_id = ? "
            "AND action IN ('claim.release', 'queue.cancel', 'task.release')",
            (waiter["id"],),
        ).fetchall()
        normal_receipt = connection.execute(
            "SELECT terminal_json FROM operation_receipts WHERE operation_id = ?",
            (normal_release["operation"]["operation_id"],),
        ).fetchone()
        waiter_task = connection.execute(
            "SELECT 1 FROM tasks WHERE id = ?", (waiter["id"],)
        ).fetchone()
    assert recovery_receipts == []
    assert waiter_task is None
    assert normal_receipt is not None
    assert json.loads(normal_receipt["terminal_json"])["reason"] == "task-released"
    assert blocker["state"] == "active"
    assert inspect_state(coordinator.paths.database)["integrity_check"] == "ok"
    assert verify_state(coordinator.paths.database)["integrity_check"] == "ok"
    blocker_path.unlink(missing_ok=True)
    waiter_path.unlink(missing_ok=True)
    normal_path.unlink(missing_ok=True)


def test_unregister_deletes_resolution_receipts_before_terminal_tasks(
    registered: tuple[WorkspaceCoordinator, Path],
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    coordinator, workspace = registered
    scenario = resolved_pending_claim(coordinator, workspace, monkeypatch)
    blocker = scenario["blocker"]
    coordinator.release_task(
        workspace,
        str(scenario["blocker_token"]),
        operation_id=operation_id(),
        result="outcome-unknown",
    )
    coordinator.resolve_unknown(
        workspace,
        str(blocker["id"]),
        operation_id=operation_id(),
        resolution="completed",
        evidence="terminalize blocker before unregister",
    )

    removed = coordinator.unregister(workspace, operation_id=operation_id())
    assert removed["removed"] is True
    with open_database(coordinator.paths) as connection:
        assert (
            connection.execute(
                "SELECT COUNT(*) FROM operation_receipts WHERE operation_id = ?",
                (scenario["operation_id"],),
            ).fetchone()[0]
            == 0
        )
    assert inspect_state(coordinator.paths.database)["schema_version"] == 3
    backup = tmp_path / "unregistered-resolution-backup.sqlite3"
    backup_state(coordinator.paths, backup, confirm_no_processes=True)
    restored_paths = resolve_state_paths(tmp_path / "unregistered-resolution-restored")
    restore_state(
        restored_paths,
        backup,
        confirm_no_processes=True,
    )
    assert inspect_state(restored_paths.database)["schema_version"] == 3


def test_recovery_resolution_ack_uses_causal_retirement_time(
    registered: tuple[WorkspaceCoordinator, Path],
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    coordinator, workspace = registered
    scenario = resolved_pending_claim(coordinator, workspace, monkeypatch)
    replay = scenario["replay"]
    with open_database(coordinator.paths) as connection:
        retired_at = connection.execute(
            "SELECT retired_at FROM operation_receipts WHERE operation_id = ?",
            (scenario["operation_id"],),
        ).fetchone()[0]
    monkeypatch.setattr(coordinator_module.time, "time", lambda: 1.0)
    coordinator.acknowledge_receipt(
        str(scenario["operation_id"]),
        str(replay["operation"]["fingerprint"]),
        str(replay["operation"]["delivery_digest"]),
    )
    with open_database(coordinator.paths) as connection:
        delivered_at = connection.execute(
            "SELECT delivered_at FROM operation_receipts WHERE operation_id = ?",
            (scenario["operation_id"],),
        ).fetchone()[0]
    assert delivered_at >= retired_at


def test_legacy_delivered_unresolved_receipt_ack_remains_idempotent(
    registered: tuple[WorkspaceCoordinator, Path],
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    coordinator, workspace = registered
    scenario = resolved_pending_claim(coordinator, workspace, monkeypatch)
    replay = scenario["replay"]
    with open_database(coordinator.paths) as connection:
        connection.execute(
            "UPDATE operation_receipts SET terminal_json = NULL, delivered_at = retired_at "
            "WHERE operation_id = ?",
            (scenario["operation_id"],),
        )
        connection.commit()

    acknowledged = coordinator.acknowledge_receipt(
        str(scenario["operation_id"]),
        str(replay["operation"]["fingerprint"]),
        "0" * 64,
    )
    assert acknowledged["acknowledged"] is True
    assert acknowledged["operation"]["replayed"] is True


def test_identify_is_read_only_and_release_replays_before_terminal_auth(
    registered: tuple[WorkspaceCoordinator, Path],
) -> None:
    coordinator, workspace = registered
    task, token = start(coordinator, workspace, "owner")
    with open_database(coordinator.paths) as connection:
        before = connection.execute(
            "SELECT epoch FROM workspaces WHERE root = ?", (str(workspace.resolve()),)
        ).fetchone()[0]
    identified = coordinator.identify_task(workspace, token)
    with open_database(coordinator.paths) as connection:
        after = connection.execute(
            "SELECT epoch FROM workspaces WHERE root = ?", (str(workspace.resolve()),)
        ).fetchone()[0]
    assert identified["id"] == task["id"]
    assert before == after

    mutation_id = operation_id()
    cleanup_path = os.path.normpath(str((workspace / "missing.token").resolve()))
    released = coordinator.release_task(
        workspace,
        token,
        operation_id=mutation_id,
        result="completed",
        token_cleanup_path=cleanup_path,
    )
    replay = coordinator.release_task(
        workspace,
        token,
        operation_id=mutation_id,
        result="completed",
        token_cleanup_path=cleanup_path,
        receipt_only=True,
    )
    assert replay["id"] == released["id"]
    assert replay["operation"]["replayed"] is True
    with pytest.raises(AuthorizationError):
        coordinator.identify_task(workspace, token)


def test_terminal_release_ack_retires_only_safe_task_lifecycle_receipts(
    registered: tuple[WorkspaceCoordinator, Path],
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    coordinator, workspace = registered
    start_id = operation_id()
    _task, token = coordinator.start_task(
        workspace,
        "owner",
        "owner work",
        operation_id=start_id,
    )
    heartbeat_id = operation_id()
    coordinator.heartbeat(
        workspace,
        token,
        operation_id=heartbeat_id,
        note="still running",
    )
    release_id = operation_id()
    cleanup_path = os.path.normpath(str((workspace / "owner.token").resolve()))
    released = coordinator.release_task(
        workspace,
        token,
        operation_id=release_id,
        result="completed",
        token_cleanup_path=cleanup_path,
    )
    fingerprint = str(released["operation"]["fingerprint"])
    delivery_digest = str(released["operation"]["delivery_digest"])

    monkeypatch.setattr(
        coordinator_module,
        "remove_matching_token_hash_file",
        lambda _path, _expected_hash: False,
    )
    with pytest.raises(StateError) as cleanup_failed:
        coordinator.acknowledge_receipt(release_id, fingerprint, delivery_digest)
    assert cleanup_failed.value.details["reason"] == "receipt-token-cleanup-failed"
    with open_database(coordinator.paths) as connection:
        before_retry = {
            row["operation_id"]: row
            for row in connection.execute(
                "SELECT operation_id, delivered_at, token_cleanup_path "
                "FROM operation_receipts WHERE operation_id IN (?, ?, ?)",
                (start_id, heartbeat_id, release_id),
            ).fetchall()
        }
    assert before_retry[start_id]["delivered_at"] is None
    assert before_retry[heartbeat_id]["delivered_at"] is None
    assert before_retry[release_id]["delivered_at"] is not None
    assert before_retry[release_id]["token_cleanup_path"] == cleanup_path

    monkeypatch.setattr(
        coordinator_module,
        "remove_matching_token_hash_file",
        lambda _path, _expected_hash: True,
    )
    acknowledged = coordinator.acknowledge_receipt(
        release_id,
        fingerprint,
        delivery_digest,
    )
    assert acknowledged["token_file_removed"] is True
    assert set(acknowledged) == {
        "action",
        "acknowledged",
        "token_cleanup_expected",
        "token_file_removed",
        "operation",
    }
    with open_database(coordinator.paths) as connection:
        after_retry = {
            row["operation_id"]: row
            for row in connection.execute(
                "SELECT operation_id, delivered_at, token_cleanup_path "
                "FROM operation_receipts WHERE operation_id IN (?, ?, ?)",
                (start_id, heartbeat_id, release_id),
            ).fetchall()
        }
    assert after_retry[start_id]["delivered_at"] is not None
    assert after_retry[heartbeat_id]["delivered_at"] is not None
    assert after_retry[release_id]["token_cleanup_path"] is None

    terminal_start, _ = coordinator.start_task(
        workspace,
        "owner",
        "owner work",
        operation_id=start_id,
        receipt_only=True,
        token=token,
    )
    heartbeat_replay = coordinator.heartbeat(
        workspace,
        token,
        operation_id=heartbeat_id,
        receipt_only=True,
        note="still running",
    )
    assert terminal_start["aborted"] is True
    assert terminal_start["reason"] == "task-released"
    assert terminal_start["terminal_state"] == "completed"
    assert terminal_start["token_cleanup_completed"] is True
    assert terminal_start["operation"]["retired"] is True
    assert heartbeat_replay["operation"]["delivered"] is True


def test_terminal_lifecycle_replay_binds_deleted_token_to_receipt_and_task(
    registered: tuple[WorkspaceCoordinator, Path],
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    coordinator, workspace = registered
    token_path = os.path.normpath(str((workspace / "owner.token").resolve()))
    start_id = operation_id()
    _task, token = coordinator.start_task(
        workspace,
        "owner",
        "owner work",
        operation_id=start_id,
        token_file_path=token_path,
        token="owner-secret",
    )
    heartbeat_id = operation_id()
    coordinator.heartbeat(
        workspace,
        token,
        operation_id=heartbeat_id,
        note="still running",
    )

    with pytest.raises(StateError) as active:
        coordinator.replay_terminal_lifecycle_without_token(
            workspace,
            action="task.heartbeat",
            operation_id=heartbeat_id,
            note="still running",
            token_file_path=token_path,
        )
    assert active.value.details["reason"] == "task-token-missing"

    release_id = operation_id()
    released = coordinator.release_task(
        workspace,
        token,
        operation_id=release_id,
        result="completed",
        token_cleanup_path=token_path,
    )
    monkeypatch.setattr(
        coordinator_module,
        "remove_matching_token_hash_file",
        lambda _path, _expected_hash: True,
    )
    coordinator.acknowledge_receipt(
        release_id,
        str(released["operation"]["fingerprint"]),
        str(released["operation"]["delivery_digest"]),
    )

    replay = coordinator.replay_terminal_lifecycle_without_token(
        workspace,
        action="task.heartbeat",
        operation_id=heartbeat_id,
        note="still running",
        token_file_path=token_path,
    )
    assert replay["aborted"] is True
    assert replay["reason"] == "task-released"
    assert replay["terminal_state"] == "completed"
    assert replay["operation"]["replayed"] is True
    original_fingerprint = str(replay["operation"]["fingerprint"])

    with pytest.raises(StateError) as missing:
        coordinator.replay_terminal_lifecycle_without_token(
            workspace,
            action="task.heartbeat",
            operation_id=operation_id(),
            note="still running",
            token_file_path=token_path,
        )
    assert missing.value.details["reason"] == "operation-receipt-missing"

    with pytest.raises(UsageError) as conflict:
        coordinator.replay_terminal_lifecycle_without_token(
            workspace,
            action="task.heartbeat",
            operation_id=heartbeat_id,
            note="different note",
            token_file_path=token_path,
        )
    assert conflict.value.details["reason"] == "operation-id-conflict"

    with pytest.raises(StateError) as wrong_path:
        coordinator.replay_terminal_lifecycle_without_token(
            workspace,
            action="task.heartbeat",
            operation_id=heartbeat_id,
            note="still running",
            token_file_path=os.path.normpath(str((workspace / "other.token").resolve())),
        )
    assert wrong_path.value.details["reason"] == "operation-receipt-invalid"

    with open_database(coordinator.paths) as connection:
        connection.execute(
            "UPDATE operation_receipts SET fingerprint = ? WHERE operation_id = ?",
            ("0" * 64, heartbeat_id),
        )
        connection.commit()
    with pytest.raises(StateError) as tampered:
        coordinator.replay_terminal_lifecycle_without_token(
            workspace,
            action="task.heartbeat",
            operation_id=heartbeat_id,
            note="still running",
            token_file_path=token_path,
        )
    assert tampered.value.details["reason"] == "operation-receipt-invalid"

    with open_database(coordinator.paths) as connection:
        connection.execute(
            "UPDATE operation_receipts SET fingerprint = ?, result_json = ? WHERE operation_id = ?",
            (original_fingerprint, "{", heartbeat_id),
        )
        connection.commit()
    with pytest.raises(StateError) as malformed:
        coordinator.replay_terminal_lifecycle_without_token(
            workspace,
            action="task.heartbeat",
            operation_id=heartbeat_id,
            note="still running",
            token_file_path=token_path,
        )
    assert malformed.value.details["reason"] == "operation-receipt-invalid"


@pytest.mark.parametrize("lineage_action", ["task.release", "task.start"])
def test_terminal_lifecycle_replay_rejects_malformed_lineage_json(
    registered: tuple[WorkspaceCoordinator, Path],
    lineage_action: str,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    coordinator, workspace = registered
    token_path = os.path.normpath(str((workspace / "lineage-owner.token").resolve()))
    started, token = coordinator.start_task(
        workspace,
        "lineage-owner",
        "lineage corruption",
        operation_id=operation_id(),
        token_file_path=token_path,
        token="lineage-owner-secret",
    )
    heartbeat_id = operation_id()
    coordinator.heartbeat(
        workspace,
        token,
        operation_id=heartbeat_id,
        note="lineage corruption",
    )
    release_id = operation_id()
    release = coordinator.release_task(
        workspace,
        token,
        operation_id=release_id,
        result="completed",
        token_cleanup_path=token_path,
    )
    monkeypatch.setattr(coordinator_module, "remove_matching_token_hash_file", lambda *_: True)
    coordinator.acknowledge_receipt(
        release_id,
        str(release["operation"]["fingerprint"]),
        str(release["operation"]["delivery_digest"]),
    )
    lineage_id = (
        release_id
        if lineage_action == "task.release"
        else str(started["operation"]["operation_id"])
    )
    with open_database(coordinator.paths) as connection:
        connection.execute(
            "UPDATE operation_receipts SET result_json = ? WHERE operation_id = ?",
            ("{", lineage_id),
        )
        connection.commit()

    with pytest.raises(StateError) as malformed:
        coordinator.replay_terminal_lifecycle_without_token(
            workspace,
            action="task.heartbeat",
            operation_id=heartbeat_id,
            token_file_path=token_path,
            note="lineage corruption",
        )
    assert malformed.value.details == {
        "reason": "operation-receipt-invalid",
        "operation_id": heartbeat_id,
        "recovery_required": True,
    }


@pytest.mark.parametrize(
    "action",
    ["task.heartbeat", "claim.acquire", "freeze.acquire", "task.park"],
)
def test_terminal_lifecycle_replay_deleted_token_exact_lineage_for_all_actions(
    tmp_path: Path,
    action: str,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    workspace = tmp_path / action.replace(".", "-")
    workspace.mkdir()
    coordinator = WorkspaceCoordinator(resolve_state_paths(tmp_path / f"{action}-state"))
    coordinator.register(workspace, operation_id=operation_id())
    if os.name == "nt":
        canonical_temp = tmp_path / "runneradmin" / "AppData" / "Local" / "Temp"
        canonical_temp.mkdir(parents=True)
        alias_temp = tmp_path / "RUNNER~1" / "AppData" / "Local" / "Temp"
        token_root = alias_temp / f"unity-scheduler-replay-{uuid.uuid4().hex}"
        original_resolve = Path.resolve

        def resolve_alias(path: Path, strict: bool = False) -> Path:
            try:
                relative = path.relative_to(alias_temp)
            except ValueError:
                return original_resolve(path, strict=strict)
            return canonical_temp / relative

        monkeypatch.setattr(state_module.tempfile, "gettempdir", lambda: str(alias_temp))
        monkeypatch.setattr(Path, "resolve", resolve_alias)
    else:
        token_root = Path(tempfile.gettempdir()) / f"unity-scheduler-replay-{uuid.uuid4().hex}"
    token_root.mkdir()
    token_path = token_root / "owner.token"
    blocker_path = token_root / "blocker.token"

    try:
        _task, token = coordinator.start_task(
            workspace,
            "owner",
            f"{action} replay",
            operation_id=operation_id(),
            token_file_path=str(token_path),
            token="owner-secret",
        )
        replay_arguments: dict[str, object]
        if action == "task.heartbeat":
            mutation = coordinator.heartbeat(
                workspace,
                token,
                operation_id=operation_id(),
                note="exact replay",
            )
            replay_arguments = {"note": "exact replay"}
        elif action == "claim.acquire":
            mutation = coordinator.acquire_claim(
                workspace,
                token,
                operation_id=operation_id(),
                resources=("unity-live",),
            )
            replay_arguments = {
                "resources": ("unity-live",),
                "writes": (),
                "wait_seconds": 0.0,
                "requested_wait_seconds": 0.0,
                "keep_queued": False,
            }
        elif action == "freeze.acquire":
            mutation = coordinator.acquire_claim(
                workspace,
                token,
                operation_id=operation_id(),
                freeze=True,
            )
            replay_arguments = {
                "freeze": True,
                "priority": "normal",
                "wait_seconds": 0.0,
                "requested_wait_seconds": 0.0,
                "keep_queued": False,
            }
        else:
            _blocker, blocker_token = coordinator.start_task(
                workspace,
                "blocker",
                "freeze blocker",
                operation_id=operation_id(),
                token_file_path=str(blocker_path),
                token="blocker-secret",
            )
            coordinator.acquire_claim(
                workspace,
                token,
                operation_id=operation_id(),
                writes=("owned.txt",),
            )
            coordinator.acquire_claim(
                workspace,
                blocker_token,
                operation_id=operation_id(),
                freeze=True,
                keep_queued=True,
            )
            mutation = coordinator.park_task(
                workspace,
                token,
                operation_id=operation_id(),
            )
            replay_arguments = {
                "wait_seconds": 0.0,
                "requested_wait_seconds": 0.0,
            }

        operation = mutation["operation"]
        release = coordinator.release_task(
            workspace,
            token,
            operation_id=operation_id(),
            result="completed",
            token_cleanup_path=str(token_path),
        )
        coordinator.acknowledge_receipt(
            release["operation"]["operation_id"],
            release["operation"]["fingerprint"],
            release["operation"]["delivery_digest"],
        )
        if action == "task.park":
            blocker_release = coordinator.release_task(
                workspace,
                blocker_token,
                operation_id=operation_id(),
                result="completed",
                token_cleanup_path=str(blocker_path),
            )
            coordinator.acknowledge_receipt(
                blocker_release["operation"]["operation_id"],
                blocker_release["operation"]["fingerprint"],
                blocker_release["operation"]["delivery_digest"],
            )

        with open_database(coordinator.paths) as connection:
            before = [
                tuple(row)
                for row in connection.execute(
                    "SELECT operation_id, result_json, terminal_json, delivered_at, retired_at "
                    "FROM operation_receipts ORDER BY operation_id"
                ).fetchall()
            ]

        replay = coordinator.replay_terminal_lifecycle_without_token(
            workspace,
            action=action,
            operation_id=operation["operation_id"],
            token_file_path=str(token_path),
            **replay_arguments,
        )
        replay_again = coordinator.replay_terminal_lifecycle_without_token(
            workspace,
            action=action,
            operation_id=operation["operation_id"],
            token_file_path=str(token_path),
            **replay_arguments,
        )
        assert replay == replay_again
        assert replay["aborted"] is True
        assert replay["reason"] == "task-released"
        assert replay["operation"]["replayed"] is True
        assert replay["operation"]["delivered"] is (action == "task.heartbeat")
        assert replay["operation"]["finalized"] is True
        assert "retired" not in replay["operation"]
        assert not token_path.exists()
        with open_database(coordinator.paths) as connection:
            after = [
                tuple(row)
                for row in connection.execute(
                    "SELECT operation_id, result_json, terminal_json, delivered_at, retired_at "
                    "FROM operation_receipts ORDER BY operation_id"
                ).fetchall()
            ]
        assert after == before
    finally:
        shutil.rmtree(token_root, ignore_errors=True)


def test_terminal_lifecycle_replay_uses_retained_task_path_after_ttl_expiry(
    registered: tuple[WorkspaceCoordinator, Path],
    tmp_path: Path,
) -> None:
    coordinator, workspace = registered
    token_path = os.path.normpath(str((tmp_path / "ttl-owner.token").resolve()))
    task, token = coordinator.start_task(
        workspace,
        "ttl-owner",
        "ttl lifecycle",
        operation_id=operation_id(),
        token_file_path=token_path,
        token="ttl-owner-secret",
    )
    heartbeat_id = operation_id()
    coordinator.heartbeat(
        workspace,
        token,
        operation_id=heartbeat_id,
        note="ttl replay",
    )
    with open_database(coordinator.paths) as connection:
        connection.execute("UPDATE tasks SET expires_at = 0 WHERE id = ?", (task["id"],))
        connection.commit()
    coordinator.status(workspace)
    coordinator.drain_token_cleanup_jobs()
    assert not Path(token_path).exists()
    with open_database(coordinator.paths) as connection:
        retained = connection.execute(
            "SELECT token_file_path, token_file_identity FROM tasks WHERE id = ?",
            (task["id"],),
        ).fetchone()
        start_receipt = connection.execute(
            "SELECT terminal_json FROM operation_receipts WHERE task_id = ? "
            "AND action = 'task.start'",
            (task["id"],),
        ).fetchone()
    assert retained["token_file_path"] == token_path
    expected_identity = os.path.normcase(token_path)
    if os.name == "nt":
        expected_identity = expected_identity.casefold()
    assert retained["token_file_identity"] == expected_identity
    assert start_receipt["terminal_json"] is not None
    assert '"token_cleanup_completed":true' in start_receipt["terminal_json"]

    replay = coordinator.replay_terminal_lifecycle_without_token(
        workspace,
        action="task.heartbeat",
        operation_id=heartbeat_id,
        token_file_path=token_path,
        note="ttl replay",
    )
    assert replay["reason"] == "task-ttl-expired"
    assert replay["terminal_state"] == "expired"
    assert replay["operation"]["replayed"] is True

    with pytest.raises(StateError) as wrong_path:
        coordinator.replay_terminal_lifecycle_without_token(
            workspace,
            action="task.heartbeat",
            operation_id=heartbeat_id,
            token_file_path=os.path.normpath(str((tmp_path / "other.token").resolve())),
            note="ttl replay",
        )
    assert wrong_path.value.details["reason"] == "operation-receipt-invalid"


def test_terminal_lifecycle_replay_accepts_outcome_unknown_release_history(
    registered: tuple[WorkspaceCoordinator, Path],
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    coordinator, workspace = registered
    _blocker, blocker_token = start(coordinator, workspace, "outcome-blocker")
    blocker_claim = coordinator.acquire_claim(
        workspace,
        blocker_token,
        operation_id=operation_id(),
        resources=("unity-live",),
    )
    token_path = os.path.normpath(str((tmp_path / "outcome-owner.token").resolve()))
    task, token = coordinator.start_task(
        workspace,
        "outcome-owner",
        "outcome lifecycle",
        operation_id=operation_id(),
        token_file_path=token_path,
        token="outcome-owner-secret",
    )
    claim_id = operation_id()

    class SimulatedCrash(RuntimeError):
        pass

    with monkeypatch.context() as scoped:
        scoped.setattr(
            coordinator_module.time,
            "sleep",
            lambda _seconds: (_ for _ in ()).throw(SimulatedCrash()),
        )
        with pytest.raises(SimulatedCrash):
            coordinator.acquire_claim(
                workspace,
                token,
                operation_id=claim_id,
                resources=("unity-live",),
                wait_seconds=30.0,
                requested_wait_seconds=30.0,
                keep_queued=True,
            )

    coordinator.release_task(
        workspace,
        token,
        operation_id=operation_id(),
        result="outcome-unknown",
    )
    coordinator.resolve_unknown(
        workspace,
        str(task["id"]),
        operation_id=operation_id(),
        resolution="completed",
        evidence="pending claim recovered",
    )
    coordinator.drain_token_cleanup_jobs()
    assert not Path(token_path).exists()

    replay = coordinator.replay_terminal_lifecycle_without_token(
        workspace,
        action="claim.acquire",
        operation_id=claim_id,
        token_file_path=token_path,
        resources=("unity-live",),
        wait_seconds=0.0,
        requested_wait_seconds=30.0,
        keep_queued=True,
    )
    assert replay["reason"] == "task-released-outcome-unknown"
    assert replay["resolution_reason"] == "task-recovery-resolved"
    assert replay["terminal_state"] == "completed"
    assert replay["operation"]["replayed"] is True

    coordinator.release_claim(
        workspace,
        blocker_token,
        str(blocker_claim["id"]),
        operation_id=operation_id(),
    )


def test_terminal_lifecycle_replay_uses_release_lineage_after_start_receipt_prune(
    registered: tuple[WorkspaceCoordinator, Path],
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    coordinator, workspace = registered
    token_path = os.path.normpath(str((tmp_path / "release-owner.token").resolve()))
    task, token = coordinator.start_task(
        workspace,
        "release-owner",
        "release lineage",
        operation_id=operation_id(),
        token_file_path=token_path,
        token="release-owner-secret",
    )
    heartbeat_id = operation_id()
    coordinator.heartbeat(
        workspace,
        token,
        operation_id=heartbeat_id,
        note="release replay",
    )
    release = coordinator.release_task(
        workspace,
        token,
        operation_id=operation_id(),
        result="completed",
        token_cleanup_path=token_path,
    )
    monkeypatch.setattr(coordinator_module, "remove_matching_token_hash_file", lambda *_: True)
    coordinator.acknowledge_receipt(
        release["operation"]["operation_id"],
        release["operation"]["fingerprint"],
        release["operation"]["delivery_digest"],
    )
    with open_database(coordinator.paths) as connection:
        connection.execute(
            "DELETE FROM operation_receipts WHERE operation_id = ?",
            (task["operation"]["operation_id"],),
        )
        connection.commit()

    replay = coordinator.replay_terminal_lifecycle_without_token(
        workspace,
        action="task.heartbeat",
        operation_id=heartbeat_id,
        token_file_path=token_path,
        note="release replay",
    )
    assert replay["reason"] == "task-released"
    assert replay["operation"]["replayed"] is True

    with pytest.raises(StateError) as wrong_path:
        coordinator.replay_terminal_lifecycle_without_token(
            workspace,
            action="task.heartbeat",
            operation_id=heartbeat_id,
            token_file_path=os.path.normpath(str((tmp_path / "wrong-release.token").resolve())),
            note="release replay",
        )
    assert wrong_path.value.details["reason"] == "operation-receipt-invalid"


def test_release_prunes_newly_retired_lifecycle_receipts_in_same_transaction(
    registered: tuple[WorkspaceCoordinator, Path],
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    coordinator, workspace = registered
    task, token = start(coordinator, workspace, "owner")
    heartbeat_ids = []
    for index in range(2):
        heartbeat = coordinator.heartbeat(
            workspace,
            token,
            operation_id=operation_id(),
            note=f"release-retention-{index}",
        )
        heartbeat_ids.append(str(heartbeat["operation"]["operation_id"]))

    monkeypatch.setattr(coordinator_module, "DELIVERED_OPERATION_RETENTION", 1)
    coordinator.release_task(
        workspace,
        token,
        operation_id=operation_id(),
        result="outcome-unknown",
    )

    with open_database(coordinator.paths) as connection:
        retained = connection.execute(
            "SELECT COUNT(*) FROM operation_receipts "
            f"WHERE operation_id IN ({','.join('?' for _ in heartbeat_ids)})",
            heartbeat_ids,
        ).fetchone()[0]
        task_state = connection.execute(
            "SELECT state FROM tasks WHERE id = ?", (task["id"],)
        ).fetchone()[0]
    assert retained == 1
    assert task_state == "outcome_unknown"


def test_schema_three_inspect_rejects_release_cleanup_path_not_bound_to_parameters(
    registered: tuple[WorkspaceCoordinator, Path],
) -> None:
    coordinator, workspace = registered
    _, token = start(coordinator, workspace, "owner")
    mutation_id = operation_id()
    cleanup_path = os.path.normpath(str((workspace / "owner.token").resolve()))
    coordinator.release_task(
        workspace,
        token,
        operation_id=mutation_id,
        result="completed",
        token_cleanup_path=cleanup_path,
    )
    other_path = os.path.normpath(str((workspace / "other.token").resolve()))
    with sqlite3.connect(coordinator.paths.database) as connection:
        connection.execute(
            "UPDATE operation_receipts SET token_cleanup_path = ? WHERE operation_id = ?",
            (other_path, mutation_id),
        )

    with pytest.raises(StateError) as invalid:
        inspect_state(coordinator.paths.database)
    assert invalid.value.details["reason"] == "operation-receipt-invalid"


def test_schema_three_receipts_are_in_same_transaction(
    registered: tuple[WorkspaceCoordinator, Path],
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    coordinator, workspace = registered
    _, token = start(coordinator, workspace, "owner")
    mutation_id = operation_id()

    def fail_receipt(*_args: object, **_kwargs: object) -> dict[str, object]:
        raise sqlite3.OperationalError("injected receipt insert failure")

    monkeypatch.setattr(coordinator, "_record_operation", fail_receipt)
    with pytest.raises(sqlite3.OperationalError):
        coordinator.acquire_claim(
            workspace,
            token,
            operation_id=mutation_id,
            resources=("rolled-back",),
        )
    with open_database(coordinator.paths) as connection:
        assert (
            connection.execute(
                "SELECT COUNT(*) FROM claim_scopes WHERE scope_type = 'resource' "
                "AND value = 'rolled-back'"
            ).fetchone()[0]
            == 0
        )
        assert (
            connection.execute(
                "SELECT COUNT(*) FROM operation_receipts WHERE operation_id = ?",
                (mutation_id,),
            ).fetchone()[0]
            == 0
        )


def test_schema_two_migrates_to_schema_three_with_empty_receipt_ledger(
    tmp_path: Path,
) -> None:
    paths = resolve_state_paths(tmp_path / "legacy-state")
    with sqlite3.connect(paths.database) as connection:
        for statement in state_module._SCHEMA_TWO_STATEMENTS:
            connection.execute(statement)
        for statement in state_module._SCHEMA_TWO_INDEX_STATEMENTS:
            connection.execute(statement)
        connection.execute("INSERT INTO scheduler_meta(key, value) VALUES('schema_version', '2')")

    with open_database(paths) as connection:
        assert (
            connection.execute(
                "SELECT value FROM scheduler_meta WHERE key = 'schema_version'"
            ).fetchone()[0]
            == "3"
        )
        assert connection.execute("SELECT COUNT(*) FROM operation_receipts").fetchone()[0] == 0


def test_invalid_schema_two_migration_is_atomic(tmp_path: Path) -> None:
    paths = resolve_state_paths(tmp_path / "invalid-legacy-state")
    with sqlite3.connect(paths.database) as connection:
        for statement in state_module._SCHEMA_TWO_STATEMENTS:
            connection.execute(statement)
        for statement in state_module._SCHEMA_TWO_INDEX_STATEMENTS:
            connection.execute(statement)
        connection.execute("INSERT INTO scheduler_meta(key, value) VALUES('schema_version', '2')")
        connection.execute("CREATE TABLE unexpected(value TEXT)")
        journal_before = connection.execute("PRAGMA journal_mode").fetchone()[0]
    bytes_before = paths.database.read_bytes()

    with pytest.raises(StateError):
        open_database(paths)

    assert paths.database.read_bytes() == bytes_before
    with sqlite3.connect(paths.database) as connection:
        assert connection.execute("PRAGMA journal_mode").fetchone()[0] == journal_before
        assert (
            connection.execute(
                "SELECT value FROM scheduler_meta WHERE key = 'schema_version'"
            ).fetchone()[0]
            == "2"
        )
        assert (
            connection.execute(
                "SELECT COUNT(*) FROM sqlite_master "
                "WHERE type = 'table' AND name = 'operation_receipts'"
            ).fetchone()[0]
            == 0
        )


@pytest.mark.parametrize("task_result", ["completed", "failed"])
def test_terminal_claim_release_proof_and_normal_task_release_contract(
    registered: tuple[WorkspaceCoordinator, Path],
    monkeypatch: pytest.MonkeyPatch,
    task_result: str,
) -> None:
    coordinator, workspace = registered
    token_path = Path(tempfile.gettempdir()) / f"scheduler-proof-{uuid.uuid4().hex}.token"
    token_path = Path(os.path.normpath(str(token_path.resolve())))
    try:
        task, token = coordinator.start_task(
            workspace,
            "proof-owner",
            "proof lifecycle",
            operation_id=operation_id(),
            token="proof-secret",
            token_file_path=str(token_path),
        )
        claim = coordinator.acquire_claim(
            workspace,
            token,
            operation_id=operation_id(),
            resources=("proof-resource",),
        )
        claim_release = coordinator.release_claim(
            workspace,
            token,
            str(claim["id"]),
            operation_id=operation_id(),
        )
        task_release = coordinator.release_task(
            workspace,
            token,
            operation_id=operation_id(),
            result=task_result,
            token_cleanup_path=str(token_path),
        )

        def remove_token(path: Path, _expected_hash: str) -> bool:
            path.unlink(missing_ok=True)
            return True

        monkeypatch.setattr(coordinator_module, "remove_matching_token_hash_file", remove_token)
        coordinator.acknowledge_receipt(
            str(task_release["operation"]["operation_id"]),
            str(task_release["operation"]["fingerprint"]),
            str(task_release["operation"]["delivery_digest"]),
        )
        with open_database(coordinator.paths) as connection:
            claim_receipt = connection.execute(
                "SELECT terminal_json, retired_at FROM operation_receipts WHERE operation_id = ?",
                (claim_release["operation"]["operation_id"],),
            ).fetchone()
            task_receipt = connection.execute(
                "SELECT terminal_json, retired_at FROM operation_receipts WHERE operation_id = ?",
                (task_release["operation"]["operation_id"],),
            ).fetchone()
            assert (
                connection.execute(
                    "SELECT COUNT(*) FROM token_cleanup_jobs WHERE task_id = ?",
                    (task["id"],),
                ).fetchone()[0]
                == 0
            )
        claim_proof = json.loads(claim_receipt["terminal_json"])
        assert claim_receipt["retired_at"] is not None
        assert claim_proof == {
            "aborted": True,
            "reason": "task-released",
            "terminal_finished_at": task_release["finished_at"],
            "terminal_result": task_result,
            "terminal_state": task_result,
            "token_cleanup_completed": True,
        }
        assert task_receipt["terminal_json"] is None
        assert task_receipt["retired_at"] is None
        with open_database(coordinator.paths) as connection:
            workspace_id = connection.execute(
                "SELECT workspace_id FROM tasks WHERE id = ?", (task["id"],)
            ).fetchone()[0]
            safely_retired = connection.execute(
                "SELECT COUNT(*) FROM operation_receipts "
                "WHERE workspace_id = ? AND finalized_at IS NOT NULL "
                "AND delivered_at IS NULL AND retired_at IS NOT NULL",
                (workspace_id,),
            ).fetchone()[0]
            actionable = connection.execute(
                "SELECT COUNT(*) FROM operation_receipts "
                "WHERE workspace_id = ? AND finalized_at IS NOT NULL "
                "AND delivered_at IS NULL AND retired_at IS NULL",
                (workspace_id,),
            ).fetchone()[0]
        assert safely_retired > 0
        assert actionable > 0
        assert (
            coordinator.maintenance_history(workspace)["receipt_summary"]["finalized_undelivered"]
            == actionable
        )
        replay = coordinator.replay_terminal_task_release_without_token(
            workspace,
            operation_id=str(task_release["operation"]["operation_id"]),
            result=task_result,
            note=None,
            token_cleanup_path=str(token_path),
            token_file_path=str(token_path),
        )
        assert replay["operation"]["replayed"] is True
        assert replay["token_cleanup_pending"] is False
    finally:
        token_path.unlink(missing_ok=True)


def test_terminal_queue_cancel_proof_is_retired_after_cleanup(
    registered: tuple[WorkspaceCoordinator, Path],
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    coordinator, workspace = registered
    blocker_path = Path(tempfile.gettempdir()) / f"scheduler-queue-blocker-{uuid.uuid4().hex}.token"
    queued_path = Path(tempfile.gettempdir()) / f"scheduler-queue-owner-{uuid.uuid4().hex}.token"
    blocker_path = Path(os.path.normpath(str(blocker_path.resolve())))
    queued_path = Path(os.path.normpath(str(queued_path.resolve())))
    try:
        blocker, blocker_token = coordinator.start_task(
            workspace,
            "queue-blocker",
            "queue blocker",
            operation_id=operation_id(),
            token="blocker-secret",
            token_file_path=str(blocker_path),
        )
        queued, queued_token = coordinator.start_task(
            workspace,
            "queue-owner",
            "queue owner",
            operation_id=operation_id(),
            token="queued-secret",
            token_file_path=str(queued_path),
        )
        coordinator.acquire_claim(
            workspace,
            blocker_token,
            operation_id=operation_id(),
            resources=("queue-proof",),
        )
        queued_claim = coordinator.acquire_claim(
            workspace,
            queued_token,
            operation_id=operation_id(),
            resources=("queue-proof",),
            keep_queued=True,
        )
        cancelled = coordinator.cancel_claim(
            workspace,
            queued_token,
            str(queued_claim["id"]),
            operation_id=operation_id(),
        )
        queued_release = coordinator.release_task(
            workspace,
            queued_token,
            operation_id=operation_id(),
            result="completed",
            token_cleanup_path=str(queued_path),
        )

        def remove_token(path: Path, _expected_hash: str) -> bool:
            path.unlink(missing_ok=True)
            return True

        monkeypatch.setattr(coordinator_module, "remove_matching_token_hash_file", remove_token)
        coordinator.acknowledge_receipt(
            str(queued_release["operation"]["operation_id"]),
            str(queued_release["operation"]["fingerprint"]),
            str(queued_release["operation"]["delivery_digest"]),
        )
        replay = coordinator.replay_terminal_lifecycle_without_token(
            workspace,
            action="queue.cancel",
            operation_id=str(cancelled["operation"]["operation_id"]),
            claim_id=str(queued_claim["id"]),
            token_file_path=str(queued_path),
        )
        assert replay["operation"]["replayed"] is True
        with open_database(coordinator.paths) as connection:
            receipt = connection.execute(
                "SELECT terminal_json, retired_at FROM operation_receipts WHERE operation_id = ?",
                (cancelled["operation"]["operation_id"],),
            ).fetchone()
        proof = json.loads(receipt["terminal_json"])
        assert receipt["retired_at"] is not None
        assert proof["aborted"] is True
        assert proof["reason"] == "task-released"
        assert proof["terminal_state"] == "completed"
        assert proof["terminal_result"] == "completed"
        assert proof["token_cleanup_completed"] is True
        assert queued["state"] == "active"
        assert blocker["state"] == "active"
    finally:
        blocker_path.unlink(missing_ok=True)
        queued_path.unlink(missing_ok=True)


def test_ttl_and_recovery_proofs_are_retired_only_after_token_cleanup(
    registered: tuple[WorkspaceCoordinator, Path],
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    coordinator, workspace = registered
    ttl_path = Path(tempfile.gettempdir()) / f"scheduler-ttl-proof-{uuid.uuid4().hex}.token"
    recovery_path = (
        Path(tempfile.gettempdir()) / f"scheduler-recovery-proof-{uuid.uuid4().hex}.token"
    )
    ttl_path = Path(os.path.normpath(str(ttl_path.resolve())))
    recovery_path = Path(os.path.normpath(str(recovery_path.resolve())))
    try:
        ttl_task, _ = coordinator.start_task(
            workspace,
            "ttl-owner",
            "ttl proof",
            operation_id=operation_id(),
            token="ttl-secret",
            token_file_path=str(ttl_path),
        )
        with open_database(coordinator.paths) as connection:
            connection.execute("UPDATE tasks SET expires_at = 0 WHERE id = ?", (ttl_task["id"],))
            connection.commit()

        def remove_token(path: Path, _expected_hash: str) -> bool:
            path.unlink(missing_ok=True)
            return True

        monkeypatch.setattr(coordinator_module, "remove_matching_token_hash_file", remove_token)
        coordinator.status(workspace)
        drained = coordinator.drain_token_cleanup_jobs(limit=8, workspace=workspace)
        assert drained["completed"] == 1
        with open_database(coordinator.paths) as connection:
            ttl_receipt = connection.execute(
                "SELECT terminal_json, retired_at FROM operation_receipts "
                "WHERE task_id = ? AND action = 'task.start'",
                (ttl_task["id"],),
            ).fetchone()
        ttl_proof = json.loads(ttl_receipt["terminal_json"])
        assert ttl_receipt["retired_at"] is not None
        assert ttl_proof["reason"] == "task-ttl-expired"
        assert ttl_proof["terminal_state"] == "expired"
        assert ttl_proof["token_cleanup_completed"] is True

        recovery_task, recovery_token = coordinator.start_task(
            workspace,
            "recovery-owner",
            "recovery proof",
            operation_id=operation_id(),
            token="recovery-secret",
            token_file_path=str(recovery_path),
        )
        recovery_path.write_text("recovery-secret\n", encoding="utf-8")
        unknown_release = coordinator.release_task(
            workspace,
            recovery_token,
            operation_id=operation_id(),
            result="outcome-unknown",
        )
        with pytest.raises(UsageError) as pending:
            coordinator.replay_terminal_lifecycle_without_token(
                workspace,
                action="task.release",
                operation_id=str(unknown_release["operation"]["operation_id"]),
                result="outcome-unknown",
                note=None,
                token_file_path=str(recovery_path),
            )
        assert pending.value.details["reason"] == "task-token-still-present"
        coordinator.resolve_unknown(
            workspace,
            str(recovery_task["id"]),
            operation_id=operation_id(),
            resolution="completed",
            evidence="recovery proof",
        )
        coordinator.drain_token_cleanup_jobs(limit=8, workspace=workspace)
        replay = coordinator.replay_terminal_lifecycle_without_token(
            workspace,
            action="task.release",
            operation_id=str(unknown_release["operation"]["operation_id"]),
            result="outcome-unknown",
            note=None,
            token_file_path=str(recovery_path),
        )
        assert replay["resolution_reason"] == "task-recovery-resolved"
        with open_database(coordinator.paths) as connection:
            recovery_receipt = connection.execute(
                "SELECT terminal_json, retired_at FROM operation_receipts WHERE operation_id = ?",
                (unknown_release["operation"]["operation_id"],),
            ).fetchone()
        recovery_proof = json.loads(recovery_receipt["terminal_json"])
        assert recovery_receipt["retired_at"] is not None
        assert recovery_proof["resolution_reason"] == "task-recovery-resolved"
        assert recovery_proof["terminal_state"] == "completed"
        assert recovery_proof["token_cleanup_completed"] is True
    finally:
        ttl_path.unlink(missing_ok=True)
        recovery_path.unlink(missing_ok=True)


def test_concurrent_terminal_ack_leaves_only_complete_receipt_state(
    registered: tuple[WorkspaceCoordinator, Path],
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    coordinator, workspace = registered
    token_path = (
        Path(tempfile.gettempdir()) / f"scheduler-concurrent-proof-{uuid.uuid4().hex}.token"
    )
    token_path = Path(os.path.normpath(str(token_path.resolve())))
    try:
        _task, token = coordinator.start_task(
            workspace,
            "concurrent-owner",
            "concurrent proof",
            operation_id=operation_id(),
            token="concurrent-secret",
            token_file_path=str(token_path),
        )
        release = coordinator.release_task(
            workspace,
            token,
            operation_id=operation_id(),
            result="completed",
            token_cleanup_path=str(token_path),
        )

        def remove_token(path: Path, _expected_hash: str) -> bool:
            path.unlink(missing_ok=True)
            return True

        monkeypatch.setattr(coordinator_module, "remove_matching_token_hash_file", remove_token)
        arguments = (
            str(release["operation"]["operation_id"]),
            str(release["operation"]["fingerprint"]),
            str(release["operation"]["delivery_digest"]),
        )
        with ThreadPoolExecutor(max_workers=2) as pool:
            results = list(
                pool.map(lambda _: coordinator.acknowledge_receipt(*arguments), range(2))
            )
        assert all(result["acknowledged"] is True for result in results)
        with open_database(coordinator.paths) as connection:
            receipt = connection.execute(
                "SELECT terminal_json, retired_at, delivered_at, token_cleanup_path "
                "FROM operation_receipts WHERE operation_id = ?",
                (release["operation"]["operation_id"],),
            ).fetchone()
        assert receipt["delivered_at"] is not None
        assert receipt["token_cleanup_path"] is None
        assert receipt["terminal_json"] is None
        assert receipt["retired_at"] is None
        replay = coordinator.replay_terminal_task_release_without_token(
            workspace,
            operation_id=str(release["operation"]["operation_id"]),
            result="completed",
            note=None,
            token_cleanup_path=str(token_path),
            token_file_path=str(token_path),
        )
        assert replay["operation"]["replayed"] is True
    finally:
        token_path.unlink(missing_ok=True)


def test_control_char_scopes_are_rejected_without_database_writes(
    registered: tuple[WorkspaceCoordinator, Path],
) -> None:
    coordinator, workspace = registered
    _task, token = start(coordinator, workspace, "control-owner")
    with open_database(coordinator.paths) as connection:
        before = tuple(
            tuple(row)
            for row in connection.execute(
                "SELECT * FROM operation_receipts ORDER BY operation_id"
            ).fetchall()
        )
    with pytest.raises(UsageError) as write_error:
        coordinator.acquire_claim(
            workspace,
            token,
            operation_id=operation_id(),
            writes=("Assets/Bad\x00.prefab",),
        )
    assert write_error.value.details["reason"] == "write-scope-control-character"
    with pytest.raises(UsageError) as resource_error:
        coordinator.acquire_claim(
            workspace,
            token,
            operation_id=operation_id(),
            resources=("unity-\x01live",),
        )
    assert resource_error.value.details["reason"] == "resource-control-character"
    with open_database(coordinator.paths) as connection:
        after = tuple(
            tuple(row)
            for row in connection.execute(
                "SELECT * FROM operation_receipts ORDER BY operation_id"
            ).fetchall()
        )
    assert after == before
    if os.name != "nt":
        first = workspace / "CaseA"
        second = workspace / "casea"
        first.mkdir()
        second.mkdir()
        coordinator.register(first, operation_id=operation_id())
        coordinator.register(second, operation_id=operation_id())
        assert WorkspaceCoordinator._normalize_writes(str(workspace), ("Assets/Foo.prefab",)) == (
            "Assets/Foo.prefab",
        )
        with open_database(coordinator.paths) as connection:
            assert connection.execute("SELECT COUNT(*) FROM workspaces").fetchone()[0] == 3
