from __future__ import annotations

import json
import sys
from pathlib import Path

import pytest

from unity_mcp_supervisor import test_farm, test_worker
from unity_mcp_supervisor.service_state import StatePaths
from unity_mcp_supervisor.test_worker import build_unity_command, parse_test_results


def write_snapshot_manifest(path: Path, revision: str = "one") -> None:
    path.write_text(
        json.dumps(
            {
                "vcs": {"kind": "git", "revision": revision, "repository": "repo"},
                "critical_inputs": {},
                "overlay": [],
            }
        ),
        encoding="utf-8",
    )


def test_unity_command_is_filtered_and_externalizes_results(tmp_path: Path) -> None:
    job = {
        "platform": "EditMode",
        "filters": ["Tests.One", "Tests.Two"],
        "categories": ["Fast"],
        "assemblies": ["Tests.Editor"],
    }
    command = build_unity_command(
        Path("Unity.exe"), tmp_path / "project", job, tmp_path / "artifacts"
    )
    assert "-runTests" in command
    assert "-forgetProjectPath" in command
    assert "-quit" not in command
    assert command[command.index("-testFilter") + 1] == "Tests.One;Tests.Two"
    assert command[command.index("-testResults") + 1].startswith(
        str(tmp_path / "artifacts")
    )


def test_results_require_at_least_one_executed_passing_test(tmp_path: Path) -> None:
    passed = tmp_path / "passed.xml"
    passed.write_text(
        '<test-run result="Passed" total="1" passed="1" failed="0" />',
        encoding="utf-8",
    )
    empty = tmp_path / "empty.xml"
    empty.write_text(
        '<test-run result="Passed" total="0" passed="0" failed="0" />',
        encoding="utf-8",
    )
    assert parse_test_results(passed)["success"] is True
    assert parse_test_results(empty)["success"] is False


def test_first_slot_use_certifies_cold_and_warm_then_hits_cache(
    monkeypatch: pytest.MonkeyPatch, tmp_path: Path
) -> None:
    slot_root = tmp_path / "slot"
    project = slot_root / "project"
    (project / "ProjectSettings").mkdir(parents=True)
    (project / "ProjectSettings" / "ProjectVersion.txt").write_text(
        "m_EditorVersion: 2022.3.62f3\n", encoding="utf-8"
    )
    (project / "Library").mkdir()
    store = test_farm.TestFarmStore(StatePaths(tmp_path / "state"))
    job = {
        "job_id": "job-one",
        "project_root": str(tmp_path / "source"),
        "snapshot_manifest": str(tmp_path / "snapshot.json"),
        "artifact_root": str(tmp_path / "artifacts"),
        "timeout_seconds": 10,
    }
    write_snapshot_manifest(Path(job["snapshot_manifest"]))
    slot = {"root": str(slot_root)}
    calls: list[Path] = []

    def materialize(*_args, **_kwargs):
        (project / "Library").mkdir(exist_ok=True)
        return project

    def run(_store, _job, _project, artifact, **_kwargs):
        calls.append(artifact)
        return test_farm.WorkerResult(
            "passed",
            {
                "tests": {
                    "total": 1,
                    "passed": 1,
                    "success": True,
                    "cases": [{"name": "One", "result": "Passed"}],
                },
                "timings": {},
            },
        )

    monkeypatch.setattr(test_worker, "materialize_snapshot", materialize)
    monkeypatch.setattr(test_worker, "_run_materialized", run)
    first = test_worker.execute_job(store, job, slot)
    second = test_worker.execute_job(store, job, slot)
    assert first.state == "passed"
    assert first.summary["warm_cache"] == "certified"
    assert second.summary["warm_cache"] == "hit"
    assert calls == [
        tmp_path / "artifacts" / "cold",
        tmp_path / "artifacts" / "warm",
        tmp_path / "artifacts",
    ]

    write_snapshot_manifest(Path(job["snapshot_manifest"]), revision="two")
    third = test_worker.execute_job(store, job, slot)
    assert third.summary["warm_cache"] == "certified"
    assert calls[-2:] == [
        tmp_path / "artifacts" / "cold",
        tmp_path / "artifacts" / "warm",
    ]


