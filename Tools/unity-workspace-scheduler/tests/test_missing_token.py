from __future__ import annotations

import copy
import hashlib
import json
import time
import uuid
from pathlib import Path

import pytest
from test_cli import private_token_root  # noqa: F401 - pytest discovers the imported fixture

from unity_workspace_scheduler.coordinator import WorkspaceCoordinator
from unity_workspace_scheduler.errors import SchedulerError
from unity_workspace_scheduler.state import resolve_state_paths
from unity_workspace_scheduler.state_ops import verify_state


@pytest.fixture
def case(request):
    tmp_path = request.getfixturevalue("private_token_root").resolve()
    scheduler = WorkspaceCoordinator(resolve_state_paths(tmp_path / "state"))
    root = tmp_path / "project"
    root.mkdir()
    scheduler.register(root)
    start_id = str(uuid.uuid4())
    # Direct SDK calls bind synthetic token lineage without creating credentials.
    token_path = root / "lost.token"
    task, token = scheduler.start_task(
        root, "owner", "test", token_file_path=str(token_path), operation_id=start_id
    )
    source = root / "source.txt"
    source.write_text("pending work")
    backup = tmp_path / "source.backup"
    backup.write_bytes(source.read_bytes())
    claim = scheduler.acquire_claim(root, token, writes=["source.txt"])
    native = tmp_path / "terminal.json"
    native.write_text(json.dumps({"state": "terminal", "failed": 1}))
    digest = hashlib.sha256(native.read_bytes()).hexdigest()
    reports = []
    for kind in ("clients", "native", "writers"):
        report = {
            "version": 1,
            "workspace": str(root),
            "task_id": task["id"],
            "start_operation_id": start_id,
            "kind": kind,
            "terminal": True,
            "observed_at": time.time(),
            "references": [{"path": str(native), "sha256": digest}],
        }
        if kind == "clients":
            report["clients"] = [{"id": "original-client", "exit_code": 1}]
        elif kind == "writers":
            report.update(
                stopped=True,
                detached_writers=False,
                writes_paused=True,
                coordinator_calls_paused=True,
            )
        else:
            report.update(
                state="completed",
                executor_id="synthetic-native",
                artifact={"path": str(native), "sha256": digest},
                starter={"path": str(native), "sha256": digest},
            )
        path = tmp_path / (kind + ".json")
        path.write_text(json.dumps(report))
        reports.append(
            {
                "kind": kind,
                "state": "terminal",
                "path": str(path),
                "sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
            }
        )
    evidence = {
        "version": 1,
        "workspace": str(root),
        "task_id": task["id"],
        "owner": "owner",
        "start_operation_id": start_id,
        "token_path": str(token_path),
        "claim_ids": [claim["id"]],
        "terminal_evidence": reports,
        "absent_sources": [],
        "preserved_sources": [
            {
                "path": str(source),
                "backup": str(backup),
                "sha256": hashlib.sha256(source.read_bytes()).hexdigest(),
            }
        ],
        "unavailable_evidence": ["Original full input snapshot was lost; not recreated."],
        "operator_note": "Terminal infrastructure phase, not a product pass.",
    }
    return scheduler, root, task, token, claim, evidence


def fence(case, evidence=None, **kwargs):
    scheduler, root, task, _, _, original = case
    return scheduler.fence_missing_token(
        root,
        task["id"],
        evidence=evidence or original,
        operation_id=kwargs.pop("operation_id", str(uuid.uuid4())),
        operator_confirmed=kwargs.pop("operator_confirmed", True),
        **kwargs,
    )


