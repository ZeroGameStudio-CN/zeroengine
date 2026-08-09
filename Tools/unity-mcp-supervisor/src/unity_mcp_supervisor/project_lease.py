from __future__ import annotations

import json
import math
import os
import time
import uuid
from dataclasses import asdict, dataclass
from pathlib import Path

from filelock import FileLock, Timeout

from .errors import ProjectBusyError, UsageError
from .locking import project_lock, project_lock_key
from .service_state import StatePaths, _atomic_write, _unlink_with_retry, process_alive

LEASE_SCHEMA_VERSION = 1
POLL_INTERVAL_SECONDS = 0.05


@dataclass(frozen=True)
class ProjectLease:
    schema_version: int
    project_root: str
    lease_id: str
    owner: str
    acquired_at: float
    renewed_at: float
    expires_at: float

    @classmethod
    def from_dict(cls, value: dict) -> ProjectLease:
        lease = cls(
            schema_version=int(value["schema_version"]),
            project_root=str(value["project_root"]),
            lease_id=str(value["lease_id"]),
            owner=str(value["owner"]),
            acquired_at=float(value["acquired_at"]),
            renewed_at=float(value["renewed_at"]),
            expires_at=float(value["expires_at"]),
        )
        if (
            lease.schema_version != LEASE_SCHEMA_VERSION
            or not lease.project_root
            or not lease.lease_id
            or not lease.owner
        ):
            raise ValueError("invalid project lease record")
        return lease

    def public_payload(self) -> dict:
        return {
            "active": True,
            "project_root": self.project_root,
            "owner": self.owner,
            "acquired_at": self.acquired_at,
            "renewed_at": self.renewed_at,
            "expires_at": self.expires_at,
        }

    def private_payload(self) -> dict:
        return {**self.public_payload(), "lease_id": self.lease_id}


@dataclass(frozen=True)
class ProjectLeaseWaiter:
    project_root: str
    waiter_id: str
    owner: str
    pid: int
    queue_order: int
    enqueued_at: float
    expires_at: float

    @classmethod
    def from_dict(cls, value: dict) -> ProjectLeaseWaiter:
        waiter = cls(
            project_root=str(value["project_root"]),
            waiter_id=str(value["waiter_id"]),
            owner=str(value["owner"]),
            pid=int(value["pid"]),
            queue_order=int(value["queue_order"]),
            enqueued_at=float(value["enqueued_at"]),
            expires_at=float(value["expires_at"]),
        )
        if (
            not waiter.project_root
            or not waiter.waiter_id
            or not waiter.owner
            or waiter.pid <= 0
            or waiter.queue_order <= 0
            or waiter.expires_at <= waiter.enqueued_at
        ):
            raise ValueError("invalid project lease waiter record")
        return waiter


def _validate_duration(value: float, label: str, *, allow_zero: bool = False) -> None:
    minimum = 0 if allow_zero else 0.0
    if not math.isfinite(value) or value < minimum or (not allow_zero and value == 0):
        qualifier = "zero or greater" if allow_zero else "greater than zero"
        raise UsageError(f"{label} must be {qualifier}.")


def _lease_files(paths: StatePaths, canonical_project_root: str) -> tuple[Path, Path]:
    key = project_lock_key(canonical_project_root)
    return (
        paths.project_leases / f"{key}.json",
        paths.project_leases / f"{key}.lock",
    )


def _queue_dir(paths: StatePaths, canonical_project_root: str) -> Path:
    key = project_lock_key(canonical_project_root)
    return paths.project_leases / "queues" / key


def _waiter_file(
    paths: StatePaths,
    canonical_project_root: str,
    waiter: ProjectLeaseWaiter,
) -> Path:
    return _queue_dir(paths, canonical_project_root) / (
        f"{waiter.queue_order:020d}-{waiter.waiter_id}.json"
    )


