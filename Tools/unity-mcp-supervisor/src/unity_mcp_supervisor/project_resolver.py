from __future__ import annotations

import hashlib
import os
import time
from dataclasses import dataclass
from pathlib import Path
from typing import Any

from .errors import ProjectError, ServiceError, UsageError
from .rest_client import RestClient


def find_project_root(start: Path | str) -> Path:
    candidate = Path(start).expanduser().resolve()
    if candidate.is_file():
        candidate = candidate.parent
    for current in (candidate, *candidate.parents):
        if (current / "Assets").is_dir() and (
            current / "ProjectSettings" / "ProjectVersion.txt"
        ).is_file():
            return current
    raise UsageError(f"No Unity project root found from: {candidate}")


def canonical_project_root(path: Path | str) -> str:
    return os.path.normcase(os.path.realpath(Path(path))).replace("\\", "/").rstrip("/")


def unity_project_hash_candidate(project_root: Path | str) -> str:
    resolved = Path(project_root).expanduser().resolve()
    assets_path = f"{resolved.as_posix().rstrip('/')}/Assets"
    return hashlib.sha1(assets_path.encode("utf-8")).hexdigest()[:16].lower()


def _find_project_root_value(value: Any) -> str | None:
    if isinstance(value, dict):
        direct = value.get("projectRoot")
        if isinstance(direct, str) and direct:
            return direct
        for nested in value.values():
            found = _find_project_root_value(nested)
            if found:
                return found
    elif isinstance(value, list):
        for nested in value:
            found = _find_project_root_value(nested)
            if found:
                return found
    return None


@dataclass(frozen=True)
class ResolvedProject:
    root: Path
    canonical_root: str
    project_hash: str
    project_name: str
    unity_version: str
    connected_at: str


class ProjectResolver:
    def __init__(self, client: RestClient) -> None:
        self.client = client

    def resolve_once(self, project_root: Path) -> ResolvedProject:
        root = project_root.expanduser().resolve()
        expected = canonical_project_root(root)
        instances = self.client.instances()
        candidate_hash = unity_project_hash_candidate(root)
        candidates = [
            item for item in instances if str(item.get("hash", "")) == candidate_hash
        ]
        if len(candidates) > 1:
            raise ProjectError(
                "Multiple Unity plugin sessions report the requested project hash.",
                details={
                    "matching_hashes": [candidate_hash],
                    "matching_session_count": len(candidates),
                },
            )
        if not candidates:
            raise ProjectError(
                "No connected Unity Editor reports the requested project hash.",
                details={
                    "connected_instance_count": len(instances),
                    "hash_candidate": candidate_hash,
                },
            )

        instance = candidates[0]
        try:
            info = self.client.command(
                "get_project_info",
                {},
                candidate_hash,
                safe_probe=True,
            )
        except (ProjectError, ServiceError) as exc:
            raise ProjectError(
                "The requested project session failed its safe identity probe.",
                details={"hash_candidate": candidate_hash},
            ) from exc
        reported = _find_project_root_value(info)
        if not reported or canonical_project_root(reported) != expected:
            raise ProjectError(
                "The requested project hash reported a different absolute path.",
                details={
                    "hash_candidate": candidate_hash,
                    "reported_project_root": reported,
                },
            )
        return ResolvedProject(
            root=root,
            canonical_root=expected,
            project_hash=candidate_hash,
            project_name=str(instance.get("project") or root.name),
            unity_version=str(instance.get("unity_version") or "unknown"),
            connected_at=str(instance.get("connected_at") or ""),
        )

    def wait(self, project_root: Path, timeout_seconds: float) -> ResolvedProject:
        deadline = time.monotonic() + timeout_seconds
        last_error: Exception | None = None
        while True:
            try:
                return self.resolve_once(project_root)
            except (ProjectError, ServiceError) as exc:
                last_error = exc
            remaining = deadline - time.monotonic()
            if remaining <= 0:
                details = getattr(last_error, "details", {}) if last_error else {}
                raise ProjectError(
                    "Timed out waiting for the requested Unity Editor connection.",
                    details={
                        **details,
                        "hint": "Open the Editor, then run 'umcp connect --project <path>'.",
                    },
                ) from last_error
            time.sleep(min(0.5, remaining))
