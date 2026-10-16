from __future__ import annotations

import json
import sqlite3
import uuid
from contextlib import closing

import pytest
from test_cli import private_token_root  # noqa: F401

from unity_workspace_scheduler.cli import run as cli_run
from unity_workspace_scheduler.coordinator import WorkspaceCoordinator
from unity_workspace_scheduler.errors import SchedulerError
from unity_workspace_scheduler.operations import canonical_json
from unity_workspace_scheduler.state import resolve_state_paths
from unity_workspace_scheduler.state_ops import verify_state


@pytest.fixture
def case(request):
    root = request.getfixturevalue("private_token_root").resolve()
    project = root / "project"
    project.mkdir()
    scheduler = WorkspaceCoordinator(resolve_state_paths(root / "state"))
    scheduler.register(project)
    owner, token = scheduler.start_task(project, "owner", "retain pending source")
    source = project / "owned.cs"
    source.write_bytes(b"unsubmitted source\r\n")
    claim = scheduler.acquire_claim(project, token, writes=["owned.cs"])
    editor = scheduler.acquire_claim(project, token, resources=["unity-live"])
    if getattr(request, "param", None) != "active":
        scheduler.release_task(project, token, result="outcome-unknown")
    return scheduler, project, owner, token, claim, editor, source


def release(case, **changes):
    scheduler, project, owner, _, _, editor, _ = case
    args = {
        "operation_id": str(uuid.uuid4()),
        "evidence": "Exact native test terminal; two fresh cleanup observations.",
        "operator_confirmed": True,
    }
    args.update(changes)
    return scheduler.release_unknown_editor(project, owner["id"], editor["id"], **args)


def rows(scheduler, task_id):
    with closing(sqlite3.connect(scheduler.paths.database)) as db:
        task = db.execute("SELECT * FROM tasks WHERE id=?", (task_id,)).fetchone()
        claims = db.execute(
            "SELECT * FROM claims WHERE task_id=? ORDER BY id", (task_id,)
        ).fetchall()
        scopes = db.execute(
            "SELECT * FROM claim_scopes WHERE claim_id IN (SELECT id FROM claims WHERE task_id=?) ORDER BY claim_id,scope_type,value",
            (task_id,),
        ).fetchall()
        return task, claims, scopes


@pytest.mark.parametrize("case", ["active"], indirect=True)
def test_release_keeps_source_and_task_bytes_then_grants_fifo_and_replays(case):
    s, p, owner, token, source_claim, _editor, source = case
    _, first = s.start_task(p, "first", "Editor waiter")
    _, second = s.start_task(p, "second", "Editor waiter")
    waiting = s.acquire_claim(p, first, resources=["unity-live"], keep_queued=True)
    following = s.acquire_claim(p, second, resources=["unity-live"], keep_queued=True)
    source_wait = s.acquire_claim(p, second, writes=["owned.cs"], keep_queued=True)
    s.release_task(p, token, result="outcome-unknown")
    before = rows(s, owner["id"])
    operation = str(uuid.uuid4())
    result = release(case, operation_id=operation)
    after = rows(s, owner["id"])
    assert before[0] == after[0] and before[2] == after[2]
    assert next(c for c in before[1] if c[0] == source_claim["id"]) == next(
        c for c in after[1] if c[0] == source_claim["id"]
    )
    assert source.read_bytes() == b"unsubmitted source\r\n"
    assert result["retained_claim_ids"] == [source_claim["id"]]
    assert result["source_claims_released"] is False and result["task_state"] == "outcome_unknown"
    claims = {c["id"]: c for c in s.status(p)["claims"]}
    assert claims[waiting["id"]]["state"] == "active"
    assert claims[following["id"]]["state"] == claims[source_wait["id"]]["state"] == "queued"
    replay = release(case, operation_id=operation, receipt_only=True)
    assert replay["operation"]["replayed"] is True
    assert replay["released_at"] == result["released_at"] and rows(s, owner["id"]) == after
    assert verify_state(s.paths.database)["integrity_check"] == "ok"
    s.acknowledge_receipt(
        operation, replay["operation"]["fingerprint"], replay["operation"]["delivery_digest"]
    )
    s.release_claim(p, first, waiting["id"])
    assert next(c for c in s.status(p)["claims"] if c["id"] == following["id"])["state"] == "active"
    # A later ordinary full source recovery does not invalidate this historical receipt.
    s.resolve_unknown(
        p,
        owner["id"],
        resolution="failed",
        evidence="Synthetic source owner separately stopped; cleanup only.",
    )
    assert (
        release(case, operation_id=operation, receipt_only=True)["released_at"]
        == result["released_at"]
    )
    assert verify_state(s.paths.database)["integrity_check"] == "ok"


