from __future__ import annotations

import json
from pathlib import Path

from click.testing import CliRunner

from tests.helpers import create_unity_project, fake_http_server
from unity_mcp_supervisor.cli import cli
from unity_mcp_supervisor.editor_bootstrap import BootstrapEvidence
from unity_mcp_supervisor.errors import ServiceError
from unity_mcp_supervisor.project_resolver import (
    ResolvedProject,
    unity_project_hash_candidate,
)
from unity_mcp_supervisor.service_state import Settings
from unity_mcp_supervisor.supervisor import ServiceManager

FAKE_SERVER = Path(__file__).parents[1] / "fixtures" / "fake_upstream_server.py"
FAKE_CLI = Path(__file__).parents[1] / "fixtures" / "fake_upstream_cli.py"


def _stop_manager(manager: ServiceManager) -> None:
    try:
        manager.stop()
    except ServiceError:
        pass


def test_config_commands_emit_stable_json(tmp_path: Path) -> None:
    runner = CliRunner()
    state = tmp_path / "state"
    result = runner.invoke(
        cli,
        ["--state-dir", str(state), "config", "set-endpoint", "http://127.0.0.1:18080"],
    )
    assert result.exit_code == 0
    payload = json.loads(result.output)
    assert payload["ok"] is True
    assert payload["endpoint"] == "http://127.0.0.1:18080"
    assert {
        "ok",
        "code",
        "message",
        "project_hash",
        "endpoint",
        "duration_ms",
        "result",
    } <= payload.keys()


def test_doctor_reports_editor_not_connected_without_guessing_settings(
    tmp_path: Path,
) -> None:
    project = create_unity_project(tmp_path / "Project")
    with fake_http_server() as endpoint:
        result = CliRunner().invoke(
            cli,
            [
                "--state-dir",
                str(tmp_path / "state"),
                "--endpoint",
                endpoint,
                "doctor",
                "--project",
                str(project),
            ],
        )
    assert result.exit_code == 0
    payload = json.loads(result.output)
    assert payload["result"]["diagnosis"] == "editor-not-connected"


def test_connect_and_call_use_verified_project_hash(
    monkeypatch, tmp_path: Path
) -> None:
    project = create_unity_project(tmp_path / "Project")
    state = tmp_path / "state"
    project_hash = unity_project_hash_candidate(project)
    instances = [
        {
            "hash": project_hash,
            "project": "Project",
            "unity_version": "2022.3.62f3",
            "connected_at": "now",
            "project_root": str(project),
        }
    ]
    monkeypatch.setenv("UMCP_TEST_MODE", "1")
    monkeypatch.setenv("UMCP_TEST_SERVER_SCRIPT", str(FAKE_SERVER))
    with fake_http_server(instances) as endpoint:
        settings = Settings.load(state, endpoint)
        try:
            runner = CliRunner()
            connect = runner.invoke(
                cli,
                [
                    "--state-dir",
                    str(state),
                    "--endpoint",
                    endpoint,
                    "connect",
                    "--project",
                    str(project),
                ],
            )
            assert connect.exit_code == 0, connect.output
            connect_payload = json.loads(connect.output)
            assert connect_payload["project_hash"] == project_hash
            assert connect_payload["result"]["bootstrap"]["mode"] == "existing-session"

            call = runner.invoke(
                cli,
                [
                    "--state-dir",
                    str(state),
                    "--endpoint",
                    endpoint,
                    "call",
                    "read_console",
                    "--project",
                    str(project),
                    "--params",
                    "{}",
                ],
            )
            assert call.exit_code == 0, call.output
            assert json.loads(call.output)["project_hash"] == project_hash
        finally:
            _stop_manager(ServiceManager(settings))


