"""Transfer prepared and signed cohorts through the existing Worker01 publisher."""
from __future__ import annotations

import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import shutil
import socket
import stat
import sys
import tarfile
import tempfile


SIGNING = Path('/opt/zgs-ze-upm-preview/signing/current')
UPM = Path('/opt/zgs-ze-upm-preview/tools/upm/9.36.0/UnityPackageManager')
CREDENTIAL = Path('/etc/zgs-ze-upm-preview/signing.env')
TOOLS = ('sign_release.py', 'prepare_pob_core.py', 'publish_release.py')
MAX_FILES = 32768
MAX_BYTES = 512 * 1024**2


def write_bundle(folder, stream, mode='w|'):
    with tarfile.open(fileobj=stream, mode=mode) as bundle:
        for path in sorted(folder.rglob('*')):
            if path.is_symlink():
                raise ValueError('Release bundles cannot contain links')
            if path.is_file():
                bundle.add(path, arcname=path.relative_to(folder).as_posix(), recursive=False)


def unpack_bundle(stream, folder):
    folder.mkdir(exist_ok=False)
    total = 0
    with tarfile.open(fileobj=stream, mode='r|*') as bundle:
        for count, member in enumerate(bundle, 1):
            path = PurePosixPath(member.name)
            total += member.size
            if (not member.isfile() or path.is_absolute() or not path.parts or '..' in path.parts
                    or '\\' in member.name or ':' in member.name
                    or count > MAX_FILES or total > MAX_BYTES):
                raise ValueError('Unexpected or oversized release bundle member')
            target = folder.joinpath(*path.parts)
            target.parent.mkdir(parents=True, exist_ok=True)
            with target.open('xb') as output:
                shutil.copyfileobj(bundle.extractfile(member), output)


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def tool_digest(data):
    return hashlib.sha256(data.replace(b'\r\n', b'\n')).hexdigest()


def require_trust(signed, proof):
    expected = {(name, signed['versions'][name], archive['sha256'])
                for name, archive in signed['packages'].items()}
    items = proof['packages']
    actual = {(row['name'], row['version'], row['sha256']) for row in items
              if row['signatureStatus'] == 'Valid' and row['source'] == 'LocalTarball'}
    if len(items) != len(expected) or actual != expected:
        raise ValueError('Require Unity verification of every exact signed archive before publishing')


def main(mode, run_id, plan_sha, tool_hashes):
    if os.geteuid() != 0 or socket.gethostname() != 'zgs-worker-linux-01':
        raise ValueError('Use the established Worker01 root publisher')
    if not re.fullmatch(r'[a-f0-9]{32}', run_id) or not re.fullmatch(r'[a-f0-9]{64}', plan_sha):
        raise ValueError('Invalid release identity')
    installed = SIGNING.resolve(strict=True)
    if installed.parent != SIGNING.parent / 'releases':
        raise ValueError('Use a versioned Worker01 signer')
    actual = {name: tool_digest((installed / name).read_bytes()) for name in TOOLS}
    if json.loads(tool_hashes) != actual:
        raise ValueError('Worker01 release tools differ from the reviewed source')
    sys.path.insert(0, str(installed))
    from sign_release import run

    root = Path('/var/tmp') / ('zgs-ze-release-' + run_id)
    if mode == 'sign':
        root.mkdir(mode=0o700, exist_ok=True)
    info = root.lstat()
    if (root.resolve() != root or not stat.S_ISDIR(info.st_mode)
            or info.st_uid != 0 or stat.S_IMODE(info.st_mode) != 0o700):
        raise ValueError('Unexpected Worker01 release staging directory')
    prepared, signed = root / 'prepared', root / 'signed'
    plan = prepared / 'release-input.json'
    if mode == 'sign':
        with tempfile.TemporaryDirectory(dir=root) as temp_dir:
            temporary = Path(temp_dir)
            incoming = temporary / 'prepared'
            unpack_bundle(sys.stdin.buffer, incoming)
            if digest(incoming / plan.name) != plan_sha:
                raise ValueError('Prepared release plan changed in transit')
            if not prepared.exists():
                incoming.rename(prepared)
            if digest(plan) != plan_sha:
                raise ValueError('An earlier prepared release must be preserved')
            if not signed.exists():
                candidate = temporary / 'signed'
                run(plan, candidate, UPM, CREDENTIAL)
                candidate.rename(signed)
            else:
                run(plan, signed, verify_only=True)
        write_bundle(signed, sys.stdout.buffer)
        return
    if digest(plan) != plan_sha:
        raise ValueError('Worker01 release inputs changed')
    if mode == 'origin':
        from publish_release import stage_origin

        receipt = json.loads((signed / 'signed-release.json').read_text(encoding='utf-8'))
        require_trust(receipt, json.load(sys.stdin.buffer))
        with tempfile.TemporaryDirectory(dir=root) as temp_dir:
            output = Path(temp_dir) / 'export'
            stage_origin([(prepared, signed)], output)
            sys.stdout.buffer.write((output / 'origin-receipt.json').read_bytes())
        return
    if mode == 'clean':
        shutil.rmtree(root)
        return
    raise ValueError('Unknown release operation')


if __name__ == '__main__':
    main(*sys.argv[1:])
