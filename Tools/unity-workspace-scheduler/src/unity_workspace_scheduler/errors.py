"""Public scheduler errors."""

from __future__ import annotations

from typing import Any


class SchedulerError(Exception):
    """A stable, JSON-serializable command failure."""

    code = "scheduler-error"
    exit_code = 1

    def __init__(self, message: str, *, details: dict[str, Any] | None = None) -> None:
        super().__init__(message)
        self.message = message
        self.details = details or {}


class UsageError(SchedulerError):
    code = "usage-error"
    exit_code = 2


class BusyError(SchedulerError):
    code = "workspace-busy"
    exit_code = 3


class AuthorizationError(SchedulerError):
    code = "task-token-invalid"
    exit_code = 4


class ClaimAuthorizationError(SchedulerError):
    code = "claim-not-owned"
    exit_code = 4


class StateError(SchedulerError):
    code = "workspace-state-invalid"
    exit_code = 5
