"""Offline portability contract; no real account, Steam call or production data."""
import copy
from pathlib import Path
import sqlite3
import tempfile
import unittest

from ze_leaderboards.common import AuthenticationRejected, Rejected, WriteNotSent
from ze_leaderboards.core import RunService
from ze_leaderboards.steam import SteamPublisher


class StageGame(RunService):
    """Independent synthetic game: its score rule is deliberately not POB's."""
    @staticmethod
    def validate_policies(config):
        result = {}
        for policy in config['profiles']:
            if policy['mode'] != 'stages' or policy['pointsPerStage'] <= 0:
                raise ValueError('Explicit game scoring policy required')
            result[policy['board']] = copy.deepcopy(policy)
        return result

    def _apply_progress(self, db, row, body, policy):
        index, room, kind, elapsed = (body[k] for k in ('roomIndex', 'room', 'kind', 'battleMs'))
        if room not in policy['rooms'] or elapsed < row['battle_ms'] or elapsed > (self.clock()-row['started'])*1000+2000:
            raise Rejected('progress_invalid', 403)
        cleared, state, score = row['cleared'], 'active', None
        if kind == 'enter':
            if index != row['room_index']+1 or (row['room_index'] and not cleared):
                raise Rejected('room_sequence_invalid', 403)
            cleared = 0
        else:
            if index != row['room_index'] or room != row['room']:
                raise Rejected('room_mismatch', 403)
            if kind == 'clear':
                if cleared:
                    raise Rejected('clear_invalid', 403)
                cleared = 1
            elif kind == 'finish':
                if not cleared:
                    raise Rejected('completion_invalid', 403)
                state, score = 'ready', index*policy['pointsPerStage']
            else:
                state = 'revoked'
        return cleared, state, score, policy, {'findings': []}


class Sink:
    def __init__(self):
        self.writes = []
        self.failure = None

    def set_score(self, policy, subject, score):
        if self.failure:
            raise self.failure
        self.writes.append((policy['targetBoard'], subject, score))


class PortabilityTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='ze-ranked-contract-')
        self.addCleanup(self.temp.cleanup)
        self.clock = 1700000000
        self.sink = Sink()

    def make(self, game='another-game', key=b'isolated-other-game-signing-key-32-bytes', auth=None):
        config = {'enabled': True, 'profiles': [{
            'board': 'SeasonOne', 'targetBoard': game+'.ranked', 'boardId': 11,
            'mode': 'stages', 'pointsPerStage': 75, 'maxAgeSeconds': 3600, 'publish': True,
            'builds': ['one'], 'rules': ['stage-v1'], 'content': ['map-v1'], 'rooms': {'arena': True},
        }]}
        def verify(ticket):
            if ticket != 'verified-mobile-fixture':
                raise AuthenticationRejected()
            return 'mobile:fixture-player'
        return StageGame(str(Path(self.temp.name)/(game+'.sqlite3')), key, config,
                         self.sink, lambda: self.clock, authenticator=auth or verify)

    @staticmethod
    def request(**changes):
        return {'clientRunId': 'd'*32, 'board': 'SeasonOne', 'build': 'one',
                'rules': 'stage-v1', 'content': 'map-v1', 'seed': 'stage-seed',
                'ticket': 'verified-mobile-fixture', **changes}

    def event(self, service, receipt, kind, **changes):
        result = service.event(receipt['runId'], receipt['token'], {
            'sequence': receipt['sequence']+1, 'nonce': receipt['nonce'],
            'kind': kind, 'roomIndex': 1, 'room': 'arena', 'battleMs': 1000, **changes})
        result['token'] = receipt['token']
        return result

    def finished(self, service):
        receipt = service.start(self.request())
        receipt = self.event(service, receipt, 'enter')
        receipt = self.event(service, receipt, 'clear')
        finish = {'sequence': receipt['sequence']+1, 'nonce': receipt['nonce'],
                  'kind': 'finish', 'roomIndex': 1, 'room': 'arena', 'battleMs': 1000}
        return receipt, finish, service.event(receipt['runId'], receipt['token'], finish)

    def test_mobile_identity_and_non_steam_sink_can_finish_and_replay(self):
        service = self.make()
        receipt, finish, published = self.finished(service)
        self.assertEqual(('published', 75), (published['state'], published['score']))
        self.assertEqual([('another-game.ranked', 'mobile:fixture-player', 75)], self.sink.writes)
        self.assertEqual(published, service.event(receipt['runId'], receipt['token'], finish))
        self.assertEqual(1, len(self.sink.writes))

    def test_authentication_is_required_and_rejected_start_creates_no_run(self):
        service = self.make()
        with self.assertRaises(Rejected) as error:
            service.start(self.request(ticket='unverified'))
        self.assertEqual('invalid_ticket', error.exception.code)
        db = sqlite3.connect(service.db_path)
        try:
            self.assertEqual(0, db.execute('SELECT count(*) FROM runs').fetchone()[0])
        finally:
            db.close()

    def test_project_ledgers_and_signing_keys_do_not_share_receipts(self):
        one = self.make('first')
        two = self.make('second', key=b'separate-second-game-key-32-bytes!!!!')
        a, b = one.start(self.request()), two.start(self.request())
        self.assertNotEqual(a['runId'], b['runId'])
        with self.assertRaises(Rejected):
            two.status(a['runId'], a['token'])
        with self.assertRaises(Rejected):
            two.status(b['runId'], a['token'])
        self.assertEqual('active', one.status(a['runId'], a['token'])['state'])
        self.assertEqual('active', two.status(b['runId'], b['token'])['state'])

    def test_changed_start_and_progress_retry_are_still_rejected(self):
        service = self.make()
        receipt = service.start(self.request())
        self.assertEqual(receipt, service.start(self.request()))
        with self.assertRaises(Rejected):
            service.start(self.request(seed='different'))
        entered = self.event(service, receipt, 'enter')
        with self.assertRaises(Rejected):
            self.event(service, receipt, 'enter', battleMs=2000)
        self.assertEqual(1, entered['sequence'])

    def test_ambiguous_publication_is_preserved_without_blind_retry(self):
        self.sink.failure = OSError('synthetic lost acknowledgement')
        service = self.make()
        receipt, _, result = self.finished(service)
        self.assertEqual('unknown', result['state'])
        self.sink.failure = None
        self.assertEqual('unknown', service.status(receipt['runId'], receipt['token'])['state'])
        self.assertEqual([], self.sink.writes)

    def test_known_not_sent_publication_can_retry(self):
        self.sink.failure = WriteNotSent()
        service = self.make()
        receipt, _, result = self.finished(service)
        self.assertEqual('ready', result['state'])
        self.sink.failure = None
        self.assertEqual('published', service.status(receipt['runId'], receipt['token'])['state'])
        self.assertEqual(1, len(self.sink.writes))

    def test_steam_transport_uses_its_explicit_game_and_identity(self):
        path = Path(self.temp.name)/'synthetic-key'
        path.write_text('a'*32)
        transport = SteamPublisher(str(path), app_id=480, ticket_identity='another-game', descending_modes={'stages'})
        seen = []
        def request(route, params, post=False):
            seen.append(params)
            return {'response': {'params': {'result': 'OK', 'steamid': '76561198000000001'}}}
        transport._request = request
        self.assertEqual('76561198000000001', transport.authenticate('ab'))
        self.assertEqual('another-game', seen[0]['identity'])
        self.assertEqual(480, transport.app_id)


if __name__ == '__main__':
    unittest.main()
