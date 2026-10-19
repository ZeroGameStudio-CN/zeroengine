from __future__ import annotations

import uuid
from pathlib import Path

import pytest

from unity_workspace_scheduler.coordinator import WorkspaceCoordinator
from unity_workspace_scheduler.state import resolve_state_paths


def test_wait_maintenance_does_not_rescan_retained_heartbeat_payloads(tmp_path: Path) -> None:
    """Use SQLite work units instead of a machine-dependent timing assertion."""
    coordinator = WorkspaceCoordinator(resolve_state_paths(tmp_path / "state"))
    workspace = tmp_path / "workspace"
    workspace.mkdir()
    coordinator.register(workspace)
    _, token = coordinator.start_task(workspace, "owner", "synthetic history")
    coordinator.heartbeat(workspace, token)
    with coordinator._transaction() as connection:
        task_row = dict(connection.execute("SELECT * FROM tasks").fetchone())
        template = dict(
            connection.execute(
                "SELECT * FROM operation_receipts WHERE action = 'task.heartbeat'"
            ).fetchone()
        )
        # Delivered history is deliberately below the retention limit. A normal
        # maintenance pass must not inspect every large, irrelevant payload.
        for _ in range(4000):
            row = {
                **template,
                "operation_id": str(uuid.uuid4()),
                "delivered_at": template["finalized_at"],
            }
            connection.execute(
                "INSERT INTO operation_receipts ("
                + ",".join(row)
                + ") VALUES ("
                + ",".join("?" for _ in row)
                + ")",
                tuple(row.values()),
            )
        steps = 0

        def progress() -> int:
            nonlocal steps
            steps += 100
            return 0

        connection.set_progress_handler(progress, 100)
        try:
            coordinator._finalize_due_wait_operations(
                connection, str(task_row["workspace_id"]), float(task_row["created_at"])
            )
        finally:
            connection.set_progress_handler(None, 0)
        assert steps < 1_000
        assert connection.execute("SELECT COUNT(*) FROM operation_receipts").fetchone()[0] == 4003
        assert connection.execute("SELECT COUNT(*) FROM tasks").fetchone()[0] == 1


@pytest.mark.parametrize("claim_state", ["released", "cancelled"])
def test_unknown_claim_lookup_skips_terminal_history(tmp_path: Path, claim_state: str) -> None:
    coordinator = WorkspaceCoordinator(resolve_state_paths(tmp_path / "state"))
    workspace = tmp_path / "workspace"
    workspace.mkdir()
    coordinator.register(workspace)
    task, token = coordinator.start_task(workspace, "owner", "private history")
    original = coordinator.acquire_claim(workspace, token, resources=("unity-live",))
    with coordinator._transaction() as connection:
        template = dict(connection.execute("SELECT * FROM claims").fetchone())
        for index in range(4000):
            row = {
                **template,
                "id": uuid.uuid4().hex,
                "state": claim_state,
                "queue_order": index + 2,
            }
            connection.execute(
                "INSERT INTO claims ("
                + ",".join(row)
                + ") VALUES ("
                + ",".join("?" for _ in row)
                + ")",
                tuple(row.values()),
            )
        connection.execute("UPDATE tasks SET state = 'outcome_unknown' WHERE id = ?", (task["id"],))
        steps = 0

        def progress() -> int:
            nonlocal steps
            steps += 100
            return 0

        connection.set_progress_handler(progress, 100)
        try:
            unknown = coordinator._unknown_open_claims(connection, template["workspace_id"])
        finally:
            connection.set_progress_handler(None, 0)
        assert [row["id"] for row in unknown] == [original["id"]]
        assert steps < 1000
        assert connection.execute("SELECT COUNT(*) FROM claims").fetchone()[0] == 4001


def test_retention_checks_below_limit_do_not_load_history_payloads(tmp_path: Path) -> None:
    coordinator = WorkspaceCoordinator(resolve_state_paths(tmp_path / "state"))
    workspace = tmp_path / "workspace"
    workspace.mkdir()
    coordinator.register(workspace)
    _, token = coordinator.start_task(workspace, "owner", "private history")
    coordinator.heartbeat(workspace, token)
    with coordinator._transaction() as connection:
        template = dict(
            connection.execute(
                "SELECT * FROM operation_receipts WHERE action = 'task.heartbeat'"
            ).fetchone()
        )
        for _ in range(4000):
            row = {
                **template,
                "operation_id": str(uuid.uuid4()),
                "delivered_at": template["finalized_at"],
            }
            connection.execute(
                "INSERT INTO operation_receipts ("
                + ",".join(row)
                + ") VALUES ("
                + ",".join("?" for _ in row)
                + ")",
                tuple(row.values()),
            )
        reads = set()

        def authorizer(action, table, column, database, trigger):
            if action == 20:  # SQLITE_READ
                reads.add((table, column))
            return 0

        connection.set_authorizer(authorizer)
        try:
            coordinator._prune_delivered_operations(connection)
        finally:
            connection.set_authorizer(None)
        assert ("operation_receipts", "parameters_json") not in reads
        assert ("operation_receipts", "result_json") not in reads
        assert ("operation_receipts", "action") not in reads
        assert connection.execute("SELECT COUNT(*) FROM operation_receipts").fetchone()[0] == 4003
