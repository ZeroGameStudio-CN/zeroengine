from __future__ import annotations

import json
import time
import uuid
from collections.abc import Callable
from dataclasses import dataclass
from pathlib import Path
from typing import Any

import click

from .compatibility import check_compatibility, require_compatible
from .editor_bootstrap import bootstrap_diagnostics, ensure_project_connection
from .editor_control import companion_package_path
from .errors import ProjectError, ServiceError, UmcpError, UsageError
from .locking import project_lock
from .project_resolver import (
    ProjectResolver,
    canonical_project_root,
    find_project_root,
)
from .rest_client import RestClient
from .service_state import Settings
from .startup import disable_startup, enable_startup
from .supervisor import ServiceManager, run_daemon
from .upstream_cli import run_upstream_cli


@dataclass(frozen=True)
class AppContext:
    state_dir: Path | None
    endpoint: str | None
    human: bool

    def settings(self) -> Settings:
        return Settings.load(self.state_dir, self.endpoint)


@dataclass(frozen=True)
class ActionResult:
    message: str
    result: Any
    project_hash: str | None = None


def _emit(app: AppContext, envelope: dict[str, Any]) -> None:
    if app.human:
        click.echo(envelope["message"])
        if envelope.get("result") not in (None, {}, []):
            click.echo(
                json.dumps(
                    envelope["result"], ensure_ascii=False, indent=2, sort_keys=True
                )
            )
        return
    click.echo(json.dumps(envelope, ensure_ascii=False, sort_keys=True))


def _execute(app: AppContext, action: Callable[[], ActionResult]) -> None:
    started = time.monotonic()
    endpoint = app.endpoint
    try:
        settings = app.settings()
        endpoint = settings.endpoint
        value = action()
        endpoint = app.settings().endpoint
        envelope = {
            "ok": True,
            "code": "ok",
            "message": value.message,
            "project_hash": value.project_hash,
            "endpoint": endpoint,
            "duration_ms": round((time.monotonic() - started) * 1000, 3),
            "result": value.result,
        }
        _emit(app, envelope)
    except UmcpError as exc:
        envelope = {
            "ok": False,
            "code": exc.error_code,
            "message": exc.message,
            "project_hash": exc.details.get("project_hash"),
            "endpoint": endpoint,
            "duration_ms": round((time.monotonic() - started) * 1000, 3),
            "result": exc.details,
            "retryable": exc.retryable,
            "outcome_unknown": exc.outcome_unknown,
        }
        _emit(app, envelope)
        raise click.exceptions.Exit(exc.exit_code) from exc


@click.group()
@click.option(
    "--state-dir",
    type=click.Path(path_type=Path),
    default=None,
    help="Override the per-user state directory.",
)
@click.option(
    "--endpoint",
    default=None,
    help="Override the fixed loopback endpoint for this invocation.",
)
@click.option(
    "--human", is_flag=True, help="Print concise human-readable output instead of JSON."
)
@click.pass_context
def cli(
    ctx: click.Context, state_dir: Path | None, endpoint: str | None, human: bool
) -> None:
    """Project-safe Unity MCP supervisor and command CLI."""
    ctx.obj = AppContext(state_dir=state_dir, endpoint=endpoint, human=human)


@cli.group()
def service() -> None:
    """Manage the shared Unity MCP server supervisor."""


@service.command("start")
@click.pass_obj
def service_start(app: AppContext) -> None:
    _execute(app, lambda: _service_ensure_result(app.settings(), "Service is ready."))


@service.command("ensure")
@click.pass_obj
def service_ensure(app: AppContext) -> None:
    _execute(app, lambda: _service_ensure_result(app.settings(), "Service is ready."))


def _service_ensure_result(settings: Settings, message: str) -> ActionResult:
    return ActionResult(message, ServiceManager(settings).ensure())


@service.command("status")
@click.pass_obj
def service_status(app: AppContext) -> None:
    _execute(
        app,
        lambda: ActionResult(
            "Service status inspected.", ServiceManager(app.settings()).status()
        ),
    )


