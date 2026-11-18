"""Stage signed ZE packages at Worker01 and prepare an immutable fallback release.

The existing admission catalog stays active until its verified candidate is
deployed. Publisher credentials stay at the existing Worker01 service boundary.
"""
from __future__ import annotations

import argparse
from copy import deepcopy
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import socket
import stat
import subprocess
import urllib.error
import urllib.request

from sign_release import PACKAGE, VERSION, catalog_json, run

PUBLIC = 'https://packages-preview.zerogamestudio.com'
ORIGIN = 'http://127.0.0.1:19196'
REPO = 'ZeroGameStudio-CN/zeroengine'
RUNTIME = Path('/opt/zgs-ze-upm-preview/current/node')
PUBLISHER = Path('/etc/zgs-ze-upm-preview/publisher.npmrc')
SUMMARY = ('displayName', 'description', 'unity', 'unityRelease')
NO_PROXY = urllib.request.build_opener(urllib.request.ProxyHandler({}))


def dump(path, value):
    with path.open('x', encoding='utf-8') as stream:
        json.dump(value, stream, indent=2, ensure_ascii=False)
        stream.write('\n')


def get(url, limit, missing=False):
    request = urllib.request.Request(url, headers={'User-Agent': 'UnityPackageManager/1.0 ZE-registry-operator'})
    try:
        with NO_PROXY.open(request, timeout=30) as response:
            data = response.read(limit + 1)
    except urllib.error.HTTPError as error:
        if missing and error.code == 404:
            return None
        raise
    if len(data) > limit:
        raise ValueError('Registry response exceeded the approved size')
    return data


def check_version(name, version, item, record):
    expected_url = f'{PUBLIC}/{name}/-/{record["asset"]}'
    if (item.get('name') != name or item.get('version') != version
            or item.get('zgsPilot', {}).get('sourceCommit') != record['sourceCommit']
            or item.get('zgsPilot', {}).get('sourceTree') != record['sourceTree']
            or item.get('dist', {}).get('integrity') != record['sha512']
            or item.get('dist', {}).get('tarball') != expected_url
            or ('dependencies' in record and item.get('dependencies', {}) != record['dependencies'])):
        raise ValueError(f'Registry metadata differs from reviewed {name}@{version}')


def verify_bytes(data, record):
    if len(data) != record['bytes'] or hashlib.sha256(data).hexdigest() != record['sha256']:
        raise ValueError('Registry archive digest differs: ' + record['asset'])


def stage_origin(cohorts, output):
    import fcntl

    if os.geteuid() != 0 or socket.gethostname() != 'zgs-worker-linux-01':
        raise ValueError('Use the established Worker01 root publisher')
    credentials = PUBLISHER.lstat()
    if not stat.S_ISREG(credentials.st_mode) or credentials.st_uid != 0 or stat.S_IMODE(credentials.st_mode) != 0o600:
        raise ValueError('The publisher credential must be a root-owned 0600 regular file')
    if subprocess.run(['systemctl', 'is-active', '--quiet', 'zgs-ze-upm-preview.service']).returncode:
        raise ValueError('The Worker01 registry is not ready')
    archives = {}
    for prepared, signed in cohorts:
        checked = run(prepared / 'release-input.json', signed, verify_only=True)
        if checked.get('schemaVersion') != 2:
            raise ValueError('Use a prepared generic release cohort')
        for name, value in checked['packages'].items():
            key = (name, checked['versions'][name])
            if key in archives:
                raise ValueError('Duplicate version in this publication: ' + str(key))
            archives[key] = (signed / name / value['asset'], value)
    output.mkdir(parents=True, exist_ok=False)
    with Path('/run/lock/zgs-ze-upm-preview-publish.lock').open('a') as lock:
        fcntl.flock(lock.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)
        entries = []
        for (name, version), (path, record) in archives.items():
            published = get(f'{ORIGIN}/{name}/{version}', 512 * 1024, missing=True)
            if published is None:
                environment = os.environ.copy()
                environment['PATH'] = str(RUNTIME / 'bin') + ':' + environment['PATH']
                command = [str(RUNTIME / 'bin/node'), str(RUNTIME / 'lib/node_modules/npm/bin/npm-cli.js'),
                           'publish', str(path), '--registry=' + ORIGIN + '/', '--userconfig=' + str(PUBLISHER),
                           '--cache=' + str(output / 'npm-cache'), '--tag=ze-pending', '--ignore-scripts',
                           '--no-audit', '--loglevel=error']
                result = subprocess.run(command, cwd=output, env=environment, capture_output=True, timeout=75)
                # An uncertain npm reply is reconciled by reading the exact version.
                published = get(f'{ORIGIN}/{name}/{version}', 512 * 1024, missing=True)
                if published is None:
                    raise RuntimeError(f'Worker01 did not store {name}@{version} (npm exit {result.returncode})')
            item = json.loads(published)
            item['dist']['tarball'] = f'{PUBLIC}/{name}/-/{record["asset"]}'
            check_version(name, version, item, record)
            verify_bytes(get(f'{ORIGIN}/{name}/-/{record["asset"]}', record['bytes']), record)
            shutil.copyfile(path, output / record['asset'])
            entries.append({'name': name, 'version': version, 'record': record, 'metadata': item})
        dump(output / 'origin-receipt.json', {'origin': ORIGIN, 'entries': entries})
    return {'verifiedOriginVersions': len(entries)}


