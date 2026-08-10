from __future__ import annotations

import json
import subprocess
from pathlib import Path

import pytest

from unity_mcp_supervisor.errors import UsageError
from unity_mcp_supervisor.test_snapshot import (
    create_snapshot,
    materialize_snapshot,
    observe_plastic,
    sha256_file,
)


def git(project: Path, *arguments: str) -> str:
    completed = subprocess.run(
        ["git", *arguments],
        cwd=project,
        check=True,
        capture_output=True,
        text=True,
        encoding="utf-8",
    )
    return completed.stdout.strip()


def create_git_project(root: Path) -> Path:
    root.mkdir()
    (root / "Assets" / "Feature").mkdir(parents=True)
    (root / "ProjectSettings").mkdir()
    (root / "Packages").mkdir()
    (root / "Assets" / "Feature" / "Existing.cs").write_text("old\n", encoding="utf-8")
    (root / "Assets" / "Feature" / "Existing.cs.meta").write_text(
        "guid: existing\n", encoding="utf-8"
    )
    (root / "Assets" / "Feature" / "Delete.cs").write_text("delete\n", encoding="utf-8")
    (root / "Assets" / "Feature" / "Delete.cs.meta").write_text(
        "guid: delete\n", encoding="utf-8"
    )
    (root / "Assets" / "Other.cs").write_text("other\n", encoding="utf-8")
    (root / "ProjectSettings" / "ProjectVersion.txt").write_text(
        "m_EditorVersion: 2022.3.62f3\n", encoding="utf-8"
    )
    (root / "Packages" / "manifest.json").write_text(
        '{"dependencies": {}}\n', encoding="utf-8"
    )
    (root / "Packages" / "packages-lock.json").write_text(
        '{"dependencies": {}}\n', encoding="utf-8"
    )
    git(root, "init")
    git(root, "config", "user.email", "test@example.com")
    git(root, "config", "user.name", "Test")
    git(root, "add", ".")
    git(root, "commit", "-m", "baseline")
    return root


def test_git_snapshot_contains_only_claimed_overlay_and_materializes(
    tmp_path: Path,
) -> None:
    project = create_git_project(tmp_path / "project")
    (project / "Assets" / "Feature" / "Existing.cs").write_text(
        "new\n", encoding="utf-8"
    )
    (project / "Assets" / "Feature" / "Added.cs").write_text(
        "added\n", encoding="utf-8"
    )
    (project / "Assets" / "Feature" / "Added.cs.meta").write_text(
        "guid: added\n", encoding="utf-8"
    )
    (project / "Assets" / "Feature" / "Delete.cs").unlink()
    (project / "Assets" / "Feature" / "Delete.cs.meta").unlink()
    (project / "Assets" / "Other.cs").write_text("unrelated\n", encoding="utf-8")
    artifact = tmp_path / "artifacts" / "job"
    snapshot = create_snapshot(
        project,
        artifact,
        ["Assets/Feature"],
        [
            "Assets/Feature/Existing.cs",
            "Assets/Feature/Added.cs",
            "Assets/Feature/Added.cs.meta",
            "Assets/Feature/Delete.cs",
            "Assets/Feature/Delete.cs.meta",
        ],
    )
    paths = {value["path"] for value in snapshot["overlay"]}
    assert "Assets/Other.cs" not in paths
    assert paths == {
        "Assets/Feature/Existing.cs",
        "Assets/Feature/Added.cs",
        "Assets/Feature/Added.cs.meta",
        "Assets/Feature/Delete.cs",
        "Assets/Feature/Delete.cs.meta",
    }
    isolated = materialize_snapshot(Path(snapshot["manifest"]), tmp_path / "slot")
    assert (isolated / "Assets" / "Feature" / "Existing.cs").read_text() == "new\n"
    assert (isolated / "Assets" / "Feature" / "Added.cs").is_file()
    assert not (isolated / "Assets" / "Feature" / "Delete.cs").exists()
    assert (isolated / "Assets" / "Other.cs").read_text() == "other\n"


def test_snapshot_rejects_mutable_file_dependency(tmp_path: Path) -> None:
    project = create_git_project(tmp_path / "project")
    (project / "Packages" / "manifest.json").write_text(
        '{"dependencies":{"x":"file:../x"}}', encoding="utf-8"
    )
    with pytest.raises(UsageError, match="mutable file"):
        create_snapshot(
            project,
            tmp_path / "artifact",
            ["Packages/manifest.json"],
            ["Packages/manifest.json"],
        )