@service.command("restart")
@click.pass_obj
def service_restart(app: AppContext) -> None:
    _execute(
        app,
        lambda: ActionResult(
            "Owned service restarted.", ServiceManager(app.settings()).restart()
        ),
    )


@service.command("stop")
@click.pass_obj
def service_stop(app: AppContext) -> None:
    _execute(
        app,
        lambda: ActionResult(
            "Owned supervisor stopped.", ServiceManager(app.settings()).stop()
        ),
    )


@service.command("enable")
@click.pass_obj
def service_enable(app: AppContext) -> None:
    def action() -> ActionResult:
        path = enable_startup(app.settings())
        return ActionResult("User login startup enabled.", {"startup_file": str(path)})

    _execute(app, action)


@service.command("disable")
@click.pass_obj
def service_disable(app: AppContext) -> None:
    def action() -> ActionResult:
        path = disable_startup(app.settings())
        return ActionResult("User login startup disabled.", {"startup_file": str(path)})

    _execute(app, action)


@service.command("logs")
@click.pass_obj
def service_logs(app: AppContext) -> None:
    def action() -> ActionResult:
        paths = app.settings().paths
        return ActionResult(
            "Service log paths resolved.",
            {"supervisor": str(paths.supervisor_log), "server": str(paths.server_log)},
        )

    _execute(app, action)


@cli.group("config")
def config_group() -> None:
    """Manage machine-local supervisor configuration."""


@config_group.command("show")
@click.pass_obj
def config_show(app: AppContext) -> None:
    def action() -> ActionResult:
        settings = app.settings()
        return ActionResult(
            "Configuration loaded.",
            {
                "state_dir": str(settings.state_dir),
                "endpoint": settings.endpoint,
                "approved_plugin_refs": list(settings.approved_plugin_refs),
            },
        )

    _execute(app, action)


@config_group.command("set-endpoint")
@click.argument("endpoint")
@click.pass_obj
def config_set_endpoint(app: AppContext, endpoint: str) -> None:
    def action() -> ActionResult:
        current = app.settings()
        if ServiceManager(current).status()["supervisor_alive"]:
            raise ServiceError(
                "Stop the owned supervisor before changing its fixed endpoint."
            )
        settings = current.save(endpoint=endpoint)
        return ActionResult(
            "Fixed machine endpoint saved.", {"endpoint": settings.endpoint}
        )

    _execute(app, action)


@config_group.command("approve-plugin")
@click.argument("plugin_ref")
@click.pass_obj
def config_approve_plugin(app: AppContext, plugin_ref: str) -> None:
    def action() -> ActionResult:
        value = plugin_ref.strip()
        if not value:
            raise UsageError("Plugin reference must not be empty.")
        settings = app.settings()
        approved = tuple(dict.fromkeys((*settings.approved_plugin_refs, value)))
        saved = settings.save(approved_plugin_refs=approved)
        return ActionResult(
            "Plugin reference approved locally.",
            {"approved_plugin_refs": list(saved.approved_plugin_refs)},
        )

    _execute(app, action)


@cli.group("control")
def control_group() -> None:
    """Inspect the Unity Editor companion control package."""


@control_group.command("package-path")
@click.pass_obj
def control_package_path(app: AppContext) -> None:
    def action() -> ActionResult:
        path = companion_package_path()
        if not (path / "package.json").is_file():
            raise UsageError(
                "The installed wheel does not contain the companion package."
            )
        return ActionResult(
            "Companion package path resolved.",
            {"package_path": str(path)},
        )

    _execute(app, action)


def _project_root(project: Path) -> Path:
    return find_project_root(project)


def _resolved_payload(resolved: Any) -> dict[str, Any]:
    return {
        "project_root": str(resolved.root),
        "project_hash": resolved.project_hash,
        "project_name": resolved.project_name,
        "unity_version": resolved.unity_version,
        "connected_at": resolved.connected_at,
    }