@pytest.mark.parametrize(
    "changes", [{"operator_confirmed": False}, {"evidence": ""}, {"evidence": "x\x00y"}]
)
def test_missing_operator_or_evidence_cannot_mutate(case, changes):
    before = rows(case[0], case[2]["id"])
    with pytest.raises(SchedulerError):
        release(case, **changes)
    assert rows(case[0], case[2]["id"]) == before


def test_wrong_task_workspace_claim_and_active_task_are_retained(case):
    s, p, owner, _token, source_claim, editor, _ = case
    other, secret = s.start_task(p, "other", "normal work")
    other_source = s.acquire_claim(p, secret, writes=["other.cs"])
    before = rows(s, owner["id"])
    for task_id, claim_id in [
        (other["id"], editor["id"]),
        (owner["id"], source_claim["id"]),
        (owner["id"], other_source["id"]),
    ]:
        with pytest.raises(SchedulerError):
            s.release_unknown_editor(
                p,
                task_id,
                claim_id,
                operation_id=str(uuid.uuid4()),
                evidence="terminal",
                operator_confirmed=True,
            )
    wrong = p.parent / "other-project"
    wrong.mkdir()
    s.register(wrong)
    with pytest.raises(SchedulerError):
        s.release_unknown_editor(
            wrong,
            owner["id"],
            editor["id"],
            operation_id=str(uuid.uuid4()),
            evidence="terminal",
            operator_confirmed=True,
        )
    assert rows(s, owner["id"]) == before
    assert s.identify_task(p, secret)["state"] == "active"


@pytest.mark.parametrize(
    "shape", ["combined-write", "other-resource", "freeze", "queued", "extra-resource"]
)
def test_unsupported_resource_scopes_do_not_release_anything(request, shape):
    root = request.getfixturevalue("private_token_root").resolve()
    p = root / "project"
    p.mkdir()
    s = WorkspaceCoordinator(resolve_state_paths(root / "state"))
    s.register(p)
    owner, token = s.start_task(p, "owner", "scope safety")
    s.acquire_claim(p, token, writes=["owned.cs"])
    if shape == "queued":
        _, blocker = s.start_task(p, "blocker", "normal active Editor")
        s.acquire_claim(p, blocker, resources=["unity-live"])
    selected = s.acquire_claim(
        p,
        token,
        resources=["unity-live", "vcs-maintenance"]
        if shape == "other-resource"
        else []
        if shape == "freeze"
        else ["unity-live"],
        writes=["mixed.cs"] if shape == "combined-write" else [],
        freeze=shape == "freeze",
        keep_queued=True,
    )
    if shape == "extra-resource":
        s.acquire_claim(p, token, resources=["vcs-maintenance"])
    s.release_task(p, token, result="outcome-unknown")
    before = rows(s, owner["id"])
    with pytest.raises(SchedulerError):
        s.release_unknown_editor(
            p,
            owner["id"],
            selected["id"],
            operation_id=str(uuid.uuid4()),
            evidence="terminal",
            operator_confirmed=True,
        )
    assert rows(s, owner["id"]) == before


def test_schedule_failure_rolls_back_release_and_no_receipt_is_created(case, monkeypatch):
    s, _, owner, *_ = case
    before = rows(s, owner["id"])

    def fail(*args):
        raise RuntimeError("synthetic scheduling failure")

    monkeypatch.setattr(s, "_schedule_workspace", fail)
    with pytest.raises(RuntimeError):
        release(case)
    assert rows(s, owner["id"]) == before