def build_candidate(previous, entries, release_base, metadata):
    catalog = deepcopy(previous)
    catalog['updatedAt'] = datetime.now(timezone.utc).isoformat(timespec='seconds').replace('+00:00', 'Z')
    products = {}
    for entry in entries:
        name, version, record, item = (entry[key] for key in ('name', 'version', 'record', 'metadata'))
        if not PACKAGE.fullmatch(name) or not VERSION.fullmatch(version) or record['asset'] != name + '-' + version + '.tgz':
            raise ValueError('Unexpected origin release identity')
        check_version(name, version, item, record)
        product = catalog['packages'].setdefault(name, {'versions': {}})
        if version in product['versions']:
            raise ValueError(f'Preserve already admitted version {name}@{version}')
        if name not in products:
            # Old archives stay in their original releases. New metadata references
            # the complete set of verified versions without copying old archives.
            old_versions = previous['packages'].get(name, {}).get('versions', {})
            previous_base = product.get('githubBase')
            for old in product['versions'].values():
                old.setdefault('githubBase', previous_base)
            if old_versions and set(metadata[name]) != set(old_versions):
                raise ValueError('Previous public registry version inventory changed')
            rows = dict(metadata.get(name, {}))
            for old_version, old_record in old_versions.items():
                check_version(name, old_version, rows[old_version], old_record)
            product['githubBase'] = release_base
            product['metadataAsset'] = name + '-registry.json'
            tags = {key: value for key, value in product.get('tags', {}).items() if value in old_versions}
            if not tags and old_versions:
                tags['preview'] = list(old_versions)[-1]
            product['tags'] = tags
            products[name] = {'name': name, 'versions': rows, 'dist-tags': tags}
        product['versions'][version] = record
        products[name]['versions'][version] = item
        product['summary'] = {key: item[key] for key in SUMMARY if key in item}
        channel = 'preview' if '-' in version else 'latest'
        old = product['tags'].get(channel)
        if channel == 'preview' or old is None or tuple(map(int, old.split('.'))) < tuple(map(int, version.split('.'))):
            product['tags'][channel] = version
    for name, product in catalog['packages'].items():
        for version, value in product['versions'].items():
            for dependency, pin in value.get('dependencies', {}).items():
                if PACKAGE.fullmatch(dependency) and pin not in catalog['packages'].get(dependency, {}).get('versions', {}):
                    raise ValueError(f'Candidate dependency is not admitted: {name}@{version} needs {dependency}@{pin}')
    return catalog, products


def upload_backup(tag, commit, files):
    endpoint = f'repos/{REPO}/releases/tags/{tag}'
    def read_release():
        result = subprocess.run(['gh', 'api', endpoint], capture_output=True, timeout=45)
        if result.returncode:
            if b'HTTP 404' in result.stderr:
                return None
            raise RuntimeError('Unable to inspect the GitHub backup release')
        return json.loads(result.stdout)
    release = read_release()
    if release is None:
        command = ['gh', 'release', 'create', tag, '--repo', REPO, '--target', commit, '--prerelease',
                   '--notes', 'Verified immutable ZeroEngine Package Manager assets.', *map(str, files)]
        subprocess.run(command, capture_output=True, timeout=120)
        release = read_release()
    if release is None or release.get('draft') or release.get('tag_name') != tag:
        raise ValueError('The signed release backup did not read back')
    assets = {item['name']: item for item in release['assets']}
    missing = [path for path in files if path.name not in assets]
    if missing:
        subprocess.run(['gh', 'release', 'upload', tag, '--repo', REPO, *map(str, missing)], capture_output=True, timeout=120)
        release = read_release()
        if release is None:
            raise ValueError('The backup release disappeared after upload')
        assets = {item['name']: item for item in release['assets']}
    if set(assets) != {path.name for path in files}:
        raise ValueError('Backup asset inventory differs; preserve the existing release')
    for path in files:
        item = assets[path.name]
        if item['size'] != path.stat().st_size or item['digest'] != 'sha256:' + hashlib.sha256(path.read_bytes()).hexdigest():
            raise ValueError('Backup asset checksum differs: ' + path.name)
    return release['html_url']


