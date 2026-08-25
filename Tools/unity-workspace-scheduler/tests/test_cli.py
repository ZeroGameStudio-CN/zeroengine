from __future__ import annotations

import ctypes
import json
import os
import shutil
import stat
import subprocess
import sys
import tempfile
import uuid
from pathlib import Path

import pytest

import unity_workspace_scheduler.coordinator as coordinator_module
import unity_workspace_scheduler.state as state_module
from unity_workspace_scheduler.cli import _emit, build_parser
from unity_workspace_scheduler.cli import run as _cli_run
from unity_workspace_scheduler.errors import UsageError
from unity_workspace_scheduler.state import open_database, resolve_state_paths

_MUTATION_COMMANDS = {
    ("workspace", "register"),
    ("workspace", "unregister"),
    ("task", "start"),
    ("task", "heartbeat"),
    ("task", "park"),
    ("task", "release"),
    ("claim", "acquire"),
    ("claim", "release"),
    ("queue", "cancel"),
    ("freeze", "acquire"),
    ("recovery", "resolve"),
}


def _with_operation(arguments: list[str]) -> list[str]:
    updated = list(arguments)
    command = next(
        (
            (updated[index], updated[index + 1])
            for index in range(len(updated) - 1)
            if (updated[index], updated[index + 1]) in _MUTATION_COMMANDS
        ),
        None,
    )
    if command is not None and "--operation-id" not in updated:
        updated.extend(("--operation-id", str(uuid.uuid4())))
    return updated


def run(arguments: list[str]) -> int:
    return _cli_run(_with_operation(arguments))


def _create_private_windows_directory(path: Path) -> None:
    """Atomically create a test-only directory with the token allowlist DACL."""

    from ctypes import wintypes

    class _SecurityAttributes(ctypes.Structure):
        _fields_ = [
            ("length", wintypes.DWORD),
            ("security_descriptor", ctypes.c_void_p),
            ("inherit_handle", wintypes.BOOL),
        ]

    if not path.is_absolute():
        raise ValueError(f"Windows test directory must be absolute: {path}")
    current_sid = state_module._current_windows_user_sid()
    sddl = f"O:{current_sid}D:P(A;OICI;FA;;;{current_sid})(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)"
    advapi32 = ctypes.WinDLL("advapi32", use_last_error=True)
    kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
    advapi32.ConvertStringSecurityDescriptorToSecurityDescriptorW.argtypes = [
        wintypes.LPCWSTR,
        wintypes.DWORD,
        ctypes.POINTER(ctypes.c_void_p),
        ctypes.POINTER(wintypes.DWORD),
    ]
    advapi32.ConvertStringSecurityDescriptorToSecurityDescriptorW.restype = wintypes.BOOL
    kernel32.CreateDirectoryW.argtypes = [
        wintypes.LPCWSTR,
        ctypes.POINTER(_SecurityAttributes),
    ]
    kernel32.CreateDirectoryW.restype = wintypes.BOOL
    kernel32.LocalFree.argtypes = [ctypes.c_void_p]
    kernel32.LocalFree.restype = ctypes.c_void_p

    descriptor = ctypes.c_void_p()
    descriptor_size = wintypes.DWORD()
    if not advapi32.ConvertStringSecurityDescriptorToSecurityDescriptorW(
        sddl,
        1,
        ctypes.byref(descriptor),
        ctypes.byref(descriptor_size),
    ):
        raise ctypes.WinError(ctypes.get_last_error())
    try:
        security_attributes = _SecurityAttributes(
            ctypes.sizeof(_SecurityAttributes), descriptor, False
        )
        if kernel32.CreateDirectoryW(str(path), ctypes.byref(security_attributes)):
            return
        error = ctypes.get_last_error()
        if error == 183:
            raise FileExistsError(error, "Windows test directory already exists", str(path))
        raise ctypes.WinError(error)
    finally:
        kernel32.LocalFree(descriptor)


def _windows_directory_security(path: Path) -> tuple[str, int, list[tuple[int, int, int, str]]]:
    """Read the created test directory's DACL without changing it."""

    from ctypes import wintypes

    class _Acl(ctypes.Structure):
        _fields_ = [
            ("revision", ctypes.c_ubyte),
            ("reserved_one", ctypes.c_ubyte),
            ("size", wintypes.WORD),
            ("ace_count", wintypes.WORD),
            ("reserved_two", wintypes.WORD),
        ]

    class _AceHeader(ctypes.Structure):
        _fields_ = [
            ("ace_type", ctypes.c_ubyte),
            ("ace_flags", ctypes.c_ubyte),
            ("ace_size", wintypes.WORD),
        ]

    advapi32 = ctypes.WinDLL("advapi32", use_last_error=True)
    kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
    advapi32.GetNamedSecurityInfoW.argtypes = [
        wintypes.LPWSTR,
        ctypes.c_int,
        wintypes.DWORD,
        ctypes.POINTER(ctypes.c_void_p),
        ctypes.POINTER(ctypes.c_void_p),
        ctypes.POINTER(ctypes.c_void_p),
        ctypes.POINTER(ctypes.c_void_p),
        ctypes.POINTER(ctypes.c_void_p),
    ]
    advapi32.GetNamedSecurityInfoW.restype = wintypes.DWORD
    advapi32.GetSecurityDescriptorControl.argtypes = [
        ctypes.c_void_p,
        ctypes.POINTER(wintypes.WORD),
        ctypes.POINTER(wintypes.DWORD),
    ]
    advapi32.GetSecurityDescriptorControl.restype = wintypes.BOOL
    advapi32.GetAce.argtypes = [
        ctypes.c_void_p,
        wintypes.DWORD,
        ctypes.POINTER(ctypes.c_void_p),
    ]
    advapi32.GetAce.restype = wintypes.BOOL
    kernel32.LocalFree.argtypes = [ctypes.c_void_p]
    kernel32.LocalFree.restype = ctypes.c_void_p

    owner = ctypes.c_void_p()
    dacl = ctypes.c_void_p()
    descriptor = ctypes.c_void_p()
    result = advapi32.GetNamedSecurityInfoW(
        str(path),
        1,
        0x00000001 | 0x00000004,
        ctypes.byref(owner),
        None,
        ctypes.byref(dacl),
        None,
        ctypes.byref(descriptor),
    )
    if result != 0:
        raise OSError(result, "Windows test directory security inspection failed")
    try:
        if not dacl:
            raise OSError("Windows test directory has a null DACL")
        control = wintypes.WORD()
        revision = wintypes.DWORD()
        if not advapi32.GetSecurityDescriptorControl(
            descriptor, ctypes.byref(control), ctypes.byref(revision)
        ):
            raise ctypes.WinError(ctypes.get_last_error())
        acl = ctypes.cast(dacl, ctypes.POINTER(_Acl)).contents
        entries: list[tuple[int, int, int, str]] = []
        for index in range(acl.ace_count):
            ace = ctypes.c_void_p()
            if not advapi32.GetAce(dacl, index, ctypes.byref(ace)):
                raise ctypes.WinError(ctypes.get_last_error())
            header = ctypes.cast(ace, ctypes.POINTER(_AceHeader)).contents
            address = int(ace.value)
            if header.ace_type == 0:
                if header.ace_size < 12:
                    raise OSError("Windows test directory has a truncated allow ACE")
                mask = ctypes.c_uint32.from_address(address + 4).value
                sid = state_module._windows_sid_string(ctypes.c_void_p(address + 8))
            else:
                mask = 0
                sid = ""
            entries.append((int(header.ace_type), int(header.ace_flags), mask, sid))
        owner_sid = state_module._windows_sid_string(owner)
        return owner_sid, int(control.value), entries
    finally:
        kernel32.LocalFree(descriptor)


@pytest.fixture
def private_token_root(tmp_path: Path):
    if os.name != "nt":
        yield tmp_path
        return
    temp_root = Path(tempfile.gettempdir()).resolve(strict=True)
    root: Path | None = None
    for _ in range(32):
        candidate = temp_root / f"unity-scheduler-test-{uuid.uuid4().hex}"
        try:
            _create_private_windows_directory(candidate)
        except FileExistsError:
            continue
        root = candidate
        break
    if root is None:
        raise RuntimeError("Could not allocate a unique Windows test directory.")
    try:
        yield root
    finally:
        shutil.rmtree(root)