def test_connect_invokes_cli_bootstrap_without_editor_ui(
    monkeypatch, tmp_path: Path
) -> None:
    project = create_unity_project(tmp_path / "Project")
    project_hash = "abcdef0123456789"
    state = tmp_path / "state"

    def bootstrap(root, _settings, _client, _timeout, *, allow_editor_restart):
        assert root == project
        assert allow_editor_restart is True
        return (
            ResolvedProject(
                root=project,
                canonical_root=project.as_posix(),
                project_hash=project_hash,
                project_name="Project",
                unity_version="2022.3.62f3",
                connected_at="after-cleanup",
            ),
            BootstrapEvidence(
                mode="configured-editor-restart",
                editor_pid=1234,
                previous_editor_pid=1233,
                unity_version="2022.3.62f3",
                prefs_configured=True,
                restarted=True,
            ),
        )

    monkeypatch.setattr("unity_mcp_supervisor.cli.ensure_project_connection", bootstrap)
    with fake_http_server() as endpoint:
        settings = Settings.load(state, endpoint)
        try:
            result = CliRunner().invoke(
                cli,
                [
                    "--state-dir",
                    str(state),
                    "--endpoint",
                    endpoint,
                    "connect",
                    "--project",
                    str(project),
                    "--restart-editor",
                ],
            )
        finally:
            _stop_manager(ServiceManager(settings))
    assert result.exit_code == 0, result.output
    payload = json.loads(result.output)
    assert payload["project_hash"] == project_hash
    assert payload["result"]["bootstrap"]["restarted"] is True


def test_connect_reports_closed_editor_without_ui_fallback(tmp_path: Path) -> None:
    project = create_unity_project(tmp_path / "Project")
    state = tmp_path / "state"
    with fake_http_server() as endpoint:
        settings = Settings.load(state, endpoint)
        try:
            result = CliRunner().invoke(
                cli,
                [
                    "--state-dir",
                    str(state),
                    "--endpoint",
                    endpoint,
                    "connect",
                    "--project",
                    str(project),
                    "--timeout",
                    "1",
                ],
            )
        finally:
            _stop_manager(ServiceManager(settings))
    assert result.exit_code == 4
    payload = json.loads(result.output)
    assert payload["code"] == "editor_not_open"


def test_connect_restart_reports_missing_editor_record(tmp_path: Path) -> None:
    project = create_unity_project(tmp_path / "Project")
    state = tmp_path / "state"
    with fake_http_server() as endpoint:
        settings = Settings.load(state, endpoint)
        try:
            result = CliRunner().invoke(
                cli,
                [
                    "--state-dir",
                    str(state),
                    "--endpoint",
                    endpoint,
                    "connect",
                    "--project",
                    str(project),
                    "--restart-editor",
                    "--timeout",
                    "1",
                ],
            )
        finally:
            _stop_manager(ServiceManager(settings))
    assert result.exit_code == 4
    assert json.loads(result.output)["code"] == "editor_not_open"


def test_run_injects_verified_hash_into_pinned_upstream_cli(
    monkeypatch, tmp_path: Path
) -> None:
    project = create_unity_project(tmp_path / "Project")
    state = tmp_path / "state"
    capture = tmp_path / "capture.json"
    project_hash = unity_project_hash_candidate(project)
    instances = [
        {
            "hash": project_hash,
            "project": "Project",
            "unity_version": "2022.3.62f3",
            "connected_at": "now",
            "project_root": str(project),
        }
    ]
    monkeypatch.setenv("UMCP_TEST_MODE", "1")
    monkeypatch.setenv("UMCP_TEST_SERVER_SCRIPT", str(FAKE_SERVER))
    monkeypatch.setenv("UMCP_TEST_CLI_SCRIPT", str(FAKE_CLI))
    monkeypatch.setenv("UMCP_TEST_CLI_CAPTURE", str(capture))
    with fake_http_server(instances) as endpoint:
        settings = Settings.load(state, endpoint)
        try:
            result = CliRunner().invoke(
                cli,
                [
                    "--state-dir",
                    str(state),
                    "--endpoint",
                    endpoint,
                    "run",
                    "--project",
                    str(project),
                    "--",
                    "status",
                ],
            )
            assert result.exit_code == 0, result.output
            assert json.loads(result.output)["project_hash"] == project_hash
            assert (
                json.loads(capture.read_text(encoding="utf-8"))["instance"]
                == project_hash
            )
        finally:
            _stop_manager(ServiceManager(settings))


