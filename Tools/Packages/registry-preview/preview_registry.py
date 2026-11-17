"""Narrow server-local operator for the authorized ZE single-package Preview."""
import argparse
import configparser
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import urllib.request

HOST = 'packages-preview.zerogamestudio.com'
UNIT = 'zgs-ze-upm-preview.service'
VHOST = Path('/www/server/panel/vhost/nginx') / (HOST + '.conf')
NGINX = '/www/server/nginx/sbin/nginx'
CONFIG = Path('/etc/zgs-ze-upm-preview')
SOURCE = Path(__file__).resolve().parent


def call(args, **kwargs):
    return subprocess.run(args, check=True, **kwargs)


def save(path, value):
    with path.open('x', encoding='utf-8') as stream:
        json.dump(value, stream, indent=2)


def api(path, *, data=None, method='GET'):
    values = configparser.ConfigParser()
    values.read_string('[cloudflare]\n' + Path('/etc/letsencrypt/cloudflare-zgs-tail.ini').read_text())
    token = values['cloudflare']['dns_cloudflare_api_token']
    req = urllib.request.Request('https://api.cloudflare.com/client/v4/' + path,
        headers={'Authorization': 'Bearer ' + token, 'Content-Type': 'application/json'},
        data=None if data is None else json.dumps(data).encode(), method=method)
    with urllib.request.urlopen(req, timeout=25) as response:
        result = json.load(response)
    if not result.get('success'):
        raise RuntimeError('Cloudflare refused the scoped operation; inspect its error codes server-locally.')
    return result['result']


def dns(stage):
    if (stage / 'dns-intent.json').exists():
        raise RuntimeError('Inspect the original DNS operation; never replay an uncertain write.')
    zones = api('zones?name=zerogamestudio.com')
    if len(zones) != 1:
        raise RuntimeError('Exact existing DNS zone required.')
    zone = zones[0]['id']
    existing = api('zones/' + zone + '/dns_records?name=' + HOST)
    origin = api('zones/' + zone + '/dns_records?name=feedback-download.zerogamestudio.com')
    if existing or len(origin) != 1 or origin[0]['type'] != 'A' or origin[0]['proxied']:
        raise RuntimeError('Preserve existing names; verify the independent Tencent IPv4 origin.')
    save(stage / 'dns-before.json', {'hostname': HOST, 'records': existing, 'originRecord': origin[0]})
    save(stage / 'dns-intent.json', {'zoneId': zone, 'hostname': HOST, 'type': 'A', 'content': origin[0]['content']})
    created = api('zones/' + zone + '/dns_records', method='POST', data={
        'type': 'A', 'name': HOST, 'content': origin[0]['content'], 'ttl': 300,
        'proxied': False, 'comment': 'Authorized ZE single-package HTTPS Preview'})
    save(stage / 'dns-created.json', created)
    print(json.dumps({'hostname': HOST, 'dnsOnly': True, 'created': True}))


def install(stage, release):
    destination = Path('/opt/zgs-ze-upm-preview')
    unit = Path('/etc/systemd/system') / UNIT
    if destination.exists() or unit.exists() or CONFIG.exists():
        raise RuntimeError('An existing deployment requires its own reviewed update; preserve it.')
    available = int(next(line.split()[1] for line in Path('/proc/meminfo').read_text().splitlines() if line.startswith('MemAvailable:')))
    if available < 768 * 1024:
        raise RuntimeError('Insufficient memory headroom for this Preview.')
    usage = shutil.disk_usage('/')
    if usage.free < usage.total * .20:
        raise RuntimeError('Keep the Shanghai 20% free-space target.')
    save(stage / 'install-intent.json', {'release': release, 'root': str(destination), 'unit': UNIT})
    target = destination / 'releases' / release
    target.mkdir(parents=True)
    shutil.copytree(stage / 'node-v24.11.1-linux-x64', target / 'node', symlinks=True)
    app = target / 'app'; app.mkdir()
    for name in ['package.json', 'package-lock.json']:
        shutil.copyfile(SOURCE / name, app / name)
    env = os.environ.copy(); env['PATH'] = str(target / 'node/bin') + ':' + env['PATH']
    call([str(target / 'node/bin/node'), str(target / 'node/lib/node_modules/npm/bin/npm-cli.js'),
          'ci', '--omit=dev', '--ignore-scripts', '--no-audit', '--no-fund',
          '--registry=https://registry.npmjs.org/', '--cache=' + str(stage / 'npm-cache')], cwd=app, env=env)
    call([str(target / 'node/bin/node'), '--version'])
    CONFIG.mkdir(mode=0o755)
    shutil.copyfile(SOURCE / 'verdaccio.yaml', CONFIG / 'config.yaml')
    shutil.copyfile(SOURCE / UNIT, unit)
    (destination / 'current').symlink_to(target, target_is_directory=True)
    call(['systemd-analyze', 'verify', str(unit)])
    call(['systemctl', 'daemon-reload'])
    call(['systemctl', 'enable', '--now', UNIT])
    save(stage / 'install-completed.json', {'release': release, 'root': str(target), 'unit': UNIT})


def edge(stage):
    if VHOST.exists():
        raise RuntimeError('Preserve an existing vhost and inspect original activation.')
    cert = Path('/etc/letsencrypt/live') / HOST / 'fullchain.pem'
    if not cert.is_file():
        raise RuntimeError('Issue and verify the dedicated publicly trusted certificate first.')
    data = (SOURCE / 'nginx.conf').read_bytes()
    save(stage / 'edge-intent.json', {'vhost': str(VHOST), 'previouslyAbsent': True, 'sha256': hashlib.sha256(data).hexdigest()})
    with VHOST.open('xb') as output:
        output.write(data)
    tested = subprocess.run([NGINX, '-t'], capture_output=True, text=True)
    if tested.returncode:
        if VHOST.read_bytes() == data:
            VHOST.unlink()
        raise RuntimeError('Nginx validation failed; the new candidate was removed without reload.')
    call([NGINX, '-s', 'reload'])
    save(stage / 'edge-completed.json', {'vhost': str(VHOST), 'sha256': hashlib.sha256(data).hexdigest()})


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action', choices=['dns', 'install', 'edge'])
    parser.add_argument('--stage', type=Path, required=True)
    parser.add_argument('--release')
    args = parser.parse_args()
    stage = args.stage.resolve()
    if not str(stage).startswith('/var/lib/zgs-service-maintenance/ze-upm-https-') or not stage.is_dir():
        raise RuntimeError('Use the exact existing owned maintenance directory.')
    if args.action == 'dns': dns(stage)
    elif args.action == 'edge': edge(stage)
    else:
        if not re.fullmatch('[a-f0-9]{40}', args.release or ''):
            raise RuntimeError('A full committed source identity is required.')
        install(stage, args.release)


if __name__ == '__main__': main()
