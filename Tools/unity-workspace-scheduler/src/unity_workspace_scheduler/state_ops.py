"""Offline, WAL-consistent scheduler state backup and restore operations."""

from __future__ import annotations

import hashlib
import json
import math
import os
import re
import shutil
import sqlite3
import stat
import tempfile
import time
import uuid
from dataclasses import dataclass
from pathlib import Path, PurePosixPath
from typing import Any

from .coordinator import (
    REPLAY_REQUIRED_OPERATION_LIMIT,
    TOKEN_CLEANUP_BACKLOG_LIMIT,
    _path_conflicts,
    _workspace_id,
)
from .errors import StateError, UsageError
from .operations import (
    CLAIM_RELEASE_STATES,
    LIFECYCLE_ACTIONS,
    LIFECYCLE_REVOCATION_ACTIONS,
    LIFECYCLE_TERMINAL_ACTIONS,
    PUBLIC_MUTATION_ACTIONS,
    QUEUE_CANCEL_STATES,
    is_sha256_hex,
    operation_fingerprint,
    parse_canonical_json,
    validate_operation_id,
)
from .state import (
    LEGACY_SCHEMA_TWO_VERSION,
    MAX_TASK_TTL_SECONDS,
    SCHEMA_VERSION,
    TERMINAL_CLAIM_RETENTION,
    StatePaths,
    _canonical_schema_version,
    _durable_directory_barrier,
    _ensure_private_directory,
    _has_control_characters,
    _is_normalized_recovery_evidence,
    _is_windows_reparse_point,
    _platform_case_identity,
    _verify_windows_maintenance_acl,
)

_REQUIRED_COLUMNS = {
    "scheduler_meta": {"key", "value"},
    "workspaces": {"id", "root", "registered_at", "epoch"},
    "tasks": {
        "id",
        "workspace_id",
        "owner",
        "summary",
        "token_hash",
        "state",
        "created_at",
        "heartbeat_at",
        "expires_at",
        "finished_at",
        "result",
        "note",
    },
    "claims": {
        "id",
        "workspace_id",
        "task_id",
        "kind",
        "state",
        "queue_order",
        "created_at",
        "granted_at",
        "released_at",
    },
    "claim_scopes": {"claim_id", "scope_type", "value"},
    "recovery_events": {
        "id",
        "workspace_id",
        "task_id",
        "resolution",
        "evidence",
        "created_at",
    },
}
_RECEIPT_REQUIRED_COLUMNS = {
    "operation_id",
    "workspace_id",
    "action",
    "parameters_json",
    "owner_token_hash",
    "fingerprint",
    "task_id",
    "result_json",
    "terminal_json",
    "token_cleanup_path",
    "token_cleanup_identity",
    "created_at",
    "finalized_at",
    "delivered_at",
    "retired_at",
}
_TOKEN_CLEANUP_JOB_REQUIRED_COLUMNS = {
    "task_id",
    "workspace_id",
    "token_file_path",
    "token_file_identity",
    "token_hash",
    "reason",
    "created_at",
    "completed_at",
    "last_attempt_at",
    "attempt_count",
}
_COMMON_COLUMN_SIGNATURES = {
    "scheduler_meta": (
        ("key", "TEXT", False, None, 1, 0),
        ("value", "TEXT", True, None, 0, 0),
    ),
    "tasks": (
        ("id", "TEXT", False, None, 1, 0),
        ("workspace_id", "TEXT", True, None, 0, 0),
        ("owner", "TEXT", True, None, 0, 0),
        ("summary", "TEXT", True, None, 0, 0),
        ("token_hash", "TEXT", True, None, 0, 0),
        ("state", "TEXT", True, None, 0, 0),
        ("created_at", "REAL", True, None, 0, 0),
        ("heartbeat_at", "REAL", True, None, 0, 0),
        ("expires_at", "REAL", True, None, 0, 0),
        ("finished_at", "REAL", False, None, 0, 0),
        ("result", "TEXT", False, None, 0, 0),
        ("note", "TEXT", False, None, 0, 0),
    ),
    "claims": (
        ("id", "TEXT", False, None, 1, 0),
        ("workspace_id", "TEXT", True, None, 0, 0),
        ("task_id", "TEXT", True, None, 0, 0),
        ("kind", "TEXT", True, None, 0, 0),
        ("state", "TEXT", True, None, 0, 0),
        ("queue_order", "INTEGER", True, None, 0, 0),
        ("created_at", "REAL", True, None, 0, 0),
        ("granted_at", "REAL", False, None, 0, 0),
        ("released_at", "REAL", False, None, 0, 0),
    ),
    "claim_scopes": (
        ("claim_id", "TEXT", True, None, 1, 0),
        ("scope_type", "TEXT", True, None, 2, 0),
        ("value", "TEXT", True, None, 3, 0),
    ),
    "recovery_events": (
        ("id", "TEXT", False, None, 1, 0),
        ("workspace_id", "TEXT", True, None, 0, 0),
        ("task_id", "TEXT", True, None, 0, 0),
        ("resolution", "TEXT", True, None, 0, 0),
        ("evidence", "TEXT", True, None, 0, 0),
        ("created_at", "REAL", True, None, 0, 0),
    ),
}
_SCHEMA_COLUMN_SIGNATURES = {
    1: {
        **_COMMON_COLUMN_SIGNATURES,
        "workspaces": (
            ("id", "TEXT", False, None, 1, 0),
            ("root", "TEXT", True, None, 0, 0),
            ("registered_at", "REAL", True, None, 0, 0),
            ("epoch", "INTEGER", True, "1", 0, 0),
        ),
    },
    LEGACY_SCHEMA_TWO_VERSION: {
        **_COMMON_COLUMN_SIGNATURES,
        "workspaces": (
            ("id", "TEXT", False, None, 1, 0),
            ("root", "TEXT", True, None, 0, 0),
            ("registered_at", "REAL", True, None, 0, 0),
            ("epoch", "INTEGER", True, "1", 0, 0),
            ("next_queue_order", "INTEGER", True, "1", 0, 0),
        ),
    },
    SCHEMA_VERSION: {
        **_COMMON_COLUMN_SIGNATURES,
        "tasks": (
            *_COMMON_COLUMN_SIGNATURES["tasks"],
            ("token_file_path", "TEXT", False, None, 0, 0),
            ("token_file_identity", "TEXT", False, None, 0, 0),
            ("start_operation_id", "TEXT", False, None, 0, 0),
        ),
        "workspaces": (
            ("id", "TEXT", False, None, 1, 0),
            ("root", "TEXT", True, None, 0, 0),
            ("registered_at", "REAL", True, None, 0, 0),
            ("epoch", "INTEGER", True, "1", 0, 0),
            ("next_queue_order", "INTEGER", True, "1", 0, 0),
        ),
        "operation_receipts": (
            ("operation_id", "TEXT", False, None, 1, 0),
            ("workspace_id", "TEXT", True, None, 0, 0),
            ("action", "TEXT", True, None, 0, 0),
            ("parameters_json", "TEXT", True, None, 0, 0),
            ("owner_token_hash", "TEXT", False, None, 0, 0),
            ("fingerprint", "TEXT", True, None, 0, 0),
            ("task_id", "TEXT", False, None, 0, 0),
            ("result_json", "TEXT", True, None, 0, 0),
            ("terminal_json", "TEXT", False, None, 0, 0),
            ("token_cleanup_path", "TEXT", False, None, 0, 0),
            ("token_cleanup_identity", "TEXT", False, None, 0, 0),
            ("created_at", "REAL", True, None, 0, 0),
            ("finalized_at", "REAL", False, None, 0, 0),
            ("delivered_at", "REAL", False, None, 0, 0),
            ("retired_at", "REAL", False, None, 0, 0),
        ),
        "token_cleanup_jobs": (
            ("task_id", "TEXT", False, None, 1, 0),
            ("workspace_id", "TEXT", True, None, 0, 0),
            ("token_file_path", "TEXT", True, None, 0, 0),
            ("token_file_identity", "TEXT", True, None, 0, 0),
            ("token_hash", "TEXT", True, None, 0, 0),
            ("reason", "TEXT", True, None, 0, 0),
            ("created_at", "REAL", True, None, 0, 0),
            ("completed_at", "REAL", False, None, 0, 0),
            ("last_attempt_at", "REAL", False, None, 0, 0),
            ("attempt_count", "INTEGER", True, "0", 0, 0),
        ),
    },
}
_REQUIRED_PRIMARY_KEYS = {
    "scheduler_meta": ("key",),
    "workspaces": ("id",),
    "tasks": ("id",),
    "claims": ("id",),
    "claim_scopes": ("claim_id", "scope_type", "value"),
    "recovery_events": ("id",),
    "operation_receipts": ("operation_id",),
    "token_cleanup_jobs": ("task_id",),
}
_REQUIRED_FOREIGN_KEYS = {
    "scheduler_meta": (),
    "workspaces": (),
    "tasks": (("workspace_id", "workspaces", "id", "NO ACTION", "CASCADE", "NONE"),),
    "claims": (
        ("task_id", "tasks", "id", "NO ACTION", "CASCADE", "NONE"),
        ("workspace_id", "workspaces", "id", "NO ACTION", "CASCADE", "NONE"),
    ),
    "claim_scopes": (("claim_id", "claims", "id", "NO ACTION", "CASCADE", "NONE"),),
    "recovery_events": (
        ("task_id", "tasks", "id", "NO ACTION", "CASCADE", "NONE"),
        ("workspace_id", "workspaces", "id", "NO ACTION", "CASCADE", "NONE"),
    ),
    "operation_receipts": (),
    "token_cleanup_jobs": (
        ("task_id", "tasks", "id", "NO ACTION", "CASCADE", "NONE"),
        ("workspace_id", "workspaces", "id", "NO ACTION", "CASCADE", "NONE"),
    ),
}
_SCHEMA_TWO_INDEX_SIGNATURES = {
    "tasks_workspace_state": ("tasks", (("workspace_id", False), ("state", False)), None),
    "tasks_state_expires": ("tasks", (("state", False), ("expires_at", False)), None),
    "tasks_workspace_state_expires": (
        "tasks",
        (("workspace_id", False), ("state", False), ("expires_at", False)),
        None,
    ),
    "tasks_workspace_token_created": (
        "tasks",
        (("workspace_id", False), ("token_hash", False), ("created_at", True)),
        None,
    ),
    "tasks_workspace_terminal_recency": (
        "tasks",
        (
            ("workspace_id", False),
            ("finished_at", True),
            ("created_at", True),
            ("id", True),
        ),
        "state in ('completed', 'failed', 'expired')",
    ),
    "claims_workspace_state_order": (
        "claims",
        (("workspace_id", False), ("state", False), ("queue_order", False)),
        None,
    ),
    "claims_workspace_order": (
        "claims",
        (("workspace_id", False), ("queue_order", False)),
        None,
    ),
    "claims_task_state": ("claims", (("task_id", False), ("state", False)), None),
    "recovery_events_task_id": ("recovery_events", (("task_id", False),), None),
}
_SCHEMA_ONE_INDEX_SIGNATURES = {
    "tasks_workspace_state": _SCHEMA_TWO_INDEX_SIGNATURES["tasks_workspace_state"],
    "claims_workspace_state_order": _SCHEMA_TWO_INDEX_SIGNATURES["claims_workspace_state_order"],
}
_SCHEMA_TWO_INDEXES = frozenset(_SCHEMA_TWO_INDEX_SIGNATURES)
_SCHEMA_THREE_RECEIPT_INDEX_SIGNATURES = {
    "tasks_open_token_hash_global": (
        "tasks",
        (("token_hash", False),),
        "state in ('active', 'outcome_unknown')",
    ),
    "tasks_open_token_file_identity": (
        "tasks",
        (("token_file_identity", False),),
        "token_file_identity is not null and state in ('active', 'outcome_unknown')",
    ),
    "tasks_start_operation_id": (
        "tasks",
        (("start_operation_id", False),),
        "start_operation_id is not null",
    ),
    "claims_open_global": (
        "claims",
        (("state", False),),
        "state in ('queued', 'active', 'parked')",
    ),
    "operation_receipts_delivered_created": (
        "operation_receipts",
        (("delivered_at", True), ("created_at", True), ("operation_id", True)),
        "delivered_at is not null",
    ),
    "operation_receipts_retired_created": (
        "operation_receipts",
        (("retired_at", True), ("created_at", True), ("operation_id", True)),
        "retired_at is not null",
    ),
    "operation_receipts_workspace_created": (
        "operation_receipts",
        (("workspace_id", False), ("created_at", True), ("operation_id", True)),
        None,
    ),
    "operation_receipts_action_task": (
        "operation_receipts",
        (("action", False), ("task_id", False)),
        None,
    ),
    "operation_receipts_replay_required": (
        "operation_receipts",
        (("operation_id", False),),
        "delivered_at is null and retired_at is null",
    ),
    "operation_receipts_task_start_unique": (
        "operation_receipts",
        (("task_id", False),),
        "action = 'task.start'",
    ),
    "operation_receipts_cleanup_identity": (
        "operation_receipts",
        (("token_cleanup_identity", False),),
        "token_cleanup_identity is not null",
    ),
    "operation_receipts_cleanup_token_hash": (
        "operation_receipts",
        (("owner_token_hash", False),),
        "token_cleanup_path is not null",
    ),
    "token_cleanup_jobs_pending_created": (
        "token_cleanup_jobs",
        (("last_attempt_at", False), ("created_at", False), ("task_id", False)),
        None,
    ),
}
_SCHEMA_THREE_INDEX_SIGNATURES = {
    **_SCHEMA_TWO_INDEX_SIGNATURES,
    **_SCHEMA_THREE_RECEIPT_INDEX_SIGNATURES,
}
_SCHEMA_THREE_INDEXES = frozenset(_SCHEMA_THREE_INDEX_SIGNATURES)
_REQUIRED_AUTO_INDEX_SIGNATURES = {
    "scheduler_meta": (("pk", (("key", False),)),),
    "workspaces": (
        ("pk", (("id", False),)),
        ("u", (("root", False),)),
    ),
    "tasks": (("pk", (("id", False),)),),
    "claims": (("pk", (("id", False),)),),
    "claim_scopes": (("pk", (("claim_id", False), ("scope_type", False), ("value", False))),),
    "recovery_events": (("pk", (("id", False),)),),
    "operation_receipts": (("pk", (("operation_id", False),)),),
    "token_cleanup_jobs": (
        ("pk", (("task_id", False),)),
        ("u", (("token_file_path", False),)),
        ("u", (("token_file_identity", False),)),
        ("u", (("token_hash", False),)),
    ),
}
_OPEN_TASK_STATES = ("active", "outcome_unknown")
_OPEN_CLAIM_STATES = ("queued", "active", "parked")
_TASK_STATES = frozenset((*_OPEN_TASK_STATES, "completed", "failed", "expired"))
_CLAIM_STATES = frozenset((*_OPEN_CLAIM_STATES, "released", "cancelled"))
_CLAIM_KINDS = frozenset(("normal", "freeze"))
_CLAIM_SCOPE_TYPES = frozenset(("write", "resource", "parked_for", "priority"))
_SIDECAR_SUFFIXES = ("-wal", "-shm", "-journal")
_SQLITE_BACKUP_TIMEOUT_SECONDS = 30.0
_SQLITE_BACKUP_PAGES = 256
_SQLITE_BACKUP_SLEEP_SECONDS = 0.05


def _explicit_path(path: Path) -> Path:
    return Path(os.path.abspath(path.expanduser()))


def _reject_symlink(path: Path) -> None:
    if _is_link_or_junction(_explicit_path(path)):
        raise UsageError(
            "Scheduler state files must not be symbolic links.",
            details={"path": str(_explicit_path(path)), "reason": "state-file-symlink"},
        )


def _is_link_or_junction(path: Path) -> bool:
    return path.is_symlink() or _is_windows_reparse_point(path)


def _directory_entry_exists(path: Path) -> bool:
    try:
        path.lstat()
    except FileNotFoundError:
        return False
    return True


def _validate_existing_maintenance_parent(path: Path) -> Path:
    explicit = _explicit_path(path)
    if _is_link_or_junction(explicit):
        raise UsageError(
            "Scheduler maintenance parents must not be links or junctions.",
            details={"path": str(explicit), "reason": "maintenance-parent-link"},
        )
    if not explicit.exists():
        raise UsageError(
            "Scheduler maintenance parent does not exist.",
            details={"path": str(explicit), "reason": "maintenance-parent-missing"},
        )
    try:
        _ensure_private_directory(explicit, preserve_existing=True)
        if os.name == "nt":
            _verify_windows_maintenance_acl(explicit)
    except OSError as exc:
        raise UsageError(
            f"Scheduler maintenance parent is not private: {exc}",
            details={"path": str(explicit), "reason": "maintenance-parent-unsafe"},
        ) from exc
    return explicit.resolve()


def _create_durable_maintenance_parent(path: Path) -> Path:
    path = _explicit_path(path)
    missing: list[Path] = []
    cursor = path
    while not cursor.exists():
        missing.append(cursor)
        parent = cursor.parent
        if parent == cursor:
            raise UsageError(
                "Scheduler maintenance parent could not be resolved.",
                details={"path": str(path), "reason": "maintenance-parent-missing"},
            )
        cursor = parent
    if _is_link_or_junction(cursor) or not cursor.is_dir():
        raise UsageError(
            "Scheduler maintenance parents must be real directories.",
            details={"path": str(cursor), "reason": "maintenance-parent-unsafe"},
        )
    for directory in reversed(missing):
        existed_before = _directory_entry_exists(directory)
        try:
            # Keep all runtime directory creation in the shared platform ACL
            # helper.  Calling it one level at a time also gives us the exact
            # entry whose parent barrier failed.
            _ensure_private_directory(directory, preserve_existing=True)
            if os.name == "nt":
                _verify_windows_maintenance_acl(directory)
        except OSError as exc:
            created_entry = (
                directory if not existed_before and _directory_entry_exists(directory) else None
            )
            raise StateError(
                "Scheduler maintenance parent creation could not be durably proven.",
                details={
                    "path": str(path),
                    "reason": "maintenance-directory-barrier-failed",
                    "operation": "maintenance-parent-create",
                    "entry": str(created_entry) if created_entry is not None else None,
                    # A newly-created maintenance parent is never a deletion
                    # artifact.  Report its exact entry for evidence only;
                    # callers may re-flush the parent listed separately.
                    "cleanup_pending": [],
                    "durability_pending_parent": [str(directory.parent)],
                    "recovery_required": True,
                },
            ) from exc
    return path.resolve()


def _prepare_maintenance_parent(path: Path) -> Path:
    explicit = _explicit_path(path)
    if explicit.exists() and _is_link_or_junction(explicit):
        raise UsageError(
            "Scheduler maintenance parents must not be links or junctions.",
            details={"path": str(explicit), "reason": "maintenance-parent-link"},
        )
    try:
        if explicit.exists():
            _ensure_private_directory(explicit, preserve_existing=True)
            if os.name == "nt":
                _verify_windows_maintenance_acl(explicit)
            return explicit.resolve()
        return _create_durable_maintenance_parent(explicit)
    except OSError as exc:
        raise UsageError(
            f"Scheduler maintenance parent is not private: {exc}",
            details={"path": str(explicit), "reason": "maintenance-parent-unsafe"},
        ) from exc


def _verify_windows_maintenance_file(path: Path) -> None:
    if os.name != "nt":
        return
    try:
        _verify_windows_maintenance_acl(path)
    except OSError as exc:
        raise UsageError(
            f"Scheduler maintenance file ACL is not private: {exc}",
            details={"path": str(path), "reason": "maintenance-file-unsafe"},
        ) from exc


def _validate_maintenance_database_file(path: Path) -> None:
    _reject_symlink(path)
    try:
        metadata = path.lstat()
    except FileNotFoundError as exc:
        raise UsageError(
            "Scheduler maintenance database entry does not exist.",
            details={"path": str(path), "reason": "state-file-missing"},
        ) from exc
    if not stat.S_ISREG(metadata.st_mode):
        raise UsageError(
            "Scheduler maintenance database entries must be regular files.",
            details={"path": str(path), "reason": "maintenance-file-not-regular"},
        )
    if os.name == "nt":
        _verify_windows_maintenance_file(path)
        return
    if metadata.st_uid != os.geteuid() or stat.S_IMODE(metadata.st_mode) & 0o022:
        raise UsageError(
            "Scheduler maintenance database entries must be owned by the current user and not "
            "group- or other-writable.",
            details={"path": str(path), "reason": "maintenance-file-unsafe"},
        )


def _require_no_processes(confirmed: bool) -> None:
    if not confirmed:
        raise UsageError(
            "Stop Router entry and all scheduler/executor processes, then repeat with "
            "--confirm-no-processes.",
            details={"reason": "zero-process-attestation-required"},
        )


@dataclass(frozen=True)
class _ReadOnlyFileEvidence:
    path: Path
    identity: _MaintenanceFileIdentity
    digest: str


@dataclass(frozen=True)
class _StandaloneReadEvidence:
    database: Path
    files: tuple[_ReadOnlyFileEvidence, ...]
    staging_root: Path | None = None


