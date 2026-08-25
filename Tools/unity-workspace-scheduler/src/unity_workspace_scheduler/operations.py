"""Canonical identity and serialization for durable public mutations."""

from __future__ import annotations

import hashlib
import json
import uuid
from typing import Any

from .errors import UsageError

PUBLIC_MUTATION_ACTIONS = frozenset(
    {
        "workspace.register",
        "workspace.unregister",
        "task.start",
        "task.heartbeat",
        "task.park",
        "task.release",
        "claim.acquire",
        "claim.release",
        "queue.cancel",
        "freeze.acquire",
        "recovery.resolve",
    }
)

# Lifecycle proof contracts are shared by receipt replay and offline state
# verification.  Keep the action/state matrix in one place so a new proof
# action cannot silently drift between the two validators.
LIFECYCLE_TERMINAL_ACTIONS = frozenset(
    {"task.heartbeat", "claim.acquire", "freeze.acquire", "task.park"}
)
LIFECYCLE_REVOCATION_ACTIONS = frozenset({"claim.release", "queue.cancel"})
LIFECYCLE_ACTIONS = LIFECYCLE_TERMINAL_ACTIONS | LIFECYCLE_REVOCATION_ACTIONS | {"task.release"}
CLAIM_RELEASE_STATES = frozenset({"released", "cancelled"})
QUEUE_CANCEL_STATES = frozenset({"cancelled"})


def validate_operation_id(value: object) -> str:
    if not isinstance(value, str):
        raise UsageError(
            "Operation ID must be a canonical lowercase UUIDv4.",
            details={"reason": "operation-id-invalid"},
        )
    try:
        parsed = uuid.UUID(value)
    except (AttributeError, ValueError) as exc:
        raise UsageError(
            "Operation ID must be a canonical lowercase UUIDv4.",
            details={"reason": "operation-id-invalid"},
        ) from exc
    if parsed.version != 4 or str(parsed) != value:
        raise UsageError(
            "Operation ID must be a canonical lowercase UUIDv4.",
            details={"reason": "operation-id-invalid"},
        )
    return value


def canonical_json(value: object) -> str:
    try:
        return json.dumps(
            value,
            ensure_ascii=True,
            sort_keys=True,
            separators=(",", ":"),
            allow_nan=False,
        )
    except (TypeError, ValueError) as exc:
        raise UsageError(
            "Operation parameters must be finite canonical JSON values.",
            details={"reason": "operation-parameters-invalid"},
        ) from exc


def parse_canonical_json(value: object, *, require_object: bool = True) -> dict[str, Any]:
    if not isinstance(value, str):
        raise TypeError("canonical JSON must use TEXT storage")
    parsed = json.loads(value)
    if require_object and not isinstance(parsed, dict):
        raise ValueError("canonical JSON must contain an object")
    if canonical_json(parsed) != value:
        raise ValueError("JSON is not in canonical form")
    return parsed


def operation_fingerprint(
    workspace_id: str,
    action: str,
    parameters_json: str,
    owner_token_hash: str | None,
) -> str:
    if action not in PUBLIC_MUTATION_ACTIONS:
        raise UsageError(
            "Operation action is not a public Scheduler mutation.",
            details={"reason": "operation-action-invalid", "action": action},
        )
    payload = canonical_json(
        {
            "action": action,
            "owner_token_hash": owner_token_hash,
            "parameters": parse_canonical_json(parameters_json),
            "workspace_id": workspace_id,
        }
    )
    return hashlib.sha256(payload.encode("utf-8")).hexdigest()


def receipt_delivery_digest(result_json: str, terminal_json: str | None) -> str:
    """Bind acknowledgement to the exact durable result/proof version delivered."""

    payload = canonical_json(
        {
            "result": parse_canonical_json(result_json),
            "terminal": (
                parse_canonical_json(terminal_json) if terminal_json is not None else None
            ),
        }
    )
    return hashlib.sha256(payload.encode("utf-8")).hexdigest()


def is_sha256_hex(value: object) -> bool:
    return (
        isinstance(value, str)
        and len(value) == 64
        and value.casefold() == value
        and all(character in "0123456789abcdef" for character in value)
    )
