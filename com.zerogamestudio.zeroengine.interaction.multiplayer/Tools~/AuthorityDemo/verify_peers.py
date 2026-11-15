"""Exercise the shipped sample in independent offline/host/client processes; no Unity project or player data."""
import argparse
from contextlib import contextmanager
import hashlib
import json
from pathlib import Path
import queue
import socket
import subprocess
import threading
import time


class Peer:
    def __init__(self, dll, role, output, port=0):
        self.role, self.counter, self.closed = role, 0, None
        self.lines = queue.Queue()
        self.trace = (output / (role + '.jsonl')).open('w', encoding='utf-8')
        self.errors = (output / (role + '.stderr')).open('w', encoding='utf-8')
        self.process = subprocess.Popen(['dotnet', str(dll), role, str(port)], stdin=subprocess.PIPE,
                                        stdout=subprocess.PIPE, stderr=self.errors, text=True, encoding='utf-8', bufsize=1)
        self.reader = threading.Thread(target=self.read, daemon=True)
        self.reader.start()
        self.ready = self.receive('ready')
        assert self.ready['Pid'] == self.process.pid

    def read(self):
        try:
            for line in self.process.stdout:
                self.trace.write(line)
                self.trace.flush()
                try:
                    value = json.loads(line.lstrip('\ufeff'))
                    self.lines.put(value)
                except ValueError:
                    pass
        finally:
            self.lines.put({'Kind': 'exited'})

    def receive(self, kind, request=None):
        deadline = time.monotonic() + 12
        while time.monotonic() < deadline:
            value = self.lines.get(timeout=max(.01, deadline - time.monotonic()))
            if value.get('Kind') == 'closed':
                self.closed = value
            if value.get('Kind') == kind and (request is None or value.get('Id') == request):
                return value
            if value.get('Kind') == 'exited':
                raise RuntimeError(self.role + ' exited before ' + kind)
        raise TimeoutError(self.role + ': ' + kind)

    def send(self, op, **values):
        self.counter += 1
        payload = dict(Id=self.counter, Op=op, **values)
        self.process.stdin.write(json.dumps(payload) + '\n')
        self.process.stdin.flush()
        result = self.receive('report', self.counter)
        if result.get('Error'):
            raise AssertionError(result['Error'])
        return result

    def wait(self, predicate, label):
        deadline = time.monotonic() + 8
        last = None
        while time.monotonic() < deadline:
            last = self.send('state')
            if predicate(last):
                return last
            time.sleep(.025)
        raise AssertionError(label + ': ' + json.dumps(last, ensure_ascii=True))

    def stop(self):
        try:
            if self.process.poll() is None:
                self.send('quit')
                self.closed = self.receive('closed')
                self.process.stdin.close()
            self.process.wait(timeout=5)
            self.reader.join(timeout=2)
            assert self.process.returncode == 0, (self.role, self.process.returncode)
            assert self.closed and self.closed['Clean'], self.role + ' has live transport workers'
        finally:
            if self.process.poll() is None:
                self.process.terminate()
                self.process.wait(timeout=5)
            self.reader.join(timeout=2)
            self.trace.close()
            self.errors.close()


def self_actor(value):
    return next(a for a in value['View']['Actors'] if a['Entity'] == value['Actor'] and a['Generation'] == value['ActorGeneration'])


def guest_actor(value):
    return next(a for a in value['View']['Actors'] if a['Name'].startswith('guest-'))


def approach(peer, name):
    peer.send('approach', Target=name)
    positions = {'door': 0, 'npc': 5, 'shared': 10, 'shop': 15, 'personal': 20}
    return peer.wait(lambda r: self_actor(r)['Position'] == positions[name], 'approach ' + name)


def tap(peer, target):
    peer.send('press', Target=target)
    peer.send('release')


@contextmanager
def pair(dll, output, offline=False):
    output.mkdir()
    host = Peer(dll, 'offline' if offline else 'host', output)
    guest = None
    try:
        if not offline:
            guest = Peer(dll, 'client', output, host.ready['Port'])
            assert host.process.pid != guest.process.pid
            guest.wait(lambda r: r['Connected'] and r.get('View') is not None, 'authenticated sample binding')
            host.wait(lambda r: len(r['View']['Actors']) == 2, 'host observes the other process')
        yield host, guest
    finally:
        try:
            if guest:
                guest.stop()
        finally:
            host.stop()
        if not offline:
            with socket.socket() as probe:
                probe.settimeout(.25)
                assert probe.connect_ex(('127.0.0.1', host.ready['Port'])) != 0, 'sample listener remained open'