@cli.command("connect")
@click.option("--project", type=click.Path(path_type=Path), default=Path.cwd)
@click.option(
    "--timeout", type=float, default=None, help="Connection wait budget in seconds."
)
@click.option(
    "--restart-editor",
    is_flag=True,
    help="Normally close and silently relaunch the target Unity Editor.",
)
@click.pass_obj
def connect_command(
    app: AppContext,
    project: Path,
    timeout: float | None,
    restart_editor: bool,
) -> None:
    def action() -> ActionResult:
        settings = app.settings()
        root = _project_root(project)
        compatibility = require_compatible(root, settings.approved_plugin_refs)
        ServiceManager(settings).ensure()
        canonical = canonical_project_root(root)
        budget = timeout if timeout is not None else settings.bootstrap_timeout_seconds
        if budget <= 0:
            raise UsageError("Connection timeout must be greater than zero.")
        started = time.monotonic()
        with project_lock(
            settings.paths,
            canonical,
            "connect",
            min(settings.project_lock_timeout_seconds, budget),
        ):
            remaining = budget - (time.monotonic() - started)
            if remaining <= 0:
                raise ProjectError(
                    "Connection budget expired while waiting for the project lock."
                )
            resolved, bootstrap = ensure_project_connection(
                root,
                settings,
                RestClient(settings.endpoint, settings.command_timeout_seconds),
                remaining,
                allow_editor_restart=restart_editor,
            )
        result = _resolved_payload(resolved)
        result["compatibility"] = compatibility.status
        result["bootstrap"] = bootstrap.to_dict()
        return ActionResult(
            "Target Unity Editor is connected and path-verified.",
            result,
            resolved.project_hash,
        )

    _execute(app, action)


@cli.command("status")
@click.option("--project", type=click.Path(path_type=Path), default=Path.cwd)
@click.pass_obj
def status_command(app: AppContext, project: Path) -> None:
    def action() -> ActionResult:
        settings = app.settings()
        root = _project_root(project)
        compatibility = check_compatibility(root, settings.approved_plugin_refs)
        service_value = ServiceManager(settings).status()
        result: dict[str, Any] = {
            "service": service_value,
            "compatibility": compatibility.__dict__,
            "project": None,
        }
        project_hash = None
        if service_value["healthy"]:
            canonical = canonical_project_root(root)
            with project_lock(settings.paths, canonical, "status", 0.2):
                resolved = ProjectResolver(
                    RestClient(settings.endpoint, settings.command_timeout_seconds)
                ).resolve_once(root)
            result["project"] = _resolved_payload(resolved)
            project_hash = resolved.project_hash
        return ActionResult(
            "Project and service status inspected.", result, project_hash
        )

    _execute(app, action)


def _load_params(value: str) -> dict[str, Any]:
    raw = value
    if value.startswith("@"):
        path = Path(value[1:]).expanduser().resolve()
        try:
            raw = path.read_text(encoding="utf-8")
        except OSError as exc:
            raise UsageError(f"Cannot read params file: {exc}") from exc
    try:
        parsed = json.loads(raw)
    except ValueError as exc:
        raise UsageError(f"Params must be valid JSON: {exc}") from exc
    if not isinstance(parsed, dict):
        raise UsageError("Params JSON must be an object.")
    return parsed


@cli.command("call")
@click.argument("command_type")
@click.option("--params", default="{}", help="JSON object or @path-to-json.")
@click.option("--project", type=click.Path(path_type=Path), default=Path.cwd)
@click.option(
    "--timeout", type=float, default=None, help="Unity command timeout in seconds."
)
@click.pass_obj
def call_command(
    app: AppContext,
    command_type: str,
    params: str,
    project: Path,
    timeout: float | None,
) -> None:
    def action() -> ActionResult:
        parsed_params = _load_params(params)
        if timeout is not None and timeout <= 0:
            raise UsageError("Unity command timeout must be greater than zero.")
        settings = app.settings()
        root = _project_root(project)
        require_compatible(root, settings.approved_plugin_refs)
        ServiceManager(settings).ensure()
        canonical = canonical_project_root(root)
        with project_lock(
            settings.paths,
            canonical,
            command_type,
            settings.project_lock_timeout_seconds,
        ):
            client = RestClient(settings.endpoint, settings.command_timeout_seconds)
            resolved, _ = ensure_project_connection(
                root, settings, client, settings.bootstrap_timeout_seconds
            )
            result = client.command(
                command_type,
                parsed_params,
                resolved.project_hash,
                timeout_seconds=timeout,
            )
        return ActionResult("Unity command completed.", result, resolved.project_hash)

    _execute(app, action)


