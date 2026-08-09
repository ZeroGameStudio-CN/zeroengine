from __future__ import annotations

import json
import os
import sqlite3
import subprocess
import time
from pathlib import Path

import pytest

from unity_mcp_supervisor.errors import IncompatibleError, ProjectBusyError, UsageError
from unity_mcp_supervisor.project_lease import inspect_project_lease
from unity_mcp_supervisor.project_resolver import canonical_project_root
from unity_mcp_supervisor.service_state import StatePaths
from unity_mcp_supervisor.workspace_control import (
    WorkspaceCoordinator,
    load_workspace_policy,
)


def _project(tmp_path: Path, *, enforcement: str = "audit") -> Path:
    project = tmp_path / "Project"
    (project / "Assets").mkdir(parents=True)
    (project / "ProjectSettings").mkdir()
    (project / "ProjectSettings" / "ProjectVersion.txt").write_text(
        "m_EditorVersion: 2022.3.62f3\n", encoding="utf-8"
    )
    coordination = project / "Tools" / "Coordination"
    coordination.mkdir(parents=True)
    (coordination / "workspace-control.json").write_text(
        "{\n"
        '  "schemaVersion": 1,\n'
        f'  "enforcement": "{enforcement}",\n'
        '  "unityMetaPairing": true\n'
        "}\n",
        encoding="utf-8",
    )
    return project


def _coordinator(tmp_path: Path, project: Path) -> WorkspaceCoordinator:
    return WorkspaceCoordinator(
        StatePaths(tmp_path / "state"),
        project,
        canonical_project_root(project),
        lease_ttl_seconds=30,
    )


def _task(coordinator: WorkspaceCoordinator, owner: str, *, ttl: float = 30) -> dict:
    return coordinator.start_task(
        owner=owner,
        summary=f"{owner} work",
        task_uri=f"codex://{owner}",
        ttl_seconds=ttl,
    )


def test_non_overlapping_claims_run_and_overlapping_claims_queue_fifo(
    tmp_path: Path,
) -> None:
    project = _project(tmp_path)
    coordinator = _coordinator(tmp_path, project)
    first = _task(coordinator, "first")
    second = _task(coordinator, "second")

    first_claim = coordinator.acquire_claim(first["task_token"], writes=("Assets/A",))
    independent = coordinator.acquire_claim(second["task_token"], writes=("Assets/B",))
    queued = coordinator.acquire_claim(
        second["task_token"],
        writes=("Assets/A/Child.asset",),
        keep_queued=True,
    )

    assert first_claim["state"] == "granted"
    assert independent["state"] == "granted"
    assert queued["state"] == "queued"
    assert queued["blocked_by"]["claim_id"] == first_claim["claim_id"]

    coordinator.release_claim(first["task_token"], first_claim["claim_id"])
    granted = coordinator.acquire_claim(
        second["task_token"], writes=("Assets/A/Child.asset",)
    )
    assert granted["claim_id"] == queued["claim_id"]
    assert granted["state"] == "granted"


def test_unity_asset_and_meta_are_one_conflict_unit(tmp_path: Path) -> None:
    project = _project(tmp_path)
    coordinator = _coordinator(tmp_path, project)
    first = _task(coordinator, "first")
    second = _task(coordinator, "second")

    asset = coordinator.acquire_claim(
        first["task_token"], writes=("Assets/Thing.asset",)
    )
    meta = coordinator.acquire_claim(
        second["task_token"],
        writes=("Assets/Thing.asset.meta",),
        keep_queued=True,
    )

    assert asset["write"] == ["assets/thing.asset", "assets/thing.asset.meta"]
    assert meta["state"] == "queued"
    assert meta["blocked_by"]["claim_id"] == asset["claim_id"]


