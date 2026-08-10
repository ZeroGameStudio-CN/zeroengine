from __future__ import annotations

import json
import os
import secrets
import subprocess
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
from .errors import (
    IncompatibleError,
    OutcomeUnknownError,
    ProjectBusyError,
    ProjectError,
    ServiceError,
    UmcpError,
    UnityCommandError,
    UsageError,
)
from .locking import project_lock
from .project_lease import (
    acquire_project_lease,
    inspect_project_lease,
    inspect_project_lease_queue,
    release_project_lease,
    require_project_lease,
)
from .project_resolver import (
    ProjectResolver,
    canonical_project_root,
    find_project_root,
)
from .rest_client import RestClient
from .service_state import Settings
from .startup import disable_startup, enable_startup
from .supervisor import ServiceManager, run_daemon
from .test_farm import TestFarmStore, TestJobRequest
from .test_snapshot import create_snapshot, normalize_relative, scope_covers
from .test_worker import launch_workers, run_worker
from .upstream_cli import run_upstream_cli
from .workspace_control import (
    TOKEN_ENVIRONMENT_VARIABLE,
    WorkspaceCoordinator,
    bootstrap_workspace_policy,
    load_workspace_policy,
    unregister_workspace_policy,
)


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


def _lease_status_payload(settings: Settings, canonical: str) -> dict[str, Any]:
    lease = inspect_project_lease(settings.paths, canonical)
    queue = inspect_project_lease_queue(settings.paths, canonical)
    active = lease.public_payload() if lease is not None else {"active": False}
    active["queue"] = [
        {
            "owner": waiter.owner,
            "queue_order": waiter.queue_order,
            "enqueued_at": waiter.enqueued_at,
            "expires_at": waiter.expires_at,
        }
        for waiter in queue
    ]
    return active


def _workspace_coordinator(
    settings: Settings, root: Path, canonical: str
) -> WorkspaceCoordinator:
    return WorkspaceCoordinator(
        settings.paths,
        root,
        canonical,
        lease_ttl_seconds=settings.project_lease_ttl_seconds,
    )


def _read_workspace_token(
    token_file: Path | None = None, *, token_stdin: bool = False
) -> str | None:
    if token_file is not None and token_stdin:
        raise UsageError("Use either --token-file or --token-stdin, not both.")
    if token_file is not None:
        try:
            return token_file.expanduser().read_text(encoding="utf-8").strip()
        except OSError as exc:
            raise UsageError(f"Cannot read workspace token file: {exc}") from exc
    if token_stdin:
        return click.get_text_stream("stdin").read().strip()
    return os.environ.get(TOKEN_ENVIRONMENT_VARIABLE)