def test_warm_mismatch_disables_cache_and_quarantines_slot(
    monkeypatch: pytest.MonkeyPatch, tmp_path: Path
) -> None:
    slot_root = tmp_path / "slot"
    project = slot_root / "project"
    (project / "ProjectSettings").mkdir(parents=True)
    (project / "ProjectSettings" / "ProjectVersion.txt").write_text(
        "m_EditorVersion: 2022.3.62f3\n", encoding="utf-8"
    )
    store = test_farm.TestFarmStore(StatePaths(tmp_path / "state"))
    job = {
        "job_id": "job-one",
        "project_root": str(tmp_path / "source"),
        "snapshot_manifest": str(tmp_path / "snapshot.json"),
        "artifact_root": str(tmp_path / "artifacts"),
        "timeout_seconds": 10,
    }
    write_snapshot_manifest(Path(job["snapshot_manifest"]))
    outcomes = iter(("passed", "failed"))

    monkeypatch.setattr(
        test_worker, "materialize_snapshot", lambda *_args, **_kwargs: project
    )

    def run(*_args, **_kwargs):
        state = next(outcomes)
        return test_farm.WorkerResult(
            state,
            {
                "tests": {"total": 1, "success": state == "passed"},
                "timings": {},
            },
        )

    monkeypatch.setattr(test_worker, "_run_materialized", run)
    result = test_worker.execute_job(store, job, {"root": str(slot_root)})
    assert result.state == "infra_failed"
    assert result.quarantine is True
    assert result.summary["warm_cache"] == "disabled"


def running_job(tmp_path: Path) -> tuple[test_farm.TestFarmStore, dict, Path]:
    project = tmp_path / "project"
    (project / "ProjectSettings").mkdir(parents=True)
    (project / "ProjectSettings" / "ProjectVersion.txt").write_text(
        "m_EditorVersion: 2022.3.62f3\n", encoding="utf-8"
    )
    store = test_farm.TestFarmStore(StatePaths(tmp_path / "state"))
    store.provision(1, tmp_path / "slots")
    store.submit(
        test_farm.TestJobRequest(
            project_root=str(project),
            task_id="task-one",
            platform="EditMode",
            filters=("Tests.One",),
            artifact_root=str(tmp_path / "artifacts"),
            snapshot_id="snapshot-one",
            snapshot_manifest=str(tmp_path / "snapshot.json"),
            timeout_seconds=1,
        )
    )
    return store, store.claim_next(worker_pid=1234), project


def test_process_without_results_classifies_license_failure(
    monkeypatch: pytest.MonkeyPatch, tmp_path: Path
) -> None:
    store, job, project = running_job(tmp_path)
    script = tmp_path / "license_failure.py"
    script.write_text(
        "from pathlib import Path\n"
        "import sys\n"
        "root=Path(sys.argv[1]); root.mkdir(parents=True, exist_ok=True)\n"
        "(root/'unity.log').write_text('LICENSE checkout failed', encoding='utf-8')\n",
        encoding="utf-8",
    )
    monkeypatch.setattr(test_worker, "resolve_unity", lambda _project: Path("Unity"))
    monkeypatch.setattr(
        test_worker,
        "build_unity_command",
        lambda _unity, _project, _job, artifact: [
            sys.executable,
            str(script),
            str(artifact),
        ],
    )
    monkeypatch.setattr(test_worker, "mutation_fingerprint", lambda _project: {})
    result = test_worker._run_materialized(
        store, job, project, tmp_path / "license-artifact", timeout_seconds=5
    )
    assert result.state == "infra_failed"
    assert result.summary["infrastructure"] == "license"
    assert result.quarantine is False


def test_timeout_terminates_own_process_and_quarantines(
    monkeypatch: pytest.MonkeyPatch, tmp_path: Path
) -> None:
    store, job, project = running_job(tmp_path)
    script = tmp_path / "timeout.py"
    script.write_text("import time\ntime.sleep(5)\n", encoding="utf-8")
    monkeypatch.setattr(test_worker, "resolve_unity", lambda _project: Path("Unity"))
    monkeypatch.setattr(
        test_worker,
        "build_unity_command",
        lambda *_args: [sys.executable, str(script)],
    )
    monkeypatch.setattr(test_worker, "mutation_fingerprint", lambda _project: {})
    result = test_worker._run_materialized(
        store, job, project, tmp_path / "timeout-artifact", timeout_seconds=0.1
    )
    assert result.state == "infra_failed"
    assert result.summary["timed_out"] is True
    assert result.quarantine is True
