import hashlib
import importlib.util
import json
from pathlib import Path
import subprocess
import tarfile
import tempfile
import unittest

spec = importlib.util.spec_from_file_location('prepare_pob_core', Path(__file__).with_name('prepare_pob_core.py'))
MODULE = importlib.util.module_from_spec(spec)
spec.loader.exec_module(MODULE)


class ConsumerLockedPackageTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.repo = self.root / 'source'; self.repo.mkdir()
        self.project = self.root / 'consumer'; (self.project / 'Packages').mkdir(parents=True)
        for name in MODULE.SIBLINGS:
            package = self.repo / name; package.mkdir()
            (package / 'package.json').write_text(json.dumps({'name': name, 'version': '0.1.0', 'dependencies': {}}))
            (package / 'README.md').write_text('# Original committed content\n')
        core = self.repo / MODULE.NAME
        (core / 'Runtime').mkdir()
        (core / 'Runtime/Core.cs').write_text('// fixed source\n')
        (core / 'Runtime/Core.cs.meta').write_text('guid: ' + 'a' * 32 + '\n')
        self.git('init', '-q'); self.git('add', '.')
        self.git('-c', 'user.name=Test', '-c', 'user.email=test@example.invalid', 'commit', '-qm', 'Fixture')
        self.commit = self.git('rev-parse', 'HEAD').decode().strip()
        self.manifest = {'dependencies': {name: MODULE.REPOSITORY + '?path=' + name + '#' + self.commit for name in MODULE.SIBLINGS}}
        self.lock = {'dependencies': {name: {'source': 'git', 'version': locator, 'hash': self.commit}
                    for name, locator in self.manifest['dependencies'].items()}}
        self.save_inputs()

    def git(self, *args):
        return subprocess.check_output(['git', '-C', str(self.repo), *args], stderr=subprocess.DEVNULL)

    def save_inputs(self):
        for name, value in [('manifest.json', self.manifest), ('packages-lock.json', self.lock)]:
            (self.project / 'Packages' / name).write_text(json.dumps(value))

    def prepare(self, output='trial'):
        return MODULE.prepare(self.repo, self.project, self.root / output, 'ze-upm-pob-core-12345678-20261117')

    def test_both_versions_preserve_committed_runtime_and_metadata_files(self):
        before = {p.name: p.read_bytes() for p in (self.project / 'Packages').iterdir()}
        result = self.prepare()
        original = MODULE.committed_files(self.repo, self.commit, MODULE.NAME)
        self.assertEqual(len(result['versions']), 2)
        for version, item in result['versions'].items():
            path = self.root / 'trial' / item['asset']
            self.assertEqual(hashlib.sha256(path.read_bytes()).hexdigest(), item['sha256'])
            with tarfile.open(path) as archive:
                files = {p.name.removeprefix('package/'): archive.extractfile(p).read() for p in archive.getmembers()}
            metadata = json.loads(files.pop('package.json'))
            self.assertEqual(metadata['zgsPilot']['sourceCommit'], self.commit)
            self.assertEqual(metadata['version'], version)
            self.assertEqual(files, {k:v for k,v in original.items() if k != 'package.json'})
        self.assertEqual(before, {p.name:p.read_bytes() for p in (self.project / 'Packages').iterdir()})

    def test_mismatched_consumer_lock_never_creates_output(self):
        self.lock['dependencies'][MODULE.NAME]['hash'] = 'f' * 40
        self.save_inputs()
        with self.assertRaisesRegex(ValueError, 'manifest and lock differ'):
            self.prepare()
        self.assertFalse((self.root / 'trial').exists())

    def test_packages_are_byte_reproducible_without_overwriting_a_prior_run(self):
        first = self.prepare()
        second = self.prepare('second')
        self.assertEqual(first['versions'], second['versions'])
        with self.assertRaisesRegex(ValueError, 'preserve prior artifacts'):
            self.prepare()

    def test_dependency_expansion_is_refused(self):
        metadata = {'name': MODULE.NAME, 'version': '0.1.0', 'dependencies': {'other': '1.0.0'}}
        with self.assertRaisesRegex(ValueError, 'dependency-free'):
            MODULE.pack(self.root, {'package.json': json.dumps(metadata).encode()}, '0.1.0-trial.1', self.commit, 'a' * 40)
        self.assertEqual(list(self.root.glob('*.tgz')), [])


if __name__ == '__main__':
    unittest.main()
