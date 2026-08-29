# Setup and protocol

## Boundary

The scheduler owns only machine-local coordination state. It has no daemon, network listener, Unity package, Editor integration, test farm, executor adapter, MCP client/server, or VCS adapter. Unity projects never depend on it. The Router invokes `unity-scheduler` as a private external process and parses its public JSON fields.

This is Router-private protocol and maintainer documentation, not a normal
Agent entrypoint. Every Agent/Unity registration, task, claim, queue, freeze,
recovery, and live operation goes through `unity-workspace-router`. Direct
Scheduler invocation is limited to an explicitly authorized offline state
inspect/backup/migrate/verify/restore runbook after Router admission is stopped;
all other examples below describe the private protocol rather than an Agent
workflow.

State defaults to `%LOCALAPPDATA%\UnityWorkspaceScheduler` on Windows and `$XDG_STATE_HOME/unity-workspace-scheduler` or `~/.local/state/unity-workspace-scheduler` on POSIX. `UNITY_SCHEDULER_STATE_DIR` and the global `--state-dir` option can select an isolated state root. Every JSON response has `protocol_version=3`; callers must reject missing or different protocol values.
Scheduler-created directories inherit the already controlled parent ACL on Windows. Windows task
token files must be under the current user's temporary directory and inside a caller-controlled
private parent; the Scheduler never mutates that DACL. Creation, every read, and terminal removal
inspect the DACL through the already opened file handle. The owner must be the current user, every
allow entry must name that user, SYSTEM, or Administrators, and OWNER RIGHTS is accepted only when
the owner is proven to be the current user. Maintenance ACL probes use the absolute `icacls.exe`
returned by the Windows System32 API, never `PATH`; maintenance parents and files still fail closed
when their ACL contains a broad principal. POSIX state,
token-parent, backup-parent, and restore-parent directories that the Scheduler newly creates use
`0700`; the dedicated state root is always enforced as `0700`. An existing caller-selected token or
maintenance parent keeps its mode only when the current effective user owns it and it has no group
or other write bit. Token, staging, backup, and restored database files are `0600`.

## Registration

```text
unity-scheduler workspace register --workspace <absolute-root> --operation-id <uuid-v4> [--receipt-only]
unity-scheduler workspace status --workspace <absolute-root>
unity-scheduler workspace list
unity-scheduler workspace unregister --workspace <absolute-root> --operation-id <uuid-v4> [--receipt-only]
```

Registration is machine-local and writes nothing into the workspace. Unregistered or moved paths fail closed. Unregister refuses any active/unknown task or open claim.

## Maintenance history

```text
unity-scheduler maintenance history --workspace <absolute-root> [--limit <1..100>]
```

This Router-private observation is a bounded read-only projection over retained task,
recovery-event, operation-receipt, and token-cleanup state. It does not run maintenance,
heartbeat a task, create a receipt, or expose token paths, token hashes, receipt parameters,
or owner credentials. The default limit is 20 and the maximum is 100.

## Tasks and claims

