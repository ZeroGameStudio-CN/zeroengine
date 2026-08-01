from __future__ import annotations

import hashlib
import json
import os
import time
from collections.abc import Iterator
from contextlib import contextmanager
from pathlib import Path

from filelock import FileLock, Timeout

from .errors import ProjectBusyError, ServiceError
from .service_state import StatePaths, _atomic_write, _unlink_with_retry, process_alive


def project_lock_key(canonical_project_root: str) -> str:
    return hashlib.sha256(canonical_project_root.encode("utf-8")).hexdigest()[:24]


def live_operation_owners(paths: StatePaths) -> list[dict]:
    owners: list[dict] = []
    if not paths.locks.exists():
        return owners

    for metadata_path in paths.locks.glob("*.json"):
        lock = FileLock(metadata_path.with_suffix(".lock"))
        try:
            lock.acquire(timeout=0)
        except Timeout:
            pass
        else:
            lock.release()
            continue

        try:
            value = json.loads(metadata_path.read_text(encoding="utf-8"))
            pid = int(value.get("pid", 0))
        except (OSError, TypeError, ValueError):
            value = {}
            pid = 0
        owners.append(
            {
                "pid": pid if process_alive(pid) else None,
                "started_at": value.get("started_at"),
                "command_type": value.get("command_type", "unknown"),
            }
        )
    return owners


def _atomic_metadata(path: Path, value: dict) -> None:
    _atomic_write(path, json.dumps(value, sort_keys=True) + "\n")


@contextmanager
def project_lock(
    paths: StatePaths,
    canonical_project_root: str,
    command_type: str,
    timeout_seconds: float,
) -> Iterator[None]:
    paths.ensure()
    key = project_lock_key(canonical_project_root)
    lock_path = paths.locks / f"{key}.lock"
    metadata_path = paths.locks / f"{key}.json"
    lock = FileLock(lock_path)
    metadata_written = False
    try:
        lock.acquire(timeout=timeout_seconds)
    except Timeout as exc:
        details = {}
        try:
            details = json.loads(metadata_path.read_text(encoding="utf-8"))
        except (FileNotFoundError, OSError, ValueError):
            pass
        raise ProjectBusyError(
            "Timed out waiting for the project live-operation lock.", details=details
        ) from exc

    try:
        gate = FileLock(paths.operations_gate)
        try:
            gate.acquire(timeout=timeout_seconds)
        except Timeout as exc:
            raise ProjectBusyError(
                "Timed out waiting for an in-progress service lifecycle mutation."
            ) from exc
        try:
            _atomic_metadata(
                metadata_path,
                {
                    "pid": os.getpid(),
                    "started_at": time.time(),
                    "command_type": command_type,
                },
            )
            metadata_written = True
        finally:
            gate.release()
        yield
    finally:
        try:
            if metadata_written:
                _unlink_with_retry(metadata_path)
        finally:
            lock.release()


@contextmanager
def lifecycle_gate(paths: StatePaths, timeout_seconds: float = 30.0) -> Iterator[None]:
    paths.ensure()
    lock = FileLock(paths.operations_gate)
    try:
        lock.acquire(timeout=timeout_seconds)
    except Timeout as exc:
        raise ServiceError(
            "Timed out waiting for live-operation registration to finish."
        ) from exc
    try:
        yield
    finally:
        lock.release()


@contextmanager
def service_lock(paths: StatePaths, timeout_seconds: float = 30.0) -> Iterator[None]:
    paths.ensure()
    lock = FileLock(paths.service_lock)
    try:
        lock.acquire(timeout=timeout_seconds)
    except Timeout as exc:
        raise ServiceError(
            "Timed out waiting for the supervisor lifecycle lock."
        ) from exc
    try:
        yield
    finally:
        lock.release()