def test_snapshot_rejects_new_asset_without_meta(tmp_path: Path) -> None:
    project = create_git_project(tmp_path / "project")
    (project / "Assets" / "Feature" / "NoMeta.cs").write_text("new\n", encoding="utf-8")
    with pytest.raises(UsageError, match="meta pair"):
        create_snapshot(
            project,
            tmp_path / "artifact",
            ["Assets/Feature"],
            ["Assets/Feature/NoMeta.cs"],
        )


def test_baseline_only_snapshot_rejects_an_overlay(tmp_path: Path) -> None:
    project = create_git_project(tmp_path / "project")
    with pytest.raises(UsageError, match="baseline-only"):
        create_snapshot(
            project,
            tmp_path / "artifact",
            ["Assets/Feature"],
            ["Assets/Feature/Existing.cs"],
            baseline_only=True,
        )


def test_plastic_xml_preserves_move_delete_and_private(tmp_path: Path) -> None:
    project = tmp_path / "plastic"
    project.mkdir()
    xml = """<?xml version="1.0" encoding="utf-8"?>
    <StatusOutput>
      <WorkspaceStatus><Status><RepSpec><Server>org@cloud</Server><Name>Repo</Name></RepSpec><Changeset>42</Changeset></Status></WorkspaceStatus>
      <Changes>
        <Change><Type>MV</Type><Path>Assets/New.cs</Path><OldPath>Assets/Old.cs</OldPath><RevisionType>enTextFile</RevisionType></Change>
        <Change><Type>DE</Type><Path>Assets/Delete.cs</Path><OldPath/><RevisionType>enTextFile</RevisionType></Change>
        <Change><Type>PR</Type><Path>Assets/Private.cs</Path><OldPath/><RevisionType>enTextFile</RevisionType></Change>
        <Change><Type>PR</Type><Path>Assets/Folder</Path><OldPath/><RevisionType>enDirectory</RevisionType></Change>
      </Changes>
    </StatusOutput>"""

    def runner(*_args, **_kwargs):
        return subprocess.CompletedProcess([], 0, xml, "")

    observation = observe_plastic(project, runner=runner)
    assert observation.revision == "42"
    assert observation.repository == "Repo@org@cloud"
    assert [(value.path, value.operation) for value in observation.entries] == [
        ("Assets/New.cs", "copy"),
        ("Assets/Old.cs", "delete"),
        ("Assets/Delete.cs", "delete"),
        ("Assets/Private.cs", "copy"),
    ]


def test_plastic_snapshot_requires_both_move_ends(tmp_path: Path) -> None:
    project = tmp_path / "plastic"
    (project / ".plastic").mkdir(parents=True)
    (project / "Assets").mkdir()
    (project / "Assets" / "New.cs").write_text("moved", encoding="utf-8")
    xml = """<StatusOutput><WorkspaceStatus><Status><RepSpec><Server>org@cloud</Server><Name>Repo</Name></RepSpec><Changeset>42</Changeset></Status></WorkspaceStatus><Changes><Change><Type>MV</Type><Path>Assets/New.cs</Path><OldPath>Assets/Old.cs</OldPath><RevisionType>enTextFile</RevisionType></Change></Changes></StatusOutput>"""

    def runner(*_args, **_kwargs):
        return subprocess.CompletedProcess([], 0, xml, "")

    with pytest.raises(UsageError, match="both exact source and destination"):
        create_snapshot(
            project,
            tmp_path / "artifact",
            ["Assets"],
            ["Assets/New.cs"],
            runner=runner,
        )


def test_snapshot_manifest_has_no_source_contents(tmp_path: Path) -> None:
    project = create_git_project(tmp_path / "project")
    secret = "content-not-for-status"
    (project / "Assets" / "Feature" / "Existing.cs").write_text(
        secret, encoding="utf-8"
    )
    snapshot = create_snapshot(
        project,
        tmp_path / "artifacts",
        ["Assets/Feature"],
        ["Assets/Feature/Existing.cs"],
    )
    manifest = json.loads(Path(snapshot["manifest"]).read_text(encoding="utf-8"))
    assert secret not in json.dumps(manifest)


