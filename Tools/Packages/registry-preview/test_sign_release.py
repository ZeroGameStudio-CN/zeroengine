"""Archive byte preservation is separate from Unity's signature trust verdict."""
import io
import json
from pathlib import Path
import tarfile
import tempfile
import unittest

from sign_release import verify_archive


class SignedArchiveContract(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix='ze-signed-archive-')
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.source = self.root / 'source'
        self.source.mkdir()
        self.files = {'package.json': json.dumps({'zgsPilot': {'sourceCommit': 'a' * 40, 'sourceTree': 'b' * 40}}).encode(),
                      'Runtime/Example.cs': b'public class Example {}', 'Runtime/Example.cs.meta': b'guid: stable'}
        for name, data in self.files.items():
            path = self.source / name
            path.parent.mkdir(exist_ok=True)
            path.write_bytes(data)

    def archive(self, files):
        target = self.root / 'candidate.tgz'
        with tarfile.open(target, 'w:gz') as archive:
            for name, data in files.items():
                member = tarfile.TarInfo('package/' + name)
                member.size = len(data)
                archive.addfile(member, io.BytesIO(data))
        return target

    def test_preserves_every_source_and_guid_byte(self):
        archive = self.archive({**self.files, '.attestation.p7m': b'fixture-attestation'})
        result = verify_archive(archive, self.source, '123')
        self.assertEqual(result['matchedSourceFiles'], 3)
        self.assertTrue(result['sourceBytesUnchanged'])

    def test_rejects_changed_runtime_or_guid(self):
        for name in ('Runtime/Example.cs', 'Runtime/Example.cs.meta'):
            with self.subTest(name=name):
                files = {**self.files, '.attestation.p7m': b'fixture-attestation', name: b'changed'}
                with self.assertRaisesRegex(ValueError, 'changed'):
                    verify_archive(self.archive(files), self.source, '123')

    def test_rejects_unsigned_and_extra_files(self):
        for extra in ({}, {'.attestation.p7m': b'fixture', 'unexpected.cs': b'code'}):
            with self.subTest(extra=list(extra)):
                with self.assertRaisesRegex(ValueError, 'attestation'):
                    verify_archive(self.archive({**self.files, **extra}), self.source, '123')

    def test_rejects_escaping_member(self):
        with self.assertRaisesRegex(ValueError, 'archive path'):
            verify_archive(self.archive({**self.files, '.attestation.p7m': b'fixture', '../outside': b'code'}), self.source, '123')


if __name__ == '__main__':
    unittest.main()