def test_fence_retains_claims_then_normal_recovery_and_lost_reply(case):
    scheduler, root, task, _, claim, evidence = case
    other, secret = scheduler.start_task(root, "other", "untouched")
    other_claim = scheduler.acquire_claim(root, secret, writes=["other.txt"])
    with pytest.raises(SchedulerError):
        scheduler.resolve_unknown(root, task["id"], resolution="failed", evidence="active refused")
    operation_id = str(uuid.uuid4())
    result = fence(case, operation_id=operation_id)
    assert result["claims_released"] is False
    current = {c["id"]: c for c in scheduler.status(root)["claims"]}
    assert current[claim["id"]]["state"] == "active"
    assert current[other_claim["id"]]["state"] == "active"
    verify_state(scheduler.paths.database)
    scheduler.resolve_unknown(root, task["id"], resolution="failed", evidence="native terminal")
    # Files may disappear after recovery; exact durable replay must still work.
    Path(evidence["terminal_evidence"][0]["path"]).unlink()
    replay = fence(case, operation_id=operation_id, receipt_only=True)
    assert replay["operation"]["replayed"] is True
    assert replay["retained_claim_ids"] == result["retained_claim_ids"]
    assert scheduler.identify_task(root, secret)["id"] == other["id"]
    verify_state(scheduler.paths.database)


@pytest.mark.parametrize(
    "mutation",
    [
        "workspace",
        "task_id",
        "owner",
        "start_operation_id",
        "token_path",
        "claim_ids",
        "token_exists",
        "clients_running",
        "native_running",
        "writers_running",
        "source_changed",
        "backup_changed",
        "evidence_changed",
        "source_absent",
        "not_confirmed",
    ],
)
def test_wrong_scope_or_uncertain_work_never_fences(case, mutation):
    scheduler, root, _task, token, _, evidence = case
    changed = copy.deepcopy(evidence)
    kwargs = {}
    if mutation in {"workspace", "token_path"}:
        changed[mutation] += "-wrong"
    elif mutation in {"task_id", "owner"}:
        changed[mutation] = "wrong"
    elif mutation == "start_operation_id":
        changed[mutation] = str(uuid.uuid4())
    elif mutation == "claim_ids":
        changed[mutation] = ["unknown"]
    elif mutation == "token_exists":
        Path(evidence["token_path"]).write_text("never read this")
    elif mutation.endswith("_running"):
        kind = mutation.removesuffix("_running")
        next(x for x in changed["terminal_evidence"] if x["kind"] == kind)["state"] = "running"
    elif mutation in {"source_changed", "backup_changed"}:
        key = "path" if mutation == "source_changed" else "backup"
        Path(changed["preserved_sources"][0][key]).write_text("changed")
    elif mutation == "evidence_changed":
        Path(changed["terminal_evidence"][0]["path"]).write_text("changed")
    elif mutation == "source_absent":
        changed["preserved_sources"] = []
    else:
        kwargs["operator_confirmed"] = False
    with pytest.raises(SchedulerError):
        fence(case, changed, **kwargs)
    assert scheduler.identify_task(root, token)["state"] == "active"
    verify_state(scheduler.paths.database)


def test_duplicate_operation_changed_evidence_is_refused(case):
    operation_id = str(uuid.uuid4())
    fence(case, operation_id=operation_id)
    changed = copy.deepcopy(case[-1])
    changed["operator_note"] = "different intent"
    with pytest.raises(SchedulerError):
        fence(case, changed, operation_id=operation_id)


def _change_report(evidence, kind, change):
    record = next(x for x in evidence["terminal_evidence"] if x["kind"] == kind)
    path = Path(record["path"])
    report = json.loads(path.read_text())
    change(report)
    path.write_text(json.dumps(report))
    record["sha256"] = hashlib.sha256(path.read_bytes()).hexdigest()


@pytest.mark.parametrize(
    "kind,change",
    [
        ("clients", lambda p: p["clients"][0].update(exit_code=None)),
        ("native", lambda p: p.update(state="running")),
        ("native", lambda p: p.update(task_id="another-task")),
        ("writers", lambda p: p.update(detached_writers=True)),
        ("writers", lambda p: p.update(writes_paused=False)),
        ("clients", lambda p: p.update(observed_at=1)),
    ],
)
def test_actual_report_content_cannot_be_hidden_by_matching_hash(case, kind, change):
    evidence = copy.deepcopy(case[-1])
    _change_report(evidence, kind, change)
    with pytest.raises(SchedulerError):
        fence(case, evidence)
    assert case[0].identify_task(case[1], case[3])["state"] == "active"


