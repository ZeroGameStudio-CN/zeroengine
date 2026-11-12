"""Incremental expiry maintenance; no publisher, credential or gameplay changes."""
from __future__ import annotations

import time

from .ledger import LedgerUnavailable

GRACE_SECONDS = 3 * 86400
PRUNABLE = frozenset(("published", "revoked", "active"))


def prune_history(ledger, now, *, max_runs=256, max_events=128, budget_seconds=2):
    """Retain start/final/last-batch replay; leave live and held runs untouched.

    The expiry cursor only visits each immutable run expiry once. Deferred holds
    stay in an indexed queue, so resolving a review later does not lose cleanup.
    All newly issued expiries are ahead of this three-day-old watermark, even
    when an old server binary temporarily runs during rollback.
    """
    if not 1 <= max_runs <= 256 or not 1 <= max_events <= 128 or not 0 < budget_seconds <= 2:
        raise ValueError("Maintenance budget out of range")
    deadline = time.monotonic() + budget_seconds
    result = {"queued": 0, "processed": 0, "deleted": 0, "deferred": 0, "busy": False}
    try:
        with ledger.transaction("history-prune", timeout=0) as db:
            cursor = db.execute("SELECT expires,run_id FROM history_cursor WHERE name='events'").fetchone()
            after = (cursor[0], cursor[1]) if cursor else (-1, "")
            rows = db.execute("""SELECT id,expires FROM runs WHERE expires<? AND (expires,id)>(?,?)
                ORDER BY expires,id LIMIT ?""", (now-GRACE_SECONDS, *after, max_runs)).fetchall()
            for row in rows:
                db.execute("INSERT INTO history_pending VALUES(?,?) ON CONFLICT(run_id) DO NOTHING", (row[0], now))
            if rows:
                db.execute("""INSERT INTO history_cursor VALUES('events',?,?) ON CONFLICT(name)
                    DO UPDATE SET expires=excluded.expires,run_id=excluded.run_id""", (rows[-1][1], rows[-1][0]))
            result["queued"] = len(rows)

        for _ in range(max_runs):
            if time.monotonic() >= deadline:
                break
            with ledger.snapshot() as db:
                pending = db.execute("""SELECT p.run_id,r.sequence,r.state FROM history_pending p
                    LEFT JOIN runs r ON r.id=p.run_id WHERE p.due<=? ORDER BY p.due,p.run_id LIMIT 1""", (now,)).fetchone()
                if not pending:
                    break
                run_id, sequence, state = pending[0], pending[1], pending[2]
                marker = 0
                if state in PRUNABLE:
                    batch = db.execute("""SELECT sequence FROM events WHERE run_id=? AND sequence>0
                        AND response LIKE ? ORDER BY sequence DESC LIMIT 1""", (run_id, '%"batch":%')).fetchone()
                    marker = batch[0] if batch else 0
            if time.monotonic() >= deadline:
                break
            with ledger.transaction("history-prune", timeout=0) as db:
                current = db.execute("SELECT sequence,state,expires FROM runs WHERE id=?", (run_id,)).fetchone()
                if not current:
                    db.execute("DELETE FROM history_pending WHERE run_id=?", (run_id,))
                elif current[1] not in PRUNABLE or current[2] >= now-GRACE_SECONDS:
                    db.execute("UPDATE history_pending SET due=? WHERE run_id=?", (max(now+3600, current[2]+GRACE_SECONDS+1), run_id))
                    result["deferred"] += 1
                elif current[0] == sequence and state in PRUNABLE:
                    deleted = db.execute("""DELETE FROM events WHERE run_id=? AND sequence IN
                        (SELECT sequence FROM events WHERE run_id=? AND sequence>0 AND sequence<?
                        AND sequence<>? ORDER BY sequence LIMIT ?)""", (run_id, run_id, sequence, marker, max_events)).rowcount
                    result["deleted"] += deleted
                    remaining = db.execute("""SELECT 1 FROM events WHERE run_id=? AND sequence>0
                        AND sequence<? AND sequence<>? LIMIT 1""", (run_id, sequence, marker)).fetchone()
                    if not remaining:
                        db.execute("DELETE FROM history_pending WHERE run_id=?", (run_id,))
                else:
                    # A concurrent review changed the snapshot. Re-read next run.
                    db.execute("UPDATE history_pending SET due=? WHERE run_id=?", (now+1, run_id))
                result["processed"] += 1
    except LedgerUnavailable:
        # Maintenance yields to gameplay. API transactions map the same error
        # to 503; neither path revokes a run nor retries an external publication.
        result["busy"] = True
    return result
