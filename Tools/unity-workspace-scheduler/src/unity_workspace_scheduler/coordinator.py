"""Atomic workspace task and resource scheduling."""

from __future__ import annotations

import hashlib
import json
import math
import os
import secrets
import sqlite3
import time
import uuid
from collections.abc import Iterator, Sequence
from contextlib import contextmanager
from dataclasses import dataclass
from pathlib import Path, PurePosixPath
from typing import Any

from .errors import (
    AuthorizationError,
    BusyError,
    ClaimAuthorizationError,
    SchedulerError,
    StateError,
    UsageError,
)
from .operations import (
    CLAIM_RELEASE_STATES,
    LIFECYCLE_ACTIONS,
    LIFECYCLE_REVOCATION_ACTIONS,
    PUBLIC_MUTATION_ACTIONS,
    QUEUE_CANCEL_STATES,
    canonical_json,
    is_sha256_hex,
    operation_fingerprint,
    parse_canonical_json,
    receipt_delivery_digest,
    validate_operation_id,
)
from .state import (
    MAX_TASK_TTL_SECONDS,
    SCHEMA_VERSION,
    TERMINAL_CLAIM_RETENTION,
    StatePaths,
    _has_control_characters,
    _is_normalized_recovery_evidence,
    _platform_case_identity,
    canonical_token_file_path,
    canonical_workspace,
    open_database,
    remove_matching_token_hash_file,
    task_token_path_lock,
)

ACTIVE_TASK_STATES = ("active", "outcome_unknown")
OPEN_CLAIM_STATES = ("queued", "active", "parked")
TERMINAL_TASK_STATES = ("completed", "failed", "expired")
DEFAULT_TASK_TTL_SECONDS = 1800.0
TERMINAL_TASK_RETENTION = 1000
DELIVERED_OPERATION_RETENTION = 10000
TOKEN_CLEANUP_DRAIN_LIMIT = 8
TOKEN_CLEANUP_BACKLOG_LIMIT = 4096
REPLAY_REQUIRED_OPERATION_LIMIT = 16384
TASK_LIFECYCLE_OPERATION_LIMIT = 512
TASK_SAFE_LIFECYCLE_RETENTION = 256


@dataclass(frozen=True)
class _Operation:
    operation_id: str
    workspace_id: str
    action: str
    parameters_json: str
    owner_token_hash: str | None
    fingerprint: str


def _operation(
    operation_id: str,
    workspace_id: str,
    action: str,
    parameters: dict[str, Any],
    owner_token_hash: str | None,
) -> _Operation:
    validated_id = validate_operation_id(operation_id)
    parameters_json = canonical_json(parameters)
    return _Operation(
        operation_id=validated_id,
        workspace_id=workspace_id,
        action=action,
        parameters_json=parameters_json,
        owner_token_hash=owner_token_hash,
        fingerprint=operation_fingerprint(
            workspace_id,
            action,
            parameters_json,
            owner_token_hash,
        ),
    )


def _validate_ttl(ttl_seconds: float) -> None:
    if not math.isfinite(ttl_seconds) or ttl_seconds <= 0 or ttl_seconds > MAX_TASK_TTL_SECONDS:
        raise UsageError(
            f"Task TTL must be finite, greater than zero, and no more than "
            f"{int(MAX_TASK_TTL_SECONDS)} seconds."
        )


def _validate_wait(wait_seconds: float, subject: str) -> None:
    if not math.isfinite(wait_seconds) or wait_seconds < 0 or wait_seconds > MAX_TASK_TTL_SECONDS:
        raise UsageError(
            f"{subject} wait must be finite, not negative, and no more than "
            f"{int(MAX_TASK_TTL_SECONDS)} seconds."
        )


def _remaining_operation_wait(
    receipt_created_at: float,
    requested_wait_seconds: float,
    effective_wait_seconds: float,
    subject: str,
) -> float:
    wall_now = time.time()
    if not math.isfinite(wall_now) or not math.isfinite(receipt_created_at):
        raise StateError(
            f"System clock is invalid; {subject} wait cannot continue safely.",
            details={"reason": "system-clock-invalid"},
        )
    if wall_now < receipt_created_at:
        return 0.0
    return max(
        0.0,
        min(
            receipt_created_at + requested_wait_seconds - wall_now,
            effective_wait_seconds,
        ),
    )


def _token_hash(token: str) -> str:
    if not isinstance(token, str) or not token:
        raise UsageError("Task token must be non-empty text.")
    return hashlib.sha256(token.encode("utf-8")).hexdigest()


def _canonical_token_file_path(path: str) -> str:
    if (
        not isinstance(path, str)
        or not path
        or _has_control_characters(path)
        or not os.path.isabs(path)
        or os.path.normpath(path) != path
    ):
        raise UsageError(
            "Task token path must be canonical and absolute.",
            details={"reason": "task-token-path-invalid"},
        )
    return path


def _canonical_lifecycle_replay_token_path(path: str) -> str:
    if os.name == "nt" and not os.path.lexists(path):
        return str(canonical_token_file_path(Path(path)))
    return _canonical_token_file_path(path)


def _token_path_identity(path: str) -> str:
    normalized = os.path.normcase(os.path.normpath(path))
    return normalized.casefold() if os.name == "nt" else normalized


def _workspace_id(root: str) -> str:
    key = _platform_case_identity(root)
    return hashlib.sha256(key.encode("utf-8")).hexdigest()


def _entity_id(value: object) -> bool:
    return isinstance(value, str) and bool(value) and not _has_control_characters(value)


def _path_conflicts(left: str, right: str) -> bool:
    left_unit = _platform_case_identity(left)
    right_unit = _platform_case_identity(right)
    if left_unit.endswith(".meta"):
        left_unit = left_unit.removesuffix(".meta")
    if right_unit.endswith(".meta"):
        right_unit = right_unit.removesuffix(".meta")
    return (
        left_unit == right_unit
        or left_unit == "."
        or right_unit == "."
        or left_unit.startswith(right_unit + "/")
        or right_unit.startswith(left_unit + "/")
    )