@pytest.mark.parametrize("mistake", ["missing-claim", "extra-source", "internal-backup"])
def test_source_coverage_is_exact_and_backups_are_external(case, mistake):
    scheduler, root, _, token, _, original = case
    evidence = copy.deepcopy(original)
    if mistake == "missing-claim":
        scheduler.acquire_claim(root, token, writes=["second.txt"])
    elif mistake == "extra-source":
        extra = root / "unclaimed.txt"
        extra.write_text("unclaimed")
        backup = root.parent / "unclaimed.backup"
        backup.write_bytes(extra.read_bytes())
        evidence["preserved_sources"].append(
            {
                "path": str(extra),
                "backup": str(backup),
                "sha256": hashlib.sha256(extra.read_bytes()).hexdigest(),
            }
        )
    else:
        backup = root / "internal.backup"
        backup.write_bytes(Path(evidence["preserved_sources"][0]["path"]).read_bytes())
        evidence["preserved_sources"][0]["backup"] = str(backup)
    evidence["claim_ids"] = sorted(c["id"] for c in scheduler.status(root)["claims"])
    with pytest.raises(SchedulerError):
        fence(case, evidence)
    assert scheduler.identify_task(root, token)["state"] == "active"


@pytest.mark.parametrize("appeared", [False, True])
def test_owned_absent_scratch_is_bound_without_inventing_a_backup(case, appeared):
    scheduler, root, _, token, _, original = case
    scratch = root / "scratch.txt"
    claim = scheduler.acquire_claim(root, token, writes=["scratch.txt"])
    evidence = copy.deepcopy(original)
    evidence["claim_ids"] = sorted([*evidence["claim_ids"], claim["id"]])
    evidence["absent_sources"] = [str(scratch)]
    if appeared:
        scratch.write_text("changed")
        with pytest.raises(SchedulerError):
            fence(case, evidence)
        assert scheduler.identify_task(root, token)["state"] == "active"
    else:
        assert fence(case, evidence)["claims_released"] is False
        verify_state(scheduler.paths.database)


def test_cli_requires_manifest_digest_and_confirmation_before_fencing(case, capsys):
    from unity_workspace_scheduler.cli import run

    scheduler, root, task, token, _, evidence = case
    manifest = root.parent / "manifest.json"
    manifest.write_text(json.dumps(evidence))
    command = [
        "--state-dir",
        str(scheduler.paths.root),
        "recovery",
        "fence-missing-token",
        "--workspace",
        str(root),
        "--task-id",
        task["id"],
        "--operation-id",
        str(uuid.uuid4()),
        "--evidence-file",
        str(manifest),
        "--evidence-sha256",
        "0" * 64,
        "--operator-confirmed",
    ]
    assert run(command) != 0
    assert json.loads(capsys.readouterr().out)["ok"] is False
    assert scheduler.identify_task(root, token)["state"] == "active"
    command[-2] = hashlib.sha256(manifest.read_bytes()).hexdigest()
    assert run(command[:-1]) != 0
    assert json.loads(capsys.readouterr().out)["ok"] is False
    assert run(command) == 0
    fenced = json.loads(capsys.readouterr().out)
    assert fenced["result"]["claims_released"] is False
    assert fenced["result"]["state"] == "outcome_unknown"
    assert run(command + ["--receipt-only"]) == 0
    assert json.loads(capsys.readouterr().out)["result"]["operation"]["replayed"] is True


@pytest.mark.parametrize("field", ["operation", "token_cleanup_pending"])
def test_fence_stored_result_retains_shared_delivery_metadata_guard(case, field):
    from unity_workspace_scheduler.missing_token import ACTION
    from unity_workspace_scheduler.state_ops import _validate_receipt_result

    result = fence(case)
    stored = {key: value for key, value in result.items() if key != "operation"}
    stored[field] = {} if field == "operation" else False
    with pytest.raises(ValueError):
        _validate_receipt_result(ACTION, {"task_id": case[2]["id"], "evidence": case[-1]}, stored)
