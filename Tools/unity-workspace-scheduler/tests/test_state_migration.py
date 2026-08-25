from __future__ import annotations

import os
import sqlite3
import sys
import threading
import time
import types
import uuid
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

import pytest

import unity_workspace_scheduler.coordinator as coordinator_module
import unity_workspace_scheduler.state as state_module
import unity_workspace_scheduler.state_ops as state_ops_module
from unity_workspace_scheduler.coordinator import (
    TERMINAL_TASK_RETENTION,
    WorkspaceCoordinator,
    _token_hash,
    _workspace_id,
)
from unity_workspace_scheduler.errors import StateError, UsageError
from unity_workspace_scheduler.state import (
    StatePaths,
    _enable_wal,
    open_database,
    resolve_state_paths,
)
from unity_workspace_scheduler.state_ops import inspect_state, verify_state

ROOT = Path(__file__).resolve().parents[1]
SCHEMA_ONE_SQL = (ROOT / "tests" / "fixtures" / "schema1.sql").read_text(encoding="utf-8")
AMBIGUOUS_RESTORATION_SQL_TEMPLATE = (
    ROOT / "tests" / "fixtures" / "schema1_ambiguous_restoration.sql"
).read_text(encoding="utf-8")


@pytest.fixture(autouse=True)
def _mock_inherited_host_temp_acl_for_state_migration_tests(
    monkeypatch: pytest.MonkeyPatch,
    request: pytest.FixtureRequest,
) -> None:
    """Keep migration fixtures independent from the host temp ACL inheritance."""

    if state_module.os.name == "nt" and "windows_maintenance_acl" not in request.node.name:
        monkeypatch.setattr(state_module, "_verify_windows_maintenance_acl", lambda _path: None)
        monkeypatch.setattr(state_ops_module, "_verify_windows_maintenance_acl", lambda _path: None)


def _ambiguous_restoration_sql(workspace: Path) -> str:
    root = str(workspace.resolve())
    return AMBIGUOUS_RESTORATION_SQL_TEMPLATE.replace(
        "__WORKSPACE_ID__", _workspace_id(root)
    ).replace("__WORKSPACE_ROOT__", root.replace("'", "''"))


def test_wal_lock_retry_has_a_wall_clock_deadline(monkeypatch: pytest.MonkeyPatch) -> None:
    class LockedConnection:
        calls = 0

        def execute(self, _statement: str) -> None:
            self.calls += 1
            raise sqlite3.OperationalError("database is locked")

    clock = 0.0

    def monotonic() -> float:
        return clock

    def sleep(seconds: float) -> None:
        nonlocal clock
        clock += seconds

    connection = LockedConnection()
    monkeypatch.setattr(state_module, "_WAL_RETRY_TIMEOUT_SECONDS", 0.11)
    monkeypatch.setattr(state_module.time, "monotonic", monotonic)
    monkeypatch.setattr(state_module.time, "sleep", sleep)
    with pytest.raises(sqlite3.OperationalError, match="locked"):
        _enable_wal(connection)  # type: ignore[arg-type]
    assert clock == pytest.approx(0.11)
    assert connection.calls <= 4