def _read_waiter_ticket(ticket_path: Path) -> ProjectLeaseWaiter | None:
    deadline = time.monotonic() + 1.0
    while True:
        try:
            return ProjectLeaseWaiter.from_dict(
                json.loads(ticket_path.read_text(encoding="utf-8"))
            )
        except FileNotFoundError:
            return None
        except PermissionError:
            if os.name != "nt" or time.monotonic() >= deadline:
                raise
            time.sleep(0.01)


def _read_waiters(
    paths: StatePaths,
    canonical_project_root: str,
    *,
    now: float,
) -> list[ProjectLeaseWaiter]:
    active: list[ProjectLeaseWaiter] = []
    for ticket_path in sorted(_queue_dir(paths, canonical_project_root).glob("*.json")):
        try:
            waiter = _read_waiter_ticket(ticket_path)
            if waiter is None:
                continue
            if (
                waiter.project_root != canonical_project_root
                or ticket_path != _waiter_file(paths, canonical_project_root, waiter)
            ):
                raise ValueError("project lease waiter mismatch")
        except FileNotFoundError:
            continue
        except (KeyError, OSError, TypeError, ValueError) as exc:
            raise ProjectBusyError(
                "Project lease queue is invalid; refusing to bypass it.",
                details={
                    "project_root": canonical_project_root,
                    "queue_record": str(ticket_path),
                    "reason": "invalid-queue",
                },
            ) from exc
        if waiter.expires_at <= now or not process_alive(waiter.pid):
            _unlink_with_retry(ticket_path)
            continue
        active.append(waiter)
    return active


def _read_active_unlocked(
    metadata_path: Path,
    canonical_project_root: str,
    *,
    now: float,
) -> ProjectLease | None:
    try:
        value = json.loads(metadata_path.read_text(encoding="utf-8"))
        lease = ProjectLease.from_dict(value)
    except FileNotFoundError:
        return None
    except (KeyError, OSError, TypeError, ValueError) as exc:
        raise ProjectBusyError(
            "Project lease record is invalid; refusing to overwrite it.",
            details={
                "project_root": canonical_project_root,
                "lease_record": str(metadata_path),
                "reason": "invalid-record",
            },
        ) from exc
    if lease.project_root != canonical_project_root:
        raise ProjectBusyError(
            "Project lease record does not match its canonical project root.",
            details={
                "project_root": canonical_project_root,
                "lease_record": str(metadata_path),
                "reason": "project-root-mismatch",
            },
        )
    if lease.expires_at <= now:
        _unlink_with_retry(metadata_path)
        return None
    return lease


def _guard(lock_path: Path) -> FileLock:
    lock = FileLock(lock_path)
    try:
        lock.acquire(timeout=2)
    except Timeout as exc:
        raise ProjectBusyError("Timed out inspecting the project task lease.") from exc
    return lock


def inspect_project_lease(
    paths: StatePaths, canonical_project_root: str
) -> ProjectLease | None:
    paths.ensure()
    metadata_path, lock_path = _lease_files(paths, canonical_project_root)
    lock = _guard(lock_path)
    try:
        return _read_active_unlocked(
            metadata_path, canonical_project_root, now=time.time()
        )
    finally:
        lock.release()


def inspect_project_lease_queue(
    paths: StatePaths, canonical_project_root: str
) -> list[ProjectLeaseWaiter]:
    paths.ensure()
    _, lock_path = _lease_files(paths, canonical_project_root)
    lock = _guard(lock_path)
    try:
        return _read_waiters(paths, canonical_project_root, now=time.time())
    finally:
        lock.release()


def _conflict(lease: ProjectLease) -> ProjectBusyError:
    return ProjectBusyError(
        "Project task lease is owned by another task.",
        details=lease.public_payload(),
    )


def _queue_conflict(
    waiters: list[ProjectLeaseWaiter], waiter_id: str | None = None
) -> ProjectBusyError:
    details = {
        "owner": waiters[0].owner,
        "queue_depth": len(waiters),
        "reason": "lease-queue",
    }
    if waiter_id is not None:
        for index, waiter in enumerate(waiters):
            if waiter.waiter_id == waiter_id:
                details["queue_position"] = index + 1
                break
    return ProjectBusyError(
        "Project task lease is queued behind another task.", details=details
    )


