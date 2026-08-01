from __future__ import annotations

import pytest


@pytest.fixture(autouse=True)
def disable_real_editor_prefs_guard(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setenv("UMCP_TEST_MODE", "1")
