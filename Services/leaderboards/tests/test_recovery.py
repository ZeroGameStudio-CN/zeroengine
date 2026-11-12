"""A sealed bundle must restore pinned packages, data and private config."""
import hashlib
from contextlib import closing
import json
from pathlib import Path
import shutil
import sqlite3
import subprocess
import sys
import tarfile
import tempfile
import tracemalloc
import unittest
from unittest.mock import patch

from ze_leaderboards.recovery import backup, verify_bundle, CHUNK


class RecoveryTests(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory(prefix="ze-ranked-recovery-")
        self.addCleanup(temp.cleanup)
        self.root = Path(temp.name)
        self.app = self.root/"app"
        self.app.mkdir()
        package = Path(__file__).resolve().parents[1]/"ze_leaderboards"
        target = self.app/"ze_leaderboards"
        target.mkdir()
        for path in package.glob("*.py"):
            shutil.copyfile(path, target/path.name)
        (target/"LICENSE").write_text("fixture license")
        self.manifest = {"schemaVersion": 1, "revision": "a"*40, "files": {
            "ze_leaderboards/"+p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in target.iterdir()}}
        self.save_manifest()
        (self.app/"app.py").write_text("from ze_leaderboards.core import RunService\n")
        (self.app/"service.py").write_text("from ze_leaderboards.ledger import SQLiteLedger\n")
        (self.app/"constraints.txt").write_text("fixture==1.0\n")
        self.db = self.root/"live.sqlite3"
        with closing(sqlite3.connect(self.db)) as db:
            db.execute("CREATE TABLE scores(id INTEGER PRIMARY KEY,score INTEGER,data BLOB)")
            db.execute("INSERT INTO scores VALUES(1,42,zeroblob(?))", (12*CHUNK,))
            db.commit()
        self.layout = {"ledger": str(self.db), "app": str(self.app), "units": {}}
        for name in ("policy", "signing-key", "publisher-key"):
            path = self.root/name
            path.write_text("fixture-only-not-a-real-credential")
            self.layout[name] = str(path)
        unit = self.root/"fixture.service"
        unit.write_text("[Service]\nExecStart=/fixture\n")
        self.layout["units"]["fixture.service"] = str(unit)
        self.output = self.root/"backups"

    def save_manifest(self):
        (self.app/"zeroengine-source.json").write_text(json.dumps(self.manifest))

    def test_cold_restore_contains_data_pinned_package_and_runtime_dependencies(self):
        result = backup(self.layout, self.output, "20261112T000000Z")
        bundle = self.output/result["bundle"]
        manifest = verify_bundle(bundle)
        for name in ("core.py", "ledger.py", "maintenance.py", "recovery.py", "LICENSE"):
            self.assertIn("app/ze_leaderboards/"+name, manifest["files"])
        self.assertIn("app/zeroengine-source.json", manifest["files"])
        self.assertIn("app/constraints.txt", manifest["files"])
        restored = self.root/"isolated-restore"
        restored.mkdir()
        with tarfile.open(bundle) as archive:
            for member in archive:
                # All names/types were validated by verify_bundle above.
                path = restored/member.name
                path.parent.mkdir(parents=True, exist_ok=True)
                with archive.extractfile(member) as source, path.open("wb") as target:
                    shutil.copyfileobj(source, target, CHUNK)
        process = subprocess.run([sys.executable, "-B", "-c",
            "import app,service; from ze_leaderboards.recovery import verify_bundle"],
            cwd=restored/"app", capture_output=True, text=True)
        self.assertEqual(0, process.returncode, process.stderr)
        with closing(sqlite3.connect(restored/"state/runs.sqlite3")) as db:
            self.assertEqual((42, 12*CHUNK), db.execute("SELECT score,length(data) FROM scores").fetchone())
        self.assertEqual("fixture-only-not-a-real-credential", (restored/"config/signing-key").read_text())

    def test_large_database_is_streamed_with_bounded_python_memory(self):
        tracemalloc.start()
        try:
            with patch.object(Path, "read_bytes", side_effect=AssertionError("Unbounded whole-file read")):
                result = backup(self.layout, self.output, "20261112T000000Z")
            _, peak = tracemalloc.get_traced_memory()
        finally:
            tracemalloc.stop()
        self.assertLess(peak, 4*CHUNK)
        self.assertTrue((self.output/result["database"]).is_file())

    def test_missing_runtime_or_bad_pin_cannot_rotate_a_good_backup(self):
        first = backup(self.layout, self.output, "20261112T000000Z")
        original = hashlib.sha256((self.output/first["bundle"]).read_bytes()).hexdigest()
        (self.app/"ze_leaderboards/core.py").write_text("changed source\n")
        with self.assertRaisesRegex(ValueError, "source hash mismatch"):
            backup(self.layout, self.output, "20261112T000001Z")
        self.assertEqual(original, hashlib.sha256((self.output/first["bundle"]).read_bytes()).hexdigest())
        self.assertEqual([], list(self.output.glob("*.partial")))
        (self.app/"ze_leaderboards/core.py").unlink()
        with self.assertRaises(ValueError):
            backup(self.layout, self.output, "20261112T000002Z")
        self.assertTrue((self.output/first["database"]).exists())

    def test_package_without_manifest_and_manifest_escape_are_rejected(self):
        (self.app/"zeroengine-source.json").unlink()
        with self.assertRaisesRegex(ValueError, "provenance missing"):
            backup(self.layout, self.output, "20261112T000000Z")
        self.manifest["files"]["ze_leaderboards/../../private"] = "a"*64
        self.save_manifest()
        with self.assertRaisesRegex(ValueError, "source member"):
            backup(self.layout, self.output, "20261112T000001Z")

    def test_bundle_verification_precedes_retention_and_preserves_manual_holds(self):
        self.output.mkdir(mode=0o700)
        hold = self.output/"manual-preservation.tar.gz"
        hold.write_text("must remain")
        for number in range(4):
            backup(self.layout, self.output, f"20261112T00000{number}Z")
        self.assertEqual(3, len(list(self.output.glob("recovery-*.tar.gz"))))
        self.assertEqual(1, len(list(self.output.glob("runs-*.sqlite3"))))
        self.assertEqual("must remain", hold.read_text())
        sealed = sorted(p.name for p in self.output.iterdir())
        with patch("ze_leaderboards.recovery.verify_bundle", side_effect=ValueError("fixture verification failed")):
            with self.assertRaisesRegex(ValueError, "verification failed"):
                backup(self.layout, self.output, "20261112T000004Z")
        self.assertEqual(sealed, sorted(p.name for p in self.output.iterdir()))


if __name__ == "__main__":
    unittest.main()
