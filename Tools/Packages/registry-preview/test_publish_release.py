import unittest

from publish_release import PUBLIC, build_candidate


class PromotionContract(unittest.TestCase):
    name = 'com.zerogamestudio.zeroengine.activity'
    base = 'https://github.com/ZeroGameStudio-CN/zeroengine/releases/download/'

    def entry(self, version):
        record = {'asset': f'{self.name}-{version}.tgz', 'sourceCommit': 'a' * 40, 'sourceTree': 'b' * 40,
                  'sha512': 'sha512-example', 'dependencies': {}}
        return {'name': self.name, 'version': version, 'record': record,
                'metadata': {'name': self.name, 'version': version, 'displayName': 'Activity', 'dependencies': {},
                             'zgsPilot': {'sourceCommit': 'a' * 40, 'sourceTree': 'b' * 40},
                             'dist': {'tarball': f'{PUBLIC}/{self.name}/-/{record["asset"]}', 'integrity': record['sha512']}}}

    def test_backup_keeps_original_asset_location_when_new_metadata_is_published(self):
        first = self.entry('1.1.0')
        old_base = self.base + 'old/'
        previous, _ = build_candidate({'publicBase': PUBLIC, 'packages': {}}, [first], old_base, {})
        new_base = self.base + 'new/'
        new, meta = build_candidate(previous, [self.entry('1.1.1-ze-test.1')], new_base,
                                    {self.name: {'1.1.0': first['metadata']}})
        package = new['packages'][self.name]
        self.assertEqual(package['versions']['1.1.0']['githubBase'], old_base)
        self.assertEqual(package['githubBase'], new_base)
        self.assertEqual(meta[self.name]['dist-tags'], {'latest': '1.1.0', 'preview': '1.1.1-ze-test.1'})
        self.assertEqual(package['versions']['1.1.0']['sha512'], first['record']['sha512'])
        self.assertNotIn('githubBase', previous['packages'][self.name]['versions']['1.1.0'])

    def test_checks_original_versions_and_candidate_dependencies_before_promotion(self):
        first = self.entry('1.1.0')
        existing, _ = build_candidate({'publicBase': PUBLIC, 'packages': {}}, [first], self.base + 'old/', {})
        with self.assertRaisesRegex(ValueError, 'already admitted'):
            build_candidate(existing, [first], self.base + 'new/', {})
        unrelated = self.entry('1.1.1')
        pin = {'com.zerogamestudio.zeroengine.core': '2.2.2'}
        unrelated['record']['dependencies'] = unrelated['metadata']['dependencies'] = pin
        with self.assertRaisesRegex(ValueError, 'not admitted'):
            build_candidate(existing, [unrelated], self.base + 'new/', {self.name: {'1.1.0': first['metadata']}})


if __name__ == '__main__':
    unittest.main()
