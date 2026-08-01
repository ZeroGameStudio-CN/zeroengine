from __future__ import annotations

import json
import os
from pathlib import Path

import pytest

from unity_mcp_supervisor.errors import UsageError
from unity_mcp_supervisor.service_state import (
    ServiceRecord,
    Settings,
    StateStore,
    process_alive,
    validate_endpoint,
)


def test_validate_endpoint_accepts_only_fixed_ipv4_loopback() -> None:
    assert validate_endpoint("http://127.0.0.1:8080/") == "http://127.0.0.1:8080"
    for value in (
        "http://0.0.0.0:8080",
        "http://localhost:8080",
        "https://127.0.0.1:8080",
        "http://127.0.0.1:8080/path",
    ):
        with pytest.raises(UsageError):
            validate_endpoint(value)


def test_settings_round_trip_is_machine_local(tmp_path: Path) -> None:
    settings = Settings.load(tmp_path)
    saved = settings.save(
        endpoint="http://127.0.0.1:18080", approved_plugin_refs=("local-ref",)
    )
    loaded = Settings.load(tmp_path)
    assert loaded.endpoint == "http://127.0.0.1:18080"
    assert loaded.approved_plugin_refs == ("local-ref",)
    assert saved == loaded


def test_service_state_atomic_round_trip(tmp_path: Path) -> None:
    settings = Settings.load(tmp_path)
    store = StateStore(settings.paths)
    record = ServiceRecord(
        status="healthy-owned",
        supervisor_pid=123,
        supervisor_token="supervisor-token",
        server_pid=456,
        server_token="server-token",
        server_created_at=123.5,
        adopted=True,
        endpoint="http://127.0.0.1:8080",
    )
    store.write_service(record)
    loaded = store.read_service()
    assert loaded is not None
    assert loaded.status == "healthy-owned"
    assert loaded.server_pid == 456
    assert loaded.server_created_at == 123.5
    assert loaded.adopted is True
    assert (
        json.loads(settings.paths.service.read_text(encoding="utf-8"))["server_token"]
        == "server-token"
    )


def test_control_file_requires_explicit_token_and_action(tmp_path: Path) -> None:
    store = StateStore(Settings.load(tmp_path).paths)
    store.write_control(token="owner", action="stop", request_id="request")
    assert store.read_control() == {
        "action": "stop",
        "request_id": "request",
        "supervisor_token": "owner",
    }
    store.clear_control()
    assert store.read_control() is None


def test_invalid_process_identifier_is_not_alive() -> None:
    assert process_alive("not-a-pid") is False


def test_current_process_is_alive() -> None:
    assert process_alive(os.getpid()) is True
