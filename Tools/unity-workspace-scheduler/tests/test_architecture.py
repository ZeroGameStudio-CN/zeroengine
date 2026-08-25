from __future__ import annotations

import ast
import tomllib
from pathlib import Path

from unity_workspace_scheduler import PROTOCOL_VERSION
from unity_workspace_scheduler.state import SCHEMA_VERSION

ROOT = Path(__file__).resolve().parents[1]


def test_distribution_has_no_runtime_dependencies() -> None:
    project = tomllib.loads((ROOT / "pyproject.toml").read_text(encoding="utf-8"))
    assert project["project"]["version"] == "1.4.0"
    assert project["project"]["dependencies"] == []
    assert set(project["project"]["scripts"]) == {"unity-scheduler"}
    assert SCHEMA_VERSION == 3
    assert PROTOCOL_VERSION == 3


def test_claim_queue_allocation_does_not_scan_claim_history() -> None:
    source = (ROOT / "src" / "unity_workspace_scheduler" / "coordinator.py").read_text(
        encoding="utf-8"
    )
    assert "MAX(queue_order)" not in source
    assert "next_queue_order = next_queue_order + 1" in source


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


def test_all_runtime_directory_creation_uses_the_platform_acl_helper() -> None:
    source_root = ROOT / "src" / "unity_workspace_scheduler"
    violations: list[str] = []
    for path in source_root.glob("*.py"):
        tree = ast.parse(path.read_text(encoding="utf-8"))
        helper = next(
            (
                node
                for node in tree.body
                if isinstance(node, ast.FunctionDef) and node.name == "_ensure_private_directory"
            ),
            None,
        )
        allowed_nodes = set(ast.walk(helper)) if helper is not None else set()
        for node in ast.walk(tree):
            if (
                isinstance(node, ast.Call)
                and isinstance(node.func, ast.Attribute)
                and node.func.attr == "mkdir"
                and node not in allowed_nodes
            ):
                violations.append(f"{path.name}:{node.lineno}")
    assert violations == []


def test_standalone_read_staging_uses_controlled_private_cleanup() -> None:
    source = (ROOT / "src" / "unity_workspace_scheduler" / "state_ops.py").read_text(
        encoding="utf-8"
    )
    assert "tempfile.mkdtemp" not in source
    assert "shutil.rmtree" not in source
    assert "ignore_errors=True" not in source
    assert "_create_standalone_staging_parent" in source
    assert "standalone-staging-rmdir" in source


def test_windows_token_acl_is_read_only_and_rejects_broad_principals() -> None:
    source = (ROOT / "src" / "unity_workspace_scheduler" / "state.py").read_text(encoding="utf-8")
    lowered = source.casefold()
    assert "/grant" not in lowered
    assert "/inheritance" not in lowered
    assert "tempfile.gettempdir()" in source
    assert "is_symlink()" in source
    assert "_is_windows_reparse_point" in source
    assert "st_file_attributes" in source
    assert "FILE_ATTRIBUTE_REPARSE_POINT" in source
    assert "S-1-1-0" in source
    assert "S-1-5-11" in source
    assert "S-1-5-32-545" in source


def test_restore_recovery_runbook_covers_persistent_evidence_and_cleanup() -> None:
    setup = (ROOT / "docs" / "setup.md").read_text(encoding="utf-8")
    readme = (ROOT / "README.md").read_text(encoding="utf-8")

    for required in (
        ".scheduler.sqlite3.restore-quarantine",
        "recovery_required=true",
        "publication_uncertain=false",
        "publication_uncertain=true",
        "cleanup_pending",
        "do not delete them or blindly retry",
        "restore that main and its sidecars together with no-overwrite moves",
    ):
        assert required in setup
    for required in (
        "restore quarantine",
        "recovery_required",
        "publication_uncertain",
        "cleanup_pending",
    ):
        assert required in readme


def test_upgrade_runbook_uses_an_absolute_staged_candidate_before_canonical_install() -> None:
    setup = (ROOT / "docs" / "setup.md").read_text(encoding="utf-8")
    normalized_setup = " ".join(setup.split())
    staged_version = setup.index("<absolute-staged-1.4-executable> --version")
    staged_backup = setup.index(
        "<absolute-staged-1.4-executable> --state-dir <current-state-dir> state backup"
    )
    router_install = setup.index("install the canonical Router version that requires 1.4")
    scheduler_install = setup.index("Then install canonical Scheduler")

    assert staged_version < staged_backup < router_install < scheduler_install
    assert "require the parsed version to equal exactly `1.4.0`" in normalized_setup
    assert "unity-scheduler --state-dir <current-state-dir> state backup" not in setup