def test_cli_same_request_replays_and_offline_state_remains_valid(case, capsys):
    s, p, owner, _, _, editor, _ = case
    op = str(uuid.uuid4())
    args = [
        "--state-dir",
        str(s.paths.root),
        "recovery",
        "release-editor",
        "--workspace",
        str(p),
        "--task-id",
        owner["id"],
        "--claim-id",
        editor["id"],
        "--evidence",
        "Exact native terminal and cleanup.",
        "--operator-confirmed",
        "--operation-id",
        op,
    ]
    assert cli_run(args) == 0
    first = json.loads(capsys.readouterr().out)
    assert cli_run([*args, "--receipt-only"]) == 0
    second = json.loads(capsys.readouterr().out)
    assert second["result"]["operation"]["replayed"] is True
    assert second["result"]["released_at"] == first["result"]["released_at"]
    operation = second["result"]["operation"]
    assert (
        cli_run(
            [
                "--state-dir",
                str(s.paths.root),
                "receipt",
                "ack",
                "--operation-id",
                op,
                "--fingerprint",
                operation["fingerprint"],
                "--delivery-digest",
                operation["delivery_digest"],
            ]
        )
        == 0
    )
    capsys.readouterr()
    assert verify_state(s.paths.database)["integrity_check"] == "ok"


@pytest.mark.parametrize("case", ["active"], indirect=True)
def test_expired_queued_peer_is_not_granted_a_new_editor_fence(case, monkeypatch):
    from unity_workspace_scheduler import coordinator

    s, p, owner, token, source_claim, _, _ = case
    expired, expired_token = s.start_task(p, "expired", "past admission TTL", ttl_seconds=1)
    _, following_token = s.start_task(p, "following", "valid next waiter")
    expired_claim = s.acquire_claim(p, expired_token, resources=["unity-live"], keep_queued=True)
    following = s.acquire_claim(p, following_token, resources=["unity-live"], keep_queued=True)
    s.release_task(p, token, result="outcome-unknown")
    before = rows(s, owner["id"])
    monkeypatch.setattr(coordinator.time, "time", lambda: expired["expires_at"] + 1)
    release(case)
    with closing(sqlite3.connect(s.paths.database)) as db:
        assert (
            db.execute("SELECT state FROM claims WHERE id=?", (expired_claim["id"],)).fetchone()[0]
            == "cancelled"
        )
        assert (
            db.execute("SELECT state FROM tasks WHERE id=?", (expired["id"],)).fetchone()[0]
            == "expired"
        )
        assert (
            db.execute("SELECT state FROM claims WHERE id=?", (following["id"],)).fetchone()[0]
            == "active"
        )
    after = rows(s, owner["id"])
    assert before[0] == after[0] and before[2] == after[2]
    assert next(c for c in before[1] if c[0] == source_claim["id"]) == next(
        c for c in after[1] if c[0] == source_claim["id"]
    )
    assert verify_state(s.paths.database)["integrity_check"] == "ok"


@pytest.mark.parametrize("case", ["active"], indirect=True)
def test_receipt_replay_does_not_maintain_the_already_admitted_peer(case, monkeypatch):
    from unity_workspace_scheduler import coordinator

    s, p, _, token, _, _, _ = case
    peer, secret = s.start_task(p, "peer", "new Editor owner", ttl_seconds=1)
    s.acquire_claim(p, secret, resources=["unity-live"], keep_queued=True)
    s.release_task(p, token, result="outcome-unknown")
    operation = str(uuid.uuid4())
    original = release(case, operation_id=operation)
    before = rows(s, peer["id"])
    monkeypatch.setattr(coordinator.time, "time", lambda: peer["expires_at"] + 1)
    replay = release(case, operation_id=operation, receipt_only=True)
    assert replay["released_at"] == original["released_at"]
    assert rows(s, peer["id"]) == before


@pytest.mark.parametrize(
    "field,value",
    [
        ("source_claims_released", True),
        ("task_state", "completed"),
        ("retained_claim_ids", []),
        ("claim_id", "a" * 32),
        ("evidence_sha256", "0" * 64),
    ],
)
def test_offline_verifier_and_replay_reject_forged_release_receipt(case, field, value):
    s = case[0]
    op = str(uuid.uuid4())
    release(case, operation_id=op)
    with closing(sqlite3.connect(s.paths.database)) as db:
        stored = json.loads(
            db.execute(
                "SELECT result_json FROM operation_receipts WHERE operation_id=?", (op,)
            ).fetchone()[0]
        )
        stored[field] = value
        db.execute(
            "UPDATE operation_receipts SET result_json=? WHERE operation_id=?",
            (canonical_json(stored), op),
        )
        db.commit()
    with pytest.raises(SchedulerError):
        verify_state(s.paths.database)
    with pytest.raises(SchedulerError):
        release(case, operation_id=op, receipt_only=True)