@cli.command(
    "run",
    context_settings={"ignore_unknown_options": True, "allow_extra_args": True},
)
@click.option("--project", type=click.Path(path_type=Path), default=Path.cwd)
@click.argument("upstream_args", nargs=-1, type=click.UNPROCESSED)
@click.pass_obj
def run_command(app: AppContext, project: Path, upstream_args: tuple[str, ...]) -> None:
    def action() -> ActionResult:
        if not upstream_args:
            raise UsageError("Pass an upstream unity-mcp command after '--'.")
        settings = app.settings()
        root = _project_root(project)
        require_compatible(root, settings.approved_plugin_refs)
        ServiceManager(settings).ensure()
        canonical = canonical_project_root(root)
        with project_lock(
            settings.paths,
            canonical,
            "upstream-cli",
            settings.project_lock_timeout_seconds,
        ):
            client = RestClient(settings.endpoint, settings.command_timeout_seconds)
            resolved, _ = ensure_project_connection(
                root, settings, client, settings.bootstrap_timeout_seconds
            )
            result = run_upstream_cli(settings, resolved.project_hash, upstream_args)
        return ActionResult(
            "Upstream Unity MCP CLI command completed.", result, resolved.project_hash
        )

    _execute(app, action)


@cli.command("doctor")
@click.option("--project", type=click.Path(path_type=Path), default=Path.cwd)
@click.pass_obj
def doctor_command(app: AppContext, project: Path) -> None:
    def action() -> ActionResult:
        settings = app.settings()
        root = _project_root(project)
        compatibility = check_compatibility(root, settings.approved_plugin_refs)
        service_value = ServiceManager(settings).status()
        diagnosis = {
            "service": service_value,
            "compatibility": compatibility.__dict__,
            "editor_bootstrap": bootstrap_diagnostics(
                root, settings.endpoint, settings.state_dir
            ),
            "project": None,
            "diagnosis": service_value["status"],
        }
        project_hash = None
        if not compatibility.compatible:
            diagnosis["diagnosis"] = "incompatible"
        elif service_value["healthy"]:
            try:
                canonical = canonical_project_root(root)
                with project_lock(settings.paths, canonical, "doctor", 0.2):
                    resolved = ProjectResolver(
                        RestClient(settings.endpoint, settings.command_timeout_seconds)
                    ).resolve_once(root)
                diagnosis["project"] = _resolved_payload(resolved)
                diagnosis["diagnosis"] = service_value["status"]
                project_hash = resolved.project_hash
            except UmcpError as exc:
                if exc.error_code == "project_busy":
                    diagnosis["diagnosis"] = "project-busy"
                elif isinstance(exc, ProjectError):
                    diagnosis["diagnosis"] = "editor-not-connected"
                else:
                    diagnosis["diagnosis"] = exc.error_code.replace("_", "-")
                diagnosis["project_error"] = {"message": exc.message, **exc.details}
        return ActionResult("Supervisor diagnosis completed.", diagnosis, project_hash)

    _execute(app, action)


@cli.command("_daemon", hidden=True)
@click.option("--token", required=True)
@click.pass_obj
def daemon_command(app: AppContext, token: str) -> None:
    raise click.exceptions.Exit(run_daemon(app.settings(), token))


def main() -> None:
    cli(prog_name="umcp")


def daemon_entry() -> None:
    settings = Settings.load()
    raise SystemExit(run_daemon(settings, uuid.uuid4().hex))


if __name__ == "__main__":
    main()