def test_platform_case_identity_is_casefolded_only_on_windows(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setattr(state_module.os, "name", "posix")
    assert state_module._platform_case_identity("C:/State/Owner.Token") == "C:/State/Owner.Token"

    monkeypatch.setattr(state_module.os, "name", "nt")
    assert state_module._platform_case_identity("C:/State/Owner.Token") == "c:/state/owner.token"


@pytest.mark.skipif(state_module.os.name == "nt", reason="POSIX directory fsync behavior only")
def test_durable_directory_barrier_flushes_an_existing_parent(tmp_path: Path) -> None:
    parent = tmp_path / "parent"
    parent.mkdir()
    state_module._durable_directory_barrier(parent)


@pytest.mark.skipif(state_module.os.name == "nt", reason="POSIX directory fsync behavior only")
def test_durable_directory_barrier_fails_closed_when_fsync_fails(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    parent = tmp_path / "parent"
    parent.mkdir()

    def fail_fsync(_descriptor: int) -> None:
        raise OSError("injected directory fsync failure")

    monkeypatch.setattr(state_module.os, "fsync", fail_fsync)
    with pytest.raises(OSError, match="metadata flush failed"):
        state_module._durable_directory_barrier(parent)


def test_token_creation_rejects_an_unproven_directory_barrier(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    token_file = tmp_path / "owner.token"

    def fail_barrier(_path: Path) -> None:
        raise OSError("injected directory durability failure")

    monkeypatch.setattr(state_module, "_verify_windows_token_acl", lambda _descriptor: None)
    monkeypatch.setattr(
        state_module,
        "_validate_windows_token_descriptor",
        lambda _descriptor: None,
    )
    monkeypatch.setattr(state_module, "_durable_directory_barrier", fail_barrier)
    with pytest.raises(UsageError) as failure:
        state_module.create_token_file(token_file, "secret")

    assert failure.value.details["reason"] == "token-create-durable-barrier-failed"
    assert failure.value.details["recovery_required"] is True
    assert not token_file.exists()


def test_token_removal_preserves_cleanup_proof_when_barrier_fails_then_retries(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setattr(state_module, "_verify_windows_token_acl", lambda _descriptor: None)
    monkeypatch.setattr(
        state_module,
        "_validate_windows_token_descriptor",
        lambda _descriptor: None,
    )
    monkeypatch.setattr(state_module, "_durable_directory_barrier", lambda _path: None)
    token_file = state_module.create_token_file(tmp_path / "owner.token", "secret")
    expected_hash = _token_hash("secret")

    def fail_barrier(_path: Path) -> None:
        raise OSError("injected directory durability failure")

    monkeypatch.setattr(state_module, "_durable_directory_barrier", fail_barrier)
    with pytest.raises(UsageError) as failure:
        state_module.remove_matching_token_hash_file(token_file, expected_hash)

    assert failure.value.details == {
        "reason": "token-cleanup-durable-barrier-failed",
        "recovery_required": True,
    }
    assert not token_file.exists()

    monkeypatch.setattr(state_module, "_durable_directory_barrier", lambda _path: None)
    assert state_module.remove_matching_token_hash_file(token_file, expected_hash) is True


def test_windows_token_removal_closes_marked_handle_before_directory_barrier(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    events: list[str] = []
    token_path = tmp_path / "owner.token"
    monkeypatch.setattr(state_module.os, "name", "nt")
    monkeypatch.setattr(
        state_module,
        "_open_validated_windows_token",
        lambda _path, *, delete_access: (123, token_path),
    )
    monkeypatch.setattr(state_module, "_read_token_descriptor", lambda _descriptor: "secret")
    monkeypatch.setattr(
        state_module,
        "_validate_windows_token_descriptor",
        lambda _descriptor: events.append("validate"),
    )
    monkeypatch.setattr(
        state_module,
        "_delete_windows_token_descriptor",
        lambda _descriptor: events.append("mark-delete"),
    )
    monkeypatch.setattr(
        state_module.os,
        "close",
        lambda _descriptor: events.append("close"),
    )

    def barrier(_path: Path) -> None:
        assert events == ["validate", "mark-delete", "close"]
        events.append("barrier")

    monkeypatch.setattr(state_module, "_durable_token_cleanup_barrier", barrier)
    assert state_module.remove_matching_token_file(token_path, "secret") is True
    assert events == ["validate", "mark-delete", "close", "barrier"]


def test_windows_token_open_uses_exclusive_share_for_delete_access(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Pin the CreateFileW share-mode contract even when Windows tests are skipped."""

    token_path = tmp_path / "owner.token"
    token_path.write_text("secret\n", encoding="utf-8")
    share_modes: list[int] = []

    class FakeFunction:
        def __init__(self, result: int) -> None:
            self.result = result

        def __call__(self, *arguments: object) -> int:
            if len(arguments) >= 3:
                share_modes.append(int(arguments[2]))
            return self.result

    class FakeKernel32:
        def __init__(self) -> None:
            self.CreateFileW = FakeFunction(100)
            self.CloseHandle = FakeFunction(1)

    kernel32 = FakeKernel32()
    fake_msvcrt = types.SimpleNamespace(open_osfhandle=lambda _handle, _flags: 123)
    monkeypatch.setitem(sys.modules, "msvcrt", fake_msvcrt)
    monkeypatch.setattr(state_module.os, "name", "nt")
    monkeypatch.setattr(state_module, "_validate_windows_token_location", lambda _path: None)
    monkeypatch.setattr(state_module, "_validate_windows_token_descriptor", lambda _fd: None)
    monkeypatch.setattr(state_module, "_windows_descriptor_final_path", lambda _fd: token_path)
    monkeypatch.setattr(state_module, "_verify_windows_token_acl", lambda _fd: None)
    monkeypatch.setattr(state_module, "_same_regular_file", lambda *_args: True)
    monkeypatch.setattr(state_module, "_is_windows_reparse_point", lambda _path: False)
    monkeypatch.setattr(state_module.ctypes, "WinDLL", lambda *_args, **_kwargs: kernel32)

    read_descriptor, _ = state_module._open_validated_windows_token(
        token_path,
        delete_access=False,
    )
    delete_descriptor, _ = state_module._open_validated_windows_token(
        token_path,
        delete_access=True,
    )

    assert read_descriptor == 123
    assert delete_descriptor == 123
    assert share_modes == [0x00000001 | 0x00000002 | 0x00000004, 0]


@pytest.mark.skipif(state_module.os.name != "nt", reason="Windows token sharing behavior only")
def test_windows_token_delete_conflict_preserves_token_until_reader_closes(
    tmp_path: Path,
) -> None:
    """A live normal reader must keep cleanup retryable until it closes."""

    token_path = state_module.create_token_file(tmp_path / "owner.token", "secret")
    reader_descriptor, _ = state_module._open_validated_windows_token(
        token_path,
        delete_access=False,
    )
    try:
        with pytest.raises(UsageError, match="Cannot remove task token file"):
            state_module.remove_matching_token_file(token_path, "secret")
        assert token_path.exists()
    finally:
        os.close(reader_descriptor)

    assert state_module.remove_matching_token_file(token_path, "secret") is True
    assert not token_path.exists()


def test_windows_token_delete_requires_absence_before_directory_barrier(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    token_path = tmp_path / "owner.token"
    token_path.write_text("secret\n", encoding="utf-8")
    events: list[str] = []
    monkeypatch.setattr(state_module.os, "name", "nt")
    monkeypatch.setattr(
        state_module,
        "_open_validated_windows_token",
        lambda _path, *, delete_access: (123, token_path),
    )
    monkeypatch.setattr(state_module, "_read_token_descriptor", lambda _descriptor: "secret")
    monkeypatch.setattr(state_module, "_validate_windows_token_descriptor", lambda _fd: None)
    monkeypatch.setattr(
        state_module,
        "_delete_windows_token_descriptor",
        lambda _descriptor: events.append("mark-delete"),
    )
    monkeypatch.setattr(state_module.os, "close", lambda _descriptor: events.append("close"))
    monkeypatch.setattr(
        state_module,
        "_durable_token_cleanup_barrier",
        lambda _path: events.append("barrier"),
    )

    with pytest.raises(UsageError, match="Cannot remove task token file"):
        state_module.remove_matching_token_file(token_path, "secret")

    assert events == ["mark-delete", "close"]


def test_windows_token_delete_permission_error_does_not_enter_barrier(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    token_path = tmp_path / "owner.token"
    events: list[str] = []
    monkeypatch.setattr(state_module.os, "name", "nt")
    monkeypatch.setattr(
        state_module,
        "_open_validated_windows_token",
        lambda _path, *, delete_access: (123, token_path),
    )
    monkeypatch.setattr(state_module, "_read_token_descriptor", lambda _descriptor: "secret")
    monkeypatch.setattr(state_module, "_validate_windows_token_descriptor", lambda _fd: None)
    monkeypatch.setattr(
        state_module,
        "_delete_windows_token_descriptor",
        lambda _descriptor: events.append("mark-delete"),
    )
    monkeypatch.setattr(state_module.os, "close", lambda _descriptor: events.append("close"))

    def deny_lstat(_path: Path) -> os.stat_result:
        raise PermissionError("injected access denied")

    monkeypatch.setattr(
        state_module.Path,
        "lstat",
        deny_lstat,
    )
    monkeypatch.setattr(
        state_module,
        "_durable_token_cleanup_barrier",
        lambda _path: events.append("barrier"),
    )

    with pytest.raises(UsageError, match="Cannot remove task token file"):
        state_module.remove_matching_token_file(token_path, "secret")

    assert events == ["mark-delete", "close"]


@pytest.mark.parametrize(
    ("function_name", "argument"),
    (
        ("remove_matching_token_file", "secret"),
        ("remove_matching_token_hash_file", _token_hash("secret")),
    ),
)
def test_windows_token_file_not_found_after_open_is_not_treated_as_missing(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    function_name: str,
    argument: str,
) -> None:
    token_path = tmp_path / "owner.token"
    events: list[str] = []
    monkeypatch.setattr(state_module.os, "name", "nt")
    monkeypatch.setattr(
        state_module,
        "_open_validated_windows_token",
        lambda _path, *, delete_access: (123, token_path),
    )

    def fail_read(_descriptor: int) -> str:
        raise FileNotFoundError("injected after open")

    monkeypatch.setattr(
        state_module,
        "_read_token_descriptor",
        fail_read,
    )
    monkeypatch.setattr(state_module.os, "close", lambda _descriptor: events.append("close"))
    monkeypatch.setattr(
        state_module,
        "_durable_token_cleanup_barrier",
        lambda _path: events.append("barrier"),
    )

    remove = getattr(state_module, function_name)
    with pytest.raises(UsageError, match="Cannot remove task token file"):
        remove(token_path, argument)

    assert events == ["close"]


@pytest.mark.parametrize(
    "owner_sid",
    (
        "S-1-5-21-111111111-222222222-333333333-1001",
        "S-1-5-18",
        "S-1-5-32-544",
    ),
)
def test_windows_maintenance_acl_accepts_the_complete_private_allowlist(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
    owner_sid: str,
) -> None:
    current_sid = "S-1-5-21-111111111-222222222-333333333-1001"
    monkeypatch.setattr(
        state_module,
        "_windows_maintenance_acl_snapshot",
        lambda _path: (
            owner_sid,
            current_sid,
            [
                (state_module._WINDOWS_ACCESS_ALLOWED_ACE_TYPE, 1, current_sid),
                (state_module._WINDOWS_ACCESS_ALLOWED_ACE_TYPE, 1, "S-1-5-18"),
                (state_module._WINDOWS_ACCESS_ALLOWED_ACE_TYPE, 1, "S-1-5-32-544"),
                (
                    state_module._WINDOWS_ACCESS_ALLOWED_ACE_TYPE,
                    1,
                    state_module._WINDOWS_OWNER_RIGHTS_SID,
                ),
            ],
        ),
    )

    state_module._verify_windows_maintenance_acl(tmp_path)


@pytest.mark.parametrize(
    "entry",
    (
        (state_module._WINDOWS_ACCESS_ALLOWED_ACE_TYPE, 1, "S-1-5-4"),
        (
            state_module._WINDOWS_ACCESS_ALLOWED_ACE_TYPE,
            1,
            "S-1-5-21-111111111-222222222-333333333-513",
        ),
        (1, 1, ""),
    ),
)
def test_windows_maintenance_acl_rejects_interactive_custom_and_deny_entries(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
    entry: tuple[int, int, str],
) -> None:
    current_sid = "S-1-5-21-111111111-222222222-333333333-1001"
    monkeypatch.setattr(
        state_module,
        "_windows_maintenance_acl_snapshot",
        lambda _path: (current_sid, current_sid, [(0, 1, current_sid), entry]),
    )

    with pytest.raises(OSError, match="(unapproved principal|deny or unsupported)"):
        state_module._verify_windows_maintenance_acl(tmp_path)


def test_windows_maintenance_acl_rejects_owner_mismatch(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
) -> None:
    current_sid = "S-1-5-21-111111111-222222222-333333333-1001"
    monkeypatch.setattr(
        state_module,
        "_windows_maintenance_acl_snapshot",
        lambda _path: (
            "S-1-5-21-111111111-222222222-333333333-1002",
            current_sid,
            [(0, 1, current_sid)],
        ),
    )

    with pytest.raises(OSError, match="not owned"):
        state_module._verify_windows_maintenance_acl(tmp_path)


def test_private_directory_creation_flushes_each_new_parent(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    barriers: list[Path] = []
    monkeypatch.setattr(
        state_module,
        "_durable_directory_barrier",
        lambda path: barriers.append(Path(path)),
    )

    directory = tmp_path / "state" / "token-parent"
    state_module._ensure_private_directory(directory)

    assert directory.is_dir()
    assert barriers == [tmp_path, tmp_path / "state"]

    barriers.clear()
    state_module._ensure_private_directory(directory, preserve_existing=True)
    assert barriers == []

    barriers.clear()
    required = tmp_path / "required"
    state_module._ensure_private_directory(required, require_new=True)
    assert barriers == [tmp_path]


@pytest.mark.skipif(state_module.os.name == "nt", reason="POSIX modes only")
def test_posix_token_parent_preserves_existing_mode_and_secures_token(
    tmp_path: Path,
) -> None:
    existing_parent = tmp_path / "shared-token-parent"
    existing_parent.mkdir()
    existing_parent.chmod(0o755)
    existing_token = state_module.create_token_file(existing_parent / "owner.token", "secret")

    assert existing_parent.stat().st_mode & 0o777 == 0o755
    assert existing_token.stat().st_mode & 0o777 == 0o600

    new_parent = tmp_path / "new-token-parent"
    new_token = state_module.create_token_file(new_parent / "owner.token", "secret")
    assert new_parent.stat().st_mode & 0o777 == 0o700
    assert new_token.stat().st_mode & 0o777 == 0o600

    unsafe_parent = tmp_path / "unsafe-token-parent"
    unsafe_parent.mkdir()
    unsafe_parent.chmod(0o775)
    with pytest.raises(UsageError, match="writable by another user or group"):
        state_module.create_token_file(unsafe_parent / "owner.token", "secret")
    assert not (unsafe_parent / "owner.token").exists()


@pytest.mark.skipif(state_module.os.name == "nt", reason="POSIX symlink behavior only")
def test_posix_token_rejects_a_preexisting_final_symlink(tmp_path: Path) -> None:
    target = tmp_path / "target.token"
    target.write_text("preserve\n", encoding="utf-8")
    token_link = tmp_path / "owner.token"
    token_link.symlink_to(target)

    with pytest.raises(UsageError, match="symbolic links are not allowed"):
        state_module.create_token_file(token_link, "secret")
    assert target.read_text(encoding="utf-8") == "preserve\n"


@pytest.mark.skipif(state_module.os.name != "nt", reason="Windows ACL behavior only")
def test_windows_token_acl_probe_failure_removes_the_unsecured_file(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    token_file = tmp_path / "owner.token"
    monkeypatch.setattr(state_module.tempfile, "gettempdir", lambda: str(tmp_path))

    def fail_probe(_descriptor: int):
        raise OSError("descriptor security probe failed")

    monkeypatch.setattr(state_module, "_windows_token_acl_snapshot", fail_probe)
    with pytest.raises(UsageError, match="Cannot create task token file"):
        state_module.create_token_file(token_file, "secret")
    assert not token_file.exists()


def test_windows_token_location_rejects_an_arbitrary_parent(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    trusted_temp = tmp_path / "trusted-temp"
    trusted_temp.mkdir()
    monkeypatch.setattr(state_module.tempfile, "gettempdir", lambda: str(trusted_temp))

    state_module._validate_windows_token_location(trusted_temp / "router" / "owner.token")
    with pytest.raises(OSError, match="current-user temporary directory"):
        state_module._validate_windows_token_location(tmp_path / "untrusted" / "owner.token")


@pytest.mark.skipif(state_module.os.name != "nt", reason="Windows short-path behavior only")
def test_windows_token_location_canonicalizes_a_short_temp_alias(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    canonical_temp = tmp_path / "runneradmin" / "AppData" / "Local" / "Temp"
    canonical_temp.mkdir(parents=True)
    alias_temp = tmp_path / "RUNNER~1" / "AppData" / "Local" / "Temp"
    alias_path = alias_temp / "router" / "owner.token"
    canonical_path = canonical_temp / "router" / "owner.token"
    original_resolve = Path.resolve

    def resolve_alias(path: Path, strict: bool = False) -> Path:
        if path == alias_temp:
            return canonical_temp
        if path == alias_path:
            return canonical_path
        return original_resolve(path, strict=strict)

    monkeypatch.setattr(state_module.tempfile, "gettempdir", lambda: str(alias_temp))
    monkeypatch.setattr(Path, "resolve", resolve_alias)

    assert state_module.canonical_token_file_path(alias_path) == canonical_path


def test_windows_token_location_rejects_a_reparse_escape(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    trusted_temp = tmp_path / "trusted-temp"
    outside = tmp_path / "outside"
    trusted_temp.mkdir()
    outside.mkdir()
    escape = trusted_temp / "escape"
    try:
        escape.symlink_to(outside, target_is_directory=True)
    except OSError as exc:
        pytest.skip(f"Directory symbolic links unavailable: {exc}")
    monkeypatch.setattr(state_module.tempfile, "gettempdir", lambda: str(trusted_temp))

    with pytest.raises(OSError, match="current-user temporary directory"):
        state_module._validate_windows_token_location(escape / "owner.token")


@pytest.mark.skipif(state_module.os.name != "nt", reason="Windows reparse behavior only")
def test_windows_token_file_rejects_a_symbolic_link(
    tmp_path: Path,
) -> None:
    target = tmp_path / "target.token"
    target.write_text("preserve\n", encoding="utf-8")
    token_link = tmp_path / "owner.token"
    try:
        token_link.symlink_to(target)
    except OSError as exc:
        pytest.skip(f"Windows symbolic links unavailable: {exc}")

    with pytest.raises(UsageError, match="symbolic links are not allowed"):
        state_module.create_token_file(token_link, "secret")
    assert target.read_text(encoding="utf-8") == "preserve\n"


@pytest.mark.parametrize(
    "unapproved_sid",
    ("S-1-1-0", "S-1-5-21-111111111-222222222-333333333-513"),
)
@pytest.mark.skipif(state_module.os.name != "nt", reason="Windows ACL behavior only")
def test_windows_token_acl_rejects_every_unapproved_principal_and_removes_file(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch, unapproved_sid: str
) -> None:
    token_file = tmp_path / f"{unapproved_sid.rsplit('-', 1)[-1]}.token"
    current_sid = "S-1-5-21-111111111-222222222-333333333-1001"
    monkeypatch.setattr(state_module.tempfile, "gettempdir", lambda: str(tmp_path))
    monkeypatch.setattr(
        state_module,
        "_windows_token_acl_snapshot",
        lambda _descriptor: (
            current_sid,
            current_sid,
            [
                (state_module._WINDOWS_ACCESS_ALLOWED_ACE_TYPE, 1, current_sid),
                (state_module._WINDOWS_ACCESS_ALLOWED_ACE_TYPE, 1, unapproved_sid),
            ],
        ),
    )
    with pytest.raises(UsageError, match="unapproved principal"):
        state_module.create_token_file(token_file, "secret")
    assert not token_file.exists()


@pytest.mark.parametrize(
    "owner_sid",
    (
        "S-1-5-21-111111111-222222222-333333333-1001",
        "S-1-5-18",
        "S-1-5-32-544",
    ),
)
@pytest.mark.skipif(state_module.os.name != "nt", reason="Windows ACL behavior only")
def test_windows_token_acl_accepts_trusted_owner(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch, owner_sid: str
) -> None:
    current_sid = "S-1-5-21-111111111-222222222-333333333-1001"
    monkeypatch.setattr(
        state_module,
        "_windows_token_acl_snapshot",
        lambda _descriptor: (
            owner_sid,
            current_sid,
            [
                (state_module._WINDOWS_ACCESS_ALLOWED_ACE_TYPE, 1, current_sid),
            ],
        ),
    )

    state_module._verify_windows_token_acl(123)


@pytest.mark.skipif(state_module.os.name != "nt", reason="Windows ACL behavior only")
def test_windows_token_acl_owner_rights_requires_trusted_owner(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    current_sid = "S-1-5-21-111111111-222222222-333333333-1001"
    monkeypatch.setattr(
        state_module,
        "_windows_token_acl_snapshot",
        lambda _descriptor: (
            "S-1-5-21-111111111-222222222-333333333-1002",
            current_sid,
            [
                (
                    state_module._WINDOWS_ACCESS_ALLOWED_ACE_TYPE,
                    1,
                    state_module._WINDOWS_OWNER_RIGHTS_SID,
                )
            ],
        ),
    )
    with pytest.raises(OSError, match="not owned by the current identity"):
        state_module._verify_windows_token_acl(123)


@pytest.mark.skipif(state_module.os.name != "nt", reason="Windows ACL behavior only")
def test_windows_token_read_and_remove_recheck_acl_and_use_the_opened_file(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    token_file = tmp_path / "owner.token"
    current_sid = "S-1-5-21-111111111-222222222-333333333-1001"
    allowed = (
        current_sid,
        current_sid,
        [(state_module._WINDOWS_ACCESS_ALLOWED_ACE_TYPE, 1, current_sid)],
    )
    acl_snapshot = allowed
    monkeypatch.setattr(state_module.tempfile, "gettempdir", lambda: str(tmp_path))
    monkeypatch.setattr(
        state_module, "_windows_token_acl_snapshot", lambda _descriptor: acl_snapshot
    )

    state_module.create_token_file(token_file, "secret")
    assert state_module.read_token_file(token_file) == "secret"

    acl_snapshot = (
        current_sid,
        current_sid,
        [
            (state_module._WINDOWS_ACCESS_ALLOWED_ACE_TYPE, 1, current_sid),
            (
                state_module._WINDOWS_ACCESS_ALLOWED_ACE_TYPE,
                1,
                "S-1-5-21-111111111-222222222-333333333-513",
            ),
        ],
    )
    with pytest.raises(UsageError, match="unapproved principal"):
        state_module.read_token_file(token_file)
    with pytest.raises(UsageError, match="unapproved principal"):
        state_module.remove_matching_token_file(token_file, "secret")
    with pytest.raises(UsageError, match="unapproved principal"):
        state_module.remove_matching_token_hash_file(token_file, _token_hash("secret"))
    assert token_file.exists()

    acl_snapshot = allowed
    assert state_module.remove_matching_token_hash_file(token_file, _token_hash("secret")) is True
    assert not token_file.exists()


@pytest.mark.skipif(state_module.os.name != "nt", reason="Windows ACL behavior only")
def test_windows_token_read_and_remove_reject_a_path_identity_swap(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    token_file = tmp_path / "owner.token"
    current_sid = "S-1-5-21-111111111-222222222-333333333-1001"
    monkeypatch.setattr(state_module.tempfile, "gettempdir", lambda: str(tmp_path))
    monkeypatch.setattr(
        state_module,
        "_windows_token_acl_snapshot",
        lambda _descriptor: (
            current_sid,
            current_sid,
            [(state_module._WINDOWS_ACCESS_ALLOWED_ACE_TYPE, 1, current_sid)],
        ),
    )
    state_module.create_token_file(token_file, "secret")
    monkeypatch.setattr(state_module, "_same_regular_file", lambda *_args: False)

    with pytest.raises(UsageError, match="opened regular file"):
        state_module.read_token_file(token_file)
    with pytest.raises(UsageError, match="opened regular file"):
        state_module.remove_matching_token_file(token_file, "secret")
    with pytest.raises(UsageError, match="opened regular file"):
        state_module.remove_matching_token_hash_file(token_file, _token_hash("secret"))
    assert token_file.read_text(encoding="utf-8").strip() == "secret"


@pytest.mark.skipif(state_module.os.name != "nt", reason="Windows hard-link behavior only")
def test_windows_token_read_remove_and_canonicalization_reject_hard_links(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    token_file = tmp_path / "owner.token"
    alias = tmp_path / "owner-alias.token"
    current_sid = "S-1-5-21-111111111-222222222-333333333-1001"
    monkeypatch.setattr(state_module.tempfile, "gettempdir", lambda: str(tmp_path))
    monkeypatch.setattr(
        state_module,
        "_windows_token_acl_snapshot",
        lambda _descriptor: (
            current_sid,
            current_sid,
            [(state_module._WINDOWS_ACCESS_ALLOWED_ACE_TYPE, 1, current_sid)],
        ),
    )
    state_module.create_token_file(token_file, "secret")
    os.link(token_file, alias)
    assert token_file.stat().st_nlink == 2

    for candidate in (token_file, alias):
        with pytest.raises(UsageError, match="hard-link aliases"):
            state_module.canonical_token_file_path(candidate)
        with pytest.raises(UsageError, match="hard-link aliases"):
            state_module.read_token_file(candidate)
        with pytest.raises(UsageError, match="hard-link aliases"):
            state_module.remove_matching_token_hash_file(candidate, _token_hash("secret"))
    assert token_file.read_text(encoding="utf-8").strip() == "secret"
    assert alias.read_text(encoding="utf-8").strip() == "secret"


@pytest.mark.skipif(state_module.os.name != "nt", reason="Windows handle containment only")
def test_windows_token_read_and_remove_wrap_final_containment_errors(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    token_file = tmp_path / "owner.token"
    token_file.write_text("preserve\n", encoding="utf-8")

    def fail_final_containment(_path: Path, *, delete_access: bool):
        del delete_access
        raise ValueError("opened token handle escaped the trusted temporary directory")

    monkeypatch.setattr(
        state_module,
        "_open_validated_windows_token",
        fail_final_containment,
    )

    with pytest.raises(UsageError, match="Cannot read task token file"):
        state_module.read_token_file(token_file)
    with pytest.raises(UsageError, match="Cannot remove task token file"):
        state_module.remove_matching_token_file(token_file, "preserve")
    with pytest.raises(UsageError, match="Cannot remove task token file"):
        state_module.remove_matching_token_hash_file(token_file, _token_hash("preserve"))
    assert token_file.read_text(encoding="utf-8") == "preserve\n"


@pytest.mark.skipif(state_module.os.name != "nt", reason="Windows reparse behavior only")
def test_windows_token_read_and_remove_reject_a_final_symlink(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    target = tmp_path / "target.token"
    target.write_text("preserve\n", encoding="utf-8")
    token_link = tmp_path / "owner.token"
    try:
        token_link.symlink_to(target)
    except OSError as exc:
        pytest.skip(f"Windows symbolic links unavailable: {exc}")
    monkeypatch.setattr(state_module.tempfile, "gettempdir", lambda: str(tmp_path))

    with pytest.raises(UsageError, match="reparse points are not allowed"):
        state_module.read_token_file(token_link)
    with pytest.raises(UsageError, match="reparse points are not allowed"):
        state_module.remove_matching_token_file(token_link, "preserve")
    with pytest.raises(UsageError, match="reparse points are not allowed"):
        state_module.remove_matching_token_hash_file(token_link, _token_hash("preserve"))
    assert target.read_text(encoding="utf-8") == "preserve\n"


def create_schema_one(root: Path) -> StatePaths:
    root.mkdir(parents=True)
    paths = StatePaths(root)
    with sqlite3.connect(paths.database) as connection:
        connection.executescript(SCHEMA_ONE_SQL)
    return paths


def create_schema_two(root: Path) -> StatePaths:
    paths = create_schema_one(root)
    with sqlite3.connect(paths.database) as connection:
        connection.execute(
            "ALTER TABLE workspaces ADD COLUMN next_queue_order INTEGER NOT NULL DEFAULT 1"
        )
        for statement in state_module._SCHEMA_TWO_INDEX_STATEMENTS:
            connection.execute(statement)
        connection.execute("UPDATE scheduler_meta SET value = '2' WHERE key = 'schema_version'")
    return paths


def create_legacy_schema(paths_root: Path, schema_version: int) -> StatePaths:
    if schema_version == 1:
        return create_schema_one(paths_root)
    return create_schema_two(paths_root)


def insert_task(
    connection: sqlite3.Connection,
    *,
    task_id: str,
    workspace_id: str,
    state: str,
    created_at: float,
    finished_at: float | None = None,
) -> None:
    if state == "outcome_unknown" and finished_at is None:
        finished_at = created_at
        result = "outcome-unknown"
    else:
        result = state if finished_at is not None else None
    connection.execute(
        "INSERT INTO tasks(id, workspace_id, owner, summary, token_hash, state, created_at, "
        "heartbeat_at, expires_at, finished_at, result) "
        "VALUES(?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
        (
            task_id,
            workspace_id,
            task_id,
            task_id,
            _token_hash(f"token-{task_id}"),
            state,
            created_at,
            created_at,
            created_at + 86400,
            finished_at,
            result,
        ),
    )


def create_schema_one_active_state(tmp_path: Path) -> tuple[StatePaths, str]:
    workspace = tmp_path / "workspace"
    workspace.mkdir()
    root = str(workspace.resolve())
    workspace_id = _workspace_id(root)
    paths = create_schema_one(tmp_path / "state")
    with sqlite3.connect(paths.database) as connection:
        connection.execute(
            "INSERT INTO workspaces(id, root, registered_at, epoch) VALUES(?, ?, 1000, 1)",
            (workspace_id, root),
        )
        for task_id in ("first-task", "second-task"):
            insert_task(
                connection,
                task_id=task_id,
                workspace_id=workspace_id,
                state="active",
                created_at=1000,
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
    return paths, workspace_id


@pytest.mark.parametrize("schema_version", (1, 2))
def test_legacy_casefolded_workspace_ids_remap_with_all_foreign_keys(
    tmp_path: Path,
    schema_version: int,
) -> None:
    workspace = tmp_path / "LegacyUpperCaseWorkspace"
    workspace.mkdir()
    root = str(workspace.resolve())
    legacy_id = state_module._legacy_workspace_id(root)
    current_id = _workspace_id(root)
    paths = create_legacy_schema(tmp_path / "state", schema_version)
    with sqlite3.connect(paths.database) as connection:
        connection.execute(
            "INSERT INTO workspaces(id, root, registered_at, epoch) VALUES(?, ?, 1000, 1)",
            (legacy_id, root),
        )
        connection.execute(
            "INSERT INTO tasks(id, workspace_id, owner, summary, token_hash, state, "
            "created_at, heartbeat_at, expires_at, finished_at, result, note) "
            "VALUES('recovered-task', ?, 'owner', 'summary', ?, 'completed', "
            "1000, 1000, 1000, 1001, 'recovered-completed', 'evidence')",
            (legacy_id, _token_hash("legacy-token")),
        )
        connection.execute(
            "INSERT INTO claims(id, workspace_id, task_id, kind, state, queue_order, "
            "created_at, released_at) VALUES('released-claim', ?, 'recovered-task', "
            "'normal', 'released', 1, 1000, 1001)",
            (legacy_id,),
        )
        connection.execute(
            "INSERT INTO claim_scopes(claim_id, scope_type, value) "
            "VALUES('released-claim', 'write', 'assets/legacy.prefab')"
        )
        connection.execute(
            "INSERT INTO recovery_events(id, workspace_id, task_id, resolution, evidence, "
            "created_at) VALUES('recovery-event', ?, 'recovered-task', 'completed', "
            "'evidence', 1001)",
            (legacy_id,),
        )

    with open_database(paths) as connection:
        assert (
            connection.execute(
                "SELECT value FROM scheduler_meta WHERE key = 'schema_version'"
            ).fetchone()["value"]
            == "3"
        )
        assert connection.execute("SELECT id FROM workspaces").fetchone()["id"] == current_id
        for table in ("tasks", "claims", "recovery_events"):
            assert (
                connection.execute(f"SELECT workspace_id FROM {table}").fetchone()[0] == current_id
            )
        assert connection.execute("PRAGMA foreign_key_check").fetchall() == []


@pytest.mark.parametrize("schema_version", (1, 2))
def test_legacy_workspace_id_collision_rolls_back_before_schema_change(
    tmp_path: Path,
    schema_version: int,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    first = tmp_path / "FirstWorkspace"
    second = tmp_path / "SecondWorkspace"
    first.mkdir()
    second.mkdir()
    first_root = str(first.resolve())
    second_root = str(second.resolve())
    paths = create_legacy_schema(tmp_path / "state", schema_version)
    with sqlite3.connect(paths.database) as connection:
        connection.executemany(
            "INSERT INTO workspaces(id, root, registered_at, epoch) VALUES(?, ?, 1000, 1)",
            (
                (state_module._legacy_workspace_id(first_root), first_root),
                (state_module._legacy_workspace_id(second_root), second_root),
            ),
        )
    database_before = paths.database.read_bytes()
    monkeypatch.setattr(state_module, "_current_workspace_id", lambda _root: "collision")

    with pytest.raises(StateError) as failure:
        open_database(paths)

    assert failure.value.details["reason"] == "legacy-workspace-id-collision"
    assert paths.database.read_bytes() == database_before


def assert_schema_one_unchanged(paths: StatePaths, database_before: bytes) -> None:
    assert paths.database.read_bytes() == database_before
    with sqlite3.connect(paths.database) as connection:
        assert connection.execute("PRAGMA journal_mode").fetchone()[0] == "delete"
        assert (
            connection.execute(
                "SELECT value FROM scheduler_meta WHERE key = 'schema_version'"
            ).fetchone()[0]
            == "1"
        )
        columns = {row[1] for row in connection.execute("PRAGMA table_info(workspaces)")}
    assert "next_queue_order" not in columns
    for suffix in ("-journal", "-wal", "-shm"):
        assert not Path(f"{paths.database}{suffix}").exists()


def test_schema_one_fixture_migrates_atomically_and_preserves_open_state(
    tmp_path: Path,
) -> None:
    workspace = tmp_path / "workspace"
    workspace.mkdir()
    root = str(workspace.resolve())
    workspace_id = _workspace_id(root)
    paths = create_schema_one(tmp_path / "state")
    now = time.time()
    with sqlite3.connect(paths.database) as connection:
        connection.execute(
            "INSERT INTO workspaces(id, root, registered_at, epoch) VALUES(?, ?, ?, 7)",
            (workspace_id, root, now),
        )
        for task_id, state in (
            ("active-owner", "active"),
            ("unknown-owner", "outcome_unknown"),
            ("urgent-waiter", "active"),
        ):
            insert_task(
                connection,
                task_id=task_id,
                workspace_id=workspace_id,
                state=state,
                created_at=now,
            )
        claims = (
            ("active-claim", "active-owner", "normal", "active", 5),
            ("unknown-claim", "active-owner", "normal", "active", 7),
            ("urgent-freeze", "active-owner", "freeze", "active", 12),
        )
        connection.executemany(
            "INSERT INTO claims(id, workspace_id, task_id, kind, state, queue_order, created_at) "
            "VALUES(?, ?, ?, ?, ?, ?, ?)",
            (
                (claim_id, workspace_id, task_id, kind, state, order, now)
                for claim_id, task_id, kind, state, order in claims
            ),
        )
        connection.execute(
            "INSERT INTO claim_scopes(claim_id, scope_type, value) "
            "VALUES('active-claim', 'write', 'assets/hero.prefab')"
        )
        connection.execute(
            "INSERT INTO claim_scopes(claim_id, scope_type, value) "
            "VALUES('unknown-claim', 'resource', 'unity-live')"
        )
        connection.execute(
            "INSERT INTO claim_scopes(claim_id, scope_type, value) "
            "VALUES('urgent-freeze', 'priority', 'urgent')"
        )

    if state_module.os.name != "nt":
        database_before = paths.database.read_bytes()
        with pytest.raises(StateError) as blocked:
            open_database(paths)
        assert blocked.value.details["reason"] == "legacy-open-write-scope-migration-blocked"
        assert blocked.value.details["open_write_scope_count"] == 1
        assert_schema_one_unchanged(paths, database_before)
        return

    with open_database(paths) as connection:
        schema = connection.execute(
            "SELECT value FROM scheduler_meta WHERE key = 'schema_version'"
        ).fetchone()["value"]
        workspace_row = connection.execute(
            "SELECT epoch, next_queue_order FROM workspaces WHERE id = ?", (workspace_id,)
        ).fetchone()
        tasks = connection.execute("SELECT id, state FROM tasks ORDER BY id").fetchall()
        claims = connection.execute(
            "SELECT id, state, queue_order FROM claims ORDER BY queue_order"
        ).fetchall()
        indexes = {
            row["name"]
            for row in connection.execute(
                "SELECT name FROM sqlite_master WHERE type = 'index'"
            ).fetchall()
        }

    assert schema == "3"
    assert dict(workspace_row) == {"epoch": 7, "next_queue_order": 13}
    assert [(row["id"], row["state"]) for row in tasks] == [
        ("active-owner", "active"),
        ("unknown-owner", "outcome_unknown"),
        ("urgent-waiter", "active"),
    ]
    assert [(row["id"], row["state"], row["queue_order"]) for row in claims] == [
        ("active-claim", "active", 5),
        ("unknown-claim", "active", 7),
        ("urgent-freeze", "active", 12),
    ]
    assert {
        "tasks_state_expires",
        "tasks_workspace_token_created",
        "tasks_workspace_terminal_recency",
        "claims_workspace_order",
    } <= indexes

    coordinator = WorkspaceCoordinator(paths)
    status = coordinator.status(workspace)
    by_id = {claim["id"]: claim for claim in status["claims"]}
    assert by_id["urgent-freeze"]["priority"] == "urgent"

    coordinator.resolve_unknown(
        workspace,
        "unknown-owner",
        resolution="failed",
        evidence="isolated migration fixture",
    )
    coordinator.release_claim(workspace, "token-active-owner", "active-claim")
    scheduled = {claim["id"]: claim for claim in coordinator.status(workspace)["claims"]}
    assert scheduled["urgent-freeze"]["state"] == "active"


def test_schema_one_migration_allows_no_open_write_scope_on_posix(tmp_path: Path) -> None:
    workspace = tmp_path / "workspace"
    workspace.mkdir()
    paths = create_schema_one(tmp_path / "state")
    root = str(workspace.resolve())
    workspace_id = _workspace_id(root)
    with sqlite3.connect(paths.database) as connection:
        connection.execute(
            "INSERT INTO workspaces(id, root, registered_at, epoch) VALUES(?, ?, 1000, 1)",
            (workspace_id, root),
        )
        insert_task(
            connection,
            task_id="active-owner",
            workspace_id=workspace_id,
            state="active",
            created_at=1000,
        )

    with open_database(paths) as connection:
        assert (
            connection.execute(
                "SELECT value FROM scheduler_meta WHERE key = 'schema_version'"
            ).fetchone()["value"]
            == "3"
        )


def test_legacy_task_can_release_verify_and_ack_without_a_start_receipt(
    tmp_path: Path,
) -> None:
    workspace = tmp_path / "legacy-release-workspace"
    workspace.mkdir()
    root = str(workspace.resolve())
    workspace_id = _workspace_id(root)
    paths = create_schema_one(tmp_path / "legacy-release-state")
    now = time.time()
    with sqlite3.connect(paths.database) as connection:
        connection.execute(
            "INSERT INTO workspaces(id, root, registered_at, epoch) VALUES(?, ?, ?, 1)",
            (workspace_id, root, now),
        )
        insert_task(
            connection,
            task_id="legacy-release-task",
            workspace_id=workspace_id,
            state="active",
            created_at=now,
        )

    coordinator = WorkspaceCoordinator(paths)
    cleanup_path = os.path.normpath(str((workspace / "legacy-owner.token").resolve()))
    released = coordinator.release_task(
        workspace,
        "token-legacy-release-task",
        operation_id=str(uuid.uuid4()),
        result="completed",
        token_cleanup_path=cleanup_path,
    )
    assert inspect_state(paths.database)["schema_version"] == 3
    with open_database(paths) as connection:
        before_ack = connection.execute(
            "SELECT start_operation_id, token_file_path, token_file_identity FROM tasks "
            "WHERE id = 'legacy-release-task'"
        ).fetchone()
    assert before_ack["start_operation_id"] is None
    assert before_ack["token_file_path"] == cleanup_path
    assert before_ack["token_file_identity"] is not None

    coordinator.acknowledge_receipt(
        str(released["operation"]["operation_id"]),
        str(released["operation"]["fingerprint"]),
        str(released["operation"]["delivery_digest"]),
    )
    assert inspect_state(paths.database)["schema_version"] == 3
    with open_database(paths) as connection:
        after_ack = connection.execute(
            "SELECT start_operation_id, token_file_path, token_file_identity FROM tasks "
            "WHERE id = 'legacy-release-task'"
        ).fetchone()
    assert dict(after_ack) == {
        "start_operation_id": None,
        "token_file_path": None,
        "token_file_identity": None,
    }


@pytest.mark.parametrize("open_state", ["queued", "parked"])
def test_schema_one_ambiguous_restoration_fixture_blocks_migration_without_guessing(
    tmp_path: Path, open_state: str
) -> None:
    paths = create_schema_one(tmp_path / open_state)
    workspace = tmp_path / f"{open_state}-workspace"
    workspace.mkdir()
    with sqlite3.connect(paths.database) as connection:
        connection.executescript(_ambiguous_restoration_sql(workspace))
        if open_state == "parked":
            connection.execute(
                "UPDATE claims SET state = 'parked' WHERE id = 'ambiguous-owner-claim'"
            )
            connection.execute(
                "UPDATE claims SET state = 'released' WHERE id = 'queued-normal-freeze'"
            )
            connection.execute(
                "INSERT INTO claim_scopes(claim_id, scope_type, value) "
                "VALUES('ambiguous-owner-claim', 'parked_for', 'active-urgent-freeze')"
            )

    with sqlite3.connect(paths.database) as connection:
        journal_mode_before = connection.execute("PRAGMA journal_mode").fetchone()[0]
    bytes_before = paths.database.read_bytes()

    with pytest.raises(UsageError) as blocked:
        open_database(paths)

    bytes_after = paths.database.read_bytes()
    with sqlite3.connect(paths.database) as connection:
        journal_mode_after = connection.execute("PRAGMA journal_mode").fetchone()[0]

    expected_states = {"queued": 2} if open_state == "queued" else {"parked": 1}
    assert blocked.value.details == {
        "reason": "schema-one-open-claim-migration-blocked",
        "claim_states": expected_states,
    }
    assert bytes_after == bytes_before
    assert journal_mode_before == journal_mode_after == "delete"
    for suffix in ("-journal", "-wal", "-shm"):
        assert not Path(f"{paths.database}{suffix}").exists()
    with sqlite3.connect(paths.database) as connection:
        schema = connection.execute(
            "SELECT value FROM scheduler_meta WHERE key = 'schema_version'"
        ).fetchone()[0]
        columns = {row[1] for row in connection.execute("PRAGMA table_info(workspaces)").fetchall()}
        claims = connection.execute("SELECT id, state FROM claims ORDER BY queue_order").fetchall()
    assert schema == "1"
    assert "next_queue_order" not in columns
    assert claims[0] == ("ambiguous-owner-claim", open_state)


def test_schema_one_migration_can_retry_after_wal_switch_failure(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    paths = create_schema_one(tmp_path / "state")
    real_enable_wal = state_module._enable_wal
    calls = 0

    def fail_first_wal_switch(connection: sqlite3.Connection) -> None:
        nonlocal calls
        calls += 1
        if calls == 1:
            raise sqlite3.OperationalError("simulated WAL switch failure")
        real_enable_wal(connection)

    monkeypatch.setattr(state_module, "_enable_wal", fail_first_wal_switch)
    with pytest.raises(sqlite3.OperationalError, match="simulated WAL switch failure"):
        open_database(paths)

    with sqlite3.connect(paths.database) as connection:
        schema_after_failure = connection.execute(
            "SELECT value FROM scheduler_meta WHERE key = 'schema_version'"
        ).fetchone()[0]
        journal_after_failure = connection.execute("PRAGMA journal_mode").fetchone()[0]
    assert schema_after_failure == "3"
    assert journal_after_failure == "delete"

    with open_database(paths) as connection:
        assert connection.execute("PRAGMA journal_mode").fetchone()[0] == "wal"
    assert calls == 2


def test_schema_one_migration_bounds_legacy_open_task_timing(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    workspace = tmp_path / "workspace"
    workspace.mkdir()
    root = str(workspace.resolve())
    workspace_id = _workspace_id(root)
    paths = create_schema_one(tmp_path / "state")
    migration_now = 1_000_000.0
    maximum_expiry = migration_now + state_module.MAX_TASK_TTL_SECONDS
    monkeypatch.setattr(state_module.time, "time", lambda: migration_now)
    with sqlite3.connect(paths.database) as connection:
        connection.execute(
            "INSERT INTO workspaces(id, root, registered_at, epoch) VALUES(?, ?, ?, 1)",
            (workspace_id, root, migration_now),
        )
        for task_id, state in (
            ("claimless-infinite", "active"),
            ("claimed-huge", "active"),
            ("unknown-infinite", "outcome_unknown"),
        ):
            insert_task(
                connection,
                task_id=task_id,
                workspace_id=workspace_id,
                state=state,
                created_at=migration_now,
            )
        for task_id in ("stale-future", "expired-stale"):
            insert_task(
                connection,
                task_id=task_id,
                workspace_id=workspace_id,
                state="active",
                created_at=migration_now - 2 * state_module.MAX_TASK_TTL_SECONDS,
            )
        connection.execute(
            "UPDATE tasks SET heartbeat_at = ?, expires_at = ? WHERE id = ?",
            (float("inf"), float("inf"), "claimless-infinite"),
        )
        connection.execute(
            "UPDATE tasks SET expires_at = ? WHERE id = ?",
            (migration_now + 10 * state_module.MAX_TASK_TTL_SECONDS, "claimed-huge"),
        )
        connection.execute(
            "UPDATE tasks SET expires_at = ? WHERE id = ?",
            (float("inf"), "unknown-infinite"),
        )
        connection.execute(
            "UPDATE tasks SET heartbeat_at = ?, expires_at = ? WHERE id = ?",
            (
                migration_now - 2 * state_module.MAX_TASK_TTL_SECONDS,
                migration_now + 10 * state_module.MAX_TASK_TTL_SECONDS,
                "stale-future",
            ),
        )
        connection.execute(
            "UPDATE tasks SET heartbeat_at = ?, expires_at = ? WHERE id = ?",
            (
                migration_now - 2 * state_module.MAX_TASK_TTL_SECONDS,
                migration_now - 1,
                "expired-stale",
            ),
        )
        connection.execute(
            "INSERT INTO claims(id, workspace_id, task_id, kind, state, queue_order, "
            "created_at, granted_at) VALUES(?, ?, ?, 'normal', 'active', 1, ?, ?)",
            ("live-claim", workspace_id, "claimed-huge", migration_now, migration_now),
        )
        connection.execute(
            "INSERT INTO claim_scopes(claim_id, scope_type, value) "
            "VALUES('live-claim', 'resource', 'unity-live')"
        )

    with open_database(paths) as connection:
        timing = {
            row["id"]: (row["heartbeat_at"], row["expires_at"])
            for row in connection.execute(
                "SELECT id, heartbeat_at, expires_at FROM tasks ORDER BY id"
            ).fetchall()
        }

    assert timing["claimless-infinite"] == (migration_now, maximum_expiry)
    assert timing["claimed-huge"][1] == maximum_expiry
    assert timing["unknown-infinite"][1] == maximum_expiry
    assert timing["stale-future"] == (migration_now, maximum_expiry)
    assert timing["expired-stale"] == (
        migration_now - 2 * state_module.MAX_TASK_TTL_SECONDS,
        migration_now - 1,
    )

    monkeypatch.setattr(coordinator_module.time, "time", lambda: migration_now)
    renewed = WorkspaceCoordinator(paths).heartbeat(workspace, "token-stale-future")
    assert renewed["expires_at"] - renewed["heartbeat_at"] == pytest.approx(
        state_module.MAX_TASK_TTL_SECONDS
    )

    monkeypatch.setattr(coordinator_module.time, "time", lambda: maximum_expiry + 1.0)
    status = WorkspaceCoordinator(paths).status(workspace)
    tasks = {task["id"]: task for task in status["tasks"]}
    assert "claimless-infinite" not in tasks
    assert tasks["claimed-huge"]["state"] == "outcome_unknown"
    assert tasks["claimed-huge"]["result"] == "expired-with-active-claim"
    assert tasks["unknown-infinite"]["state"] == "outcome_unknown"
    assert status["blocked"] is True


def test_schema_one_migration_does_not_revive_negative_infinite_expiry(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    workspace = tmp_path / "workspace"
    workspace.mkdir()
    root = str(workspace.resolve())
    workspace_id = _workspace_id(root)
    paths = create_schema_one(tmp_path / "state")
    migration_now = 1_000_000.0
    monkeypatch.setattr(state_module.time, "time", lambda: migration_now)
    with sqlite3.connect(paths.database) as connection:
        connection.execute(
            "INSERT INTO workspaces(id, root, registered_at, epoch) VALUES(?, ?, ?, 1)",
            (workspace_id, root, migration_now),
        )
        insert_task(
            connection,
            task_id="expired-owner",
            workspace_id=workspace_id,
            state="active",
            created_at=migration_now,
        )
        connection.execute(
            "UPDATE tasks SET expires_at = ? WHERE id = 'expired-owner'",
            (float("-inf"),),
        )
        connection.execute(
            "INSERT INTO claims(id, workspace_id, task_id, kind, state, queue_order, "
            "created_at, granted_at) VALUES(?, ?, ?, 'normal', 'active', 1, ?, ?)",
            ("live-claim", workspace_id, "expired-owner", migration_now, migration_now),
        )
        connection.execute(
            "INSERT INTO claim_scopes(claim_id, scope_type, value) "
            "VALUES('live-claim', 'resource', 'unity-live')"
        )

    with open_database(paths) as connection:
        migrated = connection.execute(
            "SELECT state, expires_at FROM tasks WHERE id = 'expired-owner'"
        ).fetchone()
        assert migrated is not None
        assert migrated["state"] == "active"
        assert migrated["expires_at"] == migration_now

    monkeypatch.setattr(coordinator_module.time, "time", lambda: migration_now)
    status = WorkspaceCoordinator(paths).status(workspace)
    task = next(task for task in status["tasks"] if task["id"] == "expired-owner")
    claim = next(claim for claim in status["claims"] if claim["id"] == "live-claim")
    assert task["state"] == "outcome_unknown"
    assert task["result"] == "expired-with-active-claim"
    assert claim["state"] == "active"
    assert status["blocked"] is True


def test_schema_one_migration_rejects_unexpected_table_atomically(tmp_path: Path) -> None:
    paths = create_schema_one(tmp_path / "state")
    with sqlite3.connect(paths.database) as connection:
        connection.execute("CREATE TABLE tasks_workspace_token_created(value TEXT)")

    with pytest.raises(StateError) as invalid:
        open_database(paths)
    assert invalid.value.details["reason"] == "schema-declaration-invalid"
    assert invalid.value.details["unexpected_tables"] == ["tasks_workspace_token_created"]

    with sqlite3.connect(paths.database) as connection:
        schema = connection.execute(
            "SELECT value FROM scheduler_meta WHERE key = 'schema_version'"
        ).fetchone()[0]
        columns = {row[1] for row in connection.execute("PRAGMA table_info(workspaces)").fetchall()}
        unexpected_table = connection.execute(
            "SELECT COUNT(*) FROM sqlite_master "
            "WHERE type = 'table' AND name = 'tasks_workspace_token_created'"
        ).fetchone()[0]
    assert schema == "1"
    assert "next_queue_order" not in columns
    assert unexpected_table == 1


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
        (
            (
                "UPDATE claim_scopes SET value = 'assets/hero.prefab/child' "
                "WHERE claim_id = 'second-claim'"
            ),
            "active-claim-conflict",
        ),
    ),
)
def test_schema_one_migration_rejects_unrepaired_invariants_atomically(
    tmp_path: Path, mutation: str, reason: str
) -> None:
    paths, _ = create_schema_one_active_state(tmp_path)
    with sqlite3.connect(paths.database) as connection:
        connection.execute(mutation)
    database_before = paths.database.read_bytes()

    with pytest.raises(StateError) as invalid:
        open_database(paths)

    assert invalid.value.details["reason"] == reason
    assert_schema_one_unchanged(paths, database_before)


@pytest.mark.parametrize(
    ("orphan_claim", "reason"),
    (
        (False, "schema-relational-signature-invalid"),
        (True, "relational-orphan-invalid"),
    ),
)
def test_schema_one_migration_preflight_rejects_missing_fk_and_orphan_atomically(
    tmp_path: Path, orphan_claim: bool, reason: str
) -> None:
    workspace = tmp_path / "workspace"
    workspace.mkdir()
    root = str(workspace.resolve())
    workspace_id = _workspace_id(root)
    paths = StatePaths(tmp_path / "state")
    paths.root.mkdir()
    schema = SCHEMA_ONE_SQL.replace(
        "task_id TEXT NOT NULL REFERENCES tasks(id) ON DELETE CASCADE,",
        "task_id TEXT NOT NULL,",
    )
    assert schema != SCHEMA_ONE_SQL
    with sqlite3.connect(paths.database) as connection:
        connection.executescript(schema)
        connection.execute(
            "INSERT INTO workspaces(id, root, registered_at, epoch) VALUES(?, ?, 1000, 1)",
            (workspace_id, root),
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
    database_before = paths.database.read_bytes()

    with pytest.raises(StateError) as preflight:
        verify_state(paths.database, for_migration=True)
    assert preflight.value.details["reason"] == reason
    with pytest.raises(StateError) as invalid:
        open_database(paths)

    assert invalid.value.details["reason"] == reason
    assert_schema_one_unchanged(paths, database_before)


def test_schema_one_migration_rejects_recovery_binding_mismatch_atomically(
    tmp_path: Path,
) -> None:
    paths, workspace_id = create_schema_one_active_state(tmp_path)
    with sqlite3.connect(paths.database) as connection:
        connection.execute(
            "UPDATE tasks SET state = 'completed', result = 'recovered-completed', "
            "finished_at = 1001, note = 'verified evidence' WHERE id = 'first-task'"
        )
        connection.execute(
            "UPDATE claims SET state = 'released', released_at = 1001 WHERE task_id = 'first-task'"
        )
        connection.execute(
            "INSERT INTO recovery_events(id, workspace_id, task_id, resolution, evidence, "
            "created_at) VALUES('recovery-event', ?, 'first-task', 'completed', "
            "'verified evidence', 1002)",
            (workspace_id,),
        )
    database_before = paths.database.read_bytes()

    with pytest.raises(StateError) as invalid:
        open_database(paths)

    assert invalid.value.details["reason"] == "recovery-event-binding-invalid"
    assert_schema_one_unchanged(paths, database_before)


def test_schema_one_migration_rejects_wrong_future_index_signature_atomically(
    tmp_path: Path,
) -> None:
    paths, _ = create_schema_one_active_state(tmp_path)
    with sqlite3.connect(paths.database) as connection:
        connection.execute("CREATE INDEX tasks_state_expires ON tasks(expires_at, state)")
    database_before = paths.database.read_bytes()

    with pytest.raises(StateError) as invalid:
        open_database(paths)

    assert invalid.value.details["reason"] == "schema-index-signature-invalid"
    assert invalid.value.details["indexes"] == ["tasks_state_expires"]
    assert_schema_one_unchanged(paths, database_before)


@pytest.mark.parametrize("claim_state", ["released", "cancelled"])
def test_schema_one_migration_cleans_closed_legacy_park_markers(
    tmp_path: Path, claim_state: str
) -> None:
    paths, _ = create_schema_one_active_state(tmp_path)
    with sqlite3.connect(paths.database) as connection:
        connection.execute(
            "UPDATE claims SET state = ?, released_at = 1001 WHERE id = 'first-claim'",
            (claim_state,),
        )
        connection.execute(
            "INSERT INTO claim_scopes(claim_id, scope_type, value) "
            "VALUES('first-claim', 'parked_for', 'legacy-freeze')"
        )

    with open_database(paths) as connection:
        marker_count = connection.execute(
            "SELECT COUNT(*) FROM claim_scopes WHERE scope_type = 'parked_for'"
        ).fetchone()[0]

    assert marker_count == 0
    assert inspect_state(paths.database)["schema_version"] == 3


def test_schema_one_migration_rejects_active_legacy_park_marker_atomically(
    tmp_path: Path,
) -> None:
    paths, _ = create_schema_one_active_state(tmp_path)
    with sqlite3.connect(paths.database) as connection:
        connection.execute(
            "INSERT INTO claim_scopes(claim_id, scope_type, value) "
            "VALUES('first-claim', 'parked_for', 'legacy-freeze')"
        )
    database_before = paths.database.read_bytes()

    with pytest.raises(StateError) as preflight:
        verify_state(paths.database, for_migration=True)
    assert preflight.value.details["reason"] == "schema-one-active-park-marker-invalid"
    with pytest.raises(StateError) as invalid:
        open_database(paths)

    assert invalid.value.details["reason"] == "schema-one-active-park-marker-invalid"
    assert_schema_one_unchanged(paths, database_before)


def test_concurrent_schema_one_openers_converge_on_one_complete_migration(
    tmp_path: Path,
) -> None:
    paths = create_schema_one(tmp_path / "state")
    barrier = threading.Barrier(2)

    def migrate() -> str:
        barrier.wait()
        with open_database(paths) as connection:
            return str(
                connection.execute(
                    "SELECT value FROM scheduler_meta WHERE key = 'schema_version'"
                ).fetchone()["value"]
            )

    with ThreadPoolExecutor(max_workers=2) as pool:
        versions = list(pool.map(lambda _: migrate(), range(2)))

    assert versions == ["3", "3"]
    with open_database(paths) as connection:
        columns = {
            row["name"] for row in connection.execute("PRAGMA table_info(workspaces)").fetchall()
        }
        token_indexes = connection.execute(
            "SELECT COUNT(*) AS count FROM sqlite_master "
            "WHERE type = 'index' AND name = 'tasks_workspace_token_created'"
        ).fetchone()["count"]
    assert "next_queue_order" in columns
    assert token_indexes == 1


def test_schema_three_is_rejected_by_the_legacy_schema_one_contract(tmp_path: Path) -> None:
    paths = resolve_state_paths(tmp_path / "state")
    with open_database(paths):
        pass

    def open_as_legacy_schema_one() -> None:
        with sqlite3.connect(paths.database) as connection:
            version = connection.execute(
                "SELECT value FROM scheduler_meta WHERE key = 'schema_version'"
            ).fetchone()[0]
        if version != "1":
            raise UsageError(f"Unsupported scheduler schema {version}; expected 1.")

    with pytest.raises(UsageError, match="schema 3; expected 1"):
        open_as_legacy_schema_one()


def test_queue_counter_and_token_authentication_use_indexed_bounded_paths(
    tmp_path: Path,
) -> None:
    workspace = tmp_path / "workspace"
    other_workspace = tmp_path / "other-workspace"
    workspace.mkdir()
    other_workspace.mkdir()
    coordinator = WorkspaceCoordinator(resolve_state_paths(tmp_path / "state"))
    registered = coordinator.register(workspace)
    coordinator.register(other_workspace)
    _, token = coordinator.start_task(workspace, "owner", "indexed token", token="known-token")
    _, other_token = coordinator.start_task(other_workspace, "other", "separate counter")
    first = coordinator.acquire_claim(workspace, token, writes=("Assets/One.asset",))
    second = coordinator.acquire_claim(workspace, token, writes=("Assets/Two.asset",))
    other = coordinator.acquire_claim(other_workspace, other_token, writes=("Assets/Other.asset",))

    assert (first["queue_order"], second["queue_order"]) == (1, 2)
    assert other["queue_order"] == 1
    with open_database(coordinator.paths) as connection:
        counter_plan = connection.execute(
            "EXPLAIN QUERY PLAN SELECT next_queue_order FROM workspaces WHERE id = ?",
            (registered["id"],),
        ).fetchall()
        token_plan = connection.execute(
            "EXPLAIN QUERY PLAN SELECT * FROM tasks "
            "WHERE workspace_id = ? AND token_hash = ? "
            "ORDER BY created_at DESC LIMIT 1",
            (registered["id"], _token_hash("known-token")),
        ).fetchall()
        retention_plan = connection.execute(
            "EXPLAIN QUERY PLAN SELECT candidate.id FROM tasks AS candidate "
            "WHERE candidate.workspace_id = ? "
            "AND candidate.state IN ('completed', 'failed', 'expired') "
            "AND NOT EXISTS ("
            "SELECT 1 FROM claims WHERE claims.task_id = candidate.id "
            "AND claims.state IN ('queued', 'active', 'parked')"
            ") "
            "ORDER BY candidate.finished_at DESC, candidate.created_at DESC, candidate.id DESC "
            "LIMIT -1 OFFSET ?",
            (registered["id"], TERMINAL_TASK_RETENTION),
        ).fetchall()
        expiration_plan = connection.execute(
            "EXPLAIN QUERY PLAN SELECT id, workspace_id FROM tasks "
            "WHERE state = 'active' AND expires_at <= ?",
            (time.time(),),
        ).fetchall()
        targeted_timing_plan = connection.execute(
            "EXPLAIN QUERY PLAN SELECT id, workspace_id, heartbeat_at, expires_at FROM tasks "
            "WHERE workspace_id = ? AND state = 'active'",
            (registered["id"],),
        ).fetchall()
        targeted_expiration_plan = connection.execute(
            "EXPLAIN QUERY PLAN SELECT id, workspace_id FROM tasks "
            "WHERE workspace_id = ? AND state = 'active' AND expires_at <= ?",
            (registered["id"], time.time()),
        ).fetchall()
        next_order = connection.execute(
            "SELECT next_queue_order FROM workspaces WHERE id = ?", (registered["id"],)
        ).fetchone()["next_queue_order"]

    assert next_order == 3
    assert any("sqlite_autoindex_workspaces_1" in row["detail"] for row in counter_plan)
    assert any("tasks_workspace_token_created" in row["detail"] for row in token_plan)
    assert any("tasks_workspace_terminal_recency" in row["detail"] for row in retention_plan)
    assert any("claims_task_state" in row["detail"] for row in retention_plan)
    assert any("tasks_state_expires" in row["detail"] for row in expiration_plan)
    assert any("tasks_workspace_state" in row["detail"] for row in targeted_timing_plan)
    assert any("tasks_workspace_state_expires" in row["detail"] for row in targeted_expiration_plan)


def test_targeted_maintenance_prunes_only_the_requested_workspace(
    tmp_path: Path,
) -> None:
    first_workspace = tmp_path / "first"
    second_workspace = tmp_path / "second"
    first_workspace.mkdir()
    second_workspace.mkdir()
    coordinator = WorkspaceCoordinator(resolve_state_paths(tmp_path / "state"))
    first = coordinator.register(first_workspace)
    second = coordinator.register(second_workspace)
    now = time.time()
    with open_database(coordinator.paths) as connection:
        for workspace_id, prefix in (
            (str(first["id"]), "first"),
            (str(second["id"]), "second"),
        ):
            for index in range(TERMINAL_TASK_RETENTION + 1):
                insert_task(
                    connection,
                    task_id=f"{prefix}-{index:04d}",
                    workspace_id=workspace_id,
                    state="completed",
                    created_at=now + index,
                    finished_at=now + index,
                )

    coordinator.status(first_workspace)
    with open_database(coordinator.paths) as connection:
        first_count = connection.execute(
            "SELECT COUNT(*) FROM tasks WHERE workspace_id = ?",
            (first["id"],),
        ).fetchone()[0]
        second_count = connection.execute(
            "SELECT COUNT(*) FROM tasks WHERE workspace_id = ?",
            (second["id"],),
        ).fetchone()[0]
    assert first_count == TERMINAL_TASK_RETENTION
    assert second_count == TERMINAL_TASK_RETENTION + 1

    coordinator.list_workspaces()
    with open_database(coordinator.paths) as connection:
        second_count = connection.execute(
            "SELECT COUNT(*) FROM tasks WHERE workspace_id = ?",
            (second["id"],),
        ).fetchone()[0]
    assert second_count == TERMINAL_TASK_RETENTION


def test_terminal_retention_is_per_workspace_and_preserves_open_unknown_and_cascades(
    tmp_path: Path,
) -> None:
    workspace = tmp_path / "workspace"
    workspace.mkdir()
    coordinator = WorkspaceCoordinator(resolve_state_paths(tmp_path / "state"))
    registered = coordinator.register(workspace)
    workspace_id = str(registered["id"])
    now = time.time()
    with open_database(coordinator.paths) as connection:
        for index in range(TERMINAL_TASK_RETENTION + 5):
            insert_task(
                connection,
                task_id=f"terminal-{index:04d}",
                workspace_id=workspace_id,
                state="completed",
                created_at=now + index,
                finished_at=now + index,
            )
        insert_task(
            connection,
            task_id="active-task",
            workspace_id=workspace_id,
            state="active",
            created_at=now,
        )
        insert_task(
            connection,
            task_id="unknown-task",
            workspace_id=workspace_id,
            state="outcome_unknown",
            created_at=now,
        )
        connection.execute(
            "INSERT INTO claims(id, workspace_id, task_id, kind, state, queue_order, created_at, "
            "released_at) VALUES('old-claim', ?, 'terminal-0000', 'normal', 'released', 1, ?, ?)",
            (workspace_id, now, now),
        )
        connection.execute(
            "INSERT INTO claim_scopes(claim_id, scope_type, value) "
            "VALUES('old-claim', 'write', 'assets/old.asset')"
        )
        connection.execute(
            "INSERT INTO recovery_events(id, workspace_id, task_id, resolution, evidence, "
            "created_at) VALUES('old-recovery', ?, 'terminal-0000', 'completed', 'evidence', ?)",
            (workspace_id, now),
        )
        connection.execute(
            "INSERT INTO claims(id, workspace_id, task_id, kind, state, queue_order, created_at) "
            "VALUES('active-claim', ?, 'active-task', 'normal', 'active', 2, ?)",
            (workspace_id, now),
        )
        connection.execute(
            "INSERT INTO claims(id, workspace_id, task_id, kind, state, queue_order, created_at) "
            "VALUES('unknown-claim', ?, 'unknown-task', 'normal', 'active', 3, ?)",
            (workspace_id, now),
        )

    coordinator.status(workspace)
    with open_database(coordinator.paths) as connection:
        terminal_count = connection.execute(
            "SELECT COUNT(*) FROM tasks WHERE workspace_id = ? "
            "AND state IN ('completed', 'failed', 'expired')",
            (workspace_id,),
        ).fetchone()[0]
        task_ids = {
            row[0]
            for row in connection.execute(
                "SELECT id FROM tasks WHERE workspace_id = ?", (workspace_id,)
            ).fetchall()
        }
        old_claim = connection.execute("SELECT 1 FROM claims WHERE id = 'old-claim'").fetchone()
        old_scope = connection.execute(
            "SELECT 1 FROM claim_scopes WHERE claim_id = 'old-claim'"
        ).fetchone()
        old_recovery = connection.execute(
            "SELECT 1 FROM recovery_events WHERE id = 'old-recovery'"
        ).fetchone()

    assert terminal_count == TERMINAL_TASK_RETENTION
    assert not {f"terminal-{index:04d}" for index in range(5)} & task_ids
    assert {"terminal-0005", "terminal-1004", "active-task", "unknown-task"} <= task_ids
    assert old_claim is None
    assert old_scope is None
    assert old_recovery is None


def test_task_release_preserves_cleanup_lineage_then_prunes_terminal_history(
    tmp_path: Path,
) -> None:
    workspace = tmp_path / "workspace"
    workspace.mkdir()
    coordinator = WorkspaceCoordinator(resolve_state_paths(tmp_path / "state"))
    registered = coordinator.register(workspace)
    workspace_id = str(registered["id"])
    now = time.time()
    with open_database(coordinator.paths) as connection:
        for index in range(TERMINAL_TASK_RETENTION):
            insert_task(
                connection,
                task_id=f"terminal-{index:04d}",
                workspace_id=workspace_id,
                state="completed",
                created_at=now + index,
                finished_at=now + index,
            )

    _, token = coordinator.start_task(workspace, "newest", "new terminal")
    released = coordinator.release_task(workspace, token, result="completed")
    with open_database(coordinator.paths) as connection:
        protected_count = connection.execute(
            "SELECT COUNT(*) FROM tasks WHERE workspace_id = ? "
            "AND state IN ('completed', 'failed', 'expired')",
            (workspace_id,),
        ).fetchone()[0]
    assert protected_count == TERMINAL_TASK_RETENTION + 1

    coordinator.acknowledge_receipt(
        str(released["operation"]["operation_id"]),
        str(released["operation"]["fingerprint"]),
        str(released["operation"]["delivery_digest"]),
    )
    coordinator.status(workspace)
    with open_database(coordinator.paths) as connection:
        terminal_count = connection.execute(
            "SELECT COUNT(*) FROM tasks WHERE workspace_id = ? "
            "AND state IN ('completed', 'failed', 'expired')",
            (workspace_id,),
        ).fetchone()[0]
    assert terminal_count == TERMINAL_TASK_RETENTION
