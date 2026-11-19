import base64
import copy
import hashlib
import io
import json
from pathlib import Path
import tarfile
import tempfile
import unittest

from activate_release import check_public_index
from publish_release import PUBLIC
from release import signed_receipt
from worker_release import require_trust, unpack_bundle


class ReleaseBoundaries(unittest.TestCase):
    name = 'com.zerogamestudio.zeroengine.activity'

    def test_signed_receipt_checks_archive_directory(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            asset = f'{self.name}-1.1.0.tgz'
            data = b'signed fixture'
            prepared = root / 'prepared'
            signed = root / 'signed'
            prepared.mkdir()
            (signed / self.name).mkdir(parents=True)
            (prepared / 'release-input.json').write_text(json.dumps({
                'commit': 'a' * 40, 'organizationKey': '123', 'packages': {self.name: {'version': '1.1.0'}}
            }), encoding='utf-8')
            record = {'asset': asset, 'bytes': len(data), 'sha256': hashlib.sha256(data).hexdigest(),
                      'sha512': 'sha512-' + base64.b64encode(hashlib.sha512(data).digest()).decode()}
            receipt = {'schemaVersion': 2, 'sourceCommit': 'a' * 40, 'organizationKey': '123',
                       'versions': {self.name: '1.1.0'}, 'packages': {self.name: record}}
            (signed / 'signed-release.json').write_text(json.dumps(receipt), encoding='utf-8')
            with self.assertRaisesRegex(ValueError, 'Missing signed archives'):
                signed_receipt(prepared, signed)
            (signed / self.name / asset).write_bytes(data)
            self.assertEqual(signed_receipt(prepared, signed), receipt)
            orphan = signed / 'unused.tgz'
            orphan.write_bytes(data)
            with self.assertRaisesRegex(ValueError, 'Unused signed archives: unused.tgz'):
                signed_receipt(prepared, signed)
            self.assertEqual(orphan.read_bytes(), data)

    def test_catalog_check_rejects_missing_versions_and_changed_integrity(self):
        asset = self.name + '-1.1.0.tgz'
        record = {'asset': asset, 'sourceCommit': 'a' * 40, 'sourceTree': 'b' * 40, 'sha512': 'frozen'}
        tags = {'latest': '1.1.0'}
        catalog = {'packages': {self.name: {'versions': {'1.1.0': record}, 'tags': tags}}}
        version = {'name': self.name, 'version': '1.1.0', 'dist': {
            'integrity': record['sha512'], 'tarball': f'{PUBLIC}/{self.name}/-/{asset}'},
            'zgsPilot': {'sourceCommit': record['sourceCommit'], 'sourceTree': record['sourceTree']}}
        index = {'_updated': 0, self.name: {'name': self.name, 'versions': {'1.1.0': version}, 'dist-tags': tags}}
        check_public_index(json.dumps(index), catalog)
        stale = copy.deepcopy(index)
        stale[self.name]['versions'].clear()
        with self.assertRaisesRegex(ValueError, 'versions or tags differ'):
            check_public_index(json.dumps(stale), catalog)
        version['dist']['integrity'] = 'changed'
        with self.assertRaisesRegex(ValueError, 'metadata differs'):
            check_public_index(json.dumps(index), catalog)
        version['dist']['integrity'] = record['sha512']
        record.pop('sourceTree')
        version['zgsPilot'].pop('sourceTree')
        check_public_index(json.dumps(index), catalog)

    def test_origin_requires_the_exact_local_unity_proof(self):
        signed = {'packages': {self.name: {'sha256': 'a' * 64}}, 'versions': {self.name: '1.1.0'}}
        row = {'name': self.name, 'version': '1.1.0', 'sha256': 'a' * 64,
               'signatureStatus': 'Valid', 'source': 'LocalTarball'}
        require_trust(signed, {'packages': [row]})
        for change in ({'sha256': 'b' * 64}, {'source': 'Registry'}, {'signatureStatus': 'Invalid'}):
            with self.subTest(change=change), self.assertRaisesRegex(ValueError, 'Unity verification'):
                require_trust(signed, {'packages': [dict(row, **change)]})

    def test_transfer_refuses_a_tar_member_outside_the_output_root(self):
        payload = io.BytesIO()
        with tarfile.open(fileobj=payload, mode='w') as bundle:
            entry = tarfile.TarInfo('../outside')
            entry.size = 1
            bundle.addfile(entry, io.BytesIO(b'x'))
        payload.seek(0)
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            with self.assertRaisesRegex(ValueError, 'Unexpected'):
                unpack_bundle(payload, root / 'unpacked')
            self.assertFalse((root / 'outside').exists())


if __name__ == '__main__':
    unittest.main()