@pytest.mark.skipif(os.name != "nt", reason="Windows ACL fixture contract only")
def test_private_token_root_is_protected_and_supports_token_lifecycle(
    private_token_root: Path,
) -> None:
    owner_sid, control, entries = _windows_directory_security(private_token_root)
    current_sid = state_module._current_windows_user_sid()
    assert owner_sid == current_sid
    assert control & 0x1000
    expected_entries = [
        (0, 0x03, 0x001F01FF, current_sid),
        (0, 0x03, 0x001F01FF, "S-1-5-18"),
        (0, 0x03, 0x001F01FF, "S-1-5-32-544"),
    ]
    assert len(entries) == len(expected_entries) == 3
    assert sorted(entries) == sorted(expected_entries)

    token_path = state_module.create_token_file(private_token_root / "fixture.token", "fixture")
    assert state_module.read_token_file(token_path) == "fixture"
    assert state_module.remove_matching_token_file(token_path, "fixture") is True
    assert not token_path.exists()


def read_output(capsys) -> dict[str, object]:
    payload = json.loads(capsys.readouterr().out)
    assert payload["protocol_version"] == 3
    return payload


def _database_snapshot(state_dir: Path) -> tuple[tuple[str, tuple[tuple[object, ...], ...]], ...]:
    with open_database(resolve_state_paths(state_dir)) as connection:
        tables = [
            row["name"]
            for row in connection.execute(
                "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' "
                "ORDER BY name"
            ).fetchall()
        ]
        return tuple(
            (
                table,
                tuple(
                    tuple(row) for row in connection.execute(f'SELECT * FROM "{table}"').fetchall()
                ),
            )
            for table in tables
        )


def test_cli_heartbeat_omits_ttl_by_default() -> None:
    args = build_parser().parse_args(
        [
            "task",
            "heartbeat",
            "--workspace",
            ".",
            "--token-file",
            "task.token",
            "--operation-id",
            str(uuid.uuid4()),
        ]
    )

    assert args.ttl is None


def test_cli_mutation_requires_operation_id() -> None:
    with pytest.raises(UsageError):
        build_parser().parse_args(["workspace", "register", "--workspace", "."])


@pytest.mark.parametrize(
    "invalid_operation_id",
    [
        "not-a-uuid",
        "AAAAAAAA-AAAA-4AAA-8AAA-AAAAAAAAAAAA",
        "aaaaaaaa-aaaa-1aaa-8aaa-aaaaaaaaaaaa",
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
    ],
)
def test_cli_rejects_noncanonical_uuid4_before_dispatch(invalid_operation_id: str) -> None:
    with pytest.raises(UsageError) as invalid:
        build_parser().parse_args(
            [
                "task",
                "heartbeat",
                "--workspace",
                ".",
                "--token-file",
                "missing.token",
                "--operation-id",
                invalid_operation_id,
            ]
        )

    assert invalid.value.details["reason"] == "operation-id-invalid"


def test_cli_json_output_is_ascii_safe_and_preserves_unicode(capsys) -> None:
    _emit({"message": "中文任务备注"})

    output = capsys.readouterr().out
    assert output.isascii()
    assert json.loads(output) == {"message": "中文任务备注", "protocol_version": 3}


