from __future__ import annotations

import socket
from dataclasses import dataclass
from enum import Enum
from typing import Any
from urllib.parse import urlparse

import httpx

from .errors import OutcomeUnknownError, ProjectError, ServiceError, UnityCommandError

PINNED_SERVER_VERSION = "10.1.0"


def _request(method: str, url: str, **kwargs: Any) -> httpx.Response:
    with httpx.Client(trust_env=False) as client:
        return client.request(method, url, **kwargs)


class EndpointKind(str, Enum):
    DOWN = "server-down"
    COMPATIBLE = "compatible"
    FOREIGN = "foreign-listener"


@dataclass(frozen=True)
class EndpointProbe:
    kind: EndpointKind
    health: dict[str, Any] | None = None
    message: str = ""


class RestClient:
    def __init__(self, endpoint: str, timeout_seconds: float = 30.0) -> None:
        self.endpoint = endpoint.rstrip("/")
        self.timeout_seconds = timeout_seconds

    def _url(self, path: str) -> str:
        return f"{self.endpoint}/{path.lstrip('/')}"

    def tcp_open(self, timeout_seconds: float = 0.4) -> bool:
        parsed = urlparse(self.endpoint)
        try:
            with socket.create_connection(
                (parsed.hostname or "127.0.0.1", parsed.port or 80), timeout_seconds
            ):
                return True
        except OSError:
            return False

    def health(self, timeout_seconds: float = 2.0) -> dict[str, Any]:
        try:
            response = _request("GET", self._url("health"), timeout=timeout_seconds)
            response.raise_for_status()
            data = response.json()
        except (httpx.HTTPError, ValueError, TypeError) as exc:
            raise ServiceError(f"Unity MCP health check failed: {exc}") from exc
        if not isinstance(data, dict) or data.get("status") != "healthy":
            raise ServiceError(
                "Endpoint health response is not a Unity MCP health payload."
            )
        version = str(data.get("version") or "").removeprefix("v")
        if version != PINNED_SERVER_VERSION:
            raise ServiceError(
                f"Endpoint Unity MCP version '{version or 'missing'}' does not match pinned {PINNED_SERVER_VERSION}."
            )
        return data

    def instances(self, timeout_seconds: float | None = None) -> list[dict[str, Any]]:
        try:
            response = _request(
                "GET",
                self._url("api/instances"),
                timeout=timeout_seconds or self.timeout_seconds,
            )
            response.raise_for_status()
            data = response.json()
        except (httpx.HTTPError, ValueError, TypeError) as exc:
            raise ServiceError(f"Failed to list Unity instances: {exc}") from exc
        instances = data.get("instances") if isinstance(data, dict) else None
        if not isinstance(instances, list):
            raise ServiceError(
                "Endpoint does not expose the expected Unity MCP instances contract."
            )
        return [item for item in instances if isinstance(item, dict)]

    def classify(self) -> EndpointProbe:
        if not self.tcp_open():
            return EndpointProbe(
                EndpointKind.DOWN, message="No listener on the configured endpoint."
            )
        try:
            health = self.health()
            self.instances(timeout_seconds=2.0)
            return EndpointProbe(
                EndpointKind.COMPATIBLE,
                health=health,
                message="Compatible Unity MCP server is reachable.",
            )
        except ServiceError as exc:
            return EndpointProbe(EndpointKind.FOREIGN, message=str(exc))

    def command(
        self,
        command_type: str,
        params: dict[str, Any],
        project_hash: str,
        *,
        timeout_seconds: float | None = None,
        safe_probe: bool = False,
    ) -> dict[str, Any]:
        payload = {
            "type": command_type,
            "params": params,
            "unity_instance": project_hash,
        }
        try:
            response = _request(
                "POST",
                self._url("api/command"),
                json=payload,
                timeout=timeout_seconds or self.timeout_seconds,
            )
        except httpx.RequestError as exc:
            if safe_probe:
                raise ServiceError(f"Safe Unity probe failed: {exc}") from exc
            raise OutcomeUnknownError(
                "Unity command response was lost after dispatch was attempted.",
                details={"command_type": command_type},
            ) from exc

        if response.status_code in (404, 503):
            raise ProjectError(
                "The target Unity project is not currently connected.",
                details={
                    "project_hash": project_hash,
                    "http_status": response.status_code,
                },
            )
        try:
            response.raise_for_status()
            data = response.json()
        except (httpx.HTTPError, ValueError, TypeError) as exc:
            if safe_probe:
                raise ServiceError(
                    f"Safe Unity probe returned an invalid response: {exc}"
                ) from exc
            raise OutcomeUnknownError(
                "Unity command returned no trustworthy result after dispatch.",
                details={
                    "command_type": command_type,
                    "http_status": response.status_code,
                },
            ) from exc

        if not isinstance(data, dict):
            raise UnityCommandError("Unity command response must be a JSON object.")
        if data.get("success") is False:
            error = str(
                data.get("error") or data.get("message") or "Unity command failed."
            )
            hint = str(data.get("hint") or "")
            if safe_probe:
                raise ServiceError(f"Safe Unity probe failed: {error}")
            if hint.lower() == "retry" and "disconnect" in error.lower():
                raise OutcomeUnknownError(
                    "Unity disconnected while the command result was pending.",
                    details={"command_type": command_type, "upstream_error": error},
                )
            raise UnityCommandError(error, details={"response": data})
        return data
