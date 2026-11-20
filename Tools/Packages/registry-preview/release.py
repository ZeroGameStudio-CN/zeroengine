"""Release a tested ZeroEngine package cohort with the existing ZE publisher."""
from __future__ import annotations

import argparse
from contextlib import contextmanager
import importlib.util
import json
import os
from pathlib import Path
import shlex
import shutil
import subprocess
import tempfile
import time
import uuid

from activate_release import INDEX_LIMIT, active_version, activate, check_public_index, sha
from prepare_pob_core import git
from publish_release import PUBLIC, dump, get, prepare_backup, verify_bytes
from sign_release import GIT_ID, PACKAGE, VERSION, catalog_json, plan_selected, prepare_selected
from verify_release import verify
from worker_release import TOOLS, require_trust, tool_digest, unpack_bundle, write_bundle


SOURCE = Path(__file__).resolve().parent
REPOSITORY = SOURCE.parents[2]
CATALOG = SOURCE / 'release-manifest.mjs'
ORGANIZATION = '16767998560366'
# The publisher runs the deployed immutable tool release. The controller's
# catalog and verification fixes can ship without updating the signer.
WORKER_TOOLS_COMMIT = '0e8153eef8dcab240dbb956a79fd24466b01f575'
SSH = ['ssh', '-o', 'BatchMode=yes', '-o', 'ConnectTimeout=10',
       '-o', 'ServerAliveInterval=5', '-o', 'ServerAliveCountMax=3', 'zgs-worker-linux-01']


def read(path):
    return json.loads(path.read_bytes())


def check_consumer(project):
    tools = REPOSITORY / 'com.zerogamestudio.zeroengine.ai-support/Tools~/sync_skill.py'
    spec = importlib.util.spec_from_file_location('ze_archive_check', tools)
    skill = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(skill)
    project = project.resolve()
    return skill.check_archive_delivery(project, skill.load(project / 'Packages/manifest.json'),
                                        skill.load(project / 'Packages/packages-lock.json'))


@contextmanager
def release_lock():
    # Use the existing Worker01 custody for a process-held publication lock.
    command = shlex.join(['sudo', '-n', 'flock', '-n', '/run/lock/zgs-ze-upm-release.lock',
                          'sh', '-c', 'printf "locked\\n"; cat >/dev/null'])
    with subprocess.Popen([*SSH, command], stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                          stderr=subprocess.DEVNULL) as session:
        try:
            if session.stdout.readline() != b'locked\n':
                raise RuntimeError('Worker01 is unreachable or another ZE release is active')
            yield session
        finally:
            session.stdin.close()
            try:
                session.wait(timeout=10)
            except subprocess.TimeoutExpired:
                session.kill()
                session.wait()


def worker(intent, mode, stdin, stdout):
    program = (SOURCE / 'worker_release.py').read_text(encoding='utf-8')
    tools = json.dumps(intent['workerTools'], separators=(',', ':'))
    command = shlex.join(['sudo', '-n', 'python3', '-B', '-c', program, mode, intent['run'],
                          intent['planSha256'], tools])
    result = subprocess.run([*SSH, command], stdin=stdin, stdout=stdout, stderr=subprocess.PIPE, timeout=600)
    if result.returncode:
        detail = result.stderr.decode(errors='replace').strip().splitlines()
        raise RuntimeError(f'Worker01 {mode} failed (exit {result.returncode}): ' + (detail[-1] if detail else 'no reply'))


def sign(intent, prepared, signed):
    with tempfile.TemporaryDirectory(dir=prepared.parent) as directory:
        work = Path(directory)
        with (work / 'input.tgz').open('w+b') as upload, (work / 'signed.tar').open('w+b') as download:
            write_bundle(prepared, upload, 'w:gz')
            upload.seek(0)
            worker(intent, 'sign', upload, download)
            download.seek(0)
            candidate = work / 'signed'
            unpack_bundle(download, candidate)
        receipt = signed_receipt(prepared, candidate)
        candidate.rename(signed)
    return receipt


