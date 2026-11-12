"""Private online SQLite recovery bundles, including pinned runtime packages."""
from __future__ import annotations

import hashlib
import gzip
import io
import json
import os
from contextlib import closing
from pathlib import Path, PurePosixPath
import re
import sqlite3
import tarfile

CHUNK = 1024 * 1024


def _signature(path):
    if path.is_symlink() or not path.is_file():
        raise ValueError("Recovery input missing or linked")
    stat = path.stat()
    return stat.st_dev, stat.st_ino, stat.st_size, stat.st_mtime_ns, stat.st_ctime_ns


class _HashingReader:
    def __init__(self, stream):
        self.stream, self.digest = stream, hashlib.sha256()
        self.size = 0

    def read(self, size=CHUNK):
        data = self.stream.read(min(size, CHUNK) if size >= 0 else CHUNK)
        self.digest.update(data)
        self.size += len(data)
        return data


def _hash_file(path):
    with path.open("rb") as source:
        reader = _HashingReader(source)
        while reader.read():
            pass
        return reader.digest.hexdigest()


def _runtime_files(app):
    files = {"app/"+path.name: path for path in sorted(app.glob("*.py")) if not path.name.startswith("test_")}
    for name in ("requirements.txt", "requirements-postgres.txt", "constraints.txt"):
        if (app/name).is_file():
            files["app/"+name] = app/name
    if "app/app.py" not in files or "app/service.py" not in files:
        raise ValueError("Application recovery sources missing")
    source = app/"zeroengine-source.json"
    expected = {}
    if source.exists():
        if (app/"ze_leaderboards").is_symlink():
            raise ValueError("Runtime package must not be linked")
        _signature(source)
        snapshot = json.loads(source.read_text(encoding="utf-8"))
        if snapshot.get("schemaVersion") != 1 or not re.fullmatch(r"[0-9a-f]{40}", snapshot.get("revision", "")):
            raise ValueError("Invalid runtime source provenance")
        files["app/zeroengine-source.json"] = source
        for relative, digest in snapshot["files"].items():
            parts = PurePosixPath(relative).parts
            if len(parts) != 2 or parts[0] != "ze_leaderboards" or "\\" in relative or not re.fullmatch(r"[0-9a-f]{64}", digest):
                raise ValueError("Invalid runtime source member")
            if parts[1] != "LICENSE" and not re.fullmatch(r"[A-Za-z_][A-Za-z_0-9]*\.py", parts[1]):
                raise ValueError("Invalid runtime source member")
            path = app.joinpath(*parts)
            _signature(path)
            files["app/"+relative] = path
            expected["app/"+relative] = digest
        actual = {p.name for p in (app/"ze_leaderboards").iterdir() if p.suffix == ".py" or p.name == "LICENSE"}
        if actual != {PurePosixPath(name).name for name in snapshot["files"]} or not actual:
            raise ValueError("Runtime package and source manifest differ")
    elif (app/"ze_leaderboards").exists():
        raise ValueError("Runtime package provenance missing")
    return files, expected


def verify_bundle(path):
    """Stream every member before sealing/rotation; never extract secrets here."""
    actual, manifest = {}, None
    # gzip.GzipFile bounds decompression reads. tarfile's own r|gz decoder can
    # inflate a highly compressible database into a very large intermediate buffer.
    with gzip.open(path, "rb") as stream, tarfile.open(fileobj=stream, mode="r|") as archive:
        for member in archive:
            parts = PurePosixPath(member.name).parts
            if not member.isfile() or not parts or ".." in parts or member.name.startswith("/") or member.name in actual:
                raise ValueError("Invalid recovery archive member")
            with archive.extractfile(member) as stream:
                if member.name == "manifest.json":
                    if manifest is not None or member.size > CHUNK:
                        raise ValueError("Invalid recovery manifest")
                    manifest = json.loads(stream.read(CHUNK))
                else:
                    reader = _HashingReader(stream)
                    while reader.read():
                        pass
                    actual[member.name] = {"bytes": reader.size, "sha256": reader.digest.hexdigest()}
    if not manifest or actual != manifest.get("files"):
        raise ValueError("Recovery archive incomplete or changed")
    return manifest


