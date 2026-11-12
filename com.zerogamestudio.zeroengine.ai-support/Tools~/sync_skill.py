"""Preview or explicitly synchronize guidance from actual registered UPM packages."""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import shutil
import tempfile
import uuid

PACKAGE = "com.zerogamestudio.zeroengine.ai-support"
TOOL_VERSION = "0.1.0"
SCHEMA = 1
RECEIPT = ".zeroengine-sync.json"
MAX_BYTES = 8 * 1024 * 1024


class Conflict(ValueError):
    pass


def digest(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def encode(value) -> bytes:
    return (json.dumps(value, ensure_ascii=False, indent=2, sort_keys=True) + "\n").encode("utf-8")


def plain(path: Path) -> Path:
    path = path.absolute()
    for parent in (path, *path.parents):
        if parent.is_symlink() or (hasattr(parent, "is_junction") and parent.is_junction()):
            raise Conflict("Preserve linked paths: " + str(path))
        if parent.exists() and getattr(parent.stat(), "st_file_attributes", 0) & 0x400:
            raise Conflict("Preserve reparse paths: " + str(path))
    return path


def read(path: Path, maximum=MAX_BYTES) -> bytes:
    path = plain(path)
    if not path.is_file() or path.stat().st_size > maximum:
        raise Conflict("Require a bounded regular file: " + str(path))
    data = path.read_bytes()
    if len(data) > maximum:
        raise Conflict("Input grew beyond its bound")
    return data


def load(path: Path):
    def unique(pairs):
        value = {}
        for key, item in pairs:
            if key in value:
                raise Conflict("Duplicate JSON key: " + key)
            value[key] = item
        return value
    return json.loads(read(path).decode("utf-8-sig"), object_pairs_hook=unique)


def relative(value: str) -> str:
    path = PurePosixPath(value)
    if path.is_absolute() or ".." in path.parts or "\\" in value or ":" in value or not path.parts:
        raise Conflict("Invalid managed relative path")
    return path.as_posix()


def version(value: str):
    match = re.fullmatch(r"(\d+)\.(\d+)\.(\d+)", value or "")
    if not match:
        raise Conflict("Cannot order this tool version: " + str(value))
    return tuple(map(int, match.groups()))


def prepare(project: Path, snapshot_path: Path, *, allow_version_split=False,
            include_local_trials=False, accept_current_commit=False):
    project = plain(project.resolve())
    snapshot = load(snapshot_path)
    if snapshot.get("schemaVersion") != SCHEMA or Path(snapshot.get("projectPath", "")).resolve() != project:
        raise Conflict("Snapshot schema or project identity differs")
    manifest_path = project / "Packages/manifest.json"
    lock_path = project / "Packages/packages-lock.json"
    if digest(read(manifest_path)) != snapshot.get("manifestSha256") or digest(read(lock_path)) != snapshot.get("lockSha256"):
        raise Conflict("Package snapshot is stale; export actual PackageInfo again")
    manifest, lock = load(manifest_path), load(lock_path)
    packages = snapshot.get("packages", [])
    if not isinstance(packages, list) or not 1 <= len(packages) <= 256:
        raise Conflict("Invalid registered-package inventory")
    indexed, roots = {}, {}
    for package in packages:
        name = package.get("name", "")
        if not re.fullmatch(r"com\.zerogamestudio\.[a-z0-9.-]+", name) or name in indexed:
            raise Conflict("Duplicate or invalid package identity")
        resolved = Path(package["resolvedPath"])
        if not resolved.is_absolute():
            raise Conflict("PackageInfo requires an absolute resolved path")
        root = plain(resolved)
        metadata = load(root / "package.json")
        if metadata.get("name") != name or metadata.get("version") != package.get("version"):
            raise Conflict("Resolved package metadata differs: " + name)
        node = lock.get("dependencies", {}).get(name)
        if not isinstance(node, dict):
            raise Conflict("Registered package is absent from lock: " + name)
        source = package.get("source", "").lower()
        commit = package.get("gitHash") or ""
        repository = ""
        if source == "git":
            if not re.fullmatch(r"[a-fA-F0-9]{40}", commit) or node.get("source") != "git" or node.get("hash", "").lower() != commit.lower():
                raise Conflict("PackageInfo and locked Git commit differ: " + name)
            direct = manifest.get("dependencies", {}).get(name)
            if direct is not None and (not isinstance(direct, str) or direct.rsplit("#", 1)[-1].lower() != commit.lower()):
                raise Conflict("Direct Git dependency is not pinned to this exact commit: " + name)
            locator = node.get("version") or direct or metadata.get("repository", {}).get("url", "")
            if not isinstance(locator, str) or not locator:
                raise Conflict("Git repository identity is unavailable: " + name)
            repository = locator.split("#", 1)[0].split("?", 1)[0].rstrip("/").removesuffix(".git").lower()
        elif source == "registry":
            if node.get("source") != "registry" or node.get("version") != package["version"]:
                raise Conflict("Registry version differs: " + name)
        elif not include_local_trials:
            raise Conflict("Existing local trial requires --include-local-trials: " + name)
        indexed[name] = {"name": name, "version": package["version"], "source": package["source"],
                         "gitHash": commit.lower(), "resolvedPath": str(root),
                         "repository": repository,
                         "verification": "local-trial-unverified" if source not in ("git", "registry") else "installed-source-only"}
        roots[name] = root
    if PACKAGE not in indexed:
        raise Conflict("AI Support is not actually installed")
    tool = indexed[PACKAGE]
    if tool["version"] != TOOL_VERSION:
        raise Conflict("Run the synchronizer shipped with the installed AI Support version")
    repositories = {}
    for package in indexed.values():
        if package["gitHash"]:
            repositories.setdefault(package["repository"], set()).add(package["gitHash"])
    version_split = any(len(commits) > 1 for commits in repositories.values())
    if version_split and not allow_version_split:
        raise Conflict("Mixed Git pins require explicit --allow-version-split; no pin was changed")
    source_root = roots[PACKAGE]
    if read(source_root / "Tools~/sync_skill.py") != Path(__file__).read_bytes():
        raise Conflict("This script is older or modified; run the installed package's Tools~/sync_skill.py")
    template = source_root / "Skills~/zeroengine"
    desired = {}
    for path in sorted(template.rglob("*")):
        plain(path)
        if path.is_file():
            desired[relative(path.relative_to(template).as_posix())] = read(path)
    if "SKILL.md" not in desired:
        raise Conflict("Installed Skill template is missing")
    desired["scripts/sync_skill.py"] = Path(__file__).read_bytes()
    lines = ["# Installed package sources", "", "Generated from registered PackageInfo and the matching manifest/lock.",
             "These versions identify installed source; they do not establish compatibility, release or acceptance.", "",
             "| Package | Version | Commit / source | Module README |", "| --- | --- | --- | --- |"]
    for name, package in sorted(indexed.items()):
        readme = roots[name] / "README.md"
        link = "Read installed source"
        if readme.is_file():
            target = "references/packages/" + name + ".md"
            desired[target] = read(readme, 1024 * 1024)
            link = "[README](packages/" + name + ".md)"
        provenance = package["gitHash"] or package["source"]
        lines.append(f"| {name} | {package['version']} | `{provenance}` | {link} |")
    desired["references/installed-packages.md"] = ("\n".join(lines) + "\n").encode("utf-8")
    desired["references/installed-packages.json"] = encode({"schemaVersion": SCHEMA, "packages": indexed})
    if sum(map(len, desired.values())) > MAX_BYTES or len(desired) > 512:
        raise Conflict("Generated guidance exceeds its bounded size")
    target = plain(project / ".agents/skills/zeroengine")
    previous = None
    if target.exists():
        if not target.is_dir() or not (target / RECEIPT).is_file():
            raise Conflict("Preserve existing unowned project guidance")
        previous = load(target / RECEIPT)
        if previous.get("schemaVersion") != SCHEMA:
            raise Conflict("Preserve unknown Skill receipt schema")
        if version(previous.get("toolVersion")) > version(TOOL_VERSION):
            raise Conflict("Preserve guidance from a newer AI Support version")
        if (previous.get("toolVersion") == TOOL_VERSION and previous.get("toolGitHash") and
                previous["toolGitHash"] != tool["gitHash"] and not accept_current_commit):
            raise Conflict("Review this installed same-version commit, then use --accept-current-commit")
        for path, checksum in previous.get("files", {}).items():
            leaf = target / relative(path)
            if not leaf.is_file() or digest(read(leaf)) != checksum:
                raise Conflict("Preserve locally modified managed guidance: " + path)
    owned = set(previous.get("files", {})) if previous else set()
    for name in desired:
        if (target / name).exists() and name not in owned:
            raise Conflict("Preserve an unowned file at a generated path: " + name)
    receipt = dict(previous or {})
    receipt.update({"schemaVersion": SCHEMA, "toolVersion": TOOL_VERSION, "toolGitHash": tool["gitHash"],
                    "manifestSha256": snapshot["manifestSha256"], "lockSha256": snapshot["lockSha256"],
                    "versionSplit": version_split, "files": {name: digest(data) for name, data in sorted(desired.items())}})
    desired[RECEIPT] = encode(receipt)
    touched = sorted(set(desired) | owned)
    before = {name: read(target / name) if (target / name).is_file() else None for name in touched}
    changed = [name for name in touched if before[name] != desired.get(name)]
    return {"target": target, "desired": desired, "before": before, "changed": changed,
            "packages": len(indexed), "versionSplit": version_split,
            "inputs": {manifest_path: snapshot["manifestSha256"], lock_path: snapshot["lockSha256"]}}


def replace(path: Path, data: bytes):
    plain(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name("." + path.name + ".zeroengine-" + uuid.uuid4().hex)
    try:
        with temporary.open("xb") as stream:
            stream.write(data)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary, path)
    finally:
        if temporary.exists():
            temporary.unlink()


def apply(plan):
    target, desired, before = plan["target"], plan["desired"], plan["before"]
    if not plan["changed"]:
        return
    recovery = Path(tempfile.mkdtemp(prefix="zeroengine-skill-recovery-"))
    for name, data in before.items():
        if data is not None:
            leaf = recovery / name
            leaf.parent.mkdir(parents=True, exist_ok=True)
            leaf.write_bytes(data)
    (recovery / "transaction.json").write_bytes(encode({"target": str(target), "files": plan["changed"]}))
    written = []
    keep_recovery = False
    target_existed = target.exists()
    try:
        for path, checksum in plan["inputs"].items():
            if digest(read(path)) != checksum:
                raise Conflict("Package inputs changed after preview")
        for name in plan["changed"]:
            leaf = target / name
            current = read(leaf) if leaf.is_file() else None
            if current != before[name]:
                raise Conflict("Guidance changed after preview: " + name)
        # Receipt is last; interrupted transactions cannot impersonate a clean sync.
        for name in sorted(plan["changed"], key=lambda name: (name == RECEIPT, name)):
            leaf = target / name
            if (read(leaf) if leaf.is_file() else None) != before[name]:
                raise Conflict("Preserve a concurrent guidance write: " + name)
            if name in desired:
                replace(leaf, desired[name])
            else:
                plain(leaf).unlink()
            written.append(name)
        for name, data in desired.items():
            if read(target / name) != data:
                raise Conflict("Guidance readback changed: " + name)
    except BaseException:
        for name in reversed(written):
            leaf = target / name
            current = read(leaf) if leaf.is_file() else None
            if current != desired.get(name):
                keep_recovery = True
                continue
            try:
                if before[name] is None:
                    if leaf.exists():
                        leaf.unlink()
                else:
                    replace(leaf, before[name])
            except BaseException:
                keep_recovery = True
        if not target_existed:
            for name in sorted(plan["changed"], key=lambda name: name.count("/"), reverse=True):
                directory = (target / name).parent
                while directory == target or target in directory.parents:
                    try:
                        directory.rmdir()
                    except OSError:
                        break
                    directory = directory.parent
        if keep_recovery:
            raise Conflict("Preserved a concurrent write; recovery preimages: " + str(recovery))
        raise
    finally:
        if not keep_recovery:
            resolved = plain(recovery.resolve())
            if resolved.parent != Path(tempfile.gettempdir()).resolve() or not resolved.name.startswith("zeroengine-skill-recovery-"):
                raise Conflict("Preserve an unexpected recovery path")
            shutil.rmtree(resolved)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--project", type=Path, required=True)
    parser.add_argument("--snapshot", type=Path, required=True)
    action = parser.add_mutually_exclusive_group()
    action.add_argument("--apply", action="store_true")
    action.add_argument("--check", action="store_true")
    parser.add_argument("--allow-version-split", action="store_true")
    parser.add_argument("--include-local-trials", action="store_true")
    parser.add_argument("--accept-current-commit", action="store_true")
    args = parser.parse_args()
    try:
        plan = prepare(args.project, args.snapshot, allow_version_split=args.allow_version_split,
                       include_local_trials=args.include_local_trials, accept_current_commit=args.accept_current_commit)
        if args.apply:
            apply(plan)
        print(json.dumps({"status": "synchronized" if args.apply else "update-available" if plan["changed"] else "current",
                          "target": str(plan["target"]), "changed": plan["changed"],
                          "packages": plan["packages"], "versionSplit": plan["versionSplit"]}, ensure_ascii=False))
        return 3 if args.check and plan["changed"] else 0
    except (Conflict, OSError, ValueError, KeyError, TypeError) as error:
        print(json.dumps({"status": "conflict", "reason": str(error)}, ensure_ascii=False))
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
