from __future__ import annotations

import hashlib
import json
import os
import shutil
import subprocess
import time
import uuid
import xml.etree.ElementTree as ET
from collections.abc import Callable, Sequence
from dataclasses import dataclass
from pathlib import Path, PurePosixPath
from typing import Any

from .errors import ProjectBusyError, UsageError
from .service_state import _atomic_write, ensure_private_directory

CRITICAL_INPUTS = (
    "ProjectSettings/ProjectVersion.txt",
    "Packages/manifest.json",
    "Packages/packages-lock.json",
)


@dataclass(frozen=True)
class PendingEntry:
    path: str
    operation: str
    source_path: str | None = None
    status: str = ""


@dataclass(frozen=True)
class VcsObservation:
    kind: str
    revision: str
    repository: str
    entries: tuple[PendingEntry, ...]


CommandRunner = Callable[..., subprocess.CompletedProcess[str]]


def _run(
    command: Sequence[str], *, cwd: Path, runner: CommandRunner = subprocess.run
) -> subprocess.CompletedProcess[str]:
    return runner(
        list(command),
        cwd=cwd,
        check=False,
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
    )


def _required_command(
    command: Sequence[str],
    *,
    cwd: Path,
    label: str,
    runner: CommandRunner = subprocess.run,
) -> str:
    completed = _run(command, cwd=cwd, runner=runner)
    if completed.returncode != 0:
        raise UsageError(
            f"{label} failed.",
            details={"stderr": completed.stderr.strip(), "command": list(command)},
        )
    return completed.stdout


def normalize_relative(project_root: Path, value: str | Path) -> str:
    candidate = Path(value)
    candidate = candidate if candidate.is_absolute() else project_root / candidate
    try:
        relative = Path(os.path.abspath(candidate)).relative_to(project_root.resolve())
    except ValueError as exc:
        raise UsageError(f"Snapshot path escapes the project: {value}") from exc
    result = PurePosixPath(relative.as_posix()).as_posix().removeprefix("./")
    if not result or result == "." or ".." in PurePosixPath(result).parts:
        raise UsageError(f"Invalid snapshot path: {value}")
    return result


def _path_key(value: str) -> str:
    normalized = value.replace("\\", "/").strip("/")
    return normalized.casefold() if os.name == "nt" else normalized


def scope_covers(scope: str, path: str) -> bool:
    scope_parts = PurePosixPath(_path_key(scope)).parts
    path_parts = PurePosixPath(_path_key(path)).parts
    return (
        len(scope_parts) <= len(path_parts)
        and scope_parts == path_parts[: len(scope_parts)]
    )


def detect_vcs(project_root: Path, *, runner: CommandRunner = subprocess.run) -> str:
    if (project_root / ".plastic").is_dir():
        return "plastic"
    completed = _run(
        ["git", "rev-parse", "--show-toplevel"], cwd=project_root, runner=runner
    )
    if completed.returncode == 0:
        root = Path(completed.stdout.strip()).resolve()
        if root == project_root.resolve():
            return "git"
    raise UsageError("Unity test farm supports only a Git or Plastic workspace root.")


def observe_git(
    project_root: Path, *, runner: CommandRunner = subprocess.run
) -> VcsObservation:
    revision = _required_command(
        ["git", "rev-parse", "HEAD"],
        cwd=project_root,
        label="Git baseline observation",
        runner=runner,
    ).strip()
    unresolved = _required_command(
        ["git", "diff", "--name-only", "--diff-filter=U"],
        cwd=project_root,
        label="Git conflict observation",
        runner=runner,
    ).strip()
    if unresolved:
        raise ProjectBusyError("Git has unresolved merge conflicts.")
    status = _required_command(
        [
            "git",
            "status",
            "--porcelain=v1",
            "-z",
            "--untracked-files=all",
            "--no-renames",
        ],
        cwd=project_root,
        label="Git pending observation",
        runner=runner,
    )
    entries: list[PendingEntry] = []
    for raw in status.split("\0"):
        if not raw:
            continue
        if len(raw) < 4 or raw[2] != " ":
            raise UsageError("Git returned an unrecognized porcelain status record.")
        code, raw_path = raw[:2], raw[3:]
        path = normalize_relative(project_root, raw_path)
        if "U" in code:
            raise ProjectBusyError(f"Git path has an unresolved status: {path}")
        operation = "delete" if "D" in code else "copy"
        entries.append(PendingEntry(path, operation, status=code))
    repository = _required_command(
        ["git", "rev-parse", "--show-toplevel"],
        cwd=project_root,
        label="Git repository observation",
        runner=runner,
    ).strip()
    return VcsObservation("git", revision, repository, tuple(entries))


