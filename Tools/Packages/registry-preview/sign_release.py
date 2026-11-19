"""Prepare and sign ZeroEngine release cohorts with Unity Package Manager.

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
PACKAGE = re.compile(r'com\.zerogamestudio\.(?:analytics|zeroengine\.[a-z0-9]+(?:[.-][a-z0-9]+)*)')
VERSION = re.compile(r'(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?')
GIT_ID = re.compile(r'[a-f0-9]{40}')


def catalog_json(path):
    value = path.read_text(encoding='utf-8-sig').strip()
    return json.loads(value.removeprefix('export default ').removesuffix(';'))


def tree_digest(files):
    digest = hashlib.sha256()
    for name, data in sorted(files.items()):
        digest.update(name.encode('utf-8') + b'\0' + hashlib.sha256(data).digest())
    return digest.hexdigest()


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
    omitted = set(expected) - set(actual)
    if (set(actual) - set(expected) != {ATTESTATION} or not actual[ATTESTATION]
            or any(PurePosixPath(path).name != '.gitignore' for path in omitted)):
        raise ValueError('Require the organization attestation and every packaged source file')
    normalized = actual['package.json'] != expected['package.json']
    if (json.loads(actual['package.json']) != json.loads(expected['package.json'])
            or any(actual[path] != expected[path] for path in set(expected) - omitted - {'package.json'})):
        raise ValueError('Packing changed package metadata or runtime bytes')
    metadata = json.loads(expected['package.json'])
    data = archive_path.read_bytes()
    return {'asset': archive_path.name, 'bytes': len(data), 'sha256': hashlib.sha256(data).hexdigest(),
            'sha512': 'sha512-' + base64.b64encode(hashlib.sha512(data).digest()).decode(),
            'sourceCommit': metadata['zgsPilot']['sourceCommit'],
            'sourceTree': metadata['zgsPilot']['sourceTree'], 'matchedSourceFiles': len(expected) - len(omitted),
            'sourceBytesUnchanged': not normalized and not omitted, 'runtimeBytesUnchanged': True,
            'packageJsonReformatted': normalized, 'omittedGitIgnore': sorted(omitted), 'signingOrganizationId': organization,
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
    if not GIT_ID.fullmatch(plan['commit']):
        raise ValueError('Require a frozen source commit')
    if not re.fullmatch(r'[1-9][0-9]*', plan['organizationKey']):
        raise ValueError('Require the signing organization ID from Unity Cloud')
    if plan.get('schemaVersion', 1) == 1:
        if tuple(plan['packages']) != NAMES or not VERSION.fullmatch(plan['version']):
            raise ValueError('Require a fixed version of the original Console cohort')
        return
    if plan['schemaVersion'] != 2 or not 1 <= len(plan['packages']) <= 128:
        raise ValueError('Require a bounded ZeroEngine release cohort')
    for name, item in plan['packages'].items():
        if (not PACKAGE.fullmatch(name) or not VERSION.fullmatch(item['version'])
                or not GIT_ID.fullmatch(item['sourceTree'])
                or not re.fullmatch(r'[a-f0-9]{64}', item['sourceSha256'])):
            raise ValueError('Invalid ZeroEngine package release identity: ' + name)
        for dependency, version in item['dependencies'].items():
            if dependency.startswith('com.zerogamestudio.') and plan['resolved'].get(dependency) != version:
                raise ValueError('Internal dependency is outside the verified cohort: ' + dependency)


def plan_cohort(load, selection, existing, organization):
    """Resolve package versions and preserve already admitted byte-identical packages."""
    resolved, pending, visiting, display_names = {}, {}, set(), {}

    def resolve(name):
        if not PACKAGE.fullmatch(name):
            raise ValueError('Project-local or non-ZE package cannot enter this feed: ' + name)
        if name in visiting:
            raise ValueError('Cyclic package dependencies at ' + name)
        if name in resolved:
            return resolved[name]
        visiting.add(name)
        tree, manifest = load(name)
        version = selection.get(name) or manifest['version']
        if manifest['name'] != name or not VERSION.fullmatch(version):
            raise ValueError('Invalid package identity or version: ' + name)
        author = manifest.get('author')
        if not isinstance(author, dict) or author.get('name') != 'ZeroGameStudio':
            raise ValueError(f'{name}: author.name must be ZeroGameStudio')
        label = manifest.get('displayName')
        if not isinstance(label, str) or not label.startswith('ZeroEngine ') or ' '.join(label.split()) != label:
            raise ValueError(f'{name}: displayName must use ZeroEngine <Module>')
        other = display_names.setdefault(label.casefold(), name)
        if other != name:
            raise ValueError(f'{name} and {other} share the displayName {label}')
        dependencies = {dep: resolve(dep) if dep.startswith('com.zerogamestudio.') else pin
                        for dep, pin in manifest.get('dependencies', {}).items()}
        old = existing.get(name, {}).get('versions', {}).get(version)
        if old is not None:
            if (old.get('sourceTree') != tree or old.get('dependencies', {}) != dependencies
                    or old.get('signingOrganizationId') != organization):
                raise ValueError(f'{name}@{version} is immutable. Set a new version for this package.')
        else:
            pending[name] = {'sourceTree': tree, 'version': version, 'dependencies': dependencies}
        visiting.remove(name)
        resolved[name] = version
        return version

    for name in sorted(selection):
        resolve(name)
    return resolved, pending


def plan_selected(repository, commit, selection, organization, existing):
    if not GIT_ID.fullmatch(commit):
        raise ValueError('Require a full commit ID')
    git(repository, 'merge-base', '--is-ancestor', commit, 'refs/remotes/origin/main')

    def load(name):
        tree = git(repository, 'rev-parse', commit + ':' + name).decode().strip()
        return tree, json.loads(git(repository, 'show', commit + ':' + name + '/package.json'))

    return plan_cohort(load, selection, existing, organization)


def prepare_selected(repository, commit, selection, organization, output, existing):
    resolved, pending = plan_selected(repository, commit, selection, organization, existing)
    if not pending:
        raise ValueError('Every requested package version is already admitted')
    payloads = {}
    for name, item in pending.items():
        payload = committed_files(repository, commit, name)
        if any(data.startswith(b'version https://git-lfs.github.com/spec/v1\n') for data in payload.values()):
            raise ValueError('Resolve source payload rather than publishing Git LFS pointers: ' + name)
        metadata = json.loads(payload['package.json'])
        metadata['version'] = item['version']
        if 'dependencies' in metadata or item['dependencies']:
            metadata['dependencies'] = item['dependencies']
        metadata['zgsPilot'] = {'sourceCommit': commit, 'sourceTree': item['sourceTree'], 'purpose': 'signed-registry-release'}
        payload['package.json'] = (json.dumps(metadata, indent=2) + '\n').encode()
        item['sourceSha256'] = tree_digest(payload)
        payloads[name] = payload
    plan = {'schemaVersion': 2, 'commit': commit, 'organizationKey': organization, 'resolved': resolved, 'packages': pending}
    validate_plan(plan)
    write_prepared(output, payloads, plan)
    return {'sourceCommit': commit, 'packages': resolved, 'newVersions': len(pending), 'input': str(output / 'release-input.json')}


def write_prepared(output, sources, plan):
    output.mkdir(parents=True, exist_ok=False)
    for name, files in sources.items():
        for relative, data in files.items():
            path = output / 'source' / name / relative
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(data)
    (output / 'release-input.json').write_text(json.dumps(plan, indent=2) + '\n', encoding='utf-8')


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
    write_prepared(output, sources, plan)
    return {'sourceCommit': commit, 'version': version, 'preparedPackages': len(sources),
            'input': str(output / 'release-input.json')}


def run(plan_path, output, upm=None, credential_path=None, verify_only=False):
    plan = json.loads(plan_path.read_text(encoding='utf-8-sig'))
    validate_plan(plan)
    generic = plan.get('schemaVersion', 1) == 2
    versions = {name: item['version'] for name, item in plan['packages'].items()} if generic else dict.fromkeys(NAMES, plan['version'])
    sources = {}
    for name, version in versions.items():
        source = plan_path.parent / 'source' / name
        if output == source or source in output.parents:
            raise ValueError('Keep signing output outside the prepared source')
        files = source_files(source)
        metadata = json.loads(files['package.json'])
        provenance = metadata.get('zgsPilot', {})
        if (metadata['name'] != name or metadata['version'] != version or provenance.get('sourceCommit') != plan['commit']
                or not re.fullmatch(r'[a-f0-9]{40}', provenance.get('sourceTree', ''))):
            raise ValueError('Prepared source identity differs from the release plan')
        dependencies = metadata.get('dependencies', {})
        if generic:
            info = plan['packages'][name]
            if (dependencies != info['dependencies'] or provenance['sourceTree'] != info['sourceTree']
                    or tree_digest(files) != info['sourceSha256']):
                raise ValueError('Prepared package bytes or dependencies differ from the release plan')
        else:
            siblings = set() if name == CORE else {CORE} if name == CORE + '.ui' else {CORE, CORE + '.ui'}
            if {key for key in dependencies if key.startswith('com.zerogamestudio.')} != siblings or any(
                    dependencies[key] != version for key in siblings):
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
        archive = output / name / (name + '-' + versions[name] + '.tgz')
        records[name] = verify_archive(archive, source, plan['organizationKey'])
    receipt = {'sourceCommit': plan['commit'], 'packages': records,
               'unityTrustVerificationRequired': True}
    if generic:
        receipt.update(schemaVersion=2, versions=versions, organizationKey=plan['organizationKey'])
    else:
        receipt['version'] = plan['version']
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
    parser.add_argument('--package', action='append', default=[], metavar='NAME[=VERSION]',
                        help='Select a public ZE package and its dependencies, optionally overriding its version')
    parser.add_argument('--against-catalog', type=Path, help='Reuse matching immutable versions already admitted')
    parser.add_argument('--organization-id')
    parser.add_argument('--upm', type=Path)
    parser.add_argument('--credentials', type=Path)
    parser.add_argument('--verify-only', action='store_true')
    args = parser.parse_args()
    if args.prepare_from_commit:
        if not (args.version or args.package) or (args.version and args.package) or not args.organization_id or args.upm or args.credentials or args.verify_only:
            parser.error('Preparation requires --organization-id plus --package or the original Console --version')
        repository = Path(__file__).resolve().parents[3]
        if args.package:
            selected = {}
            for value in args.package:
                name, separator, version = value.partition('=')
                if name in selected or (separator and not version):
                    parser.error('Specify each package once, with a complete version after =')
                selected[name] = version or None
            existing = catalog_json(args.against_catalog)['packages'] if args.against_catalog else {}
            result = prepare_selected(repository, args.prepare_from_commit, selected, args.organization_id, args.output.resolve(), existing)
        else:
            result = prepare(repository, args.prepare_from_commit, args.version, args.organization_id, args.output.resolve())
        print(json.dumps(result))
        return
    if args.version or args.package or args.against_catalog or args.organization_id:
        parser.error('The reviewed input file owns the signing version and organization')
    result = run(args.input.resolve(), args.output.resolve(), args.upm, args.credentials, args.verify_only)
    print(json.dumps({'sourceCommit': result['sourceCommit'], 'versions': result.get('versions', result.get('version')),
                      'archivesVerified': len(result['packages']), 'unityTrustVerificationRequired': True}))


if __name__ == '__main__':
    main()