def test_reused_git_slot_must_match_snapshot_repository(tmp_path: Path) -> None:
    project = create_git_project(tmp_path / "project")
    other = create_git_project(tmp_path / "other")
    snapshot = create_snapshot(
        project,
        tmp_path / "artifacts",
        (),
        (),
        baseline_only=True,
    )
    slot = tmp_path / "slot"
    isolated = materialize_snapshot(Path(snapshot["manifest"]), slot)
    git(isolated, "remote", "set-url", "origin", str(other))
    with pytest.raises(UsageError, match="different Git repository"):
        materialize_snapshot(Path(snapshot["manifest"]), slot)


def test_reused_git_slot_fetches_a_new_baseline_revision(tmp_path: Path) -> None:
    project = create_git_project(tmp_path / "project")
    first = create_snapshot(
        project,
        tmp_path / "first-artifact",
        (),
        (),
        baseline_only=True,
    )
    slot = tmp_path / "slot"
    isolated = materialize_snapshot(Path(first["manifest"]), slot)
    assert (isolated / "Assets" / "Other.cs").read_text() == "other\n"

    (project / "Assets" / "Other.cs").write_text("next\n", encoding="utf-8")
    git(project, "add", "Assets/Other.cs")
    git(project, "commit", "-m", "next baseline")
    second = create_snapshot(
        project,
        tmp_path / "second-artifact",
        (),
        (),
        baseline_only=True,
    )
    isolated = materialize_snapshot(Path(second["manifest"]), slot)
    assert (isolated / "Assets" / "Other.cs").read_text() == "next\n"


def test_snapshot_rejects_symbolic_link_overlay(tmp_path: Path) -> None:
    project = create_git_project(tmp_path / "project")
    target = project / "Assets" / "Feature" / "Target.cs"
    target.write_text("target\n", encoding="utf-8")
    link = project / "Assets" / "Feature" / "Link.cs"
    try:
        link.symlink_to(target)
    except OSError:
        pytest.skip("Symbolic links are unavailable on this machine.")
    (project / "Assets" / "Feature" / "Link.cs.meta").write_text(
        "guid: link\n", encoding="utf-8"
    )
    with pytest.raises(UsageError, match="symbolic links"):
        create_snapshot(
            project,
            tmp_path / "artifacts",
            ["Assets/Feature"],
            ["Assets/Feature/Link.cs", "Assets/Feature/Link.cs.meta"],
        )


def test_plastic_slot_validates_repository_and_applies_overlay(tmp_path: Path) -> None:
    slot = tmp_path / "slot"
    project = slot / "project"
    (project / ".plastic").mkdir(parents=True)
    (project / "Assets").mkdir()
    (project / "Assets" / "Delete.cs").write_text("old", encoding="utf-8")
    artifact = tmp_path / "artifact"
    overlay = artifact / "overlay" / "Assets"
    overlay.mkdir(parents=True)
    copied = overlay / "Copy.cs"
    copied.write_text("new", encoding="utf-8")
    manifest = artifact / "snapshot.json"
    manifest.write_text(
        json.dumps(
            {
                "snapshot_id": "snapshot-plastic",
                "vcs": {
                    "kind": "plastic",
                    "revision": "42",
                    "repository": "Repo@org@cloud",
                },
                "overlay": [
                    {
                        "path": "Assets/Copy.cs",
                        "operation": "copy",
                        "sha256": sha256_file(copied),
                    },
                    {"path": "Assets/Delete.cs", "operation": "delete"},
                ],
            }
        ),
        encoding="utf-8",
    )
    commands: list[list[str]] = []
    status_xml = """<StatusOutput><WorkspaceStatus><Status><RepSpec><Server>org@cloud</Server><Name>Repo</Name></RepSpec><Changeset>41</Changeset></Status></WorkspaceStatus><Changes /></StatusOutput>"""

    def runner(command, **_kwargs):
        commands.append(command)
        output = status_xml if command[:2] == ["cm", "status"] else ""
        return subprocess.CompletedProcess(command, 0, output, "")

    materialized = materialize_snapshot(manifest, slot, runner=runner)
    assert (materialized / "Assets" / "Copy.cs").read_text() == "new"
    assert not (materialized / "Assets" / "Delete.cs").exists()
    assert ["cm", "undo", ".", "--recursive"] in commands
    assert any(command[:2] == ["cm", "switch"] for command in commands)

    wrong = json.loads(manifest.read_text(encoding="utf-8"))
    wrong["vcs"]["repository"] = "Other@org@cloud"
    manifest.write_text(json.dumps(wrong), encoding="utf-8")
    with pytest.raises(UsageError, match="different Plastic repository"):
        materialize_snapshot(manifest, slot, runner=runner)
