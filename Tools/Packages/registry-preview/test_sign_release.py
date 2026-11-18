"""Archive byte preservation is separate from Unity's signature trust verdict."""
import io
import json
from pathlib import Path
import tarfile
import tempfile
import unittest

from sign_release import plan_cohort, verify_archive


class CohortContract(unittest.TestCase):
    core = 'com.zerogamestudio.zeroengine.core'
    application = 'com.zerogamestudio.zeroengine.achievement'

    def load(self, name):
        if name == self.core:
            return 'a' * 40, {'name': name, 'version': '2.2.2'}
        return 'b' * 40, {'name': name, 'version': '1.0.0',
                          'dependencies': {self.core: '2.0.0', 'com.unity.ugui': '1.0.0'}}

    def test_uses_verified_sibling_versions_without_changing_unity_dependencies(self):
        resolved, new = plan_cohort(self.load, {self.application: None}, {}, '123')
        self.assertEqual(list(new), [self.core, self.application])
        self.assertEqual(resolved[self.core], '2.2.2')
        self.assertEqual(new[self.application]['dependencies'], {self.core: '2.2.2', 'com.unity.ugui': '1.0.0'})

    def test_reuses_the_existing_parent_and_refuses_mutating_an_admitted_version(self):
        core = {'sourceTree': 'a' * 40, 'dependencies': {}, 'signingOrganizationId': '123'}
        existing = {self.core: {'versions': {'2.2.2': core}}}
        _, new = plan_cohort(self.load, {self.application: '1.0.1'}, existing, '123')
        self.assertNotIn(self.core, new)
        core['sourceTree'] = 'c' * 40
        with self.assertRaisesRegex(ValueError, 'immutable'):
            plan_cohort(self.load, {self.application: None}, existing, '123')

    def test_rejects_project_private_dependencies(self):
        def private(name):
            return 'b' * 40, {'name': name, 'version': '1.0.0',
                              'dependencies': {'com.zerogamestudio.pob.formula': '0.1.0'}}
        with self.assertRaisesRegex(ValueError, 'Project-local'):
            plan_cohort(private, {self.application: None}, {}, '123')


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

    def test_allows_only_unchanged_manifest_data_and_ignored_git_control_files(self):
        (self.source / '.gitignore').write_bytes(b'fixture')
        compact = json.dumps(json.loads(self.files['package.json']), separators=(',', ':')).encode()
        archive = self.archive({**self.files, 'package.json': compact, '.attestation.p7m': b'fixture'})
        report = verify_archive(archive, self.source, '123')
        self.assertTrue(report['runtimeBytesUnchanged'])
        self.assertTrue(report['packageJsonReformatted'])
        self.assertEqual(report['omittedGitIgnore'], ['.gitignore'])
        with self.assertRaisesRegex(ValueError, 'metadata'):
            verify_archive(self.archive({**self.files, 'package.json': b'{}', '.attestation.p7m': b'fixture'}), self.source, '123')


if __name__ == '__main__':
    unittest.main()