def _create_standalone_staging_parent(parent: Path) -> Path:
    """Create a unique private staging leaf with a durable parent barrier."""

    secured_parent = _prepare_maintenance_parent(parent)
    for _ in range(32):
        candidate = secured_parent / f".unity-scheduler-read-{uuid.uuid4().hex}"
        try:
            _ensure_private_directory(candidate, require_new=True)
        except FileExistsError:
            continue
        except OSError as exc:
            raise StateError(
                "Standalone scheduler read staging could not be created durably.",
                details={
                    "path": str(candidate),
                    "reason": "standalone-staging-create-failed",
                    "entry": str(candidate),
                    "cleanup_pending": [str(candidate)]
                    if _directory_entry_exists(candidate)
                    else [],
                    "durability_pending_parent": [str(candidate.parent)],
                    "recovery_required": True,
                },
            ) from exc
        if os.name == "nt":
            try:
                _verify_windows_maintenance_acl(candidate)
            except OSError as exc:
                raise StateError(
                    "Standalone scheduler read staging ACL could not be proven.",
                    details={
                        "path": str(candidate),
                        "reason": "standalone-staging-create-failed",
                        "entry": str(candidate),
                        "cleanup_pending": [str(candidate)],
                        "durability_pending_parent": [str(candidate.parent)],
                        "recovery_required": True,
                    },
                ) from exc
        return candidate
    raise StateError(
        "Standalone scheduler read staging name allocation was exhausted.",
        details={
            "path": str(secured_parent),
            "reason": "standalone-staging-create-failed",
            "recovery_required": True,
        },
    )


def _read_only_connection(
    path: Path,
) -> tuple[sqlite3.Connection, _StandaloneReadEvidence]:
    _reject_symlink(path)
    explicit = _explicit_path(path)
    parent = _validate_existing_maintenance_parent(explicit.parent)
    resolved = parent / explicit.name
    _validate_maintenance_database_file(resolved)
    sidecars = _existing_sidecars(resolved)
    for sidecar in sidecars:
        _validate_maintenance_database_file(sidecar)
    files = tuple(
        _ReadOnlyFileEvidence(
            candidate,
            _maintenance_file_identity(candidate),
            _sha256_file(candidate),
        )
        for candidate in (resolved, *sidecars)
    )
    staging_root: Path | None = None
    read_database = resolved
    if sidecars:
        staging_root = _create_standalone_staging_parent(resolved.parent)
        try:
            read_database = staging_root / resolved.name
            for evidence in files:
                suffix = str(evidence.path)[len(str(resolved)) :]
                staged = Path(f"{read_database}{suffix}")
                shutil.copyfile(evidence.path, staged)
                if _sha256_file(staged) != evidence.digest:
                    raise StateError(
                        "Standalone scheduler input changed while its read snapshot was staged.",
                        details={
                            "path": str(resolved),
                            "reason": "standalone-input-changed",
                        },
                    )
                if os.name != "nt":
                    staged.chmod(0o600)
                _verify_windows_maintenance_file(staged)
        except Exception as exc:
            try:
                _cleanup_standalone_read(_StandaloneReadEvidence(resolved, files, staging_root))
            except StateError as cleanup_exc:
                if isinstance(exc, (StateError, UsageError)):
                    exc.details = {**exc.details, "cleanup_secondary_error": cleanup_exc.details}
                else:
                    raise StateError(
                        "Standalone scheduler staging failed; the primary error is preserved "
                        "with cleanup evidence.",
                        details={
                            "path": str(resolved),
                            "reason": "standalone-staging-failed",
                            "cause": exc.__class__.__name__,
                            "cleanup_secondary_error": cleanup_exc.details,
                        },
                    ) from exc
            raise
    standalone_evidence = _StandaloneReadEvidence(resolved, files, staging_root)
    query = f"{read_database.as_uri()}?mode=ro"
    if staging_root is None:
        query += "&immutable=1"
    try:
        connection = sqlite3.connect(query, uri=True, timeout=30.0)
    except sqlite3.DatabaseError as exc:
        if staging_root is not None:
            try:
                _cleanup_standalone_read(standalone_evidence)
            except StateError as cleanup_exc:
                raise StateError(
                    f"Cannot open scheduler state read-only: {exc}",
                    details={
                        "path": str(resolved),
                        "reason": "state-open-failed",
                        "cleanup_secondary_error": cleanup_exc.details,
                    },
                ) from exc
        raise StateError(
            f"Cannot open scheduler state read-only: {exc}",
            details={"path": str(resolved), "reason": "state-open-failed"},
        ) from exc
    connection.row_factory = sqlite3.Row
    return connection, standalone_evidence


def _verify_standalone_read(evidence: _StandaloneReadEvidence) -> None:
    try:
        expected_sidecars = tuple(item.path for item in evidence.files[1:])
        unchanged = tuple(_existing_sidecars(evidence.database)) == expected_sidecars and all(
            _maintenance_file_identity(item.path) == item.identity
            and _sha256_file(item.path) == item.digest
            for item in evidence.files
        )
    except (OSError, UsageError):
        unchanged = False
    cleanup_error: StateError | None = None
    try:
        cleanup_failed = not _cleanup_standalone_read(evidence)
    except StateError as exc:
        cleanup_failed = True
        cleanup_error = exc
    if not unchanged:
        details: dict[str, Any] = {
            "path": str(evidence.database),
            "reason": "standalone-input-changed",
        }
        if cleanup_error is not None:
            details["cleanup_secondary_error"] = cleanup_error.details
        raise StateError(
            "Standalone scheduler input changed during read-only verification.",
            details=details,
        )
    if cleanup_failed:
        details = {
            "path": str(evidence.database),
            "reason": "standalone-staging-cleanup-failed",
        }
        if cleanup_error is not None:
            details["cleanup_secondary_error"] = cleanup_error.details
        raise StateError(
            "Standalone scheduler read staging could not be removed.",
            details=details,
        )


def _cleanup_standalone_read(evidence: _StandaloneReadEvidence) -> bool:
    if evidence.staging_root is None:
        return True
    staging_root = evidence.staging_root
    pending: list[str] = []
    durability_pending_parent: list[str] = []
    for item in evidence.files:
        staged = staging_root / item.path.name
        try:
            staged.unlink(missing_ok=True)
        except OSError:
            if _directory_entry_exists(staged):
                pending.append(str(staged))
            continue
        try:
            _durable_barrier_or_recovery(
                staging_root,
                operation="standalone-staging-entry-unlink",
                entry=staged,
            )
        except StateError as exc:
            pending.extend(str(path) for path in exc.details.get("cleanup_pending", []))
            durability_pending_parent.extend(
                str(path) for path in exc.details.get("durability_pending_parent", [])
            )
    try:
        staging_root.rmdir()
    except OSError:
        if _directory_entry_exists(staging_root):
            pending.append(str(staging_root))
    else:
        try:
            _durable_barrier_or_recovery(
                staging_root.parent,
                operation="standalone-staging-rmdir",
                entry=staging_root,
            )
        except StateError as exc:
            durability_pending_parent.extend(
                str(path) for path in exc.details.get("durability_pending_parent", [])
            )
    if pending or durability_pending_parent:
        raise StateError(
            "Standalone scheduler read staging cleanup remains pending.",
            details={
                "path": str(evidence.database),
                "reason": "standalone-staging-cleanup-failed",
                "cleanup_pending": pending,
                "durability_pending_parent": durability_pending_parent,
                "recovery_required": True,
            },
        )
    return True


def _scalar(connection: sqlite3.Connection, query: str, parameters: tuple[Any, ...] = ()) -> int:
    return int(connection.execute(query, parameters).fetchone()[0])


def _group_counts(connection: sqlite3.Connection, table: str, column: str) -> dict[str, int]:
    return {
        str(row[column]): int(row["count"])
        for row in connection.execute(
            f"SELECT {column}, COUNT(*) AS count FROM {table} GROUP BY {column} ORDER BY {column}"
        ).fetchall()
    }


def _semantic_failure(
    message: str,
    resolved: Path,
    reason: str,
    **details: Any,
) -> None:
    raise StateError(
        message,
        details={"path": str(resolved), "reason": reason, **details},
    )


def _is_finite_numeric(value: object, storage_type: str) -> bool:
    if storage_type not in {"integer", "real"}:
        return False
    try:
        return math.isfinite(float(value))
    except (TypeError, ValueError, OverflowError):
        return False


def _validate_identifiers(connection: sqlite3.Connection, resolved: Path) -> None:
    invalid_counts: dict[str, int] = {}
    tables = ["workspaces", "tasks", "claims", "recovery_events"]
    if connection.execute(
        "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'operation_receipts'"
    ).fetchone():
        tables.append("operation_receipts")
    for table in tables:
        identifier_column = "operation_id" if table == "operation_receipts" else "id"
        invalid = 0
        for row in connection.execute(
            f'SELECT "{identifier_column}" AS identifier, '
            f'typeof("{identifier_column}") AS storage_type FROM "{table}"'
        ):
            value = row["identifier"]
            if (
                row["storage_type"] != "text"
                or not isinstance(value, str)
                or not value
                or _has_control_characters(value)
            ):
                invalid += 1
        if invalid:
            invalid_counts[table] = invalid
    if invalid_counts:
        _semantic_failure(
            "Scheduler state contains malformed identifiers.",
            resolved,
            "identifier-invalid",
            tables=invalid_counts,
        )


def _validate_scope_values(connection: sqlite3.Connection, resolved: Path) -> None:
    invalid_counts = {"write": 0, "resource": 0, "parked_for": 0, "priority": 0}
    rows = connection.execute(
        "SELECT scope_type, value, typeof(value) AS storage_type FROM claim_scopes"
    ).fetchall()
    for row in rows:
        scope_type = row["scope_type"]
        if scope_type not in invalid_counts:
            continue
        value = row["value"]
        if (
            row["storage_type"] != "text"
            or not isinstance(value, str)
            or not value
            or _has_control_characters(value)
        ):
            invalid_counts[scope_type] += 1
            continue
        if scope_type == "write":
            if not _is_canonical_write_scope(value):
                invalid_counts[scope_type] += 1
        elif (scope_type == "resource" and value.strip().casefold() != value) or (
            scope_type == "priority" and value != "urgent"
        ):
            invalid_counts[scope_type] += 1
    invalid_counts = {kind: count for kind, count in invalid_counts.items() if count}
    if invalid_counts:
        _semantic_failure(
            "Scheduler state contains malformed claim scope values.",
            resolved,
            "claim-scope-value-invalid",
            scope_types=invalid_counts,
        )


def _is_canonical_write_scope(value: object) -> bool:
    if not isinstance(value, str) or not value or _has_control_characters(value) or "\\" in value:
        return False
    pure = PurePosixPath(value)
    return (
        not pure.is_absolute()
        and ".." not in pure.parts
        and pure.as_posix() == value
        and _platform_case_identity(value) == value
    )


def _workspace_identity(root: str, schema_version: int) -> str:
    identity = (
        os.path.normcase(root).casefold()
        if schema_version < SCHEMA_VERSION
        else _platform_case_identity(root)
    )
    return hashlib.sha256(identity.encode("utf-8")).hexdigest()


def _legacy_open_write_scope_migration_count(
    connection: sqlite3.Connection,
    schema_version: int,
) -> int:
    """Count legacy open write scopes whose original case cannot be reconstructed."""

    if os.name == "nt" or schema_version >= SCHEMA_VERSION:
        return 0
    return _scalar(
        connection,
        "SELECT COUNT(*) FROM claim_scopes AS scope "
        "JOIN claims AS claim ON claim.id = scope.claim_id "
        "WHERE scope.scope_type = 'write' "
        "AND claim.state IN ('queued', 'active', 'parked')",
    )


def _validate_relational_schema_signatures(
    connection: sqlite3.Connection,
    resolved: Path,
    schema_version: int = SCHEMA_VERSION,
) -> None:
    invalid_primary_keys: list[str] = []
    invalid_foreign_keys: list[str] = []
    tables = set(_REQUIRED_COLUMNS)
    if schema_version == SCHEMA_VERSION:
        tables.update({"operation_receipts", "token_cleanup_jobs"})
    for table in sorted(tables):
        expected_primary_key = _REQUIRED_PRIMARY_KEYS[table]
        primary_key = tuple(
            str(row["name"])
            for row in sorted(
                (
                    row
                    for row in connection.execute(f'PRAGMA table_info("{table}")')
                    if int(row["pk"]) > 0
                ),
                key=lambda row: int(row["pk"]),
            )
        )
        if primary_key != expected_primary_key:
            invalid_primary_keys.append(table)

        foreign_keys = tuple(
            sorted(
                (
                    str(row["from"]),
                    str(row["table"]),
                    str(row["to"]),
                    str(row["on_update"]).upper(),
                    str(row["on_delete"]).upper(),
                    str(row["match"]).upper(),
                )
                for row in connection.execute(f'PRAGMA foreign_key_list("{table}")')
            )
        )
        if foreign_keys != _REQUIRED_FOREIGN_KEYS[table]:
            invalid_foreign_keys.append(table)
    if invalid_primary_keys or invalid_foreign_keys:
        _semantic_failure(
            "Scheduler relational schema constraints are invalid.",
            resolved,
            "schema-relational-signature-invalid",
            primary_keys=invalid_primary_keys,
            foreign_keys=invalid_foreign_keys,
        )


def _validate_index_signatures(
    connection: sqlite3.Connection,
    resolved: Path,
    signatures: dict[str, tuple[str, tuple[tuple[str, bool], ...], str | None]],
    schema_version: int,
) -> None:
    invalid_indexes: list[str] = []
    for name, (expected_table, expected_columns, expected_predicate) in signatures.items():
        definition = connection.execute(
            "SELECT tbl_name, sql FROM sqlite_master WHERE type = 'index' AND name = ?",
            (name,),
        ).fetchone()
        if definition is None or definition["tbl_name"] != expected_table:
            invalid_indexes.append(name)
            continue
        listed = next(
            (
                row
                for row in connection.execute(f'PRAGMA index_list("{expected_table}")')
                if row["name"] == name
            ),
            None,
        )
        if listed is None:
            invalid_indexes.append(name)
            continue
        key_rows = tuple(
            row
            for row in connection.execute(f'PRAGMA index_xinfo("{name}")')
            if int(row["key"]) == 1
        )
        columns = tuple((str(row["name"]), bool(row["desc"])) for row in key_rows)
        collations = tuple(str(row["coll"]).upper() for row in key_rows)
        normalized_sql = " ".join(str(definition["sql"] or "").casefold().split())
        expected_partial = expected_predicate is not None
        actual_predicate = (
            normalized_sql.partition(" where ")[2].removesuffix(";") if expected_partial else None
        )
        expected_unique = name in {
            "operation_receipts_task_start_unique",
            "tasks_start_operation_id",
        }
        if (
            bool(listed["unique"]) != expected_unique
            or bool(listed["partial"]) != expected_partial
            or columns != expected_columns
            or collations != ("BINARY",) * len(expected_columns)
            or actual_predicate != expected_predicate
        ):
            invalid_indexes.append(name)
    if invalid_indexes:
        _semantic_failure(
            f"Scheduler schema {schema_version} index definitions are invalid.",
            resolved,
            "schema-index-signature-invalid",
            indexes=sorted(invalid_indexes),
        )


def _validate_schema_two_index_signatures(connection: sqlite3.Connection, resolved: Path) -> None:
    _validate_index_signatures(
        connection,
        resolved,
        _SCHEMA_TWO_INDEX_SIGNATURES,
        LEGACY_SCHEMA_TWO_VERSION,
    )


def _validate_schema_three_index_signatures(connection: sqlite3.Connection, resolved: Path) -> None:
    _validate_index_signatures(
        connection,
        resolved,
        _SCHEMA_THREE_INDEX_SIGNATURES,
        SCHEMA_VERSION,
    )


def _validate_declared_schema_structure(
    connection: sqlite3.Connection,
    resolved: Path,
    schema_version: int,
) -> None:
    expected_columns = _SCHEMA_COLUMN_SIGNATURES[schema_version]
    user_tables = {
        str(row["name"])
        for row in connection.execute(
            "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%'"
        )
    }
    expected_tables = set(expected_columns)
    schema_programs = [
        {"type": str(row["type"]), "name": str(row["name"])}
        for row in connection.execute(
            "SELECT type, name FROM sqlite_master "
            "WHERE type IN ('trigger', 'view') ORDER BY type, name"
        )
    ]
    invalid_columns: list[str] = []
    forbidden_table_features: list[str] = []
    forbidden_pattern = re.compile(
        r"\b(check\b|collate\b|autoincrement\b|strict\b|without\s+rowid\b|"
        r"on\s+conflict\b|deferrable\b)",
        re.IGNORECASE,
    )
    for table, signature in expected_columns.items():
        actual = tuple(
            (
                str(row["name"]),
                str(row["type"]).upper(),
                bool(row["notnull"]),
                None if row["dflt_value"] is None else str(row["dflt_value"]),
                int(row["pk"]),
                int(row["hidden"]),
            )
            for row in connection.execute(f'PRAGMA table_xinfo("{table}")')
        )
        if actual != signature:
            invalid_columns.append(table)
        definition = connection.execute(
            "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = ?",
            (table,),
        ).fetchone()
        if definition is not None and forbidden_pattern.search(str(definition["sql"] or "")):
            forbidden_table_features.append(table)

    if schema_version == 1:
        expected_created_indexes = set(_SCHEMA_ONE_INDEX_SIGNATURES)
    elif schema_version == LEGACY_SCHEMA_TWO_VERSION:
        expected_created_indexes = set(_SCHEMA_TWO_INDEX_SIGNATURES)
    else:
        expected_created_indexes = set(_SCHEMA_THREE_INDEX_SIGNATURES)
    actual_created_indexes: set[str] = set()
    invalid_auto_indexes: list[str] = []
    for table in sorted(expected_tables):
        expected_auto_indexes = _REQUIRED_AUTO_INDEX_SIGNATURES[table]
        actual_auto_indexes: list[tuple[str, tuple[tuple[str, bool], ...]]] = []
        for listed in connection.execute(f'PRAGMA index_list("{table}")'):
            origin = str(listed["origin"])
            if origin == "c":
                actual_created_indexes.add(str(listed["name"]))
                continue
            key_rows = tuple(
                row
                for row in connection.execute(f'PRAGMA index_xinfo("{listed["name"]}")')
                if int(row["key"]) == 1
            )
            columns = tuple((str(row["name"]), bool(row["desc"])) for row in key_rows)
            collations = tuple(str(row["coll"]).upper() for row in key_rows)
            if (
                origin not in {"pk", "u"}
                or int(listed["unique"]) != 1
                or int(listed["partial"]) != 0
                or collations != ("BINARY",) * len(columns)
                or any(row["name"] is None for row in key_rows)
            ):
                invalid_auto_indexes.append(table)
                continue
            actual_auto_indexes.append((origin, columns))
        if sorted(actual_auto_indexes) != sorted(expected_auto_indexes):
            invalid_auto_indexes.append(table)

    unexpected_tables = sorted(user_tables - expected_tables)
    missing_tables = sorted(expected_tables - user_tables)
    unexpected_indexes = sorted(actual_created_indexes - expected_created_indexes)
    missing_indexes = sorted(expected_created_indexes - actual_created_indexes)
    reserved_future_indexes = sorted(
        actual_created_indexes & (set(_SCHEMA_THREE_INDEX_SIGNATURES) - expected_created_indexes)
    )
    if reserved_future_indexes:
        _semantic_failure(
            "Scheduler legacy schema contains reserved future index names.",
            resolved,
            "schema-index-signature-invalid",
            indexes=reserved_future_indexes,
        )
    if (
        unexpected_tables
        or missing_tables
        or schema_programs
        or invalid_columns
        or forbidden_table_features
        or unexpected_indexes
        or missing_indexes
        or invalid_auto_indexes
    ):
        _semantic_failure(
            "Scheduler declared schema contains unexpected or altered objects.",
            resolved,
            "schema-declaration-invalid",
            unexpected_tables=unexpected_tables,
            missing_tables=missing_tables,
            programs=schema_programs,
            columns=sorted(invalid_columns),
            forbidden_table_features=sorted(forbidden_table_features),
            unexpected_indexes=unexpected_indexes,
            missing_indexes=missing_indexes,
            auto_indexes=sorted(set(invalid_auto_indexes)),
        )

    if schema_version == 1:
        _validate_index_signatures(
            connection,
            resolved,
            _SCHEMA_ONE_INDEX_SIGNATURES,
            schema_version,
        )


_OWNER_AUTHENTICATED_ACTIONS = frozenset(
    {
        "task.start",
        "task.heartbeat",
        "task.park",
        "task.release",
        "claim.acquire",
        "claim.release",
        "queue.cancel",
        "freeze.acquire",
    }
)
_ACTION_RESULT_REQUIRED_KEYS = {
    "workspace.register": frozenset({"id", "root", "registered_at", "epoch", "created"}),
    "workspace.unregister": frozenset({"id", "root", "removed"}),
    "task.start": frozenset({"id", "state", "created_at", "expires_at"}),
    "task.heartbeat": frozenset({"id", "state", "heartbeat_at", "expires_at"}),
    "task.park": frozenset({"task_id", "freeze_id", "claim_ids", "states", "parked"}),
    "task.release": frozenset({"id", "state", "result", "finished_at"}),
    "claim.acquire": frozenset({"id", "task_id", "kind", "state", "queue_order"}),
    "claim.release": frozenset({"id", "task_id", "kind", "state", "queue_order"}),
    "queue.cancel": frozenset({"id", "task_id", "kind", "state", "queue_order"}),
    "freeze.acquire": frozenset({"id", "task_id", "kind", "state", "queue_order"}),
    "recovery.resolve": frozenset({"id", "state", "result", "finished_at"}),
}
_TASK_WAIT_ABORT_REASONS = frozenset(
    {
        "task-ttl-expired",
        "task-ttl-expired-with-active-claim",
        "task-released",
        "task-released-outcome-unknown",
        "task-recovery-resolved",
    }
)