def test_cli_missing_token_receipt_only_replays_claim_queue_and_recovery_lifecycles(
    tmp_path: Path,
    private_token_root: Path,
    capsys,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    state = tmp_path / "state"
    workspace = tmp_path / "workspace"
    workspace.mkdir()

    def remove_token(path: Path, _expected_hash: str) -> bool:
        path.unlink(missing_ok=True)
        return True

    monkeypatch.setattr(coordinator_module, "remove_matching_token_hash_file", remove_token)

    def call(arguments: list[str]) -> dict[str, object]:
        assert run(["--state-dir", str(state), *arguments]) == 0
        return read_output(capsys)

    call(["workspace", "register", "--workspace", str(workspace)])
    owner_token_file = private_token_root / "cli-claim-owner.token"
    call(
        [
            "task",
            "start",
            "--workspace",
            str(workspace),
            "--owner",
            "claim-owner",
            "--summary",
            "claim lifecycle",
            "--token-file",
            str(owner_token_file),
        ]
    )
    claim = call(
        [
            "claim",
            "acquire",
            "--workspace",
            str(workspace),
            "--resource",
            "claim-resource",
            "--token-file",
            str(owner_token_file),
        ]
    )["result"]
    claim_release_id = str(uuid.uuid4())
    call(
        [
            "claim",
            "release",
            "--workspace",
            str(workspace),
            "--claim-id",
            str(claim["id"]),
            "--token-file",
            str(owner_token_file),
            "--operation-id",
            claim_release_id,
        ]
    )
    task_release = call(
        [
            "task",
            "release",
            "--workspace",
            str(workspace),
            "--result",
            "completed",
            "--token-file",
            str(owner_token_file),
        ]
    )["result"]
    call(
        [
            "receipt",
            "ack",
            "--operation-id",
            str(task_release["operation"]["operation_id"]),
            "--fingerprint",
            str(task_release["operation"]["fingerprint"]),
            "--delivery-digest",
            str(task_release["operation"]["delivery_digest"]),
        ]
    )
    assert not owner_token_file.exists()
    replayed_claim = call(
        [
            "claim",
            "release",
            "--workspace",
            str(workspace),
            "--claim-id",
            str(claim["id"]),
            "--token-file",
            str(owner_token_file),
            "--operation-id",
            claim_release_id,
            "--receipt-only",
        ]
    )["result"]
    assert replayed_claim["operation"]["replayed"] is True
    with open_database(resolve_state_paths(state)) as connection:
        proof = connection.execute(
            "SELECT terminal_json, retired_at FROM operation_receipts WHERE operation_id = ?",
            (claim_release_id,),
        ).fetchone()
    assert proof["terminal_json"] is not None
    assert proof["retired_at"] is not None
    assert json.loads(proof["terminal_json"])["token_cleanup_completed"] is True

    blocker_token_file = private_token_root / "cli-queue-blocker.token"
    queued_token_file = private_token_root / "cli-queue-owner.token"
    call(
        [
            "task",
            "start",
            "--workspace",
            str(workspace),
            "--owner",
            "queue-blocker",
            "--summary",
            "queue blocker",
            "--token-file",
            str(blocker_token_file),
        ]
    )
    call(
        [
            "task",
            "start",
            "--workspace",
            str(workspace),
            "--owner",
            "queue-owner",
            "--summary",
            "queue lifecycle",
            "--token-file",
            str(queued_token_file),
        ]
    )
    blocker_claim = call(
        [
            "claim",
            "acquire",
            "--workspace",
            str(workspace),
            "--resource",
            "queue-resource",
            "--token-file",
            str(blocker_token_file),
        ]
    )["result"]
    queued_claim = call(
        [
            "claim",
            "acquire",
            "--workspace",
            str(workspace),
            "--resource",
            "queue-resource",
            "--keep-queued",
            "--token-file",
            str(queued_token_file),
        ]
    )["result"]
    queue_cancel_id = str(uuid.uuid4())
    call(
        [
            "queue",
            "cancel",
            "--workspace",
            str(workspace),
            "--claim-id",
            str(queued_claim["id"]),
            "--token-file",
            str(queued_token_file),
            "--operation-id",
            queue_cancel_id,
        ]
    )
    queued_release = call(
        [
            "task",
            "release",
            "--workspace",
            str(workspace),
            "--result",
            "completed",
            "--token-file",
            str(queued_token_file),
        ]
    )["result"]
    call(
        [
            "receipt",
            "ack",
            "--operation-id",
            str(queued_release["operation"]["operation_id"]),
            "--fingerprint",
            str(queued_release["operation"]["fingerprint"]),
            "--delivery-digest",
            str(queued_release["operation"]["delivery_digest"]),
        ]
    )
    replayed_queue = call(
        [
            "queue",
            "cancel",
            "--workspace",
            str(workspace),
            "--claim-id",
            str(queued_claim["id"]),
            "--token-file",
            str(queued_token_file),
            "--operation-id",
            queue_cancel_id,
            "--receipt-only",
        ]
    )["result"]
    assert replayed_queue["operation"]["replayed"] is True
    with open_database(resolve_state_paths(state)) as connection:
        proof = connection.execute(
            "SELECT terminal_json, retired_at FROM operation_receipts WHERE operation_id = ?",
            (queue_cancel_id,),
        ).fetchone()
    assert proof["terminal_json"] is not None
    assert proof["retired_at"] is not None
    assert json.loads(proof["terminal_json"])["token_cleanup_completed"] is True

    call(
        [
            "claim",
            "release",
            "--workspace",
            str(workspace),
            "--claim-id",
            str(blocker_claim["id"]),
            "--token-file",
            str(blocker_token_file),
        ]
    )
    blocker_release = call(
        [
            "task",
            "release",
            "--workspace",
            str(workspace),
            "--result",
            "completed",
            "--token-file",
            str(blocker_token_file),
        ]
    )["result"]
    call(
        [
            "receipt",
            "ack",
            "--operation-id",
            str(blocker_release["operation"]["operation_id"]),
            "--fingerprint",
            str(blocker_release["operation"]["fingerprint"]),
            "--delivery-digest",
            str(blocker_release["operation"]["delivery_digest"]),
        ]
    )

    recovery_token_file = private_token_root / "cli-recovery-owner.token"
    unknown = call(
        [
            "task",
            "start",
            "--workspace",
            str(workspace),
            "--owner",
            "recovery-owner",
            "--summary",
            "recovery lifecycle",
            "--token-file",
            str(recovery_token_file),
        ]
    )["result"]
    unknown_release_id = str(uuid.uuid4())
    unknown_release = call(
        [
            "task",
            "release",
            "--workspace",
            str(workspace),
            "--result",
            "outcome-unknown",
            "--token-file",
            str(recovery_token_file),
            "--operation-id",
            unknown_release_id,
        ]
    )["result"]
    unresolved_replay = call(
        [
            "task",
            "release",
            "--workspace",
            str(workspace),
            "--result",
            "outcome-unknown",
            "--token-file",
            str(recovery_token_file),
            "--operation-id",
            unknown_release_id,
            "--receipt-only",
        ]
    )["result"]
    assert unresolved_replay["state"] == "outcome_unknown"
    assert unresolved_replay["operation"]["replayed"] is True
    call(
        [
            "recovery",
            "resolve",
            "--workspace",
            str(workspace),
            "--task-id",
            str(unknown["id"]),
            "--resolution",
            "completed",
            "--evidence",
            "executor proof",
        ]
    )
    call(["workspace", "status", "--workspace", str(workspace)])
    assert not recovery_token_file.exists()
    recovered_replay = call(
        [
            "task",
            "release",
            "--workspace",
            str(workspace),
            "--result",
            "outcome-unknown",
            "--token-file",
            str(recovery_token_file),
            "--operation-id",
            unknown_release_id,
            "--receipt-only",
        ]
    )["result"]
    assert recovered_replay["resolution_reason"] == "task-recovery-resolved"
    assert recovered_replay["terminal_state"] == "completed"
    assert recovered_replay["operation"]["replayed"] is True
    assert unknown_release["state"] == "outcome_unknown"


def test_cli_missing_token_replay_rejects_conflicts_without_database_writes(
    tmp_path: Path,
    private_token_root: Path,
    capsys,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    state = tmp_path / "state"
    workspace = tmp_path / "workspace"
    other_workspace = tmp_path / "other-workspace"
    workspace.mkdir()
    other_workspace.mkdir()

    def remove_token(path: Path, _expected_hash: str) -> bool:
        path.unlink(missing_ok=True)
        return True

    monkeypatch.setattr(coordinator_module, "remove_matching_token_hash_file", remove_token)

    def call(arguments: list[str]) -> dict[str, object]:
        assert run(["--state-dir", str(state), *arguments]) == 0
        return read_output(capsys)

    call(["workspace", "register", "--workspace", str(workspace)])
    call(["workspace", "register", "--workspace", str(other_workspace)])
    token_file = private_token_root / "cli-conflict-owner.token"
    call(
        [
            "task",
            "start",
            "--workspace",
            str(workspace),
            "--owner",
            "conflict-owner",
            "--summary",
            "conflict lifecycle",
            "--token-file",
            str(token_file),
        ]
    )
    claim = call(
        [
            "claim",
            "acquire",
            "--workspace",
            str(workspace),
            "--resource",
            "conflict-resource",
            "--token-file",
            str(token_file),
        ]
    )["result"]
    claim_release_id = str(uuid.uuid4())
    call(
        [
            "claim",
            "release",
            "--workspace",
            str(workspace),
            "--claim-id",
            str(claim["id"]),
            "--token-file",
            str(token_file),
            "--operation-id",
            claim_release_id,
        ]
    )
    task_release = call(
        [
            "task",
            "release",
            "--workspace",
            str(workspace),
            "--result",
            "completed",
            "--token-file",
            str(token_file),
            "--note",
            "original note",
        ]
    )["result"]
    task_release_id = str(task_release["operation"]["operation_id"])
    call(
        [
            "receipt",
            "ack",
            "--operation-id",
            task_release_id,
            "--fingerprint",
            str(task_release["operation"]["fingerprint"]),
            "--delivery-digest",
            str(task_release["operation"]["delivery_digest"]),
        ]
    )
    assert not token_file.exists()
    before = _database_snapshot(state)

    invalid_commands = [
        [
            "claim",
            "release",
            "--workspace",
            str(workspace),
            "--claim-id",
            str(claim["id"]),
            "--token-file",
            str(token_file),
            "--operation-id",
            str(uuid.uuid4()),
            "--receipt-only",
        ],
        [
            "claim",
            "release",
            "--workspace",
            str(workspace),
            "--claim-id",
            str(uuid.uuid4()),
            "--token-file",
            str(token_file),
            "--operation-id",
            claim_release_id,
            "--receipt-only",
        ],
        [
            "claim",
            "release",
            "--workspace",
            str(other_workspace),
            "--claim-id",
            str(claim["id"]),
            "--token-file",
            str(token_file),
            "--operation-id",
            claim_release_id,
            "--receipt-only",
        ],
        [
            "task",
            "release",
            "--workspace",
            str(workspace),
            "--result",
            "completed",
            "--note",
            "wrong note",
            "--token-file",
            str(token_file),
            "--operation-id",
            task_release_id,
            "--receipt-only",
        ],
        [
            "task",
            "release",
            "--workspace",
            str(workspace),
            "--result",
            "failed",
            "--note",
            "original note",
            "--token-file",
            str(token_file),
            "--operation-id",
            task_release_id,
            "--receipt-only",
        ],
        [
            "task",
            "release",
            "--workspace",
            str(workspace),
            "--result",
            "completed",
            "--note",
            "original note",
            "--token-file",
            str(private_token_root / "wrong-path.token"),
            "--operation-id",
            task_release_id,
            "--receipt-only",
        ],
    ]
    for invalid in invalid_commands:
        assert run(["--state-dir", str(state), *invalid]) in {2, 5}
        payload = read_output(capsys)
        assert payload["ok"] is False
        assert payload["code"] in {"usage-error", "workspace-state-invalid"}
        assert _database_snapshot(state) == before


def test_cli_task_flow_uses_private_token_file_and_json_contract(
    tmp_path: Path, private_token_root: Path, capsys
) -> None:
    state = tmp_path / "state"
    workspace = tmp_path / "workspace"
    token_file = private_token_root / "tokens" / "task.token"
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
    assert claim["result"]["timed_out"] is False
    assert claim["result"]["priority"] == "normal"

    heartbeat_id = str(uuid.uuid4())
    assert (
        run(
            [
                "--state-dir",
                str(state),
                "task",
                "heartbeat",
                "--workspace",
                str(workspace),
                "--token-file",
                str(token_file),
                "--operation-id",
                heartbeat_id,
                "--note",
                "before release",
            ]
        )
        == 0
    )
    heartbeat = read_output(capsys)
    assert heartbeat["result"]["operation"]["operation_id"] == heartbeat_id

    assert (
        run(
            [
                "--state-dir",
                str(state),
                "queue",
                "cancel",
                "--workspace",
                str(workspace),
                "--claim-id",
                str(claim["result"]["id"]),
                "--token-file",
                str(token_file),
            ]
        )
        == 5
    )
    rejected_cancel = read_output(capsys)
    assert rejected_cancel["code"] == "workspace-state-invalid"
    assert rejected_cancel["details"]["state"] == "active"

    assert (
        run(
            [
                "--state-dir",
                str(state),
                "workspace",
                "status",
                "--workspace",
                str(workspace),
            ]
        )
        == 0
    )
    status = read_output(capsys)
    active_claim = next(
        item for item in status["result"]["claims"] if item["id"] == claim["result"]["id"]
    )
    assert active_claim["state"] == "active"

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
    assert released["result"]["token_cleanup_pending"] is True
    assert token_file.exists()
    operation = released["result"]["operation"]
    assert (
        run(
            [
                "--state-dir",
                str(state),
                "receipt",
                "ack",
                "--operation-id",
                str(operation["operation_id"]),
                "--fingerprint",
                str(operation["fingerprint"]),
                "--delivery-digest",
                str(operation["delivery_digest"]),
            ]
        )
        == 0
    )
    acknowledged = read_output(capsys)
    assert acknowledged["result"]["token_file_removed"] is True
    assert not token_file.exists()

    assert (
        run(
            [
                "--state-dir",
                str(state),
                "task",
                "heartbeat",
                "--workspace",
                str(workspace),
                "--token-file",
                str(token_file),
                "--operation-id",
                heartbeat_id,
                "--note",
                "before release",
            ]
        )
        == 2
    )
    missing_without_receipt_only = read_output(capsys)
    assert missing_without_receipt_only["code"] == "usage-error"

    assert (
        run(
            [
                "--state-dir",
                str(state),
                "task",
                "heartbeat",
                "--workspace",
                str(workspace),
                "--token-file",
                str(token_file),
                "--operation-id",
                heartbeat_id,
                "--note",
                "before release",
                "--receipt-only",
            ]
        )
        == 0
    )
    replayed_heartbeat = read_output(capsys)
    assert replayed_heartbeat["result"]["aborted"] is True
    assert replayed_heartbeat["result"]["reason"] == "task-released"
    assert replayed_heartbeat["result"]["terminal_state"] == "completed"
    assert replayed_heartbeat["result"]["operation"]["replayed"] is True

    wrong_heartbeat_id = str(uuid.uuid4())
    assert (
        run(
            [
                "--state-dir",
                str(state),
                "task",
                "heartbeat",
                "--workspace",
                str(workspace),
                "--token-file",
                str(token_file),
                "--operation-id",
                wrong_heartbeat_id,
                "--note",
                "before release",
                "--receipt-only",
            ]
        )
        == 5
    )
    missing_heartbeat = read_output(capsys)
    assert missing_heartbeat["details"]["reason"] == "operation-receipt-missing"

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
                "--operation-id",
                str(operation["operation_id"]),
                "--receipt-only",
            ]
        )
        == 0
    )
    replayed_release = read_output(capsys)
    assert replayed_release["result"]["id"] == released["result"]["id"]
    assert replayed_release["result"]["operation"]["replayed"] is True
    assert replayed_release["result"]["operation"]["delivered"] is True

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
                "failed",
                "--token-file",
                str(token_file),
                "--operation-id",
                str(operation["operation_id"]),
                "--receipt-only",
            ]
        )
        == 2
    )
    conflicting_release = read_output(capsys)
    assert conflicting_release["details"]["reason"] == "operation-id-conflict"


