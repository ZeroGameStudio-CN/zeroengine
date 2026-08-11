from __future__ import annotations

import importlib.metadata
from pathlib import Path

import pytest

from tests.helpers import create_unity_project, fake_http_server
from unity_mcp_supervisor.errors import IncompatibleError, OutcomeUnknownError
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


def test_business_command_disconnect_recovers_completed_receipt_without_replay(
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
    with fake_http_server(instances, drop_counter=counter) as endpoint:
        result = RestClient(endpoint).command("drop_response", {}, "aaaaaaaaaaaaaaaa")
    assert result["data"]["recovered"] is True
    assert counter == [1]


def test_business_command_started_without_result_stays_outcome_unknown(
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
        RestClient(endpoint).command("ambiguous_response", {}, "aaaaaaaaaaaaaaaa")
    assert counter == [1]


def test_business_command_rejects_plugin_without_receipt_protocol(
    tmp_path: Path,
) -> None:
    project = create_unity_project(tmp_path / "Project")
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
        fake_http_server(instances, receipt_protocol_unavailable=True) as endpoint,
        pytest.raises(IncompatibleError) as exc_info,
    ):
        RestClient(endpoint).command("probe", {}, "aaaaaaaaaaaaaaaa")

    assert exc_info.value.details["reason"] == "receipt-protocol-unavailable"


def test_server_error_recovers_completed_receipt_without_replay(
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
    with fake_http_server(instances, drop_counter=counter) as endpoint:
        result = RestClient(endpoint).command(
            "server_error_after_receipt", {}, "aaaaaaaaaaaaaaaa"
        )
    assert result["data"]["recovered"] is True
    assert counter == [1]
