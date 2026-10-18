"""Bounded read-only diagnostics never grant receipt acknowledgement authority."""

from __future__ import annotations

import json
from pathlib import Path

import pytest

from unity_workspace_scheduler.cli import run
from unity_workspace_scheduler.coordinator import WorkspaceCoordinator
from unity_workspace_scheduler.errors import UsageError
from unity_workspace_scheduler.state import open_database, resolve_state_paths


@pytest.fixture
def fixture(tmp_path: Path):
    coordinator = WorkspaceCoordinator(resolve_state_paths(tmp_path / "state"))
    project = tmp_path / "project"
    project.mkdir()
    coordinator.register(project)
    task, token = coordinator.start_task(project, "receipt-owner", "private fixture")
    return coordinator, project, task, token


def snapshot(coordinator):
    with open_database(coordinator.paths) as connection:
        return {
            name: [tuple(row) for row in connection.execute(f'SELECT * FROM "{name}" ORDER BY 1')]
            for name in (
                "tasks",
                "claims",
                "operation_receipts",
                "token_cleanup_jobs",
                "recovery_events",
            )
        }


def test_metadata_binds_task_and_never_exposes_credentials_or_ack_capability(fixture):
    coordinator, project, task, token = fixture
    claim = coordinator.acquire_claim(project, token, writes=("Assets/PrivateDraft.cs",))
    before = snapshot(coordinator)
    result = coordinator.maintenance_receipts(project)
    assert snapshot(coordinator) == before
    record = next(row for row in result["receipts"] if row["result_id"] == claim["id"])
    assert record["task_id"] == task["id"]
    assert record["owner"] == "receipt-owner"
    assert record["task_state"] == "active"
    assert record["result_state"] == "active"
    serialized = json.dumps(result)
    for private in (
        token,
        "fingerprint",
        "delivery_digest",
        "parameters_json",
        "PrivateDraft.cs",
        "token_file",
    ):
        assert private not in serialized


def test_read_does_not_expire_a_task_or_drain_terminal_token_cleanup(fixture):
    coordinator, project, task, _token = fixture
    with open_database(coordinator.paths) as connection:
        connection.execute("UPDATE tasks SET expires_at=0 WHERE id=?", (task["id"],))
    before = snapshot(coordinator)
    coordinator.maintenance_receipts(project)
    assert snapshot(coordinator) == before
    # A separate fresh terminal release has its own cleanup pointer, not an
    # active-task release or a diagnostic permission to delete its token.
    other, other_token = coordinator.start_task(project, "terminal-owner", "fixture")
    released = coordinator.release_task(project, other_token, result="completed")
    before = snapshot(coordinator)
    records = coordinator.maintenance_receipts(project)["receipts"]
    assert snapshot(coordinator) == before
    record = next(
        row for row in records if row["operation_id"] == released["operation"]["operation_id"]
    )
    assert record["cleanup_pending"] is True
    assert record["task_id"] == other["id"]
    assert record["task_state"] == "completed"
    assert len(record["cleanup_path_hash"]) == 64
    assert "token_cleanup_path" not in record


def test_limit_and_workspace_scope_do_not_include_other_projects(fixture, tmp_path):
    coordinator, project, _task, _token = fixture
    foreign = tmp_path / "foreign"
    foreign.mkdir()
    coordinator.register(foreign)
    foreign_task, _ = coordinator.start_task(foreign, "foreign-owner", "fixture")
    result = coordinator.maintenance_receipts(project, limit=1)
    assert result["limit"] == 1 and result["truncated"] is True
    assert len(result["receipts"]) == 1
    assert foreign_task["id"] not in json.dumps(coordinator.maintenance_receipts(project))


def test_corrupt_result_is_diagnostic_only_and_retained(fixture):
    coordinator, project, task, _ = fixture
    with open_database(coordinator.paths) as connection:
        connection.execute(
            "UPDATE operation_receipts SET result_json='{' WHERE task_id=?", (task["id"],)
        )
    before = snapshot(coordinator)
    records = coordinator.maintenance_receipts(project)["receipts"]
    assert any(row["task_id"] == task["id"] and row["result_valid"] is False for row in records)
    assert snapshot(coordinator) == before


@pytest.mark.parametrize("limit", [0, 101, True, 1.5])
def test_invalid_limits_are_refused(fixture, limit):
    coordinator, project, _, _ = fixture
    with pytest.raises(UsageError):
        coordinator.maintenance_receipts(project, limit=limit)


def test_cli_diagnostic_never_runs_cleanup(fixture, capsys, monkeypatch):
    coordinator, project, _, _ = fixture

    def forbidden(*_args, **_kwargs):
        raise AssertionError("A diagnostic cannot run token cleanup")

    monkeypatch.setattr(WorkspaceCoordinator, "drain_token_cleanup_jobs", forbidden)
    before = snapshot(coordinator)
    assert (
        run(
            [
                "--state-dir",
                str(coordinator.paths.root),
                "maintenance",
                "receipts",
                "--workspace",
                str(project),
                "--limit",
                "2",
            ]
        )
        == 0
    )
    result = json.loads(capsys.readouterr().out)["result"]
    assert len(result["receipts"]) <= 2
    assert snapshot(coordinator) == before