def signed_receipt(prepared, signed):
    plan = read(prepared / 'release-input.json')
    versions = {name: item['version'] for name, item in plan['packages'].items()}
    metadata = read(signed / 'signed-release.json')
    if (metadata.get('schemaVersion') != 2 or metadata['organizationKey'] != plan['organizationKey']
            or metadata['sourceCommit'] != plan['commit'] or metadata['versions'] != versions
            or metadata['packages'].keys() != versions.keys()):
        raise ValueError('The received signed bytes differ from the Worker01 receipt')
    expected = {f'{name}/{name}-{version}.tgz' for name, version in versions.items()}
    on_disk = {path.relative_to(signed).as_posix() for path in signed.rglob('*') if path.suffix.lower() == '.tgz'}
    if on_disk - expected:
        raise ValueError('Unused signed archives: ' + ', '.join(sorted(on_disk - expected)))
    if expected - on_disk:
        raise ValueError('Missing signed archives: ' + ', '.join(sorted(expected - on_disk)))
    for name, record in metadata['packages'].items():
        asset = f'{name}-{versions[name]}.tgz'
        if record['asset'] != asset:
            raise ValueError('Unexpected signed archive identity')
        verify_bytes((signed / name / asset).read_bytes(), record)
    return metadata


def stage(intent, signed, proof_path, trust, export):
    with tempfile.TemporaryDirectory(dir=export.parent) as directory:
        candidate = Path(directory) / 'export'
        candidate.mkdir()
        for entry in trust['packages']:
            name, version = entry['name'], entry['version']
            asset = f'{name}-{version}.tgz'
            shutil.copyfile(signed / name / asset, candidate / asset)
        with (candidate / 'origin-receipt.json').open('wb') as origin, proof_path.open('rb') as proof:
            worker(intent, 'origin', proof, origin)
        candidate.rename(export)


def timed(output, name, action):
    start = time.monotonic()
    outcome = 'failed'
    try:
        value = action()
        outcome = 'ok'
        return value
    finally:
        row = {'phase': name, 'seconds': round(time.monotonic() - start, 3), 'outcome': outcome}
        with (output / 'timings.jsonl').open('a', encoding='utf-8') as stream:
            stream.write(json.dumps(row) + '\n')
        print(json.dumps(row), flush=True)


def finish_catalog(before, candidate):
    digest = sha(candidate)
    if sha(CATALOG) != digest:
        if sha(CATALOG) != sha(before):
            raise ValueError('The checked-out catalog changed; preserve the verified candidate')
        temporary = CATALOG.with_name('.release-manifest-' + uuid.uuid4().hex + '.mjs')
        try:
            shutil.copyfile(candidate, temporary)
            os.replace(temporary, CATALOG)
        finally:
            temporary.unlink(missing_ok=True)
    return digest


