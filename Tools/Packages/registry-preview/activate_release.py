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


def activate(promotion, catalog_path, node, wrangler, expected, resume=None):
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
    command = [str(node), str(wrangler)]
    flags = ['--config', str(stage / 'wrangler.toml'), '--name', WORKER]
    environment = dict(os.environ, CI='true', WRANGLER_SEND_METRICS='false')

    def cli(args):
        return subprocess.run(command + args + flags, cwd=stage, env=environment, capture_output=True, timeout=120)

    def current():
        result = cli(['deployments', 'status', '--json'])
        if result.returncode:
            raise RuntimeError('The existing ZE Worker deployment cannot be read')
        deployment = json.loads(result.stdout)
        versions = deployment.get('versions', [])
        if len(versions) != 1 or versions[0]['percentage'] != 100:
            raise ValueError('The existing Worker has a staged rollout; preserve it')
        return str(uuid.UUID(versions[0]['version_id']))

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

    def read_active(url, limit):
        for attempt in range(6):
            try:
                return get(url, limit)
            except urllib.error.HTTPError as exc:
                if exc.code not in (404, 429, 502, 503, 504):
                    raise
                if attempt == 5:
                    dump(stage / 'verification-error.json', {'url': url, 'status': exc.code,
                         'ray': exc.headers.get('CF-Ray'), 'cache': exc.headers.get('CF-Cache-Status')})
                    raise
                # The control plane can report 100% before every POP serves it.
                time.sleep(min(2 + attempt * 2, 8))

    try:
        for name, version, record in additions:
            item = json.loads(read_active(f'{PUBLIC}/{name}/{version}', 512 * 1024))
            check_version(name, version, item, record)
            verify_bytes(read_active(item['dist']['tarball'], record['bytes']), record)
        for name, product in before_catalog['packages'].items():
            version = list(product['versions'])[-1]
            item = json.loads(get(f'{PUBLIC}/{name}/{version}', 512 * 1024))
            check_version(name, version, item, product['versions'][version])
    except Exception:
        if current() != active:
            raise RuntimeError('Another Worker version became active during verification') from None
        revert = cli(['rollback', expected, '--yes', '--message', 'ZE manifest verification failed'])
        if revert.returncode or current() != expected:
            raise RuntimeError('The ZE Worker rollback needs exact readback') from None
        dump(stage / 'rollback-receipt.json', {'restoredVersion': expected, 'rejectedVersion': active})
        raise
    report = {'worker': WORKER, 'previousVersion': expected, 'activeVersion': active,
              'verifiedNewVersions': len(additions), 'retainedPackagesVerified': len(before_catalog['packages'])}
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
    args = parser.parse_args()
    result = activate(args.promotion.resolve(), args.catalog.resolve(), args.node.resolve(),
                      args.wrangler.resolve(), args.expect_version, args.resume_rolled_back)
    print(json.dumps(result))


if __name__ == '__main__':
    main()