@pytest.mark.parametrize(
    "action",
    ["heartbeat", "claim", "freeze", "park"],
)
def test_cli_receipt_only_replays_all_terminal_lifecycle_actions(
    tmp_path: Path,
    private_token_root: Path,
    capsys,
    action: str,
) -> None:
    state = tmp_path / f"{action}-state"
    workspace = tmp_path / f"{action}-workspace"
    token_file = private_token_root / f"{action}-owner.token"
    workspace.mkdir()
    assert (
        run(
            [
                "--state-dir",
                str(state),
                "workspace",
                "register",
                "--workspace",
                str(workspace),
            ]
        )
        == 0
    )
    read_output(capsys)
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
                f"{action}-owner",
                "--summary",
                f"{action} replay",
                "--token-file",
                str(token_file),
            ]
        )
        == 0
    )
    read_output(capsys)
    mutation_id = str(uuid.uuid4())
    blocker_token_file: Path | None = None
    if action == "heartbeat":
        mutation_arguments = [
            "task",
            "heartbeat",
            "--note",
            "exact replay",
        ]
    elif action == "claim":
        mutation_arguments = ["claim", "acquire", "--resource", "unity-live"]
    elif action == "freeze":
        mutation_arguments = ["freeze", "acquire"]
    else:
        blocker_token_file = private_token_root / "park-blocker.token"
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
                    "park-blocker",
                    "--summary",
                    "park blocker",
                    "--token-file",
                    str(blocker_token_file),
                ]
            )
            == 0
        )
        read_output(capsys)
        assert (
            run(
                [
                    "--state-dir",
                    str(state),
                    "claim",
                    "acquire",
                    "--workspace",
                    str(workspace),
                    "--write",
                    "owned.txt",
                    "--token-file",
                    str(token_file),
                ]
            )
            == 0
        )
        read_output(capsys)
        assert (
            run(
                [
                    "--state-dir",
                    str(state),
                    "freeze",
                    "acquire",
                    "--workspace",
                    str(workspace),
                    "--token-file",
                    str(blocker_token_file),
                    "--keep-queued",
                ]
            )
            == 0
        )
        read_output(capsys)
        mutation_arguments = ["task", "park"]
    common_arguments = [
        "--workspace",
        str(workspace),
        "--token-file",
        str(token_file),
        "--operation-id",
        mutation_id,
    ]
    mutation_command = ["--state-dir", str(state), *mutation_arguments, *common_arguments]
    assert run(mutation_command) == 0
    mutation = read_output(capsys)["result"]
    release_id = str(uuid.uuid4())
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
                "--operation-id",
                release_id,
            ]
        )
        == 0
    )
    release = read_output(capsys)["result"]["operation"]
    assert (
        run(
            [
                "--state-dir",
                str(state),
                "receipt",
                "ack",
                "--operation-id",
                str(release["operation_id"]),
                "--fingerprint",
                str(release["fingerprint"]),
                "--delivery-digest",
                str(release["delivery_digest"]),
            ]
        )
        == 0
    )
    read_output(capsys)
    assert not token_file.exists()

    assert (
        run(
            [
                "--state-dir",
                str(state),
                *mutation_arguments,
                *common_arguments,
                "--receipt-only",
            ]
        )
        == 0
    )
    replay = read_output(capsys)["result"]
    assert replay["reason"] == "task-released"
    assert replay["aborted"] is True
    assert replay["operation"]["replayed"] is True
    assert replay["operation"]["finalized"] is True
    assert mutation["operation"]["operation_id"] == mutation_id
    if blocker_token_file is not None:
        blocker_release = str(uuid.uuid4())
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
                    str(blocker_token_file),
                    "--operation-id",
                    blocker_release,
                ]
            )
            == 0
        )
        blocker_result = read_output(capsys)["result"]["operation"]
        assert (
            run(
                [
                    "--state-dir",
                    str(state),
                    "receipt",
                    "ack",
                    "--operation-id",
                    str(blocker_result["operation_id"]),
                    "--fingerprint",
                    str(blocker_result["fingerprint"]),
                    "--delivery-digest",
                    str(blocker_result["delivery_digest"]),
                ]
            )
            == 0
        )
        read_output(capsys)


