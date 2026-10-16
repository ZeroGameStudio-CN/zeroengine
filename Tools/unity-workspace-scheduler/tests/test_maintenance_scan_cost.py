from __future__ import annotations

import uuid
from pathlib import Path

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