def test_freeze_is_fair_barrier_rebinds_owner_and_fences_old_tasks(
    tmp_path: Path,
) -> None:
    project = _project(tmp_path)
    coordinator = _coordinator(tmp_path, project)
    writer = _task(coordinator, "writer")
    freezer = _task(coordinator, "freezer")
    newcomer = _task(coordinator, "newcomer")
    writer_claim = coordinator.acquire_claim(writer["task_token"], writes=("Assets/A",))

    queued_freeze = coordinator.acquire_claim(
        freezer["task_token"], freeze=True, keep_queued=True
    )
    assert queued_freeze["state"] == "queued"
    with pytest.raises(ProjectBusyError, match="freeze"):
        coordinator.acquire_claim(newcomer["task_token"], writes=("Assets/Unrelated",))

    coordinator.release_claim(writer["task_token"], writer_claim["claim_id"])
    freeze = coordinator.acquire_claim(freezer["task_token"], freeze=True)
    assert freeze["state"] == "granted"
    assert freeze["epoch"] == writer["epoch"] + 1
    maintenance = coordinator.acquire_claim(
        freezer["task_token"], resources=("vcs-maintenance",)
    )
    assert maintenance["state"] == "granted"
    with pytest.raises(ProjectBusyError, match="fenced"):
        coordinator.assert_claims(writer["task_token"], writes=("Assets/A",))


def test_wait_timeout_cancels_unless_keep_queued(tmp_path: Path) -> None:
    project = _project(tmp_path)
    coordinator = _coordinator(tmp_path, project)
    first = _task(coordinator, "first")
    second = _task(coordinator, "second")
    coordinator.acquire_claim(first["task_token"], writes=("Assets/A",))

    timed_out = coordinator.acquire_claim(
        second["task_token"], writes=("Assets/A",), wait_seconds=0.01
    )
    assert timed_out["state"] == "cancelled"
    assert all(
        claim["claim_id"] != timed_out["claim_id"]
        for claim in coordinator.status()["claims"]
    )


def test_unknown_outcome_expires_to_hard_block_until_evidence_recovery(
    tmp_path: Path,
) -> None:
    project = _project(tmp_path)
    coordinator = _coordinator(tmp_path, project)
    uncertain = _task(coordinator, "uncertain", ttl=5)
    waiting = _task(coordinator, "waiting")
    held = coordinator.acquire_claim(uncertain["task_token"], writes=("Assets/A",))
    coordinator.heartbeat(
        uncertain["task_token"],
        phase="outcome_unknown",
        note="write response lost",
        ttl_seconds=5,
    )
    with pytest.raises(ProjectBusyError, match="evidence-backed"):
        coordinator.release_claim(uncertain["task_token"], held["claim_id"])
    with pytest.raises(ProjectBusyError, match="evidence-backed"):
        coordinator.release_task(uncertain["task_token"], result="failed")
    coordinator.heartbeat(
        uncertain["task_token"],
        phase="outcome_unknown",
        note="write response lost",
        ttl_seconds=0.05,
    )
    time.sleep(0.06)

    status = coordinator.status()
    task = next(
        item for item in status["tasks"] if item["task_id"] == uncertain["task_id"]
    )
    assert task["state"] == "orphaned_unknown"
    blocked = coordinator.acquire_claim(
        waiting["task_token"], writes=("Assets/A",), keep_queued=True
    )
    assert blocked["blocked_by"]["task_state"] == "orphaned_unknown"

    recovery = coordinator.resolve_unknown(
        task_id=uncertain["task_id"],
        disposition="contained",
        evidence="process exited; diff stored at evidence-1",
    )
    assert recovery["new_epoch"] == held["epoch"] + 1


def test_required_unity_claim_binds_private_legacy_lease(tmp_path: Path) -> None:
    project = _project(tmp_path, enforcement="required")
    coordinator = _coordinator(tmp_path, project)
    task = _task(coordinator, "unity")

    claim = coordinator.acquire_claim(task["task_token"], resources=("unity-live",))
    lease = inspect_project_lease(coordinator.paths, coordinator.canonical_project_root)
    assertion = coordinator.assert_claims(task["task_token"], resources=("unity-live",))

    assert claim["state"] == "granted"
    assert lease is not None
    assert assertion["legacy_lease_id"] == lease.lease_id
    public = coordinator.status()
    serialized = str(public)
    assert task["task_token"] not in serialized
    assert lease.lease_id not in serialized


