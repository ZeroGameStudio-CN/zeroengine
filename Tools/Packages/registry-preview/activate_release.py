"""Deploy a verified admission catalog to the existing ZE Cloudflare Worker."""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import time
import urllib.error
import uuid

from publish_release import PUBLIC, check_version, dump, get, verify_bytes
from sign_release import catalog_json

WORKER = 'zgs-ze-upm-preview'
SOURCE = Path(__file__).resolve().parent
INDEX_LIMIT = 2 * 1024 * 1024


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def selected_packages(previous, candidate):
    additions = []
    for name, old_product in previous['packages'].items():
        product = candidate['packages'][name]
        for version, old in old_product['versions'].items():
            new = product['versions'][version]
            for key, value in old.items():
                if new.get(key) != value:
                    raise ValueError(f'Admitted version changed: {name}@{version}')
            if old.get('githubBase', old_product['githubBase']) != new.get('githubBase', product['githubBase']):
                raise ValueError(f'Previously admitted backup location changed: {name}@{version}')
    for name, product in candidate['packages'].items():
        for version, item in product['versions'].items():
            if version not in previous['packages'].get(name, {}).get('versions', {}):
                additions.append((name, version, item))
    if not additions:
        raise ValueError('No new versions in the promotion')
    return additions


def check_public_index(data, catalog):
    """Check the complete admitted version and provenance inventory in one request."""
    index = json.loads(data)
    packages = catalog['packages']
    if set(index) - {'_updated'} != packages.keys():
        raise ValueError('The public registry package inventory differs')
    for name, package in packages.items():
        row = index[name]
        versions = package['versions']
        tags = package.get('tags', {'preview': list(versions)[-1]})
        if (row.get('dist-tags') != tags or row.get('name') != name
                or row.get('versions', {}).keys() != versions.keys()):
            raise ValueError('The public registry versions or tags differ: ' + name)
        for version, record in versions.items():
            check_version(name, version, row['versions'][version], record)


def wrangler_call(node, wrangler, directory, args):
    environment = dict(os.environ, CI='true', WRANGLER_SEND_METRICS='false')
    flags = ['--config', str(directory / 'wrangler.toml'), '--name', WORKER]
    return subprocess.run([str(node), str(wrangler), *args, *flags], cwd=directory,
                          env=environment, capture_output=True, timeout=120)


def active_version(node, wrangler, directory):
    result = wrangler_call(node, wrangler, directory, ['deployments', 'status', '--json'])
    if result.returncode:
        raise RuntimeError('The existing ZE Worker deployment cannot be read')
    versions = json.loads(result.stdout).get('versions', [])
    if len(versions) != 1 or versions[0]['percentage'] != 100:
        raise ValueError('The existing Worker has a staged rollout; preserve it')
    return str(uuid.UUID(versions[0]['version_id']))


