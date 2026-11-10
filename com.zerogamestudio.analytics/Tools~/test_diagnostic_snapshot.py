import base64
import gzip
import hashlib
import json
import unittest

import diagnostic_snapshot as subject


class DiagnosticSnapshotTests(unittest.TestCase):
    def props(self, raw=None):
        raw = raw or json.dumps({"state": "native-unavailable", "label": "合成现场"}).encode()
        return {"diagnostic_id": "1" * 32, "diagnostic_revision": 1, "diagnostic_kind": "fixture",
                "snapshot_encoding": "gzip-base64", "snapshot_data": base64.b64encode(gzip.compress(raw)).decode(),
                "snapshot_sha256": hashlib.sha256(raw).hexdigest(), "snapshot_bytes": len(raw)}

    def test_reads_authorized_query_envelope_and_links_identity(self):
        result = subject.snapshots({"data": {"detail": [{"event": "diagnostic_incident", "props": json.dumps(self.props())}]}})
        self.assertEqual(result[0]["diagnostic_id"], "1" * 32)
        self.assertEqual(result[0]["snapshot"]["label"], "合成现场")

    def test_rejects_changed_hash(self):
        props = self.props(); props["snapshot_sha256"] = "0" * 64
        with self.assertRaises(ValueError): subject.decode(props)

    def test_refuses_truncated_query_response(self):
        response = [{"event": "diagnostic_incident", "props": self.props(), "props_detail_truncated": True}]
        with self.assertRaises(ValueError): subject.snapshots(response)

    def test_bounds_decompression(self):
        with self.assertRaises(ValueError): subject.decode(self.props(b"x" * (subject.MAX_SNAPSHOT_BYTES + 1)))


if __name__ == "__main__":
    unittest.main()
