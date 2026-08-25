from __future__ import annotations

import hashlib
import json
import os
import sqlite3
import stat
import tempfile
import uuid
from pathlib import Path
from types import SimpleNamespace

import pytest

import unity_workspace_scheduler.state as state_module
import unity_workspace_scheduler.state_ops as state_ops_module
from unity_workspace_scheduler.cli import run
from unity_workspace_scheduler.coordinator import WorkspaceCoordinator, _token_hash
from unity_workspace_scheduler.errors import StateError, UsageError
from unity_workspace_scheduler.operations import receipt_delivery_digest
from unity_workspace_scheduler.state import (
    StatePaths,
    create_token_file,
    open_database,
    resolve_state_paths,
)
from unity_workspace_scheduler.state_ops import (
    _remove_temporary_database,
    _sqlite_backup,
    _temporary_database,
    _validate_lifecycle_terminal_proof,
    backup_state,
    inspect_state,
    restore_state,
    verify_state,
)

ROOT = Path(__file__).resolve().parents[1]
SCHEMA_ONE_SQL = (ROOT / "tests" / "fixtures" / "schema1.sql").read_text(encoding="utf-8")
AMBIGUOUS_RESTORATION_SQL_TEMPLATE = (
    ROOT / "tests" / "fixtures" / "schema1_ambiguous_restoration.sql"
).read_text(encoding="utf-8")


def _ambiguous_restoration_sql(workspace: Path) -> str:
    root = str(workspace.resolve())
    return AMBIGUOUS_RESTORATION_SQL_TEMPLATE.replace(
        "__WORKSPACE_ID__", _legacy_workspace_id(root)
    ).replace("__WORKSPACE_ROOT__", root.replace("'", "''"))


def _legacy_workspace_id(root: str) -> str:
    return hashlib.sha256(os.path.normcase(root).casefold().encode("utf-8")).hexdigest()


def _custom_schema_two_state(
    tmp_path: Path,
    *,
    omit_claim_task_fk: bool = False,
    omit_recovery_event_pk: bool = False,
    orphan_claim: bool = False,
    add_task_check: bool = False,
) -> Path:
    workspace = tmp_path / "custom-schema-workspace"
    workspace.mkdir()
    root = str(workspace.resolve())
    workspace_id = _legacy_workspace_id(root)
    database = tmp_path / "custom-schema.sqlite3"
    with sqlite3.connect(database) as connection:
        for original in state_module._SCHEMA_TWO_STATEMENTS:
            statement = original
            if omit_claim_task_fk and "CREATE TABLE claims" in statement:
                statement = statement.replace(
                    "task_id TEXT NOT NULL REFERENCES tasks(id) ON DELETE CASCADE,",
                    "task_id TEXT NOT NULL,",
                )
            if omit_recovery_event_pk and "CREATE TABLE recovery_events" in statement:
                statement = statement.replace("id TEXT PRIMARY KEY,", "id TEXT NOT NULL,")
            if add_task_check and "CREATE TABLE tasks" in statement:
                statement = statement.replace(
                    "owner TEXT NOT NULL,",
                    "owner TEXT NOT NULL CHECK(length(owner) > 0),",
                )
            connection.execute(statement)
        for statement in state_module._SCHEMA_TWO_INDEX_STATEMENTS:
            connection.execute(statement)
        connection.execute("INSERT INTO scheduler_meta(key, value) VALUES('schema_version', '2')")
        connection.execute(
            "INSERT INTO workspaces(id, root, registered_at, epoch, next_queue_order) "
            "VALUES(?, ?, 1000, 1, ?)",
            (workspace_id, root, 2 if orphan_claim else 1),
        )
        if orphan_claim:
            connection.execute(
                "INSERT INTO claims(id, workspace_id, task_id, kind, state, queue_order, "
                "created_at, granted_at) VALUES('orphan-claim', ?, 'missing-task', "
                "'normal', 'active', 1, 1000, 1000)",
                (workspace_id,),
            )
            connection.execute(
                "INSERT INTO claim_scopes(claim_id, scope_type, value) "
                "VALUES('orphan-claim', 'write', 'assets/hero.prefab')"
            )
    return database


def _registered_scheduler(root: Path, workspace: Path) -> WorkspaceCoordinator:
    workspace.mkdir()
    scheduler = WorkspaceCoordinator(resolve_state_paths(root))
    scheduler.register(workspace)
    return scheduler


def _mark_finalized_receipts_delivered(scheduler: WorkspaceCoordinator) -> None:
    connection = sqlite3.connect(scheduler.paths.database)
    try:
        receipts = connection.execute(
            "SELECT operation_id, fingerprint, result_json, terminal_json, token_cleanup_path "
            "FROM operation_receipts "
            "WHERE finalized_at IS NOT NULL AND delivered_at IS NULL "
            "ORDER BY created_at, operation_id"
        ).fetchall()
    finally:
        connection.close()

    for operation_id, fingerprint, result_json, terminal_json, token_cleanup_path in receipts:
        acknowledged = scheduler.acknowledge_receipt(
            str(operation_id),
            str(fingerprint),
            receipt_delivery_digest(str(result_json), terminal_json),
        )
        assert acknowledged["acknowledged"] is True
        assert acknowledged["token_cleanup_expected"] is (token_cleanup_path is not None)


def _checkpoint_and_inspect(scheduler: WorkspaceCoordinator) -> dict[str, object]:
    connection = open_database(scheduler.paths)
    try:
        checkpoint = connection.execute("PRAGMA wal_checkpoint(TRUNCATE)").fetchone()
    finally:
        connection.close()
    assert checkpoint is not None
    assert int(checkpoint[0]) == 0
    return inspect_state(scheduler.paths.database)


def _closed_task_state(tmp_path: Path) -> tuple[WorkspaceCoordinator, Path]:
    workspace = tmp_path / "source-workspace"
    scheduler = _registered_scheduler(tmp_path / "source-state", workspace)
    _, token = scheduler.start_task(workspace, "owner", "closed work")
    scheduler.release_task(workspace, token, result="completed")
    return scheduler, workspace


def _read_output(capsys) -> dict[str, object]:
    payload = json.loads(capsys.readouterr().out)
    assert payload["protocol_version"] == 3
    return payload


def _active_write_state(
    tmp_path: Path,
) -> tuple[WorkspaceCoordinator, Path, str, str]:
    workspace = tmp_path / "semantic-workspace"
    scheduler = _registered_scheduler(tmp_path / "semantic-state", workspace)
    task, token = scheduler.start_task(workspace, "owner", "semantic verification")
    claim = scheduler.acquire_claim(workspace, token, writes=("Assets/Hero.prefab",))
    return scheduler, workspace, str(task["id"]), str(claim["id"])


def _two_active_claim_state(
    tmp_path: Path,
) -> tuple[WorkspaceCoordinator, Path, str, str]:
    workspace = tmp_path / "two-active-workspace"
    scheduler = _registered_scheduler(tmp_path / "two-active-state", workspace)
    _, first_token = scheduler.start_task(workspace, "first", "first active claim")
    _, second_token = scheduler.start_task(workspace, "second", "second active claim")
    first = scheduler.acquire_claim(workspace, first_token, writes=("Assets/Hero.prefab",))
    second = scheduler.acquire_claim(workspace, second_token, writes=("Assets/Villain.prefab",))
    return scheduler, workspace, str(first["id"]), str(second["id"])


def _schema_one_active_state(tmp_path: Path) -> tuple[Path, Path, str, str]:
    workspace = tmp_path / "schema-one-workspace"
    workspace.mkdir()
    root = str(workspace.resolve())
    workspace_id = _legacy_workspace_id(root)
    database = tmp_path / "schema-one.sqlite3"
    with sqlite3.connect(database) as connection:
        connection.executescript(SCHEMA_ONE_SQL)
        connection.execute(
            "INSERT INTO workspaces(id, root, registered_at, epoch) VALUES(?, ?, 1000, 1)",
            (workspace_id, root),
        )
        connection.executemany(
            "INSERT INTO tasks(id, workspace_id, owner, summary, token_hash, state, "
            "created_at, heartbeat_at, expires_at) VALUES(?, ?, ?, ?, ?, 'active', "
            "1000, 1100, 2900)",
            (
                ("first-task", workspace_id, "first", "first", _token_hash("first-token")),
                ("second-task", workspace_id, "second", "second", _token_hash("second-token")),
            ),
        )
        connection.executemany(
            "INSERT INTO claims(id, workspace_id, task_id, kind, state, queue_order, "
            "created_at, granted_at) VALUES(?, ?, ?, 'normal', 'active', ?, 1000, 1000)",
            (
                ("first-claim", workspace_id, "first-task", 1),
                ("second-claim", workspace_id, "second-task", 2),
            ),
        )
        connection.executemany(
            "INSERT INTO claim_scopes(claim_id, scope_type, value) VALUES(?, 'write', ?)",
            (
                ("first-claim", "assets/hero.prefab"),
                ("second-claim", "assets/villain.prefab"),
            ),
        )
    return database, workspace, "first-claim", "second-claim"


def _parked_write_state(
    tmp_path: Path,
) -> tuple[WorkspaceCoordinator, str, str]:
    workspace = tmp_path / "parked-workspace"
    scheduler = _registered_scheduler(tmp_path / "parked-state", workspace)
    _, owner_token = scheduler.start_task(workspace, "owner", "parked owner")
    _, freeze_token = scheduler.start_task(workspace, "maintenance", "freeze")
    owned = scheduler.acquire_claim(workspace, owner_token, writes=("Assets/Hero.prefab",))
    freeze = scheduler.acquire_claim(workspace, freeze_token, freeze=True)
    scheduler.park_task(workspace, owner_token)
    return scheduler, str(owned["id"]), str(freeze["id"])


def _multi_parked_write_state(
    tmp_path: Path,
) -> tuple[WorkspaceCoordinator, Path, tuple[str, str], str]:
    workspace = tmp_path / "multi-parked-workspace"
    scheduler = _registered_scheduler(tmp_path / "multi-parked-state", workspace)
    _, owner_token = scheduler.start_task(workspace, "owner", "multi-claim parked owner")
    _, freeze_token = scheduler.start_task(workspace, "maintenance", "freeze")
    first = scheduler.acquire_claim(workspace, owner_token, writes=("Assets/Hero.prefab",))
    second = scheduler.acquire_claim(workspace, owner_token, writes=("Assets/Villain.prefab",))
    freeze = scheduler.acquire_claim(workspace, freeze_token, freeze=True)
    scheduler.park_task(workspace, owner_token)
    return (
        scheduler,
        workspace,
        (str(first["id"]), str(second["id"])),
        str(freeze["id"]),
    )


def test_backup_uses_sqlite_snapshot_and_includes_committed_wal(tmp_path: Path) -> None:
    workspace = tmp_path / "workspace"
    scheduler = _registered_scheduler(tmp_path / "state", workspace)
    task, _ = scheduler.start_task(workspace, "owner", "WAL snapshot")
    backup = tmp_path / "backup.sqlite3"

    connection = open_database(scheduler.paths)
    try:
        connection.execute("PRAGMA wal_autocheckpoint = 0")
        connection.execute("PRAGMA wal_checkpoint(TRUNCATE)")
        connection.execute("UPDATE tasks SET note = 'committed-in-wal' WHERE id = ?", (task["id"],))
        connection.commit()
        wal = Path(f"{scheduler.paths.database}-wal")
        assert wal.exists()
        assert wal.stat().st_size > 0

        report = backup_state(scheduler.paths, backup, confirm_no_processes=True)
    finally:
        connection.close()

    assert report["backup"]["integrity_check"] == "ok"
    assert report["backup"]["counts"]["tasks"] == 1
    with sqlite3.connect(backup) as restored:
        note = restored.execute("SELECT note FROM tasks WHERE id = ?", (task["id"],)).fetchone()[0]
    assert note == "committed-in-wal"