def release(args, selection, output):
    start = time.monotonic()
    source_identity = {'commit': args.commit, 'selection': selection}
    intent_file = output / 'release-intent.json'
    completed_file = output / 'release-receipt.json'
    before, prepared, signed = (output / name for name in ('catalog-before.mjs', 'prepared', 'signed'))
    local_proof = output / 'unity' / 'unity-trust-receipt.json'
    export, promotion, control = (output / name for name in ('origin', 'promotion', 'control'))
    candidate = promotion / 'release-manifest.mjs'
    if args.resume:
        intent = read(intent_file)
        if intent['source'] != source_identity or intent['baselineSha256'] != sha(before):
            raise ValueError('Resume the same reviewed output, commit and package selection')
        if completed_file.exists():
            report = read(completed_file)
            check_public_index(get(PUBLIC + '/-/all', INDEX_LIMIT), catalog_json(candidate))
            if report['catalogSha256'] != sha(candidate):
                raise ValueError('The completed catalog receipt differs')
            return report
    else:
        output.mkdir(parents=True, exist_ok=False)

    control_sources = {name: sha(SOURCE / name) for name in ('edge-router.mjs', 'wrangler.toml')}
    with release_lock() as lock:
        if not args.resume:
            def prepare():
                if git(REPOSITORY, 'remote', 'get-url', 'origin').decode().strip().removesuffix('.git') != \
                        'https://github.com/ZeroGameStudio-CN/zeroengine':
                    raise ValueError('Use the canonical public ZeroEngine checkout')
                git(REPOSITORY, 'fetch', 'origin', 'main')
                relative = SOURCE.relative_to(REPOSITORY).as_posix()
                names = ('release-manifest.mjs', 'edge-router.mjs', 'wrangler.toml')
                git(REPOSITORY, 'diff', '--quiet', 'origin/main', '--', *(f'{relative}/{name}' for name in names))
                worker_tools = {name: tool_digest(git(REPOSITORY, 'show', f'{WORKER_TOOLS_COMMIT}:{relative}/{name}'))
                                for name in TOOLS}
                shutil.copyfile(CATALOG, before)
                control.mkdir()
                shutil.copyfile(SOURCE / 'wrangler.toml', control / 'wrangler.toml')
                catalog = catalog_json(before)
                prepare_selected(REPOSITORY, args.commit, selection, ORGANIZATION, prepared, catalog['packages'])
                version = active_version(args.node, args.wrangler, control)
                check_public_index(get(PUBLIC + '/-/all', INDEX_LIMIT), catalog)
                plan_sha = sha(prepared / 'release-input.json')
                details = {'source': source_identity, 'run': uuid.uuid4().hex, 'baselineSha256': sha(before),
                           'controlSources': control_sources, 'workerTools': worker_tools,
                           'beforeVersion': version, 'planSha256': plan_sha,
                           'tag': f'ze-upm-{args.commit[:8]}-{plan_sha[:12]}'}
                dump(intent_file, details)
                return details

            intent = timed(output, 'prepare', prepare)

        if (sha(prepared / 'release-input.json') != intent['planSha256']
                or intent['controlSources'] != control_sources
                or sha(control / 'wrangler.toml') != control_sources['wrangler.toml']):
            raise ValueError('Prepared source or deployment configuration changed')
        if not signed.exists():
            signature = timed(output, 'sign', lambda: sign(intent, prepared, signed))
        else:
            signature = signed_receipt(prepared, signed)
        if not local_proof.exists():
            if local_proof.parent.exists():
                raise RuntimeError('Resolve the original isolated Unity attempt before retrying')
            timed(output, 'unity', lambda: verify(prepared, signed, local_proof.parent,
                                                 args.unity, args.editor, False, args.unity_seconds))
        trust = read(local_proof)
        require_trust(signature, trust)
        if not export.exists():
            timed(output, 'worker01', lambda: stage(intent, signed, local_proof, trust, export))

        promoted = promotion / 'promotion-receipt.json'
        if not promoted.exists():
            if promotion.exists():
                # Metadata assets and the GitHub tag are immutable. Reuse the
                # original origin and prepare an identical backup in a fresh folder.
                promotion.rename(output / ('partial-promotion-' + uuid.uuid4().hex))
            check_public_index(get(PUBLIC + '/-/all', INDEX_LIMIT), catalog_json(before))
            timed(output, 'github', lambda: prepare_backup(before, [export], intent['tag'], promotion, local_proof))
        original = promotion / 'edge-versioned'
        resumed = promotion / 'edge-resumed'
        activation = next((path for path in (resumed, original) if (path / 'activation-receipt.json').exists()), None)
        if activation:
            deployed = read(activation / 'activation-receipt.json')
            if active_version(args.node, args.wrangler, control) != deployed['activeVersion']:
                raise ValueError('The previously activated package release changed')
            check_public_index(get(PUBLIC + '/-/all', INDEX_LIMIT), catalog_json(candidate))
        else:
            if sha(CATALOG) != intent['baselineSha256']:
                raise ValueError('The checked-out registry catalog changed')
            retry = None
            if original.exists():
                if resumed.exists() or not (original / 'rollback-receipt.json').is_file():
                    raise RuntimeError('Inspect the original Cloudflare activation readback before retrying')
                retry = original
            if lock.poll() is not None:
                raise RuntimeError('The Worker01 release lock was lost')
            deployed = timed(output, 'activate', lambda: activate(promotion, before, args.node, args.wrangler,
                                                                  intent['beforeVersion'], resume=retry))
        digest = finish_catalog(before, candidate)
        report = {'commit': args.commit, 'packages': signature['versions'], 'githubRelease': read(promoted)['githubRelease'],
                  'workerVersion': deployed['activeVersion'], 'catalogSha256': digest, 'unityProof': str(local_proof)}
        try:
            worker(intent, 'clean', subprocess.DEVNULL, subprocess.DEVNULL)
        except (RuntimeError, subprocess.TimeoutExpired):
            report['workerStagingRetained'] = intent['run']
        report['seconds'] = round(time.monotonic() - start, 3)
        dump(completed_file, report)
        return report


