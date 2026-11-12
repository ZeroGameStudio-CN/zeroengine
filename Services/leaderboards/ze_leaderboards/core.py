"""Durable run delivery kernel. Game rules and verified identity/publication are adapters.

One deployment/ledger/signing key per game. No shared multi-tenant database.
"""
import hashlib
import hmac
import json
import math
import re
import secrets
import time

from .common import (Rejected, AuthenticationRejected, ProviderUnavailable, WriteNotSent,
                     canonical, digest, text, integer, fields)
from .ledger import SQLiteLedger

NATIVE_ROOM_FLOW_VERSION = 2

class RunService:
    provider_unavailable_code = "provider_unavailable"
    write_not_sent_reason = "publisher_request_not_sent"

    def __init__(self, db_path, signing_key, config, publisher, clock=time.time,
                 *, recover_interrupted=True, ledger=None, authenticator=None):
        if len(signing_key) < 32:
            raise ValueError("Run signing key must have at least 256 bits")
        self.db_path, self._key, self.publisher, self.clock = db_path, signing_key, publisher, clock
        self.authenticator = authenticator
        self.policies = self.validate_policies(config)
        self.enabled = config.get("enabled") is True
        self.auto_compatible = config.get("autoCompatible") is True
        self._next_history_prune = 0
        self.ledger = ledger if ledger is not None else SQLiteLedger(db_path)
        self.ledger.initialize(recover_interrupted)

    @staticmethod
    def validate_policies(config):
        raise NotImplementedError("The game adapter must validate its policies.")

    def _auto_template(self, board):
        return None

    def _prepare_template(self, policy, board):
        return policy

    def _is_public_test(self, policy):
        return False

    @staticmethod
    def _validate_ticket(ticket):
        return text(ticket, 8192)

    @staticmethod
    def _normalize_event(body):
        return dict(body)

    def _apply_progress(self, db, row, body, policy):
        raise NotImplementedError("The game adapter must validate progress and derive scores.")

    def _db(self, lock_key=None):
        return self.ledger.transaction(lock_key)

    def _token(self, run_id: str) -> str:
        return hmac.new(self._key, run_id.encode("ascii"), hashlib.sha256).hexdigest()

    def rate_limit(self, identity: str, limit: int, seconds: int = 60):
        now = int(self.clock())
        bucket, period = digest(identity), now // seconds * seconds
        with self._db("limit:" + bucket) as db:
            db.execute("DELETE FROM limits WHERE period < ?", (now - 86400,))
            row = db.execute("SELECT count FROM limits WHERE bucket=? AND period=?", (bucket, period)).fetchone()
            if row and row[0] >= limit:
                raise Rejected("rate_limited", 429)
            db.execute("INSERT INTO limits VALUES(?,?,1) ON CONFLICT(bucket,period) DO UPDATE SET count=limits.count+1",
                       (bucket, period))

    def _result(self, row, include_token=False) -> dict:
        policy = json.loads(row["policy"])
        result = {"runId": row["id"], "sequence": row["sequence"], "nonce": row["nonce"],
                  "state": row["state"], "score": row["score"] or 0,
                  "targetBoard": policy["targetBoard"], "expiresAt": row["expires"], "evidenceVersion": 1,
                  "roomKinds": 1 if policy.get("_autoCompatible", self.auto_compatible) else 0, "batchVersion": 1}
        if include_token:
            result["token"] = self._token(row["id"])
        return result

    def start(self, body: dict, *, room_flow_version: int = 0) -> dict:
        required = {"clientRunId", "board", "build", "rules", "content", "seed", "ticket"}
        if not isinstance(body, dict) or set(body) not in (required, required | {"startedAt"}):
            raise Rejected("invalid_fields")
        room_flow = integer(room_flow_version, 0)
        if room_flow not in (0, NATIVE_ROOM_FLOW_VERSION):
            raise Rejected("unsupported_room_flow")
        if not self.enabled:
            raise Rejected("ranked_unavailable", 503)
        client_id = text(body["clientRunId"], 32)
        if not re.fullmatch(r"[0-9a-f]{32}", client_id):
            raise Rejected("invalid_run_id")
        board_name = text(body["board"])
        policy = self.policies.get(board_name)
        template_pair = self._auto_template(board_name) if (not policy and self.auto_compatible) else None
        if not policy and not template_pair:
            raise Rejected("board_unavailable", 403)
        if template_pair:
            template_pair = (self._prepare_template(template_pair[0], board_name), template_pair[1])
        for field in ("build", "rules", "content"):
            text(body[field], 128)
        seed = text(body["seed"], 64)
        now = self.clock()
        if policy and not self.auto_compatible:
            # A display version is client-reported, not an authenticity proof. For
            # explicitly approved test cohorts, permit version-only rebuilds while
            # retaining the exact rules/content pair and every identity/run guard.
            compatible = {"rules": body["rules"], "content": body["content"]} in policy.get("compatibleBuildCohorts", [])
            for field, allowlist in (("build", "builds"), ("rules", "rules"), ("content", "content")):
                if field == "build" and compatible:
                    continue
                if body[field] not in policy[allowlist]:
                    raise Rejected("unsupported_" + field, 403)
        if "startedAt" in body:
            integer(body["startedAt"], 1)
        start_hash = digest({k: v for k, v in body.items() if k != "ticket"})
        ticket = self._validate_ticket(body["ticket"])
        try:
            steam_id = self.authenticator(ticket) if self.authenticator is not None else self.publisher.authenticate(ticket)
        except AuthenticationRejected:
            raise Rejected("invalid_ticket", 401) from None
        except ProviderUnavailable:
            raise Rejected(self.provider_unavailable_code, 503) from None
        check_policy = policy if policy else template_pair[0]
        public_test = self._is_public_test(check_policy)
        if check_policy.get("testAccounts") and not public_test and steam_id not in check_policy["testAccounts"]:
            raise Rejected("canary_account_required", 403)
        self.rate_limit("account-start:" + steam_id, 30, 60)
        # A lost start response may be retried after the weekly boundary. Return
        # the same frozen run; time/seed admission below applies only to new runs.
        with self._db("start:" + steam_id) as db:
            existing = db.execute("SELECT * FROM runs WHERE steam_id=? AND client_id=?", (steam_id, client_id)).fetchone()
            if existing:
                if existing["start_hash"] != start_hash:
                    raise Rejected("idempotency_conflict", 409)
                return self._result(existing, include_token=True)
        cycle_policy = policy if policy else template_pair[0]
        if cycle_policy.get("seeds") and seed not in cycle_policy["seeds"]:
            raise Rejected("seed_unavailable", 403)
        if now < cycle_policy.get("startsAt", 0) or now >= cycle_policy.get("endsAt", math.inf):
            raise Rejected("cycle_unavailable", 403)
        if not policy:
            template, target_board = template_pair
            try:
                created = self.publisher.find_or_create_board(target_board, template["mode"])
            except ProviderUnavailable:
                raise Rejected(self.provider_unavailable_code, 503) from None
            policy = dict(template, board=board_name, targetBoard=target_board, boardId=int(created["id"]),
                          builds=[body["build"]], rules=[body["rules"]], content=[body["content"]])
            self.policies[board_name] = policy
        ticket_hash = hashlib.sha256(ticket.lower().encode()).hexdigest()
        with self._db("start:" + steam_id) as db:
            existing = db.execute("SELECT * FROM runs WHERE steam_id=? AND client_id=?", (steam_id, client_id)).fetchone()
            if existing:
                if existing["start_hash"] != start_hash:
                    raise Rejected("idempotency_conflict", 409)
                return self._result(existing, include_token=True)
            if db.execute("SELECT 1 FROM runs WHERE ticket_hash=?", (ticket_hash,)).fetchone():
                raise Rejected("ticket_reused", 409)
            # Another start must not cancel an earlier offline finish waiting for delivery.
            # Each independently authenticated run has its own expiry, nonce chain and terminal state.
            run_id, nonce = secrets.token_hex(16), secrets.token_hex(16)
            started = now
            if "startedAt" in body:
                claimed_start = integer(body["startedAt"], 1)
                started = min(now, max(now - policy["maxAgeSeconds"], claimed_start))
            policy = self._freeze_policy(policy, room_flow)
            expires = now + policy["maxAgeSeconds"]
            db.execute("INSERT INTO runs VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)", (
                run_id, steam_id, client_id, start_hash, ticket_hash, canonical(policy), started, expires,
                "active", 0, nonce, 0, "", 0, 0, None, None,
            ))
            row = db.execute("SELECT * FROM runs WHERE id=?", (run_id,)).fetchone()
            # Reserve sequence zero for reviewable start claims, excluding the authentication ticket/token.
            db.execute("INSERT INTO events VALUES(?,?,?,?)", (run_id, 0, start_hash, canonical({
                "request": {k: v for k, v in body.items() if k != "ticket"},
                "receivedAt": now, "receipt": self._result(row),
            })))
            return self._result(row, True)

    def _authorized(self, db, run_id: str, token: str):
        if not re.fullmatch(r"[0-9a-f]{32}", run_id) or not re.fullmatch(r"[0-9a-f]{64}", token or ""):
            raise Rejected("invalid_receipt", 401)
        if not hmac.compare_digest(self._token(run_id), token):
            raise Rejected("invalid_receipt", 401)
        row = db.execute("SELECT * FROM runs WHERE id=?", (run_id,)).fetchone()
        if not row:
            raise Rejected("invalid_receipt", 401)
        row = self._recover_stale_write(db, row)
        return row

    def _recover_stale_write(self, db, row):
        if self.ledger.shared and row["state"] == "writing":
            reason = row["reason"] or ""
            try:
                started = float(reason.removeprefix("dispatch_started:"))
            except ValueError:
                started = -math.inf
            # Publisher calls are bounded. A much longer hold
            # becomes unknown, never a lease that permits redispatch.
            if self.clock() - started >= 120:
                db.execute("UPDATE runs SET state='unknown',reason='interrupted_write' WHERE id=? AND state='writing' AND reason IS NOT DISTINCT FROM ?",
                           (row["id"], row["reason"]))
                row = db.execute("SELECT * FROM runs WHERE id=?", (row["id"],)).fetchone()
        return row

    def status(self, run_id: str, token: str) -> dict:
        with self._db("run:" + run_id) as db:
            result = self._result(self._authorized(db, run_id, token))
        return self.publish(run_id) if result["state"] == "ready" else result

    def event(self, run_id: str, token: str, body: dict) -> dict:
        if not self.enabled:
            raise Rejected("ranked_unavailable", 503)
        result = self._record_event(run_id, token, body)
        return self.publish(run_id) if result["state"] == "ready" else result

    MAX_BATCH_EVENTS = 16
    MAX_BATCH_BYTES = 16 * 1024

    def batch(self, run_id: str, token: str, body: dict) -> dict:
        if not self.enabled:
            raise Rejected("ranked_unavailable", 503)
        if not isinstance(body, dict) or set(body) != {"sequence", "nonce", "events"}:
            raise Rejected("invalid_fields")
        first = integer(body["sequence"], 1)
        nonce = text(body["nonce"], 32)
        events = body["events"]
        if not isinstance(events, list) or not 1 <= len(events) <= self.MAX_BATCH_EVENTS:
            raise Rejected("invalid_batch")
        if len(json.dumps(body, ensure_ascii=False, separators=(",", ":"), allow_nan=False).encode("utf-8")) > self.MAX_BATCH_BYTES:
            raise Rejected("body_too_large", 413)
        essential = []
        for i, event in enumerate(events):
            required = {"kind", "roomIndex", "room", "battleMs"}
            if not isinstance(event, dict) or not required <= set(event) or set(event) - required - {"roomKind", "evidence"}:
                raise Rejected("invalid_fields")
            if event["kind"] in {"finish", "revoke"} and i != len(events) - 1:
                raise Rejected("invalid_batch")
            # Observations retain the same first-acceptance semantics as v1 retries.
            normalized = self._normalize_event(event)
            essential.append({k: v for k, v in normalized.items() if k != "evidence"})
        identity = digest({"sequence": first, "nonce": nonce, "events": essential})
        with self._db("run:" + run_id) as db:
            row = self._authorized(db, run_id, token)
            saved = db.execute("SELECT response FROM events WHERE run_id=? AND sequence=?", (run_id, first)).fetchone()
            if saved:
                marker = json.loads(saved[0]).get("batch", {})
                if marker.get("hash") != identity:
                    raise Rejected("idempotency_conflict", 409)
                result = self._result(row) if events[-1]["kind"] in {"finish", "revoke"} else marker["receipt"]
            else:
                if first != row["sequence"] + 1 or nonce != row["nonce"]:
                    raise Rejected("sequence_mismatch", 409)
                for event in events:
                    result = self._record_event_locked(db, row, dict(event, sequence=row["sequence"] + 1, nonce=row["nonce"]))
                    row = db.execute("SELECT * FROM runs WHERE id=?", (run_id,)).fetchone()
                envelope = json.loads(db.execute("SELECT response FROM events WHERE run_id=? AND sequence=?", (run_id, first)).fetchone()[0])
                envelope["batch"] = {"hash": identity, "receipt": result}
                db.execute("UPDATE events SET response=? WHERE run_id=? AND sequence=?", (canonical(envelope), run_id, first))
        return self.publish(run_id) if result["state"] == "ready" else result

    def _record_event(self, run_id: str, token: str, body: dict) -> dict:
        with self._db("run:" + run_id) as db:
            return self._record_event_locked(db, self._authorized(db, run_id, token), body)

    def _record_event_locked(self, db, row, body: dict) -> dict:
        run_id = row["id"]
        expected = {"sequence", "nonce", "kind", "roomIndex", "room", "battleMs"}
        if not isinstance(body, dict) or not expected <= set(body) or set(body) - expected - {"evidence", "roomKind"}:
            raise Rejected("invalid_fields")
        body = self._normalize_event(body)
        sequence = integer(body["sequence"], 1)
        text(body["nonce"], 32)
        kind, room = text(body["kind"], 16), text(body["room"])
        room_index, battle_ms = integer(body["roomIndex"], 1), integer(body["battleMs"])
        # Optional observations are frozen on first acceptance. Their numeric
        # formatting/availability must not invalidate a legitimate progress retry.
        event_hash = digest({key: value for key, value in body.items() if key != "evidence"})
        if kind not in {"enter", "clear", "finish", "revoke"}:
            raise Rejected("invalid_event")
        previous = db.execute("SELECT hash,response FROM events WHERE run_id=? AND sequence=?", (run_id, sequence)).fetchone()
        if previous:
            saved = json.loads(previous["response"])
            original = saved.get("request")
            # Older receivers included observations in the stored hash.
            original_hash = digest({key: value for key, value in original.items() if key != "evidence"}) if original else previous["hash"]
            if original_hash != event_hash:
                raise Rejected("idempotency_conflict", 409)
            # Return the original nonce for replayed progress; finish exposes the current write outcome.
            return self._result(row) if kind == "finish" else saved.get("receipt", saved)
        if row["state"] != "active":
            raise Rejected("run_closed", 409)
        if self.clock() >= row["expires"]:
            raise Rejected("run_expired", 403)
        if sequence != row["sequence"] + 1 or body["nonce"] != row["nonce"]:
            raise Rejected("sequence_mismatch", 409)
        policy = json.loads(row["policy"])
        cleared, state, score, policy, assessment = self._apply_progress(db, row, body, policy)
        nonce = secrets.token_hex(16)
        db.execute("UPDATE runs SET state=?,sequence=?,nonce=?,room_index=?,room=?,cleared=?,battle_ms=?,score=?,policy=? WHERE id=?",
                   (state, sequence, nonce, room_index, room, cleared, battle_ms, score, canonical(policy), run_id))
        result = self._result(db.execute("SELECT * FROM runs WHERE id=?", (run_id,)).fetchone())
        db.execute("INSERT INTO events VALUES(?,?,?,?)", (run_id, sequence, event_hash, canonical({
            "request": body, "receivedAt": self.clock(), "receipt": result,
            "assessment": assessment,
        })))
        return result

    @staticmethod
    def _history(db, run_id):
        # Endless runs may contain thousands of observations. Stream the ledger
        # and keep only a bounded decision summary, never the entire JSON history.
        result = {"start": {}, "cleared": set(), "findings": [], "findingCount": 0}
        for row in db.execute("SELECT sequence,response FROM events WHERE run_id=? AND sequence>=0 ORDER BY sequence", (run_id,)):
            envelope = json.loads(row[1])
            request = envelope.get("request", {})
            if row[0] == 0:
                result["start"] = request
            if request.get("kind") == "clear":
                result["cleared"].add(request.get("room"))
            findings = envelope.get("assessment", {}).get("findings", [])
            result["findingCount"] += len(findings)
            result["findings"].extend(findings[:max(0, 128 - len(result["findings"]))])
        return result

    def review_report(self, run_id=None, limit=100):
        """Private operator use only; no HTTP admin endpoint or auth secrets."""
        integer(limit, 1, 100)
        with self._db() as db:
            rows = db.execute("SELECT * FROM runs WHERE id=?" if run_id else
                              "SELECT * FROM runs WHERE state='review' ORDER BY started LIMIT ?",
                              (run_id,) if run_id else (limit,)).fetchall()
            result = []
            for row in rows:
                last = db.execute("SELECT response FROM events WHERE run_id=? AND sequence>=0 ORDER BY sequence DESC LIMIT 1", (row["id"],)).fetchone()
                summary = json.loads(last[0]).get("assessment", {}).get("summary") if last else None
                if summary is None:
                    history = self._history(db, row["id"])
                    summary = {key: history[key] for key in ("findings", "findingCount")}
                progress = []
                if run_id:
                    progress = [json.loads(r[0]).get("request", {}) for r in db.execute(
                        "SELECT response FROM events WHERE run_id=? AND sequence>=0 ORDER BY sequence DESC LIMIT 100", (row["id"],))]
                    progress.reverse()
                event_count = db.execute("SELECT count(*) FROM events WHERE run_id=? AND sequence>=0", (row["id"],)).fetchone()[0]
                result.append({"runId": row["id"], "account": "***" + row["steam_id"][-4:],
                               "state": row["state"], "score": row["score"],
                               "targetBoard": json.loads(row["policy"])["targetBoard"],
                               "findings": summary["findings"], "findingCount": summary["findingCount"],
                               "findingsTruncated": summary["findingCount"] > len(summary["findings"]),
                               "progress": progress, "eventCount": event_count,
                               "progressTruncated": event_count > len(progress),
                               "decisions": [json.loads(r[0]) for r in db.execute(
                                   "SELECT response FROM events WHERE run_id=? AND sequence<0 ORDER BY sequence DESC", (row["id"],))]})
            return result

    def decide_review(self, run_id, approve, actor, reason):
        """Explicit audited operator decision; never called by player routes."""
        if not self.enabled:
            raise Rejected("ranked_unavailable", 503)
        if type(approve) is not bool:
            raise Rejected("invalid_decision")
        for value, maximum in ((actor, 80), (reason, 500)):
            if not isinstance(value, str) or not value.strip() or len(value) > maximum or any(ord(c) < 32 for c in value):
                raise Rejected("review_reason_required")
        decision = {"approve": approve, "actor": actor, "reason": reason}
        with self._db("run:" + run_id) as db:
            row = db.execute("SELECT * FROM runs WHERE id=?", (run_id,)).fetchone()
            if not row:
                raise Rejected("run_not_found", 404)
            policy = json.loads(row["policy"])
            current = self.policies.get(policy["board"])
            if approve and row["state"] in {"review", "ready"} and (
                    not current or not current.get("publish", False) or current["boardId"] != policy["boardId"] or
                    current["targetBoard"] != policy["targetBoard"]):
                raise Rejected("publication_policy_changed", 409)
            previous = db.execute("SELECT hash FROM events WHERE run_id=? AND sequence=-1", (run_id,)).fetchone()
            if previous:
                if previous[0] != digest(decision):
                    raise Rejected("review_decision_conflict", 409)
            else:
                if row["state"] != "review":
                    raise Rejected("run_not_in_review", 409)
                state = "ready" if approve else "not_published"
                db.execute("UPDATE runs SET state=?,reason=? WHERE id=?", (state, "operator_review", run_id))
                db.execute("INSERT INTO events VALUES(?,?,?,?)", (run_id, -1, digest(decision), canonical({
                    "decision": decision, "receivedAt": self.clock(), "state": state})))
            result = self._result(db.execute("SELECT * FROM runs WHERE id=?", (run_id,)).fetchone())
        # Review expiry never destroys a legitimate held result. Unknown/writing
        # remains terminal for retries; publish() only dispatches known ready.
        return self.publish(run_id) if result["state"] == "ready" else result

    def publish(self, run_id: str) -> dict:
        if not self.enabled:
            raise Rejected("ranked_unavailable", 503)
        with self._db("run:" + run_id) as db:
            row = db.execute("SELECT * FROM runs WHERE id=?", (run_id,)).fetchone()
            row = self._recover_stale_write(db, row)
            if row["state"] != "ready":
                return self._result(row)
            claim = "dispatch_started:" + str(self.clock()) if self.ledger.shared else None
            db.execute("UPDATE runs SET state='writing',reason=? WHERE id=?", (claim, run_id))
            policy, steam_id, score = json.loads(row["policy"]), row["steam_id"], row["score"]
        try:
            self.publisher.set_score(policy, steam_id, score)
            state, reason = "published", None
        except WriteNotSent:
            state, reason = "ready", self.write_not_sent_reason
        except Exception:
            # No key, ticket, token, response body or exception URL is logged.
            state, reason = "unknown", "publisher_outcome_unconfirmed"
        with self._db("run:" + run_id) as db:
            db.execute("UPDATE runs SET state=?,reason=? WHERE id=? AND state='writing'", (state, reason, run_id))
            return self._result(db.execute("SELECT * FROM runs WHERE id=?", (run_id,)).fetchone())

    def prune_history(self):
        """Bounded housekeeping; keep identities and start/final replay receipts."""
        now = self.clock()
        if now < self._next_history_prune:
            return
        self._next_history_prune = now + 600
        # Expiry bounds every late event. Keep start, final and the last batch
        # marker so a lost final-batch response still replays its receipt.
        marker = '%"batch":%'
        last_marker = """COALESCE((SELECT MAX(m.sequence) FROM events m WHERE m.run_id=r.id
            AND m.sequence>0 AND m.response LIKE ?), 0)"""
        with self._db("history-prune") as db:
            rows = db.execute(f"""SELECT r.id,r.sequence,{last_marker} FROM runs r
                WHERE r.expires<? AND r.state IN ('published','revoked','active') AND EXISTS
                (SELECT 1 FROM events e WHERE e.run_id=r.id AND e.sequence>0 AND e.sequence<r.sequence
                 AND e.sequence<>{last_marker})
                ORDER BY r.started LIMIT 256""", (marker, now - 3 * 86400, marker)).fetchall()
            for row in rows:
                db.execute("DELETE FROM events WHERE run_id=? AND sequence>0 AND sequence<? AND sequence<>?",
                           (row[0], row[1], row[2]))

    def _freeze_policy(self, policy, room_flow):
        return dict(policy, _autoCompatible=self.auto_compatible, _roomFlowVersion=room_flow)
