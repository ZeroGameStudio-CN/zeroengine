from __future__ import annotations

from pathlib import Path

import pytest

from tests.helpers import create_unity_project
from unity_mcp_supervisor.compatibility import check_compatibility, require_compatible
from unity_mcp_supervisor.errors import IncompatibleError


def test_v101_is_in_tested_matrix(tmp_path: Path) -> None:
    project = create_unity_project(tmp_path / "Project", "v10.1.0")
    result = check_compatibility(project, ())
    assert result.status == "compatible"
    assert require_compatible(project, ()).compatible


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