def test_claimless_expiry_removes_token_and_returns_terminal_start_proof(
    tmp_path: Path,
    private_token_root: Path,
    capsys,
) -> None:
    state = tmp_path / "claimless-state"
    workspace = tmp_path / "claimless-workspace"
    token_file = private_token_root / "claimless" / "task.token"
    workspace.mkdir()
    token_file.parent.mkdir()
    register_id = str(uuid.uuid4())
    start_id = str(uuid.uuid4())

    assert (
        run(
            [
                "--state-dir",
                str(state),
                "workspace",
                "register",
                "--workspace",
                str(workspace),
                "--operation-id",
                register_id,
            ]
        )
        == 0
    )
    read_output(capsys)
    start_arguments = [
        "--state-dir",
        str(state),
        "task",
        "start",
        "--workspace",
        str(workspace),
        "--owner",
        "claimless-owner",
        "--summary",
        "claimless expiry recovery",
        "--token-file",
        str(token_file),
        "--ttl",
        "60",
        "--operation-id",
        start_id,
    ]
    assert run(start_arguments) == 0
    started = read_output(capsys)["result"]
    task_id = str(started["id"])
    fingerprint = str(started["operation"]["fingerprint"])
    assert token_file.is_file()

    paths = resolve_state_paths(state)
    with open_database(paths) as connection:
        connection.execute("UPDATE tasks SET expires_at = 0 WHERE id = ?", (task_id,))
        connection.commit()

    assert (
        run(
            [
                "--state-dir",
                str(state),
                "workspace",
                "status",
                "--workspace",
                str(workspace),
            ]
        )
        == 0
    )
    status_payload = read_output(capsys)
    assert status_payload["ok"] is True
    assert not token_file.exists()
    assert [
        job for job in status_payload["result"]["token_cleanup_jobs"] if job["completed_at"] is None
    ] == []
    with open_database(paths) as connection:
        assert (
            connection.execute(
                "SELECT COUNT(*) FROM token_cleanup_jobs WHERE task_id = ?",
                (task_id,),
            ).fetchone()[0]
            == 0
        )
        retired_after_status = connection.execute(
            "SELECT terminal_json, retired_at, delivered_at FROM operation_receipts "
            "WHERE operation_id = ?",
            (start_id,),
        ).fetchone()
    assert retired_after_status is not None
    assert retired_after_status["terminal_json"] is not None
    assert retired_after_status["retired_at"] is not None
    assert retired_after_status["delivered_at"] is None

    assert run(["--state-dir", str(state), "workspace", "list"]) == 0
    read_output(capsys)
    assert not token_file.exists()
    with open_database(paths) as connection:
        retired = connection.execute(
            "SELECT terminal_json, retired_at, delivered_at FROM operation_receipts "
            "WHERE operation_id = ?",
            (start_id,),
        ).fetchone()
        assert (
            connection.execute(
                "SELECT COUNT(*) FROM token_cleanup_jobs WHERE task_id = ?",
                (task_id,),
            ).fetchone()[0]
            == 0
        )
    assert retired["terminal_json"] is not None
    assert retired["retired_at"] is not None
    assert retired["delivered_at"] is None

    assert run([*start_arguments, "--receipt-only"]) == 0
    terminal = read_output(capsys)["result"]
    assert terminal["id"] == task_id
    assert terminal["state"] == "active"
    assert terminal["expires_at"] == started["expires_at"]
    assert terminal["token_file"] == str(token_file)
    assert terminal["aborted"] is True
    assert terminal["reason"] == "task-ttl-expired"
    assert terminal["terminal_state"] == "expired"
    assert terminal["terminal_result"] == "expired"
    assert isinstance(terminal["terminal_finished_at"], (int, float))
    assert terminal["token_cleanup_completed"] is True
    assert terminal["operation"] == {
        "operation_id": start_id,
        "fingerprint": fingerprint,
        "delivery_digest": terminal["operation"]["delivery_digest"],
        "replayed": True,
        "delivered": False,
        "finalized": True,
        "retired": True,
    }

    assert (
        run(
            [
                "--state-dir",
                str(state),
                "receipt",
                "ack",
                "--operation-id",
                start_id,
                "--fingerprint",
                fingerprint,
                "--delivery-digest",
                str(terminal["operation"]["delivery_digest"]),
            ]
        )
        == 0
    )
    acknowledged = read_output(capsys)["result"]
    assert set(acknowledged) == {
        "action",
        "acknowledged",
        "token_cleanup_expected",
        "token_file_removed",
        "operation",
    }
    with open_database(paths) as connection:
        assert (
            connection.execute(
                "SELECT COUNT(*) FROM token_cleanup_jobs WHERE task_id = ?",
                (task_id,),
            ).fetchone()[0]
            == 0
        )

    replacement_id = str(uuid.uuid4())
    replacement_arguments = list(start_arguments)
    replacement_arguments[replacement_arguments.index(start_id)] = replacement_id
    assert run(replacement_arguments) == 0
    replacement = read_output(capsys)["result"]
    assert replacement["id"] != task_id
    assert token_file.is_file()


def test_cli_same_call_cleanup_fails_closed_and_retries_idempotently(
    tmp_path: Path,
    private_token_root: Path,
    capsys,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    state = tmp_path / "state"
    workspace = tmp_path / "workspace"
    token_file = private_token_root / "same-call" / "task.token"
    workspace.mkdir()
    assert (
        run(["--state-dir", str(state), "workspace", "register", "--workspace", str(workspace)])
        == 0
    )
    read_output(capsys)
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
                "same-call",
                "--summary",
                "same-call cleanup",
                "--token-file",
                str(token_file),
                "--ttl",
                "60",
            ]
        )
        == 0
    )
    read_output(capsys)
    paths = resolve_state_paths(state)
    with open_database(paths) as connection:
        task_id = str(connection.execute("SELECT id FROM tasks").fetchone()[0])
        connection.execute("UPDATE tasks SET expires_at = 0 WHERE id = ?", (task_id,))
        connection.commit()

    original_removal = coordinator_module.remove_matching_token_hash_file
    monkeypatch.setattr(
        coordinator_module,
        "remove_matching_token_hash_file",
        lambda *_args: False,
    )
    exit_code = run(
        ["--state-dir", str(state), "workspace", "status", "--workspace", str(workspace)]
    )
    failed = read_output(capsys)
    assert exit_code == 5
    assert failed["details"]["reason"] == "token-cleanup-pending"
    assert failed["details"]["cleanup"]["failed"] == 1
    assert token_file.exists()
    with open_database(paths) as connection:
        pending = connection.execute(
            "SELECT completed_at FROM token_cleanup_jobs WHERE task_id = ?", (task_id,)
        ).fetchone()
    assert pending is not None and pending["completed_at"] is None

    second_exit_code = run(
        ["--state-dir", str(state), "workspace", "status", "--workspace", str(workspace)]
    )
    second_failed = read_output(capsys)
    assert second_exit_code == 5
    assert second_failed["details"]["reason"] == "token-cleanup-pending"
    assert second_failed["details"]["cleanup"]["failed"] == 1
    with open_database(paths) as connection:
        retried = connection.execute(
            "SELECT attempt_count FROM token_cleanup_jobs WHERE task_id = ?", (task_id,)
        ).fetchone()
    assert retried is not None and retried["attempt_count"] == 2

    monkeypatch.setattr(coordinator_module, "remove_matching_token_hash_file", original_removal)
    assert (
        run(["--state-dir", str(state), "workspace", "status", "--workspace", str(workspace)]) == 0
    )
    read_output(capsys)
    assert not token_file.exists()
    with open_database(paths) as connection:
        assert (
            connection.execute(
                "SELECT COUNT(*) FROM token_cleanup_jobs WHERE task_id = ?", (task_id,)
            ).fetchone()[0]
            == 0
        )

    assert (
        run(["--state-dir", str(state), "workspace", "status", "--workspace", str(workspace)]) == 0
    )
    read_output(capsys)


