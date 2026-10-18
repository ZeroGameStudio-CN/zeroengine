from __future__ import annotations

import random
from pathlib import Path

import pytest

import unity_workspace_scheduler.coordinator as module
from unity_workspace_scheduler.coordinator import WorkspaceCoordinator
from unity_workspace_scheduler.state import resolve_state_paths


@pytest.mark.parametrize("casefold", [False, True])
def test_scope_sets_preserve_pairwise_path_semantics(
    monkeypatch: pytest.MonkeyPatch, casefold: bool
) -> None:
    monkeypatch.setattr(module, "_platform_case_identity", str.casefold if casefold else str)
    paths = (
        ".",
        "Assets",
        "Assets/Foo",
        "Assets/Foo/Child.cs",
        "Assets/FooBar",
        "Assets/Foo.cs",
        "Assets/Foo.cs.meta",
        "assets/foo.cs",
        "Assets/A.meta",
        "Assets/A.meta/Child.cs",
        "Assets/A.meta.meta",
        "Assets/中文.cs",
        "Assets/中文.cs.meta",
        "Assets/FOO.cs.META",
        ".meta",
    )
    scopes = [(), *((path,) for path in paths)]
    rng = random.Random(327)
    scopes.extend(tuple(rng.sample(paths, rng.randrange(7))) for _ in range(35))
    for left in scopes:
        for right in scopes:
            expected = any(module._path_conflicts(a, b) for a in left for b in right)
            assert module._write_scopes_conflict(left, right) is expected, (left, right)


def test_large_disjoint_scopes_bound_normalization_work(monkeypatch: pytest.MonkeyPatch) -> None:
    original = module._platform_case_identity
    calls = 0

    def identity(value: str) -> str:
        nonlocal calls
        calls += 1
        return original(value)

    monkeypatch.setattr(module, "_platform_case_identity", identity)
    left = tuple(f"Assets/Left/File{i}.cs" for i in range(300))
    right = tuple(f"Assets/Right/File{i}.cs.meta" for i in range(300))
    assert module._write_scopes_conflict(left, right) is False
    assert calls <= 2 * (len(left) + len(right))


def test_resources_freezes_and_same_owner_keep_their_existing_rules() -> None:
    normal = {"task_id": "first", "kind": "normal"}
    other = {"task_id": "second", "kind": "normal"}
    freeze = {"task_id": "first", "kind": "freeze"}
    writes = {"write": ("Assets/Owned.cs",), "resource": ()}
    editor = {"write": (), "resource": ("unity-live",)}
    assert WorkspaceCoordinator._claims_conflict(normal, writes, normal, writes) is False
    assert WorkspaceCoordinator._claims_conflict(normal, editor, normal, editor) is True
    assert WorkspaceCoordinator._claims_conflict(freeze, writes, normal, writes) is False
    assert WorkspaceCoordinator._claims_conflict(freeze, writes, other, editor) is True
    assert WorkspaceCoordinator._claims_conflict(normal, editor, other, writes) is False
    assert WorkspaceCoordinator._claims_conflict(normal, writes, other, writes) is True


def test_many_source_scopes_preserve_fifo_and_saved_bytes(tmp_path: Path) -> None:
    coordinator = WorkspaceCoordinator(resolve_state_paths(tmp_path / "state"))
    workspace = tmp_path / "workspace"
    workspace.mkdir()
    coordinator.register(workspace)
    owner, token = coordinator.start_task(workspace, "source-owner", "private scopes")
    leaf = workspace / "Assets" / "Active0" / "File0.cs"
    leaf.parent.mkdir(parents=True)
    leaf.write_bytes(b"preserve pending source bytes\n")
    sources = [
        coordinator.acquire_claim(
            workspace, token, writes=tuple(f"Assets/Active{i}/File{j}.cs" for j in range(100))
        )
        for i in range(12)
    ]
    held = coordinator.acquire_claim(workspace, token, resources=("unity-live",))
    peers = [coordinator.start_task(workspace, f"peer-{i}", "private FIFO") for i in range(3)]
    waiting = [
        coordinator.acquire_claim(
            workspace,
            peer_token,
            writes=tuple(f"Assets/Peer{i}/File{j}.cs" for j in range(100)),
            resources=("unity-live",),
            keep_queued=True,
        )
        for i, (_, peer_token) in enumerate(peers)
    ]
    assert all(claim["state"] == "queued" for claim in waiting)
    coordinator.release_claim(workspace, token, str(held["id"]))
    claims = {claim["id"]: claim for claim in coordinator.status(workspace)["claims"]}
    assert claims[waiting[0]["id"]]["state"] == "active"
    assert all(claims[claim["id"]]["state"] == "queued" for claim in waiting[1:])
    assert all(claims[claim["id"]]["state"] == "active" for claim in sources)
    assert claims[sources[0]["id"]]["task_id"] == owner["id"]
    assert leaf.read_bytes() == b"preserve pending source bytes\n"
    coordinator.release_claim(workspace, peers[0][1], str(waiting[0]["id"]))
    claims = {claim["id"]: claim for claim in coordinator.status(workspace)["claims"]}
    assert claims[waiting[1]["id"]]["state"] == "active"
