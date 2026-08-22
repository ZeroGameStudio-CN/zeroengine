from __future__ import annotations

import tomllib
from pathlib import Path

from unity_workspace_scheduler.state import SCHEMA_VERSION

ROOT = Path(__file__).resolve().parents[1]


def test_distribution_has_no_runtime_dependencies() -> None:
    project = tomllib.loads((ROOT / "pyproject.toml").read_text(encoding="utf-8"))
    assert project["project"]["version"] == "1.2.0"
    assert project["project"]["dependencies"] == []
    assert set(project["project"]["scripts"]) == {"unity-scheduler"}
    assert SCHEMA_VERSION == 1


def test_source_has_no_legacy_or_executor_implementation() -> None:
    source = "\n".join(
        path.read_text(encoding="utf-8")
        for path in (ROOT / "src" / "unity_workspace_scheduler").glob("*.py")
    ).casefold()
    forbidden = (
        "unity_" + "mcp",
        "mcp" + "forunity",
        "fastmcp",
        "test_farm",
        "editorcontrol",
        "project_lease",
        "import httpx",
        "import psutil",
        "import filelock",
    )
    assert not [term for term in forbidden if term in source]
