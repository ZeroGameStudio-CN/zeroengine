from __future__ import annotations

import importlib.metadata
import json
import os
import shutil
import subprocess
import sys
from pathlib import Path
from typing import Any
from urllib.parse import urlparse

from .errors import OutcomeUnknownError, ServiceError
from .service_state import Settings


def upstream_version() -> str:
    try:
        return importlib.metadata.version("mcpforunityserver")
    except importlib.metadata.PackageNotFoundError:
        return "unknown"


def _entry_path(name: str) -> Path | None:
    suffix = ".exe" if os.name == "nt" else ""
    sibling = Path(sys.executable).resolve().parent / f"{name}{suffix}"
    if sibling.is_file():
        return sibling
    found = shutil.which(name)
    return Path(found) if found else None


def server_command(settings: Settings, server_token: str) -> list[str]:
    if os.environ.get("UMCP_TEST_MODE") == "1" and os.environ.get(
        "UMCP_TEST_SERVER_SCRIPT"
    ):
        command = [sys.executable, os.environ["UMCP_TEST_SERVER_SCRIPT"]]
    else:
        entry = _entry_path("mcp-for-unity")
        if entry is None:
            raise ServiceError(
                "Pinned mcp-for-unity executable was not found in the current environment."
            )
        command = [str(entry)]
    parsed = urlparse(settings.endpoint)
    command.extend(
        [
            "--transport",
            "http",
            "--http-host",
            "127.0.0.1",
            "--http-port",
            str(parsed.port),
            "--unity-instance-token",
            server_token,
            "--pidfile",
            str(settings.paths.server_pid),
            "--project-scoped-tools",
        ]
    )
    return command


def run_upstream_cli(
    settings: Settings,
    project_hash: str,
    args: tuple[str, ...],
) -> dict[str, Any]:
    if os.environ.get("UMCP_TEST_MODE") == "1" and os.environ.get(
        "UMCP_TEST_CLI_SCRIPT"
    ):
        command = [sys.executable, os.environ["UMCP_TEST_CLI_SCRIPT"]]
    else:
        entry = _entry_path("unity-mcp")
        if entry is None:
            raise ServiceError(
                "Pinned unity-mcp executable was not found in the current environment."
            )
        command = [str(entry)]
    parsed = urlparse(settings.endpoint)
    command.extend(
        [
            "--host",
            "127.0.0.1",
            "--port",
            str(parsed.port),
            "--timeout",
            str(int(settings.command_timeout_seconds)),
            "--format",
            "json",
            "--instance",
            project_hash,
            *args,
        ]
    )
    env = os.environ.copy()
    env.update(
        {
            "UNITY_MCP_HOST": "127.0.0.1",
            "UNITY_MCP_HTTP_PORT": str(parsed.port),
            "UNITY_MCP_TIMEOUT": str(int(settings.command_timeout_seconds)),
            "UNITY_MCP_FORMAT": "json",
            "UNITY_MCP_INSTANCE": project_hash,
        }
    )
    try:
        run_kwargs: dict[str, Any] = {}
        if os.name == "nt":
            run_kwargs["creationflags"] = subprocess.CREATE_NO_WINDOW
        completed = subprocess.run(
            command,
            env=env,
            capture_output=True,
            text=True,
            timeout=settings.command_timeout_seconds + 5,
            check=False,
            **run_kwargs,
        )
    except (OSError, subprocess.TimeoutExpired) as exc:
        raise OutcomeUnknownError(
            "Upstream CLI did not return a trustworthy command result."
        ) from exc

    stdout = completed.stdout.strip()
    stderr = completed.stderr.strip()
    try:
        parsed_stdout: Any = json.loads(stdout) if stdout else None
    except ValueError:
        parsed_stdout = stdout
    result = {
        "exit_code": completed.returncode,
        "stdout": parsed_stdout,
        "stderr": stderr,
    }
    if completed.returncode != 0:
        raise OutcomeUnknownError(
            "Upstream CLI returned a failure after command dispatch may have occurred.",
            details=result,
        )
    return result
