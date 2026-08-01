from __future__ import annotations

import importlib.metadata
from pathlib import Path

import pytest

from tests.helpers import create_unity_project, fake_http_server
from unity_mcp_supervisor.errors import OutcomeUnknownError
from unity_mcp_supervisor.rest_client import (
    PINNED_SERVER_VERSION,
    EndpointKind,
    RestClient,
)


def test_health_contract_version_matches_installed_server_pin() -> None:
    assert importlib.metadata.version("mcpforunityserver") == PINNED_SERVER_VERSION


def test_endpoint_classification_distinguishes_compatible_and_foreign() -> None:
    with fake_http_server() as endpoint:
        assert RestClient(endpoint).classify().kind == EndpointKind.COMPATIBLE
    with fake_http_server(foreign=True) as endpoint:
        assert RestClient(endpoint).classify().kind == EndpointKind.FOREIGN
    with fake_http_server(health_version="11.0.0") as endpoint:
        assert RestClient(endpoint).classify().kind == EndpointKind.FOREIGN


def test_loopback_requests_ignore_invalid_proxy_environment(monkeypatch) -> None:
    monkeypatch.setenv("NO_PROXY", "127.0.0.1,localhost,::1")
    with fake_http_server() as endpoint:
        assert RestClient(endpoint).classify().kind == EndpointKind.COMPATIBLE


def test_business_command_disconnect_is_outcome_unknown_and_not_replayed(
    tmp_path: Path,
) -> None:
    project = create_unity_project(tmp_path / "Project")
    counter = [0]
    instances = [
        {
            "hash": "aaaaaaaaaaaaaaaa",
            "project": "Project",
            "unity_version": "2022.3.62f3",
            "connected_at": "now",
            "project_root": str(project),
        }
    ]
    with (
        fake_http_server(instances, drop_counter=counter) as endpoint,
        pytest.raises(OutcomeUnknownError),
    ):
        RestClient(endpoint).command("drop_response", {}, "aaaaaaaaaaaaaaaa")
    assert counter == [1]