def backup(layout, root, stamp, *, manifest_schema="zeroengine-leaderboard-recovery-v1"):
    if not re.fullmatch(r"\d{8}T\d{6}Z", stamp):
        raise ValueError("Invalid snapshot timestamp")
    root = Path(root)
    root.mkdir(mode=0o700, parents=True, exist_ok=True)
    if root.is_symlink() or (os.name == "posix" and root.stat().st_mode & 0o077):
        raise ValueError("Backup directory must remain private")
    database, bundle = root/f"runs-{stamp}.sqlite3", root/f"recovery-{stamp}.tar.gz"
    temporary, archive = root/f".runs-{stamp}.partial", root/f".recovery-{stamp}.partial"
    if any(os.path.lexists(path) for path in (database, bundle, temporary, archive)):
        raise ValueError("Snapshot already exists or is unfinished")
    try:
        with closing(sqlite3.connect(Path(layout["ledger"]).resolve().as_uri()+"?mode=ro", uri=True, timeout=30)) as source, \
                closing(sqlite3.connect(temporary)) as target:
            source.backup(target, pages=256, sleep=0.05)
            if target.execute("PRAGMA quick_check").fetchone()[0] != "ok":
                raise ValueError("Snapshot integrity failed")
        temporary.chmod(0o600)
        files, expected = _runtime_files(Path(layout["app"]).resolve(strict=True))
        files["state/runs.sqlite3"] = temporary
        for name in ("policy", "signing-key", "publisher-key"):
            files["config/"+name] = Path(layout[name])
        for name, path in layout.get("units", {}).items():
            if not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9_.@-]*\.(?:service|timer)", name):
                raise ValueError("Invalid recovery service unit")
            files["systemd/"+name] = Path(path)
        signatures = {name: _signature(path) for name, path in files.items()}
        manifest = {"schema": manifest_schema, "createdUtc": stamp, "files": {}}
        with tarfile.open(archive, "w:gz") as output:
            archive.chmod(0o600)
            for name, path in files.items():
                with path.open("rb") as stream:
                    info = os.fstat(stream.fileno())
                    if (info.st_dev, info.st_ino, info.st_size, info.st_mtime_ns, info.st_ctime_ns) != signatures[name]:
                        raise ValueError("Recovery input changed during snapshot")
                    reader = _HashingReader(stream)
                    entry = tarfile.TarInfo(name)
                    entry.size, entry.mode = info.st_size, 0o600
                    output.addfile(entry, reader)
                digest = reader.digest.hexdigest()
                if name in expected and digest != expected[name]:
                    raise ValueError("Runtime source hash mismatch")
                manifest["files"][name] = {"bytes": reader.size, "sha256": digest}
            data = json.dumps(manifest, sort_keys=True, indent=2).encode()
            entry = tarfile.TarInfo("manifest.json")
            entry.size, entry.mode = len(data), 0o600
            output.addfile(entry, io.BytesIO(data))
        if any(_signature(files[name]) != signature for name, signature in signatures.items()):
            raise ValueError("Recovery input changed during snapshot")
        verify_bundle(archive)
        for path in (temporary, archive):
            with path.open("rb+") as stream:
                os.fsync(stream.fileno())
        os.replace(temporary, database)
        os.replace(archive, bundle)
        # Existing local retention only; never touch operations/offsite holds.
        for pattern, keep in (("runs-*.sqlite3", 1), ("recovery-*.tar.gz", 3)):
            candidates = [path for path in sorted(root.glob(pattern)) if re.fullmatch(
                r"(?:runs-\d{8}T\d{6}Z\.sqlite3|recovery-\d{8}T\d{6}Z\.tar\.gz)", path.name)]
            for path in candidates[:-keep]:
                if path.is_file() and not path.is_symlink():
                    path.unlink()
        return {"database": database.name, "bundle": bundle.name, "bytes": bundle.stat().st_size,
                "sha256": _hash_file(bundle)}
    finally:
        for path in (temporary, archive):
            if path.is_file() and not path.is_symlink():
                path.unlink()
