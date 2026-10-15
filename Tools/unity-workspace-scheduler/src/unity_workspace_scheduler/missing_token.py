"""Same-user operator recovery evidence; no executor or automatic release logic."""

from __future__ import annotations

import hashlib
import json
import math
import os
import time
from pathlib import Path
from typing import Any

from .errors import UsageError
from .operations import is_sha256_hex, validate_operation_id

ACTION = "recovery.fence-missing-token"
KINDS = {"clients", "native", "writers"}


def _path(value: Any) -> Path:
    if not isinstance(value, str) or not os.path.isabs(value):
        raise UsageError("Recovery paths must be absolute strings.")
    path = Path(value)
    if os.path.normpath(value) != value or path.resolve() != path or path.is_symlink():
        raise UsageError("Recovery paths must be canonical and without links.")
    return path


def _inside(root: Path, path: Path) -> bool:
    try:
        path.relative_to(root)
        return True
    except ValueError:
        return False


def _read_record(record: Any, *, read_files: bool) -> bytes | None:
    if (
        not isinstance(record, dict)
        or set(record) != {"path", "sha256"}
        or not is_sha256_hex(record["sha256"])
    ):
        raise UsageError("Recovery evidence requires a path and lowercase SHA256.")
    path = _path(record["path"])
    if not read_files:
        return None
    try:
        if not path.is_file() or path.stat().st_size > 16 * 1024 * 1024:
            raise UsageError("Recovery evidence is not a bounded regular file.")
        data = path.read_bytes()
    except OSError as exc:
        raise UsageError("Required recovery evidence is unavailable.") from exc
    if hashlib.sha256(data).hexdigest() != record["sha256"]:
        raise UsageError("Recovery evidence changed; preserve custody.")
    return data


def _terminal_proof(
    value: dict[str, Any], record: dict[str, Any], *, not_before: float | None
) -> None:
    data = _read_record({k: record[k] for k in ("path", "sha256")}, read_files=True)
    try:
        proof = json.loads(data)
    except (ValueError, UnicodeError) as exc:
        raise UsageError("Terminal evidence must be a structured report.") from exc
    common = {
        "version",
        "workspace",
        "task_id",
        "start_operation_id",
        "kind",
        "terminal",
        "observed_at",
        "references",
    }
    fields = {
        "clients": {"clients"},
        "writers": {"stopped", "detached_writers", "writes_paused", "coordinator_calls_paused"},
        "native": {"state", "executor_id", "artifact", "starter"},
    }
    kind = record["kind"]
    if (
        not isinstance(proof, dict)
        or set(proof) != common | fields[kind]
        or proof["version"] != 1
        or proof["terminal"] is not True
        or proof["kind"] != kind
        or any(proof[k] != value[k] for k in ("workspace", "task_id", "start_operation_id"))
    ):
        raise UsageError("Terminal evidence does not bind the original execution.")
    observed = proof["observed_at"]
    if (
        type(observed) not in (int, float)
        or not math.isfinite(observed)
        or observed > time.time() + 30
        or observed < (not_before or 0)
    ):
        raise UsageError("Terminal evidence has an invalid observation boundary.")
    refs = proof["references"]
    if not isinstance(refs, list) or not refs:
        raise UsageError("Terminal evidence needs its preserved source records.")
    for reference in refs:
        _read_record(reference, read_files=True)
    if kind == "clients":
        clients = proof["clients"]
        if (
            not isinstance(clients, list)
            or not clients
            or any(
                not isinstance(x, dict)
                or set(x) != {"id", "exit_code"}
                or not isinstance(x["id"], str)
                or not x["id"]
                or type(x["exit_code"]) is not int
                for x in clients
            )
            or len({x["id"] for x in clients}) != len(clients)
        ):
            raise UsageError("Every original client requires a known terminal exit code.")
    elif kind == "writers":
        if (
            proof["stopped"] is not True
            or proof["detached_writers"] is not False
            or proof["writes_paused"] is not True
            or proof["coordinator_calls_paused"] is not True
        ):
            raise UsageError("Original source writers or coordinator clients are not stopped.")
    else:
        if (
            proof["state"] not in {"completed", "cancelled"}
            or not isinstance(proof["executor_id"], str)
            or not proof["executor_id"]
        ):
            raise UsageError("Original native execution is not terminal.")
        _read_record(proof["artifact"], read_files=True)
        _read_record(proof["starter"], read_files=True)


def validate_attestation(
    value: Any, *, read_files: bool = False, not_before: float | None = None
) -> None:
    keys = {
        "version",
        "workspace",
        "task_id",
        "owner",
        "start_operation_id",
        "token_path",
        "claim_ids",
        "terminal_evidence",
        "preserved_sources",
        "absent_sources",
        "unavailable_evidence",
        "operator_note",
    }
    if not isinstance(value, dict) or set(value) != keys or value["version"] != 1:
        raise UsageError("Missing-token evidence has an invalid schema.")
    for name in ("workspace", "task_id", "owner", "token_path", "operator_note"):
        if not isinstance(value[name], str) or not value[name].strip():
            raise UsageError("Recovery requires exact identity and an operator note.")
    validate_operation_id(value["start_operation_id"])
    root = _path(value["workspace"])
    _path(value["token_path"])
    claims = value["claim_ids"]
    if (
        not isinstance(claims, list)
        or not claims
        or any(not isinstance(x, str) or not x for x in claims)
        or claims != sorted(set(claims))
    ):
        raise UsageError("Recovery must enumerate every retained claim.")
    gaps = value["unavailable_evidence"]
    if not isinstance(gaps, list) or any(not isinstance(x, str) or not x.strip() for x in gaps):
        raise UsageError("Unavailable original evidence must be declared explicitly.")
    terminal = value["terminal_evidence"]
    if (
        not isinstance(terminal, list)
        or len(terminal) != 3
        or any(
            not isinstance(x, dict)
            or set(x) != {"kind", "state", "path", "sha256"}
            or not isinstance(x["kind"], str)
            or x["state"] != "terminal"
            for x in terminal
        )
        or {x["kind"] for x in terminal} != KINDS
    ):
        raise UsageError("Clients, native work and source writers each need terminal evidence.")
    for record in terminal:
        _read_record({k: record[k] for k in ("path", "sha256")}, read_files=False)
        if read_files:
            _terminal_proof(value, record, not_before=not_before)
    sources = value["preserved_sources"]
    absent = value["absent_sources"]
    if not isinstance(sources, list) or not isinstance(absent, list):
        raise UsageError("Source preservation evidence is malformed.")
    seen: set[str] = set()
    for source in sources:
        if not isinstance(source, dict) or set(source) != {"path", "backup", "sha256"}:
            raise UsageError("Source preservation evidence is malformed.")
        path, backup = _path(source["path"]), _path(source["backup"])
        identity = os.path.normcase(str(path))
        if identity in seen or path == root or not _inside(root, path) or _inside(root, backup):
            raise UsageError("Preservation must bind unique workspace leaves and external backups.")
        seen.add(identity)
        if read_files and (not path.is_file() or not backup.is_file()):
            raise UsageError("Only exact regular source files are supported.")
        for item in (path, backup):
            _read_record({"path": str(item), "sha256": source["sha256"]}, read_files=read_files)
    for name in absent:
        path = _path(name)
        identity = os.path.normcase(str(path))
        if identity in seen or path == root or not _inside(root, path):
            raise UsageError("Absent owned sources must be unique exact workspace paths.")
        seen.add(identity)
        if read_files and path.exists():
            raise UsageError("A declared absent source now exists; preserve custody.")