def prepare_backup(catalog_path, exports, tag, output, trust_path):
    if not re.fullmatch(r'ze-upm-[a-z0-9][a-z0-9.-]{0,95}', tag):
        raise ValueError('Invalid immutable GitHub backup tag')
    previous = catalog_json(catalog_path)
    if previous['publicBase'] != PUBLIC:
        raise ValueError('The existing catalog hostname differs')
    entries, files = [], []
    for folder in exports:
        receipt = json.loads((folder / 'origin-receipt.json').read_text(encoding='utf-8'))
        if receipt['origin'] != ORIGIN:
            raise ValueError('Require verified Worker01 origin output')
        for entry in receipt['entries']:
            record = entry['record']
            path = folder / record['asset']
            if path.parent != folder or not path.is_file() or path.is_symlink():
                raise ValueError('Unexpected origin artifact path')
            verify_bytes(path.read_bytes(), record)
            entries.append(entry)
            files.append(path)
    commits = {entry['record']['sourceCommit'] for entry in entries}
    if len(commits) != 1:
        raise ValueError('Use one verified source commit per backup release')
    trust = json.loads(trust_path.read_text(encoding='utf-8-sig'))
    tested = {(item['name'], item['version'], item['sha256']) for item in trust['packages'] if item['signatureStatus'] == 'Valid'}
    if tested != {(entry['name'], entry['version'], entry['record']['sha256']) for entry in entries}:
        raise ValueError('Require Unity signature verification for each exact archive')
    before = {}
    for name in {entry['name'] for entry in entries} & previous['packages'].keys():
        before[name] = json.loads(get(PUBLIC + '/' + name, 512 * 1024))['versions']
    base = f'https://github.com/{REPO}/releases/download/{tag}/'
    candidate, products = build_candidate(previous, entries, base, before)
    output.mkdir(parents=True, exist_ok=False)
    for name, product in products.items():
        path = output / candidate['packages'][name]['metadataAsset']
        dump(path, product)
        files.append(path)
    backup = upload_backup(tag, commits.pop(), files)
    with (output / 'release-manifest.mjs').open('x', encoding='utf-8') as stream:
        stream.write('export default ' + json.dumps(candidate, indent=2) + ';\n')
    report = {'githubRelease': backup, 'verifiedArtifacts': len(entries), 'admissionReady': True,
              'previousCatalogSha256': hashlib.sha256(catalog_path.read_bytes()).hexdigest(),
              'candidateCatalogSha256': hashlib.sha256((output / 'release-manifest.mjs').read_bytes()).hexdigest()}
    dump(output / 'promotion-receipt.json', report)
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest='mode', required=True)
    origin = sub.add_parser('origin')
    origin.add_argument('--cohort', nargs=2, type=Path, action='append', required=True, metavar=('PREPARED', 'SIGNED'))
    origin.add_argument('--output', type=Path, required=True)
    backup = sub.add_parser('backup')
    backup.add_argument('--catalog', type=Path, required=True)
    backup.add_argument('--export', type=Path, action='append', required=True)
    backup.add_argument('--unity-trust-receipt', type=Path, required=True)
    backup.add_argument('--tag', required=True)
    backup.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    if args.mode == 'origin':
        result = stage_origin([[path.resolve() for path in pair] for pair in args.cohort], args.output.resolve())
    else:
        result = prepare_backup(args.catalog.resolve(), [path.resolve() for path in args.export], args.tag,
                                args.output.resolve(), args.unity_trust_receipt.resolve())
    print(json.dumps(result))


if __name__ == '__main__':
    main()