def _finite_receipt_number(value: object, *, positive: bool = False) -> bool:
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        return False
    number = float(value)
    return math.isfinite(number) and (number > 0 if positive else number >= 0)


def _receipt_text(value: object, *, nonempty: bool = True) -> bool:
    return (
        isinstance(value, str)
        and (bool(value) or not nonempty)
        and not _has_control_characters(value)
    )


def _receipt_user_text(value: object, *, nonempty: bool = True) -> bool:
    return isinstance(value, str) and (bool(value) or not nonempty)


def _receipt_entity_id(value: object) -> bool:
    return isinstance(value, str) and bool(value) and not _has_control_characters(value)


def _receipt_positive_integer(value: object) -> bool:
    return isinstance(value, int) and not isinstance(value, bool) and value > 0


def _receipt_optional_number(value: object) -> bool:
    return value is None or _finite_receipt_number(value)


def _stored_token_path_identity(path: str) -> str:
    identity = os.path.normcase(os.path.normpath(path))
    return _platform_case_identity(identity)


def _validate_lifecycle_terminal_proof(
    action: str,
    terminal: dict[str, Any],
    receipt_created_at: object,
    stored_result: dict[str, Any],
) -> None:
    common_keys = {
        "aborted",
        "reason",
        "terminal_finished_at",
        "terminal_result",
        "terminal_state",
    }
    resolution_keys = {
        "resolution_reason",
        "terminal_finished_at",
        "terminal_result",
        "terminal_state",
    }
    proof_keys = frozenset(terminal)
    cleanup_completed = terminal.get("token_cleanup_completed")
    is_revocation = action in LIFECYCLE_REVOCATION_ACTIONS
    is_unknown_release = (
        action == "task.release"
        and stored_result.get("state") == "outcome_unknown"
        and stored_result.get("result") == "outcome-unknown"
    )

    # Wait receipts retain their original resolution-proof contract.  The
    # revocation receipts and an outcome-unknown task release are different:
    # they become replay-safe only after token cleanup has been proven.
    if action in LIFECYCLE_TERMINAL_ACTIONS and proof_keys == frozenset(resolution_keys):
        state = terminal.get("terminal_state")
        if (
            stored_result.get("aborted") is not True
            or stored_result.get("reason")
            not in {
                "task-ttl-expired-with-active-claim",
                "task-released-outcome-unknown",
            }
            or terminal.get("resolution_reason") != "task-recovery-resolved"
            or state not in {"completed", "failed"}
            or terminal.get("terminal_result") != f"recovered-{state}"
            or not _finite_receipt_number(terminal.get("terminal_finished_at"))
            or float(terminal["terminal_finished_at"]) < float(receipt_created_at)
        ):
            raise ValueError("operation recovery resolution proof is malformed")
        return
    if (is_revocation or is_unknown_release) and proof_keys == frozenset(
        resolution_keys | {"token_cleanup_completed"}
    ):
        state = terminal.get("terminal_state")
        if is_revocation:
            result_valid = (
                _receipt_entity_id(stored_result.get("id"))
                and _receipt_entity_id(stored_result.get("task_id"))
                and (
                    stored_result.get("state") in CLAIM_RELEASE_STATES
                    if action == "claim.release"
                    else stored_result.get("state") in QUEUE_CANCEL_STATES
                )
            )
        else:
            result_valid = is_unknown_release
        if (
            not result_valid
            or terminal.get("resolution_reason") != "task-recovery-resolved"
            or cleanup_completed is not True
            or state not in {"completed", "failed"}
            or terminal.get("terminal_result") != f"recovered-{state}"
            or not _finite_receipt_number(terminal.get("terminal_finished_at"))
            or float(terminal["terminal_finished_at"]) < float(receipt_created_at)
        ):
            raise ValueError("operation recovery resolution proof is malformed")
        return

    reason = terminal.get("reason")
    state = terminal.get("terminal_state")
    result = terminal.get("terminal_result")
    expected_transition: tuple[object, object] | None = {
        "task-ttl-expired": ("expired", "expired"),
        "task-ttl-expired-with-active-claim": (
            "outcome_unknown",
            "expired-with-active-claim",
        ),
        "task-released-outcome-unknown": ("outcome_unknown", "outcome-unknown"),
    }.get(reason)
    if reason == "task-released" and state in {"completed", "failed"}:
        expected_transition = (state, state)
    elif reason == "task-recovery-resolved" and state in {"completed", "failed"}:
        expected_transition = (state, f"recovered-{state}")
    if is_revocation:
        valid_keys = {frozenset(common_keys | {"token_cleanup_completed"})}
    elif action == "task.start":
        valid_keys = {
            frozenset(common_keys),
            frozenset(common_keys | {"token_cleanup_completed"}),
        }
    else:
        valid_keys = {frozenset(common_keys)}
    if (
        action not in LIFECYCLE_ACTIONS | {"task.start"}
        or proof_keys not in valid_keys
        or (is_revocation and cleanup_completed is not True)
        or action == "task.release"
        or terminal.get("aborted") is not True
        or expected_transition != (state, result)
        or (
            is_revocation
            and (
                not _receipt_entity_id(stored_result.get("id"))
                or not _receipt_entity_id(stored_result.get("task_id"))
                or (
                    stored_result.get("state") not in CLAIM_RELEASE_STATES
                    if action == "claim.release"
                    else stored_result.get("state") not in QUEUE_CANCEL_STATES
                )
            )
        )
        or (
            action == "task.start"
            and cleanup_completed is True
            and reason not in {"task-ttl-expired", "task-released", "task-recovery-resolved"}
        )
        or (is_revocation and reason not in {"task-ttl-expired", "task-released"})
        or not _finite_receipt_number(terminal.get("terminal_finished_at"))
        or float(terminal["terminal_finished_at"]) < float(receipt_created_at)
    ):
        raise ValueError("operation lifecycle proof is malformed")


def _validate_claim_result_shape(result: dict[str, Any]) -> None:
    required = {
        "id",
        "task_id",
        "kind",
        "state",
        "queue_order",
        "writes",
        "resources",
        "priority",
        "parked_for",
        "created_at",
        "granted_at",
    }
    if not required.issubset(result):
        raise ValueError("claim receipt result is incomplete")
    writes = result["writes"]
    resources = result["resources"]
    if (
        not _receipt_entity_id(result["id"])
        or not _receipt_entity_id(result["task_id"])
        or result["kind"] not in {"normal", "freeze"}
        or result["state"] not in _CLAIM_STATES
        or not _receipt_positive_integer(result["queue_order"])
        or not isinstance(writes, list)
        or not isinstance(resources, list)
        or any(not _is_canonical_write_scope(value) for value in writes)
        or any(not _receipt_text(value) or value.strip().casefold() != value for value in resources)
        or writes != sorted(set(writes))
        or resources != sorted(set(resources))
        or result["priority"] not in {"normal", "urgent"}
        or (result["parked_for"] is not None and not _receipt_entity_id(result["parked_for"]))
        or not _finite_receipt_number(result["created_at"])
        or not _receipt_optional_number(result["granted_at"])
    ):
        raise ValueError("claim receipt result is malformed")
    if result["kind"] == "freeze":
        if writes or resources:
            raise ValueError("freeze receipt result has scopes")
    elif result["priority"] != "normal" or not (writes or resources):
        raise ValueError("normal claim receipt result is malformed")
    if result["state"] == "parked":
        if result["parked_for"] is None:
            raise ValueError("parked claim receipt has no freeze identity")
    elif result["parked_for"] is not None:
        raise ValueError("non-parked claim receipt has a freeze identity")


def _validate_task_result_shape(result: dict[str, Any]) -> None:
    required = {
        "id",
        "owner",
        "summary",
        "state",
        "created_at",
        "heartbeat_at",
        "expires_at",
        "finished_at",
        "result",
        "note",
    }
    if not required.issubset(result):
        raise ValueError("task receipt result is incomplete")
    if (
        not _receipt_entity_id(result["id"])
        or not _receipt_user_text(result["owner"])
        or not _receipt_user_text(result["summary"])
        or result["state"] not in _TASK_STATES
        or not _finite_receipt_number(result["created_at"])
        or not _finite_receipt_number(result["heartbeat_at"])
        or not _finite_receipt_number(result["expires_at"])
        or not _receipt_optional_number(result["finished_at"])
        or (result["result"] is not None and not _receipt_text(result["result"]))
        or (result["note"] is not None and not _receipt_user_text(result["note"], nonempty=False))
    ):
        raise ValueError("task receipt result is malformed")


def _validate_receipt_result(
    action: str,
    parameters: dict[str, Any],
    result: dict[str, Any],
) -> None:
    if "operation" in result or "token_cleanup_pending" in result:
        raise ValueError("stored receipt result includes delivery metadata")
    if action == "workspace.register":
        if (
            result.get("id") != _workspace_id(parameters["workspace"])
            or result.get("root") != parameters["workspace"]
            or not _finite_receipt_number(result.get("registered_at"))
            or not _receipt_positive_integer(result.get("epoch"))
            or not isinstance(result.get("created"), bool)
        ):
            raise ValueError("workspace-register receipt result is malformed")
        return
    if action == "workspace.unregister":
        if (
            result.get("id") != _workspace_id(parameters["workspace"])
            or result.get("root") != parameters["workspace"]
            or result.get("removed") is not True
        ):
            raise ValueError("workspace-unregister receipt result is malformed")
        return
    if action in {"task.start", "task.heartbeat", "task.release", "recovery.resolve"}:
        _validate_task_result_shape(result)
        if action == "task.start":
            ttl = float(parameters["ttl_seconds"])
            if (
                result["owner"] != parameters["owner"]
                or result["summary"] != parameters["summary"]
                or result["state"] != "active"
                or result["heartbeat_at"] != result["created_at"]
                or not math.isclose(
                    float(result["expires_at"]) - float(result["created_at"]),
                    ttl,
                    rel_tol=0.0,
                    abs_tol=1e-9,
                )
                or result["finished_at"] is not None
                or result["result"] is not None
                or result["note"] is not None
            ):
                raise ValueError("task-start receipt result is malformed")
        elif action == "task.heartbeat":
            if (
                result["state"] != "active"
                or float(result["created_at"]) > float(result["heartbeat_at"])
                or float(result["heartbeat_at"]) >= float(result["expires_at"])
                or result["finished_at"] is not None
                or result["result"] is not None
                or (parameters["note"] is not None and result["note"] != parameters["note"])
            ):
                raise ValueError("task-heartbeat receipt result is malformed")
            ttl = parameters["ttl_seconds"]
            if ttl is not None and not math.isclose(
                float(result["expires_at"]) - float(result["heartbeat_at"]),
                float(ttl),
                rel_tol=0.0,
                abs_tol=1e-9,
            ):
                raise ValueError("task-heartbeat TTL result is malformed")
        elif action == "task.release":
            expected_state = (
                "outcome_unknown"
                if parameters["result"] == "outcome-unknown"
                else parameters["result"]
            )
            if (
                result["state"] != expected_state
                or result["result"] != parameters["result"]
                or result["note"] != parameters["note"]
                or result["finished_at"] is None
            ):
                raise ValueError("task-release receipt result is malformed")
        elif (
            result["id"] != parameters["task_id"]
            or result["state"] != parameters["resolution"]
            or result["result"] != f"recovered-{parameters['resolution']}"
            or result["note"] != parameters["evidence"]
            or result["finished_at"] is None
        ):
            raise ValueError("recovery receipt result is malformed")
        return
    if action == "task.park":
        claim_ids = result.get("claim_ids")
        states = result.get("states")
        aborted = result.get("aborted") is True
        if (
            not _receipt_entity_id(result.get("task_id"))
            or not _receipt_entity_id(result.get("freeze_id"))
            or not isinstance(claim_ids, list)
            or not claim_ids
            or any(not _receipt_entity_id(claim_id) for claim_id in claim_ids)
            or len(set(claim_ids)) != len(claim_ids)
            or not isinstance(states, dict)
            or set(states) != set(claim_ids)
            or any(state not in _CLAIM_STATES for state in states.values())
            or not isinstance(result.get("parked"), bool)
            or not isinstance(result.get("resumed"), bool)
            or not isinstance(result.get("timed_out"), bool)
            or (not aborted and result["parked"] == result["resumed"])
            or (result["resumed"] and any(state != "active" for state in states.values()))
            or (result["timed_out"] and result["resumed"])
        ):
            raise ValueError("task-park receipt result is malformed")
        return
    _validate_claim_result_shape(result)
    if action in {"claim.acquire", "freeze.acquire"}:
        expected_kind = "freeze" if action == "freeze.acquire" else "normal"
        if (
            result["kind"] != expected_kind
            or result["writes"] != parameters["writes"]
            or result["resources"] != parameters["resources"]
            or result["priority"] != parameters["priority"]
            or not isinstance(result.get("granted"), bool)
            or not isinstance(result.get("timed_out"), bool)
            or (result["granted"] and result["state"] != "active")
            or (result["timed_out"] and result["granted"])
        ):
            raise ValueError("claim-acquire receipt result is malformed")
    elif action == "claim.release":
        if result["id"] != parameters["claim_id"] or result["state"] not in {
            "released",
            "cancelled",
        }:
            raise ValueError("claim-release receipt result is malformed")
    elif result["id"] != parameters["claim_id"] or result["state"] != "cancelled":
        raise ValueError("queue-cancel receipt result is malformed")


def _validate_receipt_parameters(action: str, parameters: dict[str, Any]) -> None:
    expected_keys = {
        "workspace.register": {"workspace"},
        "workspace.unregister": {"workspace"},
        "task.start": {
            "owner",
            "summary",
            "token_file_path",
            "ttl_seconds",
            "workspace",
        },
        "task.heartbeat": {"note", "ttl_seconds", "workspace"},
        "task.park": {"requested_wait_seconds", "workspace"},
        "task.release": {"note", "result", "token_cleanup_path", "workspace"},
        "claim.acquire": {
            "keep_queued",
            "priority",
            "requested_wait_seconds",
            "resources",
            "workspace",
            "writes",
        },
        "freeze.acquire": {
            "keep_queued",
            "priority",
            "requested_wait_seconds",
            "resources",
            "workspace",
            "writes",
        },
        "claim.release": {"claim_id", "workspace"},
        "queue.cancel": {"claim_id", "workspace"},
        "recovery.resolve": {"evidence", "resolution", "task_id", "workspace"},
    }[action]
    if set(parameters) != expected_keys:
        raise ValueError("receipt parameter keys are not canonical")
    workspace = parameters["workspace"]
    if (
        not _receipt_text(workspace)
        or not os.path.isabs(workspace)
        or os.path.normpath(workspace) != workspace
    ):
        raise ValueError("receipt workspace is malformed")
    if action == "task.start":
        if (
            not _receipt_user_text(parameters["owner"])
            or not _receipt_user_text(parameters["summary"])
            or parameters["owner"] != parameters["owner"].strip()
            or parameters["summary"] != parameters["summary"].strip()
            or (
                parameters["token_file_path"] is not None
                and (
                    not _receipt_text(parameters["token_file_path"])
                    or not os.path.isabs(parameters["token_file_path"])
                    or os.path.normpath(parameters["token_file_path"])
                    != parameters["token_file_path"]
                )
            )
            or not _finite_receipt_number(parameters["ttl_seconds"], positive=True)
            or float(parameters["ttl_seconds"]) > MAX_TASK_TTL_SECONDS
        ):
            raise ValueError("task-start receipt parameters are malformed")
    elif action == "task.heartbeat":
        ttl = parameters["ttl_seconds"]
        if (
            parameters["note"] is not None
            and not _receipt_user_text(parameters["note"], nonempty=False)
        ) or (
            ttl is not None
            and (
                not _finite_receipt_number(ttl, positive=True) or float(ttl) > MAX_TASK_TTL_SECONDS
            )
        ):
            raise ValueError("task-heartbeat receipt parameters are malformed")
    elif action == "task.park":
        if (
            not _finite_receipt_number(parameters["requested_wait_seconds"])
            or float(parameters["requested_wait_seconds"]) > MAX_TASK_TTL_SECONDS
        ):
            raise ValueError("task-park receipt parameters are malformed")
    elif action == "task.release":
        cleanup_parameter = parameters["token_cleanup_path"]
        if (
            parameters["result"] not in {"completed", "failed", "outcome-unknown"}
            or (
                parameters["note"] is not None
                and not _receipt_user_text(parameters["note"], nonempty=False)
            )
            or (cleanup_parameter is not None and not _receipt_text(cleanup_parameter))
            or (
                parameters["result"] in {"completed", "failed"}
                and (
                    not isinstance(cleanup_parameter, str)
                    or not os.path.isabs(cleanup_parameter)
                    or os.path.normpath(cleanup_parameter) != cleanup_parameter
                )
            )
            or (parameters["result"] == "outcome-unknown" and cleanup_parameter is not None)
        ):
            raise ValueError("task-release receipt parameters are malformed")
    elif action in {"claim.acquire", "freeze.acquire"}:
        writes = parameters["writes"]
        resources = parameters["resources"]
        if (
            not isinstance(parameters["keep_queued"], bool)
            or parameters["priority"] not in {"normal", "urgent"}
            or not _finite_receipt_number(parameters["requested_wait_seconds"])
            or float(parameters["requested_wait_seconds"]) > MAX_TASK_TTL_SECONDS
            or not isinstance(writes, list)
            or not isinstance(resources, list)
            or any(not _is_canonical_write_scope(value) for value in writes)
            or any(
                not _receipt_text(value) or value.strip().casefold() != value for value in resources
            )
            or writes != sorted(set(writes))
            or resources != sorted(set(resources))
        ):
            raise ValueError("claim receipt parameters are malformed")
        if action == "freeze.acquire":
            if writes or resources:
                raise ValueError("freeze receipt includes scopes")
        elif parameters["priority"] != "normal" or not (writes or resources):
            raise ValueError("normal claim receipt parameters are malformed")
    elif action in {"claim.release", "queue.cancel"}:
        if not _receipt_text(parameters["claim_id"]):
            raise ValueError("claim identity is malformed")
    elif action == "recovery.resolve" and (
        parameters["resolution"] not in {"completed", "failed"}
        or not _receipt_user_text(parameters["evidence"])
        or not _receipt_text(parameters["task_id"])
    ):
        raise ValueError("recovery receipt parameters are malformed")


def _terminal_task_matches_start_proof(
    task: sqlite3.Row | None,
    *,
    task_id: str,
    workspace_id: str,
    owner_token_hash: str,
    token_file_identity: str,
    terminal: dict[str, Any],
) -> bool:
    return bool(
        task is not None
        and task["id"] == task_id
        and task["workspace_id"] == workspace_id
        and task["token_hash"] == owner_token_hash
        and task["token_file_identity"] == token_file_identity
        and task["state"] == terminal["terminal_state"]
        and task["result"] == terminal["terminal_result"]
        and task["finished_at"] == terminal["terminal_finished_at"]
    )