def _create_workspace_token_file(path: Path, token: str) -> Path:
    resolved = path.expanduser().resolve()
    try:
        resolved.parent.mkdir(parents=True, exist_ok=True)
        if os.name != "nt":
            resolved.parent.chmod(0o700)
        descriptor = os.open(resolved, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
        os.close(descriptor)
        if os.name == "nt":
            domain = os.environ.get("USERDOMAIN")
            username = os.environ.get("USERNAME")
            if not domain or not username:
                raise OSError("Current Windows identity is unavailable.")
            creationflags = getattr(subprocess, "CREATE_NO_WINDOW", 0)
            secured = subprocess.run(
                [
                    "icacls",
                    str(resolved),
                    "/inheritance:r",
                    "/grant:r",
                    f"{domain}\\{username}:(F)",
                ],
                check=False,
                capture_output=True,
                text=True,
                creationflags=creationflags,
            )
            if secured.returncode != 0:
                raise OSError("Windows owner-only ACL could not be applied.")
        else:
            resolved.chmod(0o600)
        try:
            with resolved.open("w", encoding="utf-8", newline="\n") as stream:
                stream.write(token + "\n")
                stream.flush()
                os.fsync(stream.fileno())
        except Exception:
            resolved.unlink(missing_ok=True)
            raise
    except OSError as exc:
        resolved.unlink(missing_ok=True)
        raise UsageError(f"Cannot create workspace token file: {exc}") from exc
    return resolved


def _remove_matching_workspace_token_file(path: Path, token: str) -> bool:
    resolved = path.expanduser().resolve()
    try:
        if resolved.read_text(encoding="utf-8").strip() != token:
            return False
        resolved.unlink()
        return True
    except FileNotFoundError:
        return True
    except OSError as exc:
        raise UsageError(f"Cannot remove workspace token file: {exc}") from exc


def _required_workspace_lease_id(
    settings: Settings,
    root: Path,
    canonical: str,
) -> str | None:
    policy = load_workspace_policy(root, settings.paths)
    token = _read_workspace_token()
    if policy.enforcement not in {"audit", "required"}:
        return None
    if policy.enforcement == "audit" and (not policy.valid or not token):
        return None
    if policy.enforcement == "audit":
        try:
            coordinator = _workspace_coordinator(settings, root, canonical)
            assertion = coordinator.assert_claims(token, resources=("unity-live",))
        except (IncompatibleError, ProjectBusyError, UsageError):
            return None
    else:
        coordinator = _workspace_coordinator(settings, root, canonical)
        assertion = coordinator.assert_claims(token, resources=("unity-live",))
    lease_id = assertion.get("legacy_lease_id")
    if not lease_id:
        raise ProjectBusyError(
            "The unity-live claim is not bound to a Unity task lease.",
            details={"reason": "unity-lease-binding-missing"},
        )
    return str(lease_id)


def _require_effective_project_lease(
    settings: Settings,
    canonical: str,
    lease_id: str | None,
    *,
    workspace_bound: bool,
    renew: bool = True,
) -> None:
    lease = require_project_lease(
        settings.paths,
        canonical,
        lease_id,
        settings.project_lease_ttl_seconds,
        renew=renew,
    )
    if workspace_bound and lease is None:
        raise ProjectBusyError(
            "The workspace unity-live claim lost its Unity lease binding.",
            details={"reason": "unity-lease-binding-expired"},
        )


@cli.group("test")
def test_group() -> None:
    """Run exact Unity Test Framework scopes in isolated local slots."""


@test_group.group("farm")
def test_farm_group() -> None:
    """Provision and inspect the machine-local isolated test farm."""


@test_farm_group.command("provision")
@click.option("--workers", type=click.IntRange(min=1), required=True)
@click.option("--slot-root", type=click.Path(path_type=Path), default=None)
@click.pass_obj
def test_farm_provision(app: AppContext, workers: int, slot_root: Path | None) -> None:
    def action() -> ActionResult:
        value = TestFarmStore(app.settings().paths).provision(workers, slot_root)
        return ActionResult("Unity test farm provisioned.", value)

    _execute(app, action)


@test_farm_group.command("status")
@click.pass_obj
def test_farm_status(app: AppContext) -> None:
    def action() -> ActionResult:
        store = TestFarmStore(app.settings().paths)
        recovered = store.recover_dead_workers()
        value = store.status()
        value["recovered_jobs"] = recovered
        return ActionResult("Unity test farm status inspected.", value)

    _execute(app, action)


@test_farm_group.command("watch")
@click.option("--interval", type=click.FloatRange(min=0.1), default=2.0)
@click.pass_obj
def test_farm_watch(app: AppContext, interval: float) -> None:
    try:
        while True:
            store = TestFarmStore(app.settings().paths)
            recovered = store.recover_dead_workers()
            value = store.status()
            value["recovered_jobs"] = recovered
            _emit(
                app,
                {
                    "ok": True,
                    "code": "ok",
                    "message": "Unity test farm status inspected.",
                    "project_hash": None,
                    "endpoint": app.settings().endpoint,
                    "duration_ms": 0,
                    "result": value,
                },
            )
            time.sleep(interval)
    except KeyboardInterrupt:
        return


def _test_job_result(value: dict[str, Any]) -> ActionResult:
    if value["state"] != "passed":
        raise UnityCommandError(
            f"Unity test job ended as {value['state']}.", details=value
        )
    return ActionResult("Unity test job passed.", value)


@test_group.command("submit")
@click.option("--project", type=click.Path(path_type=Path), default=Path.cwd)
@click.option(
    "--platform", type=click.Choice(["EditMode", "PlayMode"]), default="EditMode"
)
@click.option("--test-filter", multiple=True)
@click.option("--category", multiple=True)
@click.option("--assembly", multiple=True)
@click.option("--overlay-path", multiple=True)
@click.option("--baseline-only", is_flag=True)
@click.option("--external-state-safe", is_flag=True)
@click.option(
    "--timeout", "timeout_seconds", type=click.FloatRange(min=1), default=900.0
)
@click.option("--wait", "wait_for_result", is_flag=True)
@click.option("--wait-timeout", type=click.FloatRange(min=0), default=1800.0)
@click.option("--token-file", type=click.Path(path_type=Path), default=None)
@click.option("--token-stdin", is_flag=True)
@click.pass_obj
def test_submit(
    app: AppContext,
    project: Path,
    platform: str,
    test_filter: tuple[str, ...],
    category: tuple[str, ...],
    assembly: tuple[str, ...],
    overlay_path: tuple[str, ...],
    baseline_only: bool,
    external_state_safe: bool,
    timeout_seconds: float,
    wait_for_result: bool,
    wait_timeout: float,
    token_file: Path | None,
    token_stdin: bool,
) -> None:
    def action() -> ActionResult:
        settings = app.settings()
        root = _project_root(project)
        store = TestFarmStore(settings.paths)
        if not external_state_safe:
            return ActionResult(
                "Unity test requires the safe serial route.",
                {
                    "route": "serial",
                    "reason": "external-state-safety-not-declared",
                    "project_root": str(root),
                },
            )
        if not store.is_provisioned():
            return ActionResult(
                "Unity test requires the safe serial route.",
                {
                    "route": "serial",
                    "reason": "test-farm-not-provisioned",
                    "project_root": str(root),
                },
            )
        if not any((test_filter, category, assembly)):
            raise UsageError(
                "Isolated tests require at least one exact test, category, or assembly filter."
            )
        policy = load_workspace_policy(root, settings.paths)
        if policy.enforcement != "required" or not policy.valid:
            raise IncompatibleError(
                "Isolated tests require a valid required workspace policy.",
                details={"policy": policy.public_payload()},
            )
        canonical = canonical_project_root(root)
        coordinator = _workspace_coordinator(settings, root, canonical)
        token = _read_workspace_token(token_file, token_stdin=token_stdin)
        with project_lock(
            settings.paths,
            canonical,
            "test-submit",
            settings.project_lock_timeout_seconds,
        ):
            assertion = coordinator.assert_claims(token)
            task_id = str(assertion["task_id"])
            write_scopes = sorted(
                {
                    scope
                    for claim in coordinator.granted_claims_for_task(token)
                    for scope in claim["write"]
                }
            )
            if baseline_only and overlay_path:
                raise UsageError(
                    "Use either --baseline-only or --overlay-path, not both."
                )
            if not baseline_only and (not write_scopes or not overlay_path):
                raise UsageError(
                    "Submit exact --overlay-path values under a granted write claim, "
                    "or use --baseline-only."
                )
            normalized_overlay = tuple(
                normalize_relative(root, value) for value in overlay_path
            )
            for value in normalized_overlay:
                if not any(scope_covers(scope, value) for scope in write_scopes):
                    raise UsageError(
                        f"Overlay path is outside the task write claims: {value}"
                    )
            artifact_root = (
                settings.paths.test_farm_artifacts / f"snapshot-{uuid.uuid4().hex[:16]}"
            )
            try:
                snapshot = create_snapshot(
                    root,
                    artifact_root,
                    () if baseline_only else write_scopes,
                    () if baseline_only else normalized_overlay,
                    baseline_only=baseline_only,
                )
            except (ProjectBusyError, UsageError) as exc:
                return ActionResult(
                    "Unity test requires the safe serial route.",
                    {
                        "route": "serial",
                        "reason": "isolated-snapshot-unavailable",
                        "detail": exc.message,
                        "project_root": str(root),
                    },
                )
            job = store.submit(
                TestJobRequest(
                    project_root=canonical,
                    task_id=task_id,
                    platform=platform,
                    filters=test_filter,
                    categories=category,
                    assemblies=assembly,
                    artifact_root=str(artifact_root),
                    snapshot_id=snapshot["snapshot_id"],
                    snapshot_manifest=snapshot["manifest"],
                    timeout_seconds=timeout_seconds,
                )
            )
        launch_workers(settings.paths, store.status()["workers"])
        if wait_for_result:
            return _test_job_result(store.wait(job["job_id"], wait_timeout))
        job["route"] = "isolated"
        return ActionResult("Unity test job submitted.", job)

    _execute(app, action)


@test_group.command("status")
@click.option("--job", "job_id", required=True)
@click.pass_obj
def test_status(app: AppContext, job_id: str) -> None:
    def action() -> ActionResult:
        store = TestFarmStore(app.settings().paths)
        store.recover_dead_workers()
        return ActionResult("Unity test job status inspected.", store.job(job_id))

    _execute(app, action)


@test_group.command("wait")
@click.option("--job", "job_id", required=True)
@click.option(
    "--timeout", "timeout_seconds", type=click.FloatRange(min=0), default=1800.0
)
@click.pass_obj
def test_wait(app: AppContext, job_id: str, timeout_seconds: float) -> None:
    def action() -> ActionResult:
        value = TestFarmStore(app.settings().paths).wait(job_id, timeout_seconds)
        return _test_job_result(value)

    _execute(app, action)


@test_group.command("cancel")
@click.option("--job", "job_id", required=True)
@click.option("--token-file", type=click.Path(path_type=Path), default=None)
@click.option("--token-stdin", is_flag=True)
@click.pass_obj
def test_cancel(
    app: AppContext, job_id: str, token_file: Path | None, token_stdin: bool
) -> None:
    def action() -> ActionResult:
        settings = app.settings()
        store = TestFarmStore(settings.paths)
        job = store.job(job_id)
        root = _project_root(Path(job["project_root"]))
        coordinator = _workspace_coordinator(
            settings, root, canonical_project_root(root)
        )
        assertion = coordinator.authenticate_task_token(
            _read_workspace_token(token_file, token_stdin=token_stdin),
            expected_task_id=str(job["task_id"]),
        )
        value = store.cancel(job_id, str(assertion["task_id"]))
        return ActionResult("Unity test job cancellation requested.", value)

    _execute(app, action)


@test_group.command("_worker", hidden=True)
@click.pass_obj
def test_worker(app: AppContext) -> None:
    def action() -> ActionResult:
        count = run_worker(app.settings().paths)
        return ActionResult(
            "Unity test worker drained its queue.", {"completed": count}
        )

    _execute(app, action)


@cli.group("workspace")
def workspace_group() -> None:
    """Coordinate project tasks, write scopes, freezes, and shared resources."""


@workspace_group.command("bootstrap")
@click.option("--project", type=click.Path(path_type=Path), default=Path.cwd)
@click.pass_obj
def workspace_bootstrap(app: AppContext, project: Path) -> None:
    """Register a Unity project without modifying its files."""

    def action() -> ActionResult:
        settings = app.settings()
        root = _project_root(project)
        result = bootstrap_workspace_policy(settings.paths, root)
        return ActionResult("Workspace project bootstrapped.", result)

    _execute(app, action)


@workspace_group.command("unregister")
@click.option("--project", type=click.Path(path_type=Path), default=Path.cwd)
@click.pass_obj
def workspace_unregister(app: AppContext, project: Path) -> None:
    """Remove only the user-level workspace registration."""

    def action() -> ActionResult:
        settings = app.settings()
        root = _project_root(project)
        result = unregister_workspace_policy(
            settings.paths,
            root,
            lease_ttl_seconds=settings.project_lease_ttl_seconds,
        )
        return ActionResult("Workspace project registration removed.", result)

    _execute(app, action)


@workspace_group.group("task")
def workspace_task_group() -> None:
    """Create, renew, and finish workspace tasks."""


@workspace_task_group.command("start")
@click.option("--project", type=click.Path(path_type=Path), default=Path.cwd)
@click.option("--owner", required=True)
@click.option("--summary", required=True)
@click.option("--task-uri", default=None)
@click.option("--ttl", type=float, default=None)
@click.option("--token-file", type=click.Path(path_type=Path), default=None)
@click.pass_obj
def workspace_task_start(
    app: AppContext,
    project: Path,
    owner: str,
    summary: str,
    task_uri: str | None,
    ttl: float | None,
    token_file: Path | None,
) -> None:
    def action() -> ActionResult:
        settings = app.settings()
        root = _project_root(project)
        canonical = canonical_project_root(root)
        coordinator = _workspace_coordinator(settings, root, canonical)
        token = secrets.token_urlsafe(32) if token_file is not None else None
        resolved_token_file = None
        if token_file is not None and token is not None:
            resolved_token_file = _create_workspace_token_file(token_file, token)
        try:
            result = coordinator.start_task(
                owner=owner,
                summary=summary,
                task_uri=task_uri,
                ttl_seconds=(
                    ttl if ttl is not None else settings.project_lease_ttl_seconds
                ),
                task_token=token,
            )
        except Exception:
            if resolved_token_file is not None and token is not None:
                _remove_matching_workspace_token_file(resolved_token_file, token)
            raise
        if resolved_token_file is not None:
            result = dict(result)
            result.pop("task_token", None)
            result["token_file"] = str(resolved_token_file)
        return ActionResult("Workspace task started.", result)

    _execute(app, action)


@workspace_task_group.command("heartbeat")
@click.option("--project", type=click.Path(path_type=Path), default=Path.cwd)
@click.option("--phase", required=True)
@click.option("--note", default=None)
@click.option("--ttl", type=float, default=None)
@click.option("--token-file", type=click.Path(path_type=Path), default=None)
@click.option("--token-stdin", is_flag=True)
@click.pass_obj
def workspace_task_heartbeat(
    app: AppContext,
    project: Path,
    phase: str,
    note: str | None,
    ttl: float | None,
    token_file: Path | None,
    token_stdin: bool,
) -> None:
    def action() -> ActionResult:
        settings = app.settings()
        root = _project_root(project)
        canonical = canonical_project_root(root)
        result = _workspace_coordinator(settings, root, canonical).heartbeat(
            _read_workspace_token(token_file, token_stdin=token_stdin),
            phase=phase,
            note=note,
            ttl_seconds=ttl if ttl is not None else settings.project_lease_ttl_seconds,
        )
        return ActionResult("Workspace task heartbeat renewed.", result)

    _execute(app, action)


@workspace_task_group.command("release")
@click.option("--project", type=click.Path(path_type=Path), default=Path.cwd)
@click.option("--result", type=click.Choice(["completed", "failed"]), required=True)
@click.option("--token-file", type=click.Path(path_type=Path), default=None)
@click.option("--token-stdin", is_flag=True)
@click.pass_obj
def workspace_task_release(
    app: AppContext,
    project: Path,
    result: str,
    token_file: Path | None,
    token_stdin: bool,
) -> None:
    def action() -> ActionResult:
        settings = app.settings()
        root = _project_root(project)
        canonical = canonical_project_root(root)
        token = _read_workspace_token(token_file, token_stdin=token_stdin)
        value = _workspace_coordinator(settings, root, canonical).release_task(
            token, result=result
        )
        if token_file is not None and token is not None:
            value["token_file_removed"] = _remove_matching_workspace_token_file(
                token_file, token
            )
        return ActionResult("Workspace task released.", value)

    _execute(app, action)


@workspace_task_group.command("cleanup-idle")
@click.option("--project", type=click.Path(path_type=Path), default=Path.cwd)
@click.option("--task-id", required=True)
@click.pass_obj
def workspace_task_cleanup_idle(app: AppContext, project: Path, task_id: str) -> None:
    def action() -> ActionResult:
        settings = app.settings()
        root = _project_root(project)
        canonical = canonical_project_root(root)
        value = _workspace_coordinator(settings, root, canonical).cleanup_idle_task(
            task_id
        )
        return ActionResult("Idle workspace task cleaned.", value)

    _execute(app, action)


@workspace_group.group("claim")
def workspace_claim_group() -> None:
    """Acquire, inspect, and release workspace claims."""


@workspace_claim_group.command("acquire")
@click.option("--project", type=click.Path(path_type=Path), default=Path.cwd)
@click.option("--write", multiple=True)
@click.option(
    "--resource",
    multiple=True,
    type=click.Choice(["unity-live", "vcs-maintenance"]),
)
@click.option("--wait", type=float, default=0.0)
@click.option("--keep-queued", is_flag=True)
@click.option("--token-file", type=click.Path(path_type=Path), default=None)
@click.option("--token-stdin", is_flag=True)
@click.pass_obj
def workspace_claim_acquire(
    app: AppContext,
    project: Path,
    write: tuple[str, ...],
    resource: tuple[str, ...],
    wait: float,
    keep_queued: bool,
    token_file: Path | None,
    token_stdin: bool,
) -> None:
    def action() -> ActionResult:
        settings = app.settings()
        root = _project_root(project)
        canonical = canonical_project_root(root)
        value = _workspace_coordinator(settings, root, canonical).acquire_claim(
            _read_workspace_token(token_file, token_stdin=token_stdin),
            writes=write,
            resources=resource,
            wait_seconds=wait,
            keep_queued=keep_queued,
        )
        return ActionResult("Workspace claim evaluated.", value)

    _execute(app, action)


@workspace_claim_group.command("release")
@click.option("--project", type=click.Path(path_type=Path), default=Path.cwd)
@click.option("--claim-id", required=True)
@click.option("--token-file", type=click.Path(path_type=Path), default=None)
@click.option("--token-stdin", is_flag=True)
@click.pass_obj
def workspace_claim_release(
    app: AppContext,
    project: Path,
    claim_id: str,
    token_file: Path | None,
    token_stdin: bool,
) -> None:
    def action() -> ActionResult:
        settings = app.settings()
        root = _project_root(project)
        canonical = canonical_project_root(root)
        value = _workspace_coordinator(settings, root, canonical).release_claim(
            _read_workspace_token(token_file, token_stdin=token_stdin), claim_id
        )
        return ActionResult("Workspace claim released.", value)

    _execute(app, action)


@workspace_claim_group.command("dry-run")
@click.option("--project", type=click.Path(path_type=Path), default=Path.cwd)
@click.option("--write", multiple=True)
@click.option(
    "--resource",
    multiple=True,
    type=click.Choice(["unity-live", "vcs-maintenance"]),
)
@click.option("--freeze", is_flag=True)
@click.option("--token-file", type=click.Path(path_type=Path), default=None)
@click.option("--token-stdin", is_flag=True)
@click.pass_obj
def workspace_claim_dry_run(
    app: AppContext,
    project: Path,
    write: tuple[str, ...],
    resource: tuple[str, ...],
    freeze: bool,
    token_file: Path | None,
    token_stdin: bool,
) -> None:
    def action() -> ActionResult:
        settings = app.settings()
        root = _project_root(project)
        canonical = canonical_project_root(root)
        value = _workspace_coordinator(settings, root, canonical).dry_run(
            _read_workspace_token(token_file, token_stdin=token_stdin),
            writes=write,
            resources=resource,
            freeze=freeze,
        )
        return ActionResult("Workspace claim dry-run completed.", value)

    _execute(app, action)


@workspace_group.group("queue")
def workspace_queue_group() -> None:
    """Manage persistent queued claims."""


@workspace_queue_group.command("cancel")
@click.option("--project", type=click.Path(path_type=Path), default=Path.cwd)
@click.option("--claim-id", required=True)
@click.option("--token-file", type=click.Path(path_type=Path), default=None)
@click.option("--token-stdin", is_flag=True)
@click.pass_obj
def workspace_queue_cancel(
    app: AppContext,
    project: Path,
    claim_id: str,
    token_file: Path | None,
    token_stdin: bool,
) -> None:
    def action() -> ActionResult:
        settings = app.settings()
        root = _project_root(project)
        canonical = canonical_project_root(root)
        value = _workspace_coordinator(settings, root, canonical).cancel_claim(
            _read_workspace_token(token_file, token_stdin=token_stdin), claim_id
        )
        return ActionResult("Queued workspace claim cancelled.", value)

    _execute(app, action)


@workspace_group.group("freeze")
def workspace_freeze_group() -> None:
    """Acquire a fair whole-workspace mutation barrier."""


@workspace_freeze_group.command("acquire")
@click.option("--project", type=click.Path(path_type=Path), default=Path.cwd)
@click.option("--wait", type=float, default=0.0)
@click.option("--keep-queued", is_flag=True)
@click.option("--token-file", type=click.Path(path_type=Path), default=None)
@click.option("--token-stdin", is_flag=True)
@click.pass_obj
def workspace_freeze_acquire(
    app: AppContext,
    project: Path,
    wait: float,
    keep_queued: bool,
    token_file: Path | None,
    token_stdin: bool,
) -> None:
    def action() -> ActionResult:
        settings = app.settings()
        root = _project_root(project)
        canonical = canonical_project_root(root)
        value = _workspace_coordinator(settings, root, canonical).acquire_claim(
            _read_workspace_token(token_file, token_stdin=token_stdin),
            freeze=True,
            wait_seconds=wait,
            keep_queued=keep_queued,
        )
        return ActionResult("Workspace freeze evaluated.", value)

    _execute(app, action)


@workspace_group.command("assert")
@click.option("--project", type=click.Path(path_type=Path), default=Path.cwd)
@click.option("--write", multiple=True)
@click.option(
    "--resource",
    multiple=True,
    type=click.Choice(["unity-live", "vcs-maintenance"]),
)
@click.option("--freeze", is_flag=True)
@click.option("--token-file", type=click.Path(path_type=Path), default=None)
@click.option("--token-stdin", is_flag=True)
@click.pass_obj
def workspace_assert(
    app: AppContext,
    project: Path,
    write: tuple[str, ...],
    resource: tuple[str, ...],
    freeze: bool,
    token_file: Path | None,
    token_stdin: bool,
) -> None:
    def action() -> ActionResult:
        settings = app.settings()
        root = _project_root(project)
        canonical = canonical_project_root(root)
        value = _workspace_coordinator(settings, root, canonical).assert_claims(
            _read_workspace_token(token_file, token_stdin=token_stdin),
            writes=write,
            resources=resource,
            freeze=freeze,
        )
        value.pop("legacy_lease_id", None)
        return ActionResult("Workspace claim assertion passed.", value)

    _execute(app, action)


def _workspace_status_value(
    settings: Settings, root: Path, *, refresh_vcs: bool
) -> dict[str, Any]:
    canonical = canonical_project_root(root)
    try:
        coordinator = _workspace_coordinator(settings, root, canonical)
    except IncompatibleError as exc:
        return {
            "schema_version": None,
            "project_root": str(root),
            "policy": load_workspace_policy(root, settings.paths).public_payload(),
            "workspace_epoch": None,
            "tasks": [],
            "claims": [],
            "freeze": None,
            "vcs": {
                "observation_id": None,
                "observed_at": None,
                "pending_count": None,
                "stale": True,
                "pending": [],
            },
            "coordination_error": {"message": exc.message, **exc.details},
            "unity_lease": _lease_status_payload(settings, canonical),
        }
    refresh_error = None
    if refresh_vcs:
        try:
            coordinator.reconcile_plastic()
        except UmcpError as exc:
            refresh_error = {"message": exc.message, **exc.details}
    value = coordinator.status()
    if refresh_error is not None:
        value["coordination_error"] = {
            "reason": "vcs-refresh-failed",
            **refresh_error,
        }
    value["unity_lease"] = _lease_status_payload(settings, canonical)
    return value


@workspace_group.command("status")
@click.option("--project", type=click.Path(path_type=Path), default=Path.cwd)
@click.option("--human", "local_human", is_flag=True)
@click.option("--refresh-vcs", is_flag=True)
@click.pass_obj
def workspace_status(
    app: AppContext,
    project: Path,
    local_human: bool,
    refresh_vcs: bool,
) -> None:
    if local_human and not app.human:
        app = AppContext(app.state_dir, app.endpoint, True)

    def action() -> ActionResult:
        settings = app.settings()
        root = _project_root(project)
        return ActionResult(
            "Workspace coordination status inspected.",
            _workspace_status_value(settings, root, refresh_vcs=refresh_vcs),
        )

    _execute(app, action)


@workspace_group.command("watch")
@click.option("--project", type=click.Path(path_type=Path), default=Path.cwd)
@click.option("--human", "local_human", is_flag=True)
@click.option("--interval", type=float, default=2.0)
@click.option("--vcs-refresh-interval", type=float, default=30.0)
@click.pass_obj
def workspace_watch(
    app: AppContext,
    project: Path,
    local_human: bool,
    interval: float,
    vcs_refresh_interval: float,
) -> None:
    if interval <= 0 or vcs_refresh_interval <= 0:
        raise click.UsageError("Watch intervals must be greater than zero.")
    if local_human and not app.human:
        app = AppContext(app.state_dir, app.endpoint, True)
    settings = app.settings()
    root = _project_root(project)
    last_vcs_refresh = 0.0
    try:
        while True:
            refresh_vcs = time.monotonic() - last_vcs_refresh >= vcs_refresh_interval
            if refresh_vcs:
                last_vcs_refresh = time.monotonic()
            value = _workspace_status_value(settings, root, refresh_vcs=refresh_vcs)
            envelope = {
                "ok": True,
                "code": "ok",
                "message": "Workspace coordination status inspected.",
                "project_hash": None,
                "endpoint": settings.endpoint,
                "duration_ms": 0,
                "result": value,
            }
            _emit(app, envelope)
            time.sleep(interval)
    except KeyboardInterrupt:
        return


@workspace_group.group("baseline")
def workspace_baseline_group() -> None:
    """Import and classify pre-existing Plastic pending paths."""


@workspace_baseline_group.command("import-plastic")
@click.option("--project", type=click.Path(path_type=Path), default=Path.cwd)
@click.pass_obj
def workspace_baseline_import(app: AppContext, project: Path) -> None:
    def action() -> ActionResult:
        settings = app.settings()
        root = _project_root(project)
        canonical = canonical_project_root(root)
        value = _workspace_coordinator(
            settings, root, canonical
        ).import_plastic_baseline()
        return ActionResult("Plastic pending baseline imported.", value)

    _execute(app, action)


@workspace_baseline_group.command("disposition")
@click.option("--project", type=click.Path(path_type=Path), default=Path.cwd)
@click.option(
    "--kind",
    type=click.Choice(["adopt", "protect", "resolved-clean", "submitted"]),
    required=True,
)
@click.option("--write", multiple=True, required=True)
@click.option("--evidence", default=None)
@click.option("--token-file", type=click.Path(path_type=Path), default=None)
@click.option("--token-stdin", is_flag=True)
@click.pass_obj
def workspace_baseline_disposition(
    app: AppContext,
    project: Path,
    kind: str,
    write: tuple[str, ...],
    evidence: str | None,
    token_file: Path | None,
    token_stdin: bool,
) -> None:
    def action() -> ActionResult:
        settings = app.settings()
        root = _project_root(project)
        canonical = canonical_project_root(root)
        value = _workspace_coordinator(settings, root, canonical).set_disposition(
            _read_workspace_token(token_file, token_stdin=token_stdin),
            kind=kind,
            writes=write,
            evidence=evidence,
        )
        return ActionResult("Plastic baseline disposition recorded.", value)

    _execute(app, action)


@workspace_group.command("reconcile-plastic")
@click.option("--project", type=click.Path(path_type=Path), default=Path.cwd)
@click.pass_obj
def workspace_reconcile_plastic(app: AppContext, project: Path) -> None:
    def action() -> ActionResult:
        settings = app.settings()
        root = _project_root(project)
        canonical = canonical_project_root(root)
        value = _workspace_coordinator(settings, root, canonical).reconcile_plastic()
        return ActionResult("Plastic pending state reconciled.", value)

    _execute(app, action)


@workspace_group.group("recovery")
def workspace_recovery_group() -> None:
    """Resolve fenced unknown outcomes with explicit evidence."""


@workspace_recovery_group.command("resolve-unknown")
@click.option("--project", type=click.Path(path_type=Path), default=Path.cwd)
@click.option("--task-id", required=True)
@click.option(
    "--disposition",
    type=click.Choice(["applied", "not-applied", "contained"]),
    required=True,
)
@click.option("--evidence", required=True)
@click.pass_obj
def workspace_recovery_resolve_unknown(
    app: AppContext,
    project: Path,
    task_id: str,
    disposition: str,
    evidence: str,
) -> None:
    def action() -> ActionResult:
        settings = app.settings()
        root = _project_root(project)
        canonical = canonical_project_root(root)
        value = _workspace_coordinator(settings, root, canonical).resolve_unknown(
            task_id=task_id,
            disposition=disposition,
            evidence=evidence,
        )
        return ActionResult("Unknown workspace outcome resolved.", value)

    _execute(app, action)


@cli.group("lease")
def lease_group() -> None:
    """Coordinate one live-operation owner for a complete project task."""


@lease_group.command("acquire")
@click.option("--project", type=click.Path(path_type=Path), default=Path.cwd)
@click.option("--owner", required=True, help="Short task owner label for diagnostics.")
@click.option(
    "--wait", type=float, default=None, help="Maximum lease queue wait in seconds."
)
@click.option("--ttl", type=float, default=None, help="Lease lifetime in seconds.")
@click.pass_obj
def lease_acquire(
    app: AppContext,
    project: Path,
    owner: str,
    wait: float | None,
    ttl: float | None,
) -> None:
    def action() -> ActionResult:
        settings = app.settings()
        root = _project_root(project)
        canonical = canonical_project_root(root)
        policy = load_workspace_policy(root, settings.paths)
        if policy.enforcement == "required":
            claim = _workspace_coordinator(settings, root, canonical).acquire_claim(
                _read_workspace_token(),
                resources=("unity-live",),
                wait_seconds=wait
                if wait is not None
                else settings.project_lock_timeout_seconds,
            )
            if claim["state"] != "granted":
                raise ProjectBusyError(
                    "Unity live workspace claim was not granted.", details=claim
                )
            return ActionResult("Unity live workspace claim acquired.", claim)
        lease = acquire_project_lease(
            settings.paths,
            canonical,
            owner,
            ttl if ttl is not None else settings.project_lease_ttl_seconds,
            wait if wait is not None else settings.project_lock_timeout_seconds,
        )
        return ActionResult("Project task lease acquired.", lease.private_payload())

    _execute(app, action)


@lease_group.command("status")
@click.option("--project", type=click.Path(path_type=Path), default=Path.cwd)
@click.pass_obj
def lease_status(app: AppContext, project: Path) -> None:
    def action() -> ActionResult:
        settings = app.settings()
        root = _project_root(project)
        canonical = canonical_project_root(root)
        return ActionResult(
            "Project task lease inspected.",
            _lease_status_payload(settings, canonical),
        )

    _execute(app, action)


@lease_group.command("renew")
@click.option("--project", type=click.Path(path_type=Path), default=Path.cwd)
@click.option(
    "--lease-id", envvar="UMCP_PROJECT_LEASE_ID", default=None, hide_input=True
)
@click.option("--ttl", type=float, default=None, help="Lease lifetime in seconds.")
@click.pass_obj
def lease_renew(
    app: AppContext, project: Path, lease_id: str | None, ttl: float | None
) -> None:
    def action() -> ActionResult:
        settings = app.settings()
        root = _project_root(project)
        canonical = canonical_project_root(root)
        workspace_lease_id = _required_workspace_lease_id(settings, root, canonical)
        effective_lease_id = workspace_lease_id or lease_id
        if not effective_lease_id:
            raise UsageError("Project lease ID must not be empty.")
        lease = require_project_lease(
            settings.paths,
            canonical,
            effective_lease_id,
            ttl if ttl is not None else settings.project_lease_ttl_seconds,
        )
        if lease is None:
            raise UsageError("No active project task lease exists to renew.")
        result = (
            lease.public_payload()
            if workspace_lease_id is not None
            else lease.private_payload()
        )
        return ActionResult("Project task lease renewed.", result)

    _execute(app, action)


@lease_group.command("release")
@click.option("--project", type=click.Path(path_type=Path), default=Path.cwd)
@click.option(
    "--lease-id", envvar="UMCP_PROJECT_LEASE_ID", default=None, hide_input=True
)
@click.pass_obj
def lease_release(app: AppContext, project: Path, lease_id: str | None) -> None:
    def action() -> ActionResult:
        settings = app.settings()
        root = _project_root(project)
        canonical = canonical_project_root(root)
        workspace_lease_id = _required_workspace_lease_id(settings, root, canonical)
        if workspace_lease_id is not None:
            coordinator = _workspace_coordinator(settings, root, canonical)
            assertion = coordinator.assert_claims(
                _read_workspace_token(), resources=("unity-live",)
            )
            claim_id = assertion["resource_claim_ids"]["unity-live"]
            value = coordinator.release_claim(_read_workspace_token(), claim_id)
            return ActionResult("Unity live workspace claim released.", value)
        if not lease_id:
            raise UsageError("Project lease ID must not be empty.")
        released = release_project_lease(settings.paths, canonical, lease_id)
        return ActionResult(
            "Project task lease released." if released else "No active lease remained.",
            {"released": released},
        )

    _execute(app, action)


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
@click.option(
    "--lease-id", envvar="UMCP_PROJECT_LEASE_ID", default=None, hide_input=True
)
@click.pass_obj
def connect_command(
    app: AppContext,
    project: Path,
    timeout: float | None,
    restart_editor: bool,
    lease_id: str | None,
) -> None:
    def action() -> ActionResult:
        settings = app.settings()
        root = _project_root(project)
        compatibility = require_compatible(root, settings.approved_plugin_refs)
        canonical = canonical_project_root(root)
        workspace_lease_id = _required_workspace_lease_id(settings, root, canonical)
        effective_lease_id = workspace_lease_id or lease_id
        _require_effective_project_lease(
            settings,
            canonical,
            effective_lease_id,
            workspace_bound=workspace_lease_id is not None,
            renew=False,
        )
        ServiceManager(settings).ensure()
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
            _require_effective_project_lease(
                settings,
                canonical,
                effective_lease_id,
                workspace_bound=workspace_lease_id is not None,
            )
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
            _required_workspace_lease_id(settings, root, canonical)
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
            "task_lease": _lease_status_payload(settings, canonical_project_root(root)),
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
@click.option(
    "--lease-id", envvar="UMCP_PROJECT_LEASE_ID", default=None, hide_input=True
)
@click.pass_obj
def call_command(
    app: AppContext,
    command_type: str,
    params: str,
    project: Path,
    timeout: float | None,
    lease_id: str | None,
) -> None:
    def action() -> ActionResult:
        parsed_params = _load_params(params)
        if timeout is not None and timeout <= 0:
            raise UsageError("Unity command timeout must be greater than zero.")
        settings = app.settings()
        root = _project_root(project)
        require_compatible(root, settings.approved_plugin_refs)
        canonical = canonical_project_root(root)
        workspace_lease_id = _required_workspace_lease_id(settings, root, canonical)
        effective_lease_id = workspace_lease_id or lease_id
        _require_effective_project_lease(
            settings,
            canonical,
            effective_lease_id,
            workspace_bound=workspace_lease_id is not None,
            renew=False,
        )
        ServiceManager(settings).ensure()
        try:
            with project_lock(
                settings.paths,
                canonical,
                command_type,
                settings.project_lock_timeout_seconds,
            ):
                _require_effective_project_lease(
                    settings,
                    canonical,
                    effective_lease_id,
                    workspace_bound=workspace_lease_id is not None,
                )
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
                _required_workspace_lease_id(settings, root, canonical)
        except OutcomeUnknownError:
            if workspace_lease_id is not None:
                _workspace_coordinator(settings, root, canonical).heartbeat(
                    _read_workspace_token(),
                    phase="outcome_unknown",
                    note=f"Unity command outcome unknown: {command_type}",
                    ttl_seconds=settings.project_lease_ttl_seconds,
                )
            raise
        return ActionResult("Unity command completed.", result, resolved.project_hash)

    _execute(app, action)


@cli.command(
    "run",
    context_settings={"ignore_unknown_options": True, "allow_extra_args": True},
)
@click.option("--project", type=click.Path(path_type=Path), default=Path.cwd)
@click.option(
    "--lease-id", envvar="UMCP_PROJECT_LEASE_ID", default=None, hide_input=True
)
@click.argument("upstream_args", nargs=-1, type=click.UNPROCESSED)
@click.pass_obj
def run_command(
    app: AppContext,
    project: Path,
    lease_id: str | None,
    upstream_args: tuple[str, ...],
) -> None:
    def action() -> ActionResult:
        if not upstream_args:
            raise UsageError("Pass an upstream unity-mcp command after '--'.")
        settings = app.settings()
        root = _project_root(project)
        require_compatible(root, settings.approved_plugin_refs)
        canonical = canonical_project_root(root)
        workspace_lease_id = _required_workspace_lease_id(settings, root, canonical)
        effective_lease_id = workspace_lease_id or lease_id
        _require_effective_project_lease(
            settings,
            canonical,
            effective_lease_id,
            workspace_bound=workspace_lease_id is not None,
            renew=False,
        )
        ServiceManager(settings).ensure()
        try:
            with project_lock(
                settings.paths,
                canonical,
                "upstream-cli",
                settings.project_lock_timeout_seconds,
            ):
                _require_effective_project_lease(
                    settings,
                    canonical,
                    effective_lease_id,
                    workspace_bound=workspace_lease_id is not None,
                )
                client = RestClient(settings.endpoint, settings.command_timeout_seconds)
                resolved, _ = ensure_project_connection(
                    root, settings, client, settings.bootstrap_timeout_seconds
                )
                result = run_upstream_cli(
                    settings, resolved.project_hash, upstream_args
                )
                _required_workspace_lease_id(settings, root, canonical)
        except OutcomeUnknownError:
            if workspace_lease_id is not None:
                _workspace_coordinator(settings, root, canonical).heartbeat(
                    _read_workspace_token(),
                    phase="outcome_unknown",
                    note="Upstream Unity CLI outcome unknown",
                    ttl_seconds=settings.project_lease_ttl_seconds,
                )
            raise
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
            "task_lease": _lease_status_payload(settings, canonical_project_root(root)),
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