def test_released_or_wrong_token_cannot_assert(tmp_path: Path) -> None:
    project = _project(tmp_path)
    coordinator = _coordinator(tmp_path, project)
    task = _task(coordinator, "owner")
    coordinator.acquire_claim(task["task_token"], writes=("Assets/A",))

    with pytest.raises(UsageError):
        coordinator.assert_claims("wrong-token", writes=("Assets/A",))
    coordinator.release_task(task["task_token"], result="completed")
    with pytest.raises(UsageError):
        coordinator.assert_claims(task["task_token"], writes=("Assets/A",))


def test_file_claim_does_not_authorize_ancestor_write(tmp_path: Path) -> None:
    project = _project(tmp_path)
    coordinator = _coordinator(tmp_path, project)
    task = _task(coordinator, "owner")
    coordinator.acquire_claim(task["task_token"], writes=("Assets/Folder/File.asset",))

    coordinator.assert_claims(task["task_token"], writes=("Assets/Folder/File.asset",))
    with pytest.raises(ProjectBusyError, match="does not hold"):
        coordinator.assert_claims(task["task_token"], writes=("Assets/Folder",))


def test_mixed_claim_cannot_hide_reverse_lock_order(tmp_path: Path) -> None:
    project = _project(tmp_path)
    coordinator = _coordinator(tmp_path, project)
    task = _task(coordinator, "owner")
    coordinator.acquire_claim(task["task_token"], resources=("vcs-maintenance",))

    with pytest.raises(UsageError, match="order violation"):
        coordinator.acquire_claim(
            task["task_token"],
            writes=("Assets/Late.asset",),
            resources=("unity-live",),
        )


def test_plastic_machine_lines_preserve_both_moved_paths() -> None:
    ordinary = WorkspaceCoordinator._parse_plastic_status_line(
        r"CH|D:\Project\Assets\File With Space.cs|False"
    )
    moved = WorkspaceCoordinator._parse_plastic_status_line(
        r"CO+MV|100%|D:\Project\Old.asset|D:\Project\New.asset|False"
    )

    assert ordinary == [("CH", r"D:\Project\Assets\File With Space.cs")]
    assert moved == [
        ("CO+MV-source", r"D:\Project\Old.asset"),
        ("CO+MV-destination", r"D:\Project\New.asset"),
    ]


def test_reconcile_plastic_decodes_local_code_page(
    monkeypatch: pytest.MonkeyPatch, tmp_path: Path
) -> None:
    project = _project(tmp_path)
    (project / ".plastic").mkdir()
    pending_path = project / "Assets" / "测试.asset"

    monkeypatch.setattr(
        "unity_mcp_supervisor.workspace_control.locale.getpreferredencoding",
        lambda _do_setlocale=False: "cp936",
    )
    monkeypatch.setattr(
        "unity_mcp_supervisor.workspace_control.subprocess.run",
        lambda *_args, **_kwargs: subprocess.CompletedProcess(
            [], 0, f"CH|{pending_path}|False\n".encode("cp936"), b""
        ),
    )

    coordinator = _coordinator(tmp_path, project)
    observation = coordinator.reconcile_plastic()

    assert observation["pending_count"] == 1
    assert coordinator.status()["vcs"]["pending"][0]["path"] == (
        "assets/测试.asset" if os.name == "nt" else "Assets/测试.asset"
    )


def test_reconcile_plastic_rejects_unknown_encoding(
    monkeypatch: pytest.MonkeyPatch, tmp_path: Path
) -> None:
    project = _project(tmp_path)
    (project / ".plastic").mkdir()

    monkeypatch.setattr(
        "unity_mcp_supervisor.workspace_control.locale.getpreferredencoding",
        lambda _do_setlocale=False: "ascii",
    )
    monkeypatch.setattr(
        "unity_mcp_supervisor.workspace_control.subprocess.run",
        lambda *_args, **_kwargs: subprocess.CompletedProcess([], 0, b"\xff", b""),
    )

    coordinator = _coordinator(tmp_path, project)
    with pytest.raises(UsageError, match="Cannot decode Plastic stdout"):
        coordinator.reconcile_plastic()


