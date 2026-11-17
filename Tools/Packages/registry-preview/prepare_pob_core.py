"""Prepare immutable transport trials from the consumer's existing Console pin.

Reads the consumer manifest/lock and committed Git objects only. No consumer,
registry, Git ref, package source, or personal configuration is changed.
"""
from __future__ import annotations

import argparse
import base64
import hashlib
import io
import json
from pathlib import Path, PurePosixPath
import re
import subprocess
import tarfile

NAME = 'com.zerogamestudio.zeroengine.console'
SIBLINGS = (NAME, NAME + '.ui', NAME + '.quantum', 'com.zerogamestudio.zeroengine.ai-support')
REPOSITORY = 'https://github.com/ZeroGameStudio-CN/zeroengine.git'


def git(root, *args):
    return subprocess.check_output(['git', '-C', str(root), *args])


def read_pin(project):
    manifest_path = project / 'Packages/manifest.json'
    lock_path = project / 'Packages/packages-lock.json'
    manifest = json.loads(manifest_path.read_text(encoding='utf-8-sig'))
    lock = json.loads(lock_path.read_text(encoding='utf-8-sig'))
    pins = set()
    for name in SIBLINGS:
        locator = manifest.get('dependencies', {}).get(name, '')
        match = re.fullmatch(re.escape(REPOSITORY) + r'\?path=' + re.escape(name) + r'#([a-f0-9]{40})', locator)
        if not match:
            raise ValueError('Require the existing exact Git pin for ' + name)
        commit = match[1]
        node = lock.get('dependencies', {}).get(name, {})
        if node.get('source') != 'git' or node.get('hash') != commit or node.get('version') != locator:
            raise ValueError('Consumer manifest and lock differ for ' + name)
        pins.add(commit)
    if len(pins) != 1:
        raise ValueError('Preserve an existing version split; do not choose a baseline automatically')
    return pins.pop(), {'manifestSha256': hashlib.sha256(manifest_path.read_bytes()).hexdigest(),
                        'lockSha256': hashlib.sha256(lock_path.read_bytes()).hexdigest()}


def committed_files(repository, commit, package):
    archive = git(repository, 'archive', '--format=tar', commit, package)
    result = {}
    with tarfile.open(fileobj=io.BytesIO(archive)) as stream:
        for member in stream.getmembers():
            path = PurePosixPath(member.name)
            if path.parts[0] != package or '..' in path.parts:
                raise ValueError('Unexpected archive member')
            if member.isdir():
                continue
            if not member.isfile():
                raise ValueError('Only regular package files are admitted')
            result[PurePosixPath(*path.parts[1:]).as_posix()] = stream.extractfile(member).read()
    return result


def pack(output, files, version, commit, tree):
    original = json.loads(files['package.json'])
    if original['name'] != NAME or original.get('dependencies'):
        raise ValueError('This trial requires the dependency-free Console Core package')
    metadata = dict(original, version=version, zgsPilot={'sourceCommit': commit, 'sourceTree': tree,
        'purpose': 'consumer-locked-source-transport-trial'})
    payload = dict(files, **{'package.json': (json.dumps(metadata, indent=2) + '\n').encode()})
    raw = io.BytesIO()
    with tarfile.open(fileobj=raw, mode='w', format=tarfile.USTAR_FORMAT) as archive:
        for path, data in sorted(payload.items()):
            info = tarfile.TarInfo('package/' + path)
            info.size = len(data); info.mode = 0o644
            archive.addfile(info, io.BytesIO(data))
    import gzip
    packed = gzip.compress(raw.getvalue(), mtime=0)
    asset = NAME + '-' + version + '.tgz'
    with (output / asset).open('xb') as stream:
        stream.write(packed)
    return {'asset': asset, 'bytes': len(packed), 'sha256': hashlib.sha256(packed).hexdigest(),
            'sha512': 'sha512-' + base64.b64encode(hashlib.sha512(packed).digest()).decode(),
            'sourceCommit': commit, 'sourceTree': tree, 'runtimeFilesUnchanged': True}


def prepare(repository, project, output, release_tag):
    if output.exists():
        raise ValueError('Use a new exact output directory; preserve prior artifacts')
    commit, baseline = read_pin(project)
    tree = git(repository, 'rev-parse', commit + ':' + NAME).decode().strip()
    files = committed_files(repository, commit, NAME)
    base_version = json.loads(files['package.json'])['version']
    if not re.fullmatch(r'\d+\.\d+\.\d+', base_version):
        raise ValueError('Require an unambiguous stable package base version')
    output.mkdir(parents=True)
    versions = {}
    for sequence in (1, 2):
        version = base_version + '-ze-pob.' + commit[:8] + '.' + str(sequence)
        versions[version] = pack(output, files, version, commit, tree)
    # Agent is test instrumentation, embedded only in the disposable project.
    agent = output / 'agent-instrumentation'
    agent.mkdir()
    for relative, data in committed_files(repository, commit, SIBLINGS[-1]).items():
        target = agent / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(data)
    result = {'name': NAME, 'repository': REPOSITORY, 'sourceCommit': commit, 'sourceTree': tree,
        'consumer': str(project), 'baseline': baseline, 'publicBase': 'https://packages-preview.zerogamestudio.com',
        'githubBase': REPOSITORY.removesuffix('.git') + '/releases/download/' + release_tag + '/',
        'versions': versions, 'scope': 'same committed runtime; transport/version changes only'}
    (output / 'release-manifest.json').write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--project', required=True, type=Path)
    parser.add_argument('--output', required=True, type=Path)
    parser.add_argument('--release-tag', required=True)
    args = parser.parse_args()
    if not re.fullmatch(r'ze-upm-pob-core-[a-f0-9]{8}-\d{8}', args.release_tag):
        raise ValueError('Use a unique scoped pilot tag')
    repository = Path(__file__).resolve().parents[3]
    result = prepare(repository, args.project.resolve(), args.output.resolve(), args.release_tag)
    print(json.dumps({'sourceCommit': result['sourceCommit'], 'sourceTree': result['sourceTree'],
                      'versions': list(result['versions']), 'runtimeFilesUnchanged': True}))


if __name__ == '__main__':
    main()