def _validate_terminal_task_start_obligation(
    connection: sqlite3.Connection,
    row: sqlite3.Row,
    parameters: dict[str, Any],
    terminal: dict[str, Any],
) -> None:
    """Prove a path-bound terminal start cannot outlive its token fence."""

    declared_token_path = parameters["token_file_path"]
    task = connection.execute(
        "SELECT * FROM tasks WHERE id = ?",
        (row["task_id"],),
    ).fetchone()
    token_file_identity = (
        _stored_token_path_identity(declared_token_path)
        if declared_token_path is not None
        else (task["token_file_identity"] if task is not None else None)
    )
    if token_file_identity is None:
        if terminal.get("token_cleanup_completed") is not True and not (
            task is not None
            and task["state"] == "outcome_unknown"
            and task["workspace_id"] == row["workspace_id"]
            and task["token_hash"] == row["owner_token_hash"]
            and task["result"] == terminal["terminal_result"]
            and task["finished_at"] == terminal["terminal_finished_at"]
        ):
            raise ValueError("tokenless terminal task-start proof lacks completion marker")
        if task is not None and (
            task["workspace_id"] != row["workspace_id"]
            or task["token_hash"] != row["owner_token_hash"]
            or task["token_file_path"] is not None
            or task["token_file_identity"] is not None
            or task["state"] != terminal["terminal_state"]
            or task["result"] != terminal["terminal_result"]
            or task["finished_at"] != terminal["terminal_finished_at"]
        ):
            raise ValueError("tokenless terminal task-start proof changed task identity")
        return
    if not isinstance(token_file_identity, str):
        raise TypeError("terminal task-start token identity is malformed")
    if (
        terminal.get("token_cleanup_completed") is True
        and task is not None
        and task["workspace_id"] == row["workspace_id"]
        and task["token_hash"] == row["owner_token_hash"]
        and task["token_file_path"] is None
        and task["token_file_identity"] is None
        and task["state"] == terminal["terminal_state"]
        and task["result"] == terminal["terminal_result"]
        and task["finished_at"] == terminal["terminal_finished_at"]
    ):
        outstanding = connection.execute(
            "SELECT 1 FROM token_cleanup_jobs WHERE task_id = ? "
            "UNION ALL SELECT 1 FROM operation_receipts "
            "WHERE task_id = ? AND token_cleanup_path IS NOT NULL LIMIT 1",
            (row["task_id"], row["task_id"]),
        ).fetchone()
        if outstanding is None:
            return
    task_matches = _terminal_task_matches_start_proof(
        task,
        task_id=str(row["task_id"]),
        workspace_id=str(row["workspace_id"]),
        owner_token_hash=str(row["owner_token_hash"]),
        token_file_identity=token_file_identity,
        terminal=terminal,
    )
    if terminal.get("token_cleanup_completed") is True:
        if task is not None and (not task_matches or task["state"] == "outcome_unknown"):
            raise ValueError("completed task-start cleanup proof changed task identity")
        outstanding = connection.execute(
            "SELECT 1 FROM token_cleanup_jobs WHERE task_id = ? "
            "UNION ALL SELECT 1 FROM operation_receipts "
            "WHERE task_id = ? AND token_cleanup_path IS NOT NULL LIMIT 1",
            (row["task_id"], row["task_id"]),
        ).fetchone()
        if outstanding is not None:
            raise ValueError("completed task-start cleanup proof retains an obligation")
        return
    if not task_matches:
        raise ValueError("terminal task-start proof lost its exact task identity")
    if task["state"] == "outcome_unknown":
        return
    expected_job_reason = {
        "task-ttl-expired": "claimless-task-expired",
        "task-recovery-resolved": "recovered-task-terminal",
    }.get(terminal["reason"])
    if expected_job_reason is not None:
        cleanup_job = connection.execute(
            "SELECT 1 FROM token_cleanup_jobs WHERE task_id = ? AND workspace_id = ? "
            "AND token_file_identity = ? AND token_hash = ? AND reason = ?",
            (
                row["task_id"],
                row["workspace_id"],
                token_file_identity,
                row["owner_token_hash"],
                expected_job_reason,
            ),
        ).fetchone()
        if cleanup_job is not None:
            return
    if terminal["reason"] == "task-released":
        cleanup_receipt = connection.execute(
            "SELECT 1 FROM operation_receipts WHERE action = 'task.release' "
            "AND task_id = ? AND workspace_id = ? AND owner_token_hash = ? "
            "AND token_cleanup_identity = ? AND token_cleanup_path IS NOT NULL LIMIT 1",
            (
                row["task_id"],
                row["workspace_id"],
                row["owner_token_hash"],
                token_file_identity,
            ),
        ).fetchone()
        if cleanup_receipt is not None:
            return
    raise ValueError("terminal task-start proof has no durable token cleanup obligation")


def _validate_release_cleanup_lineage(
    connection: sqlite3.Connection,
    row: sqlite3.Row,
    parameters: dict[str, Any],
    result: dict[str, Any],
) -> None:
    """Bind every pending release cleanup to its exact task and start receipt."""

    cleanup_path = str(row["token_cleanup_path"])
    cleanup_identity = str(row["token_cleanup_identity"])
    task = connection.execute(
        "SELECT * FROM tasks WHERE id = ?",
        (row["task_id"],),
    ).fetchone()
    if (
        task is None
        or task["workspace_id"] != row["workspace_id"]
        or task["token_hash"] != row["owner_token_hash"]
        or task["token_file_identity"] != cleanup_identity
        or task["state"] != result["state"]
        or task["result"] != result["result"]
        or task["finished_at"] != result["finished_at"]
    ):
        raise ValueError("release cleanup receipt lost its exact terminal task")
    start_receipts = connection.execute(
        "SELECT * FROM operation_receipts WHERE action = 'task.start' "
        "AND task_id = ? AND workspace_id = ? AND owner_token_hash = ?",
        (row["task_id"], row["workspace_id"], row["owner_token_hash"]),
    ).fetchall()
    if task["start_operation_id"] is None:
        if start_receipts:
            raise ValueError("legacy release cleanup has unexpected task-start lineage")
        return
    if len(start_receipts) != 1:
        raise ValueError("release cleanup receipt has no unique task-start lineage")
    start = start_receipts[0]
    start_parameters = parse_canonical_json(start["parameters_json"])
    start_terminal = parse_canonical_json(start["terminal_json"])
    if not isinstance(start_parameters, dict) or not isinstance(start_terminal, dict):
        raise TypeError("release cleanup receipt has malformed task-start lineage")
    start_path = start_parameters.get("token_file_path")
    if (
        start["operation_id"] != task["start_operation_id"]
        or (start_path is not None and _stored_token_path_identity(start_path) != cleanup_identity)
        or start_terminal
        != {
            "aborted": True,
            "reason": "task-released",
            "terminal_finished_at": result["finished_at"],
            "terminal_result": result["result"],
            "terminal_state": result["state"],
        }
        or start["retired_at"] is None
        or parameters["token_cleanup_path"] != cleanup_path
    ):
        raise ValueError("release cleanup receipt does not match task-start lineage")


def _validate_operation_receipts(connection: sqlite3.Connection, resolved: Path) -> None:
    invalid = 0
    for row in connection.execute(
        "SELECT operation_id, workspace_id, action, parameters_json, owner_token_hash, "
        "fingerprint, task_id, result_json, terminal_json, token_cleanup_path, created_at, "
        "token_cleanup_identity, "
        "delivered_at, retired_at, "
        "typeof(operation_id) AS operation_type, typeof(workspace_id) AS workspace_type, "
        "typeof(action) AS action_type, typeof(parameters_json) AS parameters_type, "
        "typeof(owner_token_hash) AS owner_type, typeof(fingerprint) AS fingerprint_type, "
        "typeof(task_id) AS task_id_type, "
        "typeof(result_json) AS result_type, typeof(terminal_json) AS terminal_type, "
        "typeof(token_cleanup_path) AS cleanup_type, "
        "typeof(token_cleanup_identity) AS cleanup_identity_type, "
        "finalized_at, typeof(created_at) AS created_type, "
        "typeof(finalized_at) AS finalized_type, "
        "typeof(delivered_at) AS delivered_type, typeof(retired_at) AS retired_type "
        "FROM operation_receipts"
    ):
        try:
            operation_id = validate_operation_id(row["operation_id"])
            del operation_id
            workspace_id = row["workspace_id"]
            action = row["action"]
            owner_token_hash = row["owner_token_hash"]
            if (
                row["operation_type"] != "text"
                or row["workspace_type"] != "text"
                or not is_sha256_hex(workspace_id)
                or row["action_type"] != "text"
                or action not in PUBLIC_MUTATION_ACTIONS
                or row["parameters_type"] != "text"
                or row["result_type"] != "text"
                or row["fingerprint_type"] != "text"
                or not is_sha256_hex(row["fingerprint"])
            ):
                raise ValueError("receipt identity is malformed")
            if action in _OWNER_AUTHENTICATED_ACTIONS:
                if row["owner_type"] != "text" or not is_sha256_hex(owner_token_hash):
                    raise ValueError("receipt owner identity is malformed")
            elif owner_token_hash is not None or row["owner_type"] != "null":
                raise ValueError("unauthenticated receipt has an owner identity")
            parameters = parse_canonical_json(row["parameters_json"])
            result = parse_canonical_json(row["result_json"])
            _validate_receipt_parameters(action, parameters)
            if workspace_id != _workspace_id(parameters["workspace"]):
                raise ValueError("receipt workspace identity does not match parameters")
            if not _ACTION_RESULT_REQUIRED_KEYS[action].issubset(result):
                raise ValueError("receipt result is incomplete")
            _validate_receipt_result(action, parameters, result)
            task_id = row["task_id"]
            expected_task_id = (
                result.get("id")
                if action in {"task.start", "task.heartbeat", "task.release", "recovery.resolve"}
                else result.get("task_id")
            )
            if action not in {"workspace.register", "workspace.unregister"}:
                if (
                    row["task_id_type"] != "text"
                    or not _receipt_entity_id(task_id)
                    or task_id != expected_task_id
                ):
                    raise ValueError("task-owned receipt identity is malformed")
            elif task_id is not None or row["task_id_type"] != "null":
                raise ValueError("non-lifecycle receipt has a task identity")
            terminal_json = row["terminal_json"]
            terminal: dict[str, Any] | None = None
            retired_at = row["retired_at"]
            if terminal_json is not None:
                terminal = parse_canonical_json(terminal_json)
                if row["terminal_type"] != "text" or retired_at is None:
                    raise ValueError("operation lifecycle proof is malformed")
                _validate_lifecycle_terminal_proof(
                    action,
                    terminal,
                    row["created_at"],
                    result,
                )
                if "resolution_reason" in terminal:
                    task = connection.execute(
                        "SELECT workspace_id, state, result, finished_at FROM tasks WHERE id = ?",
                        (task_id,),
                    ).fetchone()
                    if (
                        task is None
                        or task["workspace_id"] != workspace_id
                        or task["state"] != terminal["terminal_state"]
                        or task["result"] != terminal["terminal_result"]
                        or task["finished_at"] != terminal["terminal_finished_at"]
                    ):
                        raise ValueError(
                            "operation recovery proof does not match its terminal task"
                        )
                elif set(result).intersection(terminal):
                    raise ValueError("operation lifecycle proof collides with immutable result")
            elif row["terminal_type"] != "null":
                raise ValueError("receipt lifecycle proof has an invalid storage type")
            if retired_at is not None:
                retired_wait = action in {"claim.acquire", "freeze.acquire", "task.park"}
                if (
                    action not in LIFECYCLE_ACTIONS | {"task.start"}
                    or row["retired_type"] not in {"integer", "real"}
                    or not _finite_receipt_number(retired_at)
                    or row["finalized_at"] is None
                    or float(retired_at) < float(row["finalized_at"])
                    or (action == "task.start" and terminal_json is None)
                    or (action == "task.heartbeat" and terminal_json is None)
                    or (
                        action in LIFECYCLE_REVOCATION_ACTIONS | {"task.release"}
                        and terminal_json is None
                    )
                    or (
                        terminal_json is not None
                        and float(retired_at) < float(terminal["terminal_finished_at"])
                    )
                    or (
                        retired_wait
                        and terminal_json is None
                        and (
                            result.get("aborted") is not True
                            or result.get("reason") not in _TASK_WAIT_ABORT_REASONS
                            or row["token_cleanup_path"] is not None
                        )
                    )
                ):
                    raise ValueError("receipt retirement metadata is malformed")
            elif row["retired_type"] != "null" or terminal_json is not None:
                raise ValueError("receipt retirement metadata is incomplete")
            if (
                operation_fingerprint(
                    workspace_id,
                    action,
                    row["parameters_json"],
                    owner_token_hash,
                )
                != row["fingerprint"]
            ):
                raise ValueError("receipt fingerprint does not match")
            cleanup_path = row["token_cleanup_path"]
            cleanup_identity = row["token_cleanup_identity"]
            cleanup_required = action == "task.release" and parameters.get("result") in {
                "completed",
                "failed",
            }
            if cleanup_required and row["delivered_at"] is None and cleanup_path is None:
                raise ValueError("undelivered release receipt lost its cleanup path")
            if cleanup_required and cleanup_path is not None:
                if (
                    row["cleanup_type"] != "text"
                    or not isinstance(cleanup_path, str)
                    or not cleanup_path
                    or _has_control_characters(cleanup_path)
                    or not os.path.isabs(cleanup_path)
                    or os.path.normpath(cleanup_path) != cleanup_path
                    or cleanup_path != parameters.get("token_cleanup_path")
                    or row["cleanup_identity_type"] != "text"
                    or cleanup_identity != _stored_token_path_identity(cleanup_path)
                ):
                    raise ValueError("release receipt cleanup path is malformed")
            elif cleanup_required and (
                cleanup_identity is not None or row["cleanup_identity_type"] != "null"
            ):
                raise ValueError("delivered release receipt retained a cleanup identity")
            elif not cleanup_required and (
                cleanup_path is not None
                or row["cleanup_type"] != "null"
                or cleanup_identity is not None
                or row["cleanup_identity_type"] != "null"
            ):
                raise ValueError("receipt has an unexpected cleanup path")
            if action == "task.start" and terminal is not None:
                _validate_terminal_task_start_obligation(
                    connection,
                    row,
                    parameters,
                    terminal,
                )
            if cleanup_path is not None:
                _validate_release_cleanup_lineage(connection, row, parameters, result)
            if not _is_finite_numeric(row["created_at"], row["created_type"]):
                raise ValueError("receipt creation time is malformed")
            if row["finalized_at"] is not None:
                if not _is_finite_numeric(row["finalized_at"], row["finalized_type"]) or float(
                    row["finalized_at"]
                ) < float(row["created_at"]):
                    raise ValueError("receipt finalization time is malformed")
                if "aborted" in result or "reason" in result:
                    if (
                        action not in {"claim.acquire", "freeze.acquire", "task.park"}
                        or result.get("aborted") is not True
                        or result.get("reason") not in _TASK_WAIT_ABORT_REASONS
                        or result.get("timed_out") is not False
                    ):
                        raise ValueError("receipt expiry-abort result is malformed")
                    if action in {"claim.acquire", "freeze.acquire"}:
                        if result.get("granted") is not False:
                            raise ValueError("aborted claim receipt remains granted")
                        state = result.get("state")
                        expected_states = {
                            "task-ttl-expired": {"cancelled"},
                            "task-ttl-expired-with-active-claim": {
                                "active",
                                "cancelled",
                                "parked",
                            },
                            "task-released": {"released"},
                            "task-released-outcome-unknown": {
                                "active",
                                "cancelled",
                                "parked",
                            },
                            "task-recovery-resolved": {"released"},
                        }[result["reason"]]
                        if state not in expected_states:
                            raise ValueError("aborted claim receipt has an invalid state")
                    elif (
                        result.get("resumed") is not False
                        or not isinstance(result.get("parked"), bool)
                        or result["parked"]
                        != any(state == "parked" for state in result["states"].values())
                    ):
                        raise ValueError("aborted park receipt is malformed")
                    else:
                        expected_states = {
                            "task-ttl-expired": {"cancelled"},
                            "task-ttl-expired-with-active-claim": {
                                "active",
                                "cancelled",
                                "parked",
                            },
                            "task-released": {"released"},
                            "task-released-outcome-unknown": {
                                "active",
                                "cancelled",
                                "parked",
                            },
                            "task-recovery-resolved": {"released"},
                        }[result["reason"]]
                        if any(state not in expected_states for state in result["states"].values()):
                            raise ValueError("aborted park receipt has invalid claim states")
            elif action not in {"claim.acquire", "freeze.acquire", "task.park"}:
                raise ValueError("non-waiting receipt is not finalized")
            elif (
                "aborted" in result
                or "reason" in result
                or float(parameters["requested_wait_seconds"]) <= 0
            ):
                raise ValueError("pending receipt has invalid wait or abort metadata")
            elif action in {"claim.acquire", "freeze.acquire"} and (
                result.get("state") != "queued"
                or result.get("granted") is not False
                or result.get("timed_out") is not False
            ):
                raise ValueError("pending claim receipt has a terminal result")
            elif action == "task.park" and (
                result.get("parked") is not True
                or result.get("resumed") is not False
                or result.get("timed_out") is not False
            ):
                raise ValueError("pending park receipt has a terminal result")
            if row["delivered_at"] is not None and not _is_finite_numeric(
                row["delivered_at"], row["delivered_type"]
            ):
                raise ValueError("receipt delivery time is malformed")
            if row["delivered_at"] is not None and (
                row["finalized_at"] is None
                or float(row["delivered_at"]) < float(row["finalized_at"])
            ):
                raise ValueError("pending receipt was delivered")
        except (KeyError, TypeError, ValueError, UsageError, json.JSONDecodeError):
            invalid += 1
    if invalid:
        _semantic_failure(
            "Scheduler operation receipts are malformed or internally inconsistent.",
            resolved,
            "operation-receipt-invalid",
            receipt_count=invalid,
        )


def _validate_task_start_bindings(connection: sqlite3.Connection, resolved: Path) -> None:
    invalid = 0
    linked_tasks: set[str] = set()
    for row in connection.execute(
        "SELECT task.id AS task_id, task.workspace_id AS task_workspace_id, "
        "task.owner AS task_owner, task.summary AS task_summary, "
        "task.token_hash AS task_token_hash, task.token_file_path AS task_token_file_path, "
        "task.token_file_identity AS task_token_file_identity, "
        "task.state AS task_state, task.created_at AS task_created_at, task.start_operation_id, "
        "typeof(task.start_operation_id) AS start_operation_type, workspace.root, "
        "receipt.operation_id, receipt.workspace_id AS receipt_workspace_id, "
        "receipt.action, receipt.owner_token_hash, receipt.task_id AS receipt_task_id, "
        "receipt.parameters_json, receipt.result_json, receipt.terminal_json "
        "FROM tasks AS task "
        "JOIN workspaces AS workspace ON workspace.id = task.workspace_id "
        "LEFT JOIN operation_receipts AS receipt "
        "ON receipt.operation_id = task.start_operation_id "
        "WHERE task.start_operation_id IS NOT NULL"
    ):
        try:
            validate_operation_id(row["start_operation_id"])
            parameters = parse_canonical_json(row["parameters_json"])
            result = parse_canonical_json(row["result_json"])
            terminal = (
                parse_canonical_json(row["terminal_json"])
                if row["terminal_json"] is not None
                else None
            )
            declared_path = parameters["token_file_path"]
            cleanup_cleared = (
                isinstance(terminal, dict)
                and terminal.get("token_cleanup_completed") is True
                and terminal.get("terminal_state") == row["task_state"]
                and terminal.get("terminal_result")
                in {row["task_state"], f"recovered-{row['task_state']}"}
                and row["task_token_file_path"] is None
                and row["task_token_file_identity"] is None
            )
            if (
                row["start_operation_type"] != "text"
                or row["operation_id"] != row["start_operation_id"]
                or row["action"] != "task.start"
                or row["receipt_task_id"] != row["task_id"]
                or row["receipt_workspace_id"] != row["task_workspace_id"]
                or row["owner_token_hash"] != row["task_token_hash"]
                or result["id"] != row["task_id"]
                or result["owner"] != row["task_owner"]
                or result["summary"] != row["task_summary"]
                or result["created_at"] != row["task_created_at"]
                or parameters["workspace"] != row["root"]
                or parameters["owner"] != row["task_owner"]
                or parameters["summary"] != row["task_summary"]
                or (
                    declared_path is not None
                    and not cleanup_cleared
                    and (
                        row["task_token_file_path"] != declared_path
                        or row["task_token_file_identity"]
                        != _stored_token_path_identity(declared_path)
                    )
                )
                or (
                    declared_path is None
                    and row["task_state"] in {"active", "outcome_unknown"}
                    and (
                        row["task_token_file_path"] is not None
                        or row["task_token_file_identity"] is not None
                    )
                )
            ):
                raise ValueError("task and start receipt binding changed")
            linked_tasks.add(str(row["task_id"]))
        except (KeyError, TypeError, ValueError, UsageError, json.JSONDecodeError):
            invalid += 1
    reverse_mismatch = _scalar(
        connection,
        "SELECT COUNT(*) FROM operation_receipts AS receipt "
        "LEFT JOIN tasks AS task ON task.id = receipt.task_id "
        "WHERE receipt.action = 'task.start' "
        "AND ((receipt.terminal_json IS NULL "
        "AND (task.id IS NULL OR task.start_operation_id IS NULL "
        "OR task.start_operation_id <> receipt.operation_id OR task.state != 'active')) "
        "OR (receipt.terminal_json IS NOT NULL AND task.id IS NOT NULL "
        "AND (task.start_operation_id IS NULL "
        "OR task.start_operation_id <> receipt.operation_id)))",
    )
    linked_count = _scalar(
        connection,
        "SELECT COUNT(*) FROM tasks WHERE start_operation_id IS NOT NULL",
    )
    if invalid or reverse_mismatch or len(linked_tasks) != linked_count:
        _semantic_failure(
            "Scheduler tasks are not bound to their durable task-start receipts.",
            resolved,
            "operation-receipt-invalid",
            task_binding_count=invalid,
            reverse_binding_count=reverse_mismatch,
        )