```text
unity-scheduler task start --workspace <root> --owner <label> --summary <text> --token-file <path> --operation-id <uuid-v4> [--receipt-only] [--ttl <seconds>]
unity-scheduler task identify --workspace <root> --token-file <path>
unity-scheduler task heartbeat --workspace <root> --token-file <path> --operation-id <uuid-v4> [--receipt-only] [--ttl <seconds>] [--note <text>]
unity-scheduler task park --workspace <root> --token-file <path> --operation-id <uuid-v4> [--receipt-only] [--wait <effective-seconds>] [--requested-wait <original-seconds>]
unity-scheduler claim acquire --workspace <root> --token-file <path> --operation-id <uuid-v4> [--receipt-only] [--write <path>]... [--resource <name>]... [--wait <effective-seconds>] [--requested-wait <original-seconds>] [--keep-queued]
unity-scheduler claim assert --workspace <root> --token-file <path> [--write <path>]... [--resource <name>]... [--freeze]
unity-scheduler claim release --workspace <root> --token-file <path> --claim-id <id> --operation-id <uuid-v4> [--receipt-only]
unity-scheduler queue cancel --workspace <root> --token-file <path> --claim-id <id> --operation-id <uuid-v4> [--receipt-only]
unity-scheduler freeze acquire --workspace <root> --token-file <path> --operation-id <uuid-v4> [--receipt-only] [--priority normal|urgent] [--wait <effective-seconds>] [--requested-wait <original-seconds>] [--keep-queued]
unity-scheduler task release --workspace <root> --token-file <path> --result completed|failed|outcome-unknown --operation-id <uuid-v4> [--receipt-only] [--note <text>]
unity-scheduler recovery resolve --workspace <root> --task-id <id> --resolution completed|failed --evidence <text> --operation-id <uuid-v4> [--receipt-only]
unity-scheduler receipt ack --operation-id <uuid-v4> --fingerprint <sha256> --delivery-digest <sha256>
```

Every public mutation requires a caller-generated canonical lowercase UUIDv4 operation ID. Its
final result includes `operation_id`, `fingerprint`, `delivery_digest`, `replayed`, `delivered`, and
`finalized`.
Router probes with the same request plus `--receipt-only` before owner preflight: an exact final
receipt is returned without maintenance, a miss returns `operation-receipt-missing`, and a
committed wait still in progress returns `operation-in-progress`. A reused ID with any changed
workspace, action, canonical parameter, or owner token returns `operation-id-conflict`.

For claim/freeze acquire and task park, `--requested-wait` is the immutable caller intent and
`--wait` is only the remaining effective budget after Router lock acquisition; effective must not
exceed requested. Pending receipts cannot be acknowledged. A normal same-ID retry resumes only the
stored claim/freeze IDs and cannot wait beyond the first receipt creation time plus requested wait.
After Router flushes a final mutation result, it acknowledges the exact ID, fingerprint, and
delivery digest. The digest binds the durable result and current terminal proof, closing the race
between reading a replay and acknowledging the flushed representation. Completed or failed task
release keeps the secure token until that ack opens it once, verifies the receipt-bound
hash, and deletes it through the same Windows handle. Missing means an earlier attempt already
deleted it. If ACK crashes after deletion, a tokenless `task release --receipt-only` succeeds only
for the existing finalized completed/failed receipt with the exact operation ID, workspace, action,
complete parameters, and cleanup path; it performs no maintenance or mutation. Cleanup failure is
retryable and preserves the receipt/path evidence. Outcome-unknown release keeps its token.
Pending and cleanup-pending receipts are never pruned. Finalized lifecycle receipts can be retired
without ACK only after a durable task-terminal fence proves they cannot authorize work. Start
receipts with a physical token remain protected until exact token cleanup completes. Recovery does
not rewrite an unresolved wait receipt's historical result; it appends a separate terminal
resolution proof, and the delivery digest binds the proof version Router actually flushed. Retired
receipts are bounded history and no longer block cleanup or empty-state restore.

`task identify` securely authenticates the token against one unique open task without maintenance,
TTL renewal, scheduling, claim changes, or epoch changes. Router uses the returned task ID only to
take its project+task lock; heartbeat and exact claim assertion still happen inside that lock.

Write paths are normalized inside the registered root; absolute paths outside it are rejected. Ancestor/descendant paths conflict, and an asset path conflicts with its `.meta` partner. Resource names are opaque workspace-local locks, including between claims owned by the same task; this prevents one owner from holding duplicate active `unity-live` claims. Conflicting claims queue FIFO; non-conflicting claims can run together. A queued normal freeze is a fair barrier and becomes exclusive after earlier owners leave.