def verify(dll, output):
    results = []
    def done(name, host, guest):
        row = dict(name=name, passed=True, hostPid=host.process.pid, guestPid=guest.process.pid if guest else None)
        results.append(row)
        print(json.dumps(row), flush=True)

    with pair(dll, output / 'offline', True) as (host, _):
        approach(host, 'npc'); host.send('press', Target='npc')
        host.wait(lambda r: self_actor(r)['Gifts'] == 1, 'offline hold')
        host.send('release'); time.sleep(.5)
        value = host.send('state'); assert self_actor(value)['Gifts'] == 1 and self_actor(value)['Conversations'] == 0
        tap(host, 'npc'); assert self_actor(host.send('state'))['Conversations'] == 1
        approach(host, 'shop'); tap(host, 'shop')
        value = host.send('state'); assert self_actor(value)['Coins'] == 2 and self_actor(value)['Reserved'] == 2
        host.send('confirm'); host.send('cancel')
        value = host.send('state'); assert self_actor(value)['Purchases'] == 1 and value['View']['Refunds'] == 0
        done('offline_original_tap_hold_and_deferred_purchase', host, None)

    with pair(dll, output / 'independent') as (host, guest):
        approach(host, 'door'); tap(host, 'door'); guest.wait(lambda r: r['View']['DoorOpen'], 'guest observes host door')
        approach(guest, 'door'); guest.send('press', Target='door'); guest.send('replay'); guest.send('release')
        value = host.wait(lambda r: not r['View']['DoorOpen'], 'host observes client door')
        assert value['View']['Commits'] == 2
        guest.send('replay'); time.sleep(.15)
        assert host.send('state')['View']['Commits'] == 2
        done('independent_peers_and_duplicate_commands', host, guest)

    for winner in ('host', 'client'):
        with pair(dll, output / ('contest-' + winner)) as (host, guest):
            approach(host, 'shared'); approach(guest, 'shared')
            first, second = (host, guest) if winner == 'host' else (guest, host)
            first.send('press', Target='shared')
            first.wait(lambda r: r['View']['SharedPickups'] == 1, 'first pickup decision')
            second.send('press', Target='shared'); second.send('replay')
            time.sleep(.1)
            value = host.send('state'); assert value['View']['SharedPickups'] == 1
            assert value['View']['SharedWinner'] == ('host' if winner == 'host' else 'guest-1')
            host.send('release'); guest.send('release')
            approach(guest, 'personal'); tap(guest, 'personal')
            guest.wait(lambda r: r['OutcomeStatus'] == 'Denied', 'foreign personal pickup rejected')
            assert host.send('state')['View']['PersonalPickups'] == 0
            approach(host, 'personal'); tap(host, 'personal')
            assert host.send('state')['View']['PersonalPickups'] == 1
            done('pickup_contention_' + winner + '_and_personal_ownership', host, guest)

    with pair(dll, output / 'remote-hold') as (host, guest):
        approach(guest, 'npc'); host.send('pause', Flag=True)
        guest.send('press', Target='npc')
        guest.wait(lambda r: r.get('Receipt') is not None and r['Receipt']['Sequence'] == 1
                   and r['Receipt']['Held'], 'server admitted held input')
        time.sleep(.85)
        assert guest_actor(host.send('state'))['Gifts'] == 0, 'paused authority clock advanced a hold'
        host.send('pause', Flag=False)
        guest.wait(lambda r: self_actor(r)['Gifts'] == 1, 'server-timed remote hold')
        guest.send('release')
        guest.wait(lambda r: r['Receipt']['Sequence'] == 2 and not r['Receipt']['Held'], 'server admitted release')
        time.sleep(.5)
        value = host.wait(lambda r: guest_actor(r)['Gifts'] == 1, 'host observes the remote gift')
        assert guest_actor(value)['Coins'] == 2 and self_actor(value)['Gifts'] == 0
        assert guest_actor(value)['Conversations'] == 0, 'release after hold also triggered tap'
        tap(guest, 'npc')
        value = host.wait(lambda r: guest_actor(r)['Conversations'] == 1, 'remote short tap still talks')
        assert guest_actor(value)['Gifts'] == 1
        done('remote_hold_uses_authority_clock_and_release_does_not_tap', host, guest)

    with pair(dll, output / 'cancel-first') as (host, guest):
        approach(guest, 'shop'); tap(guest, 'shop')
        guest.wait(lambda r: self_actor(r)['Reserved'] == 2, 'reserve')
        guest.send('cancel'); guest.send('replay'); guest.send('confirm')
        # The host's local projection may still predate the reservation. Zero reserved
        # alone also matches that older snapshot, so observe the settlement counters.
        value = host.wait(lambda r: guest_actor(r)['Reserved'] == 0
                          and r['View']['Reservations'] == 1 and r['View']['Refunds'] == 1, 'cancel refund')
        assert guest_actor(value)['Coins'] == 3 and guest_actor(value)['Purchases'] == 0
        assert value['View']['Reservations'] == 1 and value['View']['Refunds'] == 1
        done('cancel_before_confirmation_refunds_once', host, guest)

    with pair(dll, output / 'success-first') as (host, guest):
        approach(guest, 'shop'); tap(guest, 'shop')
        guest.wait(lambda r: self_actor(r)['Reserved'] == 2, 'reserve')
        guest.send('holdReplies', Flag=True); guest.send('confirm'); guest.send('cancel')
        value = host.wait(lambda r: guest_actor(r)['Purchases'] == 1, 'authority commits')
        assert guest_actor(value)['Coins'] == 1 and value['View']['Refunds'] == 0
        time.sleep(.2); guest.send('flush', Flag=True)
        value = guest.wait(lambda r: r['OutcomeStatus'] == 'Succeeded' and self_actor(r)['Reserved'] == 0, 'late success survives local cancellation')
        assert self_actor(value)['Coins'] == 1 and self_actor(value)['Purchases'] == 1
        guest.send('replay'); time.sleep(.1)
        assert host.send('state')['View']['Commits'] == 1
        done('late_success_reordered_receipts_and_no_double_refund', host, guest)

    with pair(dll, output / 'range-pool') as (host, guest):
        guest.send('press', Target='shop'); guest.send('release'); time.sleep(.1)
        assert host.send('state')['View']['Reservations'] == 0
        approach(guest, 'shop'); tap(guest, 'shop'); guest.send('capture')
        guest.wait(lambda r: self_actor(r)['Reserved'] == 2, 'reserve')
        guest.send('leave')
        host.wait(lambda r: guest_actor(r)['Reserved'] == 0 and r['View']['Refunds'] == 1, 'range cancellation')
        host.send('repool'); guest.send('replayCaptured'); guest.send('confirm')
        guest.wait(lambda r: next(t for t in r['View']['Targets'] if t['Name'] == 'shop')['Generation'] == 2, 'new pooled activation')
        assert host.send('state')['View']['Commits'] == 0
        approach(guest, 'shop'); tap(guest, 'shop'); guest.send('confirm')
        value = host.wait(lambda r: guest_actor(r)['Purchases'] == 1, 'fresh activation confirmation')
        guest.send('replayCaptured'); time.sleep(.1)
        assert host.send('state')['View']['Commits'] == 1 and value['View']['Refunds'] == 1
        done('authoritative_range_and_old_pooled_messages', host, guest)

    with pair(dll, output / 'binding') as (host, guest):
        approach(guest, 'door'); guest.send('press', Target='door')
        guest.wait(lambda r: r['View']['Commits'] == 1, 'first command')
        guest.send('spoof'); guest.wait(lambda r: r['Rejection'] == 'UnauthorizedActor', 'payload cannot select host actor')
        guest.send('wrongEpoch'); guest.wait(lambda r: r['Rejection'] == 'WrongEpoch', 'stale epoch rejected')
        guest.send('release'); tap(guest, 'door')
        value = host.wait(lambda r: r['View']['Commits'] == 2, 'valid sequence survives rejected headers')
        assert not value['View']['DoorOpen']
        done('authenticated_actor_binding_and_epoch', host, guest)

    with pair(dll, output / 'disconnect') as (host, guest):
        approach(guest, 'shop'); tap(guest, 'shop')
        guest.wait(lambda r: self_actor(r)['Reserved'] == 2, 'reserve')
        guest.send('disconnect')
        value = host.wait(lambda r: not guest_actor(r)['Connected'] and guest_actor(r)['Reserved'] == 0, 'disconnect cleanup')
        assert guest_actor(value)['Coins'] == 3 and guest_actor(value)['Purchases'] == 0
        assert value['View']['Refunds'] == 1
        done('disconnect_releases_pending_resources', host, guest)
    return results


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--dll', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=False)
    result = dict(schemaVersion=1, dllSha256=hashlib.sha256(args.dll.read_bytes()).hexdigest())
    try:
        result['cases'] = verify(args.dll.resolve(), args.output)
        result['passed'] = True
    except Exception as error:
        result.update(passed=False, error=repr(error))
        raise
    finally:
        (args.output / 'summary.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
    print(json.dumps({'passed': True, 'cases': len(result['cases'])}), flush=True)


if __name__ == '__main__':
    main()