def _validate_capacity_invariants(
    connection: sqlite3.Connection,
    resolved: Path,
    *,
    allow_legacy_overflow: bool = False,
) -> None:
    replay_required = _scalar(
        connection,
        "SELECT COUNT(*) FROM operation_receipts WHERE delivered_at IS NULL AND retired_at IS NULL",
    )
    registered_workspaces = _scalar(connection, "SELECT COUNT(*) FROM workspaces")
    active_tasks = _scalar(connection, "SELECT COUNT(*) FROM tasks WHERE state = 'active'")
    outcome_unknown_tasks = _scalar(
        connection,
        "SELECT COUNT(*) FROM tasks WHERE state = 'outcome_unknown'",
    )
    open_claims = _scalar(
        connection,
        "SELECT COUNT(*) FROM claims WHERE state IN ('queued', 'active', 'parked')",
    )
    cleanup_jobs = _scalar(connection, "SELECT COUNT(*) FROM token_cleanup_jobs")
    cleanup_receipts = _scalar(
        connection,
        "SELECT COUNT(*) FROM operation_receipts WHERE token_cleanup_path IS NOT NULL",
    )
    terminal_claims = _scalar(
        connection,
        "SELECT COUNT(*) FROM claims WHERE state IN ('released', 'cancelled')",
    )
    open_tasks = active_tasks + outcome_unknown_tasks
    cleanup_obligations = cleanup_jobs + cleanup_receipts + open_tasks
    reserved_capacity = (
        replay_required
        + registered_workspaces
        + (3 * active_tasks)
        + (2 * outcome_unknown_tasks)
        + open_claims
        + cleanup_jobs
        + cleanup_receipts
    )
    if not allow_legacy_overflow and cleanup_obligations > TOKEN_CLEANUP_BACKLOG_LIMIT:
        _semantic_failure(
            "Scheduler token cleanup obligations exceed the durable admission bound.",
            resolved,
            "token-cleanup-backlog-invalid",
            token_cleanup_obligations=cleanup_obligations,
            limit=TOKEN_CLEANUP_BACKLOG_LIMIT,
        )
    if terminal_claims > TERMINAL_CLAIM_RETENTION:
        _semantic_failure(
            "Scheduler terminal claim history exceeds its durable retention bound.",
            resolved,
            "terminal-claim-retention-invalid",
            terminal_claims=terminal_claims,
            limit=TERMINAL_CLAIM_RETENTION,
        )
    if not allow_legacy_overflow and reserved_capacity > REPLAY_REQUIRED_OPERATION_LIMIT:
        _semantic_failure(
            "Scheduler operation reservations exceed the durable admission bound.",
            resolved,
            "operation-receipt-backlog-invalid",
            reserved_capacity=reserved_capacity,
            limit=REPLAY_REQUIRED_OPERATION_LIMIT,
        )


def _validate_token_cleanup_jobs(connection: sqlite3.Connection, resolved: Path) -> None:
    invalid = 0
    path_identities: set[str] = set()
    job_identities: list[tuple[str, str]] = []
    for row in connection.execute(
        "SELECT job.*, typeof(job.task_id) AS task_type, "
        "typeof(job.workspace_id) AS workspace_type, "
        "typeof(job.token_file_path) AS path_type, "
        "typeof(job.token_file_identity) AS path_identity_type, "
        "typeof(job.token_hash) AS hash_type, typeof(job.reason) AS reason_type, "
        "typeof(job.created_at) AS created_type, "
        "typeof(job.completed_at) AS completed_type, "
        "typeof(job.last_attempt_at) AS attempt_time_type, "
        "typeof(job.attempt_count) AS attempt_count_type, "
        "task.workspace_id AS task_workspace_id, task.token_hash AS task_token_hash, "
        "task.token_file_path AS task_token_file_path, "
        "task.token_file_identity AS task_token_file_identity, task.state AS task_state, "
        "task.result AS task_result, task.finished_at AS task_finished_at, "
        "typeof(task.finished_at) AS task_finished_type, "
        "start.workspace_id AS start_workspace_id, start.owner_token_hash AS start_token_hash, "
        "start.parameters_json AS start_parameters_json, start.terminal_json AS start_terminal_json, "
        "start.retired_at AS start_retired_at, "
        "(SELECT COUNT(*) FROM operation_receipts AS receipt_count "
        "WHERE receipt_count.action = 'task.start' AND receipt_count.task_id = job.task_id) "
        "AS start_receipt_count "
        "FROM token_cleanup_jobs AS job "
        "LEFT JOIN tasks AS task ON task.id = job.task_id "
        "LEFT JOIN operation_receipts AS start "
        "ON start.action = 'task.start' AND start.task_id = job.task_id"
    ):
        path = row["token_file_path"]
        path_identity = _stored_token_path_identity(path) if isinstance(path, str) else ""
        created_valid = _is_finite_numeric(row["created_at"], row["created_type"])
        completed_valid = row["completed_at"] is None or _is_finite_numeric(
            row["completed_at"], row["completed_type"]
        )
        attempt_time_valid = row["last_attempt_at"] is None or _is_finite_numeric(
            row["last_attempt_at"], row["attempt_time_type"]
        )
        finished_valid = _is_finite_numeric(row["task_finished_at"], row["task_finished_type"])
        try:
            start_parameters = parse_canonical_json(row["start_parameters_json"])
        except (TypeError, ValueError, UsageError, json.JSONDecodeError):
            start_parameters = None
        try:
            start_terminal = parse_canonical_json(row["start_terminal_json"])
        except (TypeError, ValueError, UsageError, json.JSONDecodeError):
            start_terminal = None
        valid_task_transition = (
            row["reason"] == "claimless-task-expired"
            and row["task_state"] == "expired"
            and row["task_result"] == "expired"
        ) or (
            row["reason"] == "recovered-task-terminal"
            and row["task_state"] in {"completed", "failed"}
            and row["task_result"] == f"recovered-{row['task_state']}"
        )
        expected_start_terminal = {
            "aborted": True,
            "reason": (
                "task-ttl-expired"
                if row["reason"] == "claimless-task-expired"
                else "task-recovery-resolved"
            ),
            "terminal_finished_at": row["task_finished_at"],
            "terminal_result": row["task_result"],
            "terminal_state": row["task_state"],
        }
        if (
            row["task_type"] != "text"
            or not _receipt_entity_id(row["task_id"])
            or row["workspace_type"] != "text"
            or not is_sha256_hex(row["workspace_id"])
            or row["path_type"] != "text"
            or not isinstance(path, str)
            or not path
            or _has_control_characters(path)
            or not os.path.isabs(path)
            or os.path.normpath(path) != path
            or row["path_identity_type"] != "text"
            or row["token_file_identity"] != path_identity
            or not path_identity
            or path_identity in path_identities
            or row["hash_type"] != "text"
            or not is_sha256_hex(row["token_hash"])
            or row["reason_type"] != "text"
            or not valid_task_transition
            or not created_valid
            or not completed_valid
            or not attempt_time_valid
            or (
                row["last_attempt_at"] is not None
                and float(row["last_attempt_at"]) < float(row["created_at"])
            )
            or row["attempt_count_type"] != "integer"
            or isinstance(row["attempt_count"], bool)
            or not isinstance(row["attempt_count"], int)
            or row["attempt_count"] < 0
            or (
                row["completed_at"] is not None
                and float(row["completed_at"]) < float(row["created_at"])
            )
            or row["task_workspace_id"] != row["workspace_id"]
            or row["task_token_hash"] != row["token_hash"]
            or row["task_token_file_path"] != path
            or row["task_token_file_identity"] != path_identity
            or not finished_valid
            or float(row["created_at"]) < float(row["task_finished_at"])
            or row["start_receipt_count"] != 1
            or row["start_workspace_id"] != row["workspace_id"]
            or row["start_token_hash"] != row["token_hash"]
            or not isinstance(start_parameters, dict)
            or start_parameters.get("token_file_path") != path
            or start_terminal != expected_start_terminal
            or row["start_retired_at"] is None
        ):
            invalid += 1
        path_identities.add(path_identity)
        if path_identity and is_sha256_hex(row["token_hash"]):
            job_identities.append((path_identity, str(row["token_hash"])))

    cleanup_receipt_identities = [
        (str(row["token_cleanup_identity"]), str(row["owner_token_hash"]))
        for row in connection.execute(
            "SELECT token_cleanup_identity, owner_token_hash FROM operation_receipts "
            "WHERE token_cleanup_path IS NOT NULL"
        )
    ]
    open_task_identities = [
        (str(row["token_file_identity"]), str(row["token_hash"]))
        for row in connection.execute(
            "SELECT token_file_identity, token_hash FROM tasks "
            "WHERE state IN ('active', 'outcome_unknown') "
            "AND token_file_identity IS NOT NULL"
        )
    ]
    seen_paths: set[str] = set()
    seen_hashes: set[str] = set()
    cleanup_collisions = 0
    open_task_collisions = 0
    for category, identities in (
        ("job", job_identities),
        ("receipt", cleanup_receipt_identities),
        ("task", open_task_identities),
    ):
        for path_identity, token_hash in identities:
            collision = path_identity in seen_paths or token_hash in seen_hashes
            if collision and category == "receipt":
                cleanup_collisions += 1
            elif collision and category == "task":
                open_task_collisions += 1
            elif collision:
                invalid += 1
            seen_paths.add(path_identity)
            seen_hashes.add(token_hash)
    if invalid or cleanup_collisions or open_task_collisions:
        _semantic_failure(
            "Scheduler state contains malformed token cleanup jobs.",
            resolved,
            "token-cleanup-job-invalid",
            job_count=invalid,
            receipt_collision_count=cleanup_collisions,
            open_task_collision_count=open_task_collisions,
        )


