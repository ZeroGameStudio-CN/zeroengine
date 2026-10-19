"""Exact historical acquisition reads are evidence, never renewed authority."""

import json
import uuid

import pytest

from unity_workspace_scheduler.cli import run
from unity_workspace_scheduler.coordinator import WorkspaceCoordinator
from unity_workspace_scheduler.errors import UsageError
from unity_workspace_scheduler.state import open_database, resolve_state_paths


@pytest.fixture
def fixture(tmp_path):
    coordinator = WorkspaceCoordinator(resolve_state_paths(tmp_path / "state"))
    project = tmp_path / "project"
    project.mkdir()
    coordinator.register(project)
    task, token = coordinator.start_task(project, "audit-owner", "fixture")
    claim = coordinator.acquire_claim(project, token, writes=("Assets/PrivateDraft.cs",))
    return coordinator, project, task, token, claim


def snapshot(coordinator):
    with open_database(coordinator.paths) as connection:
        return {
            name: [tuple(row) for row in connection.execute(f'SELECT * FROM "{name}" ORDER BY 1')]
            for name in (
                "tasks",
                "claims",
                "claim_scopes",
                "operation_receipts",
                "token_cleanup_jobs",
                "recovery_events",
                "scheduler_meta",
            )
        }


def inspect(fixture, **overrides):
    coordinator, project, _, _, claim = fixture
    args = {
        "claim_id": claim["id"],
        "acquire_operation_id": claim["operation"]["operation_id"],
        **overrides,
    }
    before = snapshot(coordinator)
    result = coordinator.maintenance_claim(project, **args)
    assert snapshot(coordinator) == before
    return result


def test_delivered_acquisition_is_readable_without_replay_or_credentials(fixture):
    coordinator, project, _, token, claim = fixture
    operation = claim["operation"]
    coordinator.acknowledge_receipt(
        operation["operation_id"], operation["fingerprint"], operation["delivery_digest"]
    )
    assert all(
        row["result_id"] != claim["id"]
        for row in coordinator.maintenance_receipts(project)["receipts"]
    )
    result = inspect(fixture)
    assert result["binding"] == "matched"
    assert result["acquisition"]["delivered_at"] is not None
    assert result["claim"]["state"] == "active"
    assert result["grants_authority"] is False
    for secret in (token, "fingerprint", "delivery_digest", "PrivateDraft.cs", "token_file"):
        assert secret not in json.dumps(result)


def test_terminal_claim_is_distinct_from_immutable_active_grant(fixture):
    coordinator, project, _, token, claim = fixture
    coordinator.release_claim(project, token, claim["id"])
    result = inspect(fixture)
    assert result["acquisition"]["result_state"] == "active"
    assert result["claim"]["state"] == "released"
    assert result["claim"]["released_at"] >= result["claim"]["granted_at"]
    assert result["closure_cause"] is None


def test_cancelled_queued_claim_has_no_invented_grant_or_cause(fixture):
    coordinator, project, _, _, _ = fixture
    _, token = coordinator.start_task(project, "waiter", "fixture")
    claim = coordinator.acquire_claim(
        project, token, writes=("Assets/PrivateDraft.cs",), keep_queued=True
    )
    assert claim["state"] == "queued"
    coordinator.cancel_claim(project, token, claim["id"])
    result = inspect(
        fixture, claim_id=claim["id"], acquire_operation_id=claim["operation"]["operation_id"]
    )
    assert result["claim"]["state"] == "cancelled"
    assert result["claim"]["granted_at"] is None
    assert result["closure_cause"] is None


def test_wrong_acquisition_and_foreign_workspace_are_not_disclosed(fixture, tmp_path):
    coordinator, _, _, _, _ = fixture
    foreign = tmp_path / "foreign"
    foreign.mkdir()
    coordinator.register(foreign)
    _, token = coordinator.start_task(foreign, "foreign-owner", "fixture")
    foreign_claim = coordinator.acquire_claim(foreign, token, resources=("unity-live",))
    result = inspect(fixture, acquire_operation_id=foreign_claim["operation"]["operation_id"])
    assert result["binding"] == "receipt_unavailable"
    assert result["claim"] is None and result["acquisition"] is None
    result = inspect(fixture, claim_id=foreign_claim["id"])
    assert result["binding"] == "mismatch"
    assert result["claim"] is None and result["acquisition"] is None


@pytest.mark.parametrize("raw", ["{", "[]", '{"state":{"secret":"private"}}'])
def test_corrupt_receipt_remains_diagnostic_and_never_leaks_raw_result(fixture, raw):
    coordinator, _, _, _, claim = fixture
    with open_database(coordinator.paths) as connection:
        connection.execute(
            "UPDATE operation_receipts SET result_json=? WHERE operation_id=?",
            (raw, claim["operation"]["operation_id"]),
        )
    result = inspect(fixture)
    assert result["binding"] == "invalid_result"
    assert result["claim"] is None and result["acquisition"] is None


def test_missing_retained_claim_is_reported_without_a_closure_explanation(fixture):
    coordinator, _, _, _, claim = fixture
    with open_database(coordinator.paths) as connection:
        connection.execute("DELETE FROM claims WHERE id=?", (claim["id"],))
    result = inspect(fixture)
    assert result["binding"] == "claim_unavailable"
    assert result["claim"] is None
    assert result["closure_cause"] is None


@pytest.mark.parametrize("claim_id", [None, True, "bad", "A" * 32, "z" * 32])
def test_invalid_claim_selector_is_refused(fixture, claim_id):
    with pytest.raises(UsageError):
        inspect(fixture, claim_id=claim_id)


def test_missing_operation_does_not_create_a_receipt(fixture):
    assert inspect(fixture, acquire_operation_id=str(uuid.uuid4()))["binding"] == (
        "receipt_unavailable"
    )


def test_read_does_not_expire_an_overdue_task(fixture):
    coordinator, _, task, _, _ = fixture
    with open_database(coordinator.paths) as connection:
        connection.execute("UPDATE tasks SET expires_at=0 WHERE id=?", (task["id"],))
    assert inspect(fixture)["claim"]["state"] == "active"


def test_cli_never_drains_cleanup_or_runs_maintenance(fixture, monkeypatch, capsys):
    coordinator, project, _, _, claim = fixture

    def forbidden(*_args, **_kwargs):
        raise AssertionError("Audit cannot maintain, replay, or clean up")

    for method in ("drain_token_cleanup_jobs", "_maintain", "_replay_or_missing"):
        monkeypatch.setattr(WorkspaceCoordinator, method, forbidden)
    before = snapshot(coordinator)
    assert (
        run(
            [
                "--state-dir",
                str(coordinator.paths.root),
                "maintenance",
                "claim",
                "--workspace",
                str(project),
                "--claim-id",
                claim["id"],
                "--acquire-operation-id",
                claim["operation"]["operation_id"],
            ]
        )
        == 0
    )
    result = json.loads(capsys.readouterr().out)["result"]
    assert result["binding"] == "matched"
    assert snapshot(coordinator) == before
