"""Real SQLite retention, lock contention and rollback compatibility."""
from contextlib import contextmanager
from pathlib import Path
import sqlite3
import tempfile
import unittest
from unittest.mock import patch

from ze_leaderboards.ledger import SQLiteLedger, LedgerUnavailable
from ze_leaderboards.maintenance import GRACE_SECONDS, prune_history


class MaintenanceTests(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory(prefix="ze-ranked-maintenance-")
        self.addCleanup(temp.cleanup)
        self.ledger = SQLiteLedger(str(Path(temp.name)/"runs.sqlite3"))
        self.ledger.initialize(False)
        self.now = 1000+GRACE_SECONDS

    def run_row(self, name="a", state="published", expires=100, sequence=10):
        with self.ledger.transaction() as db:
            # Deliberately use the legacy positional INSERT: added GC metadata
            # must not make rollback to an old application binary unsafe.
            db.execute("INSERT INTO runs VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)", (
                name, "fixture-subject", name, "start", name, "{}", 0, expires,
                state, sequence, "fixture-nonce", 1, "fixture", 1, 100, 100, None))
            for number in range(sequence+1):
                db.execute("INSERT INTO events VALUES(?,?,?,?)", (
                    name, number, "unchanged-hash", '{"batch":{}}' if number == 8 else '{"receipt":"unchanged"}'))

    def sequences(self, name="a"):
        with self.ledger.snapshot() as db:
            return [row[0] for row in db.execute("SELECT sequence FROM events WHERE run_id=? ORDER BY sequence", (name,))]

    def test_expiry_grace_and_final_batch_receipts_survive(self):
        self.run_row()
        self.assertEqual(0, prune_history(self.ledger, 100+GRACE_SECONDS)["deleted"])
        self.assertEqual(list(range(11)), self.sequences())
        self.assertEqual(8, prune_history(self.ledger, self.now)["deleted"])
        self.assertEqual([0, 8, 10], self.sequences())
        with self.ledger.snapshot() as db:
            self.assertEqual("published", db.execute("SELECT state FROM runs WHERE id='a'").fetchone()[0])
            self.assertEqual("unchanged-hash", db.execute("SELECT hash FROM events WHERE run_id='a' AND sequence=10").fetchone()[0])
        self.assertEqual(0, prune_history(self.ledger, self.now+600)["queued"])

    def test_chunks_resume_after_restart_without_rescanning_finished_runs(self):
        self.run_row()
        result = prune_history(self.ledger, self.now, max_runs=1, max_events=3)
        self.assertEqual(3, result["deleted"])
        restarted = SQLiteLedger(self.ledger.path)
        restarted.initialize(False)
        for _ in range(3):
            result = prune_history(restarted, self.now, max_runs=1, max_events=3)
            self.assertLessEqual(result["deleted"], 3)
            self.assertEqual(0, result["queued"])
        self.assertEqual([0, 8, 10], self.sequences())
        with self.ledger.snapshot() as db:
            self.assertEqual(0, db.execute("SELECT count(*) FROM history_pending").fetchone()[0])

    def test_holds_are_preserved_and_can_be_cleaned_after_resolution(self):
        for state in ("ready", "writing", "unknown", "review", "not_published"):
            self.run_row(state, state)
        result = prune_history(self.ledger, self.now)
        self.assertEqual(5, result["deferred"])
        for state in ("ready", "writing", "unknown", "review", "not_published"):
            self.assertEqual(list(range(11)), self.sequences(state))
        with self.ledger.transaction() as db:
            db.execute("UPDATE runs SET state='published' WHERE id='review'")
        prune_history(self.ledger, self.now+3601)
        self.assertEqual([0, 8, 10], self.sequences("review"))
        self.assertEqual(list(range(11)), self.sequences("unknown"))

    def test_abandoned_runs_and_future_expiries_from_old_writers(self):
        self.run_row("old", "active")
        prune_history(self.ledger, self.now)
        self.run_row("new", "revoked", expires=self.now+10)
        prune_history(self.ledger, self.now)
        self.assertEqual(list(range(11)), self.sequences("new"))
        prune_history(self.ledger, self.now+GRACE_SECONDS+11)
        self.assertEqual([0, 8, 10], self.sequences("new"))
        self.assertEqual([0, 8, 10], self.sequences("old"))

    def test_json_scan_does_not_reserve_the_writer(self):
        self.run_row()
        snapshot = self.ledger.snapshot
        writes = []
        @contextmanager
        def concurrent_snapshot():
            with snapshot() as db:
                # Acquire the read snapshot before another connection writes.
                db.execute("SELECT count(*) FROM events").fetchone()
                with self.ledger.transaction(timeout=0) as writer:
                    writer.execute("INSERT INTO limits VALUES('concurrent-gameplay',?,1)", (len(writes),))
                    writes.append(True)
                yield db
        with patch.object(self.ledger, "snapshot", concurrent_snapshot):
            prune_history(self.ledger, self.now)
        self.assertTrue(writes)
        self.assertEqual([0, 8, 10], self.sequences())

    def test_busy_api_is_retryable_and_maintenance_yields(self):
        self.run_row()
        with self.ledger.transaction() as blocker:
            with self.assertRaises(LedgerUnavailable):
                with self.ledger.transaction(timeout=0):
                    self.fail("Competing writer admitted")
            self.assertTrue(prune_history(self.ledger, self.now)["busy"])
        self.assertEqual(list(range(11)), self.sequences())
        prune_history(self.ledger, self.now)
        self.assertEqual([0, 8, 10], self.sequences())

    def test_expiry_walk_uses_the_covering_index(self):
        with self.ledger.snapshot() as db:
            plan = list(db.execute("""EXPLAIN QUERY PLAN SELECT id,expires FROM runs
                WHERE expires<? AND (expires,id)>(?,?) ORDER BY expires,id LIMIT ?""", (self.now, -1, "", 256)))
        description = " ".join(row[3] for row in plan)
        self.assertIn("SEARCH runs USING COVERING INDEX runs_history_expiry", description)
        self.assertNotIn("TEMP B-TREE", description)


class SQLiteErrorTests(unittest.TestCase):
    def test_connection_begin_body_commit_and_close_map_only_busy_errors(self):
        for boundary in ("connect", "begin", "body", "commit", "close"):
            for code in (sqlite3.SQLITE_BUSY, sqlite3.SQLITE_LOCKED, 261, 262, sqlite3.SQLITE_CORRUPT):
                with self.subTest(boundary=boundary, code=code):
                    error = sqlite3.OperationalError("synthetic error without credentials")
                    error.sqlite_errorcode = code
                    class Connection:
                        row_factory = None
                        def execute(self, statement):
                            if boundary == "begin" and statement.startswith("BEGIN"):
                                raise error
                        def commit(self):
                            if boundary == "commit":
                                raise error
                        def rollback(self):
                            pass
                        def close(self):
                            if boundary == "close":
                                raise error
                    def connect(*args, **kwargs):
                        if boundary == "connect":
                            raise error
                        return Connection()
                    expected = sqlite3.OperationalError if code == sqlite3.SQLITE_CORRUPT else LedgerUnavailable
                    with patch("ze_leaderboards.ledger.sqlite3.connect", connect):
                        with self.assertRaises(expected):
                            with SQLiteLedger("unused").transaction():
                                if boundary == "body":
                                    raise error

    def test_application_errors_are_not_relabelled_as_storage_failures(self):
        temp = tempfile.TemporaryDirectory(prefix="ze-ranked-rollback-")
        self.addCleanup(temp.cleanup)
        ledger = SQLiteLedger(str(Path(temp.name)/"db"))
        ledger.initialize(False)
        with self.assertRaisesRegex(ValueError, "fixture rejection"):
            with ledger.transaction() as db:
                db.execute("INSERT INTO limits VALUES('must-rollback',0,1)")
                raise ValueError("fixture rejection")
        with ledger.snapshot() as db:
            self.assertEqual(0, db.execute("SELECT count(*) FROM limits").fetchone()[0])


if __name__ == "__main__":
    unittest.main()