def _validate_semantics(
    connection: sqlite3.Connection,
    resolved: Path,
    schema_version: int,
    *,
    allow_legacy_capacity_overflow: bool = False,
) -> tuple[dict[str, int], dict[str, int]]:
    _validate_identifiers(connection, resolved)
    orphan_references = {
        "tasks.workspace_id": _scalar(
            connection,
            "SELECT COUNT(*) FROM tasks AS task "
            "LEFT JOIN workspaces AS workspace ON workspace.id = task.workspace_id "
            "WHERE workspace.id IS NULL",
        ),
        "claims.workspace_id": _scalar(
            connection,
            "SELECT COUNT(*) FROM claims AS claim "
            "LEFT JOIN workspaces AS workspace ON workspace.id = claim.workspace_id "
            "WHERE workspace.id IS NULL",
        ),
        "claims.task_id": _scalar(
            connection,
            "SELECT COUNT(*) FROM claims AS claim "
            "LEFT JOIN tasks AS task ON task.id = claim.task_id WHERE task.id IS NULL",
        ),
        "claim_scopes.claim_id": _scalar(
            connection,
            "SELECT COUNT(*) FROM claim_scopes AS scope "
            "LEFT JOIN claims AS claim ON claim.id = scope.claim_id WHERE claim.id IS NULL",
        ),
        "recovery_events.workspace_id": _scalar(
            connection,
            "SELECT COUNT(*) FROM recovery_events AS event "
            "LEFT JOIN workspaces AS workspace ON workspace.id = event.workspace_id "
            "WHERE workspace.id IS NULL",
        ),
        "recovery_events.task_id": _scalar(
            connection,
            "SELECT COUNT(*) FROM recovery_events AS event "
            "LEFT JOIN tasks AS task ON task.id = event.task_id WHERE task.id IS NULL",
        ),
    }
    orphan_references = {
        reference: count for reference, count in orphan_references.items() if count
    }
    if orphan_references:
        _semantic_failure(
            "Scheduler state contains orphan relational references.",
            resolved,
            "relational-orphan-invalid",
            references=orphan_references,
        )
    invalid_workspace_identities = 0
    for row in connection.execute(
        "SELECT id, root, typeof(root) AS root_storage_type FROM workspaces"
    ):
        root = row["root"]
        valid_root = (
            row["root_storage_type"] == "text"
            and isinstance(root, str)
            and bool(root)
            and not _has_control_characters(root)
            and os.path.isabs(root)
            and os.path.normpath(root) == root
            and os.path.abspath(root) == root
        )
        expected_id = _workspace_identity(root, schema_version) if valid_root else None
        if not valid_root or row["id"] != expected_id:
            invalid_workspace_identities += 1
    if invalid_workspace_identities:
        _semantic_failure(
            "Scheduler workspaces have non-canonical roots or mismatched identities.",
            resolved,
            "workspace-identity-invalid",
            workspace_count=invalid_workspace_identities,
        )

    task_state_counts = _group_counts(connection, "tasks", "state")
    invalid_task_states = {
        state: count for state, count in task_state_counts.items() if state not in _TASK_STATES
    }
    if invalid_task_states:
        _semantic_failure(
            "Scheduler state contains unknown task states.",
            resolved,
            "task-state-invalid",
            states=invalid_task_states,
        )

    expected_results = {
        "outcome_unknown": {"outcome-unknown", "expired-with-active-claim"},
        "completed": {"completed", "recovered-completed"},
        "failed": {"failed", "recovered-failed"},
        "expired": {"expired"},
    }
    invalid_task_lifecycles = 0
    for row in connection.execute(
        "SELECT state, result, finished_at, typeof(finished_at) AS finished_type FROM tasks"
    ):
        state = row["state"]
        if state == "active":
            if row["result"] is not None or row["finished_at"] is not None:
                invalid_task_lifecycles += 1
            continue
        if row["result"] not in expected_results.get(state, set()) or not _is_finite_numeric(
            row["finished_at"], row["finished_type"]
        ):
            invalid_task_lifecycles += 1
    if invalid_task_lifecycles:
        _semantic_failure(
            "Scheduler tasks have invalid lifecycle result or completion evidence.",
            resolved,
            "task-lifecycle-invalid",
            task_count=invalid_task_lifecycles,
        )

    invalid_task_timings = 0
    for row in connection.execute(
        "SELECT state, created_at, heartbeat_at, expires_at, "
        "typeof(created_at) AS created_type, typeof(heartbeat_at) AS heartbeat_type, "
        "typeof(expires_at) AS expiry_type FROM tasks"
    ):
        valid_created = _is_finite_numeric(row["created_at"], row["created_type"])
        if schema_version >= LEGACY_SCHEMA_TWO_VERSION and row["state"] == "outcome_unknown":
            valid_heartbeat = True
            valid_expiry = True
        elif schema_version == 1 and row["state"] in _OPEN_TASK_STATES:
            valid_heartbeat = row["heartbeat_type"] in {"integer", "real"}
            valid_expiry = row["expiry_type"] in {"integer", "real"}
        else:
            valid_heartbeat = _is_finite_numeric(row["heartbeat_at"], row["heartbeat_type"])
            valid_expiry = _is_finite_numeric(row["expires_at"], row["expiry_type"])
        if not (valid_created and valid_heartbeat and valid_expiry):
            invalid_task_timings += 1
    if invalid_task_timings:
        _semantic_failure(
            "Scheduler tasks have invalid timing metadata.",
            resolved,
            "task-timing-invalid",
            task_count=invalid_task_timings,
        )

    invalid_open_task_tokens = _scalar(
        connection,
        "SELECT COUNT(*) FROM tasks WHERE state IN ('active', 'outcome_unknown') "
        "AND (typeof(token_hash) != 'text' OR length(token_hash) != 64 "
        "OR token_hash GLOB '*[^0-9a-f]*')",
    )
    if invalid_open_task_tokens:
        _semantic_failure(
            "Open scheduler tasks have malformed owner-token hashes.",
            resolved,
            "open-task-token-invalid",
            task_count=invalid_open_task_tokens,
        )
    duplicate_open_task_tokens = _scalar(
        connection,
        "SELECT COUNT(*) FROM (SELECT token_hash FROM tasks "
        "WHERE state IN ('active', 'outcome_unknown') "
        "GROUP BY token_hash HAVING COUNT(*) > 1)",
    )
    if duplicate_open_task_tokens:
        _semantic_failure(
            "Open scheduler tasks share an owner-token hash.",
            resolved,
            "open-task-token-duplicate",
            duplicate_group_count=duplicate_open_task_tokens,
        )

    if schema_version == SCHEMA_VERSION:
        invalid_token_paths = 0
        open_path_owners: dict[str, str] = {}
        duplicate_open_paths = 0
        for row in connection.execute(
            "SELECT id, state, token_file_path, token_file_identity, "
            "typeof(token_file_path) AS token_path_type, "
            "typeof(token_file_identity) AS token_identity_type FROM tasks"
        ):
            token_path = row["token_file_path"]
            token_identity = row["token_file_identity"]
            if token_path is None:
                if token_identity is not None or row["token_identity_type"] != "null":
                    invalid_token_paths += 1
                continue
            if (
                row["token_path_type"] != "text"
                or not isinstance(token_path, str)
                or not token_path
                or _has_control_characters(token_path)
                or not os.path.isabs(token_path)
                or os.path.normpath(token_path) != token_path
                or row["token_identity_type"] != "text"
                or token_identity != _stored_token_path_identity(token_path)
            ):
                invalid_token_paths += 1
                continue
            if row["state"] in _OPEN_TASK_STATES:
                if token_identity in open_path_owners:
                    duplicate_open_paths += 1
                else:
                    open_path_owners[str(token_identity)] = str(row["id"])
        if invalid_token_paths:
            _semantic_failure(
                "Scheduler tasks have malformed token-file paths.",
                resolved,
                "task-token-path-invalid",
                task_count=invalid_token_paths,
            )
        if duplicate_open_paths:
            _semantic_failure(
                "Open scheduler tasks share a token-file path.",
                resolved,
                "open-task-token-path-duplicate",
                duplicate_group_count=duplicate_open_paths,
            )

    invalid_unknown_timings = 0
    for row in connection.execute(
        "SELECT heartbeat_at, expires_at, typeof(heartbeat_at) AS heartbeat_type, "
        "typeof(expires_at) AS expiry_type FROM tasks WHERE state = 'outcome_unknown'"
    ):
        try:
            heartbeat_at = float(row["heartbeat_at"])
            expires_at = float(row["expires_at"])
        except (TypeError, ValueError, OverflowError):
            invalid_unknown_timings += 1
            continue
        if (
            row["heartbeat_type"] not in {"integer", "real"}
            or row["expiry_type"] not in {"integer", "real"}
            or not math.isfinite(heartbeat_at)
            or not math.isfinite(expires_at)
        ):
            invalid_unknown_timings += 1
    if schema_version >= LEGACY_SCHEMA_TWO_VERSION and invalid_unknown_timings:
        _semantic_failure(
            "Outcome-unknown tasks must preserve finite numeric timing evidence.",
            resolved,
            "outcome-unknown-timing-invalid",
            task_count=invalid_unknown_timings,
        )

    invalid_recovery_resolutions = _scalar(
        connection,
        "SELECT COUNT(*) FROM recovery_events "
        "WHERE typeof(resolution) != 'text' "
        "OR resolution NOT IN ('completed', 'failed')",
    )
    if invalid_recovery_resolutions:
        _semantic_failure(
            "Scheduler recovery events contain invalid resolutions.",
            resolved,
            "recovery-event-resolution-invalid",
            event_count=invalid_recovery_resolutions,
        )

    invalid_recovery_evidence = 0
    invalid_recovery_timings = 0
    for row in connection.execute(
        "SELECT evidence, created_at, typeof(evidence) AS evidence_type, "
        "typeof(created_at) AS created_type FROM recovery_events"
    ):
        evidence = row["evidence"]
        if row["evidence_type"] != "text" or not _is_normalized_recovery_evidence(evidence):
            invalid_recovery_evidence += 1
        if not _is_finite_numeric(row["created_at"], row["created_type"]):
            invalid_recovery_timings += 1
    if invalid_recovery_evidence:
        _semantic_failure(
            "Scheduler recovery events contain malformed evidence.",
            resolved,
            "recovery-event-evidence-invalid",
            event_count=invalid_recovery_evidence,
        )
    if invalid_recovery_timings:
        _semantic_failure(
            "Scheduler recovery events contain invalid timing evidence.",
            resolved,
            "recovery-event-timing-invalid",
            event_count=invalid_recovery_timings,
        )

    recovery_workspace_mismatches = _scalar(
        connection,
        "SELECT COUNT(*) FROM recovery_events AS event "
        "JOIN tasks AS task ON task.id = event.task_id "
        "WHERE event.workspace_id != task.workspace_id",
    )
    if recovery_workspace_mismatches:
        _semantic_failure(
            "Recovery events do not belong to their task's workspace.",
            resolved,
            "recovery-event-workspace-mismatch",
            event_count=recovery_workspace_mismatches,
        )

    duplicate_recovery_events = _scalar(
        connection,
        "SELECT COUNT(*) FROM (SELECT task_id FROM recovery_events "
        "GROUP BY task_id HAVING COUNT(*) > 1)",
    )
    if duplicate_recovery_events:
        _semantic_failure(
            "Scheduler tasks have more than one recovery event.",
            resolved,
            "recovery-event-duplicate",
            task_count=duplicate_recovery_events,
        )

    recovery_task_mismatches = _scalar(
        connection,
        "SELECT COUNT(*) FROM recovery_events AS event "
        "JOIN tasks AS task ON task.id = event.task_id "
        "WHERE NOT ((event.resolution = 'completed' "
        "AND task.state = 'completed' AND task.result = 'recovered-completed') "
        "OR (event.resolution = 'failed' "
        "AND task.state = 'failed' AND task.result = 'recovered-failed'))",
    )
    if recovery_task_mismatches:
        _semantic_failure(
            "Recovery events do not match their task's recovered result.",
            resolved,
            "recovery-event-task-mismatch",
            event_count=recovery_task_mismatches,
        )

    recovery_binding_mismatches = _scalar(
        connection,
        "SELECT COUNT(*) FROM recovery_events AS event "
        "JOIN tasks AS task ON task.id = event.task_id "
        "WHERE task.note IS NULL OR task.finished_at IS NULL "
        "OR task.note != event.evidence OR task.finished_at != event.created_at",
    )
    if recovery_binding_mismatches:
        _semantic_failure(
            "Recovery events do not match their task's evidence and completion time.",
            resolved,
            "recovery-event-binding-invalid",
            event_count=recovery_binding_mismatches,
        )

    recovered_tasks_without_events = _scalar(
        connection,
        "SELECT COUNT(*) FROM tasks AS task "
        "WHERE task.result IN ('recovered-completed', 'recovered-failed') "
        "AND NOT EXISTS (SELECT 1 FROM recovery_events AS event "
        "WHERE event.task_id = task.id)",
    )
    if recovered_tasks_without_events:
        _semantic_failure(
            "Recovered scheduler tasks are missing their recovery event.",
            resolved,
            "recovered-task-event-missing",
            task_count=recovered_tasks_without_events,
        )

    claim_state_counts = _group_counts(connection, "claims", "state")
    invalid_claim_states = {
        state: count for state, count in claim_state_counts.items() if state not in _CLAIM_STATES
    }
    if invalid_claim_states:
        _semantic_failure(
            "Scheduler state contains unknown claim states.",
            resolved,
            "claim-state-invalid",
            states=invalid_claim_states,
        )

    kind_counts = _group_counts(connection, "claims", "kind")
    invalid_kinds = {kind: count for kind, count in kind_counts.items() if kind not in _CLAIM_KINDS}
    if invalid_kinds:
        _semantic_failure(
            "Scheduler state contains unknown claim kinds.",
            resolved,
            "claim-kind-invalid",
            kinds=invalid_kinds,
        )

    scope_type_counts = _group_counts(connection, "claim_scopes", "scope_type")
    invalid_scope_types = {
        scope_type: count
        for scope_type, count in scope_type_counts.items()
        if scope_type not in _CLAIM_SCOPE_TYPES
    }
    if invalid_scope_types:
        _semantic_failure(
            "Scheduler state contains unknown claim scope types.",
            resolved,
            "claim-scope-type-invalid",
            scope_types=invalid_scope_types,
        )
    _validate_scope_values(connection, resolved)

    invalid_queue_orders = _scalar(
        connection,
        "SELECT COUNT(*) FROM claims WHERE typeof(queue_order) != 'integer' OR queue_order <= 0",
    )
    if invalid_queue_orders:
        _semantic_failure(
            "Scheduler state contains invalid claim queue orders.",
            resolved,
            "claim-queue-order-invalid",
            claim_count=invalid_queue_orders,
        )
    duplicate_open_queue_orders = _scalar(
        connection,
        "SELECT COUNT(*) FROM (SELECT workspace_id, queue_order FROM claims "
        "WHERE state IN ('queued', 'active', 'parked') "
        "GROUP BY workspace_id, queue_order HAVING COUNT(*) > 1)",
    )
    if duplicate_open_queue_orders:
        _semantic_failure(
            "Open scheduler claims in one workspace share a queue order.",
            resolved,
            "open-claim-queue-order-duplicate",
            duplicate_group_count=duplicate_open_queue_orders,
        )

    workspace_mismatches = _scalar(
        connection,
        "SELECT COUNT(*) FROM claims AS claim "
        "JOIN tasks AS task ON task.id = claim.task_id "
        "WHERE claim.workspace_id != task.workspace_id",
    )
    if workspace_mismatches:
        _semantic_failure(
            "Claims do not belong to their owner task's workspace.",
            resolved,
            "claim-workspace-mismatch",
            claim_count=workspace_mismatches,
        )

    closed_owner_claims = _scalar(
        connection,
        "SELECT COUNT(*) FROM claims AS claim "
        "JOIN tasks AS task ON task.id = claim.task_id "
        "WHERE claim.state IN ('queued', 'active', 'parked') "
        "AND task.state NOT IN ('active', 'outcome_unknown')",
    )
    if closed_owner_claims:
        _semantic_failure(
            "Open claims belong to closed tasks.",
            resolved,
            "open-claim-owner-closed",
            claim_count=closed_owner_claims,
        )

    unscoped_normal_claims = _scalar(
        connection,
        "SELECT COUNT(*) FROM claims AS claim WHERE claim.kind = 'normal' "
        "AND NOT EXISTS (SELECT 1 FROM claim_scopes AS scope "
        "WHERE scope.claim_id = claim.id AND scope.scope_type IN ('write', 'resource'))",
    )
    if unscoped_normal_claims:
        _semantic_failure(
            "Normal claims must have a write or resource scope.",
            resolved,
            "normal-claim-scope-invalid",
            claim_count=unscoped_normal_claims,
        )

    scoped_freezes = _scalar(
        connection,
        "SELECT COUNT(DISTINCT claim.id) FROM claims AS claim "
        "JOIN claim_scopes AS scope ON scope.claim_id = claim.id "
        "WHERE claim.kind = 'freeze' "
        "AND scope.scope_type IN ('write', 'resource', 'parked_for')",
    )
    if scoped_freezes:
        _semantic_failure(
            "Freeze claims must not have write, resource, or parking scopes.",
            resolved,
            "freeze-claim-scope-invalid",
            claim_count=scoped_freezes,
        )

    invalid_priorities = _scalar(
        connection,
        "SELECT COUNT(*) FROM claim_scopes AS priority "
        "JOIN claims AS claim ON claim.id = priority.claim_id "
        "WHERE priority.scope_type = 'priority' "
        "AND (claim.kind != 'freeze' OR priority.value != 'urgent')",
    )
    duplicate_priorities = _scalar(
        connection,
        "SELECT COUNT(*) FROM (SELECT claim_id FROM claim_scopes "
        "WHERE scope_type = 'priority' GROUP BY claim_id HAVING COUNT(*) != 1)",
    )
    if invalid_priorities or duplicate_priorities:
        _semantic_failure(
            "Claim priority markers are invalid.",
            resolved,
            "claim-priority-invalid",
            invalid_marker_count=invalid_priorities,
            non_unique_claim_count=duplicate_priorities,
        )

    if schema_version == 1:
        active_legacy_park_markers = _scalar(
            connection,
            "SELECT COUNT(*) FROM claim_scopes AS marker "
            "JOIN claims AS claim ON claim.id = marker.claim_id "
            "WHERE marker.scope_type = 'parked_for' AND claim.state = 'active'",
        )
        if active_legacy_park_markers:
            _semantic_failure(
                "Schema 1 active claims contain ambiguous restoration markers.",
                resolved,
                "schema-one-active-park-marker-invalid",
                marker_count=active_legacy_park_markers,
            )

    active_claims = connection.execute(
        "SELECT id, workspace_id, task_id, kind FROM claims "
        "WHERE state = 'active' ORDER BY workspace_id, queue_order, id"
    ).fetchall()
    active_scopes: dict[str, dict[str, set[str]]] = {
        str(claim["id"]): {"write": set(), "resource": set()} for claim in active_claims
    }
    for row in connection.execute(
        "SELECT scope.claim_id, scope.scope_type, scope.value FROM claim_scopes AS scope "
        "JOIN claims AS claim ON claim.id = scope.claim_id "
        "WHERE claim.state = 'active' AND scope.scope_type IN ('write', 'resource')"
    ):
        active_scopes[str(row["claim_id"])][str(row["scope_type"])].add(str(row["value"]))
    active_conflicts = 0
    for index, left in enumerate(active_claims):
        left_scopes = active_scopes[str(left["id"])]
        for right in active_claims[index + 1 :]:
            if left["workspace_id"] != right["workspace_id"]:
                break
            right_scopes = active_scopes[str(right["id"])]
            if left["task_id"] == right["task_id"]:
                if (
                    left["kind"] != "freeze"
                    and right["kind"] != "freeze"
                    and bool(left_scopes["resource"] & right_scopes["resource"])
                ):
                    active_conflicts += 1
                continue
            if (
                left["kind"] == "freeze"
                or right["kind"] == "freeze"
                or bool(left_scopes["resource"] & right_scopes["resource"])
                or any(
                    _path_conflicts(left_path, right_path)
                    for left_path in left_scopes["write"]
                    for right_path in right_scopes["write"]
                )
            ):
                active_conflicts += 1
    if active_conflicts:
        _semantic_failure(
            "Active claims conflict in one workspace.",
            resolved,
            "active-claim-conflict",
            conflict_count=active_conflicts,
        )

    unknown_tasks_without_active_claims = _scalar(
        connection,
        "SELECT COUNT(*) FROM tasks AS task "
        "WHERE task.state = 'outcome_unknown' "
        "AND task.result = 'expired-with-active-claim' "
        "AND NOT EXISTS (SELECT 1 FROM claims AS claim "
        "WHERE claim.task_id = task.id AND claim.workspace_id = task.workspace_id "
        "AND claim.state = 'active')",
    )
    if unknown_tasks_without_active_claims:
        _semantic_failure(
            "Expired outcome-unknown tasks must preserve an active owned claim.",
            resolved,
            "outcome-unknown-active-claim-missing",
            task_count=unknown_tasks_without_active_claims,
        )

    if schema_version >= LEGACY_SCHEMA_TWO_VERSION:
        invalid_marker_owners = _scalar(
            connection,
            "SELECT COUNT(*) FROM claim_scopes AS marker "
            "JOIN claims AS claim ON claim.id = marker.claim_id "
            "WHERE marker.scope_type = 'parked_for' "
            "AND (claim.kind != 'normal' OR claim.state NOT IN ('parked', 'queued'))",
        )
        invalid_marker_counts = _scalar(
            connection,
            "SELECT COUNT(*) FROM claims AS claim WHERE "
            "(claim.state = 'parked' AND (SELECT COUNT(*) FROM claim_scopes AS marker "
            "WHERE marker.claim_id = claim.id AND marker.scope_type = 'parked_for') != 1) "
            "OR (claim.state = 'queued' AND (SELECT COUNT(*) FROM claim_scopes AS marker "
            "WHERE marker.claim_id = claim.id AND marker.scope_type = 'parked_for') > 1)",
        )
        if invalid_marker_owners or invalid_marker_counts:
            _semantic_failure(
                "Parked-claim restoration markers are missing, duplicated, or misplaced.",
                resolved,
                "parked-claim-marker-invalid",
                invalid_owner_count=invalid_marker_owners,
                invalid_marker_count_claims=invalid_marker_counts,
            )

        invalid_queued_restorations = _scalar(
            connection,
            "SELECT COUNT(DISTINCT claim.id) FROM claims AS claim "
            "JOIN claim_scopes AS marker ON marker.claim_id = claim.id "
            "WHERE claim.state IN ('queued', 'parked') "
            "AND marker.scope_type = 'parked_for' "
            "AND (NOT EXISTS (SELECT 1 FROM claim_scopes AS path_scope "
            "WHERE path_scope.claim_id = claim.id AND path_scope.scope_type = 'write') "
            "OR EXISTS (SELECT 1 FROM claim_scopes AS resource_scope "
            "WHERE resource_scope.claim_id = claim.id "
            "AND resource_scope.scope_type = 'resource'))",
        )
        if invalid_queued_restorations:
            _semantic_failure(
                "Parked and queued restoration-pending claims must be path-only.",
                resolved,
                "queued-restoration-scope-invalid",
                claim_count=invalid_queued_restorations,
            )

        inconsistent_restoration_tasks = _scalar(
            connection,
            "SELECT COUNT(*) FROM ("
            "SELECT claim.task_id FROM claims AS claim "
            "JOIN claim_scopes AS marker ON marker.claim_id = claim.id "
            "WHERE marker.scope_type = 'parked_for' "
            "AND claim.state IN ('queued', 'parked') "
            "GROUP BY claim.task_id "
            "HAVING COUNT(DISTINCT marker.value) != 1 "
            "OR COUNT(DISTINCT claim.state) != 1)",
        )
        if inconsistent_restoration_tasks:
            _semantic_failure(
                "A task's restoration-pending claims must share one freeze and one phase.",
                resolved,
                "restoration-claim-group-invalid",
                task_count=inconsistent_restoration_tasks,
            )

        invalid_parked_marker_targets = _scalar(
            connection,
            "SELECT COUNT(*) FROM claim_scopes AS marker "
            "JOIN claims AS claim ON claim.id = marker.claim_id "
            "LEFT JOIN claims AS freeze ON freeze.id = marker.value "
            "WHERE marker.scope_type = 'parked_for' "
            "AND claim.state = 'parked' "
            "AND (freeze.id IS NULL OR freeze.kind != 'freeze' "
            "OR freeze.state NOT IN ('queued', 'active') "
            "OR freeze.workspace_id != claim.workspace_id "
            "OR freeze.task_id = claim.task_id)",
        )
        if invalid_parked_marker_targets:
            _semantic_failure(
                "Parked claims must reference another task's open same-workspace freeze.",
                resolved,
                "parked-claim-freeze-invalid",
                marker_count=invalid_parked_marker_targets,
            )

        invalid_queued_marker_targets = _scalar(
            connection,
            "SELECT COUNT(*) FROM claim_scopes AS marker "
            "JOIN claims AS claim ON claim.id = marker.claim_id "
            "LEFT JOIN claims AS freeze ON freeze.id = marker.value "
            "WHERE marker.scope_type = 'parked_for' "
            "AND claim.state = 'queued' "
            "AND freeze.id IS NOT NULL "
            "AND (freeze.kind != 'freeze' "
            "OR freeze.state NOT IN ('released', 'cancelled') "
            "OR freeze.workspace_id != claim.workspace_id "
            "OR freeze.task_id = claim.task_id)",
        )
        if invalid_queued_marker_targets:
            _semantic_failure(
                "Queued restoration claims may reference only another task's closed "
                "same-workspace freeze.",
                resolved,
                "queued-restoration-freeze-invalid",
                marker_count=invalid_queued_marker_targets,
            )

    if schema_version == SCHEMA_VERSION:
        _validate_operation_receipts(connection, resolved)
        _validate_task_start_bindings(connection, resolved)
        _validate_token_cleanup_jobs(connection, resolved)
        _validate_capacity_invariants(
            connection,
            resolved,
            allow_legacy_overflow=allow_legacy_capacity_overflow,
        )

    return task_state_counts, claim_state_counts


def inspect_state(path: Path) -> dict[str, Any]:
    """Validate one database without migrating or mutating it and return an operator report."""

    _reject_symlink(path)
    explicit = _explicit_path(path)
    resolved = explicit.parent.resolve() / explicit.name
    connection, standalone_evidence = _read_only_connection(path)
    try:
        integrity_rows = [str(row[0]) for row in connection.execute("PRAGMA integrity_check")]
        if integrity_rows != ["ok"]:
            raise StateError(
                "Scheduler state failed SQLite integrity_check.",
                details={
                    "path": str(resolved),
                    "reason": "integrity-check-failed",
                    "results": integrity_rows,
                },
            )
        foreign_key_rows = connection.execute("PRAGMA foreign_key_check").fetchall()
        if foreign_key_rows:
            raise StateError(
                "Scheduler state contains foreign-key violations.",
                details={
                    "path": str(resolved),
                    "reason": "foreign-key-check-failed",
                    "violation_count": len(foreign_key_rows),
                },
            )

        tables = {
            str(row[0])
            for row in connection.execute(
                "SELECT name FROM sqlite_master WHERE type = 'table'"
            ).fetchall()
        }
        missing_tables = sorted(_REQUIRED_COLUMNS.keys() - tables)
        if missing_tables:
            raise StateError(
                "Scheduler state schema is incomplete.",
                details={
                    "path": str(resolved),
                    "reason": "schema-tables-missing",
                    "tables": missing_tables,
                },
            )
        missing_columns: dict[str, list[str]] = {}
        for table, required_columns in _REQUIRED_COLUMNS.items():
            columns = {
                str(row["name"])
                for row in connection.execute(f"PRAGMA table_info({table})").fetchall()
            }
            missing = sorted(required_columns - columns)
            if missing:
                missing_columns[table] = missing
        if missing_columns:
            raise StateError(
                "Scheduler state schema columns are incomplete.",
                details={
                    "path": str(resolved),
                    "reason": "schema-columns-missing",
                    "columns": missing_columns,
                },
            )

        version_rows = connection.execute(
            "SELECT value, typeof(value) AS storage_type FROM scheduler_meta "
            "WHERE key = 'schema_version'"
        ).fetchall()
        if len(version_rows) != 1:
            raise StateError(
                "Scheduler schema metadata is missing or ambiguous.",
                details={"path": str(resolved), "reason": "schema-version-invalid"},
            )
        version_value = version_rows[0]["value"]
        if version_rows[0]["storage_type"] != "text" or not isinstance(version_value, str):
            raise StateError(
                "Scheduler schema version is invalid.",
                details={"path": str(resolved), "reason": "schema-version-invalid"},
            )
        canonical_version = _canonical_schema_version(
            version_value, version_rows[0]["storage_type"]
        )
        if canonical_version is None:
            if (
                version_value.isascii()
                and version_value.isdecimal()
                and not version_value.startswith("0")
            ):
                raise StateError(
                    f"Unsupported scheduler schema {version_value}.",
                    details={
                        "path": str(resolved),
                        "reason": "schema-version-unsupported",
                        "schema_version": version_value,
                    },
                )
            raise StateError(
                "Scheduler schema version is not canonical.",
                details={"path": str(resolved), "reason": "schema-version-invalid"},
            )
        schema_version = canonical_version

        workspace_columns = {
            str(row["name"])
            for row in connection.execute("PRAGMA table_info(workspaces)").fetchall()
        }
        has_queue_counter = "next_queue_order" in workspace_columns
        has_receipt_table = "operation_receipts" in tables
        has_cleanup_job_table = "token_cleanup_jobs" in tables
        if (
            has_queue_counter != (schema_version >= LEGACY_SCHEMA_TWO_VERSION)
            or has_receipt_table != (schema_version == SCHEMA_VERSION)
            or has_cleanup_job_table != (schema_version == SCHEMA_VERSION)
        ):
            raise StateError(
                "Scheduler state contains a partial schema migration.",
                details={
                    "path": str(resolved),
                    "reason": "schema-partial-migration",
                    "schema_version": schema_version,
                },
            )
        if schema_version == SCHEMA_VERSION:
            receipt_columns = {
                str(row["name"])
                for row in connection.execute("PRAGMA table_info(operation_receipts)").fetchall()
            }
            missing_receipt_columns = sorted(_RECEIPT_REQUIRED_COLUMNS - receipt_columns)
            if missing_receipt_columns:
                raise StateError(
                    "Scheduler state schema columns are incomplete.",
                    details={
                        "path": str(resolved),
                        "reason": "schema-columns-missing",
                        "columns": {"operation_receipts": missing_receipt_columns},
                    },
                )
            cleanup_job_columns = {
                str(row["name"])
                for row in connection.execute("PRAGMA table_info(token_cleanup_jobs)").fetchall()
            }
            missing_cleanup_job_columns = sorted(
                _TOKEN_CLEANUP_JOB_REQUIRED_COLUMNS - cleanup_job_columns
            )
            if missing_cleanup_job_columns:
                raise StateError(
                    "Scheduler state schema columns are incomplete.",
                    details={
                        "path": str(resolved),
                        "reason": "schema-columns-missing",
                        "columns": {"token_cleanup_jobs": missing_cleanup_job_columns},
                    },
                )
        task_state_counts, claim_state_counts = _validate_semantics(
            connection,
            resolved,
            schema_version,
        )
        _validate_relational_schema_signatures(
            connection,
            resolved,
            schema_version=schema_version,
        )
        if schema_version >= LEGACY_SCHEMA_TWO_VERSION:
            indexes = {
                str(row[0])
                for row in connection.execute(
                    "SELECT name FROM sqlite_master WHERE type = 'index'"
                ).fetchall()
            }
            expected_indexes = (
                _SCHEMA_THREE_INDEXES if schema_version == SCHEMA_VERSION else _SCHEMA_TWO_INDEXES
            )
            missing_indexes = sorted(expected_indexes - indexes)
            if missing_indexes:
                raise StateError(
                    f"Scheduler schema {schema_version} indexes are incomplete.",
                    details={
                        "path": str(resolved),
                        "reason": "schema-indexes-missing",
                        "indexes": missing_indexes,
                    },
                )
            if schema_version == SCHEMA_VERSION:
                _validate_schema_three_index_signatures(connection, resolved)
            else:
                _validate_schema_two_index_signatures(connection, resolved)
            invalid_counters = _scalar(
                connection,
                "SELECT COUNT(*) FROM workspaces AS workspace "
                "WHERE typeof(workspace.next_queue_order) != 'integer' "
                "OR workspace.next_queue_order < 1 "
                "OR workspace.next_queue_order <= COALESCE(("
                "SELECT MAX(claims.queue_order) FROM claims "
                "WHERE claims.workspace_id = workspace.id), 0)",
            )
            if invalid_counters:
                raise StateError(
                    f"Scheduler schema {schema_version} queue counters are invalid.",
                    details={
                        "path": str(resolved),
                        "reason": "queue-counter-invalid",
                        "workspace_count": invalid_counters,
                    },
                )
        _validate_declared_schema_structure(connection, resolved, schema_version)

        open_task_count = sum(task_state_counts.get(state, 0) for state in _OPEN_TASK_STATES)
        open_claim_count = sum(claim_state_counts.get(state, 0) for state in _OPEN_CLAIM_STATES)
        migration_ambiguous_claim_count = sum(
            claim_state_counts.get(state, 0) for state in ("queued", "parked")
        )
        legacy_open_write_scope_migration_count = _legacy_open_write_scope_migration_count(
            connection,
            schema_version,
        )
        now = time.time()
        if not math.isfinite(now):
            raise StateError(
                "System clock is invalid; scheduler state timing cannot be verified.",
                details={"path": str(resolved), "reason": "system-clock-invalid"},
            )
        active_future_heartbeat_count = 0
        active_expiry_out_of_bounds_count = 0
        active_timings = connection.execute(
            "SELECT heartbeat_at, expires_at FROM tasks WHERE state = 'active'"
        ).fetchall()
        for timing in active_timings:
            try:
                heartbeat_at = float(timing["heartbeat_at"])
                expires_at = float(timing["expires_at"])
            except (TypeError, ValueError):
                active_future_heartbeat_count += 1
                active_expiry_out_of_bounds_count += 1
                continue
            if not math.isfinite(heartbeat_at) or heartbeat_at > now:
                active_future_heartbeat_count += 1
            if not math.isfinite(expires_at) or expires_at > now + MAX_TASK_TTL_SECONDS:
                active_expiry_out_of_bounds_count += 1
        counts = {
            "workspaces": _scalar(connection, "SELECT COUNT(*) FROM workspaces"),
            "tasks": sum(task_state_counts.values()),
            "open_tasks": open_task_count,
            "outcome_unknown_tasks": task_state_counts.get("outcome_unknown", 0),
            "claims": sum(claim_state_counts.values()),
            "open_claims": open_claim_count,
            "queued_claims": claim_state_counts.get("queued", 0),
            "active_claims": claim_state_counts.get("active", 0),
            "parked_claims": claim_state_counts.get("parked", 0),
            "recovery_events": _scalar(connection, "SELECT COUNT(*) FROM recovery_events"),
            "operation_receipts": (
                _scalar(connection, "SELECT COUNT(*) FROM operation_receipts")
                if schema_version == SCHEMA_VERSION
                else 0
            ),
            "unacked_operation_receipts": (
                _scalar(
                    connection,
                    "SELECT COUNT(*) FROM operation_receipts WHERE delivered_at IS NULL",
                )
                if schema_version == SCHEMA_VERSION
                else 0
            ),
            "replay_required_operation_receipts": (
                _scalar(
                    connection,
                    "SELECT COUNT(*) FROM operation_receipts "
                    "WHERE delivered_at IS NULL AND retired_at IS NULL",
                )
                if schema_version == SCHEMA_VERSION
                else 0
            ),
            "acked_operation_receipts": (
                _scalar(
                    connection,
                    "SELECT COUNT(*) FROM operation_receipts WHERE delivered_at IS NOT NULL",
                )
                if schema_version == SCHEMA_VERSION
                else 0
            ),
            "retired_operation_receipts": (
                _scalar(
                    connection,
                    "SELECT COUNT(*) FROM operation_receipts WHERE retired_at IS NOT NULL",
                )
                if schema_version == SCHEMA_VERSION
                else 0
            ),
            "pending_operation_receipts": (
                _scalar(
                    connection,
                    "SELECT COUNT(*) FROM operation_receipts WHERE finalized_at IS NULL",
                )
                if schema_version == SCHEMA_VERSION
                else 0
            ),
            "cleanup_pending_operation_receipts": (
                _scalar(
                    connection,
                    "SELECT COUNT(*) FROM operation_receipts WHERE token_cleanup_path IS NOT NULL",
                )
                if schema_version == SCHEMA_VERSION
                else 0
            ),
            "token_cleanup_jobs": (
                _scalar(connection, "SELECT COUNT(*) FROM token_cleanup_jobs")
                if schema_version == SCHEMA_VERSION
                else 0
            ),
            "pending_token_cleanup_jobs": (
                _scalar(
                    connection,
                    "SELECT COUNT(*) FROM token_cleanup_jobs WHERE completed_at IS NULL",
                )
                if schema_version == SCHEMA_VERSION
                else 0
            ),
            "completed_token_cleanup_jobs": (
                _scalar(
                    connection,
                    "SELECT COUNT(*) FROM token_cleanup_jobs WHERE completed_at IS NOT NULL",
                )
                if schema_version == SCHEMA_VERSION
                else 0
            ),
            "active_future_heartbeat_tasks": active_future_heartbeat_count,
            "active_expiry_out_of_bounds_tasks": active_expiry_out_of_bounds_count,
        }
        result = {
            "path": str(resolved),
            "schema_version": schema_version,
            "integrity_check": "ok",
            "foreign_key_violations": 0,
            "schema_one_migration_safe": (
                schema_version == 1
                and migration_ambiguous_claim_count == 0
                and legacy_open_write_scope_migration_count == 0
            ),
            "legacy_open_write_scope_migration_count": legacy_open_write_scope_migration_count,
            "task_states": task_state_counts,
            "claim_states": claim_state_counts,
            "counts": counts,
        }
        return result
    except sqlite3.DatabaseError as exc:
        raise StateError(
            f"Scheduler state verification failed: {exc}",
            details={"path": str(resolved), "reason": "state-verification-failed"},
        ) from exc
    finally:
        connection.close()
        _verify_standalone_read(standalone_evidence)