def test_legacy_pending_blocks_overlap_until_disposition(
    monkeypatch: pytest.MonkeyPatch, tmp_path: Path
) -> None:
    project = _project(tmp_path)
    (project / ".plastic").mkdir()
    pending_path = project / "Assets" / "Owned.asset"

    def fake_status(*_args, **_kwargs):
        return subprocess.CompletedProcess(
            args=[],
            returncode=0,
            stdout=f"CH|{pending_path}|False\n",
            stderr="",
        )

    monkeypatch.setattr(
        "unity_mcp_supervisor.workspace_control.subprocess.run", fake_status
    )
    coordinator = _coordinator(tmp_path, project)
    task = _task(coordinator, "writer")

    blocked = coordinator.acquire_claim(
        task["task_token"], writes=("Assets/Owned.asset",), keep_queued=True
    )
    assert blocked["state"] == "queued"
    assert blocked["blocked_by"]["reason"] == "vcs-pending-protected"

    coordinator.set_disposition(
        task["task_token"],
        kind="adopt",
        writes=("Assets/Owned.asset",),
        evidence="owner classified existing pending",
    )
    granted = coordinator.acquire_claim(
        task["task_token"], writes=("Assets/Owned.asset",)
    )
    assert granted["state"] == "granted"


def test_same_relative_scope_is_independent_across_projects(tmp_path: Path) -> None:
    first_project = _project(tmp_path / "first")
    second_project = _project(tmp_path / "second")
    state = StatePaths(tmp_path / "shared-state")
    first = WorkspaceCoordinator(
        state,
        first_project,
        canonical_project_root(first_project),
        lease_ttl_seconds=30,
    )
    second = WorkspaceCoordinator(
        state,
        second_project,
        canonical_project_root(second_project),
        lease_ttl_seconds=30,
    )
    first_task = _task(first, "first")
    second_task = _task(second, "second")

    first_claim = first.acquire_claim(
        first_task["task_token"], writes=("Assets/Same.asset",)
    )
    second_claim = second.acquire_claim(
        second_task["task_token"], writes=("Assets/Same.asset",)
    )

    assert first_claim["state"] == "granted"
    assert second_claim["state"] == "granted"


def test_disposition_does_not_leak_across_clean_observation(
    monkeypatch: pytest.MonkeyPatch, tmp_path: Path
) -> None:
    project = _project(tmp_path)
    pending_path = project / "Assets" / "Returning.asset"
    visible = {"value": True}

    def fake_status(*_args, **_kwargs):
        stdout = f"CH|{pending_path}|False\n" if visible["value"] else ""
        return subprocess.CompletedProcess([], 0, stdout, "")

    monkeypatch.setattr(
        "unity_mcp_supervisor.workspace_control.subprocess.run", fake_status
    )
    coordinator = _coordinator(tmp_path, project)
    coordinator.reconcile_plastic()
    task = _task(coordinator, "owner")
    coordinator.set_disposition(
        task["task_token"],
        kind="adopt",
        writes=("Assets/Returning.asset",),
        evidence="first pending owner",
    )
    visible["value"] = False
    coordinator.reconcile_plastic()
    visible["value"] = True
    coordinator.reconcile_plastic()

    pending = coordinator.status()["vcs"]["pending"]
    assert pending[0]["disposition"] == "legacy-unowned"


