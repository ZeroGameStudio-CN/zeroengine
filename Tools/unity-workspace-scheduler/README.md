# Unity Workspace Scheduler

`unity-scheduler` is the Router's private machine-local control plane for Unity workspaces. It coordinates task lifetime, write paths, named resources, FIFO queues, cooperative maintenance draining, exclusive freezes, urgent maintenance barriers, and fail-closed recovery.

It does not start or inspect Unity, run tests, call MCP, inspect version control, or install anything into a Unity project. The package has no runtime dependencies. The Router uses only the command's JSON process protocol; it does not import the Python package or depend on its installation layout.

This README is Router-private protocol and maintainer documentation, not an
Agent entrypoint. Normal Agent and Unity registration, task, claim, queue,
freeze, recovery, and live work must use `unity-workspace-router`. The sole
direct-call exception is an explicitly authorized offline state
inspect/backup/migrate/verify/restore runbook after Router admission has been
stopped; do not extend that exception to ordinary scheduling.

## Maintainer installation

Perform the canonical install and version read-back only inside the authorized
stopped-admission maintenance runbook.

```powershell
uv tool install "unity-workspace-scheduler @ git+https://github.com/ZeroGameStudio-CN/zeroengine.git@<tested-commit>#subdirectory=Tools/unity-workspace-scheduler"
unity-scheduler --version
```

## Router-private protocol example

The following illustrates the private protocol for review. It is not an Agent
or maintainer operational workflow and must not be executed instead of the
Router.

```powershell
$workspace = "D:\unity\projects\Game"
$tokenFile = "<absolute-token-path-created-under-the-Router-private-token-directory>"
$registerOperation = "<fresh-canonical-lowercase-uuidv4>"
$startOperation = "<fresh-canonical-lowercase-uuidv4>"
$claimOperation = "<fresh-canonical-lowercase-uuidv4>"
$releaseOperation = "<fresh-canonical-lowercase-uuidv4>"

unity-scheduler workspace register --workspace $workspace --operation-id $registerOperation
unity-scheduler task start --workspace $workspace --owner task-label --summary "Targeted Unity work" --token-file $tokenFile --operation-id $startOperation
unity-scheduler claim acquire --workspace $workspace --resource unity-live --write Assets/Scripts --token-file $tokenFile --operation-id $claimOperation

# The caller independently runs the selected Unity executor here.

unity-scheduler task release --workspace $workspace --result completed --token-file $tokenFile --operation-id $releaseOperation
unity-scheduler receipt ack --operation-id $releaseOperation --fingerprint <fingerprint-from-release-result> --delivery-digest <delivery-digest-from-release-result>
```

Every command except `--help` and `--version` returns one JSON envelope with fixed
`protocol_version=3`, `ok`, `code`, `message`, `duration_ms`, and either `result` or `details`.
Task secrets are written only to an exclusive owner token file and are never returned in JSON.
On Windows the token file must be under the current user's temporary directory in a private caller
parent. Scheduler never mutates its DACL: it validates current-user ownership and an allowlist of
the current user, SYSTEM, Administrators, and owner-bound OWNER RIGHTS through the opened handle on
creation, every read, and terminal removal. Tasks running as the same OS user are not strongly
isolated from one another and still depend on keeping the token secret.

Every public mutation requires a fresh canonical lowercase UUIDv4 `--operation-id`. Scheduler
persists the mutation and its receipt in one SQLite transaction. A retry with the same canonical
request returns the same entity/result with `operation.replayed=true`; any changed workspace,
action, parameter, or owner token fails with `operation-id-conflict`. `--receipt-only` checks for
that exact receipt before Router owner preflight: a missing receipt returns
`operation-receipt-missing` without maintenance, while a wait operation that has committed but not
finalized returns `operation-in-progress` and cannot be acknowledged. Successful mutation results
include `operation_id`, `fingerprint`, `delivery_digest`, `replayed`, `delivered`, and `finalized`.
The digest binds the durable result and its current terminal proof; ACK compares it in the delivery
transaction so a proof change between flush and ACK fails closed.

For `task park`, `claim acquire`, and `freeze acquire`, Router passes the immutable original wait
as `--requested-wait` and the post-lock remaining budget as `--wait`; effective wait must not exceed
requested wait. A pending receipt owns the first claim/freeze IDs and can only finalize those IDs,
with an absolute deadline based on the first receipt creation time. This prevents retry from
extending a queue wait or parking against a different freeze.

On a normal `completed` or `failed` release, Scheduler retains the exact authenticated token file
until Router has flushed the mutation result and calls `receipt ack`. Ack first durably marks the
final receipt delivered, then opens the receipt-bound canonical path once, validates its handle/ACL
and token hash, and deletes through that same Windows handle. A missing file is treated as already
deleted so a crash after handle deletion remains retryable. After such deletion,
`task release --receipt-only` has one narrow tokenless path: it returns only an existing finalized
completed/failed receipt whose operation ID, workspace, action, complete canonical parameters, and
cleanup path all match. It never maintains or mutates state. Interrupted cleanup is protected from
receipt GC. After a completed/failed release ACK has also confirmed token deletion, its flushed
terminal result causally retires older finalized, unacknowledged `task start` and `task heartbeat`
receipts for that exact task; a failed cleanup leaves them durable. An `outcome_unknown` release
never removes its token. Token mtime or age is never sufficient evidence.