def verify_state(path: Path, *, for_migration: bool = False) -> dict[str, Any]:
    report = inspect_state(path)
    if for_migration:
        if report["schema_version"] not in {1, LEGACY_SCHEMA_TWO_VERSION}:
            raise UsageError(
                "Migration verification requires a schema-1 or schema-2 state backup.",
                details={
                    "path": report["path"],
                    "reason": "migration-source-not-supported",
                    "schema_version": report["schema_version"],
                },
            )
        counts = report["counts"]
        assert isinstance(counts, dict)
        unsafe_claims = int(counts["queued_claims"]) + int(counts["parked_claims"])
        if report["schema_version"] == 1 and unsafe_claims:
            raise UsageError(
                "Schema 1 contains queued or parked claims whose restoration lineage cannot "
                "be proven.",
                details={
                    "path": report["path"],
                    "reason": "schema-one-open-claim-migration-blocked",
                    "claim_states": {
                        state: counts[f"{state}_claims"]
                        for state in ("queued", "parked")
                        if int(counts[f"{state}_claims"])
                    },
                },
            )
        legacy_open_write_scope_count = int(report["legacy_open_write_scope_migration_count"])
        if legacy_open_write_scope_count:
            raise UsageError(
                "Legacy open write claims cannot be migrated without their original path case.",
                details={
                    "path": report["path"],
                    "reason": "legacy-open-write-scope-migration-blocked",
                    "open_write_scope_count": legacy_open_write_scope_count,
                },
            )
    return report


class _SQLiteBackupDeadlineExceeded(Exception):
    def __init__(self, status: str) -> None:
        super().__init__(status)
        self.status = status


@dataclass
class _StagedDatabase:
    path: Path
    descriptor: int | None

    def verify(self) -> None:
        if self.descriptor is None:
            raise StateError(
                "Scheduler maintenance staging descriptor is closed.",
                details={"path": str(self.path), "reason": "maintenance-staging-closed"},
            )
        descriptor_metadata = os.fstat(self.descriptor)
        path_metadata = self.path.lstat()
        if not (
            stat.S_ISREG(descriptor_metadata.st_mode)
            and stat.S_ISREG(path_metadata.st_mode)
            and descriptor_metadata.st_dev == path_metadata.st_dev
            and descriptor_metadata.st_ino == path_metadata.st_ino
        ):
            raise StateError(
                "Scheduler maintenance staging path was replaced.",
                details={"path": str(self.path), "reason": "maintenance-staging-replaced"},
            )

    def sync(self) -> None:
        self.verify()
        assert self.descriptor is not None
        os.fsync(self.descriptor)

    def close(self) -> None:
        if self.descriptor is not None:
            os.close(self.descriptor)
            self.descriptor = None

    def prepare_publish(self) -> None:
        self.sync()
        self.close()


@dataclass(frozen=True)
class _MaintenanceFileIdentity:
    device: int
    inode: int


def _temporary_database(parent: Path) -> _StagedDatabase:
    secured_parent = _prepare_maintenance_parent(parent)
    descriptor, name = tempfile.mkstemp(
        prefix=".scheduler-state-", suffix=".sqlite3.tmp", dir=secured_parent
    )
    temporary = _StagedDatabase(Path(name), descriptor)
    try:
        if os.name != "nt":
            os.fchmod(descriptor, 0o600)
        temporary.verify()
        _verify_windows_maintenance_file(temporary.path)
    except Exception as exc:
        temporary.close()
        cleanup_error: Exception | None = None
        try:
            temporary.path.unlink(missing_ok=True)
            _durable_barrier_or_recovery(
                temporary.path.parent,
                operation="temporary-initialization-unlink",
                entry=temporary.path,
            )
        except (OSError, StateError, UsageError) as cleanup_exc:
            cleanup_error = cleanup_exc
        if cleanup_error is not None:
            evidence = {
                "cleanup_secondary_error": (
                    cleanup_error.details
                    if isinstance(cleanup_error, StateError)
                    else {"type": cleanup_error.__class__.__name__, "message": str(cleanup_error)}
                )
            }
            if isinstance(exc, (StateError, UsageError)):
                exc.details = {**exc.details, **evidence}
            else:
                raise StateError(
                    "Scheduler staging initialization failed; the primary error is preserved "
                    "with cleanup evidence.",
                    details={"cause": exc.__class__.__name__, **evidence},
                ) from exc
        raise
    return temporary


def _remove_temporary_database(temporary: _StagedDatabase) -> None:
    temporary.close()
    temporary.path.unlink(missing_ok=True)
    _durable_barrier_or_recovery(temporary.path.parent, operation="temporary-unlink")
    for suffix in _SIDECAR_SUFFIXES:
        Path(f"{temporary.path}{suffix}").unlink(missing_ok=True)
        _durable_barrier_or_recovery(temporary.path.parent, operation="temporary-sidecar-unlink")


def _cleanup_temporary_database(
    temporary: _StagedDatabase,
    *,
    evidence: dict[str, list[str]] | None = None,
) -> list[str]:
    pending: list[str] = []
    try:
        temporary.close()
    except OSError:
        # The descriptor is not a filesystem entry and must not be presented
        # as a deletion target.  Report the exact staged artifact instead.
        pending.append(str(temporary.path))
    for path in (
        temporary.path,
        *(Path(f"{temporary.path}{suffix}") for suffix in _SIDECAR_SUFFIXES),
    ):
        try:
            path.unlink(missing_ok=True)
        except OSError:
            pending.append(str(path))
        else:
            try:
                _durable_barrier_or_recovery(
                    path.parent,
                    operation="temporary-cleanup-unlink",
                    entry=path,
                )
            except StateError as exc:
                if evidence is None:
                    raise
                pending_paths = exc.details.get("cleanup_pending")
                if isinstance(pending_paths, list):
                    pending.extend(str(item) for item in pending_paths)
                parent = exc.details.get("durability_pending_parent")
                if isinstance(parent, list):
                    evidence.setdefault("durability_pending_parent", []).extend(
                        str(item) for item in parent
                    )
    return pending


def _maintenance_file_identity(path: Path) -> _MaintenanceFileIdentity:
    _validate_maintenance_database_file(path)
    metadata = path.lstat()
    return _MaintenanceFileIdentity(metadata.st_dev, metadata.st_ino)


