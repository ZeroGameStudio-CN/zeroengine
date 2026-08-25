"""Stable JSON command interface for the workspace scheduler."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import secrets
import sqlite3
import time
from collections.abc import Callable, Sequence
from pathlib import Path
from typing import Any

from . import PROTOCOL_VERSION, __version__
from .coordinator import DEFAULT_TASK_TTL_SECONDS, TOKEN_CLEANUP_DRAIN_LIMIT, WorkspaceCoordinator
from .errors import SchedulerError, StateError, UsageError
from .operations import validate_operation_id
from .state import (
    canonical_missing_token_file_path,
    canonical_token_file_path,
    create_token_file,
    read_token_file,
    read_token_file_with_path,
    remove_matching_token_hash_file,
    resolve_state_paths,
    task_token_path_lock,
)
from .state_ops import backup_state, restore_state, verify_state


class SchedulerArgumentParser(argparse.ArgumentParser):
    def __init__(self, *args: Any, **kwargs: Any) -> None:
        kwargs.setdefault("allow_abbrev", False)
        super().__init__(*args, **kwargs)

    def error(self, message: str) -> None:
        raise UsageError(message)


class _TaskTokenRelock(Exception):
    def __init__(self, path: Path, *, created_token: bool) -> None:
        super().__init__(str(path))
        self.path = path
        self.created_token = created_token


def _token_path_identity(path: Path) -> str:
    identity = os.path.normcase(os.path.normpath(str(path)))
    return identity.casefold() if os.name == "nt" else identity


def _workspace_argument(parser: argparse.ArgumentParser) -> None:
    parser.add_argument("--workspace", type=Path, required=True)


def _token_argument(parser: argparse.ArgumentParser) -> None:
    parser.add_argument("--token-file", type=Path, required=True)


def _operation_arguments(parser: argparse.ArgumentParser) -> None:
    parser.add_argument("--operation-id", required=True, type=validate_operation_id)
    parser.add_argument("--receipt-only", action="store_true")


def _wait_arguments(parser: argparse.ArgumentParser) -> None:
    parser.add_argument("--wait", type=float, default=0.0)
    parser.add_argument("--requested-wait", type=float, default=None)


def _claim_scope_arguments(parser: argparse.ArgumentParser) -> None:
    parser.add_argument("--write", action="append", default=[])
    parser.add_argument("--resource", action="append", default=[])


def build_parser() -> SchedulerArgumentParser:
    parser = SchedulerArgumentParser(
        prog="unity-scheduler",
        description="Machine-local Unity workspace task and resource scheduler.",
    )
    parser.add_argument("--state-dir", type=Path, default=None)
    parser.add_argument("--version", action="version", version=f"%(prog)s {__version__}")
    groups = parser.add_subparsers(dest="group", required=True)

    workspace = groups.add_parser("workspace", help="Manage workspace registration.")
    workspace_commands = workspace.add_subparsers(dest="command", required=True)
    register = workspace_commands.add_parser("register")
    _workspace_argument(register)
    _operation_arguments(register)
    register.set_defaults(handler=_workspace_register)
    unregister = workspace_commands.add_parser("unregister")
    _workspace_argument(unregister)
    _operation_arguments(unregister)
    unregister.set_defaults(handler=_workspace_unregister)
    status = workspace_commands.add_parser("status")
    _workspace_argument(status)
    status.set_defaults(handler=_workspace_status)
    list_command = workspace_commands.add_parser("list")
    list_command.set_defaults(handler=_workspace_list)

    task = groups.add_parser("task", help="Manage bounded work units.")
    task_commands = task.add_subparsers(dest="command", required=True)
    start = task_commands.add_parser("start")
    _workspace_argument(start)
    _token_argument(start)
    _operation_arguments(start)
    start.add_argument("--owner", required=True)
    start.add_argument("--summary", required=True)
    start.add_argument("--ttl", type=float, default=DEFAULT_TASK_TTL_SECONDS)
    start.set_defaults(handler=_task_start)
    heartbeat = task_commands.add_parser("heartbeat")
    _workspace_argument(heartbeat)
    _token_argument(heartbeat)
    _operation_arguments(heartbeat)
    heartbeat.add_argument("--ttl", type=float, default=None)
    heartbeat.add_argument("--note", default=None)
    heartbeat.set_defaults(handler=_task_heartbeat)
    park = task_commands.add_parser("park")
    _workspace_argument(park)
    _token_argument(park)
    _operation_arguments(park)
    _wait_arguments(park)
    park.set_defaults(handler=_task_park)
    release = task_commands.add_parser("release")
    _workspace_argument(release)
    _token_argument(release)
    _operation_arguments(release)
    release.add_argument(
        "--result",
        choices=("completed", "failed", "outcome-unknown"),
        required=True,
    )
    release.add_argument("--note", default=None)
    release.set_defaults(handler=_task_release)

    claim = groups.add_parser("claim", help="Manage path and resource claims.")
    claim_commands = claim.add_subparsers(dest="command", required=True)
    acquire = claim_commands.add_parser("acquire")
    _workspace_argument(acquire)
    _token_argument(acquire)
    _operation_arguments(acquire)
    _claim_scope_arguments(acquire)
    _wait_arguments(acquire)
    acquire.add_argument("--keep-queued", action="store_true")
    acquire.set_defaults(handler=_claim_acquire)
    claim_release = claim_commands.add_parser("release")
    _workspace_argument(claim_release)
    _token_argument(claim_release)
    _operation_arguments(claim_release)
    claim_release.add_argument("--claim-id", required=True)
    claim_release.set_defaults(handler=_claim_release)
    assertion = claim_commands.add_parser("assert")
    _workspace_argument(assertion)
    _token_argument(assertion)
    _claim_scope_arguments(assertion)
    assertion.add_argument("--freeze", action="store_true")
    assertion.set_defaults(handler=_claim_assert)

    queue = groups.add_parser("queue", help="Manage queued claims.")
    queue_commands = queue.add_subparsers(dest="command", required=True)
    cancel = queue_commands.add_parser("cancel")
    _workspace_argument(cancel)
    _token_argument(cancel)
    _operation_arguments(cancel)
    cancel.add_argument("--claim-id", required=True)
    cancel.set_defaults(handler=_queue_cancel)

    freeze = groups.add_parser("freeze", help="Acquire an exclusive workspace barrier.")
    freeze_commands = freeze.add_subparsers(dest="command", required=True)
    freeze_acquire = freeze_commands.add_parser("acquire")
    _workspace_argument(freeze_acquire)
    _token_argument(freeze_acquire)
    _operation_arguments(freeze_acquire)
    _wait_arguments(freeze_acquire)
    freeze_acquire.add_argument("--keep-queued", action="store_true")
    freeze_acquire.add_argument("--priority", choices=("normal", "urgent"), default="normal")
    freeze_acquire.set_defaults(handler=_freeze_acquire)

    recovery = groups.add_parser("recovery", help="Resolve unknown task outcomes.")
    recovery_commands = recovery.add_subparsers(dest="command", required=True)
    resolve = recovery_commands.add_parser("resolve")
    _workspace_argument(resolve)
    _operation_arguments(resolve)
    resolve.add_argument("--task-id", required=True)
    resolve.add_argument("--resolution", choices=("completed", "failed"), required=True)
    resolve.add_argument("--evidence", required=True)
    resolve.set_defaults(handler=_recovery_resolve)

    maintenance = groups.add_parser(
        "maintenance", help="Inspect bounded scheduler maintenance history."
    )
    maintenance_commands = maintenance.add_subparsers(dest="command", required=True)
    history = maintenance_commands.add_parser("history")
    _workspace_argument(history)
    history.add_argument("--limit", type=int, default=20)
    history.set_defaults(handler=_maintenance_history)

    identify = task_commands.add_parser("identify")
    _workspace_argument(identify)
    _token_argument(identify)
    identify.set_defaults(handler=_task_identify)

    receipt = groups.add_parser("receipt", help="Acknowledge durable mutation delivery.")
    receipt_commands = receipt.add_subparsers(dest="command", required=True)
    acknowledge = receipt_commands.add_parser("ack")
    acknowledge.add_argument("--operation-id", required=True, type=validate_operation_id)
    acknowledge.add_argument("--fingerprint", required=True)
    acknowledge.add_argument("--delivery-digest", required=True)
    acknowledge.set_defaults(handler=_receipt_ack)

    state = groups.add_parser("state", help="Back up, verify, or restore scheduler state offline.")
    state_commands = state.add_subparsers(dest="command", required=True)
    backup = state_commands.add_parser("backup")
    backup.add_argument("--output", type=Path, required=True)
    backup.add_argument("--confirm-no-processes", action="store_true")
    backup.set_defaults(handler=_state_backup)
    verify = state_commands.add_parser("verify")
    verify.add_argument("--input", type=Path, required=True)
    verify.add_argument("--for-migration", action="store_true")
    verify.set_defaults(handler=_state_verify)
    restore = state_commands.add_parser("restore")
    restore.add_argument("--input", type=Path, required=True)
    restore.add_argument("--confirm-no-processes", action="store_true")
    restore.add_argument("--replace-empty", action="store_true")
    restore.add_argument("--allow-open-claims", action="store_true")
    restore.set_defaults(handler=_state_restore)
    return parser


def _workspace_register(
    coordinator: WorkspaceCoordinator, args: argparse.Namespace
) -> tuple[str, dict[str, Any]]:
    return "Workspace registered.", coordinator.register(
        args.workspace,
        operation_id=args.operation_id,
        receipt_only=args.receipt_only,
    )


def _workspace_unregister(
    coordinator: WorkspaceCoordinator, args: argparse.Namespace
) -> tuple[str, dict[str, Any]]:
    return "Workspace unregistered.", coordinator.unregister(
        args.workspace,
        operation_id=args.operation_id,
        receipt_only=args.receipt_only,
    )


def _workspace_status(
    coordinator: WorkspaceCoordinator, args: argparse.Namespace
) -> tuple[str, dict[str, Any]]:
    coordinator.status(args.workspace)
    cleanup = coordinator.drain_token_cleanup_jobs(
        limit=TOKEN_CLEANUP_DRAIN_LIMIT,
        workspace=args.workspace,
    )
    result = coordinator.status(args.workspace)
    pending_jobs = [job for job in result["token_cleanup_jobs"] if job["completed_at"] is None]
    if cleanup["failed"] or pending_jobs:
        raise StateError(
            "Workspace status found token cleanup that is not yet complete.",
            details={
                "reason": "token-cleanup-pending",
                "cleanup": cleanup,
                "task_ids": [job["task_id"] for job in pending_jobs[:8]],
                "pending_token_cleanup_jobs": len(pending_jobs),
                "recovery_required": True,
            },
        )
    return "Workspace status inspected.", result


def _workspace_list(
    coordinator: WorkspaceCoordinator, args: argparse.Namespace
) -> tuple[str, dict[str, Any]]:
    del args
    return "Workspace registrations listed.", coordinator.list_workspaces()


def _maintenance_history(
    coordinator: WorkspaceCoordinator, args: argparse.Namespace
) -> tuple[str, dict[str, Any]]:
    return "Workspace maintenance history inspected.", coordinator.maintenance_history(
        args.workspace,
        limit=args.limit,
    )


def _task_start(
    coordinator: WorkspaceCoordinator, args: argparse.Namespace
) -> tuple[str, dict[str, Any]]:
    validate_operation_id(args.operation_id)
    candidate_token_file = canonical_token_file_path(args.token_file)
    seen: set[str] = set()
    created_token = False
    for _ in range(3):
        identity = _token_path_identity(candidate_token_file)
        if identity in seen:
            break
        seen.add(identity)
        try:
            with task_token_path_lock(coordinator.paths, candidate_token_file):
                return _task_start_locked(
                    coordinator,
                    args,
                    candidate_token_file,
                    inherited_created_token=created_token,
                )
        except _TaskTokenRelock as relock:
            created_token = created_token or relock.created_token
            candidate_token_file = relock.path
    raise StateError(
        "Task token path identity did not stabilize under its canonical lock.",
        details={
            "reason": "task-token-path-identity-unstable",
            "operation_id": args.operation_id,
            "recovery_required": True,
        },
    )


def _task_start_locked(
    coordinator: WorkspaceCoordinator,
    args: argparse.Namespace,
    candidate_token_file: Path,
    *,
    inherited_created_token: bool,
) -> tuple[str, dict[str, Any]]:
    if not args.receipt_only:
        coordinator.complete_exact_task_start_cleanup(
            args.operation_id,
            str(candidate_token_file),
            args.workspace,
            args.owner,
            args.summary,
            args.ttl,
        )
    receipt_exists = coordinator.preflight_task_start_token(
        args.operation_id,
        str(candidate_token_file),
        args.workspace,
        args.owner,
        args.summary,
        args.ttl,
        receipt_only=args.receipt_only,
    )
    created_token = inherited_created_token
    if os.path.lexists(candidate_token_file):
        token, token_file = read_token_file_with_path(candidate_token_file)
    else:
        if args.receipt_only or receipt_exists:
            task = coordinator.replay_expired_task_start_without_token(
                args.operation_id,
                str(candidate_token_file),
                args.workspace,
                args.owner,
                args.summary,
                args.ttl,
            )
            task["token_file"] = str(candidate_token_file)
            return "Task started.", task
        token = secrets.token_urlsafe(32)
        try:
            token_file = create_token_file(candidate_token_file, token)
            created_token = True
        except UsageError:
            if not os.path.lexists(candidate_token_file):
                raise
            token, token_file = read_token_file_with_path(candidate_token_file)
    canonical_opened_token_file = canonical_token_file_path(token_file)
    if _token_path_identity(canonical_opened_token_file) != _token_path_identity(
        candidate_token_file
    ):
        raise _TaskTokenRelock(
            canonical_opened_token_file,
            created_token=created_token,
        )
    token_file = canonical_opened_token_file
    try:
        task, _ = coordinator.start_task(
            args.workspace,
            args.owner,
            args.summary,
            operation_id=args.operation_id,
            token_file_path=str(token_file),
            receipt_only=args.receipt_only,
            ttl_seconds=args.ttl,
            token=token,
        )
    except SchedulerError as exc:
        if created_token:
            try:
                receipt_committed = coordinator.task_start_receipt_committed(
                    args.operation_id,
                    str(token_file),
                    args.workspace,
                    args.owner,
                    args.summary,
                    args.ttl,
                    token,
                )
                removed = receipt_committed or remove_matching_token_hash_file(
                    token_file,
                    hashlib.sha256(token.encode("utf-8")).hexdigest(),
                )
            except (OSError, RuntimeError, ValueError, SchedulerError) as cleanup_exc:
                raise StateError(
                    "Task start failed and its new token cleanup is uncertain.",
                    details={
                        "reason": "task-start-token-cleanup-uncertain",
                        "operation_id": args.operation_id,
                        "recovery_required": True,
                    },
                ) from cleanup_exc
            if not removed:
                raise StateError(
                    "Task start failed and its new token no longer matches cleanup identity.",
                    details={
                        "reason": "task-start-token-cleanup-failed",
                        "operation_id": args.operation_id,
                        "recovery_required": True,
                    },
                ) from exc
        raise
    task["token_file"] = str(token_file)
    return "Task started.", task


def _task_heartbeat(
    coordinator: WorkspaceCoordinator, args: argparse.Namespace
) -> tuple[str, dict[str, Any]]:
    if args.receipt_only and not os.path.lexists(args.token_file):
        token_file = canonical_missing_token_file_path(args.token_file)
        result = coordinator.replay_terminal_lifecycle_without_token(
            args.workspace,
            action="task.heartbeat",
            operation_id=args.operation_id,
            note=args.note,
            ttl_seconds=args.ttl,
            token_file_path=str(token_file),
        )
        return "Task heartbeat replayed.", result
    token = read_token_file(args.token_file)
    return "Task heartbeat renewed.", coordinator.heartbeat(
        args.workspace,
        token,
        operation_id=args.operation_id,
        receipt_only=args.receipt_only,
        ttl_seconds=args.ttl,
        note=args.note,
    )


def _task_park(
    coordinator: WorkspaceCoordinator, args: argparse.Namespace
) -> tuple[str, dict[str, Any]]:
    if args.receipt_only and not os.path.lexists(args.token_file):
        token_file = canonical_missing_token_file_path(args.token_file)
        return (
            "Task claims parked for workspace maintenance.",
            coordinator.replay_terminal_lifecycle_without_token(
                args.workspace,
                action="task.park",
                operation_id=args.operation_id,
                wait_seconds=args.wait,
                requested_wait_seconds=args.requested_wait,
                token_file_path=str(token_file),
            ),
        )
    token = read_token_file(args.token_file)
    return "Task claims parked for workspace maintenance.", coordinator.park_task(
        args.workspace,
        token,
        operation_id=args.operation_id,
        receipt_only=args.receipt_only,
        wait_seconds=args.wait,
        requested_wait_seconds=args.requested_wait,
    )


def _task_release(
    coordinator: WorkspaceCoordinator, args: argparse.Namespace
) -> tuple[str, dict[str, Any]]:
    if args.receipt_only and not os.path.lexists(args.token_file):
        token_file = canonical_missing_token_file_path(args.token_file)
        cleanup_path = str(token_file) if args.result in {"completed", "failed"} else None
        task = coordinator.replay_terminal_task_release_without_token(
            args.workspace,
            operation_id=args.operation_id,
            result=args.result,
            note=args.note,
            token_cleanup_path=cleanup_path,
            token_file_path=str(token_file),
        )
        return "Task released.", task
    token, token_file = read_token_file_with_path(args.token_file)
    cleanup_path = str(token_file) if args.result in {"completed", "failed"} else None
    task = coordinator.release_task(
        args.workspace,
        token,
        operation_id=args.operation_id,
        receipt_only=args.receipt_only,
        result=args.result,
        note=args.note,
        token_cleanup_path=cleanup_path,
    )
    return "Task released.", task


def _claim_acquire(
    coordinator: WorkspaceCoordinator, args: argparse.Namespace
) -> tuple[str, dict[str, Any]]:
    if args.receipt_only and not os.path.lexists(args.token_file):
        token_file = canonical_missing_token_file_path(args.token_file)
        return "Claim scheduled.", coordinator.replay_terminal_lifecycle_without_token(
            args.workspace,
            action="claim.acquire",
            operation_id=args.operation_id,
            writes=args.write,
            resources=args.resource,
            wait_seconds=args.wait,
            requested_wait_seconds=args.requested_wait,
            keep_queued=args.keep_queued,
            token_file_path=str(token_file),
        )
    token = read_token_file(args.token_file)
    result = coordinator.acquire_claim(
        args.workspace,
        token,
        operation_id=args.operation_id,
        receipt_only=args.receipt_only,
        writes=args.write,
        resources=args.resource,
        wait_seconds=args.wait,
        requested_wait_seconds=args.requested_wait,
        keep_queued=args.keep_queued,
    )
    return "Claim scheduled.", result


def _freeze_acquire(
    coordinator: WorkspaceCoordinator, args: argparse.Namespace
) -> tuple[str, dict[str, Any]]:
    if args.receipt_only and not os.path.lexists(args.token_file):
        token_file = canonical_missing_token_file_path(args.token_file)
        return "Freeze scheduled.", coordinator.replay_terminal_lifecycle_without_token(
            args.workspace,
            action="freeze.acquire",
            operation_id=args.operation_id,
            freeze=True,
            priority=args.priority,
            wait_seconds=args.wait,
            requested_wait_seconds=args.requested_wait,
            keep_queued=args.keep_queued,
            token_file_path=str(token_file),
        )
    token = read_token_file(args.token_file)
    result = coordinator.acquire_claim(
        args.workspace,
        token,
        operation_id=args.operation_id,
        receipt_only=args.receipt_only,
        freeze=True,
        priority=args.priority,
        wait_seconds=args.wait,
        requested_wait_seconds=args.requested_wait,
        keep_queued=args.keep_queued,
    )
    return "Freeze scheduled.", result


def _claim_release(
    coordinator: WorkspaceCoordinator, args: argparse.Namespace
) -> tuple[str, dict[str, Any]]:
    if args.receipt_only and not os.path.lexists(args.token_file):
        token_file = canonical_missing_token_file_path(args.token_file)
        return "Claim released.", coordinator.replay_terminal_lifecycle_without_token(
            args.workspace,
            action="claim.release",
            operation_id=args.operation_id,
            claim_id=args.claim_id,
            token_file_path=str(token_file),
        )
    token = read_token_file(args.token_file)
    return "Claim released.", coordinator.release_claim(
        args.workspace,
        token,
        args.claim_id,
        operation_id=args.operation_id,
        receipt_only=args.receipt_only,
    )


def _queue_cancel(
    coordinator: WorkspaceCoordinator, args: argparse.Namespace
) -> tuple[str, dict[str, Any]]:
    if args.receipt_only and not os.path.lexists(args.token_file):
        token_file = canonical_missing_token_file_path(args.token_file)
        return "Queued claim cancelled.", coordinator.replay_terminal_lifecycle_without_token(
            args.workspace,
            action="queue.cancel",
            operation_id=args.operation_id,
            claim_id=args.claim_id,
            token_file_path=str(token_file),
        )
    token = read_token_file(args.token_file)
    return "Queued claim cancelled.", coordinator.cancel_claim(
        args.workspace,
        token,
        args.claim_id,
        operation_id=args.operation_id,
        receipt_only=args.receipt_only,
    )


def _claim_assert(
    coordinator: WorkspaceCoordinator, args: argparse.Namespace
) -> tuple[str, dict[str, Any]]:
    token = read_token_file(args.token_file)
    return "Claims authorized.", coordinator.assert_claims(
        args.workspace,
        token,
        writes=args.write,
        resources=args.resource,
        freeze=args.freeze,
    )


def _recovery_resolve(
    coordinator: WorkspaceCoordinator, args: argparse.Namespace
) -> tuple[str, dict[str, Any]]:
    return "Unknown task outcome resolved.", coordinator.resolve_unknown(
        args.workspace,
        args.task_id,
        operation_id=args.operation_id,
        resolution=args.resolution,
        evidence=args.evidence,
        receipt_only=args.receipt_only,
    )


def _task_identify(
    coordinator: WorkspaceCoordinator, args: argparse.Namespace
) -> tuple[str, dict[str, Any]]:
    token = read_token_file(args.token_file)
    return "Open task identified.", coordinator.identify_task(args.workspace, token)


def _receipt_ack(
    coordinator: WorkspaceCoordinator, args: argparse.Namespace
) -> tuple[str, dict[str, Any]]:
    return "Operation receipt acknowledged.", coordinator.acknowledge_receipt(
        args.operation_id,
        args.fingerprint,
        args.delivery_digest,
    )


def _state_backup(
    coordinator: WorkspaceCoordinator, args: argparse.Namespace
) -> tuple[str, dict[str, Any]]:
    return "Scheduler state backed up.", backup_state(
        coordinator.paths,
        args.output,
        confirm_no_processes=args.confirm_no_processes,
    )


def _state_verify(
    coordinator: WorkspaceCoordinator | None, args: argparse.Namespace
) -> tuple[str, dict[str, Any]]:
    del coordinator
    return "Scheduler state verified.", verify_state(
        args.input,
        for_migration=args.for_migration,
    )


def _state_restore(
    coordinator: WorkspaceCoordinator, args: argparse.Namespace
) -> tuple[str, dict[str, Any]]:
    return "Scheduler state restored.", restore_state(
        coordinator.paths,
        args.input,
        confirm_no_processes=args.confirm_no_processes,
        replace_empty=args.replace_empty,
        allow_open_claims=args.allow_open_claims,
    )


def _emit(payload: dict[str, Any]) -> None:
    payload = {**payload, "protocol_version": PROTOCOL_VERSION}
    print(json.dumps(payload, ensure_ascii=True, sort_keys=True))


def run(argv: Sequence[str] | None = None) -> int:
    started = time.monotonic()
    try:
        args = build_parser().parse_args(argv)
        handler: Callable[
            [WorkspaceCoordinator | None, argparse.Namespace], tuple[str, dict[str, Any]]
        ] = args.handler
        coordinator = (
            None
            if handler is _state_verify
            else WorkspaceCoordinator(resolve_state_paths(args.state_dir))
        )
        if (
            coordinator is not None
            and handler
            not in {
                _receipt_ack,
                _task_identify,
                _maintenance_history,
                _state_backup,
                _state_restore,
                _workspace_status,
            }
            and not getattr(args, "receipt_only", False)
        ):
            coordinator.drain_token_cleanup_jobs()
        message, result = handler(coordinator, args)
        _emit(
            {
                "ok": True,
                "code": "ok",
                "message": message,
                "duration_ms": round((time.monotonic() - started) * 1000, 3),
                "result": result,
            }
        )
        return 0
    except SchedulerError as exc:
        _emit(
            {
                "ok": False,
                "code": exc.code,
                "message": exc.message,
                "duration_ms": round((time.monotonic() - started) * 1000, 3),
                "details": exc.details,
            }
        )
        return exc.exit_code
    except sqlite3.DatabaseError as exc:
        error = StateError(f"Scheduler database failure: {exc}")
        _emit(
            {
                "ok": False,
                "code": error.code,
                "message": error.message,
                "duration_ms": round((time.monotonic() - started) * 1000, 3),
                "details": {},
            }
        )
        return error.exit_code
    except Exception as exc:  # noqa: BLE001  # pragma: no cover - fail-closed boundary
        _emit(
            {
                "ok": False,
                "code": "internal-error",
                "message": str(exc),
                "duration_ms": round((time.monotonic() - started) * 1000, 3),
                "details": {},
            }
        )
        return 1


def main() -> None:
    raise SystemExit(run())


if __name__ == "__main__":
    main()