Task TTL must be finite, greater than zero, and no more than 86400 seconds. Longer work must
heartbeat before expiry. Claim and park waits must be finite, non-negative, and no more than
86400 seconds; a timeout never authorizes work by itself.
Omitting `--ttl` from heartbeat preserves the task's configured lease duration; supplying it
explicitly replaces that duration after the same validation.
Task expiry finalizes that task's pending acquire, freeze, and park receipts in the same transaction
with `aborted=true`, their fixed original identities, and `reason=task-ttl-expired` or
`task-ttl-expired-with-active-claim`. Exact retry and receipt acknowledgement can then converge;
an active claim remains unauthorized behind the normal `outcome_unknown` recovery fence.

Within one workspace, an open task token hash is unique. Authentication selects
the open task before any historical terminal task and falls back to history
only when no open match exists. Multiple open matches indicate corrupt state
and fail closed; offline `state verify` enforces the same invariant.

Only a freeze may use `--priority urgent`. An urgent freeze passes all queued normal claims and
normal freezes, while urgent freezes remain FIFO by their original queue order. Priority never
preempts an active claim and never bypasses an unknown-outcome fence. Public claim JSON always
reports `priority` as `normal` or `urgent`.

While a task has a drain request or any restoration-pending claim, every new
claim or freeze for that task is rejected. The owner must finish and release
resource work, park when instructed, or wait for its original claims to restore
instead of adding ownership that can self-deadlock the drain or starve the
queue.

Version 1.4 upgrades safe schema 1 and validated schema 2 to schema 3 in one transaction. Migration preserves active claims
and `outcome_unknown` fences, bounds legacy open-task expiry to at most 86400 seconds after
migration, normalizes non-finite open-task heartbeat metadata, initializes each workspace's
`next_queue_order` from its current maximum, and adds bounded authentication and history indexes.
Scheduler 1.2 could lose the `parked_for` marker after restoring a parked claim to the queue, so
schema 1 containing any queued or parked claim is rejected before migration without changing the
database. Finish or cancel those claims with 1.2 while its owner context still exists; never infer
or hand-edit restoration lineage.
The schema/version gate and schema-1/2 migration run in the existing rollback journal before WAL is
enabled. A blocked migration therefore leaves both database bytes and journal mode unchanged. If
the schema transaction commits but the subsequent WAL switch fails, the database remains a valid
schema 3 database and a later 1.4 command safely retries the WAL switch. Schema 3 adds an empty
durable operation receipt ledger and never invents identities for legacy mutations.
After migration, 1.1, 1.2, and 1.3 binaries reject the state instead of scheduling it. A Router-managed
machine upgrades in one atomic maintenance window using the staged-candidate and canonical-install
order below. Never expose canonical Scheduler 1.4 to a Router that does not require protocol 3.
Schema 3 cannot be opened by older Scheduler versions; rollback means repairing or reinstalling
1.4.x unless a whole schema-1/2 backup can be restored before any schema-3 command writes new state.

## Offline state maintenance

Do not copy `scheduler.sqlite3` with a filesystem copy command: committed data may still live in
its WAL. Before directly invoking either Scheduler executable, stop admission at the Router entry,
wait for every Router call, scheduler process, and executor child to exit, and classify every
operation as a known terminal result or an explicit `outcome_unknown` fence. Confirm with the
operating system's process inventory that the count is zero. Then stage the tested Scheduler 1.4
executable at a separate absolute path. In the commands below,
`<absolute-staged-1.4-executable>` means that exact file, not `unity-scheduler` resolved through
`PATH` and not the still-canonical 1.2 binary. Build the candidate from the exact tested commit into
private, isolated uv tool and bin directories. For PowerShell:

```powershell
$stageRoot = "<absolute-private-staging-directory>"
$env:UV_TOOL_DIR = Join-Path $stageRoot "tools"
$env:UV_TOOL_BIN_DIR = Join-Path $stageRoot "bin"
uv tool install --force --no-cache "unity-workspace-scheduler @ git+https://github.com/ZeroGameStudio-CN/zeroengine.git@<tested-commit>#subdirectory=Tools/unity-workspace-scheduler"
$stagedScheduler = Join-Path $env:UV_TOOL_BIN_DIR "unity-scheduler.exe"
Remove-Item Env:UV_TOOL_DIR
Remove-Item Env:UV_TOOL_BIN_DIR
& $stagedScheduler --version
```

