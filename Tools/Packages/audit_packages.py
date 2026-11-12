"""Inspect a ZeroEngine package set without importing Unity or changing consumers."""

import argparse
import collections
import json
from pathlib import Path
import re
import subprocess


CANONICAL_REPOSITORY = "https://github.com/ZeroGameStudio-CN/zeroengine.git"


def load_json(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def dependency_closure(name, packages):
    found = set()
    pending = [name]
    while pending:
        current = pending.pop()
        for dependency in packages[current]["manifest"].get("dependencies", {}):
            if dependency in packages and dependency not in found:
                found.add(dependency)
                pending.append(dependency)
    return found


def inspect(root):
    root = root.resolve()
    packages = {}
    findings = []
    assemblies = collections.defaultdict(list)
    assembly_directories = collections.defaultdict(list)
    assembly_guids = collections.defaultdict(list)
    asset_guids = collections.defaultdict(list)

    def finding(code, package, path, detail, severity="error"):
        findings.append(dict(code=code, package=package, path=path, detail=detail, severity=severity))

    for manifest_path in sorted(root.glob("com.zerogamestudio.*/package.json")):
        name = manifest_path.parent.name
        manifest = load_json(manifest_path)
        files = sorted(p for p in manifest_path.parent.rglob("*") if p.is_file())
        paths = [p.relative_to(root).as_posix() for p in files]
        packages[name] = dict(
            manifest=manifest,
            path=manifest_path.relative_to(root).as_posix(),
            runtimeSources=sum("/Runtime/" in p and p.endswith(".cs") for p in paths),
            editorSources=sum("/Editor/" in p and "/Tests/" not in p and p.endswith(".cs") for p in paths),
            testSources=sum("/Tests/" in p and p.endswith(".cs") for p in paths),
            assemblies=[],
        )
        for key in ("name", "version", "displayName", "description", "unity", "author", "license", "repository"):
            if not manifest.get(key):
                finding("metadata.missing", name, packages[name]["path"], key)
        if manifest.get("name") != name:
            finding("metadata.name", name, packages[name]["path"], manifest.get("name"))
        repository = manifest.get("repository", {})
        if repository.get("url") != CANONICAL_REPOSITORY:
            finding("metadata.repository", name, packages[name]["path"], repository.get("url"))
        if repository.get("directory") != name:
            finding("metadata.directory", name, packages[name]["path"], repository.get("directory"))
        if not (manifest_path.parent / "README.md").is_file():
            finding("documentation.readme", name, packages[name]["path"], "Missing package README")
        for sample in manifest.get("samples", []):
            target = (manifest_path.parent / sample["path"]).resolve()
            if not target.is_relative_to(manifest_path.parent.resolve()) or not target.is_dir():
                finding("sample.path", name, packages[name]["path"], sample["path"])

        json_call_sites = []
        for source in files:
            parts = source.relative_to(manifest_path.parent).parts
            if source.suffix != ".cs" or not parts or parts[0] not in ("Runtime", "Editor") or "Tests" in parts:
                continue
            code = source.read_text(encoding="utf-8-sig")
            if any(re.search(r"\bJsonUtility\.(?:ToJson|FromJson|FromJsonOverwrite)\b", line)
                   and not line.lstrip().startswith("//") for line in code.splitlines()):
                json_call_sites.append(source.relative_to(root).as_posix())
        if json_call_sites and "com.unity.modules.jsonserialize" not in manifest.get("dependencies", {}):
            finding("dependency.jsonserialize", name, packages[name]["path"], json_call_sites)

        for path in files:
            relative = path.relative_to(root).as_posix()
            if path.suffix == ".meta":
                metadata = path.read_text(encoding="utf-8-sig")
                if not path.with_suffix("").exists() and "folderAsset: yes" not in metadata:
                    finding("asset.orphan-meta", name, relative, "Metadata has no source asset")
                match = re.search(r"^guid:\s*([a-fA-F0-9]{32})\s*$", metadata, re.MULTILINE)
                if match:
                    asset_guids[match[1].lower()].append(relative)
            if path.suffix != ".asmdef":
                continue
            assembly = load_json(path)
            entry = dict(package=name, path=relative, definition=assembly)
            assemblies[assembly["name"]].append(entry)
            assembly_directories[str(path.parent.relative_to(root))].append(relative)
            references = assembly.get("references", [])
            if (any(reference in ("UnityEditor.TestRunner", "UnityEngine.TestRunner") for reference in references)
                    and "/Tests/" not in relative
                    and "UNITY_INCLUDE_TESTS" not in assembly.get("defineConstraints", [])
                    and "com.unity.test-framework" not in manifest.get("dependencies", {})):
                finding("dependency.test-framework", name, relative, "Production assembly directly references Unity Test Framework")
            duplicate_references = [reference for reference, count in collections.Counter(references).items() if count > 1]
            if duplicate_references:
                finding("assembly.duplicate-reference", name, relative, duplicate_references)
            packages[name]["assemblies"].append(assembly["name"])
            meta = path.with_name(path.name + ".meta")
            if meta.is_file():
                match = re.search(r"^guid:\s*(\w+)", meta.read_text(encoding="utf-8-sig"), re.MULTILINE)
                if match:
                    assembly_guids[match[1].lower()].append(entry)

    for name, entries in assemblies.items():
        if len(entries) > 1:
            finding("assembly.duplicate", None, None, dict(name=name, paths=[x["path"] for x in entries]))
    for directory, paths in assembly_directories.items():
        if len(paths) > 1:
            finding("assembly.multiple-definitions", None, directory, paths)
    for guid, paths in asset_guids.items():
        if len(paths) > 1:
            finding("asset.duplicate-guid", None, None, dict(guid=guid, paths=paths))

    edges = collections.defaultdict(set)
    for name, package in packages.items():
        closure = dependency_closure(name, packages)
        for dependency in package["manifest"].get("dependencies", {}):
            if dependency.startswith("com.zerogamestudio.") and dependency not in packages:
                finding("dependency.missing-package", name, package["path"], dependency)
        for assembly_name in package["assemblies"]:
            for entry in assemblies[assembly_name]:
                if entry["package"] != name:
                    continue
                definition = entry["definition"]
                for reference in definition.get("references", []):
                    targets = assembly_guids.get(reference[5:].lower(), []) if reference.startswith("GUID:") else assemblies.get(reference, [])
                    if not targets:
                        if reference.startswith("ZeroEngine") or reference.startswith("GUID:"):
                            finding("assembly.unresolved-internal", name, entry["path"], reference)
                        continue
                    if len(targets) != 1:
                        continue
                    target = targets[0]
                    edges[assembly_name].add(target["definition"]["name"])
                    if target["package"] != name and target["package"] not in closure:
                        conditional = bool(definition.get("defineConstraints"))
                        finding("dependency.optional-undeclared" if conditional else "dependency.undeclared", name, entry["path"], dict(assembly=reference, package=target["package"], constraints=definition.get("defineConstraints", [])), "warning" if conditional else "error")
                    target_platforms = target["definition"].get("includePlatforms", [])
                    editor_only = "Editor" in definition.get("includePlatforms", []) or "UNITY_EDITOR" in definition.get("defineConstraints", [])
                    if "Editor" in target_platforms and not editor_only:
                        finding("assembly.runtime-to-editor", name, entry["path"], reference)

    cycles = set()
    visited = set()
    active = []

    def visit(name):
        if name in active:
            cycle = active[active.index(name):]
            rotations = [tuple(cycle[i:] + cycle[:i]) for i in range(len(cycle))]
            cycles.add(min(rotations))
            return
        if name in visited:
            return
        active.append(name)
        for target in sorted(edges[name]):
            visit(target)
        active.pop()
        visited.add(name)

    for name in sorted(assemblies):
        visit(name)
    for cycle in sorted(cycles):
        finding("assembly.cycle", None, None, list(cycle))
    return dict(schemaVersion=1, packages=packages, findings=findings)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[2])
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--check", action="store_true", help="Return nonzero when error findings remain")
    arguments = parser.parse_args()
    report = inspect(arguments.root)
    revision = subprocess.run(["git", "-C", str(arguments.root), "rev-parse", "HEAD"], capture_output=True, text=True)
    report["sourceRevision"] = revision.stdout.strip() if revision.returncode == 0 else None
    report["scope"] = "Static package health; not proof of runtime behavior or consumer adoption"
    arguments.output.parent.mkdir(parents=True, exist_ok=True)
    arguments.output.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    counts = dict(collections.Counter(x["code"] for x in report["findings"]))
    print(json.dumps(dict(packages=len(report["packages"]), findings=counts, output=str(arguments.output))))
    return 1 if arguments.check and any(x["severity"] == "error" for x in report["findings"]) else 0


if __name__ == "__main__":
    raise SystemExit(main())
