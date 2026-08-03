from __future__ import annotations

from pathlib import Path

import pytest

from tests.helpers import create_unity_project
from unity_mcp_supervisor.compatibility import check_compatibility, require_compatible
from unity_mcp_supervisor.errors import IncompatibleError


@pytest.mark.parametrize(
    "plugin_ref", ["v10.1.2", "4ce7dd3cc54e37e2ed6dc59cb5a047f3dccb3f50"]
)
def test_v1012_is_in_tested_matrix(tmp_path: Path, plugin_ref: str) -> None:
    project = create_unity_project(tmp_path / "Project", plugin_ref)
    result = check_compatibility(project, ())
    assert result.status == "compatible"
    assert require_compatible(project, ()).compatible


def test_v101_remains_a_supported_rollback(tmp_path: Path) -> None:
    project = create_unity_project(tmp_path / "Project", "v10.1.0")
    assert check_compatibility(project, ()).status == "compatible"


def test_other_major_fails_closed(tmp_path: Path) -> None:
    project = create_unity_project(tmp_path / "Project", "v11.0.0")
    result = check_compatibility(project, ())
    assert result.status == "incompatible"
    with pytest.raises(IncompatibleError):
        require_compatible(project, ())


def test_unknown_ref_requires_explicit_local_approval(tmp_path: Path) -> None:
    project = create_unity_project(tmp_path / "Project", "feature-branch")
    assert check_compatibility(project, ()).status == "unknown"
    assert check_compatibility(project, ("feature-branch",)).status == "approved"