For POSIX shells:

```sh
stage_root='<absolute-private-staging-directory>'
UV_TOOL_DIR="$stage_root/tools" UV_TOOL_BIN_DIR="$stage_root/bin" \
  uv tool install --force --no-cache \
  'unity-workspace-scheduler @ git+https://github.com/ZeroGameStudio-CN/zeroengine.git@<tested-commit>#subdirectory=Tools/unity-workspace-scheduler'
staged_scheduler="$stage_root/bin/unity-scheduler"
"$staged_scheduler" --version
```

Keep `$stagedScheduler` or `$staged_scheduler` as the exact
`<absolute-staged-1.4-executable>` for every later staged command. The isolated uv environment
variables must not remain set during the canonical install; require the parsed version to equal
exactly `1.4.2`:

```text
<absolute-staged-1.4-executable> --version
```

Keep admission stopped and re-confirm zero processes after that check succeeds.
`--confirm-no-processes` is only the operator's attestation; the Scheduler does not discover or
terminate processes. Use the same staged absolute executable for the pre-install snapshot and
verification. Choose one absolute `<backup.sqlite3>` under a directory
controlled by the current user and not writable by a broad OS principal. The `--output`, ordinary
`--input`, and `--for-migration --input` arguments below must name that exact same file; do not
substitute another copy, relative path, or later snapshot between commands:

```text
<absolute-staged-1.4-executable> --state-dir <current-state-dir> state backup --output <backup.sqlite3> --confirm-no-processes
<absolute-staged-1.4-executable> state verify --input <backup.sqlite3>
<absolute-staged-1.4-executable> state verify --input <backup.sqlite3> --for-migration
```

These staged invocations are limited to `--version`, `state backup`, and `state verify`; they do
not open the database through the scheduling path or migrate schema 1 or 2. After the backup and
`--for-migration` verification succeed, install the canonical Router version that requires 1.4
first, so it fails closed while canonical Scheduler is still older. Then install canonical Scheduler
1.4, require canonical `unity-scheduler --version` to report version `1.4.2`, run the
`workspace list` maintenance read-back, exact-workspace status read-backs, and Router protocol
canary, and only then reopen Router admission.

`state backup` takes one paged, deadline-bounded SQLite snapshot, including committed WAL pages,
verifies the staged snapshot, and publishes it without overwriting an existing output. Persistent
SQLite `BUSY` or `LOCKED` state ends in a structured timeout instead of holding maintenance
indefinitely; restore uses the same snapshot primitive. The staging descriptor remains open through
write, inode verification, and `fsync`, and is never closed and reopened by path before
publication. `state verify` is read-only and checks SQLite integrity, foreign keys, supported schema
structure, schema-specific indexes and queue counters, task/claim/recovery/receipt counts, known task/claim states,
claim kinds and scopes, queue-order validity, task/workspace ownership, open-claim ownership,
same-workspace open-token hash uniqueness, normalized write/resource values, priority and parking
restoration markers, open claims, active-task clock anomalies, and schema-3 pending/finalized,
delivered/unacknowledged receipt invariants.
`--for-migration` accepts schema 1 or schema 2; schema 1 additionally requires zero queued and zero
parked claims, while schema 2 must pass its complete semantic validation. Preserve
the JSON report with the maintenance record. On POSIX, a safe existing backup output parent retains
its mode; a missing parent is created as `0700`, and both staging and published database files use
`0600`. Existing parents must be owned by the current effective user and have no group or other
write bit.

Keep Router admission stopped and re-confirm zero processes before restore:

