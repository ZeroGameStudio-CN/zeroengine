"""Private terminal receipts may be delivered after their token was lost."""

import os
import uuid
from pathlib import Path

import pytest

from unity_workspace_scheduler.coordinator import WorkspaceCoordinator
from unity_workspace_scheduler.errors import StateError, UsageError
from unity_workspace_scheduler.state import open_database, resolve_state_paths


def snapshot(coordinator):
    with open_database(coordinator.paths) as db:
        return "\n".join(db.iterdump())


@pytest.mark.parametrize("result", ["completed", "failed"])
@pytest.mark.parametrize("fault", [None, "note", "path", "task", "cleanup", "claim"])
def test_lost_token_before_ack_requires_exact_terminal_custody(tmp_path, result, fault):
    project = tmp_path / "project"
    project.mkdir()
    c = WorkspaceCoordinator(resolve_state_paths(tmp_path / "state"))
    c.register(project)
    path = Path(os.path.normpath(str((tmp_path / "owner.token").resolve())))
    task, token = c.start_task(
        project, "original-owner", "original scope", token_file_path=str(path)
    )
    path.write_text("private fixture only")
    op = str(uuid.uuid4())
    released = c.release_task(
        project,
        token,
        operation_id=op,
        result=result,
        note="original note",
        token_cleanup_path=str(path),
    )
    path.unlink()
    if fault in {"task", "cleanup", "claim"}:
        with open_database(c.paths) as db:
            if fault == "task":
                db.execute("UPDATE tasks SET finished_at=finished_at+1 WHERE id=?", (task["id"],))
            elif fault == "cleanup":
                db.execute(
                    "UPDATE operation_receipts SET token_cleanup_identity=? WHERE operation_id=?",
                    ("0" * 64, op),
                )
            else:
                # A corrupted live claim must never be hidden by terminal task state.
                _other, other_token = c.start_task(project, "other", "other scope")
                claim = c.acquire_claim(project, other_token, writes=["Assets/Preserve.cs"])
                db.execute("UPDATE claims SET task_id=? WHERE id=?", (task["id"], claim["id"]))
            db.commit()
    before = snapshot(c)
    args = {
        "operation_id": op,
        "result": result,
        "note": "changed" if fault == "note" else "original note",
        "token_cleanup_path": str(path if fault != "path" else tmp_path / "wrong.token"),
    }
    if fault:
        with pytest.raises((StateError, UsageError)):
            c.replay_terminal_task_release_without_token(project, **args)
        assert snapshot(c) == before
        return
    replay = c.replay_terminal_task_release_without_token(project, **args)
    assert snapshot(c) == before
    assert replay["finished_at"] == released["finished_at"]
    assert replay["operation"]["replayed"] is True
    assert replay["operation"]["delivered"] is False
    assert replay["token_cleanup_pending"] is True
    receipt = replay["operation"]
    c.acknowledge_receipt(op, receipt["fingerprint"], receipt["delivery_digest"])
    after = c.replay_terminal_task_release_without_token(project, **args)
    assert after["finished_at"] == released["finished_at"]
    assert after["operation"]["delivered"] is True
    assert after["token_cleanup_pending"] is False
    assert not path.exists()
    assert not any(r["operation_id"] == op for r in c.maintenance_receipts(project)["receipts"])
