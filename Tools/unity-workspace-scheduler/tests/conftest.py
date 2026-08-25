from __future__ import annotations

import os
import uuid
from pathlib import Path
from typing import Any

import pytest

from unity_workspace_scheduler.coordinator import WorkspaceCoordinator

_MUTATION_METHODS = (
    "register",
    "unregister",
    "start_task",
    "heartbeat",
    "release_task",
    "acquire_claim",
    "park_task",
    "release_claim",
    "cancel_claim",
    "resolve_unknown",
)


@pytest.fixture(autouse=True)
def _supply_operation_ids_to_legacy_direct_calls(monkeypatch: pytest.MonkeyPatch) -> None:
    """Keep pre-v3 direct-call tests focused while production APIs stay strict."""

    for method_name in _MUTATION_METHODS:
        original = getattr(WorkspaceCoordinator, method_name)

        def wrapped(
            self: WorkspaceCoordinator,
            *args: Any,
            _original=original,
            _method_name=method_name,
            **kwargs: Any,
        ) -> Any:
            kwargs.setdefault("operation_id", str(uuid.uuid4()))
            if (
                _method_name == "release_task"
                and kwargs.get("result") in {"completed", "failed"}
                and "token_cleanup_path" not in kwargs
            ):
                workspace = Path(args[0]).expanduser().resolve()
                kwargs["token_cleanup_path"] = os.path.normpath(
                    str(workspace / f".test-token-{uuid.uuid4().hex}.token")
                )
            return _original(self, *args, **kwargs)

        monkeypatch.setattr(WorkspaceCoordinator, method_name, wrapped)