def observe_plastic(
    project_root: Path, *, runner: CommandRunner = subprocess.run
) -> VcsObservation:
    if (project_root / ".plastic" / "plastic.incomingchangesprogress").exists():
        raise ProjectBusyError("Plastic has an incomplete Incoming Changes operation.")
    output = _required_command(
        ["cm", "status", "--xml", "--encoding=utf-8", "--nomergesinfo"],
        cwd=project_root,
        label="Plastic pending observation",
        runner=runner,
    )
    try:
        root = ET.fromstring(output.lstrip("\ufeff"))
        revision = (root.findtext("./WorkspaceStatus/Status/Changeset") or "").strip()
        repository_name = (
            root.findtext("./WorkspaceStatus/Status/RepSpec/Name") or ""
        ).strip()
        server = (
            root.findtext("./WorkspaceStatus/Status/RepSpec/Server") or ""
        ).strip()
    except ET.ParseError as exc:
        raise UsageError("Plastic returned invalid status XML.") from exc
    if not revision or not repository_name or not server:
        raise UsageError("Plastic status omitted its baseline or repository.")
    entries: list[PendingEntry] = []
    copy_types = {"AD", "CH", "CO", "CP", "PR", "RP"}
    delete_types = {"DE", "LD"}
    move_types = {"LM", "MV"}
    for change in root.findall("./Changes/Change"):
        status = (change.findtext("Type") or "").strip().upper()
        path_text = (change.findtext("Path") or "").strip()
        old_path_text = (change.findtext("OldPath") or "").strip()
        revision_type = (change.findtext("RevisionType") or "").strip().casefold()
        if not path_text or "directory" in revision_type:
            continue
        path = normalize_relative(project_root, path_text)
        if status in copy_types:
            entries.append(PendingEntry(path, "copy", status=status))
        elif status in delete_types:
            entries.append(PendingEntry(path, "delete", status=status))
        elif status in move_types:
            if not old_path_text:
                raise UsageError(f"Plastic move omitted its source path: {path}")
            old_path = normalize_relative(project_root, old_path_text)
            entries.append(PendingEntry(path, "copy", old_path, status))
            entries.append(PendingEntry(old_path, "delete", path, status))
        else:
            raise UsageError(
                f"Unsupported Plastic pending status {status!r} for {path}."
            )
    return VcsObservation(
        "plastic", revision, f"{repository_name}@{server}", tuple(entries)
    )


def observe_vcs(
    project_root: Path, *, runner: CommandRunner = subprocess.run
) -> VcsObservation:
    kind = detect_vcs(project_root, runner=runner)
    return (
        observe_git(project_root, runner=runner)
        if kind == "git"
        else observe_plastic(project_root, runner=runner)
    )


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _copy_stable(source: Path, destination: Path) -> dict[str, Any]:
    if source.is_symlink():
        raise UsageError(f"Snapshot overlays do not support symbolic links: {source}")
    for attempt in range(2):
        try:
            before = source.stat()
            before_hash = sha256_file(source)
            ensure_private_directory(destination.parent)
            shutil.copy2(source, destination)
            after = source.stat()
            after_hash = sha256_file(source)
            copied_hash = sha256_file(destination)
        except FileNotFoundError:
            if attempt == 0:
                continue
            raise ProjectBusyError(f"Snapshot source disappeared: {source}") from None
        if (
            before.st_size == after.st_size
            and before.st_mtime_ns == after.st_mtime_ns
            and before_hash == after_hash == copied_hash
        ):
            return {"size": after.st_size, "sha256": after_hash}
        destination.unlink(missing_ok=True)
    raise ProjectBusyError(f"Snapshot source changed while copying: {source}")


