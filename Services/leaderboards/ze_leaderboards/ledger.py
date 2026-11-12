"""Private run ledger. SQLite remains the default; PostgreSQL is explicit opt-in."""
from __future__ import annotations

import contextlib
import hashlib
import sqlite3


SCHEMA = """
CREATE TABLE IF NOT EXISTS runs (
    id TEXT PRIMARY KEY, steam_id TEXT NOT NULL, client_id TEXT NOT NULL,
    start_hash TEXT NOT NULL, ticket_hash TEXT NOT NULL UNIQUE,
    policy TEXT NOT NULL, started DOUBLE PRECISION NOT NULL, expires DOUBLE PRECISION NOT NULL,
    state TEXT NOT NULL, sequence INTEGER NOT NULL, nonce TEXT NOT NULL,
    room_index INTEGER NOT NULL, room TEXT NOT NULL, cleared INTEGER NOT NULL,
    battle_ms INTEGER NOT NULL, score INTEGER, reason TEXT,
    UNIQUE(steam_id, client_id));
CREATE TABLE IF NOT EXISTS events (
    run_id TEXT NOT NULL, sequence INTEGER NOT NULL, hash TEXT NOT NULL,
    response TEXT NOT NULL, PRIMARY KEY(run_id, sequence));
CREATE TABLE IF NOT EXISTS limits (
    bucket TEXT NOT NULL, period INTEGER NOT NULL, count INTEGER NOT NULL,
    PRIMARY KEY(bucket, period));
CREATE INDEX IF NOT EXISTS runs_history_expiry ON runs(expires, id);
CREATE TABLE IF NOT EXISTS history_cursor (
    name TEXT PRIMARY KEY, expires DOUBLE PRECISION NOT NULL, run_id TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS history_pending (
    run_id TEXT PRIMARY KEY, due DOUBLE PRECISION NOT NULL);
CREATE INDEX IF NOT EXISTS history_pending_due ON history_pending(due, run_id);
"""


class LedgerUnavailable(Exception):
    """A retryable database failure, including a commit with no acknowledgement."""


class SQLiteLedger:
    shared = False

    def __init__(self, path):
        self.path = path

    def initialize(self, recover_interrupted):
        with self.transaction() as db:
            db.executescript("PRAGMA journal_mode=WAL;" + SCHEMA)
            if recover_interrupted:
                db.execute("UPDATE runs SET state='unknown', reason='interrupted_write' WHERE state='writing'")

    @contextlib.contextmanager
    def _connection(self, *, read_only=False, timeout=5):
        try:
            with contextlib.closing(sqlite3.connect(self.path, timeout=timeout)) as db:
                db.row_factory = sqlite3.Row
                try:
                    db.execute("PRAGMA query_only=ON" if read_only else "PRAGMA synchronous=FULL")
                    db.execute("BEGIN" if read_only else "BEGIN IMMEDIATE")
                    yield db
                    db.commit()
                except BaseException:
                    # A rollback error must not hide the original failure.
                    with contextlib.suppress(sqlite3.Error):
                        db.rollback()
                    raise
        except sqlite3.Error as error:
            if getattr(error, "sqlite_errorcode", 0) & 0xff in (sqlite3.SQLITE_BUSY, sqlite3.SQLITE_LOCKED):
                raise LedgerUnavailable("Leaderboard ledger temporarily unavailable") from None
            raise

    def transaction(self, lock_key=None, *, timeout=5):
        return self._connection(timeout=timeout)

    def snapshot(self):
        # WAL readers do not reserve the writer while examining old JSON.
        return self._connection(read_only=True, timeout=0)


class _Row(dict):
    # Existing ledger readers use both named fields and positional scalar reads.
    def __getitem__(self, key):
        return tuple(self.values())[key] if isinstance(key, int) else super().__getitem__(key)


def _row_factory(cursor):
    names = [column.name for column in cursor.description] if cursor.description else []
    return lambda values: _Row(zip(names, values))


class _PostgresConnection:
    def __init__(self, connection):
        self.connection = connection

    def execute(self, statement, parameters=()):
        # These are application-owned static qmark statements, never user SQL.
        return self.connection.execute(statement.replace("?", "%s"), parameters)


class PostgresLedger:
    shared = True

    def __init__(self, dsn, *, schema_lock="schema:zeroengine-leaderboards"):
        self.schema_lock = schema_lock
        # Optional dependency: ordinary SQLite installations do not import it.
        import psycopg
        self._connect = psycopg.connect
        self._unavailable = (psycopg.OperationalError, psycopg.errors.ReadOnlySqlTransaction,
                             psycopg.errors.SerializationFailure, psycopg.errors.DeadlockDetected,
                             psycopg.errors.QueryCanceled, psycopg.errors.LockNotAvailable)
        self._dsn = dsn

    def initialize(self, recover_interrupted):
        with self.transaction(self.schema_lock) as db:
            for statement in SCHEMA.split(";"):
                if statement.strip():
                    db.execute(statement)
        # A second API starting is not evidence that another publisher crashed.
        # Shared publication claims are recovered per run, never in a global UPDATE.

    @contextlib.contextmanager
    def transaction(self, lock_key=None, *, timeout=5):
        # libpq selects a writable server on every new transaction connection.
        # Never retry here: a lost COMMIT acknowledgement has an unknown outcome.
        try:
            with self._connect(self._dsn, target_session_attrs="read-write", connect_timeout=10,
                               row_factory=_row_factory,
                               options=f"-c statement_timeout=15000 -c lock_timeout={max(1, int(timeout*1000))} -c synchronous_commit=on") as connection:
                db = _PostgresConnection(connection)
                if lock_key is not None:
                    key = int.from_bytes(hashlib.sha256(lock_key.encode()).digest()[:8], "big", signed=True)
                    connection.execute("SELECT pg_advisory_xact_lock(%s)", (key,))
                yield db
        except self._unavailable:
            raise LedgerUnavailable("Leaderboard ledger temporarily unavailable") from None

    def snapshot(self):
        return self.transaction()