```text
unity-scheduler --state-dir <target-state-dir> state restore --input <backup.sqlite3> --confirm-no-processes
unity-scheduler --state-dir <target-state-dir> state restore --input <backup.sqlite3> --confirm-no-processes --replace-empty
```

The first form requires `scheduler.sqlite3` and its WAL/SHM/journal sidecars not to exist. The
second form is the only overwrite path and accepts only an integrity-checked target with zero
tasks, zero claims, and zero recovery events; registered workspaces alone are allowed. A source
with open claims is refused unless the operator adds `--allow-open-claims` after reviewing the
reported claim states and recovery evidence. Restore stages and verifies a standalone snapshot in
the target directory. It then creates the target-specific adjacent directory
`.<database-name>.restore-quarantine` (normally
`<target-state-dir>/.scheduler.sqlite3.restore-quarantine`) as both the restore mutex and the
persistent custody location for the prior empty main database and its sidecars. The staged main is
published without overwrite, then the published snapshot and quarantined empty state are verified
again before custody is removed.

If restore returns `recovery_required=true`, preserve the reported target, `quarantine`, and
`staged` paths exactly; do not delete them or blindly retry. A pre-commit failure reports
`publication_uncertain=false`: no new snapshot was intentionally committed, but a concurrent path
change or partial custody move may still require inspection. A post-commit verification failure
reports `publication_uncertain=true`: the target may be the new snapshot, but that publication has
not been proven safe. Keep Router admission closed and reconfirm zero Scheduler/Router/executor
processes. Verify every database that exists, retain its sidecars with it, and compare the target
and staged SHA-256 plus the staged/target schema and core-count reports. Only when the target is
again verified, has no nonempty WAL or journal, and is proven to be the staged snapshot may the
exact reported staging and quarantine artifacts be removed. Otherwise preserve the suspect target
and staged files in a separate evidence directory first; if the quarantine main verifies as the
prior empty state, restore that main and its sidecars together with no-overwrite moves, then run
`state verify` before reopening admission. Escalate instead of choosing between ambiguous copies.

A successful command may return a nonempty `cleanup_pending` list after publication has already
been proven. Treat that as a success with an operator cleanup warning, not as permission to replay
restore: with admission still closed and zero processes reconfirmed, verify the published target
again, then remove only the exact listed task-created artifacts. `durability_pending_parent` is a
separate list of parent directories whose durability barrier must be retried; it is never a
deletion target and must not be added to `cleanup_pending`. Symbolic-link state files are refused.
Standalone read staging uses a uniquely named private directory and removes its known database
and sidecar entries individually; a failed parent barrier is reported as
`durability_pending_parent`, never as permission to remove the staging parent blindly.
On Windows, source, target, staging files, and their maintenance parents
must pass the read-only broad-principal ACL gate; SYSTEM and administrators remain trusted, and
other tasks running as the same OS user remain inside the same-user trust boundary.
On POSIX, restore follows the same boundary: a safe existing target parent retains its mode, a
missing target parent is created as `0700`, and the staged and restored database files use `0600`.
Existing parents must be owned by the current effective user and have no group or other write bit.

After restoring any supported schema, run `state verify`, then the Router protocol canary and exact
workspace status checks before reopening admission. A schema-1 or schema-2 backup may be restored for rollback only if
no task, claim, or recovery write occurred after that whole snapshot; never restore individual
tables or edit `schema_version`. Before 1.4 opens a restored legacy schema, first require a passing
`state verify --for-migration`. Otherwise keep entry stopped and use the matching legacy rollback
procedure.

