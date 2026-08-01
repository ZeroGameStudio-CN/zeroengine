from __future__ import annotations


class UmcpError(Exception):
    exit_code = 1
    error_code = "umcp_error"
    retryable = False
    outcome_unknown = False

    def __init__(self, message: str, *, details: dict | None = None) -> None:
        super().__init__(message)
        self.message = message
        self.details = details or {}


class UsageError(UmcpError):
    exit_code = 2
    error_code = "invalid_request"


class ServiceError(UmcpError):
    exit_code = 3
    error_code = "service_unavailable"
    retryable = True


class ForeignListenerError(ServiceError):
    error_code = "foreign_listener"
    retryable = False


class ProjectError(UmcpError):
    exit_code = 4
    error_code = "project_not_connected"
    retryable = True


class EditorNotOpenError(ProjectError):
    error_code = "editor_not_open"


class EditorBootstrapError(ProjectError):
    error_code = "editor_bootstrap_failed"


class EditorRestartRequiredError(ProjectError):
    error_code = "editor_restart_required"


class EditorControlUnsupportedError(ProjectError):
    error_code = "editor_control_unsupported"
    retryable = False


class EditorControlUnavailableError(ProjectError):
    error_code = "editor_control_unavailable"
    retryable = False


class ProjectBusyError(UmcpError):
    exit_code = 5
    error_code = "project_busy"
    retryable = True


class UnityCommandError(UmcpError):
    exit_code = 6
    error_code = "unity_command_failed"


class OutcomeUnknownError(UmcpError):
    exit_code = 7
    error_code = "outcome_unknown"
    outcome_unknown = True


class IncompatibleError(UmcpError):
    exit_code = 8
    error_code = "incompatible"