@pytest.mark.parametrize("task_count", [9, 17, 25])
def test_cli_status_caps_same_maintenance_cleanup_at_eight_jobs(
    tmp_path: Path,
    private_token_root: Path,
    capsys,
    task_count: int,
) -> None:
    state = tmp_path / "state"
    workspace = tmp_path / "workspace"
    workspace.mkdir()
    assert (
        run(["--state-dir", str(state), "workspace", "register", "--workspace", str(workspace)])
        == 0
    )
    read_output(capsys)
    token_files: list[Path] = []
    for index in range(task_count):
        token_file = private_token_root / "bounded" / f"{index}.token"
        token_file.parent.mkdir(exist_ok=True)
        token_files.append(token_file)
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
                    f"bounded-{index}",
                    "--summary",
                    "bounded cleanup",
                    "--token-file",
                    str(token_file),
                    "--ttl",
                    "60",
                ]
            )
            == 0
        )
        read_output(capsys)
    paths = resolve_state_paths(state)
    with open_database(paths) as connection:
        connection.execute("UPDATE tasks SET expires_at = 0")
        connection.commit()

    remaining = task_count
    while remaining > 0:
        exit_code = run(
            ["--state-dir", str(state), "workspace", "status", "--workspace", str(workspace)]
        )
        payload = read_output(capsys)
        processed = min(8, remaining)
        remaining -= processed
        if remaining:
            assert exit_code == 5
            assert payload["details"]["reason"] == "token-cleanup-pending"
            assert payload["details"]["cleanup"]["processed"] == processed
            assert payload["details"]["pending_token_cleanup_jobs"] == remaining
            assert len(payload["details"]["task_ids"]) <= 8
        else:
            assert exit_code == 0
            assert [
                job
                for job in payload["result"]["token_cleanup_jobs"]
                if job["completed_at"] is None
            ] == []
    assert not any(token_file.exists() for token_file in token_files)


def test_cli_cleanup_failure_does_not_mask_committed_mutation_replay(
    tmp_path: Path,
    private_token_root: Path,
    capsys,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    state = tmp_path / "state"
    workspace = tmp_path / "workspace"
    token_file = private_token_root / "mutation-replay" / "task.token"
    workspace.mkdir()
    assert (
        run(["--state-dir", str(state), "workspace", "register", "--workspace", str(workspace)])
        == 0
    )
    read_output(capsys)
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
                "mutation-replay",
                "--summary",
                "cleanup failure does not mask mutation",
                "--token-file",
                str(token_file),
                "--ttl",
                "60",
            ]
        )
        == 0
    )
    read_output(capsys)
    paths = resolve_state_paths(state)
    with open_database(paths) as connection:
        connection.execute("UPDATE tasks SET expires_at = 0")
        connection.commit()

    monkeypatch.setattr(
        coordinator_module,
        "remove_matching_token_hash_file",
        lambda *_args: False,
    )
    operation_id = str(uuid.uuid4())
    register_args = [
        "--state-dir",
        str(state),
        "workspace",
        "register",
        "--workspace",
        str(workspace),
        "--operation-id",
        operation_id,
    ]
    assert run(register_args) == 0
    committed = read_output(capsys)["result"]
    assert committed["operation"]["replayed"] is False

    assert run([*register_args, "--receipt-only"]) == 0
    replayed = read_output(capsys)["result"]
    assert replayed["operation"]["replayed"] is True
    with open_database(paths) as connection:
        assert (
            connection.execute(
                "SELECT COUNT(*) FROM operation_receipts WHERE operation_id = ?",
                (operation_id,),
            ).fetchone()[0]
            == 1
        )


def test_task_start_receipt_only_does_not_advance_pending_cleanup(
    tmp_path: Path,
    private_token_root: Path,
    capsys,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    state = tmp_path / "probe-state"
    workspace = tmp_path / "probe-workspace"
    token_file = private_token_root / "probe" / "task.token"
    workspace.mkdir()
    token_file.parent.mkdir()
    assert (
        run(
            [
                "--state-dir",
                str(state),
                "workspace",
                "register",
                "--workspace",
                str(workspace),
            ]
        )
        == 0
    )
    read_output(capsys)
    start_id = str(uuid.uuid4())
    start_arguments = [
        "--state-dir",
        str(state),
        "task",
        "start",
        "--workspace",
        str(workspace),
        "--owner",
        "probe-owner",
        "--summary",
        "read-only receipt probe",
        "--token-file",
        str(token_file),
        "--ttl",
        "60",
        "--operation-id",
        start_id,
    ]
    assert run(start_arguments) == 0
    task_id = str(read_output(capsys)["result"]["id"])
    original_token = token_file.read_bytes()
    paths = resolve_state_paths(state)
    with open_database(paths) as connection:
        connection.execute("UPDATE tasks SET expires_at = 0 WHERE id = ?", (task_id,))
        connection.commit()
    original_removal = coordinator_module.remove_matching_token_hash_file
    monkeypatch.setattr(
        coordinator_module,
        "remove_matching_token_hash_file",
        lambda *_args: False,
    )
    status_exit = run(
        [
            "--state-dir",
            str(state),
            "workspace",
            "status",
            "--workspace",
            str(workspace),
        ]
    )
    failed_status = read_output(capsys)
    assert status_exit == 5
    assert failed_status["details"]["reason"] == "token-cleanup-pending"
    assert failed_status["details"]["cleanup"]["failed"] == 1
    assert failed_status["details"]["pending_token_cleanup_jobs"] == 1
    assert token_file.read_bytes() == original_token
    with open_database(paths) as connection:
        before = tuple(
            connection.execute(
                "SELECT completed_at, last_attempt_at, attempt_count "
                "FROM token_cleanup_jobs WHERE task_id = ?",
                (task_id,),
            ).fetchone()
        )

    assert run([*start_arguments, "--receipt-only"]) == 0
    pending = read_output(capsys)["result"]
    assert pending["id"] == task_id
    assert pending["aborted"] is True
    assert pending["reason"] == "task-ttl-expired"
    assert pending["terminal_state"] == "expired"
    assert pending["terminal_result"] == "expired"
    assert "token_cleanup_completed" not in pending
    assert pending["operation"]["replayed"] is True
    assert pending["operation"]["retired"] is True
    assert token_file.read_bytes() == original_token
    with open_database(paths) as connection:
        after = tuple(
            connection.execute(
                "SELECT completed_at, last_attempt_at, attempt_count "
                "FROM token_cleanup_jobs WHERE task_id = ?",
                (task_id,),
            ).fetchone()
        )
    assert after == before

    monkeypatch.setattr(coordinator_module, "remove_matching_token_hash_file", original_removal)
    assert (
        run(
            [
                "--state-dir",
                str(state),
                "workspace",
                "status",
                "--workspace",
                str(workspace),
            ]
        )
        == 0
    )
    recovered_status = read_output(capsys)["result"]
    assert [
        job for job in recovered_status["token_cleanup_jobs"] if job["completed_at"] is None
    ] == []
    assert not token_file.exists()
    with open_database(paths) as connection:
        assert (
            connection.execute(
                "SELECT COUNT(*) FROM token_cleanup_jobs WHERE task_id = ?", (task_id,)
            ).fetchone()[0]
            == 0
        )


def test_terminal_start_replays_after_its_retained_task_row_is_pruned(
    tmp_path: Path,
    private_token_root: Path,
    capsys,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    state = tmp_path / "pruned-task-state"
    workspace = tmp_path / "pruned-task-workspace"
    token_file = private_token_root / "pruned-task" / "task.token"
    workspace.mkdir()
    token_file.parent.mkdir()
    start_id = str(uuid.uuid4())
    release_id = str(uuid.uuid4())
    start_arguments = [
        "--state-dir",
        str(state),
        "task",
        "start",
        "--workspace",
        str(workspace),
        "--owner",
        "pruned-owner",
        "--summary",
        "retained start receipt",
        "--token-file",
        str(token_file),
        "--ttl",
        "60",
        "--operation-id",
        start_id,
    ]
    assert (
        run(
            [
                "--state-dir",
                str(state),
                "workspace",
                "register",
                "--workspace",
                str(workspace),
            ]
        )
        == 0
    )
    read_output(capsys)
    assert run(start_arguments) == 0
    started = read_output(capsys)["result"]
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
                "--operation-id",
                release_id,
            ]
        )
        == 0
    )
    released = read_output(capsys)["result"]
    assert (
        run(
            [
                "--state-dir",
                str(state),
                "receipt",
                "ack",
                "--operation-id",
                release_id,
                "--fingerprint",
                str(released["operation"]["fingerprint"]),
                "--delivery-digest",
                str(released["operation"]["delivery_digest"]),
            ]
        )
        == 0
    )
    read_output(capsys)
    assert not token_file.exists()

    monkeypatch.setattr(coordinator_module, "TERMINAL_TASK_RETENTION", 0)
    assert (
        run(
            [
                "--state-dir",
                str(state),
                "workspace",
                "status",
                "--workspace",
                str(workspace),
            ]
        )
        == 0
    )
    read_output(capsys)
    with open_database(resolve_state_paths(state)) as connection:
        assert (
            connection.execute(
                "SELECT COUNT(*) FROM tasks WHERE id = ?", (started["id"],)
            ).fetchone()[0]
            == 0
        )

    assert run(start_arguments) == 0
    replay = read_output(capsys)["result"]
    assert replay["id"] == started["id"]
    assert replay["reason"] == "task-released"
    assert replay["token_cleanup_completed"] is True
    assert replay["operation"]["replayed"] is True