Version 1.4 uses schema 3. It atomically migrates safe schema-1 or validated schema-2 state on first open, assigns queue
orders from a per-workspace counter, indexes token authentication, and retains the newest 1000
terminal tasks per workspace. Because scheduler 1.2 did not preserve enough restoration lineage,
schema 1 with queued or parked claims is rejected instead of guessed; active claims and
`outcome_unknown` fences remain migration-compatible. Active, queued, parked, and unknown state
is never removed by history retention. Migration bounds legacy active/unknown task expiry to at
most 86400 seconds after migration and normalizes non-finite open-task heartbeat metadata. New
task TTL is finite, greater than zero, and at most 86400 seconds; waits are finite, non-negative,
and at most 86400 seconds. Migration refusal occurs before WAL is enabled and leaves the schema-1
database bytes and journal mode unchanged; a WAL switch failure after commit is safely retryable
from the already valid schema-3 state. Schema 3 adds the durable operation receipt ledger. Pending
and cleanup-pending receipts are never history-pruned. Finalized lifecycle receipts may be safely
retired once the task's durable terminal fence proves they can no longer authorize work;
task-start history with a physical token remains protected until exact token cleanup completes.
Recovery appends a separate terminal resolution proof without rewriting the historical result.
Retired receipts are bounded history rather than replay-required work.

A heartbeat without `--ttl` preserves that task's configured lease duration; an explicit `--ttl`
replaces it. Named resources conflict even between claims from the same task, so one owner cannot
accidentally dispatch two concurrent executors against `unity-live`. Non-conflicting resources and
paths remain independently schedulable.

Within one workspace, an open task token hash is unique. Authentication selects
an open task before any historical terminal task and falls back to history only
when no open match exists; multiple open matches are corrupt state and fail
closed. Offline `state verify` checks the same uniqueness invariant.
`task identify --workspace <root> --token-file <path>` performs only that secure open-task
identity lookup. It does not maintain state, renew TTL, schedule claims, or change workspace epoch;
Router uses its task ID only to take the project+task OS lock before lock-internal heartbeat and
exact claim assertion.

A claimless task may close automatically after TTL expiry. If any active claim
exists, expiry preserves it under `outcome_unknown`; that fence may block until
deterministic recovery, with no unconditional timed release guarantee.
In the same expiry transaction, Scheduler finalizes every pending acquire/freeze/park receipt for
that task as `aborted=true` with an explicit TTL-expiry reason and the fixed original identities.
Retries and acknowledgement therefore converge instead of leaving an operation permanently pending;
an active claim still remains unauthorized and fenced as `outcome_unknown`.

Offline `state backup` and `state restore` use SQLite snapshot operations so committed WAL pages are
included. `state verify` instead inspects the supplied main database and any already-present
sidecars read-only, then proves their identity, bytes, and sidecar set did not change. The commands
validate integrity, schema, foreign keys, queue counters, and open claim counts. Backup and restore
require an explicit zero-process attestation; restore never overwrites a non-empty state database.
On POSIX, existing caller-selected token and maintenance parents keep their mode; missing parents
are created as `0700`, and token/staging/final database files use `0600`. See the setup guide before
using these operator commands.
The setup restore-recovery runbook owns the fixed restore quarantine and the structured
`recovery_required`, `publication_uncertain`, `cleanup_pending`, and
`durability_pending_parent` outcomes; preserve the reported evidence and never blindly retry an
interrupted restore. `cleanup_pending` contains only exact task-created artifacts. A
`durability_pending_parent` entry is only a parent directory to re-flush and must never be deleted.

`queue cancel` cancels only an exact queued claim owned by the token's task. It
never releases an active or parked claim; use `claim release` only after the
owned operation reaches a known terminal result.

For Router `start-editor` and `cli-test`, queue grant is not dispatch authority
by itself. After `unity-live` grant, Router must heartbeat the same task with
enough TTL for the remaining bounded operation, assert the exact claim, and
re-resolve the exact project, route, executor, and Unity/CLI versions. It must
build and dispatch only from that fresh result, never from the pre-wait command.

When a queued freeze requests workspace maintenance, an owner at a safe point can run
`task park`. Its path-only claims stop authorizing writes, the freeze proceeds, and the same
claim IDs automatically return to the FIFO queue when the freeze closes. Resource and freeze
claims are never parked or stolen. Heartbeat reports exact `restoration_pending_claim_ids`,
including parked claims and restored claims still queued behind another freeze; a non-empty list
means the caller must keep waiting.

While a task has a drain request or any restoration-pending claim, Scheduler
rejects every new claim or freeze for that task. The owner must finish and
release resource work, park when instructed, or wait for restoration instead
of adding ownership that can self-deadlock the drain or starve the queue.

An explicitly urgent maintenance operation can request
`freeze acquire --priority urgent`. It passes queued normal work, remains FIFO with other urgent
freezes, and never preempts active work or an unknown-outcome fence.

See [Setup and protocol](docs/setup.md) for the complete command contract and recovery rules.
