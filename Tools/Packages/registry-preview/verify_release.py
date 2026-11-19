"""Have a disposable Unity Editor verify the exact signed release cohort."""
from __future__ import annotations

import argparse
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys

from publish_release import PUBLIC, dump, get, verify_bytes
from sign_release import run

AGENT = 'com.zerogamestudio.zeroengine.ai-support'


def verify(prepared, signed, output, unity, editor, public, seconds):
    plan_path = prepared / 'release-input.json'
    plan = json.loads(plan_path.read_text(encoding='utf-8-sig'))
    signed_release = run(plan_path, signed, verify_only=True)
    if signed_release.get('schemaVersion') != 2:
        raise ValueError('Use a generic signed release cohort')
    output.mkdir(parents=True, exist_ok=False)
    project = output / 'Project'
    for folder in ('Assets/Editor', 'Packages', 'ProjectSettings'):
        (project / folder).mkdir(parents=True)
    (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 6000.6.9f1\n', encoding='utf-8')
    source = Path(__file__).with_name('VerifyReleaseSignatures.cs')
    shutil.copyfile(source, project / 'Assets/Editor' / source.name)
    dependencies = dict(plan['resolved'])
    expected = []
    for name, record in signed_release['packages'].items():
        version = signed_release['versions'][name]
        source = 'Registry' if public else 'LocalTarball'
        if public:
            verify_bytes(get(f'{PUBLIC}/{name}/-/{record["asset"]}',record['bytes']),record)
        else:
            archive = signed / name / record['asset']
            dependencies[name] = 'file:' + archive.as_posix()
        expected.append((name, version, source, plan['organizationKey'], plan['commit'], record['sha256']))
    dump(project / 'Packages/manifest.json', {'dependencies': dependencies,
         'scopedRegistries': [{'name': 'ZE Registry', 'url': PUBLIC, 'scopes': list(dependencies)}]})
    inputs, proof = output / 'expected.tsv', output / 'unity-observation.json'
    inputs.write_text('\n'.join('\t'.join(row) for row in expected) + '\n', encoding='utf-8')
    environment = dict(os.environ, UPM_CACHE_ROOT=str(output / 'cache'),
                       ZE_RELEASE_VERIFICATION_PLAN=str(inputs), ZE_RELEASE_VERIFICATION_PROOF=str(proof))
    skill_snapshot = output / 'skill-snapshot.json'
    if AGENT in signed_release['packages']:
        environment['ZE_RELEASE_SKILL_SNAPSHOT'] = str(skill_snapshot)
    else:
        environment.pop('ZE_RELEASE_SKILL_SNAPSHOT', None)
    for key in ('HTTP_PROXY', 'HTTPS_PROXY', 'ALL_PROXY', 'http_proxy', 'https_proxy', 'all_proxy'):
        environment.pop(key, None)
    environment['NO_PROXY'] = '127.0.0.1,localhost,packages-preview.zerogamestudio.com'
    log = output / 'unity.log'
    command = [str(unity), 'run', project.as_posix(), '--editor-path', str(editor), '--timeout', str(seconds),
               '--log-file', log.as_posix(), '--no-tail', '--non-interactive', '--proxy-disable', '--format', 'json',
               '--', '-nographics', '-executeMethod', 'VerifyReleaseSignatures.Verify']
    try:
        result = subprocess.run(command, env=environment, capture_output=True, timeout=seconds + 15,
                                creationflags=getattr(subprocess, 'CREATE_NO_WINDOW', 0))
    finally:
        if log.is_file():
            data = log.read_text(encoding='utf-8', errors='replace')
            data = re.sub(r'(?im)^\s*-accessToken\s*\n[^\r\n]+', '[authentication argument redacted]', data)
            data = re.sub(r'(?im)^.*(?:access.?token|Bearer\s+|password|credential).*$', '[authentication diagnostic redacted]', data)
            log.write_text(data, encoding='utf-8')
    if result.returncode or not proof.is_file():
        raise RuntimeError(f'Unity did not verify the release cohort (exit {result.returncode})')
    receipt = json.loads(proof.read_text(encoding='utf-8'))
    observed = {(item['name'], item['version'], item['source'], item['sha256']) for item in receipt['packages']
                if item['signatureStatus'] == 'Valid'}
    if observed != {(name, version, source, digest) for name, version, source, _, _, digest in expected}:
        raise ValueError('Unity signature receipt differs from the exact reviewed archives')
    if AGENT in signed_release['packages']:
        snapshot = json.loads(skill_snapshot.read_text(encoding='utf-8'))
        agent, = [row for row in snapshot['packages'] if row['name'] == AGENT]
        tool = Path(agent['resolvedPath']) / 'Tools~/sync_skill.py'
        arguments = [sys.executable, '-B', str(tool), '--project', str(project), '--snapshot', str(skill_snapshot)]
        if not public:
            arguments.append('--include-local-trials')
        for operation in ('--apply', '--check'):
            result = subprocess.run([*arguments, operation], capture_output=True, timeout=20)
            if result.returncode:
                raise RuntimeError(f'Installed ZE Agent Skill {operation} failed (exit {result.returncode})')
        dump(output / 'skill-receipt.json', {'version': agent['version'], 'source': agent['source'], 'drift': False})
    accepted = output / 'unity-trust-receipt.json'
    dump(accepted, receipt)
    return {'signatureCount': len(observed), 'receipt': str(accepted), 'registry': public}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--prepared', type=Path, required=True)
    parser.add_argument('--signed', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--unity', type=Path, required=True)
    parser.add_argument('--editor', type=Path, required=True)
    parser.add_argument('--seconds', type=int, default=300)
    parser.add_argument('--public', action='store_true', help='Verify the public HTTPS registry instead of local tarballs')
    args = parser.parse_args()
    result = verify(args.prepared.resolve(), args.signed.resolve(), args.output.resolve(), args.unity.resolve(),
                    args.editor.resolve(), args.public, args.seconds)
    print(json.dumps(result))


if __name__ == '__main__':
    main()