def activate(promotion, catalog_path, node, wrangler, expected, resume=None, unity_probe=None):
    expected = str(uuid.UUID(expected))
    before_catalog = catalog_json(catalog_path)
    next_path = promotion / 'release-manifest.mjs'
    candidate = catalog_json(next_path)
    evidence = json.loads((promotion / 'promotion-receipt.json').read_text(encoding='utf-8'))
    if (not evidence['admissionReady'] or sha(catalog_path) != evidence['previousCatalogSha256']
            or sha(next_path) != evidence['candidateCatalogSha256'] or candidate['publicBase'] != PUBLIC):
        raise ValueError('Registry promotion does not match its reviewed catalog receipt')
    additions = selected_packages(before_catalog, candidate)
    if len(additions) != evidence['verifiedArtifacts']:
        raise ValueError('Unexpected new version count in the promotion')

    stage = promotion / ('edge-resumed' if resume else 'edge-versioned')
    stage.mkdir(exist_ok=False)
    for name in ('edge-router.mjs', 'wrangler.toml'):
        shutil.copyfile(SOURCE / name, stage / name)
    shutil.copyfile(next_path, stage / next_path.name)
    def cli(args):
        return wrangler_call(node, wrangler, stage, args)

    def current():
        return active_version(node, wrangler, stage)

    if current() != expected:
        raise ValueError('The ZE Worker version changed before deployment')
    build = hashlib.sha256(next_path.read_bytes() + (stage / 'edge-router.mjs').read_bytes()).hexdigest()
    tag = 'ze-' + build[:16]
    dump(stage / 'activation-intent.json', {'previousVersion': expected, 'catalogSha256': sha(next_path), 'versionTag': tag})
    if resume:
        uploaded = json.loads((resume / 'uploaded-version.json').read_text(encoding='utf-8'))
        restored = json.loads((resume / 'rollback-receipt.json').read_text(encoding='utf-8'))
        if (uploaded['versionTag'] != tag or restored['restoredVersion'] != expected
                or restored['rejectedVersion'] != uploaded['stagedVersion']
                or any(sha(resume / name) != sha(stage / name) for name in ('edge-router.mjs','release-manifest.mjs','wrangler.toml'))):
            raise ValueError('The previous rollout must be restored and have identical source and bindings')
        staged = str(uuid.UUID(uploaded['stagedVersion']))
    else:
        # Upload code and bindings without reconciling the Worker custom-domain routes.
        result = cli(['versions', 'upload', '--keep-vars', '--tag', tag, '--message', 'ZE registry ' + build[:16]])
        match = re.search(rb'\bVersion ID:\s*([a-fA-F0-9-]{36})', result.stdout)
        if result.returncode or not match:
            raise RuntimeError(f'Wrangler upload returned {result.returncode}; inspect the version tag and retained intent')
        staged = str(uuid.UUID(match[1].decode('ascii')))
    dump(stage / 'uploaded-version.json', {'stagedVersion': staged, 'versionTag': tag})
    if current() != expected:
        raise ValueError('The active Worker changed during version upload')
    result = cli(['versions', 'deploy', staged + '@100%', '--yes', '--message', 'Verified ZE registry promotion'])
    # A lost deploy reply requires exact readback rather than automatic replay.
    active = current()
    if active != staged:
        raise RuntimeError(f'Wrangler activation returned {result.returncode}; the staged Worker is not active')
    dump(stage / 'active-version.json', {'previousVersion': expected, 'activeVersion': active})

    def read_active(url, limit, validate=None):
        for attempt in range(6):
            try:
                data = get(url, limit)
                if validate:
                    validate(data)
                return data
            except urllib.error.HTTPError as exc:
                if exc.code not in (404, 429, 502, 503, 504):
                    raise
                if attempt == 5:
                    dump(stage / 'verification-error.json', {'url': url, 'status': exc.code,
                         'ray': exc.headers.get('CF-Ray'), 'cache': exc.headers.get('CF-Cache-Status')})
                    raise
            except ValueError:
                if validate is None or attempt == 5:
                    raise
            # The control plane can report 100% before every POP serves it.
            time.sleep(min(2 + attempt * 2, 8))

    old_packages = before_catalog['packages']
    changed_names = {name for name, _, _ in additions}
    retained_probes = sorted(old_packages.keys() & changed_names)
    sentinel = next((name for name in old_packages if name not in changed_names), None)
    if sentinel:
        retained_probes.append(sentinel)
    try:
        for name, version, record in additions:
            item = json.loads(read_active(f'{PUBLIC}/{name}/{version}', 512 * 1024))
            check_version(name, version, item, record)
            verify_bytes(read_active(item['dist']['tarball'], record['bytes']), record)
        read_active(f'{PUBLIC}/-/all', INDEX_LIMIT, lambda data: check_public_index(data, candidate))
        for name in retained_probes:
            item = json.loads(read_active(f'{PUBLIC}/{name}', 512 * 1024))
            for version, record in old_packages[name]['versions'].items():
                check_version(name, version, item['versions'][version], record)
        if unity_probe:
            from verify_release import verify

            prepared, signed, unity, editor, seconds = unity_probe
            verify(prepared, signed, stage / 'unity-proof', unity, editor, True, seconds)
    except Exception:
        if current() != active:
            raise RuntimeError('Another Worker version became active during verification') from None
        revert = cli(['rollback', expected, '--yes', '--message', 'ZE manifest verification failed'])
        if revert.returncode or current() != expected:
            raise RuntimeError('The ZE Worker rollback needs exact readback') from None
        dump(stage / 'rollback-receipt.json', {'restoredVersion': expected, 'rejectedVersion': active})
        raise
    report = {'worker': WORKER, 'previousVersion': expected, 'activeVersion': active,
              'verifiedNewVersions': len(additions), 'retainedPackagesVerified': len(old_packages),
              'retainedEndpointsProbed': len(retained_probes)}
    if unity_probe:
        report['unityTrustReceipt'] = str(stage / 'unity-proof/unity-trust-receipt.json')
    dump(stage / 'activation-receipt.json', report)
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--promotion', type=Path, required=True)
    parser.add_argument('--catalog', type=Path, required=True)
    parser.add_argument('--node', type=Path, required=True)
    parser.add_argument('--wrangler', type=Path, required=True)
    parser.add_argument('--expect-version', required=True)
    parser.add_argument('--resume-rolled-back', type=Path, help='Reuse the identical uploaded version after verified rollback')
    parser.add_argument('--verify-unity', type=Path, nargs=4, metavar=('PREPARED', 'SIGNED', 'UNITY_CLI', 'EDITOR'),
                        help='Require a cold Unity registry install before keeping the new deployment')
    parser.add_argument('--unity-seconds', type=int, default=300)
    args = parser.parse_args()
    probe = (*[path.resolve() for path in args.verify_unity], args.unity_seconds) if args.verify_unity else None
    result = activate(args.promotion.resolve(), args.catalog.resolve(), args.node.resolve(),
                      args.wrangler.resolve(), args.expect_version, args.resume_rolled_back, probe)
    print(json.dumps(result))


if __name__ == '__main__':
    main()