class WorkspaceCoordinator:
    def __init__(self, paths: StatePaths) -> None:
        self.paths = paths

    @contextmanager
    def _transaction(self) -> Iterator[sqlite3.Connection]:
        connection = open_database(self.paths)
        try:
            connection.execute("BEGIN IMMEDIATE")
            yield connection
            connection.commit()
        except Exception:
            connection.rollback()
            raise
        finally:
            connection.close()

    @staticmethod
    def _operation_terminal_proof(
        receipt: sqlite3.Row,
        stored_result: dict[str, Any],
    ) -> dict[str, Any] | None:
        terminal_json = receipt["terminal_json"]
        retired_at = receipt["retired_at"]
        if terminal_json is None and retired_at is None:
            return None
        if terminal_json is None and receipt["action"] in {
            "claim.acquire",
            "freeze.acquire",
            "task.park",
        }:
            if (
                receipt["finalized_at"] is None
                or isinstance(retired_at, bool)
                or not isinstance(retired_at, (int, float))
                or not math.isfinite(float(retired_at))
                or float(retired_at) < float(receipt["finalized_at"])
                or stored_result.get("aborted") is not True
                or stored_result.get("reason")
                not in {
                    "task-ttl-expired",
                    "task-ttl-expired-with-active-claim",
                    "task-released",
                    "task-released-outcome-unknown",
                }
            ):
                raise StateError(
                    "Operation receipt has invalid lifecycle retirement metadata.",
                    details={
                        "reason": "operation-receipt-invalid",
                        "operation_id": receipt["operation_id"],
                    },
                )
            return None
        if terminal_json is None or retired_at is None:
            raise StateError(
                "Scheduler operation receipt has invalid lifecycle retirement metadata.",
                details={
                    "reason": "operation-receipt-invalid",
                    "operation_id": receipt["operation_id"],
                },
            )
        try:
            proof = parse_canonical_json(terminal_json)
            parameters = parse_canonical_json(receipt["parameters_json"])
        except (TypeError, ValueError, UsageError, json.JSONDecodeError) as exc:
            raise StateError(
                "Scheduler task-start lifecycle proof is unreadable.",
                details={
                    "reason": "operation-receipt-invalid",
                    "operation_id": receipt["operation_id"],
                },
            ) from exc
        if not isinstance(parameters, dict):
            raise StateError(
                "Scheduler task-start lifecycle parameters are unreadable.",
                details={
                    "reason": "operation-receipt-invalid",
                    "operation_id": receipt["operation_id"],
                },
            )
        action = str(receipt["action"])
        revocation_actions = LIFECYCLE_REVOCATION_ACTIONS
        resolution_keys = {
            "resolution_reason",
            "terminal_finished_at",
            "terminal_result",
            "terminal_state",
        }
        if action in revocation_actions or action == "task.release":
            resolution_proof_keys = {frozenset(resolution_keys | {"token_cleanup_completed"})}
        else:
            resolution_proof_keys = {frozenset(resolution_keys)}
        if action in LIFECYCLE_ACTIONS and frozenset(proof) in resolution_proof_keys:
            terminal_state = proof.get("terminal_state")
            terminal_result = proof.get("terminal_result")
            cleanup_completed = proof.get("token_cleanup_completed")
            recovery_result_valid = stored_result.get("aborted") is True and stored_result.get(
                "reason"
            ) in {
                "task-ttl-expired-with-active-claim",
                "task-released-outcome-unknown",
            }
            if action in LIFECYCLE_REVOCATION_ACTIONS:
                recovery_result_valid = (
                    isinstance(stored_result.get("id"), str)
                    and isinstance(stored_result.get("task_id"), str)
                    and stored_result.get("state")
                    in (CLAIM_RELEASE_STATES if action == "claim.release" else QUEUE_CANCEL_STATES)
                )
            elif action == "task.release":
                recovery_result_valid = (
                    stored_result.get("state") == "outcome_unknown"
                    and stored_result.get("result") == "outcome-unknown"
                )
            if (
                not recovery_result_valid
                or proof.get("resolution_reason") != "task-recovery-resolved"
                or terminal_state not in {"completed", "failed"}
                or terminal_result != f"recovered-{terminal_state}"
                or (action in revocation_actions or action == "task.release")
                and cleanup_completed is not True
                or isinstance(proof.get("terminal_finished_at"), bool)
                or not isinstance(proof.get("terminal_finished_at"), (int, float))
                or not math.isfinite(float(proof["terminal_finished_at"]))
                or float(proof["terminal_finished_at"])
                < float(
                    stored_result.get("created_at")
                    if action == "task.release"
                    else receipt["created_at"]
                )
                or isinstance(retired_at, bool)
                or not isinstance(retired_at, (int, float))
                or not math.isfinite(float(retired_at))
                or receipt["finalized_at"] is None
                or float(retired_at) < float(receipt["finalized_at"])
                or float(retired_at) < float(proof["terminal_finished_at"])
            ):
                raise StateError(
                    "Scheduler recovery resolution proof is internally inconsistent.",
                    details={
                        "reason": "operation-receipt-invalid",
                        "operation_id": receipt["operation_id"],
                    },
                )
            return proof
        common_keys = {
            "aborted",
            "reason",
            "terminal_finished_at",
            "terminal_result",
            "terminal_state",
        }
        proof_keys = frozenset(proof)
        cleanup_completed = proof.get("token_cleanup_completed")
        if action in revocation_actions:
            valid_proof_keys = {frozenset(common_keys | {"token_cleanup_completed"})}
        elif action == "task.start":
            valid_proof_keys = {
                frozenset(common_keys),
                frozenset(common_keys | {"token_cleanup_completed"}),
            }
        else:
            valid_proof_keys = {frozenset(common_keys)}
        reason = proof.get("reason")
        terminal_state = proof.get("terminal_state")
        terminal_result = proof.get("terminal_result")
        valid_transition = {
            "task-ttl-expired": ("expired", "expired"),
            "task-ttl-expired-with-active-claim": (
                "outcome_unknown",
                "expired-with-active-claim",
            ),
            "task-released-outcome-unknown": ("outcome_unknown", "outcome-unknown"),
        }.get(reason)
        if reason == "task-released":
            valid_transition = (
                (terminal_state, terminal_result)
                if terminal_state in {"completed", "failed"} and terminal_result == terminal_state
                else None
            )
        elif reason == "task-recovery-resolved":
            valid_transition = (
                (terminal_state, terminal_result)
                if terminal_state in {"completed", "failed"}
                and terminal_result == f"recovered-{terminal_state}"
                else None
            )
        valid_action = action == "task.start" or action in LIFECYCLE_ACTIONS
        claim_result_valid = True
        if action == "claim.release":
            claim_result_valid = (
                isinstance(stored_result.get("id"), str)
                and isinstance(stored_result.get("task_id"), str)
                and stored_result.get("state") in CLAIM_RELEASE_STATES
            )
        elif action == "queue.cancel":
            claim_result_valid = (
                isinstance(stored_result.get("id"), str)
                and isinstance(stored_result.get("task_id"), str)
                and stored_result.get("state") in QUEUE_CANCEL_STATES
            )
        elif action == "task.release":
            claim_result_valid = stored_result.get("state") in {
                "completed",
                "failed",
            } and stored_result.get("result") == stored_result.get("state")
        if (
            not valid_action
            or (
                action == "task.start"
                and (
                    proof_keys not in valid_proof_keys
                    or ("token_cleanup_completed" in proof and cleanup_completed is not True)
                )
            )
            or (action != "task.start" and proof_keys not in valid_proof_keys)
            or proof.get("aborted") is not True
            or not claim_result_valid
            or action == "task.release"
            or valid_transition != (terminal_state, terminal_result)
            or (
                action == "task.start"
                and (
                    cleanup_completed is True
                    and reason
                    not in {
                        "task-ttl-expired",
                        "task-released",
                        "task-recovery-resolved",
                    }
                )
            )
            or (
                action in revocation_actions and reason not in {"task-ttl-expired", "task-released"}
            )
            or isinstance(proof.get("terminal_finished_at"), bool)
            or not isinstance(proof.get("terminal_finished_at"), (int, float))
            or not math.isfinite(float(proof["terminal_finished_at"]))
            or float(proof["terminal_finished_at"])
            < float(
                stored_result.get("created_at")
                if action == "task.release"
                else receipt["created_at"]
            )
            or isinstance(retired_at, bool)
            or not isinstance(retired_at, (int, float))
            or not math.isfinite(float(retired_at))
            or receipt["finalized_at"] is None
            or float(retired_at) < float(receipt["finalized_at"])
            or float(retired_at) < float(proof["terminal_finished_at"])
        ):
            raise StateError(
                "Scheduler operation lifecycle proof is internally inconsistent.",
                details={
                    "reason": "operation-receipt-invalid",
                    "operation_id": receipt["operation_id"],
                },
            )
        return proof

    @staticmethod
    def _operation_result(
        receipt: sqlite3.Row,
        *,
        replayed: bool,
    ) -> dict[str, Any]:
        try:
            result = json.loads(receipt["result_json"])
        except (TypeError, ValueError, json.JSONDecodeError) as exc:
            raise StateError(
                "Scheduler operation receipt is unreadable.",
                details={
                    "reason": "operation-receipt-invalid",
                    "operation_id": receipt["operation_id"],
                },
            ) from exc
        if not isinstance(result, dict):
            raise StateError(
                "Scheduler operation receipt is unreadable.",
                details={
                    "reason": "operation-receipt-invalid",
                    "operation_id": receipt["operation_id"],
                },
            )
        terminal_proof = WorkspaceCoordinator._operation_terminal_proof(receipt, result)
        if terminal_proof is not None:
            if "resolution_reason" in terminal_proof:
                result.update(terminal_proof)
            elif set(result).intersection(terminal_proof):
                raise StateError(
                    "Scheduler task-start lifecycle proof collides with its immutable result.",
                    details={
                        "reason": "operation-receipt-invalid",
                        "operation_id": receipt["operation_id"],
                    },
                )
            else:
                result.update(terminal_proof)
        result["operation"] = {
            "operation_id": receipt["operation_id"],
            "fingerprint": receipt["fingerprint"],
            "delivery_digest": receipt_delivery_digest(
                receipt["result_json"], receipt["terminal_json"]
            ),
            "replayed": replayed,
            "delivered": receipt["delivered_at"] is not None,
            "finalized": receipt["finalized_at"] is not None,
        }
        if receipt["action"] == "task.start" and terminal_proof is not None:
            result["operation"]["retired"] = True
        if receipt["action"] == "task.release":
            result["token_cleanup_pending"] = receipt["token_cleanup_path"] is not None
        return result

    @staticmethod
    def _validated_ack_receipt(
        receipt: sqlite3.Row,
        operation_id: str,
        fingerprint: str,
        delivery_digest: str,
    ) -> tuple[dict[str, Any], dict[str, Any], str | None]:
        """Validate durable receipt identity before acknowledgement can mutate anything."""

        if receipt["operation_id"] != operation_id:
            raise StateError(
                "Scheduler operation receipt identity changed.",
                details={"reason": "operation-receipt-invalid", "operation_id": operation_id},
            )
        if receipt["fingerprint"] != fingerprint:
            raise UsageError(
                "Receipt fingerprint does not match its operation ID.",
                details={
                    "reason": "operation-fingerprint-mismatch",
                    "operation_id": operation_id,
                },
            )
        if receipt["finalized_at"] is None:
            raise BusyError(
                "Operation is committed but has not reached its terminal receipt yet.",
                details={
                    "reason": "operation-in-progress",
                    "operation_id": operation_id,
                    "fingerprint": fingerprint,
                },
            )
        try:
            current_delivery_digest = receipt_delivery_digest(
                receipt["result_json"], receipt["terminal_json"]
            )
        except (TypeError, ValueError, UsageError, json.JSONDecodeError) as exc:
            raise StateError(
                "Scheduler operation receipt is unreadable.",
                details={"reason": "operation-receipt-invalid", "operation_id": operation_id},
            ) from exc
        if receipt["delivered_at"] is None and current_delivery_digest != delivery_digest:
            raise UsageError(
                "Receipt delivery digest does not match its current durable result.",
                details={
                    "reason": "operation-delivery-digest-mismatch",
                    "operation_id": operation_id,
                },
            )
        try:
            parameters = parse_canonical_json(receipt["parameters_json"])
            stored_result = parse_canonical_json(receipt["result_json"])
            workspace_id = str(receipt["workspace_id"])
            action = str(receipt["action"])
            owner_token_hash = receipt["owner_token_hash"]
            expected_fingerprint = operation_fingerprint(
                workspace_id,
                action,
                receipt["parameters_json"],
                owner_token_hash,
            )
        except (TypeError, ValueError, UsageError, json.JSONDecodeError) as exc:
            raise StateError(
                "Scheduler operation receipt is unreadable.",
                details={"reason": "operation-receipt-invalid", "operation_id": operation_id},
            ) from exc
        if not isinstance(parameters, dict) or not isinstance(stored_result, dict):
            raise StateError(
                "Scheduler operation receipt payloads must be objects.",
                details={"reason": "operation-receipt-invalid", "operation_id": operation_id},
            )
        workspace = parameters.get("workspace")
        if (
            action not in PUBLIC_MUTATION_ACTIONS
            or not is_sha256_hex(workspace_id)
            or not isinstance(workspace, str)
            or not workspace
            or not os.path.isabs(workspace)
            or os.path.normpath(workspace) != workspace
            or _workspace_id(workspace) != workspace_id
            or expected_fingerprint != fingerprint
        ):
            raise StateError(
                "Scheduler operation receipt identity is internally inconsistent.",
                details={"reason": "operation-receipt-invalid", "operation_id": operation_id},
            )
        receipt_task_id = receipt["task_id"]
        expected_task_id = (
            stored_result.get("id")
            if action in {"task.start", "task.heartbeat", "task.release", "recovery.resolve"}
            else stored_result.get("task_id")
        )
        if action not in {"workspace.register", "workspace.unregister"}:
            if not _entity_id(receipt_task_id) or receipt_task_id != expected_task_id:
                raise StateError(
                    "Scheduler operation receipt lost its indexed task identity.",
                    details={"reason": "operation-receipt-invalid", "operation_id": operation_id},
                )
        elif receipt_task_id is not None:
            raise StateError(
                "Non-lifecycle operation receipt has an unexpected task identity.",
                details={"reason": "operation-receipt-invalid", "operation_id": operation_id},
            )
        cleanup_path = receipt["token_cleanup_path"]
        cleanup_identity = receipt["token_cleanup_identity"]
        if action != "task.release":
            if cleanup_path is not None or cleanup_identity is not None:
                raise StateError(
                    "Scheduler operation receipt has unexpected token cleanup metadata.",
                    details={"reason": "operation-receipt-invalid", "operation_id": operation_id},
                )
            if action == "task.start":
                expected_task_keys = {
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
                numeric_fields = ("created_at", "heartbeat_at", "expires_at")
                ttl = parameters.get("ttl_seconds")
                if (
                    set(parameters)
                    != {
                        "owner",
                        "summary",
                        "token_file_path",
                        "ttl_seconds",
                        "workspace",
                    }
                    or not is_sha256_hex(owner_token_hash)
                    or set(stored_result) != expected_task_keys
                    or not _entity_id(stored_result.get("id"))
                    or stored_result.get("owner") != parameters.get("owner")
                    or stored_result.get("summary") != parameters.get("summary")
                    or stored_result.get("state") != "active"
                    or stored_result.get("finished_at") is not None
                    or stored_result.get("result") is not None
                    or stored_result.get("note") is not None
                    or isinstance(ttl, bool)
                    or not isinstance(ttl, (int, float))
                    or not math.isfinite(float(ttl))
                    or float(ttl) <= 0
                    or any(
                        isinstance(stored_result.get(field), bool)
                        or not isinstance(stored_result.get(field), (int, float))
                        or not math.isfinite(float(stored_result[field]))
                        for field in numeric_fields
                    )
                    or stored_result["heartbeat_at"] != stored_result["created_at"]
                    or not math.isclose(
                        float(stored_result["expires_at"]) - float(stored_result["created_at"]),
                        float(ttl),
                        rel_tol=0.0,
                        abs_tol=1e-9,
                    )
                ):
                    raise StateError(
                        "Task-start receipt result is internally inconsistent.",
                        details={
                            "reason": "operation-receipt-invalid",
                            "operation_id": operation_id,
                        },
                    )
                WorkspaceCoordinator._operation_terminal_proof(receipt, stored_result)
            else:
                terminal_proof = WorkspaceCoordinator._operation_terminal_proof(
                    receipt,
                    stored_result,
                )
                if (
                    terminal_proof is not None
                    and "resolution_reason" not in terminal_proof
                    and set(stored_result).intersection(terminal_proof)
                ):
                    raise StateError(
                        "Scheduler lifecycle proof collides with its immutable result.",
                        details={
                            "reason": "operation-receipt-invalid",
                            "operation_id": operation_id,
                        },
                    )
            if receipt["retired_at"] is not None and receipt["terminal_json"] is None:
                retired_at = receipt["retired_at"]
                retired_wait = action in {"claim.acquire", "freeze.acquire", "task.park"}
                if (
                    action not in {"claim.acquire", "freeze.acquire", "task.park"}
                    or isinstance(retired_at, bool)
                    or not isinstance(retired_at, (int, float))
                    or not math.isfinite(float(retired_at))
                    or float(retired_at) < float(receipt["finalized_at"])
                    or (
                        retired_wait
                        and (
                            stored_result.get("aborted") is not True
                            or stored_result.get("reason")
                            not in {
                                "task-ttl-expired",
                                "task-ttl-expired-with-active-claim",
                                "task-released",
                                "task-released-outcome-unknown",
                            }
                        )
                    )
                ):
                    raise StateError(
                        "Scheduler operation receipt has invalid retirement metadata.",
                        details={
                            "reason": "operation-receipt-invalid",
                            "operation_id": operation_id,
                        },
                    )
                if (
                    retired_wait
                    and receipt["delivered_at"] is None
                    and stored_result.get("reason")
                    in {
                        "task-ttl-expired-with-active-claim",
                        "task-released-outcome-unknown",
                    }
                ):
                    raise BusyError(
                        "Unresolved synthetic lifecycle receipt must be recovered before "
                        "acknowledgement.",
                        details={
                            "reason": "operation-recovery-pending",
                            "operation_id": operation_id,
                            "task_id": receipt_task_id,
                            "next_action": "Resolve the task outcome, then replay and acknowledge.",
                        },
                    )
            return parameters, stored_result, None

        if (receipt["terminal_json"] is None) != (receipt["retired_at"] is None):
            raise StateError(
                "Task-release receipt has incomplete lifecycle retirement metadata.",
                details={"reason": "operation-receipt-invalid", "operation_id": operation_id},
            )

        expected_parameter_keys = {
            "note",
            "result",
            "token_cleanup_path",
            "workspace",
        }
        expected_task_keys = {
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
        release_result = parameters.get("result")
        expected_state = (
            "outcome_unknown" if release_result == "outcome-unknown" else release_result
        )
        numeric_fields = ("created_at", "heartbeat_at", "expires_at", "finished_at")
        if (
            set(parameters) != expected_parameter_keys
            or release_result not in {"completed", "failed", "outcome-unknown"}
            or not is_sha256_hex(owner_token_hash)
            or set(stored_result) != expected_task_keys
            or not _entity_id(stored_result.get("id"))
            or not isinstance(stored_result.get("owner"), str)
            or not stored_result["owner"]
            or not isinstance(stored_result.get("summary"), str)
            or not stored_result["summary"]
            or stored_result.get("state") != expected_state
            or stored_result.get("result") != release_result
            or stored_result.get("note") != parameters.get("note")
            or (parameters.get("note") is not None and not isinstance(parameters.get("note"), str))
            or any(
                isinstance(stored_result.get(field), bool)
                or not isinstance(stored_result.get(field), (int, float))
                or not math.isfinite(float(stored_result[field]))
                for field in numeric_fields
            )
            or float(stored_result["created_at"]) > float(stored_result["heartbeat_at"])
        ):
            raise StateError(
                "Terminal task release receipt result is internally inconsistent.",
                details={"reason": "operation-receipt-invalid", "operation_id": operation_id},
            )
        cleanup_expected = release_result in {"completed", "failed"}
        parameter_path = parameters.get("token_cleanup_path")
        if cleanup_expected:
            if (
                not isinstance(parameter_path, str)
                or not parameter_path
                or not os.path.isabs(parameter_path)
                or os.path.normpath(parameter_path) != parameter_path
                or cleanup_path not in {None, parameter_path}
                or (
                    cleanup_path is not None
                    and cleanup_identity != _token_path_identity(cleanup_path)
                )
                or (cleanup_path is None and cleanup_identity is not None)
                or (cleanup_path is None and receipt["delivered_at"] is None)
            ):
                raise StateError(
                    "Scheduler operation receipt has inconsistent token cleanup metadata.",
                    details={"reason": "operation-receipt-invalid", "operation_id": operation_id},
                )
        elif parameter_path is not None or cleanup_path is not None or cleanup_identity is not None:
            raise StateError(
                "Outcome-unknown receipt has unexpected token cleanup metadata.",
                details={"reason": "operation-receipt-invalid", "operation_id": operation_id},
            )
        if receipt["terminal_json"] is not None:
            WorkspaceCoordinator._operation_terminal_proof(receipt, stored_result)
        return parameters, stored_result, cleanup_path

    @staticmethod
    def _matching_operation_receipt(
        connection: sqlite3.Connection,
        operation: _Operation,
    ) -> sqlite3.Row | None:
        receipt = connection.execute(
            "SELECT * FROM operation_receipts WHERE operation_id = ?",
            (operation.operation_id,),
        ).fetchone()
        if receipt is None:
            return None
        if (
            receipt["workspace_id"] != operation.workspace_id
            or receipt["action"] != operation.action
            or receipt["parameters_json"] != operation.parameters_json
            or receipt["owner_token_hash"] != operation.owner_token_hash
            or receipt["fingerprint"] != operation.fingerprint
        ):
            raise UsageError(
                "Operation ID is already bound to a different mutation.",
                details={
                    "reason": "operation-id-conflict",
                    "operation_id": operation.operation_id,
                    "existing_action": receipt["action"],
                },
            )
        return receipt

    @staticmethod
    def _bound_task_for_start_receipt(
        connection: sqlite3.Connection,
        receipt: sqlite3.Row,
        stored_result: dict[str, Any],
    ) -> tuple[sqlite3.Row | None, dict[str, Any]]:
        try:
            parameters = parse_canonical_json(receipt["parameters_json"])
        except (TypeError, ValueError, UsageError, json.JSONDecodeError) as exc:
            raise StateError(
                "Task-start receipt parameters are unreadable during replay.",
                details={
                    "reason": "operation-receipt-invalid",
                    "operation_id": receipt["operation_id"],
                },
            ) from exc
        if not isinstance(parameters, dict) or not _entity_id(stored_result.get("id")):
            raise StateError(
                "Task-start receipt has no stable task identity during replay.",
                details={
                    "reason": "operation-receipt-invalid",
                    "operation_id": receipt["operation_id"],
                },
            )
        task = connection.execute(
            "SELECT * FROM tasks WHERE id = ?",
            (stored_result["id"],),
        ).fetchone()
        if task is None:
            return None, parameters
        declared_path = parameters.get("token_file_path")
        if (
            task["start_operation_id"] != receipt["operation_id"]
            or task["workspace_id"] != receipt["workspace_id"]
            or task["token_hash"] != receipt["owner_token_hash"]
            or task["owner"] != stored_result.get("owner")
            or task["summary"] != stored_result.get("summary")
            or task["created_at"] != stored_result.get("created_at")
            or (
                declared_path is not None
                and (
                    not isinstance(declared_path, str)
                    or task["token_file_path"] != declared_path
                    or task["token_file_identity"] != _token_path_identity(declared_path)
                )
            )
            or (
                declared_path is None
                and task["state"] in {"active", "outcome_unknown"}
                and (task["token_file_path"] is not None or task["token_file_identity"] is not None)
            )
        ):
            raise StateError(
                "Task-start receipt no longer matches its durable task identity.",
                details={
                    "reason": "operation-receipt-invalid",
                    "operation_id": receipt["operation_id"],
                    "task_id": stored_result["id"],
                    "recovery_required": True,
                },
            )
        return task, parameters

    @staticmethod
    def _validate_terminal_start_replay(
        connection: sqlite3.Connection,
        receipt: sqlite3.Row,
        stored_result: dict[str, Any],
        task: sqlite3.Row | None,
        parameters: dict[str, Any],
    ) -> None:
        proof = WorkspaceCoordinator._operation_terminal_proof(receipt, stored_result)
        if proof is None:
            raise StateError(
                "Terminal task-start receipt has no lifecycle proof.",
                details={
                    "reason": "operation-receipt-invalid",
                    "operation_id": receipt["operation_id"],
                },
            )
        cleanup_completed = proof.get("token_cleanup_completed") is True
        if task is None:
            if cleanup_completed:
                return
            raise StateError(
                "Terminal task-start receipt lost its cleanup obligation.",
                details={
                    "reason": "operation-receipt-invalid",
                    "operation_id": receipt["operation_id"],
                    "recovery_required": True,
                },
            )
        if (
            task["state"] != proof["terminal_state"]
            or task["result"] != proof["terminal_result"]
            or task["finished_at"] != proof["terminal_finished_at"]
        ):
            raise StateError(
                "Terminal task-start proof no longer matches durable task state.",
                details={
                    "reason": "operation-receipt-invalid",
                    "operation_id": receipt["operation_id"],
                    "task_id": task["id"],
                    "recovery_required": True,
                },
            )
        token_identity = task["token_file_identity"]
        if token_identity is None:
            if cleanup_completed or task["state"] == "outcome_unknown":
                return
            raise StateError(
                "Tokenless terminal task-start proof lacks its completion marker.",
                details={
                    "reason": "operation-receipt-invalid",
                    "operation_id": receipt["operation_id"],
                    "task_id": task["id"],
                },
            )
        if cleanup_completed:
            outstanding = connection.execute(
                "SELECT 1 FROM token_cleanup_jobs WHERE task_id = ? "
                "UNION ALL SELECT 1 FROM operation_receipts "
                "WHERE task_id = ? AND token_cleanup_path IS NOT NULL LIMIT 1",
                (task["id"], task["id"]),
            ).fetchone()
            if task["state"] == "outcome_unknown" or outstanding is not None:
                raise StateError(
                    "Task-start cleanup proof conflicts with a durable token obligation.",
                    details={
                        "reason": "operation-receipt-invalid",
                        "operation_id": receipt["operation_id"],
                        "task_id": task["id"],
                        "recovery_required": True,
                    },
                )
            return
        if task["state"] == "outcome_unknown":
            return
        job = connection.execute(
            "SELECT 1 FROM token_cleanup_jobs WHERE task_id = ? "
            "AND token_file_identity = ? AND token_hash = ?",
            (task["id"], token_identity, task["token_hash"]),
        ).fetchone()
        cleanup_receipt = connection.execute(
            "SELECT 1 FROM operation_receipts WHERE action = 'task.release' "
            "AND task_id = ? AND token_cleanup_identity = ? "
            "AND owner_token_hash = ? AND token_cleanup_path IS NOT NULL",
            (task["id"], token_identity, task["token_hash"]),
        ).fetchone()
        if job is None and cleanup_receipt is None:
            raise StateError(
                "Terminal task-start proof has no durable token cleanup obligation.",
                details={
                    "reason": "operation-receipt-invalid",
                    "operation_id": receipt["operation_id"],
                    "task_id": task["id"],
                    "recovery_required": True,
                },
            )

    @staticmethod
    def _replay_operation(
        connection: sqlite3.Connection,
        operation: _Operation,
    ) -> dict[str, Any] | None:
        receipt = WorkspaceCoordinator._matching_operation_receipt(connection, operation)
        if receipt is None:
            return None
        if receipt["finalized_at"] is None:
            raise BusyError(
                "Operation is committed but has not reached its terminal receipt yet.",
                details={
                    "reason": "operation-in-progress",
                    "operation_id": operation.operation_id,
                    "fingerprint": operation.fingerprint,
                },
            )
        if operation.action == "task.start":
            try:
                stored_result = parse_canonical_json(receipt["result_json"])
            except (TypeError, ValueError, UsageError, json.JSONDecodeError) as exc:
                raise StateError(
                    "Task-start receipt is unreadable during replay.",
                    details={
                        "reason": "operation-receipt-invalid",
                        "operation_id": operation.operation_id,
                    },
                ) from exc
            if not isinstance(stored_result, dict):
                raise StateError(
                    "Task-start receipt has no stable task identity during replay.",
                    details={
                        "reason": "operation-receipt-invalid",
                        "operation_id": operation.operation_id,
                    },
                )
            task, parameters = WorkspaceCoordinator._bound_task_for_start_receipt(
                connection,
                receipt,
                stored_result,
            )
            if receipt["terminal_json"] is not None:
                WorkspaceCoordinator._validate_terminal_start_replay(
                    connection,
                    receipt,
                    stored_result,
                    task,
                    parameters,
                )
                return WorkspaceCoordinator._operation_result(receipt, replayed=True)
            if task is None:
                raise StateError(
                    "Task-start receipt lost its current lifecycle identity.",
                    details={
                        "reason": "task-start-state-missing",
                        "operation_id": operation.operation_id,
                        "task_id": stored_result["id"],
                        "recovery_required": True,
                    },
                )
            if task["state"] != "active":
                cleanup_pending = connection.execute(
                    "SELECT 1 FROM token_cleanup_jobs WHERE task_id = ?",
                    (stored_result["id"],),
                ).fetchone()
                raise BusyError(
                    "Task-start replay is waiting for terminal lifecycle cleanup.",
                    details={
                        "reason": (
                            "task-start-cleanup-pending"
                            if cleanup_pending is not None
                            else "task-start-terminal"
                        ),
                        "operation_id": operation.operation_id,
                        "task_id": stored_result["id"],
                        "task_state": task["state"],
                    },
                )
            now = time.time()
            try:
                expires_at = float(task["expires_at"])
            except (TypeError, ValueError) as exc:
                raise StateError(
                    "Task-start replay has invalid durable expiry metadata.",
                    details={
                        "reason": "operation-receipt-invalid",
                        "operation_id": operation.operation_id,
                        "task_id": stored_result["id"],
                    },
                ) from exc
            if not math.isfinite(now) or not math.isfinite(expires_at) or expires_at <= now:
                raise BusyError(
                    "Task-start receipt expired before replay could be proven active.",
                    details={
                        "reason": "task-start-receipt-expired-unmaintained",
                        "operation_id": operation.operation_id,
                        "task_id": stored_result["id"],
                        "recovery_required": True,
                    },
                )
        return WorkspaceCoordinator._operation_result(receipt, replayed=True)

    @staticmethod
    def _replay_or_missing(
        connection: sqlite3.Connection,
        operation: _Operation,
        *,
        receipt_only: bool,
    ) -> dict[str, Any] | None:
        replay = WorkspaceCoordinator._replay_operation(connection, operation)
        if replay is None and receipt_only:
            raise StateError(
                "Operation receipt does not exist.",
                details={
                    "reason": "operation-receipt-missing",
                    "operation_id": operation.operation_id,
                    "fingerprint": operation.fingerprint,
                },
            )
        return replay

    @staticmethod
    def _wait_operation_state(
        connection: sqlite3.Connection,
        operation: _Operation,
        *,
        receipt_only: bool,
    ) -> tuple[dict[str, Any] | None, sqlite3.Row | None]:
        receipt = WorkspaceCoordinator._matching_operation_receipt(connection, operation)
        if receipt is None:
            if receipt_only:
                raise StateError(
                    "Operation receipt does not exist.",
                    details={
                        "reason": "operation-receipt-missing",
                        "operation_id": operation.operation_id,
                        "fingerprint": operation.fingerprint,
                    },
                )
            return None, None
        if receipt["finalized_at"] is not None:
            return WorkspaceCoordinator._operation_result(receipt, replayed=True), None
        if receipt_only:
            raise BusyError(
                "Operation is committed but has not reached its terminal receipt yet.",
                details={
                    "reason": "operation-in-progress",
                    "operation_id": operation.operation_id,
                    "fingerprint": operation.fingerprint,
                },
            )
        return None, receipt

    @staticmethod
    def _record_operation(
        connection: sqlite3.Connection,
        operation: _Operation,
        result: dict[str, Any],
        *,
        finalized: bool = True,
        token_cleanup_path: str | None = None,
        capacity_before: int | None = None,
    ) -> dict[str, Any]:
        now = time.time()
        if not math.isfinite(now):
            raise StateError(
                "System clock is invalid; operation receipt cannot be recorded safely.",
                details={"reason": "system-clock-invalid"},
            )
        task_id: str | None = None
        if operation.action not in {"workspace.register", "workspace.unregister"}:
            candidate_task_id = (
                result.get("id")
                if operation.action
                in {"task.start", "task.heartbeat", "task.release", "recovery.resolve"}
                else result.get("task_id")
            )
            if not _entity_id(candidate_task_id):
                raise StateError(
                    "Task-owned operation result has no stable task identity.",
                    details={
                        "reason": "operation-receipt-invalid",
                        "operation_id": operation.operation_id,
                    },
                )
            task_id = str(candidate_task_id)
        WorkspaceCoordinator._require_operation_receipt_admission(
            connection,
            operation.action,
            task_id,
            adds_cleanup_receipt=token_cleanup_path is not None,
            capacity_before=capacity_before,
        )
        connection.execute(
            "INSERT INTO operation_receipts("
            "operation_id, workspace_id, action, parameters_json, owner_token_hash, "
            "fingerprint, task_id, result_json, token_cleanup_path, token_cleanup_identity, "
            "created_at, finalized_at, delivered_at) "
            "VALUES(?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, NULL)",
            (
                operation.operation_id,
                operation.workspace_id,
                operation.action,
                operation.parameters_json,
                operation.owner_token_hash,
                operation.fingerprint,
                task_id,
                canonical_json(result),
                token_cleanup_path,
                (
                    _token_path_identity(token_cleanup_path)
                    if token_cleanup_path is not None
                    else None
                ),
                now,
                now if finalized else None,
            ),
        )
        receipt = connection.execute(
            "SELECT * FROM operation_receipts WHERE operation_id = ?",
            (operation.operation_id,),
        ).fetchone()
        assert receipt is not None
        return WorkspaceCoordinator._operation_result(receipt, replayed=False)

    @staticmethod
    def _update_operation_result(
        connection: sqlite3.Connection,
        operation: _Operation,
        result: dict[str, Any],
    ) -> dict[str, Any]:
        finalized_at = time.time()
        if not math.isfinite(finalized_at):
            raise StateError(
                "System clock is invalid; operation receipt cannot be finalized safely.",
                details={"reason": "system-clock-invalid"},
            )
        updated = connection.execute(
            "UPDATE operation_receipts SET result_json = ?, "
            "finalized_at = MAX(?, created_at) "
            "WHERE operation_id = ? AND fingerprint = ? "
            "AND finalized_at IS NULL AND delivered_at IS NULL",
            (
                canonical_json(result),
                finalized_at,
                operation.operation_id,
                operation.fingerprint,
            ),
        )
        WorkspaceCoordinator._prune_terminal_claims(connection)
        receipt = connection.execute(
            "SELECT * FROM operation_receipts WHERE operation_id = ?",
            (operation.operation_id,),
        ).fetchone()
        if receipt is None or receipt["fingerprint"] != operation.fingerprint:
            raise StateError(
                "Scheduler operation receipt disappeared or changed.",
                details={
                    "reason": "operation-receipt-missing",
                    "operation_id": operation.operation_id,
                },
            )
        return WorkspaceCoordinator._operation_result(
            receipt,
            replayed=updated.rowcount == 0,
        )

    @staticmethod
    def _prune_delivered_operations(connection: sqlite3.Connection) -> None:
        candidates = connection.execute(
            "SELECT receipt.*, task.id AS bound_task_id, "
            "task.workspace_id AS task_workspace_id, task.token_hash AS task_token_hash, "
            "task.state AS task_state, task.result AS task_result, "
            "task.finished_at AS task_finished_at, task.start_operation_id, "
            "EXISTS(SELECT 1 FROM token_cleanup_jobs AS job "
            "WHERE job.task_id = receipt.task_id) AS has_cleanup_job, "
            "EXISTS(SELECT 1 FROM operation_receipts AS cleanup "
            "WHERE cleanup.task_id = receipt.task_id "
            "AND cleanup.token_cleanup_path IS NOT NULL) AS has_cleanup_receipt "
            "FROM operation_receipts AS receipt "
            "LEFT JOIN tasks AS task ON task.id = receipt.task_id "
            "WHERE (receipt.delivered_at IS NOT NULL OR receipt.retired_at IS NOT NULL) "
            "AND receipt.token_cleanup_path IS NULL "
            "AND (receipt.action <> 'task.start' OR ("
            "NOT EXISTS(SELECT 1 FROM tasks AS open_task "
            "WHERE open_task.id = receipt.task_id "
            "AND open_task.state IN ('active', 'outcome_unknown')) "
            "AND NOT EXISTS(SELECT 1 FROM token_cleanup_jobs AS protected_job "
            "WHERE protected_job.task_id = receipt.task_id) "
            "AND NOT EXISTS(SELECT 1 FROM operation_receipts AS protected_cleanup "
            "WHERE protected_cleanup.task_id = receipt.task_id "
            "AND protected_cleanup.token_cleanup_path IS NOT NULL))) "
            "ORDER BY COALESCE(receipt.delivered_at, receipt.retired_at) DESC, "
            "receipt.created_at DESC, receipt.operation_id DESC "
            "LIMIT -1 OFFSET ?",
            (DELIVERED_OPERATION_RETENTION,),
        ).fetchall()
        for receipt in candidates:
            if receipt["action"] == "task.start":
                if not _entity_id(receipt["task_id"]):
                    raise StateError(
                        "Task-start receipt has no stable task identity during retention.",
                        details={
                            "reason": "operation-receipt-invalid",
                            "operation_id": receipt["operation_id"],
                        },
                    )
                try:
                    stored_result = parse_canonical_json(receipt["result_json"])
                except (TypeError, ValueError, UsageError, json.JSONDecodeError) as exc:
                    raise StateError(
                        "Task-start receipt is unreadable during retention.",
                        details={
                            "reason": "operation-receipt-invalid",
                            "operation_id": receipt["operation_id"],
                        },
                    ) from exc
                proof = WorkspaceCoordinator._operation_terminal_proof(
                    receipt,
                    stored_result,
                )
                if receipt["bound_task_id"] is None:
                    if proof is None or proof.get("token_cleanup_completed") is not True:
                        raise StateError(
                            "Detached task-start receipt has no completed cleanup proof.",
                            details={
                                "reason": "operation-receipt-invalid",
                                "operation_id": receipt["operation_id"],
                            },
                        )
                elif receipt["task_state"] in {"active", "outcome_unknown"}:
                    continue
                else:
                    if (
                        receipt["task_state"] not in TERMINAL_TASK_STATES
                        or receipt["start_operation_id"] != receipt["operation_id"]
                        or receipt["task_workspace_id"] != receipt["workspace_id"]
                        or receipt["task_token_hash"] != receipt["owner_token_hash"]
                        or proof is None
                        or proof.get("token_cleanup_completed") is not True
                        or receipt["task_state"] != proof.get("terminal_state")
                        or receipt["task_result"] != proof.get("terminal_result")
                        or receipt["task_finished_at"] != proof.get("terminal_finished_at")
                    ):
                        raise StateError(
                            "Terminal task-start receipt cannot be detached safely.",
                            details={
                                "reason": "operation-receipt-invalid",
                                "operation_id": receipt["operation_id"],
                            },
                        )
                    if receipt["has_cleanup_job"] or receipt["has_cleanup_receipt"]:
                        continue
                    detached = connection.execute(
                        "UPDATE tasks SET start_operation_id = NULL "
                        "WHERE id = ? AND start_operation_id = ? AND workspace_id = ? "
                        "AND state = ? AND result = ? AND finished_at = ?",
                        (
                            receipt["task_id"],
                            receipt["operation_id"],
                            receipt["workspace_id"],
                            receipt["task_state"],
                            receipt["task_result"],
                            receipt["task_finished_at"],
                        ),
                    )
                    if detached.rowcount != 1:
                        raise StateError(
                            "Terminal task-start receipt changed during retention detach.",
                            details={
                                "reason": "operation-receipt-invalid",
                                "operation_id": receipt["operation_id"],
                            },
                        )
            connection.execute(
                "DELETE FROM operation_receipts WHERE operation_id = ? "
                "AND (delivered_at IS NOT NULL OR retired_at IS NOT NULL) "
                "AND token_cleanup_path IS NULL",
                (receipt["operation_id"],),
            )

    @staticmethod
    def _prune_task_lifecycle_operations(
        connection: sqlite3.Connection,
        task_id: str,
    ) -> None:
        connection.execute(
            "DELETE FROM operation_receipts WHERE operation_id IN ("
            "SELECT operation_id FROM operation_receipts WHERE task_id = ? "
            "AND action IN ('task.heartbeat', 'claim.acquire', 'freeze.acquire', 'task.park') "
            "AND (delivered_at IS NOT NULL OR retired_at IS NOT NULL) "
            "AND token_cleanup_path IS NULL "
            "ORDER BY COALESCE(delivered_at, retired_at) DESC, created_at DESC, "
            "operation_id DESC LIMIT -1 OFFSET ?) "
            "AND (delivered_at IS NOT NULL OR retired_at IS NOT NULL) "
            "AND token_cleanup_path IS NULL",
            (task_id, TASK_SAFE_LIFECYCLE_RETENTION),
        )

    @staticmethod
    def _require_operation_receipt_admission(
        connection: sqlite3.Connection,
        action: str,
        task_id: str | None,
        *,
        adds_cleanup_receipt: bool,
        capacity_before: int | None,
    ) -> None:
        WorkspaceCoordinator._prune_terminal_claims(connection)
        if task_id is not None and action in {
            "task.start",
            "task.heartbeat",
            "claim.acquire",
            "freeze.acquire",
            "task.park",
        }:
            WorkspaceCoordinator._prune_task_lifecycle_operations(connection, task_id)
            task_backlog = int(
                connection.execute(
                    "SELECT COUNT(*) FROM operation_receipts WHERE task_id = ? "
                    "AND action IN ('task.start', 'task.heartbeat', 'claim.acquire', "
                    "'freeze.acquire', 'task.park') AND delivered_at IS NULL "
                    "AND retired_at IS NULL",
                    (task_id,),
                ).fetchone()[0]
            )
            if task_backlog >= TASK_LIFECYCLE_OPERATION_LIMIT:
                raise BusyError(
                    "Task operation receipt backlog is full.",
                    details={
                        "reason": "task-operation-receipt-backlog-full",
                        "task_id": task_id,
                        "replay_required_receipts": task_backlog,
                        "limit": TASK_LIFECYCLE_OPERATION_LIMIT,
                        "next_action": "Acknowledge durable task receipts, then retry.",
                    },
                )
        capacity = WorkspaceCoordinator._operation_capacity(connection)
        replay_required = capacity["replay_required_receipts"]
        registered_workspaces = capacity["registered_workspaces"]
        active_tasks = capacity["active_tasks"]
        outcome_unknown_tasks = capacity["outcome_unknown_tasks"]
        open_claims = capacity["open_claims"]
        cleanup_jobs = capacity["token_cleanup_jobs"]
        cleanup_receipts = capacity["token_cleanup_receipts"]
        reserved_capacity = capacity["reserved_capacity"]
        projected_capacity = reserved_capacity + 1 + int(adds_cleanup_receipt)
        migration_drain = (
            capacity_before is not None
            and capacity_before > REPLAY_REQUIRED_OPERATION_LIMIT
            and reserved_capacity < capacity_before
            and projected_capacity <= capacity_before
        )
        if projected_capacity > REPLAY_REQUIRED_OPERATION_LIMIT and not migration_drain:
            raise BusyError(
                "Scheduler durable operation capacity is full.",
                details={
                    "reason": "operation-receipt-backlog-full",
                    "replay_required_receipts": replay_required,
                    "registered_workspaces": registered_workspaces,
                    "active_tasks": active_tasks,
                    "outcome_unknown_tasks": outcome_unknown_tasks,
                    "open_claims": open_claims,
                    "token_cleanup_jobs": cleanup_jobs,
                    "token_cleanup_receipts": cleanup_receipts,
                    "reserved_capacity": reserved_capacity,
                    "projected_capacity": projected_capacity,
                    "capacity_before": capacity_before,
                    "limit": REPLAY_REQUIRED_OPERATION_LIMIT,
                    "next_action": (
                        "Acknowledge durable receipts or finish an open task, claim, cleanup, "
                        "or workspace lifecycle, then retry."
                    ),
                },
            )

    @staticmethod
    def _operation_capacity(connection: sqlite3.Connection) -> dict[str, int]:
        replay_required = int(
            connection.execute(
                "SELECT COUNT(*) FROM operation_receipts "
                "WHERE delivered_at IS NULL AND retired_at IS NULL"
            ).fetchone()[0]
        )
        registered_workspaces = int(
            connection.execute("SELECT COUNT(*) FROM workspaces").fetchone()[0]
        )
        active_tasks = int(
            connection.execute("SELECT COUNT(*) FROM tasks WHERE state = 'active'").fetchone()[0]
        )
        outcome_unknown_tasks = int(
            connection.execute(
                "SELECT COUNT(*) FROM tasks WHERE state = 'outcome_unknown'"
            ).fetchone()[0]
        )
        open_claims = int(
            connection.execute(
                "SELECT COUNT(*) FROM claims WHERE state IN ('queued', 'active', 'parked')"
            ).fetchone()[0]
        )
        cleanup_jobs = int(
            connection.execute("SELECT COUNT(*) FROM token_cleanup_jobs").fetchone()[0]
        )
        cleanup_receipts = int(
            connection.execute(
                "SELECT COUNT(*) FROM operation_receipts WHERE token_cleanup_path IS NOT NULL"
            ).fetchone()[0]
        )
        reserved_capacity = (
            replay_required
            + registered_workspaces
            + (3 * active_tasks)
            + (2 * outcome_unknown_tasks)
            + open_claims
            + cleanup_jobs
            + cleanup_receipts
        )
        return {
            "replay_required_receipts": replay_required,
            "registered_workspaces": registered_workspaces,
            "active_tasks": active_tasks,
            "outcome_unknown_tasks": outcome_unknown_tasks,
            "open_claims": open_claims,
            "token_cleanup_jobs": cleanup_jobs,
            "token_cleanup_receipts": cleanup_receipts,
            "reserved_capacity": reserved_capacity,
        }

    @staticmethod
    def _causal_time(raw_time: float, *stored_times: object) -> float:
        try:
            values = [float(raw_time)]
            values.extend(float(value) for value in stored_times if value is not None)
        except (TypeError, ValueError, OverflowError) as exc:
            raise StateError(
                "Scheduler lifecycle contains invalid timing evidence.",
                details={"reason": "scheduler-time-invalid", "recovery_required": True},
            ) from exc
        if any(not math.isfinite(value) for value in values):
            raise StateError(
                "Scheduler lifecycle contains non-finite timing evidence.",
                details={"reason": "scheduler-time-invalid", "recovery_required": True},
            )
        return max(values)

    @staticmethod
    def _claim_transition_time(claim: sqlite3.Row, raw_time: float) -> float:
        return WorkspaceCoordinator._causal_time(
            raw_time,
            claim["created_at"],
            claim["granted_at"],
            claim["released_at"],
        )

    @staticmethod
    def _task_transition_time(
        connection: sqlite3.Connection,
        task_id: str,
        raw_time: float,
    ) -> float:
        task = connection.execute(
            "SELECT created_at, heartbeat_at, finished_at FROM tasks WHERE id = ?",
            (task_id,),
        ).fetchone()
        if task is None:
            raise StateError(
                "Task lifecycle timing cannot be proven because the task is missing.",
                details={"reason": "task-state-missing", "task_id": task_id},
            )
        receipt_times = connection.execute(
            "SELECT MAX(created_at) AS created_at, MAX(finalized_at) AS finalized_at, "
            "MAX(delivered_at) AS delivered_at, MAX(retired_at) AS retired_at "
            "FROM operation_receipts WHERE task_id = ?",
            (task_id,),
        ).fetchone()
        claim_times = connection.execute(
            "SELECT MAX(created_at) AS created_at, MAX(granted_at) AS granted_at, "
            "MAX(released_at) AS released_at FROM claims WHERE task_id = ?",
            (task_id,),
        ).fetchone()
        assert receipt_times is not None and claim_times is not None
        return WorkspaceCoordinator._causal_time(
            raw_time,
            task["created_at"],
            task["heartbeat_at"],
            task["finished_at"],
            receipt_times["created_at"],
            receipt_times["finalized_at"],
            receipt_times["delivered_at"],
            receipt_times["retired_at"],
            claim_times["created_at"],
            claim_times["granted_at"],
            claim_times["released_at"],
        )

    @staticmethod
    def _prune_terminal_claims(connection: sqlite3.Connection) -> None:
        connection.execute(
            "DELETE FROM claims WHERE id IN ("
            "SELECT id FROM claims WHERE state IN ('released', 'cancelled') "
            "ORDER BY COALESCE(released_at, created_at) DESC, created_at DESC, id DESC "
            "LIMIT -1 OFFSET ?) AND state IN ('released', 'cancelled')",
            (TERMINAL_CLAIM_RETENTION,),
        )

    @staticmethod
    def _retire_terminal_task_lifecycle_receipts(
        connection: sqlite3.Connection,
        workspace_id: str,
        task_id: str,
        delivered_at: float,
    ) -> int:
        retired = 0
        receipts = connection.execute(
            "SELECT operation_id, finalized_at FROM operation_receipts "
            "WHERE workspace_id = ? AND task_id = ? "
            "AND action IN ('task.start', 'task.heartbeat') "
            "AND finalized_at IS NOT NULL AND delivered_at IS NULL",
            (workspace_id, task_id),
        ).fetchall()
        for receipt in receipts:
            updated = connection.execute(
                "UPDATE operation_receipts SET delivered_at = MAX(?, finalized_at) "
                "WHERE operation_id = ? AND finalized_at IS NOT NULL "
                "AND delivered_at IS NULL",
                (delivered_at, receipt["operation_id"]),
            )
            retired += updated.rowcount
        return retired

    @staticmethod
    def _mark_released_task_start_token_cleanup_completed(
        connection: sqlite3.Connection,
        workspace_id: str,
        task_id: str,
        completed_at: float,
    ) -> int:
        task = connection.execute(
            "SELECT state, result, finished_at, start_operation_id FROM tasks "
            "WHERE id = ? AND workspace_id = ?",
            (task_id, workspace_id),
        ).fetchone()
        if (
            task is None
            or task["state"] not in {"completed", "failed"}
            or task["result"] != task["state"]
            or task["finished_at"] is None
        ):
            raise StateError(
                "Released task token cleanup has no exact terminal task state.",
                details={"reason": "operation-receipt-invalid", "task_id": task_id},
            )
        receipt = connection.execute(
            "SELECT * FROM operation_receipts WHERE workspace_id = ? AND task_id = ? "
            "AND action = 'task.start'",
            (workspace_id, task_id),
        ).fetchone()
        if receipt is None:
            if task["start_operation_id"] is not None:
                raise StateError(
                    "Released task lost its linked task-start receipt.",
                    details={"reason": "operation-receipt-invalid", "task_id": task_id},
                )
            connection.execute(
                "UPDATE tasks SET token_file_path = NULL, token_file_identity = NULL "
                "WHERE id = ? AND workspace_id = ? AND start_operation_id IS NULL",
                (task_id, workspace_id),
            )
            return 0
        try:
            proof = parse_canonical_json(receipt["terminal_json"])
        except (TypeError, ValueError, UsageError, json.JSONDecodeError) as exc:
            raise StateError(
                "Released task-start receipt has unreadable lifecycle proof.",
                details={
                    "reason": "operation-receipt-invalid",
                    "operation_id": receipt["operation_id"],
                },
            ) from exc
        expected = {
            "aborted": True,
            "reason": "task-released",
            "terminal_finished_at": task["finished_at"],
            "terminal_result": task["result"],
            "terminal_state": task["state"],
        }
        if proof == expected | {"token_cleanup_completed": True}:
            connection.execute(
                "UPDATE tasks SET token_file_path = NULL, token_file_identity = NULL "
                "WHERE id = ? AND workspace_id = ?",
                (task_id, workspace_id),
            )
            return 0
        if proof != expected:
            raise StateError(
                "Released task-start receipt has conflicting lifecycle proof.",
                details={
                    "reason": "operation-receipt-invalid",
                    "operation_id": receipt["operation_id"],
                },
            )
        connection.execute(
            "UPDATE operation_receipts SET terminal_json = ?, retired_at = MAX(?, retired_at) "
            "WHERE operation_id = ?",
            (
                canonical_json(expected | {"token_cleanup_completed": True}),
                max(float(completed_at), float(task["finished_at"])),
                receipt["operation_id"],
            ),
        )
        connection.execute(
            "UPDATE tasks SET token_file_path = NULL, token_file_identity = NULL "
            "WHERE id = ? AND workspace_id = ?",
            (task_id, workspace_id),
        )
        return 1

    @staticmethod
    def _task_token_cleanup_conflict(
        connection: sqlite3.Connection,
        token_file_path: str | None,
        token_hash: str | None = None,
    ) -> sqlite3.Row | None:
        if token_hash is not None:
            job = connection.execute(
                "SELECT task_id, NULL AS operation_id, token_hash AS owner_token_hash, "
                "token_file_path AS token_cleanup_path FROM token_cleanup_jobs "
                "WHERE token_hash = ? LIMIT 1",
                (token_hash,),
            ).fetchone()
            if job is not None:
                return job
            receipt = connection.execute(
                "SELECT operation_id, NULL AS task_id, owner_token_hash, token_cleanup_path "
                "FROM operation_receipts WHERE token_cleanup_path IS NOT NULL "
                "AND owner_token_hash = ? LIMIT 1",
                (token_hash,),
            ).fetchone()
            if receipt is not None:
                return receipt
        if token_file_path is not None:
            path_identity = _token_path_identity(token_file_path)
            job = connection.execute(
                "SELECT task_id, NULL AS operation_id, token_hash AS owner_token_hash, "
                "token_file_path AS token_cleanup_path FROM token_cleanup_jobs "
                "WHERE token_file_identity = ? LIMIT 1",
                (path_identity,),
            ).fetchone()
            if job is not None:
                return job
            receipt = connection.execute(
                "SELECT operation_id, NULL AS task_id, owner_token_hash, token_cleanup_path "
                "FROM operation_receipts WHERE token_cleanup_identity = ? LIMIT 1",
                (path_identity,),
            ).fetchone()
            if receipt is not None:
                return receipt
        return None

    @staticmethod
    def _open_task_token_conflict(
        connection: sqlite3.Connection,
        token_file_path: str,
        token_hash: str | None = None,
        *,
        exclude_task_id: str | None = None,
    ) -> sqlite3.Row | None:
        exclusion = " AND id <> ?" if exclude_task_id is not None else ""
        if token_hash is not None:
            task = connection.execute(
                "SELECT id, workspace_id, token_hash, token_file_path FROM tasks "
                "WHERE state IN ('active', 'outcome_unknown') AND token_hash = ?"
                + exclusion
                + " LIMIT 1",
                ((token_hash, exclude_task_id) if exclude_task_id is not None else (token_hash,)),
            ).fetchone()
            if task is not None:
                return task
        return connection.execute(
            "SELECT id, workspace_id, token_hash, token_file_path FROM tasks "
            "WHERE state IN ('active', 'outcome_unknown') AND token_file_identity = ?"
            + exclusion
            + " LIMIT 1",
            (
                (_token_path_identity(token_file_path), exclude_task_id)
                if exclude_task_id is not None
                else (_token_path_identity(token_file_path),)
            ),
        ).fetchone()

    @staticmethod
    def _require_token_cleanup_admission(connection: sqlite3.Connection) -> None:
        cleanup_jobs = int(
            connection.execute("SELECT COUNT(*) FROM token_cleanup_jobs").fetchone()[0]
        )
        cleanup_receipts = int(
            connection.execute(
                "SELECT COUNT(*) FROM operation_receipts WHERE token_cleanup_path IS NOT NULL"
            ).fetchone()[0]
        )
        open_token_tasks = int(
            connection.execute(
                "SELECT COUNT(*) FROM tasks WHERE state IN ('active', 'outcome_unknown')"
            ).fetchone()[0]
        )
        cleanup_backlog = cleanup_jobs + cleanup_receipts + open_token_tasks
        if cleanup_backlog >= TOKEN_CLEANUP_BACKLOG_LIMIT:
            raise BusyError(
                "Claimless-expiry token cleanup backlog is full.",
                details={
                    "reason": "task-token-cleanup-backlog-full",
                    "token_cleanup_jobs": cleanup_jobs,
                    "token_cleanup_receipts": cleanup_receipts,
                    "open_token_tasks": open_token_tasks,
                    "token_cleanup_obligations": cleanup_backlog,
                    "limit": TOKEN_CLEANUP_BACKLOG_LIMIT,
                    "next_action": (
                        "Inspect workspace status token_cleanup_jobs and repair the exact "
                        "token identity or access failure, then retry."
                    ),
                    "recovery_required": True,
                },
            )

    @staticmethod
    def _validated_token_cleanup_job(
        connection: sqlite3.Connection,
        task_id: str,
    ) -> sqlite3.Row | None:
        job = connection.execute(
            "SELECT * FROM token_cleanup_jobs WHERE task_id = ?",
            (task_id,),
        ).fetchone()
        if job is None:
            return None
        task = connection.execute(
            "SELECT * FROM tasks WHERE id = ?",
            (task_id,),
        ).fetchone()
        path = job["token_file_path"]
        path_identity = job["token_file_identity"]
        created_at = job["created_at"]
        completed_at = job["completed_at"]
        last_attempt_at = job["last_attempt_at"]
        attempt_count = job["attempt_count"]
        job_reason = job["reason"]
        valid_task_transition = False
        if task is not None:
            valid_task_transition = (
                job_reason == "claimless-task-expired"
                and task["state"] == "expired"
                and task["result"] == "expired"
            ) or (
                job_reason == "recovered-task-terminal"
                and task["state"] in {"completed", "failed"}
                and task["result"] == f"recovered-{task['state']}"
            )
        if (
            task is None
            or task["workspace_id"] != job["workspace_id"]
            or not valid_task_transition
            or task["token_file_path"] != path
            or task["token_file_identity"] != path_identity
            or task["token_hash"] != job["token_hash"]
            or not isinstance(path, str)
            or not path
            or not os.path.isabs(path)
            or os.path.normpath(path) != path
            or path_identity != _token_path_identity(path)
            or not is_sha256_hex(job["token_hash"])
            or isinstance(created_at, bool)
            or not isinstance(created_at, (int, float))
            or not math.isfinite(float(created_at))
            or (
                completed_at is not None
                and (
                    isinstance(completed_at, bool)
                    or not isinstance(completed_at, (int, float))
                    or not math.isfinite(float(completed_at))
                    or float(completed_at) < float(created_at)
                )
            )
            or (
                last_attempt_at is not None
                and (
                    isinstance(last_attempt_at, bool)
                    or not isinstance(last_attempt_at, (int, float))
                    or not math.isfinite(float(last_attempt_at))
                    or float(last_attempt_at) < float(created_at)
                )
            )
            or isinstance(attempt_count, bool)
            or not isinstance(attempt_count, int)
            or attempt_count < 0
        ):
            raise StateError(
                "Task-token cleanup job is internally inconsistent.",
                details={
                    "reason": "token-cleanup-job-invalid",
                    "task_id": task_id,
                    "recovery_required": True,
                },
            )
        start_receipts = connection.execute(
            "SELECT * FROM operation_receipts WHERE action = 'task.start' AND task_id = ?",
            (task_id,),
        ).fetchall()
        if len(start_receipts) != 1:
            raise StateError(
                "Task-token cleanup job has no unique task-start receipt.",
                details={
                    "reason": "token-cleanup-job-invalid",
                    "task_id": task_id,
                    "recovery_required": True,
                },
            )
        start_receipt = start_receipts[0]
        try:
            parameters, stored_result, _ = WorkspaceCoordinator._validated_ack_receipt(
                start_receipt,
                str(start_receipt["operation_id"]),
                str(start_receipt["fingerprint"]),
                receipt_delivery_digest(
                    start_receipt["result_json"], start_receipt["terminal_json"]
                ),
            )
        except StateError as exc:
            raise StateError(
                "Task-token cleanup job has an invalid task-start receipt.",
                details={
                    "reason": "token-cleanup-job-invalid",
                    "task_id": task_id,
                    "recovery_required": True,
                },
            ) from exc
        try:
            terminal_proof = parse_canonical_json(start_receipt["terminal_json"])
        except (TypeError, ValueError, UsageError, json.JSONDecodeError) as exc:
            raise StateError(
                "Task-token cleanup job has an unreadable task-start lifecycle proof.",
                details={
                    "reason": "token-cleanup-job-invalid",
                    "task_id": task_id,
                    "recovery_required": True,
                },
            ) from exc
        expected_terminal_proof = {
            "aborted": True,
            "reason": (
                "task-ttl-expired"
                if job_reason == "claimless-task-expired"
                else "task-recovery-resolved"
            ),
            "terminal_finished_at": task["finished_at"],
            "terminal_result": task["result"],
            "terminal_state": task["state"],
        }
        if (
            start_receipt["workspace_id"] != job["workspace_id"]
            or start_receipt["owner_token_hash"] != job["token_hash"]
            or terminal_proof != expected_terminal_proof
            or start_receipt["retired_at"] is None
            or parameters.get("token_file_path") != path
            or stored_result.get("id") != task_id
        ):
            raise StateError(
                "Task-token cleanup job is not bound to its task-start receipt.",
                details={
                    "reason": "token-cleanup-job-invalid",
                    "task_id": task_id,
                    "recovery_required": True,
                },
            )
        return job

    @staticmethod
    def _retire_post_cleanup_lifecycle_receipts(
        connection: sqlite3.Connection,
        workspace_id: str,
        task_id: str,
        retired_at: float,
    ) -> int:
        """Fence release/cancel receipts only after the owner token is gone."""

        task = connection.execute(
            "SELECT state, result, finished_at FROM tasks WHERE id = ? AND workspace_id = ?",
            (task_id, workspace_id),
        ).fetchone()
        if (
            task is None
            or task["state"] not in {"completed", "failed", "expired"}
            or task["finished_at"] is None
        ):
            raise StateError(
                "Post-cleanup lifecycle proof has no exact terminal task state.",
                details={"reason": "operation-receipt-invalid", "task_id": task_id},
            )
        if task["state"] == "expired":
            proof_reason = "task-ttl-expired"
            resolution = False
        elif task["result"] == f"recovered-{task['state']}":
            proof_reason = "task-recovery-resolved"
            resolution = True
        elif task["result"] == task["state"]:
            proof_reason = "task-released"
            resolution = False
        else:
            raise StateError(
                "Post-cleanup lifecycle proof does not match the terminal task result.",
                details={"reason": "operation-receipt-invalid", "task_id": task_id},
            )

        receipts = connection.execute(
            "SELECT * FROM operation_receipts WHERE workspace_id = ? AND task_id = ? "
            "AND action IN ('claim.release', 'queue.cancel', 'task.release') "
            "AND finalized_at IS NOT NULL",
            (workspace_id, task_id),
        ).fetchall()
        retired = 0
        for receipt in receipts:
            try:
                _, result, _ = WorkspaceCoordinator._validated_ack_receipt(
                    receipt,
                    str(receipt["operation_id"]),
                    str(receipt["fingerprint"]),
                    receipt_delivery_digest(receipt["result_json"], receipt["terminal_json"]),
                )
            except (SchedulerError, TypeError, ValueError, json.JSONDecodeError) as exc:
                raise StateError(
                    "Post-cleanup lifecycle receipt is internally inconsistent.",
                    details={
                        "reason": "operation-receipt-invalid",
                        "operation_id": receipt["operation_id"],
                    },
                ) from exc
            if receipt["action"] == "task.release":
                release_result = result.get("result")
                if resolution:
                    if (
                        release_result != "outcome-unknown"
                        or result.get("state") != "outcome_unknown"
                    ):
                        continue
                else:
                    continue
            if resolution:
                proof = {
                    "resolution_reason": "task-recovery-resolved",
                    "terminal_finished_at": task["finished_at"],
                    "terminal_result": task["result"],
                    "terminal_state": task["state"],
                    "token_cleanup_completed": True,
                }
            else:
                proof = {
                    "aborted": True,
                    "reason": proof_reason,
                    "terminal_finished_at": task["finished_at"],
                    "terminal_result": task["result"],
                    "terminal_state": task["state"],
                    "token_cleanup_completed": True,
                }
            proof_json = canonical_json(proof)
            if receipt["terminal_json"] is not None:
                try:
                    existing = parse_canonical_json(receipt["terminal_json"])
                except (TypeError, ValueError, UsageError, json.JSONDecodeError) as exc:
                    raise StateError(
                        "Post-cleanup lifecycle proof is unreadable.",
                        details={
                            "reason": "operation-receipt-invalid",
                            "operation_id": receipt["operation_id"],
                        },
                    ) from exc
                if existing != proof:
                    raise StateError(
                        "Post-cleanup lifecycle proof conflicts with its receipt.",
                        details={
                            "reason": "operation-receipt-invalid",
                            "operation_id": receipt["operation_id"],
                        },
                    )
                continue
            safe_retired_at = max(
                float(retired_at),
                float(task["finished_at"]),
                float(receipt["finalized_at"]),
            )
            updated = connection.execute(
                "UPDATE operation_receipts SET terminal_json = ?, retired_at = ? "
                "WHERE operation_id = ? AND finalized_at IS NOT NULL "
                "AND terminal_json IS NULL AND retired_at IS NULL",
                (proof_json, safe_retired_at, receipt["operation_id"]),
            )
            if updated.rowcount != 1:
                raise StateError(
                    "Post-cleanup lifecycle proof could not be fenced atomically.",
                    details={
                        "reason": "operation-receipt-invalid",
                        "operation_id": receipt["operation_id"],
                    },
                )
            retired += 1
        return retired

    @staticmethod
    def _retire_task_lifecycle_receipts_after_token_cleanup(
        connection: sqlite3.Connection,
        task_id: str,
        retired_at: float,
    ) -> int:
        task = connection.execute("SELECT * FROM tasks WHERE id = ?", (task_id,)).fetchone()
        job = connection.execute(
            "SELECT * FROM token_cleanup_jobs WHERE task_id = ?",
            (task_id,),
        ).fetchone()
        if task is None or job is None or job["completed_at"] is None:
            raise StateError(
                "Task lifecycle cannot be retired without completed token cleanup.",
                details={
                    "reason": "token-cleanup-job-invalid",
                    "task_id": task_id,
                    "recovery_required": True,
                },
            )
        if job["reason"] == "claimless-task-expired":
            transition = ("task-ttl-expired", "expired", "expired")
        elif job["reason"] == "recovered-task-terminal" and task["state"] in {
            "completed",
            "failed",
        }:
            transition = (
                "task-recovery-resolved",
                str(task["state"]),
                f"recovered-{task['state']}",
            )
        else:
            transition = (None, None, None)
        proof_reason, terminal_state, terminal_result = transition
        if (
            task["state"] != terminal_state
            or task["result"] != terminal_result
            or task["finished_at"] is None
        ):
            raise StateError(
                "Task token cleanup does not match its durable terminal transition.",
                details={
                    "reason": "token-cleanup-job-invalid",
                    "task_id": task_id,
                    "recovery_required": True,
                },
            )
        matching = connection.execute(
            "SELECT * FROM operation_receipts WHERE action = 'task.start' AND task_id = ?",
            (task_id,),
        ).fetchall()
        if len(matching) != 1:
            raise StateError(
                "Claimless cleanup task has no unique task-start receipt.",
                details={
                    "reason": "operation-receipt-invalid",
                    "task_id": task_id,
                    "receipt_count": len(matching),
                },
            )
        start_receipt = matching[0]
        WorkspaceCoordinator._validated_ack_receipt(
            start_receipt,
            str(start_receipt["operation_id"]),
            str(start_receipt["fingerprint"]),
            receipt_delivery_digest(start_receipt["result_json"], start_receipt["terminal_json"]),
        )
        proof = {
            "aborted": True,
            "reason": proof_reason,
            "terminal_finished_at": task["finished_at"],
            "terminal_result": terminal_result,
            "terminal_state": terminal_state,
            "token_cleanup_completed": True,
        }
        proof_json = canonical_json(proof)
        existing_proof = start_receipt["terminal_json"]
        if existing_proof != proof_json:
            try:
                pending_proof = parse_canonical_json(existing_proof)
            except (TypeError, ValueError, UsageError, json.JSONDecodeError) as exc:
                raise StateError(
                    "Task-start receipt has unreadable lifecycle retirement proof.",
                    details={
                        "reason": "operation-receipt-invalid",
                        "operation_id": start_receipt["operation_id"],
                    },
                ) from exc
            if pending_proof != {
                key: value for key, value in proof.items() if key != "token_cleanup_completed"
            }:
                raise StateError(
                    "Task-start receipt has conflicting lifecycle retirement proof.",
                    details={
                        "reason": "operation-receipt-invalid",
                        "operation_id": start_receipt["operation_id"],
                    },
                )
        safe_retired_at = max(
            float(retired_at),
            float(start_receipt["finalized_at"]),
            float(task["finished_at"]),
        )
        connection.execute(
            "UPDATE operation_receipts SET terminal_json = ?, "
            "retired_at = COALESCE(retired_at, ?) WHERE operation_id = ?",
            (proof_json, safe_retired_at, start_receipt["operation_id"]),
        )
        updated = connection.execute(
            "SELECT * FROM operation_receipts WHERE operation_id = ?",
            (start_receipt["operation_id"],),
        ).fetchone()
        assert updated is not None
        WorkspaceCoordinator._validated_ack_receipt(
            updated,
            str(updated["operation_id"]),
            str(updated["fingerprint"]),
            receipt_delivery_digest(updated["result_json"], updated["terminal_json"]),
        )
        WorkspaceCoordinator._retire_post_cleanup_lifecycle_receipts(
            connection,
            str(task["workspace_id"]),
            task_id,
            safe_retired_at,
        )
        connection.execute(
            "DELETE FROM token_cleanup_jobs WHERE task_id = ? AND completed_at IS NOT NULL",
            (task_id,),
        )
        return 1

    def _complete_token_cleanup_job_locked(self, task_id: str) -> bool:
        with self._transaction() as connection:
            job = self._validated_token_cleanup_job(connection, task_id)
            if job is None:
                return False
            token_file_path = str(job["token_file_path"])
            token_hash = str(job["token_hash"])
        try:
            removed = remove_matching_token_hash_file(Path(token_file_path), token_hash)
        except (OSError, RuntimeError, ValueError, UsageError) as exc:
            raise StateError(
                "Claimless expired task token cleanup could not be completed.",
                details={
                    "reason": "token-cleanup-job-failed",
                    "task_id": task_id,
                    "recovery_required": True,
                },
            ) from exc
        if not removed:
            raise StateError(
                "Claimless expired task token no longer matches its cleanup identity.",
                details={
                    "reason": "token-cleanup-job-identity-mismatch",
                    "task_id": task_id,
                    "recovery_required": True,
                },
            )
        now = time.time()
        if not math.isfinite(now):
            raise StateError(
                "System clock is invalid; token cleanup cannot be recorded safely.",
                details={"reason": "system-clock-invalid"},
            )
        with self._transaction() as connection:
            job = self._validated_token_cleanup_job(connection, task_id)
            if job is None:
                return False
            completed_at = max(now, float(job["created_at"]))
            connection.execute(
                "UPDATE token_cleanup_jobs SET completed_at = COALESCE(completed_at, ?) "
                "WHERE task_id = ?",
                (completed_at, task_id),
            )
            self._retire_task_lifecycle_receipts_after_token_cleanup(
                connection,
                task_id,
                completed_at,
            )
        return True

    def drain_token_cleanup_jobs(
        self,
        limit: int = TOKEN_CLEANUP_DRAIN_LIMIT,
        *,
        workspace: Path | str | None = None,
    ) -> dict[str, int]:
        """Bounded best-effort cleanup for claimless tasks that expired after start."""

        if isinstance(limit, bool) or not isinstance(limit, int) or limit < 1:
            raise UsageError("Token cleanup drain limit must be a positive integer.")
        workspace_id: str | None = None
        if workspace is not None:
            workspace_id = _workspace_id(canonical_workspace(workspace))
        connection = open_database(self.paths)
        try:
            if workspace_id is None:
                jobs = connection.execute(
                    "SELECT task_id, token_file_path FROM token_cleanup_jobs "
                    "WHERE completed_at IS NULL "
                    "ORDER BY attempt_count, last_attempt_at IS NOT NULL, "
                    "COALESCE(last_attempt_at, created_at), created_at, task_id LIMIT ?",
                    (limit,),
                ).fetchall()
            else:
                jobs = connection.execute(
                    "SELECT task_id, token_file_path FROM token_cleanup_jobs "
                    "WHERE completed_at IS NULL AND workspace_id = ? "
                    "ORDER BY attempt_count, last_attempt_at IS NOT NULL, "
                    "COALESCE(last_attempt_at, created_at), created_at, task_id LIMIT ?",
                    (workspace_id, limit),
                ).fetchall()
        finally:
            connection.close()
        result = {"processed": 0, "completed": 0, "retained": 0, "failed": 0}
        for selected in jobs:
            attempt_at = time.time()
            if not math.isfinite(attempt_at):
                raise StateError(
                    "System clock is invalid; token cleanup cannot be attempted safely.",
                    details={"reason": "system-clock-invalid"},
                )
            with self._transaction() as connection:
                job = self._validated_token_cleanup_job(
                    connection,
                    str(selected["task_id"]),
                )
                if job is None:
                    continue
                previous_attempt_at = job["last_attempt_at"]
                attempt_at = max(
                    attempt_at,
                    float(job["created_at"]),
                    (
                        float(previous_attempt_at)
                        if previous_attempt_at is not None
                        else float(job["created_at"])
                    ),
                )
                connection.execute(
                    "UPDATE token_cleanup_jobs SET last_attempt_at = ?, "
                    "attempt_count = CASE WHEN attempt_count < 9223372036854775807 "
                    "THEN attempt_count + 1 ELSE attempt_count END WHERE task_id = ?",
                    (attempt_at, job["task_id"]),
                )
                task_id = str(job["task_id"])
                token_file_path = str(job["token_file_path"])
            result["processed"] += 1
            try:
                with task_token_path_lock(
                    self.paths,
                    Path(token_file_path),
                    timeout_seconds=0.0,
                ):
                    completed = self._complete_token_cleanup_job_locked(task_id)
                    if completed:
                        result["completed"] += 1
                    with self._transaction() as connection:
                        current = self._validated_token_cleanup_job(
                            connection,
                            task_id,
                        )
                        if current is None:
                            continue
                        result["retained"] += 1
            except StateError as exc:
                if exc.details.get("reason") not in {
                    "token-cleanup-job-failed",
                    "token-cleanup-job-identity-mismatch",
                }:
                    raise
                result["failed"] += 1
            except UsageError as exc:
                if exc.details.get("reason") != "task-token-path-lock-timeout":
                    raise
                result["failed"] += 1
            except (OSError, RuntimeError, ValueError):
                result["failed"] += 1
        if result["processed"]:
            with self._transaction() as connection:
                self._prune_delivered_operations(connection)
        return result

    @staticmethod
    def _workspace(connection: sqlite3.Connection, root: str) -> sqlite3.Row:
        workspace = connection.execute(
            "SELECT * FROM workspaces WHERE id = ? AND root = ?",
            (_workspace_id(root), root),
        ).fetchone()
        if workspace is None:
            raise StateError(
                "Workspace is not registered.",
                details={"workspace": root, "reason": "workspace-unregistered"},
            )
        return workspace

    @staticmethod
    def _touch(connection: sqlite3.Connection, workspace_id: str) -> None:
        connection.execute("UPDATE workspaces SET epoch = epoch + 1 WHERE id = ?", (workspace_id,))

    @staticmethod
    def _allocate_queue_order(connection: sqlite3.Connection, workspace_id: str) -> int:
        workspace = connection.execute(
            "SELECT next_queue_order FROM workspaces WHERE id = ?", (workspace_id,)
        ).fetchone()
        if workspace is None or workspace["next_queue_order"] < 1:
            raise StateError("Workspace queue counter is invalid.")
        order = int(workspace["next_queue_order"])
        connection.execute(
            "UPDATE workspaces SET next_queue_order = next_queue_order + 1 WHERE id = ?",
            (workspace_id,),
        )
        return order

    @staticmethod
    def _unknown_exists(connection: sqlite3.Connection, workspace_id: str) -> bool:
        return (
            connection.execute(
                "SELECT 1 FROM tasks WHERE workspace_id = ? AND state = 'outcome_unknown' LIMIT 1",
                (workspace_id,),
            ).fetchone()
            is not None
        )

    @staticmethod
    def _bound_open_task_times(
        connection: sqlite3.Connection, now: float, workspace_id: str | None = None
    ) -> set[str]:
        maximum_expiry = now + MAX_TASK_TTL_SECONDS
        affected_workspaces: set[str] = set()
        if workspace_id is None:
            tasks = connection.execute(
                "SELECT id, workspace_id, created_at, heartbeat_at, expires_at FROM tasks "
                "WHERE state = 'active'"
            ).fetchall()
        else:
            tasks = connection.execute(
                "SELECT id, workspace_id, created_at, heartbeat_at, expires_at FROM tasks "
                "WHERE workspace_id = ? AND state = 'active'",
                (workspace_id,),
            ).fetchall()
        for task in tasks:
            try:
                created_at = float(task["created_at"])
            except (TypeError, ValueError, OverflowError) as exc:
                raise StateError(
                    "Active task has invalid creation timing evidence.",
                    details={"reason": "scheduler-time-invalid", "task_id": task["id"]},
                ) from exc
            if not math.isfinite(created_at):
                raise StateError(
                    "Active task has non-finite creation timing evidence.",
                    details={"reason": "scheduler-time-invalid", "task_id": task["id"]},
                )
            if created_at > now:
                # Do not rebase a lease before its own creation. _expire_tasks turns this
                # rollback anomaly into a terminal fail-closed lifecycle below.
                continue
            try:
                heartbeat_at = float(task["heartbeat_at"])
            except (TypeError, ValueError):
                heartbeat_at = math.nan
            try:
                expires_at = float(task["expires_at"])
            except (TypeError, ValueError):
                expires_at = math.nan
            bounded_heartbeat = heartbeat_at if math.isfinite(heartbeat_at) else now
            if math.isfinite(expires_at):
                bounded_expiry = min(expires_at, maximum_expiry)
            elif expires_at == math.inf:
                bounded_expiry = maximum_expiry
            else:
                bounded_expiry = now
            if math.isfinite(heartbeat_at) and heartbeat_at > now:
                lease_seconds = expires_at - heartbeat_at
                bounded_heartbeat = now
                if math.isfinite(lease_seconds) and lease_seconds > 0:
                    bounded_expiry = now + min(lease_seconds, MAX_TASK_TTL_SECONDS)
                elif lease_seconds > 0:
                    bounded_expiry = maximum_expiry
                else:
                    bounded_expiry = now
            if bounded_heartbeat == heartbeat_at and bounded_expiry == expires_at:
                continue
            connection.execute(
                "UPDATE tasks SET heartbeat_at = ?, expires_at = ? WHERE id = ?",
                (bounded_heartbeat, bounded_expiry, task["id"]),
            )
            affected_workspaces.add(str(task["workspace_id"]))
        for affected_workspace_id in affected_workspaces:
            WorkspaceCoordinator._touch(connection, affected_workspace_id)
        return affected_workspaces

    @staticmethod
    def _finalize_task_wait_operations(
        connection: sqlite3.Connection,
        task_id: str,
        workspace_id: str,
        now: float,
        *,
        reason: str,
    ) -> None:
        receipts = connection.execute(
            "SELECT * FROM operation_receipts WHERE finalized_at IS NULL "
            "AND workspace_id = ? AND task_id = ? "
            "AND action IN ('claim.acquire', 'freeze.acquire', 'task.park')",
            (workspace_id, task_id),
        ).fetchall()
        for receipt in receipts:
            try:
                initial_result = parse_canonical_json(receipt["result_json"])
            except (TypeError, ValueError, UsageError, json.JSONDecodeError) as exc:
                raise StateError(
                    "Pending operation receipt is unreadable during task transition.",
                    details={
                        "reason": "operation-receipt-invalid",
                        "operation_id": receipt["operation_id"],
                    },
                ) from exc
            if initial_result.get("task_id") != task_id:
                raise StateError(
                    "Pending operation receipt does not match its indexed task identity.",
                    details={
                        "reason": "operation-receipt-invalid",
                        "operation_id": receipt["operation_id"],
                    },
                )
            if receipt["action"] in {"claim.acquire", "freeze.acquire"}:
                claim_id = initial_result.get("id")
                if not isinstance(claim_id, str) or not claim_id:
                    raise StateError(
                        "Pending claim receipt has no stable claim identity.",
                        details={
                            "reason": "operation-receipt-invalid",
                            "operation_id": receipt["operation_id"],
                        },
                    )
                claim = connection.execute(
                    "SELECT * FROM claims WHERE id = ? AND task_id = ?",
                    (claim_id, task_id),
                ).fetchone()
                if claim is None:
                    raise StateError(
                        "Pending claim disappeared during task transition.",
                        details={
                            "reason": "operation-receipt-invalid",
                            "operation_id": receipt["operation_id"],
                        },
                    )
                result = WorkspaceCoordinator._public_claim(connection, claim)
                result.update(
                    {
                        "granted": False,
                        "timed_out": False,
                        "aborted": True,
                        "reason": reason,
                    }
                )
            else:
                claim_ids = initial_result.get("claim_ids")
                freeze_id = initial_result.get("freeze_id")
                if (
                    not isinstance(claim_ids, list)
                    or not claim_ids
                    or any(not isinstance(claim_id, str) or not claim_id for claim_id in claim_ids)
                    or not isinstance(freeze_id, str)
                    or not freeze_id
                ):
                    raise StateError(
                        "Pending park receipt has no stable claim or freeze identity.",
                        details={
                            "reason": "operation-receipt-invalid",
                            "operation_id": receipt["operation_id"],
                        },
                    )
                placeholders = ", ".join("?" for _ in claim_ids)
                claims = connection.execute(
                    f"SELECT * FROM claims WHERE id IN ({placeholders}) AND task_id = ? "
                    "ORDER BY queue_order",
                    (*claim_ids, task_id),
                ).fetchall()
                if len(claims) != len(claim_ids):
                    raise StateError(
                        "Pending parked claims disappeared during task transition.",
                        details={
                            "reason": "operation-receipt-invalid",
                            "operation_id": receipt["operation_id"],
                        },
                    )
                states = {claim["id"]: claim["state"] for claim in claims}
                result = {
                    "task_id": task_id,
                    "freeze_id": freeze_id,
                    "claim_ids": claim_ids,
                    "states": states,
                    "parked": any(state == "parked" for state in states.values()),
                    "resumed": False,
                    "timed_out": False,
                    "aborted": True,
                    "reason": reason,
                }
            updated = connection.execute(
                "UPDATE operation_receipts SET result_json = ?, "
                "finalized_at = MAX(?, created_at), retired_at = MAX(?, created_at) "
                "WHERE operation_id = ? AND finalized_at IS NULL AND delivered_at IS NULL",
                (canonical_json(result), now, now, receipt["operation_id"]),
            )
            if updated.rowcount != 1:
                raise StateError(
                    "Pending operation receipt could not be finalized during task transition.",
                    details={
                        "reason": "operation-receipt-invalid",
                        "operation_id": receipt["operation_id"],
                    },
                )

    @staticmethod
    def _finalize_task_lifecycle_operations(
        connection: sqlite3.Connection,
        task_id: str,
        workspace_id: str,
        now: float,
        *,
        reason: str,
    ) -> None:
        """Fence every finalized result that could otherwise look like live authority."""

        task = connection.execute(
            "SELECT state, result, finished_at, token_file_path FROM tasks "
            "WHERE id = ? AND workspace_id = ?",
            (task_id, workspace_id),
        ).fetchone()
        if task is None or task["finished_at"] is None:
            raise StateError(
                "Terminal task lifecycle proof has no durable task transition.",
                details={"reason": "operation-receipt-invalid", "task_id": task_id},
            )
        expected_transition: tuple[str, str] | None = {
            "task-ttl-expired": ("expired", "expired"),
            "task-ttl-expired-with-active-claim": (
                "outcome_unknown",
                "expired-with-active-claim",
            ),
            "task-released-outcome-unknown": ("outcome_unknown", "outcome-unknown"),
        }.get(reason)
        if reason == "task-released" and task["state"] in {"completed", "failed"}:
            expected_transition = (str(task["state"]), str(task["state"]))
        elif reason == "task-recovery-resolved" and task["state"] in {
            "completed",
            "failed",
        }:
            expected_transition = (
                str(task["state"]),
                f"recovered-{task['state']}",
            )
        if expected_transition != (task["state"], task["result"]):
            raise StateError(
                "Terminal task lifecycle reason does not match durable task state.",
                details={"reason": "operation-receipt-invalid", "task_id": task_id},
            )
        WorkspaceCoordinator._prune_task_lifecycle_operations(connection, task_id)
        proof = {
            "aborted": True,
            "reason": reason,
            "terminal_finished_at": task["finished_at"],
            "terminal_result": task["result"],
            "terminal_state": task["state"],
        }
        receipts = connection.execute(
            "SELECT * FROM operation_receipts WHERE workspace_id = ? AND task_id = ? "
            "AND action IN ('task.start', 'task.heartbeat', 'claim.acquire', "
            "'freeze.acquire', 'task.park') AND finalized_at IS NOT NULL",
            (workspace_id, task_id),
        ).fetchall()
        for receipt in receipts:
            receipt_proof = dict(proof)
            if (
                receipt["action"] == "task.start"
                and task["token_file_path"] is None
                and reason
                in {
                    "task-ttl-expired",
                    "task-released",
                    "task-recovery-resolved",
                }
            ):
                receipt_proof["token_cleanup_completed"] = True
            proof_json = canonical_json(receipt_proof)
            try:
                result = parse_canonical_json(receipt["result_json"])
            except (TypeError, ValueError, UsageError, json.JSONDecodeError) as exc:
                raise StateError(
                    "Lifecycle operation receipt is unreadable during task transition.",
                    details={
                        "reason": "operation-receipt-invalid",
                        "operation_id": receipt["operation_id"],
                    },
                ) from exc
            if receipt["action"] != "task.start" and result.get("aborted") is True:
                if receipt["retired_at"] is None:
                    raise StateError(
                        "Aborted lifecycle receipt was not retired atomically.",
                        details={
                            "reason": "operation-receipt-invalid",
                            "operation_id": receipt["operation_id"],
                        },
                    )
                if reason == "task-recovery-resolved":
                    if result.get("reason") not in {
                        "task-ttl-expired-with-active-claim",
                        "task-released-outcome-unknown",
                    }:
                        raise StateError(
                            "Synthetic lifecycle receipt cannot be upgraded from its "
                            "stored terminal reason.",
                            details={
                                "reason": "operation-receipt-invalid",
                                "operation_id": receipt["operation_id"],
                            },
                        )
                    # The delivered result is immutable. Recovery appends a separate,
                    # monotonic proof; the public replay is derived from both values.
                    resolution_proof = {
                        "resolution_reason": "task-recovery-resolved",
                        "terminal_finished_at": task["finished_at"],
                        "terminal_result": task["result"],
                        "terminal_state": task["state"],
                    }
                    proof_json = canonical_json(resolution_proof)
                    if receipt["terminal_json"] is not None:
                        try:
                            existing_resolution = parse_canonical_json(receipt["terminal_json"])
                        except (
                            TypeError,
                            ValueError,
                            UsageError,
                            json.JSONDecodeError,
                        ) as exc:
                            raise StateError(
                                "Synthetic lifecycle resolution proof is unreadable.",
                                details={
                                    "reason": "operation-receipt-invalid",
                                    "operation_id": receipt["operation_id"],
                                },
                            ) from exc
                        WorkspaceCoordinator._operation_terminal_proof(receipt, result)
                        if existing_resolution == resolution_proof:
                            continue
                        raise StateError(
                            "Synthetic lifecycle receipt has conflicting resolution proof.",
                            details={
                                "reason": "operation-receipt-invalid",
                                "operation_id": receipt["operation_id"],
                            },
                        )
                    safe_retired_at = max(
                        float(now),
                        float(task["finished_at"]),
                        float(receipt["finalized_at"]),
                        float(receipt["retired_at"]),
                    )
                    updated = connection.execute(
                        "UPDATE operation_receipts SET terminal_json = ?, retired_at = ? "
                        "WHERE operation_id = ? AND finalized_at IS NOT NULL "
                        "AND retired_at IS NOT NULL AND terminal_json IS NULL "
                        "AND result_json = ?",
                        (
                            proof_json,
                            safe_retired_at,
                            receipt["operation_id"],
                            receipt["result_json"],
                        ),
                    )
                    if updated.rowcount != 1:
                        raise StateError(
                            "Synthetic lifecycle receipt could not be resolved atomically.",
                            details={
                                "reason": "operation-receipt-invalid",
                                "operation_id": receipt["operation_id"],
                            },
                        )
                continue
            if set(result).intersection(receipt_proof):
                raise StateError(
                    "Lifecycle proof would collide with an immutable operation result.",
                    details={
                        "reason": "operation-receipt-invalid",
                        "operation_id": receipt["operation_id"],
                    },
                )
            existing_proof: dict[str, Any] | None = None
            if receipt["terminal_json"] is not None:
                try:
                    existing_proof = parse_canonical_json(receipt["terminal_json"])
                except (TypeError, ValueError, UsageError, json.JSONDecodeError) as exc:
                    raise StateError(
                        "Lifecycle operation proof is unreadable during task transition.",
                        details={
                            "reason": "operation-receipt-invalid",
                            "operation_id": receipt["operation_id"],
                        },
                    ) from exc
                WorkspaceCoordinator._operation_terminal_proof(receipt, result)
                comparable_existing = {
                    key: value
                    for key, value in existing_proof.items()
                    if key != "token_cleanup_completed"
                }
                if comparable_existing == proof:
                    continue
                if not (
                    reason == "task-recovery-resolved"
                    and existing_proof.get("reason")
                    in {
                        "task-ttl-expired-with-active-claim",
                        "task-released-outcome-unknown",
                    }
                ):
                    raise StateError(
                        "Lifecycle operation receipt has conflicting terminal proof.",
                        details={
                            "reason": "operation-receipt-invalid",
                            "operation_id": receipt["operation_id"],
                        },
                    )
            safe_retired_at = max(
                float(now),
                float(task["finished_at"]),
                float(receipt["finalized_at"]),
                (
                    float(receipt["retired_at"])
                    if receipt["retired_at"] is not None
                    else float(receipt["finalized_at"])
                ),
            )
            updated = connection.execute(
                "UPDATE operation_receipts SET terminal_json = ?, retired_at = ? "
                "WHERE operation_id = ? AND finalized_at IS NOT NULL",
                (proof_json, safe_retired_at, receipt["operation_id"]),
            )
            if updated.rowcount != 1:
                raise StateError(
                    "Lifecycle operation receipt could not be fenced atomically.",
                    details={
                        "reason": "operation-receipt-invalid",
                        "operation_id": receipt["operation_id"],
                    },
                )

    @staticmethod
    def _expire_tasks(
        connection: sqlite3.Connection, now: float, workspace_id: str | None = None
    ) -> set[str]:
        if workspace_id is None:
            expired = connection.execute(
                "SELECT id, workspace_id, token_hash, token_file_path FROM tasks "
                "WHERE state = 'active' AND (expires_at <= ? OR created_at > ?)",
                (now, now),
            ).fetchall()
        else:
            expired = connection.execute(
                "SELECT id, workspace_id, token_hash, token_file_path FROM tasks "
                "WHERE workspace_id = ? AND state = 'active' "
                "AND (expires_at <= ? OR created_at > ?)",
                (workspace_id, now, now),
            ).fetchall()
        affected_workspaces: set[str] = set()
        for task in expired:
            transition_time = WorkspaceCoordinator._task_transition_time(
                connection,
                str(task["id"]),
                now,
            )
            affected_workspaces.add(str(task["workspace_id"]))
            queued_freezes = connection.execute(
                "SELECT id FROM claims WHERE task_id = ? AND kind = 'freeze' AND state = 'queued'",
                (task["id"],),
            ).fetchall()
            active_claim = connection.execute(
                "SELECT 1 FROM claims WHERE task_id = ? AND state = 'active' LIMIT 1",
                (task["id"],),
            ).fetchone()
            connection.execute(
                "UPDATE claims SET state = 'cancelled', released_at = ? "
                "WHERE task_id = ? AND state = 'queued'",
                (transition_time, task["id"]),
            )
            connection.execute(
                "DELETE FROM claim_scopes WHERE scope_type = 'parked_for' "
                "AND claim_id IN (SELECT id FROM claims WHERE task_id = ? "
                "AND state = 'cancelled')",
                (task["id"],),
            )
            WorkspaceCoordinator._resume_parked_for_freezes(
                connection, [row["id"] for row in queued_freezes]
            )
            if active_claim is not None:
                connection.execute(
                    "UPDATE tasks SET state = 'outcome_unknown', finished_at = ?, "
                    "result = 'expired-with-active-claim', "
                    "note = 'Task TTL expired while resources were still owned.' "
                    "WHERE id = ?",
                    (transition_time, task["id"]),
                )
            else:
                connection.execute(
                    "UPDATE claims SET state = 'cancelled', released_at = ? "
                    "WHERE task_id = ? AND state = 'parked'",
                    (transition_time, task["id"]),
                )
                connection.execute(
                    "DELETE FROM claim_scopes WHERE scope_type = 'parked_for' "
                    "AND claim_id IN (SELECT id FROM claims WHERE task_id = ?)",
                    (task["id"],),
                )
                connection.execute(
                    "UPDATE tasks SET state = 'expired', finished_at = ?, "
                    "result = 'expired' WHERE id = ?",
                    (transition_time, task["id"]),
                )
                token_file_path = task["token_file_path"]
                if token_file_path is not None:
                    connection.execute(
                        "INSERT INTO token_cleanup_jobs("
                        "task_id, workspace_id, token_file_path, token_file_identity, "
                        "token_hash, reason, "
                        "created_at, completed_at) "
                        "VALUES(?, ?, ?, ?, ?, 'claimless-task-expired', ?, NULL)",
                        (
                            task["id"],
                            task["workspace_id"],
                            token_file_path,
                            _token_path_identity(token_file_path),
                            task["token_hash"],
                            transition_time,
                        ),
                    )
            WorkspaceCoordinator._finalize_task_wait_operations(
                connection,
                str(task["id"]),
                str(task["workspace_id"]),
                transition_time,
                reason=(
                    "task-ttl-expired-with-active-claim"
                    if active_claim is not None
                    else "task-ttl-expired"
                ),
            )
            WorkspaceCoordinator._finalize_task_lifecycle_operations(
                connection,
                str(task["id"]),
                str(task["workspace_id"]),
                transition_time,
                reason=(
                    "task-ttl-expired-with-active-claim"
                    if active_claim is not None
                    else "task-ttl-expired"
                ),
            )
            WorkspaceCoordinator._touch(connection, task["workspace_id"])
        return affected_workspaces

    @staticmethod
    def _claim_scopes(connection: sqlite3.Connection, claim_id: str) -> dict[str, tuple[str, ...]]:
        rows = connection.execute(
            "SELECT scope_type, value FROM claim_scopes "
            "WHERE claim_id = ? ORDER BY scope_type, value",
            (claim_id,),
        ).fetchall()
        return {
            "write": tuple(row["value"] for row in rows if row["scope_type"] == "write"),
            "resource": tuple(row["value"] for row in rows if row["scope_type"] == "resource"),
            "parked_for": tuple(row["value"] for row in rows if row["scope_type"] == "parked_for"),
            "priority": tuple(row["value"] for row in rows if row["scope_type"] == "priority"),
        }

    @staticmethod
    def _claim_priority(claim: sqlite3.Row, scopes: dict[str, tuple[str, ...]]) -> str:
        priorities = scopes["priority"]
        if not priorities:
            return "normal"
        if claim["kind"] != "freeze" or priorities != ("urgent",):
            raise StateError("Claim priority state is invalid.")
        return "urgent"

    @staticmethod
    def _claim_sort_key(claim: sqlite3.Row, scopes: dict[str, tuple[str, ...]]) -> tuple[int, int]:
        rank = 0 if WorkspaceCoordinator._claim_priority(claim, scopes) == "urgent" else 1
        return rank, claim["queue_order"]

    @staticmethod
    def _resume_parked_for_freezes(
        connection: sqlite3.Connection, freeze_ids: Sequence[str]
    ) -> None:
        for freeze_id in freeze_ids:
            parked = connection.execute(
                "SELECT claims.id FROM claims "
                "JOIN claim_scopes ON claim_scopes.claim_id = claims.id "
                "WHERE claims.state = 'parked' "
                "AND claim_scopes.scope_type = 'parked_for' "
                "AND claim_scopes.value = ?",
                (freeze_id,),
            ).fetchall()
            claim_ids = [row["id"] for row in parked]
            if not claim_ids:
                continue
            placeholders = ", ".join("?" for _ in claim_ids)
            connection.execute(
                f"UPDATE claims SET state = 'queued', granted_at = NULL "
                f"WHERE id IN ({placeholders}) AND state = 'parked'",
                claim_ids,
            )

    @staticmethod
    def _task_drain_request(
        connection: sqlite3.Connection, workspace_id: str, task_id: str
    ) -> dict[str, Any] | None:
        freezes = connection.execute(
            "SELECT * FROM claims WHERE workspace_id = ? "
            "AND task_id != ? AND kind = 'freeze' AND state = 'queued'",
            (workspace_id, task_id),
        ).fetchall()
        if not freezes:
            return None
        freeze_scopes = {
            freeze["id"]: WorkspaceCoordinator._claim_scopes(connection, freeze["id"])
            for freeze in freezes
        }
        freeze = min(
            freezes,
            key=lambda candidate: WorkspaceCoordinator._claim_sort_key(
                candidate, freeze_scopes[candidate["id"]]
            ),
        )
        freeze_key = WorkspaceCoordinator._claim_sort_key(freeze, freeze_scopes[freeze["id"]])
        owned_claims = connection.execute(
            "SELECT * FROM claims WHERE workspace_id = ? AND task_id = ? "
            "AND state IN ('queued', 'active')",
            (workspace_id, task_id),
        ).fetchall()
        blocking_claims = [
            claim
            for claim in owned_claims
            if (
                claim["state"] == "active"
                or WorkspaceCoordinator._claim_sort_key(
                    claim, WorkspaceCoordinator._claim_scopes(connection, claim["id"])
                )
                < freeze_key
            )
        ]
        if not blocking_claims:
            return None
        unsafe_claim = any(
            claim["kind"] == "freeze"
            or WorkspaceCoordinator._claim_scopes(connection, claim["id"])["resource"]
            for claim in blocking_claims
        )
        return {
            "freeze_id": freeze["id"],
            "queue_order": freeze["queue_order"],
            "priority": WorkspaceCoordinator._claim_priority(freeze, freeze_scopes[freeze["id"]]),
            "park_ready": not unsafe_claim,
        }

    @staticmethod
    def _claims_conflict(
        left: sqlite3.Row,
        left_scopes: dict[str, tuple[str, ...]],
        right: sqlite3.Row,
        right_scopes: dict[str, tuple[str, ...]],
    ) -> bool:
        if left["task_id"] == right["task_id"]:
            if left["kind"] == "freeze" or right["kind"] == "freeze":
                return False
            return bool(set(left_scopes["resource"]) & set(right_scopes["resource"]))
        if left["kind"] == "freeze" or right["kind"] == "freeze":
            return True
        if set(left_scopes["resource"]) & set(right_scopes["resource"]):
            return True
        return any(
            _path_conflicts(left_path, right_path)
            for left_path in left_scopes["write"]
            for right_path in right_scopes["write"]
        )

    @staticmethod
    def _finalize_due_wait_operations(
        connection: sqlite3.Connection,
        workspace_id: str,
        now: float,
    ) -> None:
        receipts = connection.execute(
            "SELECT * FROM operation_receipts WHERE workspace_id = ? "
            "AND finalized_at IS NULL "
            "AND action IN ('claim.acquire', 'freeze.acquire', 'task.park')",
            (workspace_id,),
        ).fetchall()
        touched = False
        for receipt in receipts:
            try:
                parameters = parse_canonical_json(receipt["parameters_json"])
                initial_result = parse_canonical_json(receipt["result_json"])
                requested_wait = float(parameters["requested_wait_seconds"])
                created_at = float(receipt["created_at"])
                absolute_deadline = created_at + requested_wait
            except (KeyError, TypeError, ValueError, UsageError, json.JSONDecodeError) as exc:
                raise StateError(
                    "Pending wait receipt is unreadable during deadline maintenance.",
                    details={
                        "reason": "operation-receipt-invalid",
                        "operation_id": receipt["operation_id"],
                    },
                ) from exc
            if (
                not isinstance(parameters, dict)
                or not isinstance(initial_result, dict)
                or not math.isfinite(requested_wait)
                or requested_wait <= 0
                or not math.isfinite(created_at)
                or not math.isfinite(absolute_deadline)
            ):
                raise StateError(
                    "Pending wait receipt has an invalid absolute deadline.",
                    details={
                        "reason": "operation-receipt-invalid",
                        "operation_id": receipt["operation_id"],
                    },
                )
            if now < absolute_deadline:
                continue
            if receipt["action"] in {"claim.acquire", "freeze.acquire"}:
                claim_id = initial_result.get("id")
                if not isinstance(claim_id, str) or not claim_id:
                    raise StateError(
                        "Pending claim receipt has no stable claim identity.",
                        details={
                            "reason": "operation-receipt-invalid",
                            "operation_id": receipt["operation_id"],
                        },
                    )
                claim = connection.execute(
                    "SELECT * FROM claims WHERE id = ? AND workspace_id = ?",
                    (claim_id, workspace_id),
                ).fetchone()
                if claim is None:
                    raise StateError(
                        "Pending claim disappeared during deadline maintenance.",
                        details={
                            "reason": "operation-receipt-invalid",
                            "operation_id": receipt["operation_id"],
                        },
                    )
                granted_at = claim["granted_at"]
                granted_in_time = (
                    claim["state"] == "active"
                    and isinstance(granted_at, (int, float))
                    and not isinstance(granted_at, bool)
                    and math.isfinite(float(granted_at))
                    and float(granted_at) <= absolute_deadline
                )
                keep_queued = parameters.get("keep_queued") is True
                if claim["state"] in {"queued", "active"} and not granted_in_time:
                    if keep_queued:
                        result = WorkspaceCoordinator._public_claim(connection, claim)
                        if claim["state"] == "active":
                            result["state"] = "queued"
                            result["granted_at"] = None
                    else:
                        released_at = WorkspaceCoordinator._claim_transition_time(claim, now)
                        connection.execute(
                            "UPDATE claims SET state = 'cancelled', released_at = ? "
                            "WHERE id = ? AND state IN ('queued', 'active')",
                            (released_at, claim_id),
                        )
                        if receipt["action"] == "freeze.acquire":
                            WorkspaceCoordinator._resume_parked_for_freezes(
                                connection,
                                (claim_id,),
                            )
                        touched = True
                        claim = connection.execute(
                            "SELECT * FROM claims WHERE id = ?", (claim_id,)
                        ).fetchone()
                        assert claim is not None
                        result = WorkspaceCoordinator._public_claim(connection, claim)
                    result["granted"] = False
                    result["timed_out"] = True
                else:
                    result = WorkspaceCoordinator._public_claim(connection, claim)
                    result["granted"] = granted_in_time
                    result["timed_out"] = False
            else:
                claim_ids = initial_result.get("claim_ids")
                freeze_id = initial_result.get("freeze_id")
                task_id = initial_result.get("task_id")
                if (
                    not isinstance(claim_ids, list)
                    or not claim_ids
                    or any(not isinstance(claim_id, str) or not claim_id for claim_id in claim_ids)
                    or not isinstance(freeze_id, str)
                    or not freeze_id
                    or not isinstance(task_id, str)
                    or not task_id
                ):
                    raise StateError(
                        "Pending park receipt has no stable identity.",
                        details={
                            "reason": "operation-receipt-invalid",
                            "operation_id": receipt["operation_id"],
                        },
                    )
                placeholders = ", ".join("?" for _ in claim_ids)
                claims = connection.execute(
                    f"SELECT * FROM claims WHERE id IN ({placeholders}) AND task_id = ? "
                    "ORDER BY queue_order",
                    (*claim_ids, task_id),
                ).fetchall()
                if len(claims) != len(claim_ids):
                    raise StateError(
                        "Pending parked claims disappeared during deadline maintenance.",
                        details={
                            "reason": "operation-receipt-invalid",
                            "operation_id": receipt["operation_id"],
                        },
                    )
                states: dict[str, str] = {}
                resumed_in_time = True
                for claim in claims:
                    granted_at = claim["granted_at"]
                    granted_in_time = (
                        claim["state"] == "active"
                        and isinstance(granted_at, (int, float))
                        and not isinstance(granted_at, bool)
                        and math.isfinite(float(granted_at))
                        and float(granted_at) <= absolute_deadline
                    )
                    resumed_in_time = resumed_in_time and granted_in_time
                    states[claim["id"]] = (
                        "active"
                        if granted_in_time
                        else ("parked" if claim["state"] == "active" else claim["state"])
                    )
                result = {
                    "task_id": task_id,
                    "freeze_id": freeze_id,
                    "claim_ids": claim_ids,
                    "states": states,
                    "parked": not resumed_in_time,
                    "resumed": resumed_in_time,
                    "timed_out": not resumed_in_time,
                }
            updated = connection.execute(
                "UPDATE operation_receipts SET result_json = ?, "
                "finalized_at = MAX(?, created_at) "
                "WHERE operation_id = ? AND finalized_at IS NULL AND delivered_at IS NULL",
                (canonical_json(result), now, receipt["operation_id"]),
            )
            if updated.rowcount != 1:
                raise StateError(
                    "Pending operation receipt could not be finalized at its deadline.",
                    details={
                        "reason": "operation-receipt-invalid",
                        "operation_id": receipt["operation_id"],
                    },
                )
        if touched:
            WorkspaceCoordinator._touch(connection, workspace_id)

    @staticmethod
    def _schedule_workspace(connection: sqlite3.Connection, workspace_id: str, now: float) -> None:
        WorkspaceCoordinator._finalize_due_wait_operations(connection, workspace_id, now)
        if WorkspaceCoordinator._unknown_exists(connection, workspace_id):
            return
        active = list(
            connection.execute(
                "SELECT * FROM claims WHERE workspace_id = ? AND state = 'active' "
                "ORDER BY queue_order",
                (workspace_id,),
            ).fetchall()
        )
        active_scopes = {
            claim["id"]: WorkspaceCoordinator._claim_scopes(connection, claim["id"])
            for claim in active
        }
        queued = list(
            connection.execute(
                "SELECT * FROM claims WHERE workspace_id = ? AND state = 'queued'",
                (workspace_id,),
            ).fetchall()
        )
        queued_scopes = {
            claim["id"]: WorkspaceCoordinator._claim_scopes(connection, claim["id"])
            for claim in queued
        }
        queued.sort(
            key=lambda claim: WorkspaceCoordinator._claim_sort_key(
                claim, queued_scopes[claim["id"]]
            )
        )
        blocked_earlier: list[sqlite3.Row] = []
        blocked_scopes: dict[str, dict[str, tuple[str, ...]]] = {}
        for candidate in queued:
            candidate_scopes = queued_scopes[candidate["id"]]
            if candidate["kind"] == "freeze":
                other_active = [
                    claim for claim in active if claim["task_id"] != candidate["task_id"]
                ]
                if not other_active and not blocked_earlier:
                    granted_at = WorkspaceCoordinator._claim_transition_time(candidate, now)
                    connection.execute(
                        "UPDATE claims SET state = 'active', granted_at = ? WHERE id = ?",
                        (granted_at, candidate["id"]),
                    )
                    connection.execute(
                        "DELETE FROM claim_scopes WHERE claim_id = ? AND scope_type = 'parked_for'",
                        (candidate["id"],),
                    )
                    active.append(candidate)
                    active_scopes[candidate["id"]] = candidate_scopes
                break
            conflicts_active = any(
                WorkspaceCoordinator._claims_conflict(
                    candidate,
                    candidate_scopes,
                    existing,
                    active_scopes[existing["id"]],
                )
                for existing in active
            )
            conflicts_queued = any(
                WorkspaceCoordinator._claims_conflict(
                    candidate,
                    candidate_scopes,
                    earlier,
                    blocked_scopes[earlier["id"]],
                )
                for earlier in blocked_earlier
            )
            if conflicts_active or conflicts_queued:
                blocked_earlier.append(candidate)
                blocked_scopes[candidate["id"]] = candidate_scopes
                continue
            granted_at = WorkspaceCoordinator._claim_transition_time(candidate, now)
            connection.execute(
                "UPDATE claims SET state = 'active', granted_at = ? WHERE id = ?",
                (granted_at, candidate["id"]),
            )
            connection.execute(
                "DELETE FROM claim_scopes WHERE claim_id = ? AND scope_type = 'parked_for'",
                (candidate["id"],),
            )
            active.append(candidate)
            active_scopes[candidate["id"]] = candidate_scopes

    @staticmethod
    def _delete_task_resolution_receipts(
        connection: sqlite3.Connection,
        task: sqlite3.Row,
    ) -> None:
        resolution_receipts = connection.execute(
            "SELECT * FROM operation_receipts WHERE task_id = ? "
            "AND action IN ('claim.acquire', 'freeze.acquire', 'task.park', "
            "'claim.release', 'queue.cancel', 'task.release') "
            "AND terminal_json IS NOT NULL",
            (task["id"],),
        ).fetchall()
        for receipt in resolution_receipts:
            try:
                stored_result = parse_canonical_json(receipt["result_json"])
            except (TypeError, ValueError, UsageError, json.JSONDecodeError) as exc:
                raise StateError(
                    "Lifecycle receipt is unreadable during terminal task retention.",
                    details={
                        "reason": "operation-receipt-invalid",
                        "operation_id": receipt["operation_id"],
                    },
                ) from exc
            proof = WorkspaceCoordinator._operation_terminal_proof(receipt, stored_result)
            if proof is None or "resolution_reason" not in proof:
                continue
            if (
                receipt["workspace_id"] != task["workspace_id"]
                or receipt["token_cleanup_path"] is not None
                or proof["terminal_state"] != task["state"]
                or proof["terminal_result"] != task["result"]
                or proof["terminal_finished_at"] != task["finished_at"]
            ):
                raise StateError(
                    "Recovery resolution receipt changed its terminal task binding.",
                    details={
                        "reason": "operation-receipt-invalid",
                        "operation_id": receipt["operation_id"],
                    },
                )
            removed = connection.execute(
                "DELETE FROM operation_receipts WHERE operation_id = ? "
                "AND task_id = ? AND terminal_json = ? AND retired_at IS NOT NULL "
                "AND token_cleanup_path IS NULL",
                (receipt["operation_id"], task["id"], receipt["terminal_json"]),
            )
            if removed.rowcount != 1:
                raise StateError(
                    "Recovery resolution receipt changed during terminal task retention.",
                    details={
                        "reason": "operation-receipt-invalid",
                        "operation_id": receipt["operation_id"],
                    },
                )

    @staticmethod
    def _prune_terminal_tasks(connection: sqlite3.Connection, workspace_id: str) -> None:
        victims = connection.execute(
            "SELECT candidate.* FROM tasks AS candidate "
            "WHERE candidate.workspace_id = ? "
            "AND candidate.state IN ('completed', 'failed', 'expired') "
            "AND NOT EXISTS ("
            "SELECT 1 FROM claims WHERE claims.task_id = candidate.id "
            "AND claims.state IN ('queued', 'active', 'parked')"
            ") AND NOT EXISTS ("
            "SELECT 1 FROM token_cleanup_jobs AS cleanup "
            "WHERE cleanup.task_id = candidate.id"
            ") AND NOT EXISTS ("
            "SELECT 1 FROM operation_receipts AS cleanup_receipt "
            "WHERE cleanup_receipt.task_id = candidate.id "
            "AND cleanup_receipt.token_cleanup_path IS NOT NULL"
            ") "
            "ORDER BY candidate.finished_at DESC, candidate.created_at DESC, candidate.id DESC "
            "LIMIT -1 OFFSET ?",
            (workspace_id, TERMINAL_TASK_RETENTION),
        ).fetchall()
        for task in victims:
            WorkspaceCoordinator._delete_task_resolution_receipts(connection, task)
            removed_task = connection.execute(
                "DELETE FROM tasks WHERE id = ? AND workspace_id = ? "
                "AND state IN ('completed', 'failed', 'expired') "
                "AND NOT EXISTS (SELECT 1 FROM claims WHERE claims.task_id = tasks.id "
                "AND claims.state IN ('queued', 'active', 'parked')) "
                "AND NOT EXISTS (SELECT 1 FROM token_cleanup_jobs AS cleanup "
                "WHERE cleanup.task_id = tasks.id) "
                "AND NOT EXISTS (SELECT 1 FROM operation_receipts AS cleanup_receipt "
                "WHERE cleanup_receipt.task_id = tasks.id "
                "AND cleanup_receipt.token_cleanup_path IS NOT NULL)",
                (task["id"], workspace_id),
            )
            if removed_task.rowcount != 1:
                raise StateError(
                    "Terminal task changed during retention pruning.",
                    details={"reason": "task-retention-changed", "task_id": task["id"]},
                )

    @staticmethod
    def _maintain(connection: sqlite3.Connection, workspace_id: str | None = None) -> None:
        now = time.time()
        if not math.isfinite(now):
            raise StateError(
                "System clock is invalid; scheduler maintenance cannot continue safely.",
                details={"reason": "system-clock-invalid"},
            )
        affected_workspaces = WorkspaceCoordinator._bound_open_task_times(
            connection, now, workspace_id
        )
        affected_workspaces.update(
            WorkspaceCoordinator._expire_tasks(connection, now, workspace_id)
        )
        if workspace_id is None:
            affected_workspaces.update(
                str(row["id"]) for row in connection.execute("SELECT id FROM workspaces").fetchall()
            )
        else:
            affected_workspaces.add(workspace_id)
        for affected_workspace_id in sorted(affected_workspaces):
            WorkspaceCoordinator._schedule_workspace(connection, affected_workspace_id, now)
            WorkspaceCoordinator._prune_terminal_tasks(connection, affected_workspace_id)
        WorkspaceCoordinator._prune_terminal_claims(connection)
        WorkspaceCoordinator._prune_delivered_operations(connection)

    @staticmethod
    def _authenticate_task(
        connection: sqlite3.Connection,
        workspace_id: str,
        token: str,
        *,
        require_active: bool = True,
    ) -> sqlite3.Row:
        token_hash = _token_hash(token)
        open_tasks = connection.execute(
            "SELECT * FROM tasks WHERE workspace_id = ? AND token_hash = ? "
            "AND state IN ('active', 'outcome_unknown') ORDER BY id",
            (workspace_id, token_hash),
        ).fetchall()
        if len(open_tasks) > 1:
            raise StateError(
                "Task token identifies more than one open task.",
                details={"reason": "open-task-token-ambiguous"},
            )
        task = open_tasks[0] if open_tasks else None
        if task is None:
            task = connection.execute(
                "SELECT * FROM tasks WHERE workspace_id = ? AND token_hash = ? "
                "ORDER BY created_at DESC, id DESC LIMIT 1",
                (workspace_id, token_hash),
            ).fetchone()
        if task is None:
            raise AuthorizationError("Task token is invalid for this workspace.")
        if require_active and task["state"] != "active":
            raise AuthorizationError(
                f"Task is not active: {task['state']}.",
                details={"task_id": task["id"], "state": task["state"]},
            )
        return task

    @staticmethod
    def _normalize_writes(root: str, writes: Sequence[str]) -> tuple[str, ...]:
        root_path = Path(root)
        normalized: set[str] = set()
        for value in writes:
            if not isinstance(value, str) or not value.strip():
                raise UsageError("Write scopes cannot be empty.")
            if _has_control_characters(value):
                raise UsageError(
                    "Write scopes cannot contain Unicode control characters.",
                    details={"reason": "write-scope-control-character"},
                )
            candidate = Path(value)
            absolute = (
                candidate.expanduser().resolve(strict=False)
                if candidate.is_absolute()
                else (root_path / candidate).resolve(strict=False)
            )
            try:
                relative = absolute.relative_to(root_path)
            except ValueError as exc:
                raise UsageError(f"Write scope is outside the workspace: {value}") from exc
            text = PurePosixPath(relative).as_posix() or "."
            normalized.add(_platform_case_identity(text))
        return tuple(sorted(normalized))

    @staticmethod
    def _normalize_resources(resources: Sequence[str]) -> tuple[str, ...]:
        if any(not isinstance(resource, str) for resource in resources):
            raise UsageError("Resource names must be text.")
        if any(_has_control_characters(resource) for resource in resources):
            raise UsageError(
                "Resource names cannot contain Unicode control characters.",
                details={"reason": "resource-control-character"},
            )
        stripped = {resource.strip() for resource in resources}
        if "" in stripped:
            raise UsageError("Resource names cannot be empty.")
        # Resource names are logical scheduler identities, independent of the
        # host filesystem's case rules.  Keep write/root identities platform
        # aware, but always fold resource names for cross-platform replay.
        normalized = {resource.casefold() for resource in stripped}
        return tuple(sorted(normalized))

    @staticmethod
    def _public_task(task: sqlite3.Row) -> dict[str, Any]:
        return {
            "id": task["id"],
            "owner": task["owner"],
            "summary": task["summary"],
            "state": task["state"],
            "created_at": task["created_at"],
            "heartbeat_at": task["heartbeat_at"],
            "expires_at": task["expires_at"],
            "finished_at": task["finished_at"],
            "result": task["result"],
            "note": task["note"],
        }

    @staticmethod
    def _public_claim(connection: sqlite3.Connection, claim: sqlite3.Row) -> dict[str, Any]:
        scopes = WorkspaceCoordinator._claim_scopes(connection, claim["id"])
        return {
            "id": claim["id"],
            "task_id": claim["task_id"],
            "kind": claim["kind"],
            "state": claim["state"],
            "queue_order": claim["queue_order"],
            "writes": list(scopes["write"]),
            "resources": list(scopes["resource"]),
            "priority": WorkspaceCoordinator._claim_priority(claim, scopes),
            "parked_for": scopes["parked_for"][0] if scopes["parked_for"] else None,
            "created_at": claim["created_at"],
            "granted_at": claim["granted_at"],
        }

    def register(
        self,
        workspace: Path | str,
        *,
        operation_id: str,
        receipt_only: bool = False,
    ) -> dict[str, Any]:
        root = canonical_workspace(workspace)
        now = time.time()
        identifier = _workspace_id(root)
        operation = _operation(
            operation_id,
            identifier,
            "workspace.register",
            {"workspace": root},
            None,
        )
        with self._transaction() as connection:
            replay = self._replay_or_missing(
                connection,
                operation,
                receipt_only=receipt_only,
            )
            if replay is not None:
                return replay
            self._maintain(connection, identifier)
            existing = connection.execute(
                "SELECT * FROM workspaces WHERE id = ?", (identifier,)
            ).fetchone()
            if existing is not None and existing["root"] != root:
                raise StateError("Workspace identity collision detected.")
            connection.execute(
                "INSERT OR IGNORE INTO workspaces(id, root, registered_at, epoch) "
                "VALUES(?, ?, ?, 1)",
                (identifier, root, now),
            )
            registered = connection.execute(
                "SELECT * FROM workspaces WHERE id = ?", (identifier,)
            ).fetchone()
            assert registered is not None
            result = {
                "id": registered["id"],
                "root": registered["root"],
                "registered_at": registered["registered_at"],
                "epoch": registered["epoch"],
                "created": existing is None,
            }
            return self._record_operation(connection, operation, result)

    def unregister(
        self,
        workspace: Path | str,
        *,
        operation_id: str,
        receipt_only: bool = False,
    ) -> dict[str, Any]:
        root = canonical_workspace(workspace)
        operation = _operation(
            operation_id,
            _workspace_id(root),
            "workspace.unregister",
            {"workspace": root},
            None,
        )
        with self._transaction() as connection:
            replay = self._replay_or_missing(
                connection,
                operation,
                receipt_only=receipt_only,
            )
            if replay is not None:
                return replay
            self._maintain(connection, _workspace_id(root))
            registered = self._workspace(connection, root)
            open_task = connection.execute(
                "SELECT 1 FROM tasks WHERE workspace_id = ? "
                "AND state IN ('active', 'outcome_unknown') LIMIT 1",
                (registered["id"],),
            ).fetchone()
            open_claim = connection.execute(
                "SELECT 1 FROM claims WHERE workspace_id = ? "
                "AND state IN ('queued', 'active', 'parked') LIMIT 1",
                (registered["id"],),
            ).fetchone()
            pending_receipt_count = int(
                connection.execute(
                    "SELECT COUNT(*) FROM operation_receipts WHERE workspace_id = ? "
                    "AND finalized_at IS NULL",
                    (registered["id"],),
                ).fetchone()[0]
            )
            cleanup_job_count = int(
                connection.execute(
                    "SELECT COUNT(*) FROM token_cleanup_jobs WHERE workspace_id = ?",
                    (registered["id"],),
                ).fetchone()[0]
            )
            cleanup_receipt_count = int(
                connection.execute(
                    "SELECT COUNT(*) FROM operation_receipts WHERE workspace_id = ? "
                    "AND token_cleanup_path IS NOT NULL",
                    (registered["id"],),
                ).fetchone()[0]
            )
            if open_task is not None or open_claim is not None:
                raise BusyError("Workspace still has open tasks or claims.")
            if pending_receipt_count:
                raise BusyError(
                    "Workspace still has pending operation receipts.",
                    details={
                        "reason": "workspace-pending-operation-receipts",
                        "pending_operation_receipts": pending_receipt_count,
                    },
                )
            if cleanup_job_count or cleanup_receipt_count:
                raise BusyError(
                    "Workspace still has task-token cleanup obligations.",
                    details={
                        "reason": "workspace-token-cleanup-pending",
                        "token_cleanup_jobs": cleanup_job_count,
                        "token_cleanup_receipts": cleanup_receipt_count,
                    },
                )
            capacity_before = self._operation_capacity(connection)["reserved_capacity"]
            terminal_tasks = connection.execute(
                "SELECT * FROM tasks WHERE workspace_id = ? "
                "AND state IN ('completed', 'failed', 'expired')",
                (registered["id"],),
            ).fetchall()
            for terminal_task in terminal_tasks:
                self._delete_task_resolution_receipts(connection, terminal_task)
            connection.execute("DELETE FROM workspaces WHERE id = ?", (registered["id"],))
            return self._record_operation(
                connection,
                operation,
                {"id": registered["id"], "root": root, "removed": True},
                capacity_before=capacity_before,
            )

    def list_workspaces(self) -> dict[str, Any]:
        with self._transaction() as connection:
            self._maintain(connection)
            rows = connection.execute("SELECT * FROM workspaces ORDER BY root").fetchall()
            values = []
            for row in rows:
                active_tasks = connection.execute(
                    "SELECT COUNT(*) AS count FROM tasks WHERE workspace_id = ? "
                    "AND state IN ('active', 'outcome_unknown')",
                    (row["id"],),
                ).fetchone()["count"]
                open_claims = connection.execute(
                    "SELECT COUNT(*) AS count FROM claims WHERE workspace_id = ? "
                    "AND state IN ('queued', 'active', 'parked')",
                    (row["id"],),
                ).fetchone()["count"]
                values.append(
                    {
                        "id": row["id"],
                        "root": row["root"],
                        "registered_at": row["registered_at"],
                        "epoch": row["epoch"],
                        "active_tasks": active_tasks,
                        "open_claims": open_claims,
                    }
                )
            return {"schema_version": SCHEMA_VERSION, "workspaces": values}

    def status(self, workspace: Path | str) -> dict[str, Any]:
        root = canonical_workspace(workspace)
        with self._transaction() as connection:
            self._maintain(connection, _workspace_id(root))
            registered = self._workspace(connection, root)
            tasks = connection.execute(
                "SELECT * FROM tasks WHERE workspace_id = ? "
                "AND state IN ('active', 'outcome_unknown') ORDER BY created_at",
                (registered["id"],),
            ).fetchall()
            claims = connection.execute(
                "SELECT * FROM claims WHERE workspace_id = ? "
                "AND state IN ('queued', 'active', 'parked') ORDER BY queue_order",
                (registered["id"],),
            ).fetchall()
            cleanup_jobs = connection.execute(
                "SELECT task_id, token_file_path, reason, created_at, completed_at, "
                "last_attempt_at, attempt_count FROM token_cleanup_jobs "
                "WHERE workspace_id = ? ORDER BY created_at, task_id",
                (registered["id"],),
            ).fetchall()
            blocked = any(task["state"] == "outcome_unknown" for task in tasks)
            public_tasks = []
            for task in tasks:
                public_task = self._public_task(task)
                drain = self._task_drain_request(connection, registered["id"], task["id"])
                if drain is not None:
                    public_task["drain_requested"] = drain
                public_tasks.append(public_task)
            result = {
                "schema_version": SCHEMA_VERSION,
                "coordination_mode": "required",
                "ready": not blocked,
                "blocked": blocked,
                "workspace": {
                    "id": registered["id"],
                    "root": registered["root"],
                    "registered_at": registered["registered_at"],
                    "epoch": registered["epoch"],
                },
                "tasks": public_tasks,
                "token_cleanup_jobs": [
                    {
                        "task_id": job["task_id"],
                        "token_file_path": job["token_file_path"],
                        "reason": job["reason"],
                        "created_at": job["created_at"],
                        "completed_at": job["completed_at"],
                        "last_attempt_at": job["last_attempt_at"],
                        "attempt_count": job["attempt_count"],
                    }
                    for job in cleanup_jobs
                ],
                "claims": [self._public_claim(connection, claim) for claim in claims],
            }
            return result

    def identify_task(self, workspace: Path | str, token: str) -> dict[str, Any]:
        """Identify one open task without maintenance, scheduling, or lease renewal."""

        root = canonical_workspace(workspace)
        connection = open_database(self.paths)
        try:
            registered = self._workspace(connection, root)
            tasks = connection.execute(
                "SELECT * FROM tasks WHERE workspace_id = ? AND token_hash = ? "
                "AND state IN ('active', 'outcome_unknown') ORDER BY id",
                (registered["id"], _token_hash(token)),
            ).fetchall()
            if len(tasks) > 1:
                raise StateError(
                    "Task token identifies more than one open task.",
                    details={"reason": "open-task-token-ambiguous"},
                )
            if not tasks:
                raise AuthorizationError(
                    "Task token does not identify an open task in this workspace.",
                    details={"reason": "open-task-token-not-found"},
                )
            return self._public_task(tasks[0])
        finally:
            connection.close()

    def replay_terminal_task_release_without_token(
        self,
        workspace: Path | str,
        *,
        operation_id: str,
        result: str,
        note: str | None,
        token_cleanup_path: str | None,
        token_file_path: str | None = None,
    ) -> dict[str, Any]:
        """Read a finalized task-release receipt after exact token cleanup."""

        if token_file_path is None:
            token_file_path = token_cleanup_path
        if token_file_path is None:
            raise UsageError(
                "Missing-token task release replay requires the original token path.",
                details={"reason": "task-token-path-invalid"},
            )
        return self.replay_terminal_lifecycle_without_token(
            workspace,
            action="task.release",
            operation_id=operation_id,
            token_file_path=token_file_path,
            note=note,
            result=result,
            token_cleanup_path=token_cleanup_path,
        )

    def replay_terminal_lifecycle_without_token(
        self,
        workspace: Path | str,
        *,
        action: str,
        operation_id: str,
        token_file_path: str,
        claim_id: str | None = None,
        note: str | None = None,
        result: str | None = None,
        token_cleanup_path: str | None = None,
        ttl_seconds: float | None = None,
        writes: Sequence[str] = (),
        resources: Sequence[str] = (),
        freeze: bool = False,
        priority: str = "normal",
        wait_seconds: float = 0.0,
        requested_wait_seconds: float | None = None,
        keep_queued: bool = False,
    ) -> dict[str, Any]:
        """Replay one safe terminal lifecycle receipt after token cleanup.

        This is deliberately receipt-only: it never authenticates or mutates a
        task and accepts only a durable operation identity plus its exact
        original parameters.  The path lock covers both the absence check and
        the bound task/cleanup proof so a token cannot be replaced mid-replay.
        """

        allowed_actions = {
            "task.heartbeat",
            "claim.acquire",
            "freeze.acquire",
            "task.park",
            "claim.release",
            "queue.cancel",
            "task.release",
        }
        if action not in allowed_actions:
            raise UsageError(
                "Missing-token lifecycle replay is not supported for this action.",
                details={"reason": "operation-action-not-allowed", "action": action},
            )
        root = canonical_workspace(workspace)
        validated_id = validate_operation_id(operation_id)
        canonical_token_path = _canonical_lifecycle_replay_token_path(token_file_path)
        if action == "task.heartbeat":
            if note is not None and not isinstance(note, str):
                raise UsageError("Task heartbeat note must be text.")
            if ttl_seconds is not None:
                _validate_ttl(ttl_seconds)
            parameters = {
                "note": note,
                "ttl_seconds": None if ttl_seconds is None else float(ttl_seconds),
                "workspace": root,
            }
        elif action in {"claim.acquire", "freeze.acquire"}:
            normalized_writes = self._normalize_writes(root, writes)
            normalized_resources = self._normalize_resources(resources)
            if action == "freeze.acquire":
                if normalized_writes or normalized_resources:
                    raise UsageError("A freeze claim cannot include write or resource scopes.")
                if priority not in {"normal", "urgent"}:
                    raise UsageError("Claim priority must be normal or urgent.")
            else:
                if freeze or (not normalized_writes and not normalized_resources):
                    raise UsageError("A claim needs at least one write path or resource.")
                if priority != "normal":
                    raise UsageError("Urgent priority is only supported for freeze claims.")
            _validate_wait(wait_seconds, "Claim")
            if requested_wait_seconds is None:
                requested_wait_seconds = wait_seconds
            _validate_wait(requested_wait_seconds, "Requested claim")
            if wait_seconds > requested_wait_seconds:
                raise UsageError(
                    "Effective claim wait cannot exceed the caller-requested wait.",
                    details={"reason": "effective-wait-exceeds-requested"},
                )
            parameters = {
                "keep_queued": keep_queued,
                "priority": priority,
                "requested_wait_seconds": float(requested_wait_seconds),
                "resources": list(normalized_resources),
                "workspace": root,
                "writes": list(normalized_writes),
            }
        elif action == "task.park":
            _validate_wait(wait_seconds, "Task park")
            if requested_wait_seconds is None:
                requested_wait_seconds = wait_seconds
            _validate_wait(requested_wait_seconds, "Requested task park")
            if wait_seconds > requested_wait_seconds:
                raise UsageError(
                    "Effective task park wait cannot exceed the caller-requested wait.",
                    details={"reason": "effective-wait-exceeds-requested"},
                )
            parameters = {
                "requested_wait_seconds": float(requested_wait_seconds),
                "workspace": root,
            }
        elif action in {"claim.release", "queue.cancel"}:
            if not _entity_id(claim_id):
                raise UsageError(
                    "Missing-token claim replay requires a claim ID.",
                    details={"reason": "claim-id-invalid"},
                )
            parameters = {"claim_id": claim_id, "workspace": root}
        else:
            if result not in {"completed", "failed", "outcome-unknown"}:
                raise UsageError(
                    "Missing-token task release replay requires a terminal result.",
                    details={"reason": "terminal-release-replay-invalid"},
                )
            if note is not None and not isinstance(note, str):
                raise UsageError("Task release note must be text.")
            if result in {"completed", "failed"}:
                if token_cleanup_path is None:
                    token_cleanup_path = canonical_token_path
                canonical_cleanup_path = _canonical_lifecycle_replay_token_path(token_cleanup_path)
                if _token_path_identity(canonical_cleanup_path) != _token_path_identity(
                    canonical_token_path
                ):
                    raise UsageError(
                        "Task release cleanup path does not match the missing token path.",
                        details={"reason": "task-token-path-mismatch"},
                    )
            elif token_cleanup_path is not None:
                raise UsageError(
                    "Outcome-unknown task release cannot include a cleanup path.",
                    details={"reason": "token-cleanup-path-unexpected"},
                )
            else:
                canonical_cleanup_path = None
            parameters = {
                "note": note,
                "result": result,
                "token_cleanup_path": canonical_cleanup_path,
                "workspace": root,
            }
        parameters_json = canonical_json(parameters)
        identifier = _workspace_id(root)
        with task_token_path_lock(self.paths, Path(canonical_token_path)):
            locked_token_path = _canonical_lifecycle_replay_token_path(canonical_token_path)
            if _token_path_identity(locked_token_path) != _token_path_identity(
                canonical_token_path
            ):
                raise UsageError(
                    "Task token path changed while acquiring its lifecycle lock.",
                    details={
                        "reason": "task-token-path-identity-unstable",
                        "operation_id": validated_id,
                        "recovery_required": True,
                    },
                )
            canonical_token_path = locked_token_path
            if os.path.lexists(canonical_token_path):
                raise UsageError(
                    "A missing-token lifecycle replay requires the owner token to remain absent.",
                    details={"reason": "task-token-still-present"},
                )
            with self._transaction() as connection:
                receipt = connection.execute(
                    "SELECT * FROM operation_receipts WHERE operation_id = ?",
                    (validated_id,),
                ).fetchone()
                if receipt is None:
                    raise StateError(
                        "Operation receipt does not exist.",
                        details={
                            "reason": "operation-receipt-missing",
                            "operation_id": validated_id,
                        },
                    )
                if (
                    receipt["workspace_id"] != identifier
                    or receipt["action"] != action
                    or receipt["parameters_json"] != parameters_json
                ):
                    raise UsageError(
                        "Operation ID is already bound to a different mutation.",
                        details={
                            "reason": "operation-id-conflict",
                            "operation_id": validated_id,
                            "existing_action": receipt["action"],
                        },
                    )
                owner_token_hash = receipt["owner_token_hash"]
                expected_fingerprint = operation_fingerprint(
                    identifier,
                    action,
                    parameters_json,
                    owner_token_hash,
                )
                if (
                    not is_sha256_hex(owner_token_hash)
                    or receipt["fingerprint"] != expected_fingerprint
                ):
                    raise StateError(
                        "Lifecycle receipt is internally inconsistent.",
                        details={
                            "reason": "operation-receipt-invalid",
                            "operation_id": validated_id,
                            "recovery_required": True,
                        },
                    )
                if receipt["finalized_at"] is None:
                    raise BusyError(
                        "Operation is committed but has not reached its terminal receipt yet.",
                        details={
                            "reason": "operation-in-progress",
                            "operation_id": validated_id,
                            "fingerprint": receipt["fingerprint"],
                        },
                    )
                try:
                    _, stored_result, _ = self._validated_ack_receipt(
                        receipt,
                        validated_id,
                        str(receipt["fingerprint"]),
                        receipt_delivery_digest(receipt["result_json"], receipt["terminal_json"]),
                    )
                except BusyError:
                    raise
                except (TypeError, ValueError, json.JSONDecodeError) as exc:
                    raise StateError(
                        "Lifecycle receipt is internally inconsistent.",
                        details={
                            "reason": "operation-receipt-invalid",
                            "operation_id": validated_id,
                            "recovery_required": True,
                        },
                    ) from exc
                except SchedulerError as exc:
                    raise StateError(
                        "Lifecycle receipt is internally inconsistent.",
                        details={
                            "reason": "operation-receipt-invalid",
                            "operation_id": validated_id,
                            "recovery_required": True,
                        },
                    ) from exc
                terminal_proof = self._operation_terminal_proof(receipt, stored_result)
                normal_release_replay = (
                    action == "task.release"
                    and terminal_proof is None
                    and receipt["terminal_json"] is None
                    and receipt["retired_at"] is None
                    and receipt["delivered_at"] is not None
                    and stored_result.get("state") in {"completed", "failed"}
                    and stored_result.get("result") == stored_result.get("state")
                )
                if receipt["retired_at"] is None and not normal_release_replay:
                    raise StateError(
                        "Lifecycle receipt has not reached a terminal retirement fence.",
                        details={
                            "reason": "task-token-missing",
                            "operation_id": validated_id,
                            "recovery_required": True,
                        },
                    )
                task = connection.execute(
                    "SELECT * FROM tasks WHERE id = ? AND workspace_id = ?",
                    (receipt["task_id"], identifier),
                ).fetchone()
                if action in {"claim.release", "queue.cancel"}:
                    claim = connection.execute(
                        "SELECT * FROM claims WHERE id = ? AND workspace_id = ?",
                        (claim_id, identifier),
                    ).fetchone()
                    if (
                        claim is None
                        or claim["task_id"] != receipt["task_id"]
                        or stored_result.get("id") != claim["id"]
                        or stored_result.get("task_id") != claim["task_id"]
                        or stored_result.get("state") != claim["state"]
                        or action == "queue.cancel"
                        and claim["state"] != "cancelled"
                        or action == "claim.release"
                        and claim["state"] not in {"released", "cancelled"}
                    ):
                        raise StateError(
                            "Missing claim receipt is not bound to its terminal claim.",
                            details={
                                "reason": "operation-receipt-invalid",
                                "operation_id": validated_id,
                                "recovery_required": True,
                            },
                        )
                valid_special_retirement = (
                    (
                        action in {"claim.acquire", "freeze.acquire", "task.park"}
                        or normal_release_replay
                    )
                    and terminal_proof is None
                    and (
                        normal_release_replay
                        or (
                            stored_result.get("aborted") is True
                            and stored_result.get("reason")
                            in {
                                "task-ttl-expired",
                                "task-released",
                            }
                        )
                    )
                )
                if terminal_proof is None and not valid_special_retirement:
                    raise StateError(
                        "Missing task token has no completed lifecycle proof.",
                        details={
                            "reason": "task-token-missing",
                            "operation_id": validated_id,
                            "recovery_required": True,
                        },
                    )
                task_terminal = task is not None and task["state"] in {
                    "completed",
                    "failed",
                    "expired",
                }
                if terminal_proof is not None:
                    terminal_matches = (
                        task is not None
                        and task["state"] == terminal_proof.get("terminal_state")
                        and task["result"] == terminal_proof.get("terminal_result")
                        and task["finished_at"] == terminal_proof.get("terminal_finished_at")
                    )
                elif normal_release_replay:
                    terminal_matches = (
                        task is not None
                        and task["state"] == stored_result.get("state")
                        and task["result"] == stored_result.get("result")
                        and task["finished_at"] == stored_result.get("finished_at")
                    )
                else:
                    reason = stored_result.get("reason")
                    expected_result = (
                        "expired"
                        if reason == "task-ttl-expired"
                        else (
                            task["state"]
                            if task is not None and task["state"] in {"completed", "failed"}
                            else None
                        )
                        if reason == "task-released"
                        else (
                            f"recovered-{task['state']}"
                            if task is not None and task["state"] in {"completed", "failed"}
                            else None
                        )
                        if reason == "task-recovery-resolved"
                        else None
                    )
                    terminal_matches = (
                        task is not None
                        and (
                            (reason == "task-ttl-expired" and task["state"] == "expired")
                            or (
                                reason in {"task-released", "task-recovery-resolved"}
                                and task["state"] in {"completed", "failed"}
                            )
                        )
                        and task["result"] == expected_result
                        and task["finished_at"] is not None
                        and receipt["retired_at"] is not None
                        and float(receipt["retired_at"]) >= float(task["finished_at"])
                    )
                cleanup_job = (
                    connection.execute(
                        "SELECT 1 FROM token_cleanup_jobs WHERE task_id = ?",
                        (receipt["task_id"],),
                    ).fetchone()
                    if task is not None
                    else None
                )
                cleanup_receipt = (
                    connection.execute(
                        "SELECT 1 FROM operation_receipts WHERE task_id = ? "
                        "AND operation_id <> ? "
                        "AND (token_cleanup_path IS NOT NULL OR token_cleanup_identity IS NOT NULL) "
                        "LIMIT 1",
                        (receipt["task_id"], validated_id),
                    ).fetchone()
                    if task is not None
                    else None
                )
                token_path_lineage = False
                token_path_lineage_reason = "task-token-missing"
                # ACK commits delivery before token deletion; a post-delete
                # interruption leaves only this exact release receipt pending.
                pending_release_cleanup_replay = False
                if task is not None:

                    def is_canonical_token_path(value: object) -> bool:
                        if not isinstance(value, str):
                            return False
                        try:
                            return _canonical_token_file_path(value) == value
                        except UsageError:
                            return False

                    task_token_path = task["token_file_path"]
                    task_token_identity = task["token_file_identity"]
                    if task_token_path is not None or task_token_identity is not None:
                        try:
                            canonical_task_path = _canonical_token_file_path(task_token_path)
                        except (TypeError, UsageError) as exc:
                            raise StateError(
                                "Retained task token path is not canonical.",
                                details={
                                    "reason": "operation-receipt-invalid",
                                    "operation_id": validated_id,
                                    "recovery_required": True,
                                },
                            ) from exc
                        if (
                            canonical_task_path != task_token_path
                            or task_token_identity != _token_path_identity(canonical_task_path)
                            or _token_path_identity(canonical_task_path)
                            != _token_path_identity(canonical_token_path)
                        ):
                            raise StateError(
                                "Missing task token path does not match the retained task identity.",
                                details={
                                    "reason": "operation-receipt-invalid",
                                    "operation_id": validated_id,
                                    "recovery_required": True,
                                },
                            )
                        token_path_lineage = True

                    release_receipts = connection.execute(
                        "SELECT * FROM operation_receipts WHERE task_id = ? "
                        "AND action = 'task.release' ORDER BY created_at DESC, operation_id DESC",
                        (task["id"],),
                    ).fetchall()
                    if len(release_receipts) > 1:
                        raise StateError(
                            "Task has ambiguous task-release cleanup lineage.",
                            details={
                                "reason": "operation-receipt-invalid",
                                "operation_id": validated_id,
                                "recovery_required": True,
                            },
                        )
                    if release_receipts:
                        release_receipt = release_receipts[0]
                        token_path_lineage_reason = "operation-receipt-invalid"
                        if (
                            release_receipt["workspace_id"] != identifier
                            or release_receipt["owner_token_hash"] != owner_token_hash
                        ):
                            raise StateError(
                                "Task release cleanup lineage is bound to a different task owner.",
                                details={
                                    "reason": "operation-receipt-invalid",
                                    "operation_id": validated_id,
                                    "recovery_required": True,
                                },
                            )
                        try:
                            release_parameters, release_result, _ = self._validated_ack_receipt(
                                release_receipt,
                                str(release_receipt["operation_id"]),
                                str(release_receipt["fingerprint"]),
                                receipt_delivery_digest(
                                    release_receipt["result_json"],
                                    release_receipt["terminal_json"],
                                ),
                            )
                        except (TypeError, ValueError, json.JSONDecodeError) as exc:
                            raise StateError(
                                "Task release cleanup lineage is internally inconsistent.",
                                details={
                                    "reason": "operation-receipt-invalid",
                                    "operation_id": validated_id,
                                    "recovery_required": True,
                                },
                            ) from exc
                        except SchedulerError as exc:
                            raise StateError(
                                "Task release cleanup lineage is internally inconsistent.",
                                details={
                                    "reason": "operation-receipt-invalid",
                                    "operation_id": validated_id,
                                    "recovery_required": True,
                                },
                            ) from exc
                        release_path = release_parameters.get("token_cleanup_path")
                        release_kind = release_parameters.get("result")
                        release_proof = self._operation_terminal_proof(
                            release_receipt,
                            release_result,
                        )
                        if release_kind in {"completed", "failed"}:
                            release_valid = (
                                is_canonical_token_path(release_path)
                                and _token_path_identity(release_path)
                                == _token_path_identity(canonical_token_path)
                                and release_result.get("id") == task["id"]
                                and release_result.get("state") == task["state"]
                                and release_result.get("result") == task["result"]
                                and release_result.get("finished_at") == task["finished_at"]
                                and release_receipt["delivered_at"] is not None
                                and (
                                    release_proof is None
                                    or (
                                        release_proof.get("terminal_state") == task["state"]
                                        and release_proof.get("terminal_result") == task["result"]
                                        and release_proof.get("token_cleanup_completed") is True
                                    )
                                )
                            )
                        elif release_kind == "outcome-unknown":
                            # This receipt is historical lineage only. It has no
                            # cleanup path and must not prove the candidate path.
                            release_finished_at = release_result.get("finished_at")
                            release_valid = (
                                release_path is None
                                and release_result.get("id") == task["id"]
                                and release_result.get("state") == "outcome_unknown"
                                and release_result.get("result") == "outcome-unknown"
                                and not isinstance(release_finished_at, bool)
                                and isinstance(release_finished_at, (int, float))
                                and math.isfinite(float(release_finished_at))
                                and not isinstance(task["finished_at"], bool)
                                and isinstance(task["finished_at"], (int, float))
                                and math.isfinite(float(task["finished_at"]))
                                and float(release_finished_at) <= float(task["finished_at"])
                                and isinstance(release_proof, dict)
                                and release_proof.get("resolution_reason")
                                == "task-recovery-resolved"
                                and release_proof.get("terminal_state") == task["state"]
                                and release_proof.get("terminal_result") == task["result"]
                                and release_proof.get("token_cleanup_completed") is True
                            )
                        else:
                            release_valid = False
                        if not release_valid:
                            raise StateError(
                                "Task release cleanup lineage does not match the terminal task.",
                                details={
                                    "reason": "operation-receipt-invalid",
                                    "operation_id": validated_id,
                                    "recovery_required": True,
                                },
                            )
                        if release_kind in {"completed", "failed"}:
                            token_path_lineage = True
                            pending_release_cleanup_replay = (
                                action == "task.release"
                                and release_receipt["operation_id"] == validated_id
                                and release_receipt["delivered_at"] is not None
                                and release_receipt["terminal_json"] is None
                                and release_receipt["retired_at"] is None
                                and release_receipt["token_cleanup_path"] == release_path
                                and release_receipt["token_cleanup_identity"]
                                == _token_path_identity(canonical_token_path)
                            )
                            if pending_release_cleanup_replay and (
                                task["token_file_path"] != canonical_token_path
                                or task["token_file_identity"]
                                != _token_path_identity(canonical_token_path)
                            ):
                                raise StateError(
                                    "Missing task token path does not match the retained task identity.",
                                    details={
                                        "reason": "operation-receipt-invalid",
                                        "operation_id": validated_id,
                                        "recovery_required": True,
                                    },
                                )

                    start_receipts = connection.execute(
                        "SELECT * FROM operation_receipts WHERE task_id = ? "
                        "AND action = 'task.start' ORDER BY created_at DESC, operation_id DESC",
                        (task["id"],),
                    ).fetchall()
                    if len(start_receipts) > 1:
                        raise StateError(
                            "Task has ambiguous task-start cleanup lineage.",
                            details={
                                "reason": "operation-receipt-invalid",
                                "operation_id": validated_id,
                                "recovery_required": True,
                            },
                        )
                    if start_receipts:
                        start_receipt = start_receipts[0]
                        token_path_lineage_reason = "operation-receipt-invalid"
                        if (
                            start_receipt["workspace_id"] != identifier
                            or start_receipt["owner_token_hash"] != owner_token_hash
                        ):
                            raise StateError(
                                "Task-start cleanup lineage is bound to a different task owner.",
                                details={
                                    "reason": "operation-receipt-invalid",
                                    "operation_id": validated_id,
                                    "recovery_required": True,
                                },
                            )
                        try:
                            start_parameters, start_result, _ = self._validated_ack_receipt(
                                start_receipt,
                                str(start_receipt["operation_id"]),
                                str(start_receipt["fingerprint"]),
                                receipt_delivery_digest(
                                    start_receipt["result_json"],
                                    start_receipt["terminal_json"],
                                ),
                            )
                            start_proof = self._operation_terminal_proof(
                                start_receipt, start_result
                            )
                        except (TypeError, ValueError, json.JSONDecodeError) as exc:
                            raise StateError(
                                "Task-start cleanup lineage is internally inconsistent.",
                                details={
                                    "reason": "operation-receipt-invalid",
                                    "operation_id": validated_id,
                                    "recovery_required": True,
                                },
                            ) from exc
                        except SchedulerError as exc:
                            raise StateError(
                                "Task-start cleanup lineage is internally inconsistent.",
                                details={
                                    "reason": "operation-receipt-invalid",
                                    "operation_id": validated_id,
                                    "recovery_required": True,
                                },
                            ) from exc
                        start_path = start_parameters.get("token_file_path")
                        if (
                            not is_canonical_token_path(start_path)
                            or _token_path_identity(start_path)
                            != _token_path_identity(canonical_token_path)
                            or start_result.get("id") != task["id"]
                            or not isinstance(start_proof, dict)
                            or start_proof.get("terminal_state") != task["state"]
                            or start_proof.get("terminal_result") != task["result"]
                            or start_proof.get("terminal_finished_at") != task["finished_at"]
                            or (
                                start_proof.get("token_cleanup_completed") is not True
                                and not (
                                    pending_release_cleanup_replay
                                    and "token_cleanup_completed" not in start_proof
                                )
                            )
                        ):
                            raise StateError(
                                "Task-start cleanup lineage does not match the terminal task.",
                                details={
                                    "reason": "operation-receipt-invalid",
                                    "operation_id": validated_id,
                                    "recovery_required": True,
                                },
                            )
                        token_path_lineage = True

                    candidate_identity = _token_path_identity(canonical_token_path)
                    open_conflict = connection.execute(
                        "SELECT id FROM tasks WHERE token_file_identity = ? "
                        "AND state IN ('active', 'outcome_unknown') AND id <> ? LIMIT 1",
                        (candidate_identity, task["id"]),
                    ).fetchone()
                    retained_task_conflict = connection.execute(
                        "SELECT id FROM tasks WHERE token_file_identity = ? AND id <> ? LIMIT 1",
                        (candidate_identity, task["id"]),
                    ).fetchone()
                    cleanup_conflict = connection.execute(
                        "SELECT task_id FROM token_cleanup_jobs "
                        "WHERE token_file_identity = ? LIMIT 1",
                        (candidate_identity,),
                    ).fetchone()
                    receipt_conflict = connection.execute(
                        "SELECT operation_id, task_id FROM operation_receipts "
                        "WHERE token_cleanup_identity = ? AND operation_id <> ? LIMIT 1",
                        (candidate_identity, validated_id),
                    ).fetchone()
                    if (
                        open_conflict is not None
                        or retained_task_conflict is not None
                        or cleanup_conflict is not None
                        or receipt_conflict is not None
                    ):
                        raise StateError(
                            "Missing task token path is still bound to another live cleanup obligation.",
                            details={
                                "reason": "task-token-path-in-use",
                                "operation_id": validated_id,
                                "recovery_required": True,
                            },
                        )
                if (
                    not task_terminal
                    or not terminal_matches
                    or task["token_hash"] != owner_token_hash
                    or not token_path_lineage
                    or cleanup_job is not None
                    or cleanup_receipt is not None
                    or os.path.lexists(canonical_token_path)
                ):
                    raise StateError(
                        "Missing task token has no completed lifecycle cleanup proof.",
                        details={
                            "reason": token_path_lineage_reason,
                            "operation_id": validated_id,
                            "recovery_required": True,
                        },
                    )
                return self._operation_result(receipt, replayed=True)

    def acknowledge_receipt(
        self,
        operation_id: str,
        fingerprint: str,
        delivery_digest: str,
    ) -> dict[str, Any]:
        """Mark a durable receipt delivered and finish any bound token cleanup."""

        validated_id = validate_operation_id(operation_id)
        if not is_sha256_hex(fingerprint):
            raise UsageError(
                "Receipt fingerprint must be lowercase SHA-256 hex.",
                details={"reason": "operation-fingerprint-invalid"},
            )
        if not is_sha256_hex(delivery_digest):
            raise UsageError(
                "Receipt delivery digest must be lowercase SHA-256 hex.",
                details={"reason": "operation-delivery-digest-invalid"},
            )
        connection = open_database(self.paths)
        try:
            receipt = connection.execute(
                "SELECT * FROM operation_receipts WHERE operation_id = ?",
                (validated_id,),
            ).fetchone()
            if receipt is None:
                raise StateError(
                    "Operation receipt does not exist.",
                    details={
                        "reason": "operation-receipt-missing",
                        "operation_id": validated_id,
                    },
                )
            if receipt["fingerprint"] != fingerprint:
                raise UsageError(
                    "Receipt fingerprint does not match its operation ID.",
                    details={
                        "reason": "operation-fingerprint-mismatch",
                        "operation_id": validated_id,
                    },
                )
            if receipt["action"] == "task.start":
                candidate_task_id = receipt["task_id"]
                if not _entity_id(candidate_task_id):
                    raise StateError(
                        "Task-start receipt has no stable cleanup identity.",
                        details={
                            "reason": "operation-receipt-invalid",
                            "operation_id": validated_id,
                        },
                    )
                job = connection.execute(
                    "SELECT token_file_path, completed_at FROM token_cleanup_jobs "
                    "WHERE task_id = ?",
                    (candidate_task_id,),
                ).fetchone()
                if (
                    job is not None
                    and job["completed_at"] is None
                    and receipt["delivered_at"] is None
                ):
                    raise BusyError(
                        "Task-start cleanup must finish before its delivered version can be "
                        "acknowledged.",
                        details={
                            "reason": "operation-recovery-pending",
                            "operation_id": validated_id,
                            "task_id": candidate_task_id,
                            "next_action": "Replay task.start so its exact cleanup can finish.",
                        },
                    )
            _, _stored_result, cleanup_path = self._validated_ack_receipt(
                receipt,
                validated_id,
                fingerprint,
                delivery_digest,
            )
            lock_path = cleanup_path
        finally:
            connection.close()
        if lock_path is not None:
            with task_token_path_lock(self.paths, Path(lock_path)):
                return self._acknowledge_receipt_locked(
                    validated_id,
                    fingerprint,
                    delivery_digest,
                )
        return self._acknowledge_receipt_locked(
            validated_id,
            fingerprint,
            delivery_digest,
        )

    def _acknowledge_receipt_locked(
        self,
        operation_id: str,
        fingerprint: str,
        delivery_digest: str,
    ) -> dict[str, Any]:
        now = time.time()
        if not math.isfinite(now):
            raise StateError(
                "System clock is invalid; receipt acknowledgement cannot continue safely.",
                details={"reason": "system-clock-invalid"},
            )
        with self._transaction() as connection:
            receipt = connection.execute(
                "SELECT * FROM operation_receipts WHERE operation_id = ?",
                (operation_id,),
            ).fetchone()
            if receipt is None:
                raise StateError(
                    "Operation receipt does not exist.",
                    details={
                        "reason": "operation-receipt-missing",
                        "operation_id": operation_id,
                    },
                )
            parameters, stored_result, cleanup_path = self._validated_ack_receipt(
                receipt,
                operation_id,
                fingerprint,
                delivery_digest,
            )
            owner_token_hash = receipt["owner_token_hash"]
            workspace_id = str(receipt["workspace_id"])
            action = str(receipt["action"])
            cleanup_expected = action == "task.release" and parameters.get("result") in {
                "completed",
                "failed",
            }
            if cleanup_expected and cleanup_path is not None:
                open_task = self._open_task_token_conflict(
                    connection,
                    cleanup_path,
                    owner_token_hash,
                )
                if open_task is not None:
                    raise StateError(
                        "Terminal receipt token is still bound to an open task.",
                        details={
                            "reason": "receipt-token-cleanup-in-use",
                            "operation_id": operation_id,
                            "task_id": open_task["id"],
                            "workspace_id": open_task["workspace_id"],
                            "recovery_required": True,
                        },
                    )
            replayed = receipt["delivered_at"] is not None
            receipt_retired = (
                action == "task.start"
                and receipt["retired_at"] is not None
                and receipt["terminal_json"] is not None
            )
            delivered_at = self._causal_time(
                now,
                receipt["finalized_at"],
                receipt["retired_at"],
            )
            if not replayed:
                delivered = connection.execute(
                    "UPDATE operation_receipts SET delivered_at = ? "
                    "WHERE operation_id = ? AND fingerprint = ? AND delivered_at IS NULL "
                    "AND result_json = ? AND terminal_json IS ?",
                    (
                        delivered_at,
                        operation_id,
                        fingerprint,
                        receipt["result_json"],
                        receipt["terminal_json"],
                    ),
                )
                if delivered.rowcount != 1:
                    raise StateError(
                        "Operation delivery proof changed before acknowledgement committed.",
                        details={
                            "reason": "operation-delivery-changed",
                            "operation_id": operation_id,
                        },
                    )
            if action == "task.start":
                task_id = stored_result.get("id")
                assert isinstance(task_id, str)
                retired_token_cleanup_jobs = connection.execute(
                    "DELETE FROM token_cleanup_jobs WHERE task_id = ? AND completed_at IS NOT NULL",
                    (task_id,),
                ).rowcount
                if retired_token_cleanup_jobs:
                    self._retire_terminal_task_lifecycle_receipts(
                        connection,
                        workspace_id,
                        task_id,
                        delivered_at,
                    )
            if not cleanup_expected:
                self._prune_delivered_operations(connection)
        token_file_removed = cleanup_expected and cleanup_path is None
        if cleanup_path is not None:
            token_path = Path(str(cleanup_path))
            try:
                if not remove_matching_token_hash_file(token_path, owner_token_hash):
                    raise UsageError(
                        "Task token file no longer matches the acknowledged release.",
                        details={"reason": "token-cleanup-identity-mismatch"},
                    )
                token_file_removed = True
            except (OSError, RuntimeError, ValueError, UsageError) as exc:
                reason = getattr(exc, "details", {}).get("reason", "token-cleanup-failed")
                raise StateError(
                    "Receipt was acknowledged, but its task token cleanup is incomplete.",
                    details={
                        "reason": "receipt-token-cleanup-failed",
                        "cause_reason": reason,
                        "operation_id": operation_id,
                        "fingerprint": fingerprint,
                        "receipt_delivered": True,
                        "token_file_removed": False,
                        "recovery_required": True,
                        "task_id": (
                            stored_result.get("id") if isinstance(stored_result, dict) else None
                        ),
                    },
                ) from exc
        if cleanup_expected:
            task_id = stored_result.get("id")
            assert isinstance(task_id, str)
            with self._transaction() as connection:
                current = connection.execute(
                    "SELECT * FROM operation_receipts WHERE operation_id = ?",
                    (operation_id,),
                ).fetchone()
                if current is None:
                    raise StateError(
                        "Operation receipt disappeared during token cleanup.",
                        details={
                            "reason": "operation-receipt-missing",
                            "operation_id": operation_id,
                        },
                    )
                self._validated_ack_receipt(
                    current,
                    operation_id,
                    fingerprint,
                    delivery_digest,
                )
                cleared = connection.execute(
                    "UPDATE operation_receipts SET token_cleanup_path = NULL, "
                    "token_cleanup_identity = NULL "
                    "WHERE operation_id = ? AND fingerprint = ? "
                    "AND token_cleanup_path IS NOT NULL",
                    (operation_id, fingerprint),
                )
                if cleanup_path is not None and cleared.rowcount != 1:
                    raise StateError(
                        "Operation receipt cleanup identity changed after token deletion.",
                        details={
                            "reason": "operation-receipt-invalid",
                            "operation_id": operation_id,
                            "recovery_required": True,
                        },
                    )
                self._mark_released_task_start_token_cleanup_completed(
                    connection,
                    workspace_id,
                    task_id,
                    delivered_at,
                )
                self._retire_post_cleanup_lifecycle_receipts(
                    connection,
                    workspace_id,
                    task_id,
                    delivered_at,
                )
                self._retire_terminal_task_lifecycle_receipts(
                    connection,
                    workspace_id,
                    task_id,
                    delivered_at,
                )
                self._prune_delivered_operations(connection)
        operation_result = {
            "operation_id": operation_id,
            "fingerprint": fingerprint,
            "delivery_digest": delivery_digest,
            "replayed": replayed,
            "delivered": True,
            "finalized": True,
        }
        if receipt_retired:
            operation_result["retired"] = True
        return {
            "action": action,
            "acknowledged": True,
            "token_cleanup_expected": cleanup_expected,
            "token_file_removed": token_file_removed,
            "operation": operation_result,
        }

    def preflight_task_start_token(
        self,
        operation_id: str,
        token_file_path: str,
        workspace: Path | str,
        owner: str,
        summary: str,
        ttl_seconds: float,
        *,
        receipt_only: bool = False,
    ) -> bool:
        """Refuse a new token path before the CLI allocates or writes its secret."""

        validated_id = validate_operation_id(operation_id)
        canonical_path = _canonical_token_file_path(token_file_path)
        root = canonical_workspace(workspace)
        if not isinstance(owner, str) or not owner.strip():
            raise UsageError("Task owner cannot be empty.")
        if not isinstance(summary, str) or not summary.strip():
            raise UsageError("Task summary cannot be empty.")
        _validate_ttl(ttl_seconds)
        expected_parameters = {
            "owner": owner.strip(),
            "summary": summary.strip(),
            "token_file_path": canonical_path,
            "ttl_seconds": float(ttl_seconds),
            "workspace": root,
        }
        with self._transaction() as connection:
            receipt = connection.execute(
                "SELECT * FROM operation_receipts WHERE operation_id = ?",
                (validated_id,),
            ).fetchone()
            if receipt is not None:
                try:
                    parameters = parse_canonical_json(receipt["parameters_json"])
                except (TypeError, ValueError, UsageError, json.JSONDecodeError) as exc:
                    raise StateError(
                        "Task-start operation receipt is unreadable.",
                        details={
                            "reason": "operation-receipt-invalid",
                            "operation_id": validated_id,
                        },
                    ) from exc
                if (
                    receipt["action"] != "task.start"
                    or not isinstance(parameters, dict)
                    or parameters != expected_parameters
                ):
                    raise UsageError(
                        "Operation ID is already bound to a different mutation.",
                        details={
                            "reason": "operation-id-conflict",
                            "operation_id": validated_id,
                            "existing_action": receipt["action"],
                        },
                    )
                if not receipt_only:
                    self._maintain(connection, _workspace_id(root))
                    receipt = connection.execute(
                        "SELECT * FROM operation_receipts WHERE operation_id = ?",
                        (validated_id,),
                    ).fetchone()
                    assert receipt is not None
                try:
                    stored_result = parse_canonical_json(receipt["result_json"])
                except (TypeError, ValueError, UsageError, json.JSONDecodeError) as exc:
                    raise StateError(
                        "Task-start operation result is unreadable.",
                        details={
                            "reason": "operation-receipt-invalid",
                            "operation_id": validated_id,
                        },
                    ) from exc
                if not isinstance(stored_result, dict) or receipt["finalized_at"] is None:
                    raise StateError(
                        "Task-start operation receipt is not durably finalized.",
                        details={
                            "reason": "operation-receipt-invalid",
                            "operation_id": validated_id,
                        },
                    )
                task, bound_parameters = self._bound_task_for_start_receipt(
                    connection,
                    receipt,
                    stored_result,
                )
                if receipt["terminal_json"] is not None:
                    self._validate_terminal_start_replay(
                        connection,
                        receipt,
                        stored_result,
                        task,
                        bound_parameters,
                    )
                    return True
                if task is None:
                    raise StateError(
                        "Task-start receipt lost its durable task identity.",
                        details={
                            "reason": "operation-receipt-invalid",
                            "operation_id": validated_id,
                        },
                    )
                if receipt_only and task["state"] == "active":
                    now = time.time()
                    if (
                        not math.isfinite(now)
                        or not math.isfinite(float(task["expires_at"]))
                        or float(task["expires_at"]) <= now
                    ):
                        raise BusyError(
                            "Task-start receipt may have expired but read-only replay cannot "
                            "advance scheduler state.",
                            details={
                                "reason": "task-start-receipt-expired-unmaintained",
                                "operation_id": validated_id,
                                "task_id": receipt["task_id"],
                                "recovery_required": True,
                            },
                        )
                if task["state"] != "active":
                    raise StateError(
                        "Terminal task-start receipt has no lifecycle fence.",
                        details={
                            "reason": "operation-receipt-invalid",
                            "operation_id": validated_id,
                            "task_id": receipt["task_id"],
                            "recovery_required": True,
                        },
                    )
                return True
            self._maintain(connection, _workspace_id(root))
            registered = self._workspace(connection, root)
            if self._unknown_exists(connection, registered["id"]):
                raise BusyError("Workspace is blocked by an unknown task outcome.")
            self._require_token_cleanup_admission(connection)
            cleanup = self._task_token_cleanup_conflict(connection, canonical_path)
            if cleanup is not None:
                raise BusyError(
                    "Task token path is retained for terminal receipt cleanup.",
                    details={
                        "reason": "task-token-cleanup-pending",
                        "operation_id": cleanup["operation_id"],
                        "task_id": cleanup["task_id"],
                    },
                )
            open_task = self._open_task_token_conflict(connection, canonical_path)
            if open_task is not None:
                raise BusyError(
                    "Task token path already belongs to an open task.",
                    details={
                        "reason": "task-token-path-in-use",
                        "task_id": open_task["id"],
                    },
                )
            return False

    def complete_exact_task_start_cleanup(
        self,
        operation_id: str,
        token_file_path: str,
        workspace: Path | str,
        owner: str,
        summary: str,
        ttl_seconds: float,
    ) -> bool:
        """Finish only the receipt-bound expired start cleanup under its path lock."""

        validated_id = validate_operation_id(operation_id)
        canonical_path = _canonical_token_file_path(token_file_path)
        root = canonical_workspace(workspace)
        expected_parameters = canonical_json(
            {
                "owner": owner.strip(),
                "summary": summary.strip(),
                "token_file_path": canonical_path,
                "ttl_seconds": float(ttl_seconds),
                "workspace": root,
            }
        )
        task_id: str | None = None
        with self._transaction() as connection:
            receipt = connection.execute(
                "SELECT * FROM operation_receipts WHERE operation_id = ?",
                (validated_id,),
            ).fetchone()
            if receipt is None:
                return False
            if (
                receipt["action"] != "task.start"
                or receipt["parameters_json"] != expected_parameters
            ):
                raise UsageError(
                    "Operation ID is already bound to a different mutation.",
                    details={
                        "reason": "operation-id-conflict",
                        "operation_id": validated_id,
                        "existing_action": receipt["action"],
                    },
                )
            self._maintain(connection, _workspace_id(root))
            receipt = connection.execute(
                "SELECT * FROM operation_receipts WHERE operation_id = ?",
                (validated_id,),
            ).fetchone()
            assert receipt is not None
            _, stored_result, _ = self._validated_ack_receipt(
                receipt,
                validated_id,
                str(receipt["fingerprint"]),
                receipt_delivery_digest(receipt["result_json"], receipt["terminal_json"]),
            )
            task_id = str(stored_result["id"])
            job = connection.execute(
                "SELECT token_file_path FROM token_cleanup_jobs WHERE task_id = ?",
                (task_id,),
            ).fetchone()
            if job is None:
                return False
            if job["token_file_path"] != canonical_path:
                raise StateError(
                    "Task-start cleanup job is not bound to its canonical token path.",
                    details={
                        "reason": "token-cleanup-job-invalid",
                        "operation_id": validated_id,
                        "task_id": task_id,
                        "recovery_required": True,
                    },
                )
        assert task_id is not None
        completed = self._complete_token_cleanup_job_locked(task_id)
        if completed:
            with self._transaction() as connection:
                self._prune_delivered_operations(connection)
        return completed

    def operation_receipt_exists(self, operation_id: str) -> bool:
        validated_id = validate_operation_id(operation_id)
        connection = open_database(self.paths)
        try:
            return (
                connection.execute(
                    "SELECT 1 FROM operation_receipts WHERE operation_id = ?",
                    (validated_id,),
                ).fetchone()
                is not None
            )
        finally:
            connection.close()

    def task_start_receipt_committed(
        self,
        operation_id: str,
        token_file_path: str,
        workspace: Path | str,
        owner: str,
        summary: str,
        ttl_seconds: float,
        token: str,
    ) -> bool:
        """Return whether this exact task start committed before its caller lost output."""

        root = canonical_workspace(workspace)
        operation = _operation(
            operation_id,
            _workspace_id(root),
            "task.start",
            {
                "owner": owner.strip(),
                "summary": summary.strip(),
                "token_file_path": _canonical_token_file_path(token_file_path),
                "ttl_seconds": float(ttl_seconds),
                "workspace": root,
            },
            _token_hash(token),
        )
        connection = open_database(self.paths)
        try:
            receipt = connection.execute(
                "SELECT * FROM operation_receipts WHERE operation_id = ?",
                (operation.operation_id,),
            ).fetchone()
            if receipt is None:
                return False
            if (
                receipt["action"] != "task.start"
                or receipt["parameters_json"] != operation.parameters_json
            ):
                return False
            if (
                receipt["workspace_id"] != operation.workspace_id
                or receipt["owner_token_hash"] != operation.owner_token_hash
                or receipt["fingerprint"] != operation.fingerprint
                or receipt["finalized_at"] is None
            ):
                raise StateError(
                    "Task-start receipt is present but its commit identity is inconsistent.",
                    details={
                        "reason": "operation-receipt-invalid",
                        "operation_id": operation.operation_id,
                        "recovery_required": True,
                    },
                )
            return True
        finally:
            connection.close()

    def replay_expired_task_start_without_token(
        self,
        operation_id: str,
        token_file_path: str,
        workspace: Path | str,
        owner: str,
        summary: str,
        ttl_seconds: float,
    ) -> dict[str, Any]:
        """Replay one expired claimless start after durable cleanup removed its token."""

        root = canonical_workspace(workspace)
        validated_id = validate_operation_id(operation_id)
        expected_parameters = canonical_json(
            {
                "owner": owner.strip(),
                "summary": summary.strip(),
                "token_file_path": _canonical_token_file_path(token_file_path),
                "ttl_seconds": float(ttl_seconds),
                "workspace": root,
            }
        )
        connection = open_database(self.paths)
        try:
            receipt = connection.execute(
                "SELECT * FROM operation_receipts WHERE operation_id = ?",
                (validated_id,),
            ).fetchone()
            if receipt is None:
                raise StateError(
                    "Operation receipt does not exist.",
                    details={
                        "reason": "operation-receipt-missing",
                        "operation_id": validated_id,
                    },
                )
            if (
                receipt["action"] != "task.start"
                or receipt["parameters_json"] != expected_parameters
            ):
                raise UsageError(
                    "Operation ID is already bound to a different mutation.",
                    details={
                        "reason": "operation-id-conflict",
                        "operation_id": validated_id,
                        "existing_action": receipt["action"],
                    },
                )
            fingerprint = receipt["fingerprint"]
            if not is_sha256_hex(fingerprint):
                raise StateError(
                    "Task-start receipt fingerprint is malformed.",
                    details={"reason": "operation-receipt-invalid", "operation_id": validated_id},
                )
            _, stored_result, _ = self._validated_ack_receipt(
                receipt,
                validated_id,
                fingerprint,
                receipt_delivery_digest(receipt["result_json"], receipt["terminal_json"]),
            )
            terminal_proof = self._operation_terminal_proof(receipt, stored_result)
            if terminal_proof is None or terminal_proof.get("token_cleanup_completed") is not True:
                raise StateError(
                    "Missing task token has no completed lifecycle cleanup proof.",
                    details={
                        "reason": "task-start-token-missing",
                        "operation_id": validated_id,
                        "recovery_required": True,
                    },
                )
            return self._operation_result(receipt, replayed=True)
        finally:
            connection.close()

    def start_task(
        self,
        workspace: Path | str,
        owner: str,
        summary: str,
        *,
        operation_id: str,
        token_file_path: str | None = None,
        receipt_only: bool = False,
        ttl_seconds: float = DEFAULT_TASK_TTL_SECONDS,
        token: str | None = None,
    ) -> tuple[dict[str, Any], str]:
        root = canonical_workspace(workspace)
        if not isinstance(owner, str) or not owner.strip():
            raise UsageError("Task owner cannot be empty.")
        if not isinstance(summary, str) or not summary.strip():
            raise UsageError("Task summary cannot be empty.")
        _validate_ttl(ttl_seconds)
        if token_file_path is None:
            canonical_token_path = None
        elif os.name == "nt":
            canonical_token_path = str(canonical_token_file_path(Path(token_file_path)))
        else:
            canonical_token_path = _canonical_token_file_path(token_file_path)
        secret = token or secrets.token_urlsafe(32)
        secret_hash = _token_hash(secret)
        identifier = _workspace_id(root)
        operation = _operation(
            operation_id,
            identifier,
            "task.start",
            {
                "owner": owner.strip(),
                "summary": summary.strip(),
                "token_file_path": canonical_token_path,
                "ttl_seconds": float(ttl_seconds),
                "workspace": root,
            },
            secret_hash,
        )
        with self._transaction() as connection:
            replay = self._replay_or_missing(
                connection,
                operation,
                receipt_only=receipt_only,
            )
            if replay is not None:
                return replay, secret
            self._maintain(connection, identifier)
            registered = self._workspace(connection, root)
            if self._unknown_exists(connection, registered["id"]):
                raise BusyError("Workspace is blocked by an unknown task outcome.")
            self._require_token_cleanup_admission(connection)
            cleanup = self._task_token_cleanup_conflict(
                connection,
                canonical_token_path,
                secret_hash,
            )
            if cleanup is not None:
                raise BusyError(
                    "Task token is retained for terminal receipt cleanup.",
                    details={
                        "reason": "task-token-cleanup-pending",
                        "operation_id": cleanup["operation_id"],
                        "task_id": cleanup["task_id"],
                    },
                )
            if canonical_token_path is not None:
                open_path = self._open_task_token_conflict(connection, canonical_token_path)
                if open_path is not None:
                    raise BusyError(
                        "Task token path already belongs to an open task.",
                        details={
                            "reason": "task-token-path-in-use",
                            "task_id": open_path["id"],
                        },
                    )
            existing_open_token = connection.execute(
                "SELECT id, workspace_id FROM tasks WHERE token_hash = ? "
                "AND state IN ('active', 'outcome_unknown') LIMIT 1",
                (secret_hash,),
            ).fetchone()
            if existing_open_token is not None:
                raise UsageError(
                    "Task token already identifies an open task.",
                    details={
                        "reason": "open-task-token-conflict",
                        "task_id": existing_open_token["id"],
                        "workspace_id": existing_open_token["workspace_id"],
                    },
                )
            now = time.time()
            if not math.isfinite(now):
                raise StateError(
                    "System clock is invalid; task start cannot continue safely.",
                    details={"reason": "system-clock-invalid"},
                )
            task_id = uuid.uuid4().hex
            connection.execute(
                "INSERT INTO tasks(id, workspace_id, owner, summary, token_hash, "
                "token_file_path, token_file_identity, start_operation_id, state, created_at, "
                "heartbeat_at, expires_at) VALUES(?, ?, ?, ?, ?, ?, ?, ?, 'active', ?, ?, ?)",
                (
                    task_id,
                    registered["id"],
                    owner.strip(),
                    summary.strip(),
                    secret_hash,
                    canonical_token_path,
                    (
                        _token_path_identity(canonical_token_path)
                        if canonical_token_path is not None
                        else None
                    ),
                    operation.operation_id,
                    now,
                    now,
                    now + ttl_seconds,
                ),
            )
            self._touch(connection, registered["id"])
            task = connection.execute("SELECT * FROM tasks WHERE id = ?", (task_id,)).fetchone()
            assert task is not None
            return self._record_operation(
                connection,
                operation,
                self._public_task(task),
            ), secret

    def heartbeat(
        self,
        workspace: Path | str,
        token: str,
        *,
        operation_id: str,
        receipt_only: bool = False,
        ttl_seconds: float | None = None,
        note: str | None = None,
    ) -> dict[str, Any]:
        root = canonical_workspace(workspace)
        if note is not None and not isinstance(note, str):
            raise UsageError("Task heartbeat note must be text.")
        if ttl_seconds is not None:
            _validate_ttl(ttl_seconds)
        identifier = _workspace_id(root)
        operation = _operation(
            operation_id,
            identifier,
            "task.heartbeat",
            {
                "note": note,
                "ttl_seconds": None if ttl_seconds is None else float(ttl_seconds),
                "workspace": root,
            },
            _token_hash(token),
        )
        now = time.time()
        with self._transaction() as connection:
            replay = self._replay_or_missing(
                connection,
                operation,
                receipt_only=receipt_only,
            )
            if replay is not None:
                return replay
            self._maintain(connection, identifier)
            registered = self._workspace(connection, root)
            task = self._authenticate_task(connection, registered["id"], token)
            if ttl_seconds is None:
                ttl_seconds = float(task["expires_at"]) - float(task["heartbeat_at"])
                if (
                    not math.isfinite(ttl_seconds)
                    or ttl_seconds <= 0
                    or ttl_seconds > MAX_TASK_TTL_SECONDS
                ):
                    raise StateError(
                        "Active task has an invalid stored lease duration; recover before retrying.",
                        details={"task_id": task["id"], "recovery_required": True},
                    )
            connection.execute(
                "UPDATE tasks SET heartbeat_at = ?, expires_at = ?, note = COALESCE(?, note) "
                "WHERE id = ?",
                (now, now + ttl_seconds, note, task["id"]),
            )
            self._touch(connection, registered["id"])
            updated = connection.execute(
                "SELECT * FROM tasks WHERE id = ?", (task["id"],)
            ).fetchone()
            assert updated is not None
            result = self._public_task(updated)
            restoration_pending = connection.execute(
                "SELECT claims.id FROM claims "
                "JOIN claim_scopes ON claim_scopes.claim_id = claims.id "
                "WHERE claims.workspace_id = ? AND claims.task_id = ? "
                "AND claims.state IN ('queued', 'parked') "
                "AND claim_scopes.scope_type = 'parked_for' ORDER BY claims.queue_order",
                (registered["id"], task["id"]),
            ).fetchall()
            result["restoration_pending_claim_ids"] = [claim["id"] for claim in restoration_pending]
            if not restoration_pending:
                drain = self._task_drain_request(connection, registered["id"], task["id"])
                if drain is not None:
                    result["drain_requested"] = drain
            return self._record_operation(connection, operation, result)

    def release_task(
        self,
        workspace: Path | str,
        token: str,
        *,
        operation_id: str,
        result: str,
        receipt_only: bool = False,
        note: str | None = None,
        token_cleanup_path: str | None = None,
    ) -> dict[str, Any]:
        root = canonical_workspace(workspace)
        if note is not None and not isinstance(note, str):
            raise UsageError("Task release note must be text.")
        if result not in {"completed", "failed", "outcome-unknown"}:
            raise UsageError("Task result must be completed, failed, or outcome-unknown.")
        if result in {"completed", "failed"}:
            if (
                not isinstance(token_cleanup_path, str)
                or not token_cleanup_path
                or not os.path.isabs(token_cleanup_path)
                or os.path.normpath(token_cleanup_path) != token_cleanup_path
            ):
                raise UsageError(
                    "Completed or failed task release requires a canonical token cleanup path.",
                    details={"reason": "token-cleanup-path-invalid"},
                )
            try:
                canonical_cleanup_path = _canonical_token_file_path(
                    str(canonical_token_file_path(Path(token_cleanup_path)))
                )
            except UsageError as exc:
                raise UsageError(
                    "Completed or failed task release token path cannot be verified.",
                    details={"reason": "token-cleanup-path-invalid"},
                ) from exc
        elif token_cleanup_path is not None:
            raise UsageError(
                "Outcome-unknown task release must preserve its token file.",
                details={"reason": "token-cleanup-path-unexpected"},
            )
        else:
            canonical_cleanup_path = None
        identifier = _workspace_id(root)
        operation = _operation(
            operation_id,
            identifier,
            "task.release",
            {
                "note": note,
                "result": result,
                "token_cleanup_path": canonical_cleanup_path,
                "workspace": root,
            },
            _token_hash(token),
        )
        now = time.time()
        with self._transaction() as connection:
            replay = self._replay_or_missing(
                connection,
                operation,
                receipt_only=receipt_only,
            )
            if replay is not None:
                return replay
            self._maintain(connection, identifier)
            registered = self._workspace(connection, root)
            task = self._authenticate_task(connection, registered["id"], token)
            if canonical_cleanup_path is not None:
                stored_token_path = task["token_file_path"]
                if stored_token_path is not None and (
                    not isinstance(stored_token_path, str)
                    or _token_path_identity(stored_token_path)
                    != _token_path_identity(canonical_cleanup_path)
                ):
                    raise AuthorizationError(
                        "Task token file path does not match its start identity.",
                        details={"reason": "task-token-path-mismatch"},
                    )
                if stored_token_path is None:
                    cleanup_conflict = self._task_token_cleanup_conflict(
                        connection,
                        canonical_cleanup_path,
                        str(task["token_hash"]),
                    )
                    if cleanup_conflict is not None:
                        raise BusyError(
                            "Task token path is retained by another cleanup obligation.",
                            details={
                                "reason": "task-token-cleanup-pending",
                                "task_id": cleanup_conflict["task_id"],
                                "operation_id": cleanup_conflict["operation_id"],
                            },
                        )
                    open_conflict = self._open_task_token_conflict(
                        connection,
                        canonical_cleanup_path,
                        str(task["token_hash"]),
                        exclude_task_id=str(task["id"]),
                    )
                    if open_conflict is not None:
                        raise BusyError(
                            "Task token path or secret belongs to another open task.",
                            details={
                                "reason": "task-token-path-in-use",
                                "task_id": open_conflict["id"],
                            },
                        )
                    connection.execute(
                        "UPDATE tasks SET token_file_path = ?, token_file_identity = ? "
                        "WHERE id = ?",
                        (
                            canonical_cleanup_path,
                            _token_path_identity(canonical_cleanup_path),
                            task["id"],
                        ),
                    )
            capacity_before = self._operation_capacity(connection)["reserved_capacity"]
            transition_time = self._task_transition_time(connection, str(task["id"]), now)
            open_freezes = connection.execute(
                "SELECT id, state FROM claims WHERE task_id = ? AND kind = 'freeze' "
                "AND state IN ('queued', 'active')",
                (task["id"],),
            ).fetchall()
            if result == "outcome-unknown":
                connection.execute(
                    "UPDATE claims SET state = 'cancelled', released_at = ? "
                    "WHERE task_id = ? AND state = 'queued'",
                    (transition_time, task["id"]),
                )
                connection.execute(
                    "DELETE FROM claim_scopes WHERE scope_type = 'parked_for' "
                    "AND claim_id IN (SELECT id FROM claims WHERE task_id = ? "
                    "AND state = 'cancelled')",
                    (task["id"],),
                )
                self._resume_parked_for_freezes(
                    connection,
                    [row["id"] for row in open_freezes if row["state"] == "queued"],
                )
                task_state = "outcome_unknown"
            else:
                connection.execute(
                    "UPDATE claims SET state = 'released', released_at = ? "
                    "WHERE task_id = ? AND state IN ('queued', 'active', 'parked')",
                    (transition_time, task["id"]),
                )
                connection.execute(
                    "DELETE FROM claim_scopes WHERE scope_type = 'parked_for' "
                    "AND claim_id IN (SELECT id FROM claims WHERE task_id = ?)",
                    (task["id"],),
                )
                self._resume_parked_for_freezes(connection, [row["id"] for row in open_freezes])
                task_state = result
            connection.execute(
                "UPDATE tasks SET state = ?, finished_at = ?, result = ?, note = ? WHERE id = ?",
                (task_state, transition_time, result, note, task["id"]),
            )
            self._finalize_task_wait_operations(
                connection,
                str(task["id"]),
                str(registered["id"]),
                transition_time,
                reason=(
                    "task-released-outcome-unknown"
                    if result == "outcome-unknown"
                    else "task-released"
                ),
            )
            self._finalize_task_lifecycle_operations(
                connection,
                str(task["id"]),
                str(registered["id"]),
                transition_time,
                reason=(
                    "task-released-outcome-unknown"
                    if result == "outcome-unknown"
                    else "task-released"
                ),
            )
            self._touch(connection, registered["id"])
            self._schedule_workspace(connection, registered["id"], now)
            updated = connection.execute(
                "SELECT * FROM tasks WHERE id = ?", (task["id"],)
            ).fetchone()
            assert updated is not None
            recorded = self._record_operation(
                connection,
                operation,
                self._public_task(updated),
                token_cleanup_path=canonical_cleanup_path,
                capacity_before=capacity_before,
            )
            self._prune_terminal_tasks(connection, registered["id"])
            self._prune_delivered_operations(connection)
            return recorded

    def acquire_claim(
        self,
        workspace: Path | str,
        token: str,
        *,
        operation_id: str,
        receipt_only: bool = False,
        writes: Sequence[str] = (),
        resources: Sequence[str] = (),
        freeze: bool = False,
        priority: str = "normal",
        wait_seconds: float = 0.0,
        requested_wait_seconds: float | None = None,
        keep_queued: bool = False,
    ) -> dict[str, Any]:
        root = canonical_workspace(workspace)
        normalized_writes = self._normalize_writes(root, writes)
        normalized_resources = self._normalize_resources(resources)
        if freeze and (normalized_writes or normalized_resources):
            raise UsageError("A freeze claim cannot include write or resource scopes.")
        if not freeze and not normalized_writes and not normalized_resources:
            raise UsageError("A claim needs at least one write path or resource.")
        if priority not in {"normal", "urgent"}:
            raise UsageError("Claim priority must be normal or urgent.")
        if not freeze and priority != "normal":
            raise UsageError("Urgent priority is only supported for freeze claims.")
        _validate_wait(wait_seconds, "Claim")
        if requested_wait_seconds is None:
            requested_wait_seconds = wait_seconds
        _validate_wait(requested_wait_seconds, "Requested claim")
        if wait_seconds > requested_wait_seconds:
            raise UsageError(
                "Effective claim wait cannot exceed the caller-requested wait.",
                details={"reason": "effective-wait-exceeds-requested"},
            )
        identifier = _workspace_id(root)
        operation = _operation(
            operation_id,
            identifier,
            "freeze.acquire" if freeze else "claim.acquire",
            {
                "keep_queued": keep_queued,
                "priority": priority,
                "requested_wait_seconds": float(requested_wait_seconds),
                "resources": list(normalized_resources),
                "workspace": root,
                "writes": list(normalized_writes),
            },
            _token_hash(token),
        )
        with self._transaction() as connection:
            replay, pending_receipt = self._wait_operation_state(
                connection,
                operation,
                receipt_only=receipt_only,
            )
            if replay is not None:
                return replay
            if pending_receipt is not None:
                pending_result = json.loads(pending_receipt["result_json"])
                claim_id = str(pending_result["id"])
                receipt_created_at = float(pending_receipt["created_at"])
            else:
                now = time.time()
                self._maintain(connection, identifier)
                registered = self._workspace(connection, root)
                if self._unknown_exists(connection, registered["id"]):
                    raise BusyError("Workspace is blocked by an unknown task outcome.")
                task = self._authenticate_task(connection, registered["id"], token)
                restoration_pending = connection.execute(
                    "SELECT claims.id FROM claims "
                    "JOIN claim_scopes ON claim_scopes.claim_id = claims.id "
                    "WHERE claims.task_id = ? AND claims.state IN ('queued', 'parked') "
                    "AND claim_scopes.scope_type = 'parked_for' ORDER BY claims.queue_order",
                    (task["id"],),
                ).fetchall()
                if restoration_pending:
                    raise BusyError(
                        "Task claims are waiting to be restored after workspace maintenance.",
                        details={
                            "reason": "task-restoration-pending",
                            "claim_ids": [claim["id"] for claim in restoration_pending],
                        },
                    )
                drain = self._task_drain_request(connection, registered["id"], task["id"])
                if drain is not None:
                    raise BusyError(
                        "Workspace freeze is waiting for this task to park its claims.",
                        details={"reason": "freeze-drain-requested", **drain},
                    )
                order = self._allocate_queue_order(connection, registered["id"])
                claim_id = uuid.uuid4().hex
                connection.execute(
                    "INSERT INTO claims(id, workspace_id, task_id, kind, state, queue_order, "
                    "created_at) VALUES(?, ?, ?, ?, 'queued', ?, ?)",
                    (
                        claim_id,
                        registered["id"],
                        task["id"],
                        "freeze" if freeze else "normal",
                        order,
                        now,
                    ),
                )
                connection.executemany(
                    "INSERT INTO claim_scopes(claim_id, scope_type, value) VALUES(?, 'write', ?)",
                    ((claim_id, value) for value in normalized_writes),
                )
                connection.executemany(
                    "INSERT INTO claim_scopes(claim_id, scope_type, value) "
                    "VALUES(?, 'resource', ?)",
                    ((claim_id, value) for value in normalized_resources),
                )
                if priority == "urgent":
                    connection.execute(
                        "INSERT INTO claim_scopes(claim_id, scope_type, value) "
                        "VALUES(?, 'priority', 'urgent')",
                        (claim_id,),
                    )
                self._touch(connection, registered["id"])
                self._schedule_workspace(connection, registered["id"], now)
                claim = connection.execute(
                    "SELECT * FROM claims WHERE id = ?", (claim_id,)
                ).fetchone()
                assert claim is not None
                result = self._public_claim(connection, claim)
                result["granted"] = result["state"] == "active"
                result["timed_out"] = False
                pending = result["state"] == "queued" and requested_wait_seconds > 0
                recorded = self._record_operation(
                    connection,
                    operation,
                    result,
                    finalized=not pending,
                )
                if not pending:
                    return recorded
                created_row = connection.execute(
                    "SELECT created_at FROM operation_receipts WHERE operation_id = ?",
                    (operation.operation_id,),
                ).fetchone()
                assert created_row is not None
                receipt_created_at = float(created_row["created_at"])

        remaining_wait = _remaining_operation_wait(
            receipt_created_at,
            float(requested_wait_seconds),
            wait_seconds,
            "claim",
        )
        deadline = time.monotonic() + remaining_wait
        while time.monotonic() < deadline:
            time.sleep(min(0.1, max(0.0, deadline - time.monotonic())))
            with self._transaction() as connection:
                self._maintain(connection, identifier)
                receipt = self._matching_operation_receipt(connection, operation)
                if receipt is None:
                    raise StateError(
                        "Pending claim receipt disappeared from scheduler state.",
                        details={
                            "reason": "operation-receipt-missing",
                            "operation_id": operation.operation_id,
                        },
                    )
                if receipt["finalized_at"] is not None:
                    return self._operation_result(receipt, replayed=True)
                self._workspace(connection, root)
                claim = connection.execute(
                    "SELECT * FROM claims WHERE id = ? AND workspace_id = ?",
                    (claim_id, identifier),
                ).fetchone()
                if claim is None:
                    raise StateError("Claim disappeared from scheduler state.")
                if claim["state"] != "queued":
                    result = self._public_claim(connection, claim)
                    result["granted"] = result["state"] == "active"
                    result["timed_out"] = False
                    return self._update_operation_result(connection, operation, result)
        with self._transaction() as connection:
            self._maintain(connection, identifier)
            receipt = self._matching_operation_receipt(connection, operation)
            if receipt is None:
                raise StateError(
                    "Pending claim receipt disappeared from scheduler state.",
                    details={
                        "reason": "operation-receipt-missing",
                        "operation_id": operation.operation_id,
                    },
                )
            if receipt["finalized_at"] is not None:
                return self._operation_result(receipt, replayed=True)
            registered = self._workspace(connection, root)
            claim = connection.execute(
                "SELECT * FROM claims WHERE id = ? AND workspace_id = ?",
                (claim_id, identifier),
            ).fetchone()
            if claim is None:
                raise StateError("Claim disappeared from scheduler state.")
            if claim["state"] == "queued" and not keep_queued:
                now = time.time()
                released_at = self._claim_transition_time(claim, now)
                connection.execute(
                    "UPDATE claims SET state = 'cancelled', released_at = ? "
                    "WHERE id = ? AND state = 'queued'",
                    (released_at, claim_id),
                )
                if freeze:
                    self._resume_parked_for_freezes(connection, (claim_id,))
                self._touch(connection, registered["id"])
                self._schedule_workspace(connection, registered["id"], now)
                claim = connection.execute(
                    "SELECT * FROM claims WHERE id = ?", (claim_id,)
                ).fetchone()
                assert claim is not None
            result = self._public_claim(connection, claim)
            result["granted"] = result["state"] == "active"
            result["timed_out"] = not result["granted"]
            return self._update_operation_result(connection, operation, result)

    def park_task(
        self,
        workspace: Path | str,
        token: str,
        *,
        operation_id: str,
        receipt_only: bool = False,
        wait_seconds: float = 0.0,
        requested_wait_seconds: float | None = None,
    ) -> dict[str, Any]:
        root = canonical_workspace(workspace)
        _validate_wait(wait_seconds, "Task park")
        if requested_wait_seconds is None:
            requested_wait_seconds = wait_seconds
        _validate_wait(requested_wait_seconds, "Requested task park")
        if wait_seconds > requested_wait_seconds:
            raise UsageError(
                "Effective task park wait cannot exceed the caller-requested wait.",
                details={"reason": "effective-wait-exceeds-requested"},
            )
        identifier = _workspace_id(root)
        operation = _operation(
            operation_id,
            identifier,
            "task.park",
            {
                "requested_wait_seconds": float(requested_wait_seconds),
                "workspace": root,
            },
            _token_hash(token),
        )
        with self._transaction() as connection:
            replay, pending_receipt = self._wait_operation_state(
                connection,
                operation,
                receipt_only=receipt_only,
            )
            if replay is not None:
                return replay
            if pending_receipt is not None:
                initial_result = json.loads(pending_receipt["result_json"])
                task_id = str(initial_result["task_id"])
                freeze_id = str(initial_result["freeze_id"])
                claim_ids = [str(value) for value in initial_result["claim_ids"]]
                receipt_created_at = float(pending_receipt["created_at"])
            else:
                self._maintain(connection, identifier)
                registered = self._workspace(connection, root)
                task = self._authenticate_task(connection, registered["id"], token)
                task_id = str(task["id"])
                existing_parked = connection.execute(
                    "SELECT * FROM claims WHERE workspace_id = ? AND task_id = ? "
                    "AND state = 'parked' ORDER BY queue_order",
                    (registered["id"], task_id),
                ).fetchall()
                if existing_parked:
                    parked_for = {
                        self._claim_scopes(connection, claim["id"])["parked_for"]
                        for claim in existing_parked
                    }
                    if len(parked_for) != 1 or len(next(iter(parked_for))) != 1:
                        raise StateError("Parked claims have inconsistent freeze ownership.")
                    freeze_id = next(iter(next(iter(parked_for))))
                    target_freeze = connection.execute(
                        "SELECT * FROM claims WHERE id = ? AND workspace_id = ? "
                        "AND kind = 'freeze' AND state IN ('queued', 'active')",
                        (freeze_id, registered["id"]),
                    ).fetchone()
                    if target_freeze is None:
                        raise StateError("Parked claims reference a closed freeze.")
                    parked_claims = existing_parked
                else:
                    drain = self._task_drain_request(connection, registered["id"], task_id)
                    if drain is None:
                        raise StateError(
                            "No queued freeze is requesting this task to drain.",
                            details={"reason": "freeze-drain-not-requested"},
                        )
                    target_freeze = connection.execute(
                        "SELECT * FROM claims WHERE id = ?",
                        (drain["freeze_id"],),
                    ).fetchone()
                    assert target_freeze is not None
                    owned = connection.execute(
                        "SELECT * FROM claims WHERE workspace_id = ? AND task_id = ? "
                        "AND state IN ('queued', 'active') ORDER BY queue_order",
                        (registered["id"], task_id),
                    ).fetchall()
                    if not owned:
                        raise StateError(
                            "Task has no open claims to park.",
                            details={"reason": "task-claimless"},
                        )
                    target_scopes = self._claim_scopes(connection, target_freeze["id"])
                    target_key = self._claim_sort_key(target_freeze, target_scopes)
                    parked_claims = [
                        claim
                        for claim in owned
                        if claim["state"] == "active"
                        or self._claim_sort_key(
                            claim,
                            self._claim_scopes(connection, claim["id"]),
                        )
                        < target_key
                    ]
                    unsafe: list[dict[str, Any]] = []
                    for claim in parked_claims:
                        scopes = self._claim_scopes(connection, claim["id"])
                        if claim["kind"] == "freeze" or scopes["resource"]:
                            unsafe.append(
                                {
                                    "claim_id": claim["id"],
                                    "kind": claim["kind"],
                                    "resources": list(scopes["resource"]),
                                }
                            )
                    if unsafe:
                        raise BusyError(
                            "Task still owns claims that cannot be parked safely.",
                            details={"reason": "task-holds-unsafe-claims", "claims": unsafe},
                        )
                    freeze_id = str(target_freeze["id"])
                    claim_ids = [str(claim["id"]) for claim in parked_claims]
                    placeholders = ", ".join("?" for _ in claim_ids)
                    connection.execute(
                        f"UPDATE claims SET state = 'parked' WHERE id IN ({placeholders})",
                        claim_ids,
                    )
                    connection.executemany(
                        "INSERT INTO claim_scopes(claim_id, scope_type, value) "
                        "VALUES(?, 'parked_for', ?)",
                        ((claim_id, freeze_id) for claim_id in claim_ids),
                    )
                    self._touch(connection, registered["id"])
                    self._schedule_workspace(connection, registered["id"], time.time())
                claim_ids = [str(claim["id"]) for claim in parked_claims]
                placeholders = ", ".join("?" for _ in claim_ids)
                claims = connection.execute(
                    f"SELECT * FROM claims WHERE id IN ({placeholders}) ORDER BY queue_order",
                    claim_ids,
                ).fetchall()
                states = {claim["id"]: claim["state"] for claim in claims}
                resumed = len(claims) == len(claim_ids) and all(
                    state == "active" for state in states.values()
                )
                initial_result = {
                    "task_id": task_id,
                    "freeze_id": freeze_id,
                    "claim_ids": claim_ids,
                    "states": states,
                    "parked": not resumed,
                    "resumed": resumed,
                    "timed_out": False,
                }
                pending = not resumed and requested_wait_seconds > 0
                recorded = self._record_operation(
                    connection,
                    operation,
                    initial_result,
                    finalized=not pending,
                )
                if not pending:
                    return recorded
                created_row = connection.execute(
                    "SELECT created_at FROM operation_receipts WHERE operation_id = ?",
                    (operation.operation_id,),
                ).fetchone()
                assert created_row is not None
                receipt_created_at = float(created_row["created_at"])

        def result_payload(*, timed_out: bool, persist: bool) -> dict[str, Any]:
            with self._transaction() as connection:
                self._maintain(connection, identifier)
                receipt = self._matching_operation_receipt(connection, operation)
                if receipt is None:
                    raise StateError(
                        "Pending park receipt disappeared from scheduler state.",
                        details={
                            "reason": "operation-receipt-missing",
                            "operation_id": operation.operation_id,
                        },
                    )
                if receipt["finalized_at"] is not None:
                    return self._operation_result(receipt, replayed=True)
                self._workspace(connection, root)
                placeholders = ", ".join("?" for _ in claim_ids)
                claims = connection.execute(
                    f"SELECT * FROM claims WHERE id IN ({placeholders}) ORDER BY queue_order",
                    claim_ids,
                ).fetchall()
                if len(claims) != len(claim_ids):
                    raise StateError("Parked claims disappeared from scheduler state.")
                states = {claim["id"]: claim["state"] for claim in claims}
                resumed = all(state == "active" for state in states.values())
                result = {
                    "task_id": task_id,
                    "freeze_id": freeze_id,
                    "claim_ids": claim_ids,
                    "states": states,
                    "parked": not resumed,
                    "resumed": resumed,
                    "timed_out": timed_out and not resumed,
                }
                if persist:
                    return self._update_operation_result(connection, operation, result)
                return result

        remaining_wait = _remaining_operation_wait(
            receipt_created_at,
            float(requested_wait_seconds),
            wait_seconds,
            "task park",
        )
        deadline = time.monotonic() + remaining_wait
        while time.monotonic() < deadline:
            time.sleep(min(0.1, max(0.0, deadline - time.monotonic())))
            result = result_payload(timed_out=False, persist=False)
            if result["resumed"]:
                return result_payload(timed_out=False, persist=True)
        return result_payload(timed_out=True, persist=True)

    def release_claim(
        self,
        workspace: Path | str,
        token: str,
        claim_id: str,
        *,
        operation_id: str,
        receipt_only: bool = False,
    ) -> dict[str, Any]:
        root = canonical_workspace(workspace)
        identifier = _workspace_id(root)
        operation = _operation(
            operation_id,
            identifier,
            "claim.release",
            {"claim_id": claim_id, "workspace": root},
            _token_hash(token),
        )
        now = time.time()
        with self._transaction() as connection:
            replay = self._replay_or_missing(
                connection,
                operation,
                receipt_only=receipt_only,
            )
            if replay is not None:
                return replay
            self._maintain(connection, identifier)
            registered = self._workspace(connection, root)
            task = self._authenticate_task(connection, registered["id"], token)
            claim = connection.execute(
                "SELECT * FROM claims WHERE id = ? AND workspace_id = ?",
                (claim_id, registered["id"]),
            ).fetchone()
            if claim is None or claim["task_id"] != task["id"]:
                raise AuthorizationError("Claim is not owned by this task.")
            if claim["state"] not in OPEN_CLAIM_STATES:
                raise StateError(f"Claim is already {claim['state']}.")
            capacity_before = self._operation_capacity(connection)["reserved_capacity"]
            transition_time = self._claim_transition_time(claim, now)
            next_state = "cancelled" if claim["state"] == "queued" else "released"
            connection.execute(
                "UPDATE claims SET state = ?, released_at = ? WHERE id = ?",
                (next_state, transition_time, claim_id),
            )
            connection.execute(
                "DELETE FROM claim_scopes WHERE claim_id = ? AND scope_type = 'parked_for'",
                (claim_id,),
            )
            if claim["kind"] == "freeze":
                self._resume_parked_for_freezes(connection, (claim_id,))
            self._touch(connection, registered["id"])
            self._schedule_workspace(connection, registered["id"], now)
            updated = connection.execute(
                "SELECT * FROM claims WHERE id = ?", (claim_id,)
            ).fetchone()
            assert updated is not None
            return self._record_operation(
                connection,
                operation,
                self._public_claim(connection, updated),
                capacity_before=capacity_before,
            )

    def cancel_claim(
        self,
        workspace: Path | str,
        token: str,
        claim_id: str,
        *,
        operation_id: str,
        receipt_only: bool = False,
    ) -> dict[str, Any]:
        root = canonical_workspace(workspace)
        identifier = _workspace_id(root)
        operation = _operation(
            operation_id,
            identifier,
            "queue.cancel",
            {"claim_id": claim_id, "workspace": root},
            _token_hash(token),
        )
        now = time.time()
        with self._transaction() as connection:
            replay = self._replay_or_missing(
                connection,
                operation,
                receipt_only=receipt_only,
            )
            if replay is not None:
                return replay
            self._maintain(connection, identifier)
            registered = self._workspace(connection, root)
            task = self._authenticate_task(connection, registered["id"], token)
            claim = connection.execute(
                "SELECT * FROM claims WHERE id = ? AND workspace_id = ?",
                (claim_id, registered["id"]),
            ).fetchone()
            if claim is None or claim["task_id"] != task["id"]:
                raise AuthorizationError("Claim is not owned by this task.")
            if claim["state"] != "queued":
                raise StateError(
                    "Only an exact queued claim can be cancelled.",
                    details={"claim_id": claim_id, "state": claim["state"]},
                )
            capacity_before = self._operation_capacity(connection)["reserved_capacity"]
            transition_time = self._claim_transition_time(claim, now)
            connection.execute(
                "UPDATE claims SET state = 'cancelled', released_at = ? "
                "WHERE id = ? AND state = 'queued'",
                (transition_time, claim_id),
            )
            connection.execute(
                "DELETE FROM claim_scopes WHERE claim_id = ? AND scope_type = 'parked_for'",
                (claim_id,),
            )
            if claim["kind"] == "freeze":
                self._resume_parked_for_freezes(connection, (claim_id,))
            self._touch(connection, registered["id"])
            self._schedule_workspace(connection, registered["id"], now)
            updated = connection.execute(
                "SELECT * FROM claims WHERE id = ?", (claim_id,)
            ).fetchone()
            assert updated is not None
            return self._record_operation(
                connection,
                operation,
                self._public_claim(connection, updated),
                capacity_before=capacity_before,
            )

    def assert_claims(
        self,
        workspace: Path | str,
        token: str,
        *,
        writes: Sequence[str] = (),
        resources: Sequence[str] = (),
        freeze: bool = False,
    ) -> dict[str, Any]:
        root = canonical_workspace(workspace)
        normalized_writes = self._normalize_writes(root, writes)
        normalized_resources = self._normalize_resources(resources)
        with self._transaction() as connection:
            self._maintain(connection, _workspace_id(root))
            registered = self._workspace(connection, root)
            task = self._authenticate_task(connection, registered["id"], token)
            restoration_pending = connection.execute(
                "SELECT claims.id FROM claims "
                "JOIN claim_scopes ON claim_scopes.claim_id = claims.id "
                "WHERE claims.task_id = ? AND claims.state IN ('queued', 'parked') "
                "AND claim_scopes.scope_type = 'parked_for' ORDER BY claims.queue_order",
                (task["id"],),
            ).fetchall()
            if restoration_pending:
                raise BusyError(
                    "Task claims are waiting to be restored after workspace maintenance.",
                    details={
                        "reason": "task-restoration-pending",
                        "claim_ids": [claim["id"] for claim in restoration_pending],
                    },
                )
            drain = self._task_drain_request(connection, registered["id"], task["id"])
            if drain is not None:
                raise BusyError(
                    "Workspace freeze is waiting for this task to park its claims.",
                    details={"reason": "freeze-drain-requested", **drain},
                )
            claims = connection.execute(
                "SELECT * FROM claims WHERE workspace_id = ? AND task_id = ? AND state = 'active'",
                (registered["id"], task["id"]),
            ).fetchall()
            scopes = [self._claim_scopes(connection, claim["id"]) for claim in claims]
            claimed_writes = [value for scope in scopes for value in scope["write"]]
            claimed_resources = {value for scope in scopes for value in scope["resource"]}
            missing_writes = [
                requested
                for requested in normalized_writes
                if not any(
                    claimed == "." or requested == claimed or requested.startswith(claimed + "/")
                    for claimed in claimed_writes
                )
            ]
            missing_resources = [
                requested
                for requested in normalized_resources
                if requested not in claimed_resources
            ]
            has_freeze = any(claim["kind"] == "freeze" for claim in claims)
            if missing_writes or missing_resources or (freeze and not has_freeze):
                raise ClaimAuthorizationError(
                    "Task does not own all requested claims.",
                    details={
                        "missing_writes": missing_writes,
                        "missing_resources": missing_resources,
                        "missing_freeze": freeze and not has_freeze,
                    },
                )
            return {
                "task_id": task["id"],
                "authorized": True,
                "writes": list(normalized_writes),
                "resources": list(normalized_resources),
                "freeze": freeze,
            }

    def resolve_unknown(
        self,
        workspace: Path | str,
        task_id: str,
        *,
        operation_id: str,
        resolution: str,
        evidence: str,
        receipt_only: bool = False,
    ) -> dict[str, Any]:
        root = canonical_workspace(workspace)
        if resolution not in {"completed", "failed"}:
            raise UsageError("Recovery resolution must be completed or failed.")
        normalized_evidence = evidence.strip() if isinstance(evidence, str) else None
        if not _is_normalized_recovery_evidence(normalized_evidence):
            raise UsageError("Recovery evidence must be non-empty normalized text.")
        identifier = _workspace_id(root)
        operation = _operation(
            operation_id,
            identifier,
            "recovery.resolve",
            {
                "evidence": normalized_evidence,
                "resolution": resolution,
                "task_id": task_id,
                "workspace": root,
            },
            None,
        )
        now = time.time()
        with self._transaction() as connection:
            replay = self._replay_or_missing(
                connection,
                operation,
                receipt_only=receipt_only,
            )
            if replay is not None:
                return replay
            self._maintain(connection, identifier)
            registered = self._workspace(connection, root)
            task = connection.execute(
                "SELECT * FROM tasks WHERE id = ? AND workspace_id = ?",
                (task_id, registered["id"]),
            ).fetchone()
            if task is None or task["state"] != "outcome_unknown":
                raise StateError("Task is not waiting for unknown-outcome recovery.")
            capacity_before = self._operation_capacity(connection)["reserved_capacity"]
            transition_time = self._task_transition_time(connection, task_id, now)
            open_freezes = connection.execute(
                "SELECT id FROM claims WHERE task_id = ? AND kind = 'freeze' "
                "AND state IN ('queued', 'active')",
                (task_id,),
            ).fetchall()
            connection.execute(
                "UPDATE claims SET state = 'released', released_at = ? "
                "WHERE task_id = ? AND state IN ('queued', 'active', 'parked')",
                (transition_time, task_id),
            )
            connection.execute(
                "DELETE FROM claim_scopes WHERE scope_type = 'parked_for' "
                "AND claim_id IN (SELECT id FROM claims WHERE task_id = ?)",
                (task_id,),
            )
            self._resume_parked_for_freezes(connection, [row["id"] for row in open_freezes])
            connection.execute(
                "UPDATE tasks SET state = ?, result = ?, finished_at = ?, note = ? WHERE id = ?",
                (
                    resolution,
                    f"recovered-{resolution}",
                    transition_time,
                    normalized_evidence,
                    task_id,
                ),
            )
            if task["token_file_path"] is not None:
                connection.execute(
                    "INSERT INTO token_cleanup_jobs("
                    "task_id, workspace_id, token_file_path, token_file_identity, "
                    "token_hash, reason, "
                    "created_at, completed_at) "
                    "VALUES(?, ?, ?, ?, ?, 'recovered-task-terminal', ?, NULL)",
                    (
                        task_id,
                        registered["id"],
                        task["token_file_path"],
                        _token_path_identity(task["token_file_path"]),
                        task["token_hash"],
                        transition_time,
                    ),
                )
            self._finalize_task_wait_operations(
                connection,
                str(task_id),
                str(registered["id"]),
                transition_time,
                reason="task-recovery-resolved",
            )
            self._finalize_task_lifecycle_operations(
                connection,
                str(task_id),
                str(registered["id"]),
                transition_time,
                reason="task-recovery-resolved",
            )
            connection.execute(
                "INSERT INTO recovery_events(id, workspace_id, task_id, resolution, evidence, created_at) "
                "VALUES(?, ?, ?, ?, ?, ?)",
                (
                    uuid.uuid4().hex,
                    registered["id"],
                    task_id,
                    resolution,
                    normalized_evidence,
                    transition_time,
                ),
            )
            self._touch(connection, registered["id"])
            self._schedule_workspace(connection, registered["id"], now)
            self._prune_delivered_operations(connection)
            updated = connection.execute("SELECT * FROM tasks WHERE id = ?", (task_id,)).fetchone()
            assert updated is not None
            recorded = self._record_operation(
                connection,
                operation,
                self._public_task(updated),
                capacity_before=capacity_before,
            )
            self._prune_terminal_tasks(connection, registered["id"])
            return recorded
