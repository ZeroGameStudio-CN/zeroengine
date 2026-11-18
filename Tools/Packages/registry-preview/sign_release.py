"""Sign a prepared Console cohort, or verify its archive bytes without credentials.

Signing uses Unity's official UPM CLI and a root-only service-account file.
This command never publishes packages or changes consumer projects.
"""
from __future__ import annotations

import argparse
import base64
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import stat
import subprocess
import tarfile

from prepare_pob_core import committed_files, git, prepared_files

CORE = 'com.zerogamestudio.zeroengine.console'
NAMES = (CORE, CORE + '.ui', CORE + '.quantum')
KEYS = {'UPM_SERVICE_ACCOUNT_KEY_ID', 'UPM_SERVICE_ACCOUNT_KEY_SECRET'}
ATTESTATION = '.attestation.p7m'


def source_files(source):
    if not source.is_dir() or source.is_symlink():
        raise ValueError('Require a regular source directory')
    files = {}
    for path in source.rglob('*'):
        if path.is_symlink():
            raise ValueError('Source links are not admitted')
        if path.is_file():
            files[path.relative_to(source).as_posix()] = path.read_bytes()
    if 'package.json' not in files or ATTESTATION in files:
        raise ValueError('Require an unsigned prepared source package')
    return files


def verify_archive(archive_path, source, organization):
    expected = source_files(source)
    actual = {}
    with tarfile.open(archive_path) as archive:
        for member in archive.getmembers():
            path = PurePosixPath(member.name)
            if path.is_absolute() or '..' in path.parts or not path.parts or path.parts[0] != 'package':
                raise ValueError('Unexpected archive path')
            if member.isdir():
                continue
            if not member.isfile():
                raise ValueError('Archive links and special files are not admitted')
            relative = PurePosixPath(*path.parts[1:]).as_posix()
            if relative in actual or member.size > 16 * 1024**2:
                raise ValueError('Duplicate or oversized archive member')
            actual[relative] = archive.extractfile(member).read()
    if set(actual) != set(expected) | {ATTESTATION} or not actual[ATTESTATION]:
        raise ValueError('Require exactly one added organization attestation')
    if any(actual[path] != data for path, data in expected.items()):
        raise ValueError('Packing changed the prepared package bytes')
    metadata = json.loads(expected['package.json'])
    data = archive_path.read_bytes()
    return {'asset': archive_path.name, 'bytes': len(data), 'sha256': hashlib.sha256(data).hexdigest(),
            'sha512': 'sha512-' + base64.b64encode(hashlib.sha512(data).digest()).decode(),
            'sourceCommit': metadata['zgsPilot']['sourceCommit'],
            'sourceTree': metadata['zgsPilot']['sourceTree'], 'matchedSourceFiles': len(expected),
            'sourceBytesUnchanged': True, 'signingOrganizationId': organization,
            'dependencies': metadata.get('dependencies', {})}


def credentials(path):
    if os.name != 'posix' or os.geteuid() != 0:
        raise ValueError('Read the signing credential through its root operator route')
    info = path.lstat()
    if not stat.S_ISREG(info.st_mode) or info.st_uid != 0 or stat.S_IMODE(info.st_mode) != 0o600 or info.st_size > 16384:
        raise ValueError('Require a root-owned regular credential file with mode 0600')
    lines = path.read_text().splitlines()
    values = dict(line.split('=', 1) for line in lines)
    if len(lines) != 2 or set(values) != KEYS or any(not re.fullmatch(r'[!-~]{8,4096}', value) for value in values.values()):
        raise ValueError('Unexpected credential file shape')
    return values


def validate_plan(plan):
    if tuple(plan['packages']) != NAMES or not re.fullmatch(r'[a-f0-9]{40}', plan['commit']):
        raise ValueError('Require the exact Console cohort and a frozen source commit')
    if not re.fullmatch(r'[1-9][0-9]*', plan['organizationKey']):
        raise ValueError('Require the signing organization ID from Unity Cloud')
    if not re.fullmatch(r'\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?', plan['version']):
        raise ValueError('Require an explicit package version')


