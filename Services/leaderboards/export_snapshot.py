"""Export an immutable server-library snapshot for standalone deployments."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess


REPOSITORY = Path(__file__).resolve().parents[2]
PACKAGE = "Services/leaderboards/ze_leaderboards/"


def git(*args):
    return subprocess.check_output(["git", "-C", str(REPOSITORY), *args])


def export(revision, output):
    commit = git("rev-parse", "--verify", "--end-of-options", revision + "^{commit}").decode().strip()
    paths = git("ls-tree", "-r", "--name-only", commit, "--", PACKAGE).decode().splitlines()
    if not paths or any(not p.startswith(PACKAGE) or not p.endswith(".py") for p in paths):
        raise ValueError("Expected a committed server-library source tree")
    payload = {p.removeprefix("Services/leaderboards/"): git("show", commit + ":" + p) for p in paths}
    payload["ze_leaderboards/LICENSE"] = git("show", commit + ":LICENSE")
    output.mkdir(parents=True, exist_ok=False)
    hashes = {}
    for relative, data in payload.items():
        target = output / relative
        target.parent.mkdir(exist_ok=True)
        target.write_bytes(data)
        hashes[relative] = hashlib.sha256(data).hexdigest()
    manifest = {
        "schemaVersion": 1, "repository": "https://github.com/ZeroGameStudio-CN/zeroengine.git",
        "revision": commit, "sourcePath": "Services/leaderboards", "files": hashes,
    }
    (output / "zeroengine-source.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    return manifest


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--revision", required=True, help="Tested Git commit; no floating runtime fetch.")
    parser.add_argument("--output", required=True, type=Path, help="New directory outside the game/server runtime.")
    args = parser.parse_args()
    manifest = export(args.revision, args.output.resolve())
    print(json.dumps({"revision": manifest["revision"], "files": len(manifest["files"])}))


if __name__ == "__main__":
    main()