def _sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as source:
        for chunk in iter(lambda: source.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _existing_sidecars(path: Path) -> list[Path]:
    return [
        sidecar
        for suffix in _SIDECAR_SUFFIXES
        if _directory_entry_exists(sidecar := Path(f"{path}{suffix}"))
    ]


def _durable_barrier_or_recovery(
    path: Path,
    *,
    operation: str,
    entry: Path | None = None,
) -> None:
    try:
        _durable_directory_barrier(path)
    except OSError as exc:
        pending_entry = entry if entry is not None and _directory_entry_exists(entry) else None
        raise StateError(
            "Scheduler maintenance directory durability could not be proven.",
            details={
                "path": str(path),
                "reason": "maintenance-directory-barrier-failed",
                "operation": operation,
                "parent": str(path),
                "entry": str(entry) if entry is not None else None,
                # A parent needing a re-flush is not an artifact to delete.
                # Report it separately from an entry that still exists.
                "cleanup_pending": ([str(pending_entry)] if pending_entry is not None else []),
                "durability_pending_parent": [str(path)],
                "recovery_required": True,
            },
        ) from exc


def _cleanup_maintenance_entries(
    paths: list[Path],
    *,
    evidence: dict[str, list[str]] | None = None,
) -> list[str]:
    pending: list[str] = []
    for path in paths:
        try:
            path.unlink(missing_ok=True)
        except OSError:
            pending.append(str(path))
        else:
            try:
                _durable_barrier_or_recovery(
                    path.parent,
                    operation="cleanup-unlink",
                    entry=path,
                )
            except StateError as exc:
                if evidence is None:
                    raise
                pending.extend(str(item) for item in exc.details.get("cleanup_pending", []))
                evidence.setdefault("durability_pending_parent", []).extend(
                    str(item) for item in exc.details.get("durability_pending_parent", [])
                )
    return pending


def _prepare_staged_snapshot(path: Path) -> None:
    sidecars = _existing_sidecars(path)
    for sidecar in sidecars:
        _validate_maintenance_database_file(sidecar)
        if sidecar.name.endswith("-journal") or (
            sidecar.name.endswith("-wal") and sidecar.lstat().st_size != 0
        ):
            raise StateError(
                "Staged scheduler snapshot has transaction sidecars.",
                details={
                    "path": str(path),
                    "reason": "staged-snapshot-sidecars-present",
                    "sidecar": str(sidecar),
                },
            )
    cleanup_pending = _cleanup_maintenance_entries(sidecars)
    if cleanup_pending or _existing_sidecars(path):
        raise StateError(
            "Staged scheduler snapshot sidecars could not be removed before publication.",
            details={
                "path": str(path),
                "reason": "staged-snapshot-sidecar-cleanup-failed",
                "cleanup_pending": cleanup_pending,
            },
        )


def _state_report_signature(report: dict[str, Any]) -> dict[str, Any]:
    counts = report["counts"]
    assert isinstance(counts, dict)
    core_count_names = (
        "workspaces",
        "tasks",
        "open_tasks",
        "outcome_unknown_tasks",
        "claims",
        "open_claims",
        "queued_claims",
        "active_claims",
        "parked_claims",
        "recovery_events",
        "operation_receipts",
        "unacked_operation_receipts",
        "replay_required_operation_receipts",
        "acked_operation_receipts",
        "retired_operation_receipts",
        "pending_operation_receipts",
        "cleanup_pending_operation_receipts",
        "token_cleanup_jobs",
        "pending_token_cleanup_jobs",
        "completed_token_cleanup_jobs",
    )
    return {
        "schema_version": report["schema_version"],
        "integrity_check": report["integrity_check"],
        "foreign_key_violations": report["foreign_key_violations"],
        "task_states": report["task_states"],
        "claim_states": report["claim_states"],
        "counts": {name: counts[name] for name in core_count_names},
    }


def _sqlite_status_name(status: int | None) -> str:
    if status is None:
        return "deadline"
    primary = status & 0xFF
    if primary == sqlite3.SQLITE_BUSY:
        return "busy"
    if primary == sqlite3.SQLITE_LOCKED:
        return "locked"
    return f"sqlite-{status}"


def _sqlite_backup(source: Path, destination: _StagedDatabase) -> None:
    source_connection, standalone_evidence = _read_only_connection(source)
    destination_connection: sqlite3.Connection | None = None
    deadline = time.monotonic() + _SQLITE_BACKUP_TIMEOUT_SECONDS
    last_status: int | None = None

    def progress(status: int, _remaining: int, _total: int) -> None:
        nonlocal last_status
        if status & 0xFF in {sqlite3.SQLITE_BUSY, sqlite3.SQLITE_LOCKED}:
            last_status = status
        if time.monotonic() >= deadline:
            raise _SQLiteBackupDeadlineExceeded(_sqlite_status_name(last_status or status))

    try:
        source_connection.execute("PRAGMA busy_timeout = 0")
        destination_connection = sqlite3.connect(destination.path, timeout=0.0)
        destination_connection.execute("PRAGMA busy_timeout = 0")
        while True:
            try:
                source_connection.backup(
                    destination_connection,
                    pages=_SQLITE_BACKUP_PAGES,
                    progress=progress,
                    sleep=_SQLITE_BACKUP_SLEEP_SECONDS,
                )
                break
            except sqlite3.OperationalError as exc:
                message = str(exc).casefold()
                status = getattr(exc, "sqlite_errorcode", None)
                primary_status = status & 0xFF if isinstance(status, int) else None
                retryable = (
                    primary_status in {sqlite3.SQLITE_BUSY, sqlite3.SQLITE_LOCKED}
                    or "busy" in message
                    or "locked" in message
                )
                if not retryable:
                    raise
                last_status = status if isinstance(status, int) else last_status
                remaining = deadline - time.monotonic()
                if remaining <= 0:
                    raise _SQLiteBackupDeadlineExceeded(_sqlite_status_name(last_status)) from exc
                time.sleep(min(_SQLITE_BACKUP_SLEEP_SECONDS, remaining))
        destination_connection.commit()
    except _SQLiteBackupDeadlineExceeded as exc:
        raise StateError(
            "SQLite backup exceeded its hard deadline.",
            details={
                "path": str(source),
                "reason": "sqlite-backup-timeout",
                "sqlite_status": exc.status,
                "timeout_seconds": _SQLITE_BACKUP_TIMEOUT_SECONDS,
            },
        ) from exc
    except sqlite3.DatabaseError as exc:
        raise StateError(
            f"SQLite backup failed: {exc}",
            details={"path": str(source), "reason": "sqlite-backup-failed"},
        ) from exc
    finally:
        if destination_connection is not None:
            destination_connection.close()
        source_connection.close()
        _verify_standalone_read(standalone_evidence)
    destination.sync()


def _publish_without_overwrite(temporary: _StagedDatabase, destination: Path) -> None:
    temporary.prepare_publish()
    try:
        os.link(temporary.path, destination)
    except FileExistsError as exc:
        raise UsageError(
            "Destination already exists; no state was overwritten.",
            details={"path": str(destination), "reason": "destination-exists"},
        ) from exc
    except OSError as exc:
        raise StateError(
            f"Cannot publish state atomically: {exc}",
            details={"path": str(destination), "reason": "atomic-publish-failed"},
        ) from exc
    _durable_barrier_or_recovery(destination.parent, operation="publish-link")


def _inspect_published_snapshot(
    destination: Path,
    expected_identity: _MaintenanceFileIdentity,
    expected_digest: str,
) -> tuple[dict[str, Any], list[Path]]:
    if _existing_sidecars(destination):
        raise StateError(
            "Published scheduler state unexpectedly has SQLite sidecars.",
            details={"path": str(destination), "reason": "published-sidecars-present"},
        )
    if _maintenance_file_identity(destination) != expected_identity:
        raise StateError(
            "Published scheduler state identity does not match the staged snapshot.",
            details={"path": str(destination), "reason": "published-identity-mismatch"},
        )
    report = inspect_state(destination)
    generated_sidecars = _existing_sidecars(destination)
    for sidecar in generated_sidecars:
        _validate_maintenance_database_file(sidecar)
        if sidecar.name.endswith("-journal") or (
            sidecar.name.endswith("-wal") and sidecar.lstat().st_size != 0
        ):
            raise StateError(
                "Published scheduler state gained nonempty transaction sidecars.",
                details={"path": str(destination), "reason": "published-sidecars-changed"},
            )
    if (
        _maintenance_file_identity(destination) != expected_identity
        or _sha256_file(destination) != expected_digest
    ):
        raise StateError(
            "Published scheduler state changed during final verification.",
            details={"path": str(destination), "reason": "published-snapshot-changed"},
        )
    return report, generated_sidecars


def _verify_published_snapshot_bytes(
    destination: Path,
    expected_identity: _MaintenanceFileIdentity,
    expected_digest: str,
) -> None:
    if (
        _existing_sidecars(destination)
        or _maintenance_file_identity(destination) != expected_identity
        or _sha256_file(destination) != expected_digest
    ):
        raise StateError(
            "Published scheduler state changed after final inspection.",
            details={"path": str(destination), "reason": "published-snapshot-changed"},
        )


def _backup_publication_uncertain(
    destination: Path,
    temporary: _StagedDatabase,
    cause: Exception,
) -> StateError:
    cause_details = cause.details if isinstance(cause, (StateError, UsageError)) else {}
    return StateError(
        "Backup publication completed but its final state cannot be proven; preserve both "
        "paths for recovery.",
        details={
            "path": str(destination),
            "reason": "backup-publication-uncertain",
            "publication_uncertain": True,
            "recovery_required": True,
            "staged": str(temporary.path),
            "cause": cause.__class__.__name__,
            "cause_reason": cause_details.get("reason"),
            "cause_details": cause_details,
        },
    )


def _reject_backup_orphan_sidecars(destination: Path) -> None:
    sidecars = _existing_sidecars(destination)
    if sidecars:
        raise UsageError(
            "Backup destination has orphan SQLite sidecars; no state was written.",
            details={
                "path": str(destination),
                "reason": "backup-destination-orphan-sidecars",
                "sidecars": [str(sidecar) for sidecar in sidecars],
            },
        )


def backup_state(paths: StatePaths, output: Path, *, confirm_no_processes: bool) -> dict[str, Any]:
    _require_no_processes(confirm_no_processes)
    source = _explicit_path(paths.database)
    destination_path = _explicit_path(output)
    _reject_symlink(source)
    _reject_symlink(destination_path)
    destination_parent = _prepare_maintenance_parent(destination_path.parent)
    destination = destination_parent / destination_path.name
    if _directory_entry_exists(destination):
        raise UsageError(
            "Backup destination already exists; no state was overwritten.",
            details={"path": str(destination), "reason": "destination-exists"},
        )
    _reject_backup_orphan_sidecars(destination)
    source_report = inspect_state(source)
    temporary = _temporary_database(destination.parent)
    committed = False
    try:
        _sqlite_backup(source, temporary)
        staged_report = inspect_state(temporary.path)
        _prepare_staged_snapshot(temporary.path)
        temporary.sync()
        staged_identity = _maintenance_file_identity(temporary.path)
        staged_digest = _sha256_file(temporary.path)
        _reject_backup_orphan_sidecars(destination)
        _publish_without_overwrite(temporary, destination)
        committed = True
        try:
            backup_report, published_sidecars = _inspect_published_snapshot(
                destination,
                staged_identity,
                staged_digest,
            )
            if _state_report_signature(backup_report) != _state_report_signature(staged_report):
                raise StateError(
                    "Published backup report does not match its staged snapshot.",
                    details={
                        "path": str(destination),
                        "reason": "published-report-mismatch",
                    },
                )
        except Exception as exc:
            raise _backup_publication_uncertain(destination, temporary, exc) from exc
    except Exception as exc:
        if committed:
            raise
        if _directory_entry_exists(destination):
            raise _backup_publication_uncertain(destination, temporary, exc) from exc
        cleanup_evidence: dict[str, list[str]] = {"durability_pending_parent": []}
        cleanup_pending = _cleanup_temporary_database(temporary, evidence=cleanup_evidence)
        if cleanup_pending or cleanup_evidence["durability_pending_parent"]:
            if isinstance(exc, (StateError, UsageError)):
                exc.details = {
                    **exc.details,
                    "cleanup_pending": cleanup_pending,
                    "durability_pending_parent": cleanup_evidence["durability_pending_parent"],
                    "staged": str(temporary.path),
                    "recovery_required": True,
                }
                raise
            raise StateError(
                "Backup staging failed and its cleanup remains pending.",
                details={
                    "path": str(destination),
                    "reason": "backup-staging-cleanup-pending",
                    "cleanup_pending": cleanup_pending,
                    "durability_pending_parent": cleanup_evidence["durability_pending_parent"],
                    "staged": str(temporary.path),
                    "recovery_required": True,
                    "cause": exc.__class__.__name__,
                },
            ) from exc
        raise
    cleanup_pending: list[str] = []
    cleanup_evidence: dict[str, list[str]] = {"durability_pending_parent": []}
    try:
        cleanup_pending.extend(
            _cleanup_maintenance_entries(published_sidecars, evidence=cleanup_evidence)
        )
        cleanup_pending.extend(_cleanup_temporary_database(temporary, evidence=cleanup_evidence))
    except StateError as exc:
        barrier_pending = exc.details.get("cleanup_pending")
        if isinstance(barrier_pending, list):
            cleanup_pending.extend(str(path) for path in barrier_pending)
        barrier_parents = exc.details.get("durability_pending_parent")
        if isinstance(barrier_parents, list):
            cleanup_evidence["durability_pending_parent"].extend(
                str(path) for path in barrier_parents
            )
        if not barrier_pending and not barrier_parents:
            raise _backup_publication_uncertain(destination, temporary, exc) from exc
    except Exception as exc:
        raise _backup_publication_uncertain(destination, temporary, exc) from exc
    backup_report["path"] = str(destination)
    return {
        "source": source_report,
        "backup": backup_report,
        "confirmed_no_processes": True,
        "cleanup_pending": cleanup_pending,
        "durability_pending_parent": cleanup_evidence["durability_pending_parent"],
    }


def _validate_checkpoint_sidecars(target: Path) -> None:
    for sidecar in _existing_sidecars(target):
        _validate_maintenance_database_file(sidecar)
        if sidecar.name.endswith("-journal") or (
            sidecar.name.endswith("-wal") and sidecar.lstat().st_size != 0
        ):
            raise StateError(
                "Restore target has transaction sidecars after its empty checkpoint.",
                details={
                    "path": str(target),
                    "reason": "restore-target-sidecars-changed",
                    "sidecar": str(sidecar),
                },
            )


def _checkpoint_empty_target(
    target: Path,
) -> tuple[dict[str, Any], _MaintenanceFileIdentity, str]:
    initial_identity = _maintenance_file_identity(target)
    report = inspect_state(target)
    counts = report["counts"]
    assert isinstance(counts, dict)
    nonempty_count_names = (
        "tasks",
        "claims",
        "recovery_events",
        "replay_required_operation_receipts",
        "cleanup_pending_operation_receipts",
        "token_cleanup_jobs",
    )
    if any(int(counts[name]) for name in nonempty_count_names):
        raise UsageError(
            "Existing scheduler state is not empty; restore was refused.",
            details={
                "path": str(target),
                "reason": "restore-target-not-empty",
                "counts": counts,
            },
        )
    connection = sqlite3.connect(target, timeout=30.0)
    try:
        checkpoint = connection.execute("PRAGMA wal_checkpoint(TRUNCATE)").fetchone()
    except sqlite3.DatabaseError as exc:
        raise StateError(
            f"Cannot checkpoint restore target: {exc}",
            details={"path": str(target), "reason": "restore-target-checkpoint-failed"},
        ) from exc
    finally:
        connection.close()
    if checkpoint is None or int(checkpoint[0]) != 0:
        raise StateError(
            "Restore target WAL is busy; no state was replaced.",
            details={"path": str(target), "reason": "restore-target-busy"},
        )
    verified = inspect_state(target)
    verified_counts = verified["counts"]
    assert isinstance(verified_counts, dict)
    if any(int(verified_counts[name]) for name in nonempty_count_names):
        raise StateError(
            "Restore target changed during safety verification; no state was replaced.",
            details={"path": str(target), "reason": "restore-target-changed"},
        )
    verified_identity = _maintenance_file_identity(target)
    if verified_identity != initial_identity:
        raise StateError(
            "Restore target identity changed during safety verification.",
            details={"path": str(target), "reason": "restore-target-changed"},
        )
    _validate_checkpoint_sidecars(target)
    return verified, verified_identity, _sha256_file(target)


def _restore_quarantine_path(target: Path) -> Path:
    return target.with_name(f".{target.name}.restore-quarantine")


def _create_restore_quarantine(target: Path) -> Path:
    quarantine = _restore_quarantine_path(target)
    try:
        _ensure_private_directory(quarantine, require_new=True)
        if os.name == "nt":
            _verify_windows_maintenance_acl(quarantine)
        _durable_barrier_or_recovery(quarantine.parent, operation="quarantine-create")
    except FileExistsError as exc:
        raise StateError(
            "A prior restore quarantine exists; inspect and recover it before retrying.",
            details={
                "path": str(target),
                "reason": "restore-recovery-required",
                "publication_uncertain": False,
                "recovery_required": True,
                "quarantine": str(quarantine),
            },
        ) from exc
    except StateError as exc:
        if exc.details.get("reason") == "maintenance-directory-barrier-failed":
            exc.details = {
                **exc.details,
                "reason": "restore-recovery-required",
                "recovery_required": True,
                "publication_uncertain": False,
                "quarantine": str(quarantine),
            }
        raise
    except OSError as exc:
        cleanup_error: Exception | None = None
        try:
            quarantine.rmdir()
            _durable_barrier_or_recovery(
                quarantine.parent,
                operation="quarantine-create-cleanup-rmdir",
                entry=quarantine,
            )
        except (OSError, StateError, UsageError) as cleanup_exc:
            cleanup_error = cleanup_exc
        details: dict[str, Any] = {
            "path": str(target),
            "reason": "restore-quarantine-create-failed",
        }
        if cleanup_error is not None:
            details["cleanup_secondary_error"] = (
                cleanup_error.details
                if isinstance(cleanup_error, StateError)
                else {"type": cleanup_error.__class__.__name__, "message": str(cleanup_error)}
            )
        raise StateError(
            f"Cannot create a private restore quarantine: {exc}",
            details=details,
        ) from exc
    return quarantine


def _cleanup_restore_quarantine(
    quarantine: Path,
    target: Path,
    *,
    evidence: dict[str, list[str]] | None = None,
) -> list[str]:
    pending: list[str] = []
    for path in (
        quarantine / target.name,
        *(quarantine / f"{target.name}{suffix}" for suffix in _SIDECAR_SUFFIXES),
    ):
        try:
            path.unlink(missing_ok=True)
        except OSError:
            pending.append(str(path))
        else:
            try:
                _durable_barrier_or_recovery(
                    quarantine,
                    operation="quarantine-entry-unlink",
                    entry=path,
                )
            except StateError as exc:
                if evidence is None:
                    raise
                pending.extend(str(item) for item in exc.details.get("cleanup_pending", []))
                evidence.setdefault("durability_pending_parent", []).extend(
                    str(item) for item in exc.details.get("durability_pending_parent", [])
                )
    try:
        quarantine.rmdir()
    except OSError:
        pending.append(str(quarantine))
    else:
        try:
            _durable_barrier_or_recovery(
                quarantine.parent,
                operation="quarantine-rmdir",
                entry=quarantine,
            )
        except StateError as exc:
            if evidence is None:
                raise
            evidence.setdefault("durability_pending_parent", []).extend(
                str(item) for item in exc.details.get("durability_pending_parent", [])
            )
    return pending


def _quarantine_existing_target(
    target: Path,
    quarantine: Path,
    target_sidecars: list[Path],
    expected_target_identity: _MaintenanceFileIdentity,
    expected_target_digest: str,
) -> None:
    sources = (target, *target_sidecars)
    source_evidence = {
        path: (
            expected_target_identity,
            expected_target_digest,
        )
        if path == target
        else (_maintenance_file_identity(path), _sha256_file(path))
        for path in sources
    }
    for path in sources:
        expected_identity, expected_digest = source_evidence[path]
        if (
            _maintenance_file_identity(path) != expected_identity
            or _sha256_file(path) != expected_digest
        ):
            raise StateError(
                "Restore target changed before quarantine custody was established.",
                details={"path": str(path), "reason": "restore-target-changed"},
            )
        os.link(path, quarantine / path.name)

    _durable_barrier_or_recovery(quarantine, operation="quarantine-old-target-link")
    for path in sources:
        expected_identity, expected_digest = source_evidence[path]
        quarantined = quarantine / path.name
        if (
            _maintenance_file_identity(quarantined) != expected_identity
            or _sha256_file(quarantined) != expected_digest
        ):
            raise StateError(
                "Quarantine custody does not match the original restore target.",
                details={"path": str(quarantined), "reason": "restore-quarantine-changed"},
            )
    _validate_checkpoint_sidecars(quarantine / target.name)
    for path in sources:
        path.unlink()
    _durable_barrier_or_recovery(target.parent, operation="quarantine-old-target-unlink")
    if any(_directory_entry_exists(path) for path in sources):
        raise StateError(
            "Restore target entries remained after quarantine custody transfer.",
            details={"path": str(target), "reason": "restore-target-changed"},
        )


def _restore_publication_uncertain(
    target: Path,
    *,
    target_existed: bool,
    initial_target_identity: _MaintenanceFileIdentity | None,
    initial_target_digest: str | None,
    committed: bool,
) -> bool:
    if committed:
        return True
    if not _directory_entry_exists(target):
        return False
    if not target_existed or initial_target_identity is None or initial_target_digest is None:
        return True
    try:
        return (
            _maintenance_file_identity(target) != initial_target_identity
            or _sha256_file(target) != initial_target_digest
        )
    except (OSError, UsageError):
        return True


def _restore_recovery_error(
    target: Path,
    quarantine: Path,
    temporary: _StagedDatabase,
    cause: Exception,
    *,
    publication_uncertain: bool,
) -> StateError:
    cause_details = cause.details if isinstance(cause, (StateError, UsageError)) else {}
    return StateError(
        "Restore state cannot be safely retried until preserved evidence is inspected.",
        details={
            "path": str(target),
            "reason": (
                "restore-publication-uncertain"
                if publication_uncertain
                else "restore-recovery-required"
            ),
            "publication_uncertain": publication_uncertain,
            "recovery_required": True,
            "quarantine": str(quarantine),
            "staged": str(temporary.path),
            "cause": cause.__class__.__name__,
            "cause_reason": cause_details.get("reason"),
            "cause_details": cause_details,
        },
    )


def _raise_restore_recovery_required(
    target: Path,
    quarantine: Path,
    temporary: _StagedDatabase,
    reason: str,
) -> None:
    cause = StateError(reason)
    raise _restore_recovery_error(
        target,
        quarantine,
        temporary,
        cause,
        publication_uncertain=False,
    ) from cause


def restore_state(
    paths: StatePaths,
    source: Path,
    *,
    confirm_no_processes: bool,
    replace_empty: bool = False,
    allow_open_claims: bool = False,
) -> dict[str, Any]:
    _require_no_processes(confirm_no_processes)
    source_report = inspect_state(source)

    target_path = _explicit_path(paths.database)
    _reject_symlink(target_path)
    target_parent = _prepare_maintenance_parent(target_path.parent)
    target = target_parent / target_path.name
    temporary = _temporary_database(target.parent)
    quarantine: Path | None = None
    committed = False
    try:
        _sqlite_backup(source, temporary)
        staged_report = inspect_state(temporary.path)
        _prepare_staged_snapshot(temporary.path)
        staged_counts = staged_report["counts"]
        assert isinstance(staged_counts, dict)
        if int(staged_counts["open_claims"]) and not allow_open_claims:
            raise UsageError(
                "Staged backup contains open claims; inspect recovery state and repeat only "
                "with --allow-open-claims if preserving them is intentional.",
                details={
                    "path": staged_report["path"],
                    "reason": "restore-source-has-open-claims",
                    "open_claims": staged_counts["open_claims"],
                    "claim_states": staged_report["claim_states"],
                },
            )
        temporary.sync()
        staged_identity = _maintenance_file_identity(temporary.path)
        staged_digest = _sha256_file(temporary.path)

        target_existed = _directory_entry_exists(target)
        initial_target_identity: _MaintenanceFileIdentity | None = None
        initial_target_digest: str | None = None
        if target_existed:
            _reject_symlink(target)
            if not replace_empty:
                raise UsageError(
                    "Restore target exists; use --replace-empty only for a verified empty target.",
                    details={"path": str(target), "reason": "restore-target-exists"},
                )
            (
                _,
                initial_target_identity,
                initial_target_digest,
            ) = _checkpoint_empty_target(target)
        else:
            sidecars = _existing_sidecars(target)
            if sidecars:
                raise UsageError(
                    "Restore target has orphan SQLite sidecars; no state was written.",
                    details={
                        "path": str(target),
                        "reason": "restore-target-orphan-sidecars",
                        "sidecars": [str(sidecar) for sidecar in sidecars],
                    },
                )

        quarantine = _create_restore_quarantine(target)
        if target_existed:
            if not _directory_entry_exists(target):
                _raise_restore_recovery_required(
                    target, quarantine, temporary, "restore target disappeared"
                )
            try:
                (
                    _,
                    current_target_identity,
                    current_target_digest,
                ) = _checkpoint_empty_target(target)
            except Exception as exc:
                raise _restore_recovery_error(
                    target,
                    quarantine,
                    temporary,
                    exc,
                    publication_uncertain=False,
                ) from exc
            if (
                current_target_identity != initial_target_identity
                or current_target_digest != initial_target_digest
            ):
                _raise_restore_recovery_required(
                    target, quarantine, temporary, "restore target identity changed"
                )
            target_sidecars = _existing_sidecars(target)
            for sidecar in target_sidecars:
                _validate_maintenance_database_file(sidecar)
            assert initial_target_identity is not None
            _quarantine_existing_target(
                target,
                quarantine,
                target_sidecars,
                initial_target_identity,
                initial_target_digest,
            )
        elif _directory_entry_exists(target) or _existing_sidecars(target):
            _raise_restore_recovery_required(
                target, quarantine, temporary, "restore target entries appeared"
            )

        if target_existed:
            _validate_checkpoint_sidecars(quarantine / target.name)
        _publish_without_overwrite(temporary, target)
        committed = True
        try:
            restored_report, published_sidecars = _inspect_published_snapshot(
                target,
                staged_identity,
                staged_digest,
            )
            if _state_report_signature(restored_report) != _state_report_signature(staged_report):
                raise StateError(
                    "Published restore report does not match its staged snapshot.",
                    details={
                        "path": str(target),
                        "reason": "published-report-mismatch",
                    },
                )
            publication_cleanup_evidence: dict[str, list[str]] = {"durability_pending_parent": []}
            sidecar_cleanup_pending = _cleanup_maintenance_entries(
                published_sidecars, evidence=publication_cleanup_evidence
            )
            if (
                sidecar_cleanup_pending
                or publication_cleanup_evidence["durability_pending_parent"]
                or _existing_sidecars(target)
            ):
                raise StateError(
                    "Published restore sidecars could not be safely removed.",
                    details={
                        "path": str(target),
                        "reason": "published-sidecar-cleanup-failed",
                        "cleanup_pending": sidecar_cleanup_pending,
                        "durability_pending_parent": publication_cleanup_evidence[
                            "durability_pending_parent"
                        ],
                    },
                )
            if target_existed:
                quarantined_target = quarantine / target.name
                (
                    _,
                    final_quarantined_identity,
                    final_quarantined_digest,
                ) = _checkpoint_empty_target(quarantined_target)
                if (
                    final_quarantined_identity != initial_target_identity
                    or final_quarantined_digest != initial_target_digest
                ):
                    raise StateError(
                        "Quarantined restore target changed after publication.",
                        details={
                            "path": str(quarantined_target),
                            "reason": "restore-quarantine-changed",
                        },
                    )
                _validate_checkpoint_sidecars(quarantined_target)
            _verify_published_snapshot_bytes(
                target,
                staged_identity,
                staged_digest,
            )
        except Exception as exc:
            raise _restore_recovery_error(
                target,
                quarantine,
                temporary,
                exc,
                publication_uncertain=True,
            ) from exc
    except Exception as exc:
        if quarantine is not None:
            if isinstance(exc, StateError) and exc.details.get("reason") in {
                "restore-recovery-required",
                "restore-publication-uncertain",
            }:
                raise
            raise _restore_recovery_error(
                target,
                quarantine,
                temporary,
                exc,
                publication_uncertain=_restore_publication_uncertain(
                    target,
                    target_existed=target_existed,
                    initial_target_identity=initial_target_identity,
                    initial_target_digest=initial_target_digest,
                    committed=committed,
                ),
            ) from exc
        if isinstance(exc, StateError) and exc.details.get("recovery_required"):
            exc.details = {
                **exc.details,
                "path": str(target),
                "staged": str(temporary.path),
            }
            raise
        cleanup_evidence: dict[str, list[str]] = {"durability_pending_parent": []}
        cleanup_pending = _cleanup_temporary_database(temporary, evidence=cleanup_evidence)
        if cleanup_pending or cleanup_evidence["durability_pending_parent"]:
            raise StateError(
                "Restore staging cleanup failed; inspect the preserved staging path.",
                details={
                    "path": str(target),
                    "reason": "restore-recovery-required",
                    "publication_uncertain": False,
                    "recovery_required": True,
                    "staged": str(temporary.path),
                    "cleanup_pending": cleanup_pending,
                    "durability_pending_parent": cleanup_evidence["durability_pending_parent"],
                },
            ) from exc
        raise

    assert quarantine is not None
    cleanup_pending: list[str] = []
    cleanup_evidence: dict[str, list[str]] = {"durability_pending_parent": []}
    try:
        cleanup_pending.extend(_cleanup_temporary_database(temporary, evidence=cleanup_evidence))
        cleanup_pending.extend(
            _cleanup_restore_quarantine(quarantine, target, evidence=cleanup_evidence)
        )
    except StateError as exc:
        barrier_pending = exc.details.get("cleanup_pending")
        if isinstance(barrier_pending, list):
            cleanup_pending.extend(str(path) for path in barrier_pending)
        barrier_parents = exc.details.get("durability_pending_parent")
        if isinstance(barrier_parents, list):
            cleanup_evidence["durability_pending_parent"].extend(
                str(path) for path in barrier_parents
            )
        if not barrier_pending and not barrier_parents:
            raise _restore_recovery_error(
                target,
                quarantine,
                temporary,
                exc,
                publication_uncertain=True,
            ) from exc
    except Exception as exc:
        raise _restore_recovery_error(
            target,
            quarantine,
            temporary,
            exc,
            publication_uncertain=True,
        ) from exc
    return {
        "source": source_report,
        "restored": restored_report,
        "staged_schema_version": staged_report["schema_version"],
        "replaced_empty_state": target_existed,
        "preserved_open_claims": int(staged_counts["open_claims"]),
        "confirmed_no_processes": True,
        "cleanup_pending": cleanup_pending,
        "durability_pending_parent": cleanup_evidence["durability_pending_parent"],
    }