def _reject_symlink_path(project_root: Path, relative: str) -> None:
    current = project_root
    for part in PurePosixPath(relative).parts:
        current /= part
        if current.is_symlink():
            raise UsageError(
                f"Snapshot overlays do not support symbolic links: {relative}"
            )
    try:
        current.resolve(strict=True).relative_to(project_root.resolve())
    except (FileNotFoundError, ValueError) as exc:
        raise ProjectBusyError(
            f"Pending snapshot file is unavailable: {relative}"
        ) from exc


def _critical_fingerprints(project_root: Path) -> dict[str, dict[str, Any] | None]:
    result: dict[str, dict[str, Any] | None] = {}
    for relative in CRITICAL_INPUTS:
        path = project_root / relative
        result[relative] = (
            {"size": path.stat().st_size, "sha256": sha256_file(path)}
            if path.is_file()
            else None
        )
    return result


def _reject_mutable_file_dependencies(project_root: Path) -> None:
    for relative in ("Packages/manifest.json", "Packages/packages-lock.json"):
        path = project_root / relative
        if not path.is_file():
            continue
        try:
            value = json.loads(path.read_text(encoding="utf-8-sig"))
        except (OSError, json.JSONDecodeError) as exc:
            raise UsageError(
                f"Cannot validate Unity package input {relative}."
            ) from exc
        if "file:" in json.dumps(value, sort_keys=True):
            raise UsageError(
                f"Isolated tests refuse mutable file: dependencies in {relative}."
            )


def _validate_meta_pairs(entries: Sequence[PendingEntry]) -> None:
    by_path = {(_path_key(entry.path), entry.operation): entry for entry in entries}
    new_statuses = {"??", "A", "AD", "LM", "MV", "PR"}
    for entry in entries:
        key = _path_key(entry.path)
        if not key.startswith("assets/") or key.endswith(".meta"):
            continue
        requires_pair = (
            entry.operation == "delete" or entry.status.strip() in new_statuses
        )
        if not requires_pair:
            continue
        pair_key = f"{key}.meta"
        if (pair_key, entry.operation) not in by_path:
            raise UsageError(
                f"Unity asset {entry.path} is missing its {entry.operation} .meta pair."
            )