def test_same_id_normal_start_advances_expiry_without_prior_maintenance(
    tmp_path: Path,
    private_token_root: Path,
    capsys,
) -> None:
    state = tmp_path / "direct-retry-state"
    workspace = tmp_path / "direct-retry-workspace"
    token_file = private_token_root / "direct-retry" / "task.token"
    workspace.mkdir()
    token_file.parent.mkdir()
    assert (
        run(
            [
                "--state-dir",
                str(state),
                "workspace",
                "register",
                "--workspace",
                str(workspace),
            ]
        )
        == 0
    )
    read_output(capsys)
    start_id = str(uuid.uuid4())
    start_arguments = [
        "--state-dir",
        str(state),
        "task",
        "start",
        "--workspace",
        str(workspace),
        "--owner",
        "direct-retry-owner",
        "--summary",
        "same operation retry",
        "--token-file",
        str(token_file),
        "--ttl",
        "60",
        "--operation-id",
        start_id,
    ]
    assert run(start_arguments) == 0
    task_id = str(read_output(capsys)["result"]["id"])
    paths = resolve_state_paths(state)
    with open_database(paths) as connection:
        connection.execute("UPDATE tasks SET expires_at = 0 WHERE id = ?", (task_id,))
        connection.commit()

    assert run(start_arguments) == 0
    terminal = read_output(capsys)["result"]
    assert terminal["id"] == task_id
    assert terminal["aborted"] is True
    assert terminal["operation"]["retired"] is True
    assert not token_file.exists()
    with open_database(paths) as connection:
        assert connection.execute("SELECT COUNT(*) FROM token_cleanup_jobs").fetchone()[0] == 0


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


def test_cli_release_fails_closed_when_matching_token_is_not_removed(
    tmp_path: Path, private_token_root: Path, capsys, monkeypatch: pytest.MonkeyPatch
) -> None:
    state = tmp_path / "state"
    workspace = tmp_path / "workspace"
    token_file = private_token_root / "tokens" / "task.token"
    workspace.mkdir()
    assert (
        run(["--state-dir", str(state), "workspace", "register", "--workspace", str(workspace)])
        == 0
    )
    read_output(capsys)
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
                "release cleanup",
                "--token-file",
                str(token_file),
            ]
        )
        == 0
    )
    read_output(capsys)
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
    operation = released["result"]["operation"]
    monkeypatch.setattr(
        "unity_workspace_scheduler.coordinator.remove_matching_token_hash_file",
        lambda *_args: False,
    )

    exit_code = run(
        [
            "--state-dir",
            str(state),
            "receipt",
            "ack",
            "--operation-id",
            str(operation["operation_id"]),
            "--fingerprint",
            str(operation["fingerprint"]),
            "--delivery-digest",
            str(operation["delivery_digest"]),
        ]
    )

    payload = read_output(capsys)
    assert exit_code == 5
    assert payload["code"] == "workspace-state-invalid"
    assert payload["details"]["token_file_removed"] is False
    assert payload["details"]["recovery_required"] is True
    assert payload["details"]["reason"] == "receipt-token-cleanup-failed"
    assert payload["details"]["cause_reason"] == "token-cleanup-identity-mismatch"
    assert token_file.exists()


def test_cli_release_maps_token_removal_error_to_terminal_state_recovery(
    tmp_path: Path, private_token_root: Path, capsys, monkeypatch: pytest.MonkeyPatch
) -> None:
    state = tmp_path / "state"
    workspace = tmp_path / "workspace"
    token_file = private_token_root / "tokens" / "task.token"
    workspace.mkdir()
    assert (
        run(
            [
                "--state-dir",
                str(state),
                "workspace",
                "register",
                "--workspace",
                str(workspace),
            ]
        )
        == 0
    )
    read_output(capsys)
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
                "release cleanup error",
                "--token-file",
                str(token_file),
            ]
        )
        == 0
    )
    started = read_output(capsys)
    task_id = started["result"]["id"]
    token = token_file.read_text(encoding="utf-8").strip()

    original_removal = coordinator_module.remove_matching_token_hash_file

    def fail_after_removal(path: Path, expected_hash: str) -> bool:
        assert original_removal(path, expected_hash) is True
        raise UsageError(f"injected removal failure: {token_file} {token}")

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
    operation = released["result"]["operation"]
    monkeypatch.setattr(
        "unity_workspace_scheduler.coordinator.remove_matching_token_hash_file",
        fail_after_removal,
    )
    exit_code = run(
        [
            "--state-dir",
            str(state),
            "receipt",
            "ack",
            "--operation-id",
            str(operation["operation_id"]),
            "--fingerprint",
            str(operation["fingerprint"]),
            "--delivery-digest",
            str(operation["delivery_digest"]),
        ]
    )

    payload = read_output(capsys)
    assert exit_code == 5
    assert payload["code"] == "workspace-state-invalid"
    assert payload["details"]["task_id"] == task_id
    assert payload["details"]["token_file_removed"] is False
    assert payload["details"]["recovery_required"] is True
    assert payload["details"]["reason"] == "receipt-token-cleanup-failed"
    serialized = json.dumps(payload)
    assert token not in serialized
    assert str(token_file) not in serialized
    assert not token_file.exists()

    before_replay = _database_snapshot(state)
    wrong_path_exit = run(
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
            str(private_token_root / "wrong-path.token"),
            "--operation-id",
            str(operation["operation_id"]),
            "--receipt-only",
        ]
    )
    wrong_path = read_output(capsys)
    assert wrong_path_exit == 2
    assert wrong_path["details"]["reason"] == "operation-id-conflict"

    wrong_action_exit = run(
        [
            "--state-dir",
            str(state),
            "task",
            "heartbeat",
            "--workspace",
            str(workspace),
            "--token-file",
            str(token_file),
            "--operation-id",
            str(operation["operation_id"]),
            "--receipt-only",
        ]
    )
    wrong_action = read_output(capsys)
    assert wrong_action_exit == 2
    assert wrong_action["details"]["reason"] == "operation-id-conflict"
    assert _database_snapshot(state) == before_replay

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
                "--operation-id",
                str(operation["operation_id"]),
                "--receipt-only",
            ]
        )
        == 0
    )
    replay = read_output(capsys)
    assert replay["result"]["id"] == task_id
    assert replay["result"]["operation"]["replayed"] is True
    assert replay["result"]["operation"]["delivered"] is True
    assert replay["result"]["token_cleanup_pending"] is True
    assert _database_snapshot(state) == before_replay

    monkeypatch.setattr(coordinator_module, "remove_matching_token_hash_file", original_removal)
    assert (
        run(
            [
                "--state-dir",
                str(state),
                "receipt",
                "ack",
                "--operation-id",
                str(operation["operation_id"]),
                "--fingerprint",
                str(operation["fingerprint"]),
                "--delivery-digest",
                str(operation["delivery_digest"]),
            ]
        )
        == 0
    )
    recovered_ack = read_output(capsys)
    assert recovered_ack["result"]["token_file_removed"] is True
    assert recovered_ack["result"]["operation"]["replayed"] is True

    with open_database(resolve_state_paths(state)) as connection:
        terminal = connection.execute("SELECT state FROM tasks WHERE id = ?", (task_id,)).fetchone()
    assert terminal is not None
    assert terminal["state"] == "completed"


def test_cli_rejects_abbreviated_workspace_override(tmp_path: Path, capsys) -> None:
    workspace = tmp_path / "workspace"
    workspace.mkdir()
    exit_code = run(
        [
            "--state-dir",
            str(tmp_path / "state"),
            "workspace",
            "register",
            "--worksp",
            str(workspace),
        ]
    )
    payload = read_output(capsys)
    assert exit_code == 2
    assert payload["ok"] is False
    assert payload["code"] == "usage-error"


