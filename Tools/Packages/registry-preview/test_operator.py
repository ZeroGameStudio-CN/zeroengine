import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location('preview_operator', Path(__file__).with_name('operator.py'))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class PreviewOperatorTests(unittest.TestCase):
    def responses(self, existing=None, proxied=False):
        return [[{'id': 'zone'}], existing or [], [{'type': 'A', 'content': '192.0.2.1', 'proxied': proxied}], {'id': 'created'}]

    def test_dns_creates_only_the_exact_absent_name_and_records_rollback(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            with patch.object(module, 'api', side_effect=self.responses()) as api:
                module.dns(root)
            self.assertEqual(api.call_count, 4)
            request = api.call_args.kwargs
            self.assertEqual(request['method'], 'POST')
            self.assertEqual(request['data']['name'], module.HOST)
            self.assertFalse(request['data']['proxied'])
            self.assertEqual(json.loads((root / 'dns-before.json').read_text())['records'], [])
            self.assertEqual(json.loads((root / 'dns-created.json').read_text())['id'], 'created')

    def test_dns_existing_name_never_writes(self):
        with tempfile.TemporaryDirectory() as directory:
            with patch.object(module, 'api', side_effect=self.responses(existing=[{'id': 'foreign'}])) as api:
                with self.assertRaises(RuntimeError): module.dns(Path(directory))
            self.assertEqual(api.call_count, 3)
            self.assertFalse((Path(directory) / 'dns-intent.json').exists())

    def test_dns_refuses_a_proxied_or_unverified_origin(self):
        with tempfile.TemporaryDirectory() as directory:
            with patch.object(module, 'api', side_effect=self.responses(proxied=True)) as api:
                with self.assertRaises(RuntimeError): module.dns(Path(directory))
            self.assertEqual(api.call_count, 3)

    def test_dns_uncertain_original_intent_is_not_replayed(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory); (root / 'dns-intent.json').write_text('{}')
            with patch.object(module, 'api') as api:
                with self.assertRaises(RuntimeError): module.dns(root)
            api.assert_not_called()

    def test_existing_vhost_is_not_replaced(self):
        with tempfile.TemporaryDirectory() as directory:
            existing = Path(directory) / 'foreign.conf'; existing.write_text('preserve')
            with patch.object(module, 'VHOST', existing), patch.object(module, 'call') as execute:
                with self.assertRaises(RuntimeError): module.edge(Path(directory))
            execute.assert_not_called(); self.assertEqual(existing.read_text(), 'preserve')


if __name__ == '__main__': unittest.main()