def create_snapshot(
    project_root: Path,
    artifact_root: Path,
    write_scopes: Sequence[str],
    overlay_paths: Sequence[str] = (),
    *,
    baseline_only: bool = False,
    runner: CommandRunner = subprocess.run,
) -> dict[str, Any]:
    project_root = project_root.resolve()
    artifact_root = artifact_root.resolve()
    try:
        artifact_root.relative_to(project_root)
    except ValueError:
        pass
    else:
        raise UsageError("Test artifacts must be outside the Unity project.")
    if baseline_only and overlay_paths:
        raise UsageError("A baseline-only snapshot cannot include overlay paths.")
    if not baseline_only and (not write_scopes or not overlay_paths):
        raise UsageError(
            "A task overlay snapshot requires write claims and exact overlay paths."
        )
    critical_inputs = _critical_fingerprints(project_root)
    _reject_mutable_file_dependencies(project_root)
    observation = observe_vcs(project_root, runner=runner)
    requested = {
        _path_key(normalize_relative(project_root, value)) for value in overlay_paths
    }
    for path in requested:
        if not any(scope_covers(scope, path) for scope in write_scopes):
            raise UsageError(f"Overlay path is outside the task write claims: {path}")
    selected: list[PendingEntry] = []
    for entry in observation.entries:
        if _path_key(entry.path) not in requested:
            continue
        if entry.source_path:
            if _path_key(entry.source_path) not in requested:
                raise UsageError(
                    "Moved paths require both exact source and destination overlays: "
                    f"{entry.source_path}, {entry.path}"
                )
            if not any(
                scope_covers(scope, entry.source_path) for scope in write_scopes
            ):
                raise UsageError(
                    "Moved path counterpart is outside the task write claims: "
                    f"{entry.source_path}"
                )
        selected.append(entry)
    observed = {_path_key(entry.path) for entry in selected}
    missing = sorted(requested - observed)
    if missing:
        raise UsageError(
            "Declared overlay paths are not current SCM pending: " + ", ".join(missing)
        )
    _validate_meta_pairs(selected)
    snapshot_id = f"snapshot-{uuid.uuid4().hex[:16]}"
    overlay_root = artifact_root / "overlay"
    ensure_private_directory(overlay_root)
    records: list[dict[str, Any]] = []
    for entry in sorted(selected, key=lambda value: (value.path, value.operation)):
        record: dict[str, Any] = {
            "path": entry.path,
            "operation": entry.operation,
            "source_path": entry.source_path,
            "vcs_status": entry.status,
        }
        if entry.operation == "copy":
            source = project_root / Path(entry.path)
            _reject_symlink_path(project_root, entry.path)
            if not source.is_file():
                raise ProjectBusyError(
                    f"Pending snapshot file is unavailable: {entry.path}"
                )
            record.update(_copy_stable(source, overlay_root / Path(entry.path)))
        records.append(record)
    confirmed = observe_vcs(project_root, runner=runner)
    confirmed_entries = {
        (_path_key(entry.path), entry.operation, entry.status.strip())
        for entry in confirmed.entries
        if _path_key(entry.path) in requested
    }
    expected_entries = {
        (_path_key(entry.path), entry.operation, entry.status.strip())
        for entry in selected
    }
    if (
        confirmed.revision != observation.revision
        or confirmed_entries != expected_entries
    ):
        raise ProjectBusyError("SCM state changed while creating the test snapshot.")
    if _critical_fingerprints(project_root) != critical_inputs:
        raise ProjectBusyError(
            "Critical Unity project inputs changed while creating the test snapshot."
        )
    manifest = {
        "schema_version": 1,
        "snapshot_id": snapshot_id,
        "created_at": time.time(),
        "project_root": str(project_root),
        "vcs": {
            "kind": observation.kind,
            "revision": observation.revision,
            "repository": observation.repository,
        },
        "write_scopes": sorted({_path_key(value) for value in write_scopes}),
        "critical_inputs": critical_inputs,
        "overlay": records,
    }
    manifest_path = artifact_root / "snapshot.json"
    _atomic_write(manifest_path, json.dumps(manifest, indent=2, sort_keys=True) + "\n")
    return {"snapshot_id": snapshot_id, "manifest": str(manifest_path), **manifest}


def _safe_slot_project(slot_root: Path) -> Path:
    slot_root = slot_root.resolve()
    project = (slot_root / "project").resolve()
    try:
        project.relative_to(slot_root)
    except ValueError as exc:
        raise UsageError("Test slot project escapes its configured root.") from exc
    return project


def _validate_git_slot(project: Path, repository: str, runner: CommandRunner) -> None:
    git_directory = project / ".git"
    if not git_directory.is_dir():
        raise UsageError("The test slot is not a standalone Git checkout.")
    top_level = Path(
        _required_command(
            ["git", "rev-parse", "--show-toplevel"],
            cwd=project,
            label="Git test slot validation",
            runner=runner,
        ).strip()
    ).resolve()
    if top_level != project.resolve():
        raise UsageError("The test slot Git root does not match its configured path.")
    origin = _required_command(
        ["git", "remote", "get-url", "origin"],
        cwd=project,
        label="Git test slot origin validation",
        runner=runner,
    ).strip()
    if Path(origin).expanduser().resolve() != Path(repository).expanduser().resolve():
        raise UsageError("The test slot belongs to a different Git repository.")


def _clear_previous_plastic_overlay(slot_root: Path, project: Path) -> None:
    marker = slot_root / "current-snapshot.json"
    if not marker.is_file():
        return
    try:
        previous = json.loads(marker.read_text(encoding="utf-8"))
        manifest_path = Path(previous["manifest"]).resolve()
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    except (OSError, KeyError, TypeError, json.JSONDecodeError) as exc:
        raise UsageError(
            "Cannot safely reset the previous Plastic test overlay."
        ) from exc
    for record in manifest.get("overlay", []):
        if record.get("operation") != "copy":
            continue
        destination = project / Path(normalize_relative(project, record["path"]))
        if destination.is_dir():
            shutil.rmtree(destination)
        else:
            destination.unlink(missing_ok=True)