def test_lease_commands_emit_sanitized_state_and_release(tmp_path: Path) -> None:
    project = create_unity_project(tmp_path / "Project")
    state = tmp_path / "state"
    runner = CliRunner()

    acquired = runner.invoke(
        cli,
        [
            "--state-dir",
            str(state),
            "lease",
            "acquire",
            "--project",
            str(project),
            "--owner",
            "contract-test",
            "--wait",
            "0",
        ],
    )
    assert acquired.exit_code == 0, acquired.output
    acquired_payload = json.loads(acquired.output)
    lease_id = acquired_payload["result"]["lease_id"]

    status = runner.invoke(
        cli,
        ["--state-dir", str(state), "lease", "status", "--project", str(project)],
    )
    assert status.exit_code == 0, status.output
    status_payload = json.loads(status.output)
    assert status_payload["result"]["active"] is True
    assert status_payload["result"]["owner"] == "contract-test"
    assert "lease_id" not in status_payload["result"]

    released = runner.invoke(
        cli,
        [
            "--state-dir",
            str(state),
            "lease",
            "release",
            "--project",
            str(project),
            "--lease-id",
            lease_id,
        ],
    )
    assert released.exit_code == 0, released.output
    assert json.loads(released.output)["result"]["released"] is True


def test_active_lease_blocks_unclaimed_call_and_allows_owner(
    monkeypatch, tmp_path: Path
) -> None:
    project = create_unity_project(tmp_path / "Project")
    state = tmp_path / "state"
    project_hash = unity_project_hash_candidate(project)
    instances = [
        {
            "hash": project_hash,
            "project": "Project",
            "unity_version": "2022.3.62f3",
            "connected_at": "now",
            "project_root": str(project),
        }
    ]
    monkeypatch.setenv("UMCP_TEST_MODE", "1")
    monkeypatch.setenv("UMCP_TEST_SERVER_SCRIPT", str(FAKE_SERVER))
    runner = CliRunner()
    lease_id = ""

    with fake_http_server(instances) as endpoint:
        settings = Settings.load(state, endpoint)
        try:
            acquired = runner.invoke(
                cli,
                [
                    "--state-dir",
                    str(state),
                    "--endpoint",
                    endpoint,
                    "lease",
                    "acquire",
                    "--project",
                    str(project),
                    "--owner",
                    "contract-test",
                    "--wait",
                    "0",
                ],
            )
            lease_id = json.loads(acquired.output)["result"]["lease_id"]

            blocked = runner.invoke(
                cli,
                [
                    "--state-dir",
                    str(state),
                    "--endpoint",
                    endpoint,
                    "call",
                    "read_console",
                    "--project",
                    str(project),
                ],
            )
            assert blocked.exit_code == 5, blocked.output
            blocked_payload = json.loads(blocked.output)
            assert blocked_payload["code"] == "project_busy"
            assert blocked_payload["result"]["owner"] == "contract-test"
            assert "lease_id" not in blocked_payload["result"]

            allowed = runner.invoke(
                cli,
                [
                    "--state-dir",
                    str(state),
                    "--endpoint",
                    endpoint,
                    "call",
                    "read_console",
                    "--project",
                    str(project),
                    "--lease-id",
                    lease_id,
                ],
            )
            assert allowed.exit_code == 0, allowed.output
        finally:
            if lease_id:
                runner.invoke(
                    cli,
                    [
                        "--state-dir",
                        str(state),
                        "lease",
                        "release",
                        "--project",
                        str(project),
                        "--lease-id",
                        lease_id,
                    ],
                )
            _stop_manager(ServiceManager(settings))