def test_adopted_pending_is_owned_and_becomes_protected_on_release(
    monkeypatch: pytest.MonkeyPatch, tmp_path: Path
) -> None:
    project = _project(tmp_path)
    (project / ".plastic").mkdir()
    pending_path = project / "Assets" / "Owned.asset"

    def fake_status(*_args, **_kwargs):
        return subprocess.CompletedProcess([], 0, f"CH|{pending_path}|False\n", "")

    monkeypatch.setattr(
        "unity_mcp_supervisor.workspace_control.subprocess.run", fake_status
    )
    coordinator = _coordinator(tmp_path, project)
    owner = _task(coordinator, "owner")
    other = _task(coordinator, "other")
    disposition = coordinator.set_disposition(
        owner["task_token"],
        kind="adopt",
        writes=("Assets/Owned.asset",),
        evidence="owner confirmed",
    )

    assert disposition["task_id"] == owner["task_id"]
    adopted = coordinator.status()["vcs"]["pending"][0]
    assert adopted["task_id"] == owner["task_id"]
    assert adopted["owner"] == "owner"
    owned = coordinator.acquire_claim(
        owner["task_token"], writes=("Assets/Owned.asset",)
    )
    blocked = coordinator.acquire_claim(
        other["task_token"],
        writes=("Assets/Owned.asset",),
        keep_queued=True,
    )
    assert owned["state"] == "granted"
    assert blocked["state"] == "queued"

    coordinator.release_task(owner["task_token"], result="completed")
    pending = coordinator.status()["vcs"]["pending"]
    assert pending[0]["disposition"] == "protect"
    assert pending[0]["task_id"] is None


def test_adopted_pending_becomes_protected_on_unknown_recovery(
    monkeypatch: pytest.MonkeyPatch, tmp_path: Path
) -> None:
    project = _project(tmp_path)
    (project / ".plastic").mkdir()
    pending_path = project / "Assets" / "Recovered.asset"

    monkeypatch.setattr(
        "unity_mcp_supervisor.workspace_control.subprocess.run",
        lambda *_args, **_kwargs: subprocess.CompletedProcess(
            [], 0, f"CH|{pending_path}|False\n", ""
        ),
    )
    coordinator = _coordinator(tmp_path, project)
    owner = _task(coordinator, "owner")
    coordinator.set_disposition(
        owner["task_token"],
        kind="adopt",
        writes=("Assets/Recovered.asset",),
        evidence="owner confirmed",
    )
    coordinator.acquire_claim(owner["task_token"], writes=("Assets/Recovered.asset",))
    coordinator.heartbeat(
        owner["task_token"],
        phase="outcome_unknown",
        note="response lost",
        ttl_seconds=30,
    )

    coordinator.resolve_unknown(
        task_id=owner["task_id"],
        disposition="contained",
        evidence="process exited; no persistent side effects",
    )

    pending = coordinator.status()["vcs"]["pending"]
    assert pending[0]["disposition"] == "protect"
    assert pending[0]["task_id"] is None
    successor = _task(coordinator, "successor")
    adopted = coordinator.set_disposition(
        successor["task_token"],
        kind="adopt",
        writes=("Assets/Recovered.asset",),
        evidence="successor confirmed",
    )
    assert adopted["task_id"] == successor["task_id"]


def test_initialization_repairs_stale_adoption_from_terminal_task(
    tmp_path: Path,
) -> None:
    project = _project(tmp_path)
    coordinator = _coordinator(tmp_path, project)
    owner = _task(coordinator, "owner")
    project_root = coordinator.canonical_project_root
    pending_path = "assets/recovered.asset"

    with sqlite3.connect(coordinator.paths.workspace_control) as connection:
        connection.execute(
            """
            INSERT INTO vcs_pending(project_root, path, status, observation_id)
            VALUES(?, ?, 'CH', 'vcs-old')
            """,
            (project_root, pending_path),
        )
        connection.execute(
            """
            INSERT INTO vcs_dispositions(
                project_root, path, kind, task_id, evidence, updated_at
            ) VALUES(?, ?, 'adopt', ?, 'owner confirmed', ?)
            """,
            (project_root, pending_path, owner["task_id"], time.time()),
        )
        connection.execute(
            "UPDATE tasks SET state = 'failed', ended_at = ? WHERE task_id = ?",
            (time.time(), owner["task_id"]),
        )

    repaired = _coordinator(tmp_path, project)
    pending = repaired.status()["vcs"]["pending"]
    assert pending[0]["disposition"] == "protect"
    assert pending[0]["task_id"] is None
    successor = _task(repaired, "successor")
    adopted = repaired.set_disposition(
        successor["task_token"],
        kind="adopt",
        writes=("Assets/Recovered.asset",),
        evidence="successor confirmed",
    )
    assert adopted["task_id"] == successor["task_id"]