An external queued freeze is also a cooperative drain request for tasks whose earlier claims block
that freeze; claimless tasks and tasks with only later queued claims are not signalled. Before its
next project write, a blocking owner's `claim assert` returns `workspace-busy` with
`reason=freeze-drain-requested`. `park_ready=false` means the task must first release an open
resource or freeze claim. After the current operation reaches a known terminal state, the owner
releases those claims and heartbeats again. It runs `task park` only if the new drain remains and
reports `park_ready=true`; a resource-only owner may instead be claimless after the release. The command
atomically parks that task's active or queued path-only claims and optionally waits for them to
resume. Parking refuses resource claims, freeze claims, unknown tasks, and calls made without a
queued external freeze. When the target freeze is released or cancelled, the same claim IDs and
scopes return to the FIFO queue at their original order. A wait timeout leaves them parked or
queued for later automatic resumption; it never duplicates or discards them. Heartbeat always
reports `restoration_pending_claim_ids`, including claims still queued behind a later freeze, so a
caller waits while the list is non-empty and proceeds only after the same claims become active.

Token files use exclusive creation. POSIX mode is `0600`. On Windows, token paths outside the
canonical current-user temporary directory are refused, and symbolic links or junctions are
rejected. The Router creates a private `0700`-style Windows token leaf rather than using the Temp
root ACL directly. The Scheduler binds canonical containment, regular-file identity, owner, and
DACL allowlist checks to the opened descriptor on creation and every later read. Terminal removal
rechecks the same boundary and uses the opened Windows handle, so a path replacement cannot redirect
the deletion. SYSTEM and administrators remain part of the Windows trust boundary. Multiple tasks
running as the same OS user are not strongly isolated from one another, so callers must keep token
paths and contents out of messages, logs, and tracked files. Receipt acknowledgement removes only a
file whose handle-bound content hash still matches the terminal release receipt.

Scheduler performs that removal only after Router acknowledges an authenticated `completed` or
`failed` task-release receipt. It deliberately preserves the token for
`outcome_unknown`; deterministic recovery may then leave an orphaned token for
a provably terminal task because the recovery command does not accept the
secret path. Only an authorized maintenance flow with terminal-state evidence
may handle that orphan. Never infer safety from token mtime or age.

Schema 3 retains constant-time queue allocation from a per-workspace counter and uses a composite
workspace/token/creation index for authentication. Maintenance retains the newest 1000 terminal
tasks per workspace. Older terminal task deletion cascades to its closed claims, scopes, and
recovery events; active, queued, parked, and `outcome_unknown` state is excluded from pruning.
Targeted commands maintain only their workspace; explicit `workspace list` performs global
maintenance through separate global and per-workspace expiry indexes. If the system wall clock
moves backward, active-task heartbeats found in the future
are rebased while preserving at most their remaining 86400-second lease, and oversized expiry is
clamped to the same current-time horizon. A future heartbeat with no positive lease expires safely
and becomes `outcome_unknown` when an active claim exists. Existing `outcome_unknown` timing is
never rewritten automatically because it is an evidence-bearing manual recovery fence.

## Unknown outcomes

An active task whose TTL expires while it owns a claim, or a task explicitly released as `outcome-unknown`, blocks new scheduling and preserves active claims. Recovery requires human-readable evidence:

```text
unity-scheduler recovery resolve --workspace <root> --task-id <id> --resolution completed|failed --evidence <text> --operation-id <uuid-v4>
```

The command records the evidence, releases the preserved claims, and resumes the queue. A
claimless expired task is closed automatically without blocking the workspace. An expired task
with any active claim may block indefinitely until that deterministic recovery succeeds; TTL is
not an unconditional claim-release lease.

## Executor contract

The Router must acquire the needed claims before touching Unity or workspace
files, run its independently selected executor, heartbeat during long work, and
release with an honest result. No integration may import
`unity_workspace_scheduler`, inspect uv tool directories, or infer ownership
from process state.

For `start-editor` and `cli-test`, the Router treats a queued `unity-live` wait
as an invalidation boundary. After grant it must heartbeat and bind the same
task with enough TTL for the remaining bounded operation, assert the exact
claim, and re-resolve the exact project, route, executor, and Unity/CLI versions.
Only the fresh result may build and dispatch the command; maintenance changes
during the wait never authorize use of the pre-wait command.
