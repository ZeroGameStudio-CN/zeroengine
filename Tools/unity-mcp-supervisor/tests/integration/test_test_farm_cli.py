from __future__ import annotations

import json
from pathlib import Path

from click.testing import CliRunner

from tests.helpers import create_unity_project
from unity_mcp_supervisor.cli import cli


def invoke(state: Path, *arguments: str):
    return CliRunner().invoke(cli, ["--state-dir", str(state), *arguments])


def test_farm_provision_and_status_are_machine_local(tmp_path: Path) -> None:
    state = tmp_path / "state"
    slot_root = tmp_path / "slots"
    provisioned = invoke(
        state,
        "test",
        "farm",
        "provision",
        "--workers",
        "2",
        "--slot-root",
        str(slot_root),
    )
    assert provisioned.exit_code == 0, provisioned.output
    payload = json.loads(provisioned.output)
    assert payload["result"]["workers"] == 2
    assert {Path(value["root"]) for value in payload["result"]["slots"]} == {
        slot_root.resolve() / "slot-01",
        slot_root.resolve() / "slot-02",
    }
    status = invoke(state, "test", "farm", "status")
    assert status.exit_code == 0, status.output
    assert json.loads(status.output)["result"]["provisioned"] is True


def test_submit_without_external_state_proof_returns_serial_route(
    tmp_path: Path,
) -> None:
    project = create_unity_project(tmp_path / "project")
    result = invoke(
        tmp_path / "state",
        "test",
        "submit",
        "--project",
        str(project),
        "--test-filter",
        "Tests.One",
    )
    assert result.exit_code == 0, result.output
    payload = json.loads(result.output)
    assert payload["result"]["route"] == "serial"
    assert payload["result"]["reason"] == "external-state-safety-not-declared"


def test_isolated_submit_rejects_unfiltered_run_before_snapshot(
    monkeypatch, tmp_path: Path
) -> None:
    project = create_unity_project(tmp_path / "project")
    state = tmp_path / "state"
    assert (
        invoke(state, "workspace", "bootstrap", "--project", str(project)).exit_code
        == 0
    )
    started = invoke(
        state,
        "workspace",
        "task",
        "start",
        "--project",
        str(project),
        "--owner",
        "test-owner",
        "--summary",
        "reject unfiltered test",
    )
    token_file = tmp_path / "task.token"
    token_file.write_text(
        json.loads(started.output)["result"]["task_token"], encoding="utf-8"
    )
    assert invoke(state, "test", "farm", "provision", "--workers", "1").exit_code == 0
    snapshot_called = False

    def snapshot(*_args, **_kwargs):
        nonlocal snapshot_called
        snapshot_called = True

    monkeypatch.setattr("unity_mcp_supervisor.cli.create_snapshot", snapshot)
    result = invoke(
        state,
        "test",
        "submit",
        "--project",
        str(project),
        "--baseline-only",
        "--external-state-safe",
        "--token-file",
        str(token_file),
    )
    assert result.exit_code == 2, result.output
    assert "at least one exact" in result.output
    assert snapshot_called is False


def test_required_task_can_submit_and_cancel_owned_job(
    monkeypatch, tmp_path: Path
) -> None:
    project = create_unity_project(tmp_path / "project")
    state = tmp_path / "state"
    assert (
        invoke(state, "workspace", "bootstrap", "--project", str(project)).exit_code
        == 0
    )
    started = invoke(
        state,
        "workspace",
        "task",
        "start",
        "--project",
        str(project),
        "--owner",
        "test-owner",
        "--summary",
        "isolated baseline test",
    )
    token = json.loads(started.output)["result"]["task_token"]
    token_file = tmp_path / "task.token"
    token_file.write_text(token, encoding="utf-8")
    assert invoke(state, "test", "farm", "provision", "--workers", "1").exit_code == 0

    def snapshot(_root, artifact_root, _scopes, _overlay, **_kwargs):
        return {
            "snapshot_id": "snapshot-one",
            "manifest": str(artifact_root / "snapshot.json"),
        }

    monkeypatch.setattr("unity_mcp_supervisor.cli.create_snapshot", snapshot)
    monkeypatch.setattr("unity_mcp_supervisor.cli.launch_workers", lambda *_args: [])
    submitted = invoke(
        state,
        "test",
        "submit",
        "--project",
        str(project),
        "--test-filter",
        "Tests.One",
        "--baseline-only",
        "--external-state-safe",
        "--token-file",
        str(token_file),
    )
    assert submitted.exit_code == 0, submitted.output
    payload = json.loads(submitted.output)
    assert payload["result"]["route"] == "isolated"
    assert token not in submitted.output
    job_id = payload["result"]["job_id"]
    cancelled = invoke(
        state,
        "test",
        "cancel",
        "--job",
        job_id,
        "--token-file",
        str(token_file),
    )
    assert cancelled.exit_code == 0, cancelled.output
    assert json.loads(cancelled.output)["result"]["state"] == "cancelled"


def test_open_test_job_blocks_workspace_unregister(monkeypatch, tmp_path: Path) -> None:
    project = create_unity_project(tmp_path / "project")
    state = tmp_path / "state"
    assert (
        invoke(state, "workspace", "bootstrap", "--project", str(project)).exit_code
        == 0
    )
    started = invoke(
        state,
        "workspace",
        "task",
        "start",
        "--project",
        str(project),
        "--owner",
        "test-owner",
        "--summary",
        "queued isolated test",
    )
    task = json.loads(started.output)["result"]
    token_file = tmp_path / "task.token"
    token_file.write_text(task["task_token"], encoding="utf-8")
    assert invoke(state, "test", "farm", "provision", "--workers", "1").exit_code == 0
    monkeypatch.setattr(
        "unity_mcp_supervisor.cli.create_snapshot",
        lambda _root, artifact, _scopes, _overlay, **_kwargs: {
            "snapshot_id": "snapshot-one",
            "manifest": str(artifact / "snapshot.json"),
        },
    )
    monkeypatch.setattr("unity_mcp_supervisor.cli.launch_workers", lambda *_args: [])
    submitted = invoke(
        state,
        "test",
        "submit",
        "--project",
        str(project),
        "--test-filter",
        "Tests.One",
        "--baseline-only",
        "--external-state-safe",
        "--token-file",
        str(token_file),
    )
    job_id = json.loads(submitted.output)["result"]["job_id"]
    released = invoke(
        state,
        "workspace",
        "task",
        "release",
        "--project",
        str(project),
        "--result",
        "completed",
        "--token-file",
        str(token_file),
    )
    assert released.exit_code == 5, released.output
    assert json.loads(released.output)["result"]["reason"] == "task-has-test-jobs"
    assert token_file.is_file()
    blocked = invoke(state, "workspace", "unregister", "--project", str(project))
    assert blocked.exit_code == 5, blocked.output
    assert json.loads(blocked.output)["result"]["test_job_count"] == 1
    cancelled = invoke(
        state,
        "test",
        "cancel",
        "--job",
        job_id,
        "--token-file",
        str(token_file),
    )
    assert cancelled.exit_code == 0, cancelled.output
    assert json.loads(cancelled.output)["result"]["state"] == "cancelled"
    released = invoke(
        state,
        "workspace",
        "task",
        "release",
        "--project",
        str(project),
        "--result",
        "completed",
        "--token-file",
        str(token_file),
    )
    assert released.exit_code == 0, released.output
    assert not token_file.exists()
    removed = invoke(state, "workspace", "unregister", "--project", str(project))
    assert removed.exit_code == 0, removed.output
