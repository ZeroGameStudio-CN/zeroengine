"""Stable JSON command interface for the workspace scheduler."""

from __future__ import annotations

import argparse
import json
import secrets
import sqlite3
import time
from collections.abc import Callable, Sequence
from pathlib import Path
from typing import Any

from . import __version__
from .coordinator import DEFAULT_TASK_TTL_SECONDS, WorkspaceCoordinator
from .errors import SchedulerError, StateError, UsageError
from .state import (
    create_token_file,
    read_token_file,
    remove_matching_token_file,
    resolve_state_paths,
)


class SchedulerArgumentParser(argparse.ArgumentParser):
    def error(self, message: str) -> None:
        raise UsageError(message)


def _workspace_argument(parser: argparse.ArgumentParser) -> None:
    parser.add_argument("--workspace", type=Path, required=True)


def _token_argument(parser: argparse.ArgumentParser) -> None:
    parser.add_argument("--token-file", type=Path, required=True)


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
    register.set_defaults(handler=_workspace_register)
    unregister = workspace_commands.add_parser("unregister")
    _workspace_argument(unregister)
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
    start.add_argument("--owner", required=True)
    start.add_argument("--summary", required=True)
    start.add_argument("--ttl", type=float, default=DEFAULT_TASK_TTL_SECONDS)
    start.set_defaults(handler=_task_start)
    heartbeat = task_commands.add_parser("heartbeat")
    _workspace_argument(heartbeat)
    _token_argument(heartbeat)
    heartbeat.add_argument("--ttl", type=float, default=DEFAULT_TASK_TTL_SECONDS)
    heartbeat.add_argument("--note", default=None)
    heartbeat.set_defaults(handler=_task_heartbeat)
    park = task_commands.add_parser("park")
    _workspace_argument(park)
    _token_argument(park)
    park.add_argument("--wait", type=float, default=0.0)
    park.set_defaults(handler=_task_park)
    release = task_commands.add_parser("release")
    _workspace_argument(release)
    _token_argument(release)
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
    _claim_scope_arguments(acquire)
    acquire.add_argument("--wait", type=float, default=0.0)
    acquire.add_argument("--keep-queued", action="store_true")
    acquire.set_defaults(handler=_claim_acquire)
    claim_release = claim_commands.add_parser("release")
    _workspace_argument(claim_release)
    _token_argument(claim_release)
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
    cancel.add_argument("--claim-id", required=True)
    cancel.set_defaults(handler=_claim_release)

    freeze = groups.add_parser("freeze", help="Acquire an exclusive workspace barrier.")
    freeze_commands = freeze.add_subparsers(dest="command", required=True)
    freeze_acquire = freeze_commands.add_parser("acquire")
    _workspace_argument(freeze_acquire)
    _token_argument(freeze_acquire)
    freeze_acquire.add_argument("--wait", type=float, default=0.0)
    freeze_acquire.add_argument("--keep-queued", action="store_true")
    freeze_acquire.set_defaults(handler=_freeze_acquire)

    recovery = groups.add_parser("recovery", help="Resolve unknown task outcomes.")
    recovery_commands = recovery.add_subparsers(dest="command", required=True)
    resolve = recovery_commands.add_parser("resolve")
    _workspace_argument(resolve)
    resolve.add_argument("--task-id", required=True)
    resolve.add_argument("--resolution", choices=("completed", "failed"), required=True)
    resolve.add_argument("--evidence", required=True)
    resolve.set_defaults(handler=_recovery_resolve)
    return parser


def _workspace_register(
    coordinator: WorkspaceCoordinator, args: argparse.Namespace
) -> tuple[str, dict[str, Any]]:
    return "Workspace registered.", coordinator.register(args.workspace)


def _workspace_unregister(
    coordinator: WorkspaceCoordinator, args: argparse.Namespace
) -> tuple[str, dict[str, Any]]:
    return "Workspace unregistered.", coordinator.unregister(args.workspace)


def _workspace_status(
    coordinator: WorkspaceCoordinator, args: argparse.Namespace
) -> tuple[str, dict[str, Any]]:
    return "Workspace status inspected.", coordinator.status(args.workspace)


def _workspace_list(
    coordinator: WorkspaceCoordinator, args: argparse.Namespace
) -> tuple[str, dict[str, Any]]:
    del args
    return "Workspace registrations listed.", coordinator.list_workspaces()


def _task_start(
    coordinator: WorkspaceCoordinator, args: argparse.Namespace
) -> tuple[str, dict[str, Any]]:
    token = secrets.token_urlsafe(32)
    token_file = create_token_file(args.token_file, token)
    try:
        task, _ = coordinator.start_task(
            args.workspace,
            args.owner,
            args.summary,
            ttl_seconds=args.ttl,
            token=token,
        )
    except Exception:
        remove_matching_token_file(token_file, token)
        raise
    task["token_file"] = str(token_file)
    return "Task started.", task


def _task_heartbeat(
    coordinator: WorkspaceCoordinator, args: argparse.Namespace
) -> tuple[str, dict[str, Any]]:
    token = read_token_file(args.token_file)
    return "Task heartbeat renewed.", coordinator.heartbeat(
        args.workspace, token, ttl_seconds=args.ttl, note=args.note
    )


def _task_park(
    coordinator: WorkspaceCoordinator, args: argparse.Namespace
) -> tuple[str, dict[str, Any]]:
    token = read_token_file(args.token_file)
    return "Task claims parked for workspace maintenance.", coordinator.park_task(
        args.workspace, token, wait_seconds=args.wait
    )


def _task_release(
    coordinator: WorkspaceCoordinator, args: argparse.Namespace
) -> tuple[str, dict[str, Any]]:
    token = read_token_file(args.token_file)
    task = coordinator.release_task(args.workspace, token, result=args.result, note=args.note)
    if args.result == "outcome-unknown":
        task["token_file_removed"] = False
    else:
        task["token_file_removed"] = remove_matching_token_file(args.token_file, token)
    return "Task released.", task


def _claim_acquire(
    coordinator: WorkspaceCoordinator, args: argparse.Namespace
) -> tuple[str, dict[str, Any]]:
    token = read_token_file(args.token_file)
    result = coordinator.acquire_claim(
        args.workspace,
        token,
        writes=args.write,
        resources=args.resource,
        wait_seconds=args.wait,
        keep_queued=args.keep_queued,
    )
    return "Claim scheduled.", result


def _freeze_acquire(
    coordinator: WorkspaceCoordinator, args: argparse.Namespace
) -> tuple[str, dict[str, Any]]:
    token = read_token_file(args.token_file)
    result = coordinator.acquire_claim(
        args.workspace,
        token,
        freeze=True,
        wait_seconds=args.wait,
        keep_queued=args.keep_queued,
    )
    return "Freeze scheduled.", result


def _claim_release(
    coordinator: WorkspaceCoordinator, args: argparse.Namespace
) -> tuple[str, dict[str, Any]]:
    token = read_token_file(args.token_file)
    return "Claim released.", coordinator.release_claim(args.workspace, token, args.claim_id)


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
        resolution=args.resolution,
        evidence=args.evidence,
    )


def _emit(payload: dict[str, Any]) -> None:
    print(json.dumps(payload, ensure_ascii=True, sort_keys=True))


def run(argv: Sequence[str] | None = None) -> int:
    started = time.monotonic()
    try:
        args = build_parser().parse_args(argv)
        coordinator = WorkspaceCoordinator(resolve_state_paths(args.state_dir))
        handler: Callable[
            [WorkspaceCoordinator, argparse.Namespace], tuple[str, dict[str, Any]]
        ] = args.handler
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