def executable(parser, value, label):
    path = Path(value).expanduser()
    installed = path if path.is_file() else shutil.which(value)
    if not installed:
        parser.error(f'{label} is not installed or its path differs')
    return Path(installed).resolve()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--commit', help='Tested full commit reachable on public main')
    parser.add_argument('--package', action='append', default=[], metavar='NAME[=VERSION]')
    parser.add_argument('--output', type=Path, help='External release evidence directory')
    parser.add_argument('--project', type=Path, help='Check a consumer project using signed ZE archives')
    parser.add_argument('--editor', default=os.environ.get('ZE_RELEASE_EDITOR'))
    parser.add_argument('--wrangler', default=os.environ.get('ZE_RELEASE_WRANGLER'))
    parser.add_argument('--unity', default='unity')
    parser.add_argument('--node', default='node')
    parser.add_argument('--unity-seconds', type=int, default=300)
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument('--check', action='store_true', help='Check local inputs without signing or publishing')
    mode.add_argument('--resume', action='store_true', help='Resume the original known release inputs and receipts')
    args = parser.parse_args()
    selection = {}
    for value in args.package:
        name, separator, version = value.partition('=')
        if not PACKAGE.fullmatch(name) or name in selection or (separator and not VERSION.fullmatch(version)):
            parser.error('Choose each public package once, with a complete optional version')
        selection[name] = version or None
    if args.commit or selection or not args.check:
        if not GIT_ID.fullmatch(args.commit or '') or not selection:
            parser.error('Provide both a full Git commit and at least one package')
    elif args.project is None:
        parser.error('Provide a package selection or a consumer project to check')
    if args.check and args.output:
        parser.error('--check reports to stdout')

    try:
        report = {'archives': check_consumer(args.project)} if args.project else {}
        if args.check:
            if selection:
                resolved, pending = plan_selected(REPOSITORY, args.commit, selection, ORGANIZATION, catalog_json(CATALOG)['packages'])
                report.update(commit=args.commit, packages=resolved, newVersions=len(pending))
            print(json.dumps(report, ensure_ascii=False), flush=True)
            return
    except (OSError, ValueError, KeyError, TypeError, subprocess.CalledProcessError) as error:
        parser.error(str(error))

    if not args.output or not args.editor or not args.wrangler:
        parser.error('Provide an evidence directory, Unity Editor and Wrangler JavaScript entry point')
    for name in ('unity', 'editor', 'node', 'wrangler'):
        setattr(args, name, executable(parser, getattr(args, name), name))
    output = args.output.resolve()
    if output.is_relative_to(REPOSITORY) or any((path / 'Packages/manifest.json').is_file()
                                                for path in (output, *output.parents)):
        parser.error('Use scratch outside the repository and consumer Unity projects')
    print(json.dumps(release(args, selection, output), ensure_ascii=False), flush=True)


if __name__ == '__main__':
    main()