def test_disposition_requires_owner_token_and_current_pending(
    monkeypatch: pytest.MonkeyPatch, tmp_path: Path
) -> None:
    project = _project(tmp_path)
    (project / ".plastic").mkdir()
    monkeypatch.setattr(
        "unity_mcp_supervisor.workspace_control.subprocess.run",
        lambda *_args, **_kwargs: subprocess.CompletedProcess([], 0, "", ""),
    )
    coordinator = _coordinator(tmp_path, project)
    task = _task(coordinator, "owner")

    with pytest.raises(UsageError, match="token"):
        coordinator.set_disposition(
            None,
            kind="protect",
            writes=("Assets/Clean.asset",),
            evidence="not pending",
        )
    with pytest.raises(UsageError, match="currently pending"):
        coordinator.set_disposition(
            task["task_token"],
            kind="protect",
            writes=("Assets/Clean.asset",),
            evidence="not pending",
        )


def test_expired_unity_task_releases_legacy_binding_before_next_claim(
    tmp_path: Path,
) -> None:
    project = _project(tmp_path, enforcement="required")
    coordinator = _coordinator(tmp_path, project)
    expired = _task(coordinator, "expired")
    coordinator.acquire_claim(expired["task_token"], resources=("unity-live",))
    coordinator.heartbeat(
        expired["task_token"], phase="waiting", note=None, ttl_seconds=0.05
    )
    time.sleep(0.06)
    successor = _task(coordinator, "successor")

    claim = coordinator.acquire_claim(
        successor["task_token"], resources=("unity-live",)
    )

    assert claim["state"] == "granted"


@pytest.mark.parametrize(
    "field,value",
    [("schemaVersion", "1"), ("unityMetaPairing", "false")],
)
def test_policy_rejects_coerced_scalar_types(
    tmp_path: Path, field: str, value: str
) -> None:
    project = _project(tmp_path)
    policy_path = project / "Tools" / "Coordination" / "workspace-control.json"
    policy = {
        "schemaVersion": 1,
        "enforcement": "audit",
        "unityMetaPairing": True,
    }
    policy[field] = value
    policy_path.write_text(json.dumps(policy), encoding="utf-8")

    loaded = load_workspace_policy(project)

    assert loaded.valid is False
    assert loaded.error is not None


def test_workspace_database_v1_migrates_adoption_owner_column(
    tmp_path: Path,
) -> None:
    project = _project(tmp_path)
    state = StatePaths(tmp_path / "state")
    state.ensure()
    with sqlite3.connect(state.workspace_control) as connection:
        connection.execute(
            "CREATE TABLE workspace_meta (key TEXT PRIMARY KEY, value TEXT NOT NULL)"
        )
        connection.execute(
            "INSERT INTO workspace_meta(key, value) VALUES('schema_version', '1')"
        )
        connection.execute(
            """
            CREATE TABLE vcs_dispositions (
                project_root TEXT NOT NULL,
                path TEXT NOT NULL,
                kind TEXT NOT NULL,
                evidence TEXT,
                updated_at REAL NOT NULL,
                PRIMARY KEY(project_root, path)
            )
            """
        )

    _coordinator(tmp_path, project)

    with sqlite3.connect(state.workspace_control) as connection:
        version = connection.execute(
            "SELECT value FROM workspace_meta WHERE key = 'schema_version'"
        ).fetchone()[0]
        columns = {
            row[1]
            for row in connection.execute(
                "PRAGMA table_info(vcs_dispositions)"
            ).fetchall()
        }
    assert version == "2"
    assert "task_id" in columns


def test_invalid_coordination_record_fails_closed(tmp_path: Path) -> None:
    project = _project(tmp_path)
    coordinator = _coordinator(tmp_path, project)
    task = _task(coordinator, "owner")
    with sqlite3.connect(coordinator.paths.workspace_control) as connection:
        connection.execute(
            "UPDATE tasks SET state = 'invented-state' WHERE task_id = ?",
            (task["task_id"],),
        )

    with pytest.raises(IncompatibleError, match="invalid coordination record"):
        _coordinator(tmp_path, project)