def test_cli_task_park_drains_and_automatically_restores_claims(
    tmp_path: Path, private_token_root: Path, capsys
) -> None:
    state = tmp_path / "state"
    workspace = tmp_path / "workspace"
    owner_token = private_token_root / "owner.token"
    freeze_token = private_token_root / "freeze.token"
    workspace.mkdir()

    def invoke(*arguments: str) -> dict[str, object]:
        assert run(["--state-dir", str(state), *arguments]) == 0
        return read_output(capsys)

    invoke("workspace", "register", "--workspace", str(workspace))
    for token_file, owner in ((owner_token, "owner"), (freeze_token, "maintenance")):
        invoke(
            "task",
            "start",
            "--workspace",
            str(workspace),
            "--owner",
            owner,
            "--summary",
            owner,
            "--token-file",
            str(token_file),
        )
    owned = invoke(
        "claim",
        "acquire",
        "--workspace",
        str(workspace),
        "--write",
        "Assets/Hero.prefab",
        "--token-file",
        str(owner_token),
    )["result"]
    freeze = invoke(
        "freeze",
        "acquire",
        "--workspace",
        str(workspace),
        "--token-file",
        str(freeze_token),
    )["result"]
    parked = invoke(
        "task",
        "park",
        "--workspace",
        str(workspace),
        "--token-file",
        str(owner_token),
    )["result"]
    assert parked["states"] == {owned["id"]: "parked"}

    invoke(
        "claim",
        "release",
        "--workspace",
        str(workspace),
        "--claim-id",
        str(freeze["id"]),
        "--token-file",
        str(freeze_token),
    )
    status = invoke("workspace", "status", "--workspace", str(workspace))["result"]
    restored = next(claim for claim in status["claims"] if claim["id"] == owned["id"])
    assert restored["state"] == "active"


def test_cli_accepts_urgent_priority_only_on_freeze(
    tmp_path: Path, private_token_root: Path, capsys
) -> None:
    state = tmp_path / "state"
    workspace = tmp_path / "workspace"
    token_file = private_token_root / "urgent.token"
    workspace.mkdir()

    def invoke(*arguments: str) -> dict[str, object]:
        assert run(["--state-dir", str(state), *arguments]) == 0
        return read_output(capsys)

    invoke("workspace", "register", "--workspace", str(workspace))
    invoke(
        "task",
        "start",
        "--workspace",
        str(workspace),
        "--owner",
        "urgent",
        "--summary",
        "Urgent barrier",
        "--token-file",
        str(token_file),
    )
    freeze = invoke(
        "freeze",
        "acquire",
        "--workspace",
        str(workspace),
        "--priority",
        "urgent",
        "--token-file",
        str(token_file),
    )["result"]

    assert freeze["kind"] == "freeze"
    assert freeze["priority"] == "urgent"
    assert freeze["state"] == "active"

    rejected = run(
        [
            "--state-dir",
            str(state),
            "claim",
            "acquire",
            "--workspace",
            str(workspace),
            "--write",
            "Assets/Hero.prefab",
            "--priority",
            "urgent",
            "--token-file",
            str(token_file),
        ]
    )
    error = read_output(capsys)
    assert rejected == 2
    assert error["code"] == "usage-error"


def _subprocess_json(arguments: list[str], env: dict[str, str]) -> dict[str, object]:
    completed = subprocess.run(
        [sys.executable, "-m", "unity_workspace_scheduler", *_with_operation(arguments)],
        check=True,
        capture_output=True,
        text=True,
        env=env,
    )
    payload = json.loads(completed.stdout)
    assert payload["protocol_version"] == 3
    return payload


def test_two_cli_processes_serialize_conflicting_claims(
    tmp_path: Path, private_token_root: Path
) -> None:
    state = tmp_path / "state"
    workspace = tmp_path / "workspace"
    workspace.mkdir()
    env = os.environ.copy()
    env["UNITY_SCHEDULER_STATE_DIR"] = str(state)
    _subprocess_json(["workspace", "register", "--workspace", str(workspace)], env)
    token_files = [private_token_root / "one.token", private_token_root / "two.token"]
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
            _with_operation(
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
                ]
            ),
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


@pytest.mark.skipif(os.name != "nt", reason="Windows nested process ACL behavior only")
def test_candidate_executable_uses_read_only_acl_checks_in_nested_process(
    tmp_path: Path, private_token_root: Path
) -> None:
    candidate = Path(sys.executable).with_name("unity-scheduler.exe")
    assert candidate.is_file(), f"candidate executable is missing: {candidate}"
    state = tmp_path / "nested-state"
    workspace = tmp_path / "workspace"
    router_token_directory = private_token_root
    token_file = router_token_directory / f"nested-test-{uuid.uuid4().hex}.token"
    workspace.mkdir()
    driver_source = """
import json
import subprocess
import sys
import uuid

candidate, state, workspace, token_file, icacls = sys.argv[1:]
commands = [
    [candidate, "--state-dir", state, "workspace", "register", "--workspace", workspace,
     "--operation-id", str(uuid.uuid4())],
    [
        candidate,
        "--state-dir",
        state,
        "task",
        "start",
        "--workspace",
        workspace,
        "--owner",
        "nested-driver",
        "--summary",
        "nested Windows ACL canary",
        "--token-file",
        token_file,
        "--operation-id",
        str(uuid.uuid4()),
    ],
    [
        candidate,
        "--state-dir",
        state,
        "task",
        "heartbeat",
        "--workspace",
        workspace,
        "--token-file",
        token_file,
        "--operation-id",
        str(uuid.uuid4()),
    ],
    [
        candidate,
        "--state-dir",
        state,
        "task",
        "release",
        "--workspace",
        workspace,
        "--result",
        "completed",
        "--token-file",
        token_file,
        "--operation-id",
        str(uuid.uuid4()),
    ],
]
records = []
broad_matches = {}
for index, command in enumerate(commands):
    completed = subprocess.run(command, check=False, capture_output=True, text=True)
    records.append(
        {
            "returncode": completed.returncode,
            "stdout": completed.stdout,
            "stderr": completed.stderr,
        }
    )
    if completed.returncode != 0:
        break
    if index == 3:
        release = json.loads(completed.stdout)["result"]
        commands.append(
            [
                candidate,
                "--state-dir",
                state,
                "receipt",
                "ack",
                "--operation-id",
                release["operation"]["operation_id"],
                "--fingerprint",
                release["operation"]["fingerprint"],
                "--delivery-digest",
                release["operation"]["delivery_digest"],
            ]
        )
    if index == 1:
        for sid in ("S-1-1-0", "S-1-5-7", "S-1-5-11", "S-1-5-32-545", "S-1-5-32-546"):
            inspected = subprocess.run(
                [icacls, token_file, "/findsid", f"*{sid}"],
                check=False,
                capture_output=True,
                text=True,
            )
            broad_matches[sid] = {
                "returncode": inspected.returncode,
                "matched": token_file.casefold() in inspected.stdout.casefold(),
            }
print(json.dumps({"records": records, "broad_matches": broad_matches}))
raise SystemExit(0 if len(records) == len(commands) and all(r["returncode"] == 0 for r in records) else 1)
"""

    driver = subprocess.run(
        [
            sys.executable,
            "-c",
            driver_source,
            str(candidate),
            str(state),
            str(workspace),
            str(token_file),
            str(state_module._trusted_icacls_executable()),
        ],
        check=False,
        capture_output=True,
        text=True,
        timeout=60,
    )

    assert driver.returncode == 0, driver.stderr or driver.stdout
    evidence = json.loads(driver.stdout)
    payloads = [json.loads(record["stdout"]) for record in evidence["records"]]
    assert all(payload["protocol_version"] == 3 for payload in payloads)
    assert all(check["returncode"] == 0 for check in evidence["broad_matches"].values())
    assert not any(check["matched"] for check in evidence["broad_matches"].values())
    assert payloads[-1]["result"]["token_file_removed"] is True
    assert not token_file.exists()
