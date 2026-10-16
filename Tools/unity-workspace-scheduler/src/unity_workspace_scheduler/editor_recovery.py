"""Receipt contract for releasing one proven-terminal Editor resource fence."""

import hashlib
import math
import re

from .state import _is_normalized_recovery_evidence

ACTION = "recovery.release-editor"
RESULT_KEYS = frozenset(
    {
        "task_id",
        "claim_id",
        "state",
        "released_at",
        "retained_claim_ids",
        "task_state",
        "source_claims_released",
        "evidence_sha256",
    }
)


def entity(value):
    return isinstance(value, str) and re.fullmatch(r"[0-9a-f]{32}", value) is not None


def validate_parameters(parameters):
    if (
        set(parameters) != {"workspace", "task_id", "claim_id", "evidence", "operator_confirmed"}
        or not entity(parameters["task_id"])
        or not entity(parameters["claim_id"])
        or parameters["operator_confirmed"] is not True
        or not _is_normalized_recovery_evidence(parameters["evidence"])
    ):
        raise ValueError(
            "Editor recovery requires exact task/claim and confirmed terminal evidence"
        )


def validate_result(parameters, result):
    validate_parameters(parameters)
    retained = result.get("retained_claim_ids")
    timestamp = result.get("released_at")
    if (
        set(result) != RESULT_KEYS
        or result.get("task_id") != parameters["task_id"]
        or result.get("claim_id") != parameters["claim_id"]
        or result.get("state") != "released"
        or result.get("task_state") != "outcome_unknown"
        or result.get("source_claims_released") is not False
        or not isinstance(retained, list)
        or not retained
        or not all(entity(v) for v in retained)
        or retained != sorted(set(retained))
        or parameters["claim_id"] in retained
        or isinstance(timestamp, bool)
        or not isinstance(timestamp, (int, float))
        or not math.isfinite(timestamp)
        or timestamp <= 0
        or result.get("evidence_sha256")
        != hashlib.sha256(parameters["evidence"].encode()).hexdigest()
    ):
        raise ValueError("Editor recovery receipt lost its exact release/preservation binding")
