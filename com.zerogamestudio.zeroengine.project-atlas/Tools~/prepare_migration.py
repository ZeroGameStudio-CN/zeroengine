#!/usr/bin/env python3
"""Prepare a lossless schema-2 migration outside a consumer checkout; never modify it."""
import argparse
import copy
import hashlib
import json
import re
from pathlib import Path


def read(path):
    def unique(pairs):
        result = {}
        for key, value in pairs:
            if key in result:
                raise ValueError(f"Duplicate JSON property: {key}")
            result[key] = value
        return result
    return json.loads(path.read_text(encoding="utf-8-sig"), object_pairs_hook=unique)


def prepare(project, output):
    project, output = project.resolve(), output.resolve()
    if output == project or project in output.parents or output.exists():
        raise ValueError("Output must be a NEW directory outside the project")
    catalog = read(project / "docs/architecture/project-atlas.json")
    if catalog.get("schemaVersion") != 1:
        raise ValueError("Only an explicit schema-1 catalog can be migrated")
    sources = catalog.get("sources", [])
    references, systems = {}, {}
    for source in sources:
        if not re.fullmatch(r"docs/architecture/project-atlas/[^/\\]+\.json", source):
            raise ValueError(f"Unsupported source: {source}")
        path = project / source
        if path.is_symlink() or path.resolve().parent != project / "docs/architecture/project-atlas":
            raise ValueError(f"Linked source: {source}")
        fragment = read(path)
        if set(fragment) - {"schemaVersion", "references", "systems"}:
            raise ValueError("Unknown fragment fields; manual migration required")
        for key, destination in (("references", references), ("systems", systems)):
            for item in fragment.get(key, []):
                identity = item["id"]
                if not re.fullmatch(r"[a-z0-9]+(?:[._-][a-z0-9]+)*", identity) or identity in destination:
                    raise ValueError(f"Invalid/duplicate {key} ID: {identity}")
                destination[identity] = copy.deepcopy(item)
    original = {"references": copy.deepcopy(references), "systems": copy.deepcopy(systems)}
    files = {}
    for identity, reference in sorted(references.items()):
        contributions = []
        for system_id, system in sorted(systems.items()):
            contribution = {"id": identity + "." + system_id, "systemId": system_id}
            for key in ("entryRefs", "structureRefs", "verificationRefs"):
                values = system["program"].get(key, [])
                if identity in values:
                    contribution[key] = [identity]
                    system["program"][key] = [value for value in values if value != identity]
            if len(contribution) > 2:
                contributions.append(contribution)
        files[f"docs/architecture/project-atlas/modules/{identity}.atlas.json"] = {
            "schemaVersion": 1, "references": [reference], "systems": [], "contributions": contributions}
    for identity, system in sorted(systems.items()):
        files[f"docs/architecture/project-atlas/contracts/{identity}.atlas.json"] = {
            "schemaVersion": 1, "references": [], "systems": [system]}
    catalog["schemaVersion"] = 2
    catalog.pop("sources")
    catalog["sourceDirectories"] = ["docs/architecture/project-atlas/contracts", "docs/architecture/project-atlas/modules"]
    files["docs/architecture/project-atlas.json"] = catalog
    output.mkdir(parents=True)
    for relative, data in files.items():
        target = output / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    receipt = {"sourceHashes": {name: hashlib.sha256((project / name).read_bytes()).hexdigest()
                                for name in ["docs/architecture/project-atlas.json"] + sources},
               "originalSources": sources, "newSources": sorted(files),
               "references": len(references), "systems": len(systems),
               "originalGraph": original,
               "next": "Validate generated catalog; review order changes in program reference lists; apply under consumer coordination. Do not remove legacy index until all reader entrypoints use schema-2 CLI."}
    (output / "migration.json").write_text(json.dumps(receipt, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    return receipt


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--project", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    receipt = prepare(args.project, args.output)
    print(json.dumps({"output": str(args.output), "references": receipt["references"], "systems": receipt["systems"]}))
