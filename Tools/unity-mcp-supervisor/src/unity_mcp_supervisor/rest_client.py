from __future__ import annotations

import hashlib
import json
import socket
import time
import uuid
from dataclasses import dataclass
from enum import Enum
from typing import Any
from urllib.parse import urlparse

import httpx

from .errors import (
    IncompatibleError,
    OutcomeUnknownError,
    ProjectError,
    ServiceError,
    UnityCommandError,
)

PINNED_SERVER_VERSION = "10.1.2"


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
        command_id = None if safe_probe else f"command-{uuid.uuid4().hex}"
        envelope_sha256 = hashlib.sha256(
            json.dumps(
                {"name": command_type, "params": params},
                sort_keys=True,
                separators=(",", ":"),
                ensure_ascii=False,
            ).encode("utf-8")
        ).hexdigest()
        payload = {
            "type": command_type,
            "params": params,
            "unity_instance": project_hash,
        }
        if command_id is not None:
            payload.update(
                command_id=command_id,
                envelope_sha256=envelope_sha256,
            )
        request_timeout = timeout_seconds or self.timeout_seconds
        try:
            response = _request(
                "POST",
                self._url("api/command"),
                json=payload,
                timeout=request_timeout,
            )
        except httpx.RequestError as exc:
            if safe_probe:
                raise ServiceError(f"Safe Unity probe failed: {exc}") from exc
            data = self._recover_command_receipt(
                command_id,
                project_hash,
                payload,
                request_timeout,
            )
            return self._finish_command_receipt(
                command_id, project_hash, command_type, data
            )

        if response.status_code in (404, 503):
            raise ProjectError(
                "The target Unity project is not currently connected.",
                details={
                    "project_hash": project_hash,
                    "http_status": response.status_code,
                },
            )
        if response.status_code == 409:
            try:
                conflict = response.json()
            except ValueError:
                conflict = {}
            if conflict.get("code") == "receipt_protocol_unavailable":
                raise IncompatibleError(
                    "Unity plugin does not support durable command receipts.",
                    details={"reason": "receipt-protocol-unavailable"},
                )
        try:
            response.raise_for_status()
            data = response.json()
        except (httpx.HTTPError, ValueError, TypeError) as exc:
            if safe_probe:
                raise ServiceError(
                    f"Safe Unity probe returned an invalid response: {exc}"
                ) from exc
            data = self._recover_command_receipt(
                command_id,
                project_hash,
                payload,
                request_timeout,
            )
            return self._finish_command_receipt(
                command_id, project_hash, command_type, data
            )

        if not isinstance(data, dict):
            if command_id is not None:
                raise OutcomeUnknownError(
                    "Unity command receipt returned an invalid result.",
                    details={"command_type": command_type, "command_id": command_id},
                )
            raise UnityCommandError("Unity command response must be a JSON object.")
        if command_id is not None and data.get("receipt_state") == "pending":
            data = self._recover_command_receipt(
                command_id,
                project_hash,
                payload,
                request_timeout,
            )
        if command_id is not None:
            return self._finish_command_receipt(
                command_id, project_hash, command_type, data
            )

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

    def _recover_command_receipt(
        self,
        command_id: str,
        project_hash: str,
        payload: dict[str, Any],
        request_timeout: float,
    ) -> dict[str, Any]:
        deadline = time.monotonic() + max(60.0, request_timeout)
        resent = False
        last_state = "unreachable"
        while time.monotonic() < deadline:
            try:
                response = _request(
                    "GET",
                    self._url(f"api/command-receipts/{command_id}"),
                    params={"project_hash": project_hash},
                    timeout=2.0,
                )
                response.raise_for_status()
                receipt = response.json()
                state = str(receipt.get("state") or "missing")
                last_state = state
                if state == "completed" and isinstance(receipt.get("result"), dict):
                    return receipt["result"]
                if state == "ambiguous":
                    raise OutcomeUnknownError(
                        "Unity command started but did not persist a result.",
                        details={
                            "command_type": payload["type"],
                            "command_id": command_id,
                            "receipt_state": state,
                        },
                    )
                if state == "conflict":
                    raise UnityCommandError(
                        "Unity command receipt conflicts with the original envelope."
                    )
                if state == "missing" and not resent:
                    resent = True
                    try:
                        replay = _request(
                            "POST",
                            self._url("api/command"),
                            json=payload,
                            timeout=min(5.0, request_timeout),
                        )
                        if replay.status_code == 409:
                            raise IncompatibleError(
                                "Unity plugin does not support durable command receipts.",
                                details={"reason": "receipt-protocol-unavailable"},
                            )
                        if replay.status_code == 200:
                            replay_data = replay.json()
                            if replay_data.get("receipt_state") == "ambiguous":
                                raise OutcomeUnknownError(
                                    "Unity command started but did not persist a result.",
                                    details={
                                        "command_type": payload["type"],
                                        "command_id": command_id,
                                        "receipt_state": "ambiguous",
                                    },
                                )
                            if replay_data.get("receipt_state") != "pending":
                                return replay_data
                    except httpx.RequestError:
                        pass
            except (httpx.HTTPError, ValueError, TypeError):
                last_state = "unreachable"
            time.sleep(0.25)
        raise OutcomeUnknownError(
            "Unity command receipt recovery budget expired.",
            details={
                "command_type": payload["type"],
                "command_id": command_id,
                "receipt_state": last_state,
            },
        )

    def _finish_command_receipt(
        self,
        command_id: str,
        project_hash: str,
        command_type: str,
        data: Any,
    ) -> dict[str, Any]:
        if not isinstance(data, dict):
            raise OutcomeUnknownError(
                "Unity command receipt returned an invalid result.",
                details={"command_type": command_type, "command_id": command_id},
            )
        receipt_state = str(data.get("receipt_state") or "")
        if receipt_state in {"ambiguous", "receipt_error"}:
            raise OutcomeUnknownError(
                "Unity command started but did not persist a trustworthy result.",
                details={
                    "command_type": command_type,
                    "command_id": command_id,
                    "receipt_state": receipt_state,
                },
            )
        if receipt_state in {"conflict", "capacity"}:
            raise UnityCommandError(
                str(data.get("error") or "Unity command receipt was rejected."),
                details={"response": data},
            )
        try:
            _request(
                "POST",
                self._url(f"api/command-receipts/{command_id}/ack"),
                json={"project_hash": project_hash},
                timeout=2.0,
            )
        except httpx.RequestError:
            pass
        if data.get("success") is False:
            error = str(
                data.get("error") or data.get("message") or "Unity command failed."
            )
            raise UnityCommandError(error, details={"response": data})
        return data
