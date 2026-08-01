from __future__ import annotations

import os
from pathlib import Path

import pytest

from tests.helpers import create_unity_project, fake_http_server
from unity_mcp_supervisor.errors import ProjectError
from unity_mcp_supervisor.project_resolver import (
    ProjectResolver,
    canonical_project_root,
    find_project_root,
    unity_project_hash_candidate,
)
from unity_mcp_supervisor.rest_client import RestClient


@pytest.mark.skipif(
    os.name != "nt", reason="Observed Application.dataPath hash is Windows-specific"
)
def test_known_windows_unity_hash_matches_observed_project_identity() -> None:
    assert (
        unity_project_hash_candidate(Path("D:/unity/projects/POB"))
        == "e487dff6b56c0cbb"
    )


def test_find_root_walks_up_from_project_child(tmp_path: Path) -> None:
    project = create_unity_project(tmp_path / "Project")
    child = project / "Assets" / "Nested"
    child.mkdir()
    assert find_project_root(child) == project.resolve()


def test_resolver_uses_verified_absolute_path_not_name_or_first_instance(
    tmp_path: Path,
) -> None:
    first = create_unity_project(tmp_path / "A" / "SameName")
    second = create_unity_project(tmp_path / "B" / "SameName")
    instances = [
        {
            "hash": unity_project_hash_candidate(first),
            "project": "SameName",
            "unity_version": "2022.3.62f3",
            "connected_at": "first",
            "project_root": str(first),
        },
        {
            "hash": unity_project_hash_candidate(second),
            "project": "SameName",
            "unity_version": "2022.3.62f3",
            "connected_at": "second",
            "project_root": str(second),
        },
    ]
    with fake_http_server(instances) as endpoint:
        resolved = ProjectResolver(RestClient(endpoint)).resolve_once(second)
    assert resolved.project_hash == unity_project_hash_candidate(second)
    assert resolved.canonical_root == canonical_project_root(second)


def test_resolver_never_falls_back_to_first_instance(tmp_path: Path) -> None:
    requested = create_unity_project(tmp_path / "Requested")
    other = create_unity_project(tmp_path / "Other")
    instances = [
        {
            "hash": unity_project_hash_candidate(requested),
            "project": "Requested",
            "unity_version": "2022.3.62f3",
            "connected_at": "first",
            "project_root": str(other),
        }
    ]
    with fake_http_server(instances) as endpoint, pytest.raises(ProjectError):
        ProjectResolver(RestClient(endpoint)).resolve_once(requested)


def test_duplicate_path_sessions_fail_as_ambiguous(tmp_path: Path) -> None:
    project = create_unity_project(tmp_path / "Project")
    project_hash = unity_project_hash_candidate(project)
    instances = [
        {
            "hash": project_hash,
            "project": "Project",
            "unity_version": "2022.3.62f3",
            "connected_at": value,
            "project_root": str(project),
        }
        for value in ("first", "second")
    ]
    with (
        fake_http_server(instances) as endpoint,
        pytest.raises(ProjectError, match="Multiple"),
    ):
        ProjectResolver(RestClient(endpoint)).resolve_once(project)


def test_resolver_never_probes_other_hashes_when_candidate_probe_fails(
    tmp_path: Path,
) -> None:
    project = create_unity_project(tmp_path / "Project")
    project_hash = unity_project_hash_candidate(project)
    instances = [
        {
            "hash": project_hash,
            "project": "Project",
            "unity_version": "2022.3.62f3",
            "connected_at": "stale",
            "project_root": str(project),
            "probe_error": True,
        },
        {
            "hash": "bbbbbbbbbbbbbbbb",
            "project": "Project",
            "unity_version": "2022.3.62f3",
            "connected_at": "live",
            "project_root": str(project),
        },
    ]
    with fake_http_server(instances) as endpoint, pytest.raises(ProjectError):
        ProjectResolver(RestClient(endpoint)).resolve_once(project)


def test_resolver_sends_identity_probe_only_to_candidate_hash(tmp_path: Path) -> None:
    project = create_unity_project(tmp_path / "Project")
    project_hash = unity_project_hash_candidate(project)
    probed: list[str] = []

    class Client:
        def instances(self):
            return [
                {
                    "hash": project_hash,
                    "project": "Project",
                    "unity_version": "2022.3.62f3",
                    "connected_at": "candidate",
                },
                {
                    "hash": "bbbbbbbbbbbbbbbb",
                    "project": "Other",
                    "unity_version": "2022.3.62f3",
                    "connected_at": "other",
                },
            ]

        def command(self, _kind, _params, project_hash_value, **_kwargs):
            probed.append(project_hash_value)
            return {"data": {"projectRoot": str(project)}}

    resolved = ProjectResolver(Client()).resolve_once(project)

    assert resolved.project_hash == project_hash
    assert probed == [project_hash]
