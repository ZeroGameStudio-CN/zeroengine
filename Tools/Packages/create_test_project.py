"""Create a new isolated Unity package test project, selecting compatible packages."""

import argparse
import json
from pathlib import Path
import re
import shutil


LANES = ("legacy", "modular", "dashboard-only", "dashboard-with-modules", "modules-only", "lifecycle", "console-core", "console-native")
PREFIX = "com.zerogamestudio.zeroengine"


def unity_version(value):
    match = re.match(r"^(\d+)\.(\d+)(?:\.(\d+))?", value)
    if not match:
        raise ValueError("Invalid Unity version: " + value)
    return tuple(int(part or 0) for part in match.groups())


def select_packages(packages, lane, editor_version):
    """Return the lane's compatible sibling closure, with explicit exclusions."""
    target = unity_version(editor_version)
    compatible = {name for name, manifest in packages.items()
                  if unity_version(manifest["unity"]) <= target}
    excluded = {name: "requires Unity " + manifest["unity"]
                for name, manifest in packages.items() if name not in compatible}
    while True:
        removed = {}
        for name in sorted(compatible):
            for dependency in packages[name].get("dependencies", {}):
                if dependency.startswith("com.zerogamestudio.") and dependency not in compatible:
                    removed[name] = "requires unavailable sibling " + dependency
                    break
        if not removed:
            break
        compatible.difference_update(removed)
        excluded.update(removed)

    if lane == "legacy":
        seeds = {PREFIX}
    elif lane == "dashboard-only":
        seeds = {PREFIX + ".dashboard", PREFIX + ".editor-ui"}
    elif lane == "dashboard-with-modules":
        seeds = {PREFIX + suffix for suffix in (".dashboard", ".editor-ui", ".project-atlas", ".core", ".ui")}
        seeds.add("com.zerogamestudio.analytics")
    elif lane == "lifecycle":
        seeds = {PREFIX + ".core", PREFIX + ".audio"}
    elif lane == "console-core":
        seeds = {PREFIX + ".console"}
    elif lane == "console-native":
        seeds = {PREFIX + ".console.ui"}
    elif lane in ("modular", "modules-only"):
        seeds = compatible - {PREFIX}
        if lane == "modules-only":
            seeds.discard(PREFIX + ".dashboard")
    else:
        raise ValueError("Unknown package lane: " + lane)

    unavailable = seeds - compatible
    if unavailable:
        raise ValueError("Lane requires unavailable packages: " + ", ".join(sorted(unavailable)))
    selected = set(seeds)
    pending = list(seeds)
    while pending:
        for dependency in packages[pending.pop()].get("dependencies", {}):
            if dependency in packages and dependency not in selected:
                selected.add(dependency)
                pending.append(dependency)
    return sorted(selected), excluded


def create_project(root, destination, lane, editor_version, test_framework="1.3.9"):
    root = Path(root).resolve()
    destination = Path(destination).resolve()
    if destination == root or root.is_relative_to(destination):
        raise ValueError("The test destination must not contain the source repository")
    if destination.exists() and any(destination.iterdir()):
        raise ValueError("Refusing to replace an existing project: " + str(destination))
    manifests = {path.parent.name: json.loads(path.read_text(encoding="utf-8-sig"))
                 for path in root.glob("com.zerogamestudio.*/package.json")}
    if any(destination.is_relative_to(root / name) for name in manifests):
        raise ValueError("The test destination must not be inside a source package")
    selected, excluded = select_packages(manifests, lane, editor_version)
    (destination / "Assets").mkdir(parents=True, exist_ok=True)
    (destination / "Packages").mkdir()
    (destination / "ProjectSettings").mkdir()
    dependencies = {"com.unity.test-framework": test_framework}
    for name in selected:
        shutil.copytree(root / name, destination / "Packages" / name,
                        ignore=shutil.ignore_patterns("__pycache__", "*.pyc"))
        dependencies[name] = "file:" + name
    if lane == "dashboard-with-modules":
        fixture = root / "Tools/Tests/Fixtures/com.zerogamestudio.dashboard-removal-fixture"
        shutil.copytree(fixture, destination / "Packages" / fixture.name)
        dependencies[fixture.name] = "file:" + fixture.name
    manifest = dict(dependencies=dependencies, testables=selected)
    (destination / "Packages/manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    (destination / "ProjectSettings/ProjectVersion.txt").write_text(
        "m_EditorVersion: " + editor_version + "\n", encoding="utf-8")
    receipt = dict(schemaVersion=1, editorVersion=editor_version, lane=lane,
                   selected=selected, incompatible=excluded)
    (destination / "package-selection.json").write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")
    return receipt


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[2])
    parser.add_argument("--destination", type=Path, required=True)
    parser.add_argument("--lane", choices=LANES, required=True)
    parser.add_argument("--unity-version", required=True)
    parser.add_argument("--test-framework", default="1.3.9")
    args = parser.parse_args()
    receipt = create_project(args.root, args.destination, args.lane, args.unity_version, args.test_framework)
    print(json.dumps(dict(selected=len(receipt["selected"]), incompatible=receipt["incompatible"])))


if __name__ == "__main__":
    main()