def prepare(repository, commit, version, organization, output):
    plan = {'commit': commit, 'version': version, 'packages': list(NAMES), 'organizationKey': organization}
    validate_plan(plan)
    sources = {}
    for name in NAMES:
        tree = git(repository, 'rev-parse', commit + ':' + name).decode().strip()
        files = committed_files(repository, commit, name)
        payload, metadata = prepared_files(files, version, commit, tree, name, version if name != CORE else None)
        metadata['zgsPilot']['purpose'] = 'signed-consumer-locked-source-transport'
        payload['package.json'] = (json.dumps(metadata, indent=2) + '\n').encode()
        sources[name] = payload
    output.mkdir(parents=True, exist_ok=False)
    for name, files in sources.items():
        for relative, data in files.items():
            path = output / 'source' / name / relative
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(data)
    (output / 'release-input.json').write_text(json.dumps(plan, indent=2) + '\n', encoding='utf-8')
    return {'sourceCommit': commit, 'version': version, 'preparedPackages': len(sources),
            'input': str(output / 'release-input.json')}


def run(plan_path, output, upm=None, credential_path=None, verify_only=False):
    plan = json.loads(plan_path.read_text(encoding='utf-8-sig'))
    validate_plan(plan)
    sources = {}
    for name in NAMES:
        source = plan_path.parent / 'source' / name
        if output == source or source in output.parents:
            raise ValueError('Keep signing output outside the prepared source')
        metadata = json.loads(source_files(source)['package.json'])
        provenance = metadata.get('zgsPilot', {})
        if (metadata['name'] != name or metadata['version'] != plan['version'] or provenance.get('sourceCommit') != plan['commit']
                or not re.fullmatch(r'[a-f0-9]{40}', provenance.get('sourceTree', ''))):
            raise ValueError('Prepared source identity differs from the release plan')
        siblings = set() if name == CORE else {CORE} if name == CORE + '.ui' else {CORE, CORE + '.ui'}
        dependencies = metadata.get('dependencies', {})
        if {key for key in dependencies if key.startswith('com.zerogamestudio.')} != siblings or any(
                dependencies[key] != plan['version'] for key in siblings):
            raise ValueError('The prepared Console dependency cohort differs')
        sources[name] = source
    if not verify_only:
        if upm is None or credential_path is None:
            raise ValueError('Signing requires an official UPM executable and a credential path')
        environment = os.environ.copy()
        environment.update(credentials(credential_path))
        output.mkdir(parents=True, exist_ok=False)
        for name, source in sources.items():
            destination = output / name
            destination.mkdir()
            result = subprocess.run([str(upm), '--log-level', '0', '--console-log-level', '0', 'pack', str(source),
                '--organization-id', plan['organizationKey'], '--destination', str(destination)],
                env=environment, stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=120)
            if result.returncode:
                # Do not persist CLI output: authentication diagnostics can contain credentials.
                raise RuntimeError(f'UPM signing failed for {name} (exit {result.returncode}); inspect the candidate before any retry')
    records = {}
    for name, source in sources.items():
        archive = output / name / (name + '-' + plan['version'] + '.tgz')
        records[name] = verify_archive(archive, source, plan['organizationKey'])
    receipt = {'sourceCommit': plan['commit'], 'version': plan['version'], 'packages': records,
               'unityTrustVerificationRequired': True}
    receipt_path = output / 'signed-release.json'
    if not verify_only:
        receipt_path.write_text(json.dumps(receipt, indent=2) + '\n', encoding='utf-8')
    return receipt


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    selection = parser.add_mutually_exclusive_group(required=True)
    selection.add_argument('--input', type=Path)
    selection.add_argument('--prepare-from-commit')
    parser.add_argument('--output', required=True, type=Path)
    parser.add_argument('--version')
    parser.add_argument('--organization-id')
    parser.add_argument('--upm', type=Path)
    parser.add_argument('--credentials', type=Path)
    parser.add_argument('--verify-only', action='store_true')
    args = parser.parse_args()
    if args.prepare_from_commit:
        if not args.version or not args.organization_id or args.upm or args.credentials or args.verify_only:
            parser.error('Preparation requires --version and --organization-id; signing flags belong to the next step')
        result = prepare(Path(__file__).resolve().parents[3], args.prepare_from_commit, args.version, args.organization_id, args.output.resolve())
        print(json.dumps(result))
        return
    if args.version or args.organization_id:
        parser.error('The reviewed input file owns the signing version and organization')
    result = run(args.input.resolve(), args.output.resolve(), args.upm, args.credentials, args.verify_only)
    print(json.dumps({'sourceCommit': result['sourceCommit'], 'version': result['version'],
                      'archivesVerified': len(result['packages']), 'unityTrustVerificationRequired': True}))


if __name__ == '__main__':
    main()