def materialize_snapshot(
    manifest_path: Path,
    slot_root: Path,
    *,
    runner: CommandRunner = subprocess.run,
) -> Path:
    try:
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
        vcs = manifest["vcs"]
        overlay = manifest["overlay"]
    except (OSError, KeyError, TypeError, json.JSONDecodeError) as exc:
        raise UsageError("Snapshot manifest is missing or invalid.") from exc
    slot_root = slot_root.resolve()
    ensure_private_directory(slot_root)
    project = _safe_slot_project(slot_root)
    kind = vcs["kind"]
    if kind == "git":
        if not (project / ".git").is_dir():
            if project.exists() and any(project.iterdir()):
                raise UsageError(
                    "Unrecognized content exists in the test slot project."
                )
            project.parent.mkdir(parents=True, exist_ok=True)
            _required_command(
                [
                    "git",
                    "clone",
                    "--no-checkout",
                    "--no-hardlinks",
                    vcs["repository"],
                    str(project),
                ],
                cwd=slot_root,
                label="Git test slot creation",
                runner=runner,
            )
        _validate_git_slot(project, vcs["repository"], runner)
        _required_command(
            ["git", "fetch", "--prune", "origin"],
            cwd=project,
            label="Git test slot fetch",
            runner=runner,
        )
        _required_command(
            ["git", "reset", "--hard"],
            cwd=project,
            label="Git test slot reset",
            runner=runner,
        )
        _required_command(
            ["git", "clean", "-fdx", "-e", "Library/"],
            cwd=project,
            label="Git test slot clean",
            runner=runner,
        )
        _required_command(
            ["git", "checkout", "--detach", vcs["revision"]],
            cwd=project,
            label="Git test slot checkout",
            runner=runner,
        )
    elif kind == "plastic":
        if not (project / ".plastic").is_dir():
            if project.exists() and any(project.iterdir()):
                raise UsageError(
                    "Unrecognized content exists in the test slot project."
                )
            workspace_name = (
                f"umcp-test-{hashlib.sha256(str(slot_root).encode()).hexdigest()[:12]}"
            )
            _required_command(
                [
                    "cm",
                    "workspace",
                    "create",
                    workspace_name,
                    str(project),
                    vcs["repository"],
                ],
                cwd=slot_root,
                label="Plastic test slot creation",
                runner=runner,
            )
        observation = observe_plastic(project, runner=runner)
        if observation.repository != vcs["repository"]:
            raise UsageError("The test slot belongs to a different Plastic repository.")
        _clear_previous_plastic_overlay(slot_root, project)
        _required_command(
            ["cm", "undo", ".", "--recursive"],
            cwd=project,
            label="Plastic test slot reset",
            runner=runner,
        )
        _required_command(
            [
                "cm",
                "switch",
                f"cs:{vcs['revision']}",
                f"--workspace={project}",
                "--noinput",
            ],
            cwd=project,
            label="Plastic test slot checkout",
            runner=runner,
        )
    else:
        raise UsageError(f"Unsupported snapshot VCS: {kind}")
    overlay_root = manifest_path.parent / "overlay"
    for record in overlay:
        relative = normalize_relative(project, record["path"])
        destination = project / Path(relative)
        if record["operation"] == "delete":
            if destination.is_dir():
                shutil.rmtree(destination)
            else:
                destination.unlink(missing_ok=True)
        elif record["operation"] == "copy":
            source = overlay_root / Path(relative)
            if not source.is_file() or sha256_file(source) != record["sha256"]:
                raise UsageError(f"Snapshot overlay fingerprint failed: {relative}")
            destination.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(source, destination)
        else:
            raise UsageError(f"Unknown snapshot operation: {record['operation']}")
    _atomic_write(
        slot_root / "current-snapshot.json",
        json.dumps(
            {"snapshot_id": manifest["snapshot_id"], "manifest": str(manifest_path)},
            sort_keys=True,
        )
        + "\n",
    )
    return project
