from __future__ import annotations

import uuid
from pathlib import Path

import pytest

import unity_workspace_scheduler.coordinator as coordinator_module
from unity_workspace_scheduler.coordinator import WorkspaceCoordinator
from unity_workspace_scheduler.state import resolve_state_paths


@pytest.mark.parametrize("retention", [0, 1, 3, 8])
def test_retention_keeps_exact_global_order_and_protected_receipts(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch, retention: int
) -> None:
    """The cheap no-overflow path and narrow victim query share one policy."""
    coordinator = WorkspaceCoordinator(resolve_state_paths(tmp_path / "state"))
    workspace = tmp_path / "workspace"
    workspace.mkdir()
    coordinator.register(workspace)
    monkeypatch.setattr(coordinator_module, "DELIVERED_OPERATION_RETENTION", retention)
    with coordinator._transaction() as connection:
        template = dict(connection.execute("SELECT * FROM operation_receipts").fetchone())
        original_id = template["operation_id"]
        # COALESCE uses delivery when both timestamps exist; creation and ID break ties.
        rows = [
            (1, 20.0, None, 10.0, None),
            (2, 20.0, None, 11.0, None),
            (3, 20.0, None, 11.0, None),
            (4, None, 25.0, 12.0, None),
            (5, 21.0, 100.0, 13.0, None),
            (6, None, None, 14.0, None),
            (7, 30.0, None, 15.0, str(tmp_path / "protected.token")),
        ]
        identifiers = {}
        for number, delivered, retired, created, cleanup in rows:
            receipt = dict(template)
            identifiers[number] = str(uuid.UUID(f"00000000-0000-4000-8000-{number:012d}"))
            receipt.update(
                operation_id=identifiers[number],
                created_at=created,
                finalized_at=created + 1,
                delivered_at=delivered,
                retired_at=retired,
                token_cleanup_path=cleanup,
            )
            connection.execute(
                "INSERT INTO operation_receipts ("
                + ",".join(receipt)
                + ") VALUES ("
                + ",".join("?" for _ in receipt)
                + ")",
                tuple(receipt.values()),
            )
        expected = {original_id, identifiers[6], identifiers[7]}
        expected.update(identifiers[number] for number in [4, 5, 3, 2, 1][:retention])
        for _ in range(2):
            coordinator._prune_delivered_operations(connection)
            actual = {
                row[0] for row in connection.execute("SELECT operation_id FROM operation_receipts")
            }
            assert actual == expected
