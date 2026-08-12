from __future__ import annotations

import json
import os
import stat
import subprocess
import sys
from pathlib import Path

from unity_workspace_scheduler.cli import run


def read_output(capsys) -> dict[str, object]:
    return json.loads(capsys.readouterr().out)


def test_cli_task_flow_uses_private_token_file_and_json_contract(tmp_path: Path, capsys) -> None:
    state = tmp_path / "state"
    workspace = tmp_path / "workspace"
    token_file = tmp_path / "tokens" / "task.token"
    workspace.mkdir()

    assert (
        run(["--state-dir", str(state), "workspace", "register", "--workspace", str(workspace)])
        == 0
    )
    assert read_output(capsys)["ok"] is True
    assert (
        run(
            [
                "--state-dir",
                str(state),
                "task",
                "start",
                "--workspace",
                str(workspace),
                "--owner",
                "cli-test",
                "--summary",
                "CLI contract",
                "--token-file",
                str(token_file),
            ]
        )
        == 0
    )
    started = read_output(capsys)
    token = token_file.read_text(encoding="utf-8").strip()
    assert token
    assert token not in json.dumps(started)
    if os.name != "nt":
        assert stat.S_IMODE(token_file.stat().st_mode) == 0o600

    assert (
        run(
            [
                "--state-dir",
                str(state),
                "claim",
                "acquire",
                "--workspace",
                str(workspace),
                "--resource",
                "unity-live",
                "--token-file",
                str(token_file),
            ]
        )
        == 0
    )
    claim = read_output(capsys)
    assert claim["result"]["granted"] is True

    assert (
        run(
            [
                "--state-dir",
                str(state),
                "task",
                "release",
                "--workspace",
                str(workspace),
                "--result",
                "completed",
                "--token-file",
                str(token_file),
            ]
        )
        == 0
    )
    released = read_output(capsys)
    assert released["result"]["token_file_removed"] is True
    assert not token_file.exists()


def test_cli_reports_unregistered_workspace_without_traceback(tmp_path: Path, capsys) -> None:
    workspace = tmp_path / "workspace"
    workspace.mkdir()
    exit_code = run(
        [
            "--state-dir",
            str(tmp_path / "state"),
            "workspace",
            "status",
            "--workspace",
            str(workspace),
        ]
    )
    payload = read_output(capsys)
    assert exit_code == 5
    assert payload["ok"] is False
    assert payload["code"] == "workspace-state-invalid"


def _subprocess_json(arguments: list[str], env: dict[str, str]) -> dict[str, object]:
    completed = subprocess.run(
        [sys.executable, "-m", "unity_workspace_scheduler", *arguments],
        check=True,
        capture_output=True,
        text=True,
        env=env,
    )
    return json.loads(completed.stdout)


def test_two_cli_processes_serialize_conflicting_claims(tmp_path: Path) -> None:
    state = tmp_path / "state"
    workspace = tmp_path / "workspace"
    workspace.mkdir()
    env = os.environ.copy()
    env["UNITY_SCHEDULER_STATE_DIR"] = str(state)
    _subprocess_json(["workspace", "register", "--workspace", str(workspace)], env)
    token_files = [tmp_path / "one.token", tmp_path / "two.token"]
    for index, token_file in enumerate(token_files):
        _subprocess_json(
            [
                "task",
                "start",
                "--workspace",
                str(workspace),
                "--owner",
                f"process-{index}",
                "--summary",
                "Concurrent claim",
                "--token-file",
                str(token_file),
            ],
            env,
        )

    processes = [
        subprocess.Popen(
            [
                sys.executable,
                "-m",
                "unity_workspace_scheduler",
                "claim",
                "acquire",
                "--workspace",
                str(workspace),
                "--resource",
                "unity-live",
                "--token-file",
                str(token_file),
            ],
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
            env=env,
        )
        for token_file in token_files
    ]
    payloads = []
    for process in processes:
        stdout, stderr = process.communicate(timeout=20)
        assert process.returncode == 0, stderr
        payloads.append(json.loads(stdout))
    assert sorted(payload["result"]["state"] for payload in payloads) == [
        "active",
        "queued",
    ]