def create_project_lease_unlocked(
    paths: StatePaths,
    canonical_project_root: str,
    owner: str,
    ttl_seconds: float,
) -> ProjectLease:
    """Create a lease while the caller holds the canonical project lock."""
    owner = owner.strip()
    if not owner:
        raise UsageError("Lease owner must not be empty.")
    _validate_duration(ttl_seconds, "Lease TTL")
    paths.ensure()
    metadata_path, lock_path = _lease_files(paths, canonical_project_root)
    lock = _guard(lock_path)
    try:
        now = time.time()
        current = _read_active_unlocked(metadata_path, canonical_project_root, now=now)
        if current is not None:
            raise _conflict(current)
        waiters = _read_waiters(paths, canonical_project_root, now=now)
        if waiters:
            raise _queue_conflict(waiters)
        lease = ProjectLease(
            schema_version=LEASE_SCHEMA_VERSION,
            project_root=canonical_project_root,
            lease_id=uuid.uuid4().hex,
            owner=owner,
            acquired_at=now,
            renewed_at=now,
            expires_at=now + ttl_seconds,
        )
        _atomic_write(
            metadata_path,
            json.dumps(asdict(lease), sort_keys=True) + "\n",
        )
        return lease
    finally:
        lock.release()


def release_project_lease_unlocked(
    paths: StatePaths,
    canonical_project_root: str,
    lease_id: str,
) -> bool:
    """Release a lease while the caller holds the canonical project lock."""
    if not lease_id:
        raise UsageError("Lease ID must not be empty.")
    metadata_path, lock_path = _lease_files(paths, canonical_project_root)
    lock = _guard(lock_path)
    try:
        lease = _read_active_unlocked(
            metadata_path, canonical_project_root, now=time.time()
        )
        if lease is None:
            return False
        if lease_id != lease.lease_id:
            raise _conflict(lease)
        _unlink_with_retry(metadata_path)
        return True
    finally:
        lock.release()


def acquire_project_lease(
    paths: StatePaths,
    canonical_project_root: str,
    owner: str,
    ttl_seconds: float,
    wait_seconds: float,
) -> ProjectLease:
    owner = owner.strip()
    if not owner:
        raise UsageError("Lease owner must not be empty.")
    _validate_duration(ttl_seconds, "Lease TTL")
    _validate_duration(wait_seconds, "Lease wait", allow_zero=True)
    deadline = time.monotonic() + wait_seconds
    waiter: ProjectLeaseWaiter | None = None
    ticket_path: Path | None = None
    if wait_seconds > 0:
        now = time.time()
        waiter = ProjectLeaseWaiter(
            project_root=canonical_project_root,
            waiter_id=uuid.uuid4().hex,
            owner=owner,
            pid=os.getpid(),
            queue_order=time.monotonic_ns(),
            enqueued_at=now,
            expires_at=now + wait_seconds,
        )
        ticket_path = _waiter_file(paths, canonical_project_root, waiter)
        _atomic_write(ticket_path, json.dumps(asdict(waiter), sort_keys=True) + "\n")

    last_conflict: ProjectBusyError | None = None
    try:
        while True:
            current = inspect_project_lease(paths, canonical_project_root)
            waiters = inspect_project_lease_queue(paths, canonical_project_root)
            if current is not None:
                last_conflict = _conflict(current)
            elif waiters and (
                waiter is None or waiters[0].waiter_id != waiter.waiter_id
            ):
                last_conflict = _queue_conflict(
                    waiters, waiter.waiter_id if waiter else None
                )
            else:
                remaining = max(0.0, deadline - time.monotonic())
                lock_timeout = min(0.2, remaining) if wait_seconds > 0 else 0
                try:
                    with project_lock(
                        paths,
                        canonical_project_root,
                        "lease-acquire",
                        lock_timeout,
                    ):
                        metadata_path, lock_path = _lease_files(
                            paths, canonical_project_root
                        )
                        lock = _guard(lock_path)
                        try:
                            current = _read_active_unlocked(
                                metadata_path,
                                canonical_project_root,
                                now=time.time(),
                            )
                            waiters = _read_waiters(
                                paths,
                                canonical_project_root,
                                now=time.time(),
                            )
                            if current is not None:
                                last_conflict = _conflict(current)
                            elif waiters and (
                                waiter is None
                                or waiters[0].waiter_id != waiter.waiter_id
                            ):
                                last_conflict = _queue_conflict(
                                    waiters,
                                    waiter.waiter_id if waiter else None,
                                )
                            elif waiter is not None and not waiters:
                                last_conflict = ProjectBusyError(
                                    "Project lease queue entry expired before acquisition.",
                                    details={"reason": "queue-entry-expired"},
                                )
                            else:
                                now = time.time()
                                lease = ProjectLease(
                                    schema_version=LEASE_SCHEMA_VERSION,
                                    project_root=canonical_project_root,
                                    lease_id=uuid.uuid4().hex,
                                    owner=owner,
                                    acquired_at=now,
                                    renewed_at=now,
                                    expires_at=now + ttl_seconds,
                                )
                                _atomic_write(
                                    metadata_path,
                                    json.dumps(asdict(lease), sort_keys=True) + "\n",
                                )
                                return lease
                        finally:
                            lock.release()
                except ProjectBusyError as exc:
                    last_conflict = exc
            remaining = deadline - time.monotonic()
            if wait_seconds == 0 or remaining <= 0:
                raise last_conflict or ProjectBusyError(
                    "Project task lease could not be acquired."
                )
            time.sleep(min(POLL_INTERVAL_SECONDS, remaining))
    finally:
        if ticket_path is not None:
            try:
                _unlink_with_retry(ticket_path)
            except OSError:
                pass