def test_sqlite_backup_is_paged_and_busy_locked_has_a_hard_deadline(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    class SourceConnection:
        def execute(self, statement: str) -> None:
            assert statement == "PRAGMA busy_timeout = 0"

        def backup(self, _destination, **kwargs) -> None:
            assert kwargs["pages"] == state_ops_module._SQLITE_BACKUP_PAGES
            assert kwargs["sleep"] == state_ops_module._SQLITE_BACKUP_SLEEP_SECONDS
            kwargs["progress"](sqlite3.SQLITE_BUSY, 10, 10)
            kwargs["progress"](sqlite3.SQLITE_LOCKED, 10, 10)

        def close(self) -> None:
            pass

    class DestinationConnection:
        def execute(self, statement: str) -> None:
            assert statement == "PRAGMA busy_timeout = 0"

        def commit(self) -> None:
            raise AssertionError("a timed-out backup must not commit")

        def close(self) -> None:
            pass

    class Destination:
        path = tmp_path / "staged.sqlite3"

        def sync(self) -> None:
            raise AssertionError("a timed-out backup must not publish staged bytes")

    clock = iter((0.0, 0.05, 0.11))
    monkeypatch.setattr(state_ops_module, "_SQLITE_BACKUP_TIMEOUT_SECONDS", 0.1)
    monkeypatch.setattr(state_ops_module.time, "monotonic", lambda: next(clock))
    monkeypatch.setattr(
        state_ops_module,
        "_read_only_connection",
        lambda _path: (SourceConnection(), None),
    )
    monkeypatch.setattr(state_ops_module, "_verify_standalone_read", lambda _evidence: None)
    monkeypatch.setattr(
        state_ops_module.sqlite3,
        "connect",
        lambda *_args, **_kwargs: DestinationConnection(),
    )

    with pytest.raises(StateError) as timed_out:
        _sqlite_backup(tmp_path / "source.sqlite3", Destination())  # type: ignore[arg-type]
    assert timed_out.value.details == {
        "path": str(tmp_path / "source.sqlite3"),
        "reason": "sqlite-backup-timeout",
        "sqlite_status": "locked",
        "timeout_seconds": 0.1,
    }


def test_backup_requires_attestation_and_never_overwrites_destination(tmp_path: Path) -> None:
    scheduler, _ = _closed_task_state(tmp_path)
    backup = tmp_path / "backup.sqlite3"
    with pytest.raises(UsageError) as missing_attestation:
        backup_state(scheduler.paths, backup, confirm_no_processes=False)
    assert missing_attestation.value.details["reason"] == "zero-process-attestation-required"

    backup.write_bytes(b"keep-me")
    with pytest.raises(UsageError) as existing:
        backup_state(scheduler.paths, backup, confirm_no_processes=True)
    assert existing.value.details["reason"] == "destination-exists"
    assert backup.read_bytes() == b"keep-me"


@pytest.mark.parametrize("kind", ["regular", "dangling-link"])
def test_backup_rejects_orphan_destination_sidecars_before_staging(
    tmp_path: Path,
    kind: str,
) -> None:
    scheduler, _ = _closed_task_state(tmp_path)
    backup = tmp_path / f"orphan-{kind}.sqlite3"
    sidecar = Path(f"{backup}-wal")
    if kind == "regular":
        sidecar.write_bytes(b"existing-sidecar")
    else:
        try:
            sidecar.symlink_to(tmp_path / "missing-sidecar-target")
        except OSError as exc:
            pytest.skip(f"File symlinks are unavailable: {exc}")

    with pytest.raises(UsageError) as invalid:
        backup_state(scheduler.paths, backup, confirm_no_processes=True)
    assert invalid.value.details["reason"] == "backup-destination-orphan-sidecars"
    assert invalid.value.details["sidecars"] == [str(sidecar)]
    assert not backup.exists()
    assert sidecar.lstat()


def test_backup_rechecks_orphan_sidecars_immediately_before_publish(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    scheduler, _ = _closed_task_state(tmp_path)
    backup = tmp_path / "orphan-race.sqlite3"
    sidecar = Path(f"{backup}-journal")
    prepare = state_ops_module._prepare_staged_snapshot

    def prepare_then_race(path: Path) -> None:
        prepare(path)
        sidecar.write_bytes(b"late-sidecar")

    monkeypatch.setattr(state_ops_module, "_prepare_staged_snapshot", prepare_then_race)

    with pytest.raises(UsageError) as invalid:
        backup_state(scheduler.paths, backup, confirm_no_processes=True)
    assert invalid.value.details["reason"] == "backup-destination-orphan-sidecars"
    assert not backup.exists()
    assert sidecar.read_bytes() == b"late-sidecar"


@pytest.mark.skipif(os.name == "nt", reason="POSIX modes only")
def test_posix_maintenance_preserves_existing_parent_modes_and_secures_files(
    tmp_path: Path,
) -> None:
    scheduler, _ = _closed_task_state(tmp_path)
    backup_parent = tmp_path / "shared-backup-parent"
    backup_parent.mkdir()
    backup_parent.chmod(0o755)
    backup = backup_parent / "backup.sqlite3"

    backup_state(scheduler.paths, backup, confirm_no_processes=True)
    assert stat.S_IMODE(backup_parent.stat().st_mode) == 0o755
    assert stat.S_IMODE(backup.stat().st_mode) == 0o600

    restore_parent = tmp_path / "shared-restore-parent"
    restore_parent.mkdir()
    restore_parent.chmod(0o750)
    restored_paths = StatePaths(restore_parent)
    restore_state(restored_paths, backup, confirm_no_processes=True)
    assert stat.S_IMODE(restore_parent.stat().st_mode) == 0o750
    assert stat.S_IMODE(restored_paths.database.stat().st_mode) == 0o600

    unsafe_parent = tmp_path / "unsafe-maintenance-parent"
    unsafe_parent.mkdir()
    unsafe_parent.chmod(0o775)
    with pytest.raises(UsageError) as unsafe:
        backup_state(
            scheduler.paths,
            unsafe_parent / "backup.sqlite3",
            confirm_no_processes=True,
        )
    assert unsafe.value.details["reason"] == "maintenance-parent-unsafe"


@pytest.mark.skipif(os.name == "nt", reason="POSIX modes only")
def test_posix_maintenance_creates_private_parents_and_temporary_files(
    tmp_path: Path,
) -> None:
    scheduler, _ = _closed_task_state(tmp_path)
    backup_parent = tmp_path / "new-backup-parent"
    backup = backup_parent / "backup.sqlite3"

    backup_state(scheduler.paths, backup, confirm_no_processes=True)
    assert stat.S_IMODE(backup_parent.stat().st_mode) == 0o700
    assert stat.S_IMODE(backup.stat().st_mode) == 0o600

    restore_parent = tmp_path / "new-restore-parent"
    restored_paths = StatePaths(restore_parent)
    restore_state(restored_paths, backup, confirm_no_processes=True)
    assert stat.S_IMODE(restore_parent.stat().st_mode) == 0o700
    assert stat.S_IMODE(restored_paths.database.stat().st_mode) == 0o600

    temporary = _temporary_database(backup_parent)
    try:
        assert stat.S_IMODE(temporary.path.stat().st_mode) == 0o600
        temporary.verify()
    finally:
        _remove_temporary_database(temporary)


@pytest.mark.skipif(os.name == "nt", reason="POSIX ownership and modes only")
def test_posix_maintenance_rejects_writable_database_and_sidecar_entries(
    tmp_path: Path,
) -> None:
    scheduler, _ = _closed_task_state(tmp_path)
    scheduler.paths.database.chmod(0o666)
    with pytest.raises(UsageError) as unsafe_main:
        verify_state(scheduler.paths.database)
    assert unsafe_main.value.details["reason"] == "maintenance-file-unsafe"

    scheduler.paths.database.chmod(0o600)
    sidecar = Path(f"{scheduler.paths.database}-journal")
    sidecar.write_bytes(b"")
    sidecar.chmod(0o666)
    with pytest.raises(UsageError) as unsafe_sidecar:
        verify_state(scheduler.paths.database)
    assert unsafe_sidecar.value.details["reason"] == "maintenance-file-unsafe"


@pytest.mark.skipif(os.name == "nt", reason="POSIX ownership only")
def test_posix_maintenance_rejects_other_owner_before_open(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    database = tmp_path / "other-owner.sqlite3"
    database.write_bytes(b"not-opened")
    actual_euid = os.geteuid()
    monkeypatch.setattr(state_ops_module.os, "geteuid", lambda: actual_euid + 1)

    with pytest.raises(UsageError) as unsafe:
        state_ops_module._validate_maintenance_database_file(database)
    assert unsafe.value.details["reason"] == "maintenance-file-unsafe"


@pytest.mark.skipif(os.name != "nt", reason="Windows reparse attributes only")
def test_windows_reparse_detection_works_without_path_is_junction() -> None:
    reparse = SimpleNamespace(
        lstat=lambda: SimpleNamespace(st_file_attributes=0x400),
    )
    regular = SimpleNamespace(
        lstat=lambda: SimpleNamespace(st_file_attributes=0),
    )

    assert state_module._is_windows_reparse_point(reparse) is True
    assert state_module._is_windows_reparse_point(regular) is False


def test_standalone_verify_is_byte_for_byte_read_only(tmp_path: Path) -> None:
    scheduler, _ = _closed_task_state(tmp_path)
    backup = tmp_path / "standalone-read-only.sqlite3"
    backup_state(scheduler.paths, backup, confirm_no_processes=True)
    identity = state_ops_module._maintenance_file_identity(backup)
    digest = state_ops_module._sha256_file(backup)
    assert state_ops_module._existing_sidecars(backup) == []

    report = verify_state(backup)

    assert report["integrity_check"] == "ok"
    assert state_ops_module._maintenance_file_identity(backup) == identity
    assert state_ops_module._sha256_file(backup) == digest
    assert state_ops_module._existing_sidecars(backup) == []


@pytest.mark.skipif(os.name == "nt", reason="POSIX modes only")
def test_standalone_verify_accepts_read_only_private_input(tmp_path: Path) -> None:
    scheduler, _ = _closed_task_state(tmp_path)
    parent = tmp_path / "read-only-private"
    parent.mkdir()
    backup = parent / "state.sqlite3"
    backup_state(scheduler.paths, backup, confirm_no_processes=True)
    backup.chmod(0o400)
    parent.chmod(0o500)
    try:
        assert verify_state(backup)["integrity_check"] == "ok"
        assert state_ops_module._existing_sidecars(backup) == []
    finally:
        parent.chmod(0o700)
        backup.chmod(0o600)


@pytest.mark.parametrize("mutation", ["sidecar", "bytes"])
def test_standalone_verify_rejects_concurrent_input_changes(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    mutation: str,
) -> None:
    scheduler, _ = _closed_task_state(tmp_path)
    backup = tmp_path / f"standalone-race-{mutation}.sqlite3"
    backup_state(scheduler.paths, backup, confirm_no_processes=True)
    validate_schema = state_ops_module._validate_declared_schema_structure

    def validate_then_mutate(connection, resolved: Path, schema_version: int) -> None:
        validate_schema(connection, resolved, schema_version)
        if mutation == "sidecar":
            Path(f"{resolved}-wal").write_bytes(b"")
        else:
            with resolved.open("ab") as changed:
                changed.write(b"concurrent-change")

    monkeypatch.setattr(
        state_ops_module,
        "_validate_declared_schema_structure",
        validate_then_mutate,
    )

    with pytest.raises(StateError) as invalid:
        verify_state(backup)
    assert invalid.value.details["reason"] == "standalone-input-changed"


def test_verify_with_existing_wal_reads_committed_wal_pages(tmp_path: Path) -> None:
    scheduler, workspace, task_id, _ = _active_write_state(tmp_path)
    connection = open_database(scheduler.paths)
    try:
        connection.execute("PRAGMA wal_autocheckpoint = 0")
        connection.execute("PRAGMA wal_checkpoint(TRUNCATE)")
        connection.execute(
            "UPDATE tasks SET state = 'mystery' WHERE id = ?",
            (task_id,),
        )
        connection.commit()
        wal = Path(f"{scheduler.paths.database}-wal")
        assert wal.stat().st_size > 0

        with pytest.raises(StateError) as invalid:
            verify_state(scheduler.paths.database)
        assert invalid.value.details["reason"] == "task-state-invalid"
        assert wal.exists()
        assert workspace.exists()
    finally:
        connection.close()


def test_verify_reports_schema_and_blocks_ambiguous_schema_one_migration(
    tmp_path: Path,
) -> None:
    safe = tmp_path / "safe-schema-one.sqlite3"
    with sqlite3.connect(safe) as connection:
        connection.executescript(SCHEMA_ONE_SQL)
    safe_report = verify_state(safe, for_migration=True)
    assert safe_report["schema_version"] == 1
    assert safe_report["schema_one_migration_safe"] is True

    ambiguous = tmp_path / "ambiguous-schema-one.sqlite3"
    ambiguous_workspace = tmp_path / "ambiguous-workspace"
    ambiguous_workspace.mkdir()
    with sqlite3.connect(ambiguous) as connection:
        connection.executescript(SCHEMA_ONE_SQL)
        connection.executescript(_ambiguous_restoration_sql(ambiguous_workspace))
    with pytest.raises(UsageError) as blocked:
        verify_state(ambiguous, for_migration=True)
    assert blocked.value.details == {
        "path": str(ambiguous.resolve()),
        "reason": "schema-one-open-claim-migration-blocked",
        "claim_states": {"queued": 2},
    }


@pytest.mark.parametrize(
    ("mutation", "reason"),
    (
        ("UPDATE workspaces SET root = root || '-mismatch'", "workspace-identity-invalid"),
        ("UPDATE tasks SET token_hash = 'bad' WHERE id = 'first-task'", "open-task-token-invalid"),
        (
            (
                "UPDATE tasks SET token_hash = "
                "(SELECT token_hash FROM tasks WHERE id = 'first-task') "
                "WHERE id = 'second-task'"
            ),
            "open-task-token-duplicate",
        ),
        (
            "UPDATE tasks SET result = 'completed' WHERE id = 'first-task'",
            "task-lifecycle-invalid",
        ),
        (
            "UPDATE tasks SET created_at = 'invalid' WHERE id = 'first-task'",
            "task-timing-invalid",
        ),
        (
            "UPDATE claims SET queue_order = 1 WHERE id = 'second-claim'",
            "open-claim-queue-order-duplicate",
        ),
    ),
)
def test_schema_one_migration_preflight_rejects_unrepaired_invariant_violations(
    tmp_path: Path, mutation: str, reason: str
) -> None:
    database, _, _, _ = _schema_one_active_state(tmp_path)
    with sqlite3.connect(database) as connection:
        connection.executescript(mutation)

    with pytest.raises(StateError) as invalid:
        verify_state(database, for_migration=True)
    assert invalid.value.details["reason"] == reason


def test_schema_one_migration_preflight_accepts_valid_active_state(tmp_path: Path) -> None:
    database, _, _, _ = _schema_one_active_state(tmp_path)
    if os.name != "nt":
        with pytest.raises(UsageError) as blocked:
            verify_state(database, for_migration=True)
        assert blocked.value.details["reason"] == "legacy-open-write-scope-migration-blocked"
        assert blocked.value.details["open_write_scope_count"] == 2
        return
    report = verify_state(database, for_migration=True)
    assert report["schema_one_migration_safe"] is True
    assert report["counts"]["active_claims"] == 2


@pytest.mark.parametrize("version", ["02", "+2"])
def test_live_and_offline_schema_versions_must_be_canonical_text(
    tmp_path: Path,
    version: str,
) -> None:
    workspace = tmp_path / f"canonical-workspace-{version.replace('+', 'plus')}"
    scheduler = _registered_scheduler(
        tmp_path / f"canonical-state-{version.replace('+', 'plus')}",
        workspace,
    )
    with sqlite3.connect(scheduler.paths.database) as connection:
        connection.execute(
            "UPDATE scheduler_meta SET value = ? WHERE key = 'schema_version'",
            (version,),
        )

    with pytest.raises(StateError) as offline_invalid:
        verify_state(scheduler.paths.database)
    assert offline_invalid.value.details["reason"] == "schema-version-invalid"
    target = StatePaths(tmp_path / f"canonical-restore-{version.replace('+', 'plus')}")
    with pytest.raises(StateError) as restore_invalid:
        restore_state(target, scheduler.paths.database, confirm_no_processes=True)
    assert restore_invalid.value.details["reason"] == "schema-version-invalid"
    assert not target.root.exists()
    with pytest.raises(UsageError, match="canonical TEXT"):
        open_database(scheduler.paths)
    with sqlite3.connect(scheduler.paths.database) as connection:
        assert (
            connection.execute(
                "SELECT value FROM scheduler_meta WHERE key = 'schema_version'"
            ).fetchone()[0]
            == version
        )


def test_live_schema_version_rejects_integer_storage(tmp_path: Path) -> None:
    paths = StatePaths(tmp_path / "integer-version-state")
    paths.root.mkdir()
    with sqlite3.connect(paths.database) as connection:
        connection.execute("CREATE TABLE scheduler_meta(key TEXT PRIMARY KEY, value)")
        connection.execute("INSERT INTO scheduler_meta VALUES('schema_version', 2)")

    with pytest.raises(UsageError, match="canonical TEXT"):
        open_database(paths)
    with sqlite3.connect(paths.database) as connection:
        value, storage_type = connection.execute(
            "SELECT value, typeof(value) FROM scheduler_meta WHERE key = 'schema_version'"
        ).fetchone()
    assert (value, storage_type) == (2, "integer")


def test_schema_one_migration_preflight_rejects_active_claim_conflicts(tmp_path: Path) -> None:
    database, _, _, second_claim_id = _schema_one_active_state(tmp_path)
    with sqlite3.connect(database) as connection:
        connection.execute(
            "UPDATE claim_scopes SET value = 'assets/hero.prefab/child' WHERE claim_id = ?",
            (second_claim_id,),
        )

    with pytest.raises(StateError) as invalid:
        verify_state(database, for_migration=True)
    assert invalid.value.details["reason"] == "active-claim-conflict"


def test_schema_one_migration_rejects_same_task_active_resource_conflict_atomically(
    tmp_path: Path,
) -> None:
    paths = StatePaths(tmp_path / "same-task-resource-schema-one-state")
    paths.root.mkdir()
    database, _, first_claim_id, second_claim_id = _schema_one_active_state(paths.root)
    with sqlite3.connect(database) as connection:
        connection.execute(
            "UPDATE claims SET task_id = 'first-task' WHERE id = ?",
            (second_claim_id,),
        )
        connection.executemany(
            "INSERT INTO claim_scopes(claim_id, scope_type, value) "
            "VALUES(?, 'resource', 'unity-live')",
            ((first_claim_id,), (second_claim_id,)),
        )
    connection.close()
    paths.database.write_bytes(database.read_bytes())
    journal = Path(f"{paths.database}-journal")
    database_before = paths.database.read_bytes()
    journal_before = journal.read_bytes() if journal.exists() else None

    with pytest.raises(StateError) as invalid:
        open_database(paths)
    assert invalid.value.details["reason"] == "active-claim-conflict"
    assert invalid.value.details["conflict_count"] == 1
    assert paths.database.read_bytes() == database_before
    assert (journal.read_bytes() if journal.exists() else None) == journal_before
    with sqlite3.connect(paths.database) as connection:
        assert (
            connection.execute(
                "SELECT value FROM scheduler_meta WHERE key = 'schema_version'"
            ).fetchone()[0]
            == "1"
        )
        assert "next_queue_order" not in {
            row[1] for row in connection.execute("PRAGMA table_info(workspaces)")
        }


@pytest.mark.parametrize(
    ("mutation", "reason"),
    (
        ("UPDATE tasks SET state = 'mystery'", "task-state-invalid"),
        ("UPDATE claims SET state = 'mystery'", "claim-state-invalid"),
        ("UPDATE claims SET kind = 'mystery'", "claim-kind-invalid"),
        (
            "UPDATE claim_scopes SET scope_type = 'mystery'",
            "claim-scope-type-invalid",
        ),
        ("UPDATE claims SET queue_order = 0", "claim-queue-order-invalid"),
        (
            "UPDATE tasks SET state = 'completed', result = 'completed', finished_at = 1",
            "open-claim-owner-closed",
        ),
        ("DELETE FROM claim_scopes", "normal-claim-scope-invalid"),
        ("UPDATE claims SET kind = 'freeze'", "freeze-claim-scope-invalid"),
        (
            (
                "INSERT INTO claim_scopes(claim_id, scope_type, value) "
                "SELECT id, 'priority', 'urgent' FROM claims"
            ),
            "claim-priority-invalid",
        ),
        (
            "UPDATE claim_scopes SET value = '..' WHERE scope_type = 'write'",
            "claim-scope-value-invalid",
        ),
        (
            "UPDATE claim_scopes SET scope_type = 'resource', value = ' Unity-Live '",
            "claim-scope-value-invalid",
        ),
    ),
)
def test_verify_rejects_invalid_scheduler_semantics(
    tmp_path: Path,
    mutation: str,
    reason: str,
) -> None:
    scheduler, _, _, _ = _active_write_state(tmp_path)
    with sqlite3.connect(scheduler.paths.database) as connection:
        connection.executescript(mutation)

    with pytest.raises(StateError) as invalid:
        inspect_state(scheduler.paths.database)
    assert invalid.value.details["reason"] == reason


def test_verify_rejects_claim_workspace_mismatch(tmp_path: Path) -> None:
    scheduler, _, _, claim_id = _active_write_state(tmp_path)
    other_workspace = tmp_path / "other-semantic-workspace"
    other_workspace.mkdir()
    scheduler.register(other_workspace)
    with sqlite3.connect(scheduler.paths.database) as connection:
        other_workspace_id = connection.execute(
            "SELECT id FROM workspaces WHERE id != (SELECT workspace_id FROM claims WHERE id = ?)",
            (claim_id,),
        ).fetchone()[0]
        connection.execute(
            "UPDATE claims SET workspace_id = ? WHERE id = ?",
            (other_workspace_id, claim_id),
        )

    with pytest.raises(StateError) as invalid:
        inspect_state(scheduler.paths.database)
    assert invalid.value.details["reason"] == "claim-workspace-mismatch"


def test_verify_rejects_workspace_identity_mismatch(tmp_path: Path) -> None:
    scheduler, workspace, _, _ = _active_write_state(tmp_path)
    with sqlite3.connect(scheduler.paths.database) as connection:
        connection.execute(
            "UPDATE workspaces SET root = ?",
            (str(workspace.parent.resolve()),),
        )

    with pytest.raises(StateError) as invalid:
        inspect_state(scheduler.paths.database)
    assert invalid.value.details["reason"] == "workspace-identity-invalid"


def test_legacy_schema_workspace_prevalidation_folds_root_case(tmp_path: Path) -> None:
    database, workspace, _, _ = _schema_one_active_state(tmp_path)
    original_root = str(workspace.resolve())
    legacy_root = original_root.replace(workspace.name, workspace.name.upper())
    old_workspace_id = _legacy_workspace_id(original_root)
    legacy_workspace_id = hashlib.sha256(
        os.path.normcase(legacy_root).casefold().encode("utf-8")
    ).hexdigest()
    with sqlite3.connect(database) as connection:
        connection.execute("PRAGMA foreign_keys = OFF")
        connection.execute(
            "UPDATE workspaces SET id = ?, root = ? WHERE id = ?",
            (legacy_workspace_id, legacy_root, old_workspace_id),
        )
        for table in ("tasks", "claims", "recovery_events"):
            connection.execute(
                f"UPDATE {table} SET workspace_id = ? WHERE workspace_id = ?",
                (legacy_workspace_id, old_workspace_id),
            )

    assert inspect_state(database)["integrity_check"] == "ok"


@pytest.mark.parametrize(
    ("index_name", "replacement"),
    (
        (
            "tasks_state_expires",
            "CREATE INDEX tasks_state_expires ON tasks(expires_at, state)",
        ),
        (
            "tasks_state_expires",
            "CREATE UNIQUE INDEX tasks_state_expires ON tasks(state, expires_at)",
        ),
        (
            "tasks_state_expires",
            "CREATE INDEX tasks_state_expires ON claims(state, released_at)",
        ),
        (
            "tasks_workspace_terminal_recency",
            (
                "CREATE INDEX tasks_workspace_terminal_recency "
                "ON tasks(workspace_id, finished_at DESC, created_at DESC, id DESC)"
            ),
        ),
        (
            "tasks_workspace_terminal_recency",
            (
                "CREATE INDEX tasks_workspace_terminal_recency "
                "ON tasks(workspace_id, finished_at DESC, created_at DESC, id DESC) "
                "WHERE state IN ('completed', 'failed', 'expired') AND 0"
            ),
        ),
        (
            "tasks_state_expires",
            "CREATE INDEX tasks_state_expires ON tasks(state COLLATE NOCASE, expires_at)",
        ),
    ),
)
def test_verify_rejects_same_name_indexes_with_wrong_definitions(
    tmp_path: Path, index_name: str, replacement: str
) -> None:
    workspace = tmp_path / "index-workspace"
    scheduler = _registered_scheduler(tmp_path / "index-state", workspace)
    with sqlite3.connect(scheduler.paths.database) as connection:
        connection.execute(f'DROP INDEX "{index_name}"')
        connection.execute(replacement)

    with pytest.raises(StateError) as invalid:
        inspect_state(scheduler.paths.database)
    assert invalid.value.details["reason"] == "schema-index-signature-invalid"
    assert invalid.value.details["indexes"] == [index_name]


def test_verify_and_restore_reject_sabotage_trigger_atomically(tmp_path: Path) -> None:
    scheduler, _ = _closed_task_state(tmp_path)
    with sqlite3.connect(scheduler.paths.database) as connection:
        connection.execute(
            "CREATE TRIGGER sabotage_queue_counter AFTER INSERT ON tasks BEGIN "
            "UPDATE workspaces SET next_queue_order = 0; END"
        )

    with pytest.raises(StateError) as invalid:
        verify_state(scheduler.paths.database)
    assert invalid.value.details["reason"] == "schema-declaration-invalid"
    assert invalid.value.details["programs"] == [
        {"type": "trigger", "name": "sabotage_queue_counter"}
    ]

    target = StatePaths(tmp_path / "trigger-restore-target")
    with pytest.raises(StateError) as restore_invalid:
        restore_state(target, scheduler.paths.database, confirm_no_processes=True)
    assert restore_invalid.value.details["reason"] == "schema-declaration-invalid"
    assert not target.root.exists()


def test_schema_one_migration_rejects_trigger_atomically(tmp_path: Path) -> None:
    paths = StatePaths(tmp_path / "trigger-schema-one-state")
    paths.root.mkdir()
    with sqlite3.connect(paths.database) as connection:
        connection.executescript(SCHEMA_ONE_SQL)
        connection.execute(
            "CREATE TRIGGER sabotage_queue_counter AFTER INSERT ON tasks BEGIN "
            "UPDATE workspaces SET epoch = 0; END"
        )

    with pytest.raises(StateError) as verify_invalid:
        verify_state(paths.database, for_migration=True)
    assert verify_invalid.value.details["reason"] == "schema-declaration-invalid"

    with pytest.raises(StateError) as migration_invalid:
        open_database(paths)
    assert migration_invalid.value.details["reason"] == "schema-declaration-invalid"
    with sqlite3.connect(paths.database) as connection:
        assert (
            connection.execute(
                "SELECT value FROM scheduler_meta WHERE key = 'schema_version'"
            ).fetchone()[0]
            == "1"
        )
        assert "next_queue_order" not in {
            row[1] for row in connection.execute("PRAGMA table_info(workspaces)")
        }
        assert (
            connection.execute(
                "SELECT COUNT(*) FROM sqlite_master "
                "WHERE type = 'trigger' AND name = 'sabotage_queue_counter'"
            ).fetchone()[0]
            == 1
        )


@pytest.mark.parametrize(
    "mutation",
    (
        "CREATE UNIQUE INDEX extra_task_owner_unique ON tasks(owner)",
        None,
    ),
)
def test_verify_rejects_extra_unique_or_check_constraints(
    tmp_path: Path,
    mutation: str | None,
) -> None:
    if mutation is None:
        database = _custom_schema_two_state(tmp_path, add_task_check=True)
    else:
        workspace = tmp_path / "extra-constraint-workspace"
        scheduler = _registered_scheduler(tmp_path / "extra-constraint-state", workspace)
        database = scheduler.paths.database
        with sqlite3.connect(database) as connection:
            connection.execute(mutation)

    with pytest.raises(StateError) as invalid:
        verify_state(database)
    assert invalid.value.details["reason"] == "schema-declaration-invalid"


@pytest.mark.parametrize(
    ("options", "primary_keys", "foreign_keys"),
    (
        ({"omit_claim_task_fk": True}, [], ["claims"]),
        ({"omit_recovery_event_pk": True}, ["recovery_events"], []),
    ),
)
def test_verify_rejects_wrong_primary_or_foreign_key_schema_signatures(
    tmp_path: Path,
    options: dict[str, bool],
    primary_keys: list[str],
    foreign_keys: list[str],
) -> None:
    database = _custom_schema_two_state(tmp_path, **options)

    with pytest.raises(StateError) as invalid:
        verify_state(database)

    assert invalid.value.details["reason"] == "schema-relational-signature-invalid"
    assert invalid.value.details["primary_keys"] == primary_keys
    assert invalid.value.details["foreign_keys"] == foreign_keys


def test_verify_and_restore_reject_undeclared_orphan_claim_atomically(
    tmp_path: Path,
) -> None:
    database = _custom_schema_two_state(
        tmp_path,
        omit_claim_task_fk=True,
        orphan_claim=True,
    )

    with pytest.raises(StateError) as invalid:
        verify_state(database)
    assert invalid.value.details["reason"] == "relational-orphan-invalid"
    assert invalid.value.details["references"] == {"claims.task_id": 1}

    target = StatePaths(tmp_path / "orphan-restore-target")
    with pytest.raises(StateError) as restore_invalid:
        restore_state(
            target,
            database,
            confirm_no_processes=True,
            allow_open_claims=True,
        )
    assert restore_invalid.value.details["reason"] == "relational-orphan-invalid"
    assert not target.root.exists()


def test_verify_rejects_malformed_or_duplicate_open_task_tokens(tmp_path: Path) -> None:
    scheduler, workspace, task_id, _ = _active_write_state(tmp_path)
    with sqlite3.connect(scheduler.paths.database) as connection:
        connection.execute(
            "UPDATE tasks SET token_hash = upper(token_hash) WHERE id = ?",
            (task_id,),
        )
    with pytest.raises(StateError) as malformed:
        inspect_state(scheduler.paths.database)
    assert malformed.value.details["reason"] == "open-task-token-invalid"

    second, _ = scheduler.start_task(workspace, "second", "duplicate token hash")
    with sqlite3.connect(scheduler.paths.database) as connection:
        connection.execute(
            "UPDATE tasks SET token_hash = lower(token_hash) WHERE id = ?",
            (task_id,),
        )
        connection.execute(
            "UPDATE tasks SET token_hash = (SELECT token_hash FROM tasks WHERE id = ?) "
            "WHERE id = ?",
            (task_id, second["id"]),
        )
    with pytest.raises(StateError) as duplicate:
        inspect_state(scheduler.paths.database)
    assert duplicate.value.details["reason"] == "open-task-token-duplicate"


@pytest.mark.parametrize("invalid_timing", ["not-a-number", float("inf")])
def test_verify_rejects_nonfinite_unknown_timing_evidence(
    tmp_path: Path, invalid_timing: object
) -> None:
    workspace = tmp_path / "unknown-workspace"
    scheduler = _registered_scheduler(tmp_path / "unknown-state", workspace)
    task, token = scheduler.start_task(workspace, "owner", "unknown outcome")
    scheduler.release_task(workspace, token, result="outcome-unknown")
    with sqlite3.connect(scheduler.paths.database) as connection:
        connection.execute(
            "UPDATE tasks SET expires_at = ? WHERE id = ?",
            (invalid_timing, task["id"]),
        )

    with pytest.raises(StateError) as invalid:
        inspect_state(scheduler.paths.database)
    assert invalid.value.details["reason"] == "outcome-unknown-timing-invalid"


@pytest.mark.parametrize(
    ("state", "result", "finished_at"),
    (
        ("active", "completed", None),
        ("active", None, 1),
        ("outcome_unknown", "completed", 1),
        ("outcome_unknown", "outcome-unknown", None),
        ("completed", "failed", 1),
        ("failed", "completed", 1),
        ("expired", "completed", 1),
        ("completed", "completed", "not-a-number"),
        ("completed", "completed", float("inf")),
    ),
)
def test_verify_rejects_invalid_task_lifecycle_results_and_finished_times(
    tmp_path: Path,
    state: str,
    result: object,
    finished_at: object,
) -> None:
    workspace = tmp_path / "lifecycle-workspace"
    scheduler = _registered_scheduler(tmp_path / "lifecycle-state", workspace)
    task, _ = scheduler.start_task(workspace, "owner", "lifecycle verification")
    with sqlite3.connect(scheduler.paths.database) as connection:
        connection.execute(
            "UPDATE tasks SET state = ?, result = ?, finished_at = ? WHERE id = ?",
            (state, result, finished_at, task["id"]),
        )

    with pytest.raises(StateError) as invalid:
        inspect_state(scheduler.paths.database)
    assert invalid.value.details["reason"] == "task-lifecycle-invalid"


@pytest.mark.parametrize(
    ("state", "result", "finished_at"),
    (
        ("active", None, None),
        ("outcome_unknown", "outcome-unknown", 1),
        ("completed", "completed", 1),
        ("failed", "failed", 1),
        ("expired", "expired", 1),
    ),
)
def test_verify_accepts_valid_task_lifecycle_results_and_finished_times(
    tmp_path: Path,
    state: str,
    result: object,
    finished_at: object,
) -> None:
    workspace = tmp_path / "valid-lifecycle-workspace"
    scheduler = _registered_scheduler(tmp_path / "valid-lifecycle-state", workspace)
    task, token = scheduler.start_task(workspace, "owner", "valid lifecycle verification")
    if state == "outcome_unknown":
        scheduler.release_task(workspace, token, result="outcome-unknown")
    elif state in {"completed", "failed"}:
        cleanup_path = os.path.normpath(str((workspace / f"{state}.token").resolve()))
        released = scheduler.release_task(
            workspace,
            token,
            result=state,
            token_cleanup_path=cleanup_path,
        )
        acknowledged = scheduler.acknowledge_receipt(
            str(released["operation"]["operation_id"]),
            str(released["operation"]["fingerprint"]),
            str(released["operation"]["delivery_digest"]),
        )
        assert acknowledged["acknowledged"] is True
        assert acknowledged["token_file_removed"] is True
    elif state == "expired":
        with open_database(scheduler.paths) as connection:
            connection.execute("UPDATE tasks SET expires_at = 0 WHERE id = ?", (task["id"],))
        scheduler.status(workspace)

    assert inspect_state(scheduler.paths.database)["task_states"] == {state: 1}


@pytest.mark.parametrize("resolution", ["completed", "failed"])
def test_verify_accepts_consistent_recovered_task_and_event(
    tmp_path: Path, resolution: str
) -> None:
    workspace = tmp_path / f"valid-{resolution}-recovery-workspace"
    scheduler = _registered_scheduler(tmp_path / f"valid-{resolution}-recovery-state", workspace)
    task, token = scheduler.start_task(workspace, "owner", "recovery verification")
    scheduler.release_task(workspace, token, result="outcome-unknown")
    scheduler.resolve_unknown(
        workspace,
        str(task["id"]),
        resolution=resolution,
        evidence="verified recovery",
    )

    report = inspect_state(scheduler.paths.database)
    assert report["task_states"] == {resolution: 1}
    assert report["counts"]["recovery_events"] == 1


def test_verify_accepts_normalized_multiline_recovery_evidence(tmp_path: Path) -> None:
    workspace = tmp_path / "multiline-recovery-workspace"
    scheduler = _registered_scheduler(tmp_path / "multiline-recovery-state", workspace)
    task, token = scheduler.start_task(workspace, "owner", "multiline recovery")
    scheduler.release_task(workspace, token, result="outcome-unknown")
    scheduler.resolve_unknown(
        workspace,
        str(task["id"]),
        resolution="completed",
        evidence="first observation\n\tsecond observation",
    )

    assert inspect_state(scheduler.paths.database)["counts"]["recovery_events"] == 1


def _recovered_task_state(
    tmp_path: Path,
) -> tuple[WorkspaceCoordinator, Path, str, str]:
    workspace = tmp_path / "recovery-workspace"
    scheduler = _registered_scheduler(tmp_path / "recovery-state", workspace)
    task, token = scheduler.start_task(workspace, "owner", "recovery verification")
    scheduler.release_task(workspace, token, result="outcome-unknown")
    scheduler.resolve_unknown(
        workspace,
        str(task["id"]),
        resolution="completed",
        evidence="verified recovery",
    )
    with sqlite3.connect(scheduler.paths.database) as connection:
        event_id = connection.execute(
            "SELECT id FROM recovery_events WHERE task_id = ?", (task["id"],)
        ).fetchone()[0]
    return scheduler, workspace, str(task["id"]), str(event_id)


def test_verify_rejects_invalid_recovery_resolution(tmp_path: Path) -> None:
    scheduler, _, _, event_id = _recovered_task_state(tmp_path)
    with sqlite3.connect(scheduler.paths.database) as connection:
        connection.execute(
            "UPDATE recovery_events SET resolution = 'expired' WHERE id = ?",
            (event_id,),
        )

    with pytest.raises(StateError) as invalid:
        inspect_state(scheduler.paths.database)
    assert invalid.value.details["reason"] == "recovery-event-resolution-invalid"


@pytest.mark.parametrize("evidence", [" padded evidence ", "evidence\x00suffix"])
def test_verify_rejects_noncanonical_recovery_evidence(tmp_path: Path, evidence: str) -> None:
    scheduler, _, _, event_id = _recovered_task_state(tmp_path)
    with sqlite3.connect(scheduler.paths.database) as connection:
        connection.execute(
            "UPDATE recovery_events SET evidence = ? WHERE id = ?",
            (evidence, event_id),
        )

    with pytest.raises(StateError) as invalid:
        inspect_state(scheduler.paths.database)
    assert invalid.value.details["reason"] == "recovery-event-evidence-invalid"


@pytest.mark.parametrize("created_at", ["not-a-number", float("inf")])
def test_verify_rejects_invalid_recovery_event_time(tmp_path: Path, created_at: object) -> None:
    scheduler, _, _, event_id = _recovered_task_state(tmp_path)
    with sqlite3.connect(scheduler.paths.database) as connection:
        connection.execute(
            "UPDATE recovery_events SET created_at = ? WHERE id = ?",
            (created_at, event_id),
        )

    with pytest.raises(StateError) as invalid:
        inspect_state(scheduler.paths.database)
    assert invalid.value.details["reason"] == "recovery-event-timing-invalid"


@pytest.mark.parametrize(
    "mutation",
    (
        "UPDATE tasks SET note = note || ' mismatch'",
        "UPDATE recovery_events SET created_at = created_at + 1",
    ),
)
def test_verify_rejects_recovery_event_evidence_or_time_binding(
    tmp_path: Path, mutation: str
) -> None:
    scheduler, _, _, _ = _recovered_task_state(tmp_path)
    with sqlite3.connect(scheduler.paths.database) as connection:
        connection.execute(mutation)

    with pytest.raises(StateError) as invalid:
        inspect_state(scheduler.paths.database)
    assert invalid.value.details["reason"] == "recovery-event-binding-invalid"


def test_verify_rejects_recovery_workspace_mismatch(tmp_path: Path) -> None:
    scheduler, _, _, event_id = _recovered_task_state(tmp_path)
    other_workspace = tmp_path / "other-recovery-workspace"
    other_workspace.mkdir()
    other = scheduler.register(other_workspace)
    with sqlite3.connect(scheduler.paths.database) as connection:
        connection.execute(
            "UPDATE recovery_events SET workspace_id = ? WHERE id = ?",
            (other["id"], event_id),
        )

    with pytest.raises(StateError) as invalid:
        inspect_state(scheduler.paths.database)
    assert invalid.value.details["reason"] == "recovery-event-workspace-mismatch"


def test_verify_rejects_duplicate_recovery_events(tmp_path: Path) -> None:
    scheduler, _, _, event_id = _recovered_task_state(tmp_path)
    with sqlite3.connect(scheduler.paths.database) as connection:
        connection.execute(
            "INSERT INTO recovery_events(id, workspace_id, task_id, resolution, evidence, "
            "created_at) SELECT ?, workspace_id, task_id, resolution, evidence, created_at "
            "FROM recovery_events WHERE id = ?",
            ("duplicate-recovery-event", event_id),
        )

    with pytest.raises(StateError) as invalid:
        inspect_state(scheduler.paths.database)
    assert invalid.value.details["reason"] == "recovery-event-duplicate"
    assert invalid.value.details["task_count"] == 1


@pytest.mark.parametrize(
    "mutation",
    (
        "UPDATE tasks SET result = 'completed'",
        "UPDATE recovery_events SET resolution = 'failed'",
    ),
)
def test_verify_rejects_recovery_event_task_mismatch(tmp_path: Path, mutation: str) -> None:
    scheduler, _, _, _ = _recovered_task_state(tmp_path)
    with sqlite3.connect(scheduler.paths.database) as connection:
        connection.execute(mutation)

    with pytest.raises(StateError) as invalid:
        inspect_state(scheduler.paths.database)
    assert invalid.value.details["reason"] == "recovery-event-task-mismatch"


def test_verify_rejects_recovered_task_without_event(tmp_path: Path) -> None:
    scheduler, _, task_id, _ = _recovered_task_state(tmp_path)
    with sqlite3.connect(scheduler.paths.database) as connection:
        connection.execute("DELETE FROM recovery_events WHERE task_id = ?", (task_id,))

    with pytest.raises(StateError) as invalid:
        inspect_state(scheduler.paths.database)
    assert invalid.value.details["reason"] == "recovered-task-event-missing"


def test_verify_rejects_duplicate_open_claim_queue_order(tmp_path: Path) -> None:
    scheduler, workspace, _, first_claim_id = _active_write_state(tmp_path)
    _, second_token = scheduler.start_task(workspace, "second", "second claim")
    second_claim = scheduler.acquire_claim(
        workspace, second_token, writes=("Assets/Villain.prefab",)
    )
    with sqlite3.connect(scheduler.paths.database) as connection:
        connection.execute(
            "UPDATE claims SET queue_order = "
            "(SELECT queue_order FROM claims WHERE id = ?) WHERE id = ?",
            (first_claim_id, second_claim["id"]),
        )

    with pytest.raises(StateError) as invalid:
        inspect_state(scheduler.paths.database)
    assert invalid.value.details["reason"] == "open-claim-queue-order-duplicate"


def test_schema_two_inspect_rejects_same_task_active_resource_conflict(tmp_path: Path) -> None:
    workspace = tmp_path / "same-task-resource-workspace"
    scheduler = _registered_scheduler(tmp_path / "same-task-resource-state", workspace)
    _, token = scheduler.start_task(workspace, "owner", "same-task resources")
    scheduler.acquire_claim(workspace, token, resources=("unity-live",))
    second = scheduler.acquire_claim(workspace, token, resources=("vcs-maintenance",))
    with sqlite3.connect(scheduler.paths.database) as connection:
        connection.execute(
            "UPDATE claim_scopes SET value = 'unity-live' "
            "WHERE claim_id = ? AND scope_type = 'resource'",
            (second["id"],),
        )

    with pytest.raises(StateError) as invalid:
        inspect_state(scheduler.paths.database)
    assert invalid.value.details["reason"] == "active-claim-conflict"
    assert invalid.value.details["conflict_count"] == 1


def test_schema_two_inspect_allows_same_task_write_overlap_and_own_freeze(
    tmp_path: Path,
) -> None:
    workspace = tmp_path / "same-task-write-freeze-workspace"
    scheduler = _registered_scheduler(tmp_path / "same-task-write-freeze-state", workspace)
    _, token = scheduler.start_task(workspace, "owner", "same-task write and freeze")
    first = scheduler.acquire_claim(workspace, token, writes=("Assets/Hero.prefab",))
    second = scheduler.acquire_claim(
        workspace,
        token,
        writes=("Assets/Hero.prefab/Child",),
    )
    freeze = scheduler.acquire_claim(workspace, token, freeze=True)

    assert {first["state"], second["state"], freeze["state"]} == {"active"}
    assert inspect_state(scheduler.paths.database)["counts"]["active_claims"] == 3


@pytest.mark.parametrize("removed_action", ["task.release", "task.start"])
def test_restore_rejects_terminal_start_with_broken_cleanup_lineage(
    tmp_path: Path,
    removed_action: str,
) -> None:
    workspace = tmp_path / f"cleanup-lineage-{removed_action.replace('.', '-')}"
    scheduler = _registered_scheduler(tmp_path / "cleanup-lineage-source", workspace)
    token_path = os.path.normpath(str((workspace / "owner.token").resolve()))
    _, token = scheduler.start_task(
        workspace,
        "owner",
        "cleanup lineage",
        operation_id=str(uuid.uuid4()),
        token_file_path=token_path,
        token="cleanup-lineage-secret",
    )
    scheduler.release_task(
        workspace,
        token,
        operation_id=str(uuid.uuid4()),
        result="completed",
        token_cleanup_path=token_path,
    )
    backup = tmp_path / "cleanup-lineage-backup.sqlite3"
    backup_state(scheduler.paths, backup, confirm_no_processes=True)
    with sqlite3.connect(backup) as connection:
        connection.execute(
            "DELETE FROM operation_receipts WHERE action = ?",
            (removed_action,),
        )

    target = resolve_state_paths(tmp_path / "cleanup-lineage-target")
    with pytest.raises(StateError) as invalid:
        restore_state(
            target,
            backup,
            confirm_no_processes=True,
        )

    assert invalid.value.details["reason"] == "operation-receipt-invalid"
    assert not target.database.exists()


@pytest.mark.parametrize("route", ["inspect", "restore"])
@pytest.mark.parametrize("mutation", ["orphan", "terminal-without-fence"])
def test_schema_three_rejects_nonterminal_start_without_an_exact_active_task(
    tmp_path: Path,
    route: str,
    mutation: str,
) -> None:
    workspace = tmp_path / f"start-binding-{route}-{mutation}"
    scheduler = _registered_scheduler(tmp_path / "start-binding-state", workspace)
    task, _ = scheduler.start_task(workspace, "owner", "start binding")
    with sqlite3.connect(scheduler.paths.database) as connection:
        if mutation == "orphan":
            connection.execute("DELETE FROM tasks WHERE id = ?", (task["id"],))
        else:
            connection.execute(
                "UPDATE tasks SET state = 'completed', result = 'completed', "
                "finished_at = created_at WHERE id = ?",
                (task["id"],),
            )

    with pytest.raises(StateError) as invalid:
        if route == "inspect":
            inspect_state(scheduler.paths.database)
        else:
            restore_state(
                resolve_state_paths(tmp_path / "start-binding-target"),
                scheduler.paths.database,
                confirm_no_processes=True,
            )

    assert invalid.value.details["reason"] == "operation-receipt-invalid"


@pytest.mark.parametrize("conflict", ["resource", "ancestor", "meta", "freeze"])
def test_verify_rejects_conflicting_cross_task_active_claims(tmp_path: Path, conflict: str) -> None:
    scheduler, _, first_claim_id, second_claim_id = _two_active_claim_state(tmp_path)
    with sqlite3.connect(scheduler.paths.database) as connection:
        if conflict == "resource":
            connection.executemany(
                "INSERT INTO claim_scopes(claim_id, scope_type, value) "
                "VALUES(?, 'resource', 'unity-live')",
                ((first_claim_id,), (second_claim_id,)),
            )
        elif conflict == "ancestor":
            connection.execute(
                "UPDATE claim_scopes SET value = 'assets/heroes' WHERE claim_id = ?",
                (first_claim_id,),
            )
            connection.execute(
                "UPDATE claim_scopes SET value = 'assets/heroes/boss.prefab' WHERE claim_id = ?",
                (second_claim_id,),
            )
        elif conflict == "meta":
            connection.execute(
                "UPDATE claim_scopes SET value = 'assets/hero.prefab.meta' WHERE claim_id = ?",
                (second_claim_id,),
            )
        else:
            connection.execute("DELETE FROM claim_scopes WHERE claim_id = ?", (second_claim_id,))
            connection.execute("UPDATE claims SET kind = 'freeze' WHERE id = ?", (second_claim_id,))

    with pytest.raises(StateError) as invalid:
        inspect_state(scheduler.paths.database)
    assert invalid.value.details["reason"] == "active-claim-conflict"
    assert invalid.value.details["conflict_count"] == 1


def test_expired_unknown_requires_an_owned_active_claim(tmp_path: Path) -> None:
    workspace = tmp_path / "missing-unknown-claim-workspace"
    scheduler = _registered_scheduler(tmp_path / "missing-unknown-claim-state", workspace)
    task, token = scheduler.start_task(workspace, "owner", "unknown without active claim")
    scheduler.release_task(workspace, token, result="outcome-unknown")
    with sqlite3.connect(scheduler.paths.database) as connection:
        connection.execute(
            "UPDATE tasks SET result = 'expired-with-active-claim' WHERE id = ?",
            (task["id"],),
        )

    with pytest.raises(StateError) as invalid:
        inspect_state(scheduler.paths.database)
    assert invalid.value.details["reason"] == "outcome-unknown-active-claim-missing"


def test_expired_unknown_accepts_its_owned_active_claim(tmp_path: Path) -> None:
    scheduler, _, task_id, _ = _active_write_state(tmp_path)
    with sqlite3.connect(scheduler.paths.database) as connection:
        connection.execute("UPDATE tasks SET expires_at = 0 WHERE id = ?", (task_id,))
    scheduler.status(tmp_path / "semantic-workspace")

    report = inspect_state(scheduler.paths.database)
    assert report["task_states"] == {"outcome_unknown": 1}
    assert report["counts"]["active_claims"] == 1


def test_verify_rejects_control_characters_in_ids_and_markers(tmp_path: Path) -> None:
    scheduler, _, _, claim_id = _active_write_state(tmp_path)
    with sqlite3.connect(scheduler.paths.database) as connection:
        connection.execute(
            "INSERT INTO workspaces(id, root, registered_at, epoch, next_queue_order) "
            "VALUES(?, 'C:/control-workspace', 1, 1, 1)",
            ("control\x00workspace",),
        )
    with pytest.raises(StateError) as invalid_id:
        inspect_state(scheduler.paths.database)
    assert invalid_id.value.details["reason"] == "identifier-invalid"

    with sqlite3.connect(scheduler.paths.database) as connection:
        connection.execute("DELETE FROM workspaces WHERE root = 'C:/control-workspace'")
        connection.execute(
            "INSERT INTO claim_scopes(claim_id, scope_type, value) VALUES(?, 'parked_for', ?)",
            (claim_id, "freeze\x00marker"),
        )
    with pytest.raises(StateError) as invalid_marker:
        inspect_state(scheduler.paths.database)
    assert invalid_marker.value.details["reason"] == "claim-scope-value-invalid"


def test_schema_two_parked_markers_require_an_open_same_workspace_freeze(
    tmp_path: Path,
) -> None:
    scheduler, parked_claim_id, _ = _parked_write_state(tmp_path)
    assert inspect_state(scheduler.paths.database)["counts"]["parked_claims"] == 1

    with sqlite3.connect(scheduler.paths.database) as connection:
        connection.execute(
            "UPDATE claim_scopes SET value = 'missing-freeze' "
            "WHERE claim_id = ? AND scope_type = 'parked_for'",
            (parked_claim_id,),
        )
    with pytest.raises(StateError) as invalid:
        inspect_state(scheduler.paths.database)
    assert invalid.value.details["reason"] == "parked-claim-freeze-invalid"


def test_schema_two_parked_marker_rejects_its_own_tasks_open_freeze(
    tmp_path: Path,
) -> None:
    scheduler, parked_claim_id, freeze_id = _parked_write_state(tmp_path)
    with sqlite3.connect(scheduler.paths.database) as connection:
        owner_task_id = connection.execute(
            "SELECT task_id FROM claims WHERE id = ?", (parked_claim_id,)
        ).fetchone()[0]
        connection.execute("UPDATE claims SET task_id = ? WHERE id = ?", (owner_task_id, freeze_id))

    with pytest.raises(StateError) as invalid:
        inspect_state(scheduler.paths.database)
    assert invalid.value.details["reason"] == "parked-claim-freeze-invalid"


def test_schema_two_parked_marker_rejects_another_workspaces_open_freeze(
    tmp_path: Path,
) -> None:
    scheduler, _, freeze_id = _parked_write_state(tmp_path)
    other_workspace = tmp_path / "other-marker-workspace"
    other_workspace.mkdir()
    scheduler.register(other_workspace)
    other_task, _ = scheduler.start_task(other_workspace, "other", "marker target")
    with sqlite3.connect(scheduler.paths.database) as connection:
        other_workspace_id = connection.execute(
            "SELECT workspace_id FROM tasks WHERE id = ?", (other_task["id"],)
        ).fetchone()[0]
        connection.execute(
            "UPDATE claims SET workspace_id = ?, task_id = ? WHERE id = ?",
            (other_workspace_id, other_task["id"], freeze_id),
        )

    with pytest.raises(StateError) as invalid:
        inspect_state(scheduler.paths.database)
    assert invalid.value.details["reason"] == "parked-claim-freeze-invalid"


def test_schema_two_parked_marker_rejects_a_closed_freeze(tmp_path: Path) -> None:
    scheduler, _, freeze_id = _parked_write_state(tmp_path)
    with sqlite3.connect(scheduler.paths.database) as connection:
        connection.execute(
            "UPDATE claims SET state = 'released', released_at = 1 WHERE id = ?",
            (freeze_id,),
        )

    with pytest.raises(StateError) as invalid:
        inspect_state(scheduler.paths.database)
    assert invalid.value.details["reason"] == "parked-claim-freeze-invalid"


def test_schema_two_queued_restoration_marker_allows_a_closed_historical_freeze(
    tmp_path: Path,
) -> None:
    scheduler, parked_claim_id, freeze_id = _parked_write_state(tmp_path)
    with sqlite3.connect(scheduler.paths.database) as connection:
        connection.execute(
            "UPDATE claims SET state = 'queued', granted_at = NULL WHERE id = ?",
            (parked_claim_id,),
        )
        connection.execute(
            "UPDATE claims SET state = 'released', released_at = 1 WHERE id = ?",
            (freeze_id,),
        )

    report = inspect_state(scheduler.paths.database)
    assert report["counts"]["queued_claims"] == 1
    assert report["counts"]["active_claims"] == 0


def test_schema_two_queued_restoration_marker_allows_a_pruned_freeze(
    tmp_path: Path,
) -> None:
    scheduler, parked_claim_id, freeze_id = _parked_write_state(tmp_path)
    with sqlite3.connect(scheduler.paths.database) as connection:
        connection.execute(
            "UPDATE claims SET state = 'queued', granted_at = NULL WHERE id = ?",
            (parked_claim_id,),
        )
        connection.execute("DELETE FROM claims WHERE id = ?", (freeze_id,))

    report = inspect_state(scheduler.paths.database)
    assert report["counts"]["queued_claims"] == 1
    assert report["counts"]["active_claims"] == 0


def test_schema_two_queued_restoration_marker_rejects_an_open_freeze(
    tmp_path: Path,
) -> None:
    scheduler, parked_claim_id, _ = _parked_write_state(tmp_path)
    with sqlite3.connect(scheduler.paths.database) as connection:
        connection.execute(
            "UPDATE claims SET state = 'queued', granted_at = NULL WHERE id = ?",
            (parked_claim_id,),
        )

    with pytest.raises(StateError) as invalid:
        inspect_state(scheduler.paths.database)
    assert invalid.value.details["reason"] == "queued-restoration-freeze-invalid"


def test_schema_two_queued_restoration_marker_rejects_its_own_tasks_closed_freeze(
    tmp_path: Path,
) -> None:
    scheduler, parked_claim_id, freeze_id = _parked_write_state(tmp_path)
    with sqlite3.connect(scheduler.paths.database) as connection:
        owner_task_id = connection.execute(
            "SELECT task_id FROM claims WHERE id = ?", (parked_claim_id,)
        ).fetchone()[0]
        connection.execute(
            "UPDATE claims SET state = 'queued', granted_at = NULL WHERE id = ?",
            (parked_claim_id,),
        )
        connection.execute(
            "UPDATE claims SET state = 'released', released_at = 1, task_id = ? WHERE id = ?",
            (owner_task_id, freeze_id),
        )

    with pytest.raises(StateError) as invalid:
        inspect_state(scheduler.paths.database)
    assert invalid.value.details["reason"] == "queued-restoration-freeze-invalid"


def test_schema_two_queued_restoration_marker_rejects_another_workspaces_closed_freeze(
    tmp_path: Path,
) -> None:
    scheduler, parked_claim_id, freeze_id = _parked_write_state(tmp_path)
    other_workspace = tmp_path / "other-queued-marker-workspace"
    other_workspace.mkdir()
    scheduler.register(other_workspace)
    other_task, _ = scheduler.start_task(other_workspace, "other", "marker target")
    with sqlite3.connect(scheduler.paths.database) as connection:
        other_workspace_id = connection.execute(
            "SELECT workspace_id FROM tasks WHERE id = ?", (other_task["id"],)
        ).fetchone()[0]
        connection.execute(
            "UPDATE claims SET state = 'queued', granted_at = NULL WHERE id = ?",
            (parked_claim_id,),
        )
        connection.execute(
            "UPDATE claims SET state = 'released', released_at = 1, "
            "workspace_id = ?, task_id = ? WHERE id = ?",
            (other_workspace_id, other_task["id"], freeze_id),
        )

    with pytest.raises(StateError) as invalid:
        inspect_state(scheduler.paths.database)
    assert invalid.value.details["reason"] == "queued-restoration-freeze-invalid"


@pytest.mark.parametrize("inconsistency", ["target", "phase"])
def test_schema_two_rejects_inconsistent_restoration_claims_within_one_task(
    tmp_path: Path, inconsistency: str
) -> None:
    scheduler, workspace, claim_ids, _ = _multi_parked_write_state(tmp_path)
    assert inspect_state(scheduler.paths.database)["counts"]["parked_claims"] == 2

    if inconsistency == "target":
        _, second_freeze_token = scheduler.start_task(
            workspace, "second-maintenance", "second freeze"
        )
        second_freeze = scheduler.acquire_claim(workspace, second_freeze_token, freeze=True)
        with sqlite3.connect(scheduler.paths.database) as connection:
            connection.execute(
                "UPDATE claim_scopes SET value = ? "
                "WHERE claim_id = ? AND scope_type = 'parked_for'",
                (second_freeze["id"], claim_ids[1]),
            )
    else:
        with sqlite3.connect(scheduler.paths.database) as connection:
            connection.execute(
                "UPDATE claims SET state = 'queued', granted_at = NULL WHERE id = ?",
                (claim_ids[1],),
            )

    with pytest.raises(StateError) as invalid:
        inspect_state(scheduler.paths.database)
    assert invalid.value.details["reason"] == "restoration-claim-group-invalid"


@pytest.mark.parametrize(
    "mutation",
    (
        "DELETE FROM claim_scopes WHERE scope_type = 'parked_for'",
        "UPDATE claims SET state = 'released', released_at = 1 WHERE state = 'parked'",
        (
            "UPDATE claims SET state = 'queued' WHERE state = 'parked'; "
            "INSERT INTO claim_scopes(claim_id, scope_type, value) "
            "SELECT id, 'parked_for', 'another-freeze' FROM claims WHERE kind = 'normal'"
        ),
    ),
)
def test_schema_two_rejects_missing_misplaced_or_duplicate_park_markers(
    tmp_path: Path,
    mutation: str,
) -> None:
    scheduler, _, _ = _parked_write_state(tmp_path)
    with sqlite3.connect(scheduler.paths.database) as connection:
        connection.executescript(mutation)
    with pytest.raises(StateError) as invalid:
        inspect_state(scheduler.paths.database)
    assert invalid.value.details["reason"] == "parked-claim-marker-invalid"


@pytest.mark.parametrize("state", ["queued", "parked"])
def test_schema_two_restoration_pending_claim_must_be_path_only(tmp_path: Path, state: str) -> None:
    scheduler, parked_claim_id, _ = _parked_write_state(tmp_path)
    with sqlite3.connect(scheduler.paths.database) as connection:
        connection.execute("UPDATE claims SET state = ? WHERE id = ?", (state, parked_claim_id))
        connection.execute(
            "INSERT INTO claim_scopes(claim_id, scope_type, value) "
            "VALUES(?, 'resource', 'unity-live')",
            (parked_claim_id,),
        )
    with pytest.raises(StateError) as invalid:
        inspect_state(scheduler.paths.database)
    assert invalid.value.details["reason"] == "queued-restoration-scope-invalid"


def test_restore_rejects_unknown_task_with_nonfinite_timing(tmp_path: Path) -> None:
    workspace = tmp_path / "unknown-restore-workspace"
    scheduler = _registered_scheduler(tmp_path / "unknown-restore-source", workspace)
    task, token = scheduler.start_task(workspace, "owner", "unknown restore")
    scheduler.release_task(workspace, token, result="outcome-unknown")
    backup = tmp_path / "unknown-restore.sqlite3"
    backup_state(scheduler.paths, backup, confirm_no_processes=True)
    with sqlite3.connect(backup) as connection:
        connection.execute(
            "UPDATE tasks SET heartbeat_at = 'invalid' WHERE id = ?",
            (task["id"],),
        )

    target = StatePaths(tmp_path / "unknown-restore-target")
    with pytest.raises(StateError) as invalid:
        restore_state(
            target,
            backup,
            confirm_no_processes=True,
            allow_open_claims=True,
        )
    assert invalid.value.details["reason"] == "outcome-unknown-timing-invalid"
    assert not target.database.exists()


@pytest.mark.parametrize(
    "mutation",
    (
        "UPDATE claim_scopes SET value = '..' WHERE scope_type = 'write'",
        "UPDATE claim_scopes SET scope_type = 'resource', value = ' Unity-Live '",
    ),
)
def test_restore_rejects_malformed_open_claim_scope_values(
    tmp_path: Path,
    mutation: str,
) -> None:
    scheduler, _, _, _ = _active_write_state(tmp_path)
    backup = tmp_path / "malformed-source.sqlite3"
    backup_state(scheduler.paths, backup, confirm_no_processes=True)
    with sqlite3.connect(backup) as connection:
        connection.executescript(mutation)

    target = StatePaths(tmp_path / "malformed-target")
    with pytest.raises(StateError) as invalid:
        restore_state(
            target,
            backup,
            confirm_no_processes=True,
            allow_open_claims=True,
        )
    assert invalid.value.details["reason"] == "claim-scope-value-invalid"
    assert not target.database.exists()


def test_restore_is_atomic_to_missing_target_and_requires_open_claim_opt_in(
    tmp_path: Path,
) -> None:
    workspace = tmp_path / "workspace"
    source_scheduler = _registered_scheduler(tmp_path / "source-state", workspace)
    _, token = source_scheduler.start_task(workspace, "owner", "open work")
    source_scheduler.acquire_claim(workspace, token, resources=("unity-live",))
    backup = tmp_path / "backup.sqlite3"
    backup_state(source_scheduler.paths, backup, confirm_no_processes=True)

    restored_paths = resolve_state_paths(tmp_path / "restored-state")
    with pytest.raises(UsageError) as open_claims:
        restore_state(restored_paths, backup, confirm_no_processes=True)
    assert open_claims.value.details["reason"] == "restore-source-has-open-claims"
    assert not restored_paths.database.exists()

    restored = restore_state(
        restored_paths,
        backup,
        confirm_no_processes=True,
        allow_open_claims=True,
    )
    assert restored["replaced_empty_state"] is False
    assert restored["preserved_open_claims"] == 1
    assert restored["restored"]["counts"]["open_claims"] == 1
    assert inspect_state(restored_paths.database)["integrity_check"] == "ok"


def test_restore_open_claim_gate_uses_the_staged_snapshot(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    workspace = tmp_path / "staged-claim-workspace"
    scheduler = _registered_scheduler(tmp_path / "staged-claim-source", workspace)
    target = StatePaths(tmp_path / "staged-claim-target")
    sqlite_backup = state_ops_module._sqlite_backup

    def add_claim_then_snapshot(source: Path, temporary) -> None:
        _, token = scheduler.start_task(workspace, "racer", "late active claim")
        scheduler.acquire_claim(workspace, token, resources=("late-resource",))
        sqlite_backup(source, temporary)

    monkeypatch.setattr(state_ops_module, "_sqlite_backup", add_claim_then_snapshot)

    with pytest.raises(UsageError) as blocked:
        restore_state(target, scheduler.paths.database, confirm_no_processes=True)
    assert blocked.value.details["reason"] == "restore-source-has-open-claims"
    assert blocked.value.details["open_claims"] == 1
    assert not target.database.exists()
    assert not state_ops_module._restore_quarantine_path(target.database).exists()
    assert list(target.root.glob(".scheduler-state-*.sqlite3.tmp")) == []


def test_restore_replaces_only_verified_empty_state(tmp_path: Path) -> None:
    source_scheduler, _ = _closed_task_state(tmp_path)
    backup = tmp_path / "backup.sqlite3"
    backup_state(source_scheduler.paths, backup, confirm_no_processes=True)

    empty_workspace = tmp_path / "empty-workspace"
    empty_scheduler = _registered_scheduler(tmp_path / "empty-target", empty_workspace)
    _mark_finalized_receipts_delivered(empty_scheduler)
    restored = restore_state(
        empty_scheduler.paths,
        backup,
        confirm_no_processes=True,
        replace_empty=True,
    )
    assert restored["replaced_empty_state"] is True
    assert restored["restored"]["counts"]["tasks"] == 1
    assert restored["cleanup_pending"] == []
    assert not state_ops_module._restore_quarantine_path(empty_scheduler.paths.database).exists()

    nonempty_workspace = tmp_path / "nonempty-workspace"
    nonempty_scheduler = _registered_scheduler(tmp_path / "nonempty-target", nonempty_workspace)
    nonempty_scheduler.start_task(nonempty_workspace, "owner", "must survive")
    with pytest.raises(UsageError) as refused:
        restore_state(
            nonempty_scheduler.paths,
            backup,
            confirm_no_processes=True,
            replace_empty=True,
        )
    assert refused.value.details["reason"] == "restore-target-not-empty"
    assert inspect_state(nonempty_scheduler.paths.database)["counts"]["tasks"] == 1


def test_restore_preserves_nonempty_target_race_and_evidence(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    source_scheduler, _ = _closed_task_state(tmp_path)
    backup = tmp_path / "nonempty-race-backup.sqlite3"
    backup_state(source_scheduler.paths, backup, confirm_no_processes=True)
    target_workspace = tmp_path / "nonempty-race-target-workspace"
    target_scheduler = _registered_scheduler(
        tmp_path / "nonempty-race-target-state",
        target_workspace,
    )
    _mark_finalized_receipts_delivered(target_scheduler)
    create_quarantine = state_ops_module._create_restore_quarantine

    def create_then_mutate(target: Path) -> Path:
        quarantine = create_quarantine(target)
        target_scheduler.start_task(target_workspace, "racer", "must survive")
        return quarantine

    monkeypatch.setattr(
        state_ops_module,
        "_create_restore_quarantine",
        create_then_mutate,
    )

    with pytest.raises(StateError) as invalid:
        restore_state(
            target_scheduler.paths,
            backup,
            confirm_no_processes=True,
            replace_empty=True,
        )
    assert invalid.value.details["reason"] == "restore-recovery-required"
    assert invalid.value.details["publication_uncertain"] is False
    assert Path(invalid.value.details["quarantine"]).is_dir()
    assert Path(invalid.value.details["staged"]).is_file()
    assert inspect_state(target_scheduler.paths.database)["counts"]["tasks"] == 1


def test_restore_preserves_workspace_only_same_inode_race(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    source_scheduler, _ = _closed_task_state(tmp_path)
    backup = tmp_path / "workspace-race-backup.sqlite3"
    backup_state(source_scheduler.paths, backup, confirm_no_processes=True)
    target_workspace = tmp_path / "workspace-race-target-workspace"
    target_scheduler = _registered_scheduler(
        tmp_path / "workspace-race-target-state",
        target_workspace,
    )
    _mark_finalized_receipts_delivered(target_scheduler)
    concurrent_workspace = tmp_path / "workspace-race-concurrent-workspace"
    concurrent_workspace.mkdir()
    create_quarantine = state_ops_module._create_restore_quarantine

    def create_then_register(target: Path) -> Path:
        quarantine = create_quarantine(target)
        target_scheduler.register(concurrent_workspace)
        return quarantine

    monkeypatch.setattr(
        state_ops_module,
        "_create_restore_quarantine",
        create_then_register,
    )

    with pytest.raises(StateError) as invalid:
        restore_state(
            target_scheduler.paths,
            backup,
            confirm_no_processes=True,
            replace_empty=True,
        )
    assert invalid.value.details["reason"] == "restore-recovery-required"
    assert invalid.value.details["publication_uncertain"] is False
    _mark_finalized_receipts_delivered(target_scheduler)
    report = _checkpoint_and_inspect(target_scheduler)
    assert report["counts"]["workspaces"] == 2
    assert report["counts"]["tasks"] == 0
    assert Path(invalid.value.details["quarantine"]).is_dir()
    assert Path(invalid.value.details["staged"]).is_file()


def test_restore_never_overwrites_target_created_after_quarantine(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    source_scheduler, _ = _closed_task_state(tmp_path)
    backup = tmp_path / "target-race-backup.sqlite3"
    backup_state(source_scheduler.paths, backup, confirm_no_processes=True)
    target = StatePaths(tmp_path / "target-race-state")
    create_quarantine = state_ops_module._create_restore_quarantine

    def create_then_race(destination: Path) -> Path:
        quarantine = create_quarantine(destination)
        destination.write_bytes(b"concurrent-state")
        return quarantine

    monkeypatch.setattr(
        state_ops_module,
        "_create_restore_quarantine",
        create_then_race,
    )

    with pytest.raises(StateError) as invalid:
        restore_state(target, backup, confirm_no_processes=True)
    assert invalid.value.details["reason"] == "restore-recovery-required"
    assert target.database.read_bytes() == b"concurrent-state"
    assert Path(invalid.value.details["quarantine"]).is_dir()
    assert Path(invalid.value.details["staged"]).is_file()


def test_restore_postcommit_sidecar_race_is_publication_uncertain(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    source_scheduler, _ = _closed_task_state(tmp_path)
    backup = tmp_path / "sidecar-race-backup.sqlite3"
    backup_state(source_scheduler.paths, backup, confirm_no_processes=True)
    target = StatePaths(tmp_path / "sidecar-race-state")
    publish = state_ops_module._publish_without_overwrite

    def publish_then_create_sidecar(temporary, destination: Path) -> None:
        publish(temporary, destination)
        Path(f"{destination}-journal").write_bytes(b"concurrent-sidecar")

    monkeypatch.setattr(
        state_ops_module,
        "_publish_without_overwrite",
        publish_then_create_sidecar,
    )

    with pytest.raises(StateError) as invalid:
        restore_state(target, backup, confirm_no_processes=True)
    assert invalid.value.details["reason"] == "restore-publication-uncertain"
    assert invalid.value.details["publication_uncertain"] is True
    assert target.database.is_file()
    assert Path(f"{target.database}-journal").read_bytes() == b"concurrent-sidecar"
    assert Path(invalid.value.details["quarantine"]).is_dir()
    assert Path(invalid.value.details["staged"]).is_file()


def test_restore_postcommit_nonzero_quarantine_wal_preserves_evidence(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setattr(state_ops_module, "_verify_windows_maintenance_acl", lambda _path: None)
    source_scheduler, _ = _closed_task_state(tmp_path)
    backup = tmp_path / "quarantine-wal-backup.sqlite3"
    backup_state(source_scheduler.paths, backup, confirm_no_processes=True)
    target_workspace = tmp_path / "quarantine-wal-target-workspace"
    target_scheduler = _registered_scheduler(
        tmp_path / "quarantine-wal-target-state",
        target_workspace,
    )
    _mark_finalized_receipts_delivered(target_scheduler)
    _checkpoint_and_inspect(target_scheduler)
    checkpoint = state_ops_module._checkpoint_empty_target
    quarantine_calls = 0

    def checkpoint_then_inject_wal(path: Path):
        nonlocal quarantine_calls
        result = checkpoint(path)
        if path.parent == state_ops_module._restore_quarantine_path(
            target_scheduler.paths.database
        ):
            quarantine_calls += 1
            if quarantine_calls == 1:
                Path(f"{path}-wal").write_bytes(b"late-quarantine-wal")
        return result

    monkeypatch.setattr(
        state_ops_module,
        "_checkpoint_empty_target",
        checkpoint_then_inject_wal,
    )

    with pytest.raises(StateError) as invalid:
        restore_state(
            target_scheduler.paths,
            backup,
            confirm_no_processes=True,
            replace_empty=True,
        )
    assert invalid.value.details["reason"] == "restore-publication-uncertain"
    quarantine = Path(invalid.value.details["quarantine"])
    assert Path(f"{quarantine / target_scheduler.paths.database.name}-wal").read_bytes() == (
        b"late-quarantine-wal"
    )
    assert target_scheduler.paths.database.is_file()
    assert Path(invalid.value.details["staged"]).is_file()


def test_restore_repeat_is_blocked_by_persistent_quarantine(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setattr(state_ops_module, "_verify_windows_maintenance_acl", lambda _path: None)
    source_scheduler, _ = _closed_task_state(tmp_path)
    backup = tmp_path / "repeat-backup.sqlite3"
    backup_state(source_scheduler.paths, backup, confirm_no_processes=True)
    target = StatePaths(tmp_path / "repeat-target-state")
    target.root.mkdir()
    quarantine = state_ops_module._restore_quarantine_path(target.database)
    quarantine.mkdir()

    with pytest.raises(StateError) as blocked:
        restore_state(target, backup, confirm_no_processes=True)
    assert blocked.value.details["reason"] == "restore-recovery-required"
    assert blocked.value.details["recovery_required"] is True
    assert blocked.value.details["quarantine"] == str(quarantine)
    assert quarantine.is_dir()
    assert not target.database.exists()
    staged = Path(blocked.value.details["staged"])
    assert staged.is_file()
    assert staged.parent == target.root
    assert list(target.root.glob(".scheduler-state-*.sqlite3.tmp")) == [staged]


def test_backup_postcommit_verification_failure_preserves_both_paths(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    scheduler, _ = _closed_task_state(tmp_path)
    backup = tmp_path / "uncertain-backup.sqlite3"

    def fail_final_inspection(*_args, **_kwargs):
        raise StateError(
            "injected final inspection failure",
            details={"reason": "injected-final-inspection"},
        )

    monkeypatch.setattr(
        state_ops_module,
        "_inspect_published_snapshot",
        fail_final_inspection,
    )
    with pytest.raises(StateError) as uncertain:
        backup_state(scheduler.paths, backup, confirm_no_processes=True)
    assert uncertain.value.details["reason"] == "backup-publication-uncertain"
    assert uncertain.value.details["cause_reason"] == "injected-final-inspection"
    assert backup.is_file()
    assert Path(uncertain.value.details["staged"]).is_file()
    with sqlite3.connect(backup) as connection:
        assert connection.execute("PRAGMA integrity_check").fetchone()[0] == "ok"


def test_backup_precommit_cleanup_failure_does_not_mask_the_primary_error(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    scheduler, _ = _closed_task_state(tmp_path)
    backup = tmp_path / "precommit-cleanup-backup.sqlite3"
    cleanup = state_ops_module._cleanup_temporary_database

    def fail_snapshot(*_args, **_kwargs) -> None:
        raise StateError(
            "injected snapshot failure",
            details={"reason": "injected-snapshot-failure"},
        )

    def report_cleanup_pending(temporary, **_kwargs) -> list[str]:
        cleanup(temporary)
        return [str(temporary.path)]

    monkeypatch.setattr(state_ops_module, "_sqlite_backup", fail_snapshot)
    monkeypatch.setattr(
        state_ops_module,
        "_cleanup_temporary_database",
        report_cleanup_pending,
    )

    with pytest.raises(StateError) as invalid:
        backup_state(scheduler.paths, backup, confirm_no_processes=True)
    assert invalid.value.details["reason"] == "injected-snapshot-failure"
    assert invalid.value.details["cleanup_pending"]
    assert invalid.value.details["recovery_required"] is True
    assert not backup.exists()


def test_backup_rejects_staged_transaction_sidecars_before_commit(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    scheduler, _ = _closed_task_state(tmp_path)
    backup = tmp_path / "staged-sidecar-backup.sqlite3"
    inspect = state_ops_module.inspect_state

    def inspect_then_inject_journal(path: Path) -> dict[str, object]:
        report = inspect(path)
        if path.name.startswith(".scheduler-state-"):
            Path(f"{path}-journal").write_bytes(b"pending-transaction")
        return report

    monkeypatch.setattr(state_ops_module, "inspect_state", inspect_then_inject_journal)

    with pytest.raises(StateError) as invalid:
        backup_state(scheduler.paths, backup, confirm_no_processes=True)
    assert invalid.value.details["reason"] == "staged-snapshot-sidecars-present"
    assert not backup.exists()


def test_backup_cleanup_failure_is_success_with_cleanup_pending(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    scheduler, _ = _closed_task_state(tmp_path)
    backup = tmp_path / "cleanup-pending-backup.sqlite3"
    cleanup = state_ops_module._cleanup_temporary_database

    def cleanup_with_pending(temporary, **_kwargs) -> list[str]:
        cleanup(temporary)
        return [str(temporary.path)]

    monkeypatch.setattr(
        state_ops_module,
        "_cleanup_temporary_database",
        cleanup_with_pending,
    )
    result = backup_state(scheduler.paths, backup, confirm_no_processes=True)
    assert result["backup"]["integrity_check"] == "ok"
    assert result["cleanup_pending"]
    assert result["durability_pending_parent"] == []
    assert inspect_state(backup)["counts"]["tasks"] == 1


def test_state_operations_reject_explicit_file_symlinks(tmp_path: Path) -> None:
    source_scheduler, _ = _closed_task_state(tmp_path)
    backup = tmp_path / "backup.sqlite3"
    backup_state(source_scheduler.paths, backup, confirm_no_processes=True)
    input_link = tmp_path / "input-link.sqlite3"
    try:
        input_link.symlink_to(backup)
    except OSError as exc:
        pytest.skip(f"File symlinks are unavailable: {exc}")

    with pytest.raises(UsageError) as linked_input:
        verify_state(input_link)
    assert linked_input.value.details["reason"] == "state-file-symlink"

    output_link = tmp_path / "output-link.sqlite3"
    output_link.symlink_to(backup)
    with pytest.raises(UsageError) as linked_output:
        backup_state(source_scheduler.paths, output_link, confirm_no_processes=True)
    assert linked_output.value.details["reason"] == "state-file-symlink"

    target_paths = resolve_state_paths(tmp_path / "linked-target")
    target_paths.database.symlink_to(backup)
    with pytest.raises(UsageError) as linked_target:
        restore_state(target_paths, backup, confirm_no_processes=True)
    assert linked_target.value.details["reason"] == "state-file-symlink"


def test_runtime_revocation_proofs_survive_inspect_verify_and_backup(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    # The Windows canary's inherited Temp/maintenance ACL is intentionally
    # rejected in this sandbox.  Keep this protocol test synthetic and patch
    # only the OS ACL probes; production code still fails closed.
    monkeypatch.setattr(state_module, "_validate_windows_token_location", lambda _path: None)
    monkeypatch.setattr(state_module, "_verify_windows_token_acl", lambda _descriptor: None)
    monkeypatch.setattr(
        state_module,
        "_validate_windows_token_descriptor",
        lambda _descriptor: None,
    )
    monkeypatch.setattr(state_ops_module, "_verify_windows_maintenance_acl", lambda _path: None)
    workspace = tmp_path / "revocation-workspace"
    workspace.mkdir()
    scheduler = WorkspaceCoordinator(resolve_state_paths(tmp_path / "revocation-state"))
    scheduler.register(workspace, operation_id=str(uuid.uuid4()))
    token = "revocation-proof-secret"
    token_path = (
        Path(tempfile.gettempdir()) / f"scheduler-revocation-owner-{uuid.uuid4().hex}.token"
    )
    create_token_file(token_path, token)
    scheduler.start_task(
        workspace,
        "owner",
        "revocation proofs",
        operation_id=str(uuid.uuid4()),
        token_file_path=str(token_path),
        token=token,
    )
    active = scheduler.acquire_claim(
        workspace,
        token,
        operation_id=str(uuid.uuid4()),
        resources=("unity-live",),
    )
    queued = scheduler.acquire_claim(
        workspace,
        token,
        operation_id=str(uuid.uuid4()),
        resources=("unity-live",),
        keep_queued=True,
    )
    _ = scheduler.cancel_claim(
        workspace,
        token,
        str(queued["id"]),
        operation_id=str(uuid.uuid4()),
    )
    _ = scheduler.release_claim(
        workspace,
        token,
        str(active["id"]),
        operation_id=str(uuid.uuid4()),
    )
    terminal = scheduler.release_task(
        workspace,
        token,
        operation_id=str(uuid.uuid4()),
        result="completed",
        token_cleanup_path=str(token_path),
    )
    scheduler.acknowledge_receipt(
        terminal["operation"]["operation_id"],
        terminal["operation"]["fingerprint"],
        terminal["operation"]["delivery_digest"],
    )

    with sqlite3.connect(scheduler.paths.database) as connection:
        proofs = {
            action: json.loads(proof)
            for action, proof in connection.execute(
                "SELECT action, terminal_json FROM operation_receipts "
                "WHERE action IN ('claim.release', 'queue.cancel', 'task.release')"
            )
            if proof is not None
        }
    assert proofs["claim.release"]["token_cleanup_completed"] is True
    assert proofs["queue.cancel"]["token_cleanup_completed"] is True
    assert "task.release" not in proofs
    report = inspect_state(scheduler.paths.database)
    assert verify_state(scheduler.paths.database)["integrity_check"] == "ok"
    backup = backup_state(
        scheduler.paths,
        tmp_path / "revocation-backup.sqlite3",
        confirm_no_processes=True,
    )
    assert backup["backup"]["integrity_check"] == "ok"
    assert backup["durability_pending_parent"] == []
    assert inspect_state(tmp_path / "revocation-backup.sqlite3")["counts"] == report["counts"]
    restored_paths = resolve_state_paths(tmp_path / "revocation-restored-state")
    restored = restore_state(
        restored_paths,
        tmp_path / "revocation-backup.sqlite3",
        confirm_no_processes=True,
    )
    assert restored["restored"]["integrity_check"] == "ok"
    assert verify_state(restored_paths.database)["integrity_check"] == "ok"


def test_revocation_proof_rejects_wrong_action_state_or_cleanup_marker() -> None:
    proof = {
        "aborted": True,
        "reason": "task-released",
        "terminal_finished_at": 20.0,
        "terminal_result": "completed",
        "terminal_state": "completed",
    }
    claim_result = {"id": "claim", "task_id": "task", "state": "released"}
    with pytest.raises(ValueError):
        _validate_lifecycle_terminal_proof("claim.release", proof, 10.0, claim_result)
    with pytest.raises(ValueError):
        _validate_lifecycle_terminal_proof(
            "queue.cancel",
            {**proof, "token_cleanup_completed": True},
            10.0,
            {**claim_result, "state": "released"},
        )
    _validate_lifecycle_terminal_proof(
        "claim.release",
        {**proof, "token_cleanup_completed": True},
        10.0,
        claim_result,
    )


@pytest.mark.skipif(os.name == "nt", reason="POSIX preserves write-scope case")
def test_verify_accepts_runtime_posix_write_scope_case(tmp_path: Path) -> None:
    scheduler, _, _, claim_id = _active_write_state(tmp_path)
    with sqlite3.connect(scheduler.paths.database) as connection:
        connection.execute(
            "UPDATE claim_scopes SET value = 'Assets/Hero.prefab' "
            "WHERE claim_id = ? AND scope_type = 'write'",
            (claim_id,),
        )

    assert inspect_state(scheduler.paths.database)["integrity_check"] == "ok"


def test_backup_directory_barrier_failure_preserves_publication_custody(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setattr(state_ops_module, "_verify_windows_maintenance_acl", lambda _path: None)
    scheduler, _ = _closed_task_state(tmp_path)
    backup = tmp_path / "barrier-failed-backup.sqlite3"

    def fail_barrier(_path: Path) -> None:
        raise OSError("injected directory barrier failure")

    monkeypatch.setattr(state_ops_module, "_durable_directory_barrier", fail_barrier)
    with pytest.raises(StateError) as failed:
        backup_state(scheduler.paths, backup, confirm_no_processes=True)

    assert failed.value.details["recovery_required"] is True
    assert failed.value.details["reason"] == "backup-publication-uncertain"
    assert backup.is_file()
    assert Path(failed.value.details["staged"]).is_file()


def test_backup_cleanup_barrier_failure_returns_explicit_pending_warning(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setattr(state_ops_module, "_verify_windows_maintenance_acl", lambda _path: None)
    scheduler, _ = _closed_task_state(tmp_path)
    backup = tmp_path / "cleanup-barrier-warning-backup.sqlite3"
    calls = 0

    def fail_cleanup_barrier(_path: Path) -> None:
        nonlocal calls
        calls += 1
        if calls == 2:
            raise OSError("injected backup cleanup barrier failure")

    monkeypatch.setattr(state_ops_module, "_durable_directory_barrier", fail_cleanup_barrier)
    result = backup_state(scheduler.paths, backup, confirm_no_processes=True)

    assert not any(Path(path).is_dir() for path in result["cleanup_pending"])
    assert result["durability_pending_parent"]
    assert all(Path(path).is_dir() for path in result["durability_pending_parent"])
    assert backup.is_file()


def test_cleanup_barrier_failures_continue_across_all_exact_entries(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    cleanup_root = tmp_path / "cleanup-entries"
    cleanup_root.mkdir()
    entries = [cleanup_root / f"entry-{index}" for index in range(3)]
    for entry in entries:
        entry.write_text("entry", encoding="utf-8")
    calls = 0

    def fail_first_barriers(_path: Path) -> None:
        nonlocal calls
        calls += 1
        if calls <= 2:
            raise OSError("injected cleanup barrier failure")

    monkeypatch.setattr(state_ops_module, "_durable_directory_barrier", fail_first_barriers)
    evidence: dict[str, list[str]] = {"durability_pending_parent": []}
    pending = state_ops_module._cleanup_maintenance_entries(entries, evidence=evidence)

    assert pending == []
    assert all(not entry.exists() for entry in entries)
    assert len(evidence["durability_pending_parent"]) == 2


def test_restore_quarantine_cleanup_reports_parent_barrier_without_skipping_entries(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    target = tmp_path / "state.sqlite3"
    quarantine = tmp_path / "state.sqlite3.restore-quarantine"
    quarantine.mkdir()
    target.write_text("state", encoding="utf-8")
    for suffix in ("-wal", "-shm", "-journal"):
        (quarantine / f"{target.name}{suffix}").write_text("sidecar", encoding="utf-8")
    (quarantine / target.name).write_text("state", encoding="utf-8")

    def fail_barrier(_path: Path) -> None:
        raise OSError("injected quarantine barrier failure")

    monkeypatch.setattr(state_ops_module, "_durable_directory_barrier", fail_barrier)
    evidence: dict[str, list[str]] = {"durability_pending_parent": []}
    pending = state_ops_module._cleanup_restore_quarantine(quarantine, target, evidence=evidence)

    assert pending == []
    assert not quarantine.exists()
    assert evidence["durability_pending_parent"]


def test_restore_directory_barrier_failure_preserves_quarantine_and_staging(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setattr(state_ops_module, "_verify_windows_maintenance_acl", lambda _path: None)
    source_scheduler, _ = _closed_task_state(tmp_path)
    backup = tmp_path / "restore-barrier-source.sqlite3"
    backup_state(source_scheduler.paths, backup, confirm_no_processes=True)
    target = StatePaths(tmp_path / "restore-barrier-target")
    calls = 0

    def fail_publish_barrier(_path: Path) -> None:
        nonlocal calls
        calls += 1
        if calls == 2:
            raise OSError("injected publish directory barrier failure")

    monkeypatch.setattr(state_ops_module, "_durable_directory_barrier", fail_publish_barrier)
    with pytest.raises(StateError) as failed:
        restore_state(target, backup, confirm_no_processes=True)

    assert failed.value.details["recovery_required"] is True
    assert failed.value.details["reason"] == "restore-publication-uncertain"
    assert target.database.is_file()
    assert Path(failed.value.details["quarantine"]).is_dir()
    assert Path(failed.value.details["staged"]).is_file()


def test_restore_quarantine_create_barrier_failure_preserves_custody(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setattr(state_ops_module, "_verify_windows_maintenance_acl", lambda _path: None)
    source_scheduler, _ = _closed_task_state(tmp_path)
    backup = tmp_path / "quarantine-create-barrier-source.sqlite3"
    backup_state(source_scheduler.paths, backup, confirm_no_processes=True)
    target = StatePaths(tmp_path / "quarantine-create-barrier-target")

    def fail_barrier(_path: Path) -> None:
        raise OSError("injected quarantine create barrier failure")

    monkeypatch.setattr(state_ops_module, "_durable_directory_barrier", fail_barrier)
    with pytest.raises(StateError) as failed:
        restore_state(target, backup, confirm_no_processes=True)

    assert failed.value.details["reason"] == "restore-recovery-required"
    assert failed.value.details["recovery_required"] is True
    assert Path(failed.value.details["quarantine"]).is_dir()
    assert Path(failed.value.details["staged"]).is_file()


@pytest.mark.parametrize("failure_call", [2, 3])
def test_restore_old_target_move_barrier_failure_preserves_custody(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    failure_call: int,
) -> None:
    monkeypatch.setattr(state_ops_module, "_verify_windows_maintenance_acl", lambda _path: None)
    source_scheduler, _ = _closed_task_state(tmp_path)
    backup = tmp_path / "old-target-move-barrier-source.sqlite3"
    backup_state(source_scheduler.paths, backup, confirm_no_processes=True)
    target_workspace = tmp_path / "old-target-move-barrier-workspace"
    target_scheduler = _registered_scheduler(
        tmp_path / "old-target-move-barrier-target",
        target_workspace,
    )
    _mark_finalized_receipts_delivered(target_scheduler)
    calls = 0

    def fail_old_target_move_barrier(_path: Path) -> None:
        nonlocal calls
        calls += 1
        if calls == failure_call:
            raise OSError("injected old target move barrier failure")

    monkeypatch.setattr(
        state_ops_module,
        "_durable_directory_barrier",
        fail_old_target_move_barrier,
    )
    with pytest.raises(StateError) as failed:
        restore_state(
            target_scheduler.paths,
            backup,
            confirm_no_processes=True,
            replace_empty=True,
        )

    assert failed.value.details["reason"] == "restore-recovery-required"
    assert failed.value.details["recovery_required"] is True
    assert Path(failed.value.details["quarantine"]).is_dir()
    assert Path(failed.value.details["staged"]).is_file()
    assert target_scheduler.paths.database.exists() is (failure_call == 2)


def test_restore_old_target_hardlink_failure_preserves_custody(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setattr(state_ops_module, "_verify_windows_maintenance_acl", lambda _path: None)
    source_scheduler, _ = _closed_task_state(tmp_path)
    backup = tmp_path / "old-target-hardlink-source.sqlite3"
    backup_state(source_scheduler.paths, backup, confirm_no_processes=True)
    target_workspace = tmp_path / "old-target-hardlink-workspace"
    target_scheduler = _registered_scheduler(
        tmp_path / "old-target-hardlink-target",
        target_workspace,
    )
    _mark_finalized_receipts_delivered(target_scheduler)
    target = target_scheduler.paths.database
    original_link = state_ops_module.os.link

    def fail_target_link(
        source: str | bytes | Path, destination: str | bytes | Path, *args, **kwargs
    ):
        if Path(source) == target:
            raise OSError("injected old target custody link failure")
        return original_link(source, destination, *args, **kwargs)

    monkeypatch.setattr(state_ops_module.os, "link", fail_target_link)
    with pytest.raises(StateError) as failed:
        restore_state(
            target_scheduler.paths,
            backup,
            confirm_no_processes=True,
            replace_empty=True,
        )

    assert failed.value.details["reason"] == "restore-recovery-required"
    assert failed.value.details["publication_uncertain"] is False
    assert target.is_file()
    assert Path(failed.value.details["quarantine"]).is_dir()
    assert Path(failed.value.details["staged"]).is_file()


def test_restore_old_target_unlink_failure_preserves_hardlink_custody(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setattr(state_ops_module, "_verify_windows_maintenance_acl", lambda _path: None)
    source_scheduler, _ = _closed_task_state(tmp_path)
    backup = tmp_path / "old-target-unlink-source.sqlite3"
    backup_state(source_scheduler.paths, backup, confirm_no_processes=True)
    target_workspace = tmp_path / "old-target-unlink-workspace"
    target_scheduler = _registered_scheduler(
        tmp_path / "old-target-unlink-target",
        target_workspace,
    )
    _mark_finalized_receipts_delivered(target_scheduler)
    target = target_scheduler.paths.database
    original_unlink = Path.unlink

    def fail_target_unlink(self: Path, *args, **kwargs):
        if self == target:
            raise OSError("injected old target custody unlink failure")
        return original_unlink(self, *args, **kwargs)

    monkeypatch.setattr(Path, "unlink", fail_target_unlink)
    with pytest.raises(StateError) as failed:
        restore_state(
            target_scheduler.paths,
            backup,
            confirm_no_processes=True,
            replace_empty=True,
        )

    assert failed.value.details["reason"] == "restore-recovery-required"
    assert failed.value.details["publication_uncertain"] is False
    quarantine = Path(failed.value.details["quarantine"])
    assert target.is_file()
    assert (quarantine / target.name).is_file()
    assert Path(failed.value.details["staged"]).is_file()


@pytest.mark.parametrize("failure_call", [7, 11])
def test_restore_cleanup_barrier_failure_preserves_custody(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    failure_call: int,
) -> None:
    monkeypatch.setattr(state_ops_module, "_verify_windows_maintenance_acl", lambda _path: None)
    source_scheduler, _ = _closed_task_state(tmp_path)
    backup = tmp_path / f"cleanup-barrier-{failure_call}-source.sqlite3"
    backup_state(source_scheduler.paths, backup, confirm_no_processes=True)
    target = StatePaths(tmp_path / f"cleanup-barrier-{failure_call}-target")
    calls = 0

    def fail_cleanup_barrier(_path: Path) -> None:
        nonlocal calls
        calls += 1
        if calls == failure_call:
            raise OSError("injected restore cleanup barrier failure")

    monkeypatch.setattr(state_ops_module, "_durable_directory_barrier", fail_cleanup_barrier)
    result = restore_state(target, backup, confirm_no_processes=True)

    assert not any(Path(path).is_dir() for path in result["cleanup_pending"])
    pending_parents = {Path(path) for path in result["durability_pending_parent"]}
    deletion_targets = {Path(path) for path in result["cleanup_pending"]}
    assert pending_parents
    assert target.database not in pending_parents
    assert pending_parents.isdisjoint(deletion_targets)
    quarantine = state_ops_module._restore_quarantine_path(target.database)
    assert pending_parents == {quarantine if failure_call == 7 else target.root}
    assert target.database.is_file()


@pytest.mark.parametrize("failure_call", [1, 2, 3])
def test_nested_maintenance_parent_barrier_failure_preserves_created_prefix(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    failure_call: int,
) -> None:
    monkeypatch.setattr(state_ops_module, "_verify_windows_maintenance_acl", lambda _path: None)
    calls = 0

    def fail_barrier(_path: Path) -> None:
        nonlocal calls
        calls += 1
        if calls == failure_call:
            raise OSError("injected maintenance parent barrier failure")

    monkeypatch.setattr(state_module, "_durable_directory_barrier", fail_barrier)
    root = tmp_path / "nested-maintenance" / "level-one" / "level-two"

    with pytest.raises(StateError) as failed:
        state_ops_module._prepare_maintenance_parent(root)

    assert failed.value.details["reason"] == "maintenance-directory-barrier-failed"
    assert failed.value.details["recovery_required"] is True
    assert Path(failed.value.details["entry"]).is_dir()
    assert failed.value.details["cleanup_pending"] == []
    assert failed.value.details["durability_pending_parent"]
    assert root.exists() is (failure_call == 3)
    assert (root.parent).exists() is (failure_call >= 2)
    assert (root.parent.parent).exists()


def test_restore_missing_target_rejects_dangling_sidecar_entry(tmp_path: Path) -> None:
    source_scheduler, _ = _closed_task_state(tmp_path)
    backup = tmp_path / "dangling-sidecar-backup.sqlite3"
    backup_state(source_scheduler.paths, backup, confirm_no_processes=True)
    target = StatePaths(tmp_path / "dangling-sidecar-target")
    target.root.mkdir()
    dangling = Path(f"{target.database}-wal")
    try:
        dangling.symlink_to(target.root / "missing-wal-target")
    except OSError as exc:
        pytest.skip(f"File symlinks are unavailable: {exc}")

    with pytest.raises(UsageError) as invalid:
        restore_state(target, backup, confirm_no_processes=True)
    assert invalid.value.details["reason"] == "restore-target-orphan-sidecars"
    assert invalid.value.details["sidecars"] == [str(dangling)]
    assert not target.database.exists()
    assert dangling.is_symlink()


@pytest.mark.skipif(os.name == "nt", reason="POSIX special files only")
@pytest.mark.parametrize("kind", ["directory", "fifo"])
def test_verify_rejects_nonregular_sidecar_before_sqlite_open(
    tmp_path: Path,
    kind: str,
) -> None:
    scheduler, _ = _closed_task_state(tmp_path)
    sidecar = Path(f"{scheduler.paths.database}-journal")
    if kind == "directory":
        sidecar.mkdir()
    else:
        os.mkfifo(sidecar, mode=0o600)

    with pytest.raises(UsageError) as invalid:
        verify_state(scheduler.paths.database)
    assert invalid.value.details["reason"] == "maintenance-file-not-regular"


def test_state_cli_backup_verify_restore_uses_protocol_two(tmp_path: Path, capsys) -> None:
    source_scheduler, _ = _closed_task_state(tmp_path)
    backup = tmp_path / "cli-backup.sqlite3"
    assert (
        run(
            [
                "--state-dir",
                str(source_scheduler.paths.root),
                "state",
                "backup",
                "--output",
                str(backup),
                "--confirm-no-processes",
            ]
        )
        == 0
    )
    backed_up = _read_output(capsys)
    assert backed_up["result"]["backup"]["integrity_check"] == "ok"

    assert run(["state", "verify", "--input", str(backup)]) == 0
    verified = _read_output(capsys)
    assert verified["result"]["schema_version"] == 3

    restored_root = tmp_path / "cli-restored"
    assert (
        run(
            [
                "--state-dir",
                str(restored_root),
                "state",
                "restore",
                "--input",
                str(backup),
                "--confirm-no-processes",
            ]
        )
        == 0
    )
    restored = _read_output(capsys)
    assert restored["result"]["restored"]["counts"]["tasks"] == 1


def test_state_cli_verify_does_not_touch_default_state_directory(
    tmp_path: Path,
    capsys,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    source_scheduler, _ = _closed_task_state(tmp_path)
    backup = tmp_path / "read-only-cli-verify.sqlite3"
    backup_state(source_scheduler.paths, backup, confirm_no_processes=True)
    untouched_default = tmp_path / "untouched-default-state"
    monkeypatch.setenv(state_module.STATE_ENVIRONMENT_VARIABLE, str(untouched_default))

    assert run(["state", "verify", "--input", str(backup)]) == 0
    verified = _read_output(capsys)
    assert verified["result"]["integrity_check"] == "ok"
    assert not untouched_default.exists()