def require_project_lease(
    paths: StatePaths,
    canonical_project_root: str,
    lease_id: str | None,
    ttl_seconds: float,
    *,
    renew: bool = True,
) -> ProjectLease | None:
    _validate_duration(ttl_seconds, "Lease TTL")
    paths.ensure()
    metadata_path, lock_path = _lease_files(paths, canonical_project_root)
    lock = _guard(lock_path)
    try:
        lease = _read_active_unlocked(
            metadata_path, canonical_project_root, now=time.time()
        )
        if lease is None:
            return None
        if not lease_id or lease_id != lease.lease_id:
            raise _conflict(lease)
        if not renew:
            return lease
        now = time.time()
        renewed = ProjectLease(
            schema_version=lease.schema_version,
            project_root=lease.project_root,
            lease_id=lease.lease_id,
            owner=lease.owner,
            acquired_at=lease.acquired_at,
            renewed_at=now,
            expires_at=now + ttl_seconds,
        )
        _atomic_write(metadata_path, json.dumps(asdict(renewed), sort_keys=True) + "\n")
        return renewed
    finally:
        lock.release()


def release_project_lease(
    paths: StatePaths, canonical_project_root: str, lease_id: str
) -> bool:
    if not lease_id:
        raise UsageError("Lease ID must not be empty.")
    with project_lock(paths, canonical_project_root, "lease-release", 30):
        return release_project_lease_unlocked(paths, canonical_project_root, lease_id)


def live_project_lease_owners(paths: StatePaths) -> list[dict]:
    paths.ensure()
    owners: list[dict] = []
    for metadata_path in paths.project_leases.glob("*.json"):
        lock = _guard(metadata_path.with_suffix(".lock"))
        try:
            try:
                value = json.loads(metadata_path.read_text(encoding="utf-8"))
                lease = ProjectLease.from_dict(value)
            except (KeyError, OSError, TypeError, ValueError):
                owners.append(
                    {
                        "command_type": "project-lease",
                        "lease_record": str(metadata_path),
                        "reason": "invalid-record",
                    }
                )
                continue
            if lease.expires_at <= time.time():
                _unlink_with_retry(metadata_path)
                continue
            owners.append({"command_type": "project-lease", **lease.public_payload()})
        finally:
            lock.release()
    return owners
