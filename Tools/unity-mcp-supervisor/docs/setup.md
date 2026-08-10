# Setup and operation

## Install

Install from a tested ZeroEngine commit or local checkout. The lockfile pins the
Unity MCP server, its CLI, FastMCP, and MCP runtime as one tested environment.

The first install is a machine-wide MCP maintenance window: stop direct Agent
MCP sessions and live Unity operations, then migrate every MCP-enabled Editor
on the machine. Unity EditorPrefs are shared across Editors, so do not leave a
second Editor permanently connected to an old endpoint while this supervisor
is active.

```powershell
uv tool install "unity-mcp-supervisor @ git+https://github.com/ZeroGameStudio-CN/zeroengine.git@<tested-commit>#subdirectory=Tools/unity-mcp-supervisor"
umcp config set-endpoint http://127.0.0.1:8080
umcp service start
umcp service enable
```

`service enable` is explicit because it creates a current-user login startup
entry. It does not require administrator rights. On Windows the login launcher,
supervisor, server, and passthrough child processes run without opening extra
console windows; an interactive `umcp` command only uses its existing terminal.
The daemon and any Editor relaunch also break away from the caller's Windows Job,
so closing an Agent terminal does not terminate them. The CLI cannot hide a
terminal already created by its caller; long maintenance checks should therefore
run as hidden child processes and write their results to a log.
While the supervisor runs, it also keeps the four Unity MCP EditorPrefs values
at the configured endpoint, correcting stale open Editor windows before their
reachability poll can tear down a healthy session.

If a supervisor exits unexpectedly while its Server remains healthy, the next
supervisor adopts it without restarting only when the prior owned state and all
live process evidence still match. Missing or conflicting proof leaves the
Server `external-compatible`; no process is guessed, adopted, or terminated.

## Connect an Editor

Each project needs the independent
`com.zerogamestudio.unity-mcp-control` Editor package. A source checkout or
installed wheel exposes its package directory with:

```powershell
umcp control package-path
```

Use that local `file:` directory only for an isolated trial. Formal projects
must pin the package from a tested ZeroEngine commit and commit both
`Packages/manifest.json` and `Packages/packages-lock.json`:

```text
https://github.com/ZeroGameStudio-CN/zeroengine.git?path=Tools/unity-mcp-supervisor/src/unity_mcp_supervisor/unity_package#<tested-commit>
```

The package remains separate from `com.coplaydev.unity-mcp`, so upstream
updates never overwrite it.

The CLI and companion pins may advance independently when their protocol
versions remain compatible. A CLI-only fix does not require project manifest
or lock-file churn; update the project pin only when companion code or its
compatibility contract changes.

## Upstream version maintenance

Track stable Unity MCP releases, but do not promote them automatically. For
each candidate, pin the Unity package tag and `mcpforunityserver` to the same
version, add only the reviewed tag and commit to the tested matrix, and bump the
Supervisor patch version. Reject beta/prerelease and unknown refs by default.

Before promotion, review the upstream compare, regenerate `uv.lock`, run the
complete Supervisor pytest and Ruff gates, then run one real-project canary
through `umcp`: doctor, connect, project-info probe, package refresh/compile,
and final Console inspection. Keep the previous tested stable ref in the matrix
and its install command available for one-release rollback. Update consumer
`Packages/manifest.json` and `Packages/packages-lock.json` together only after
the canary passes.

Open the Unity project normally, let the package compile, then run:

```powershell
umcp connect --project D:\unity\projects\POB
umcp doctor --project D:\unity\projects\POB
```

No Unity MCP window, Connect click, Unity Auto Refresh, or Editor restart is
required. The CLI verifies the exact open Editor by absolute project path, PID,
process start time, project hash, and a per-session token. Its companion handles
only `connect/status` on Unity's main thread, configures HTTP Local plus the
fixed endpoint, and calls the upstream public Bridge API. Success is reported
only after the shared Server independently returns the same `projectRoot`.

`--restart-editor` remains an explicit maintenance fallback when the companion
is missing/incompatible, package compilation failed, or Unity's main thread is
blocked. It always relaunches even if already connected; ordinary commands
never select it automatically.

The restart fallback sends only a normal close request. It never handles save prompts or
force-kills Unity; if unsaved content, a modal, or shutdown work blocks exit,
the command fails and leaves the Editor open. EditorPrefs are written only
after normal exit, so the closing plugin cannot restore an old endpoint. Normal
control requests do not change project files. The one-time companion install
changes only its package dependency and lock entry; the upstream Unity MCP
package must already be installed at a compatible version.

If the relaunched Editor stops at Unity's Safe Mode prompt, the CLI reports
that compilation-error state and leaves the decision visible. It never clicks
the dialog or silently adds `-ignoreCompilerErrors`.

After onboarding, ordinary `connect`, `call`, and `run` hot-connect as needed.
Domain Reload replaces the companion session token; the CLI discards the stale
request and retries the exact new session within its bounded budget.

## Agent usage

Agents use only `umcp`; remove their direct Unity MCP `/mcp` client entries
after the two-Editor acceptance run passes.

Register a project once without changing its files:

```powershell
umcp workspace bootstrap --project D:\unity\projects\POB
```

The command writes a schema-1 `required` registration under the current user's
Supervisor private state. It is idempotent and does not start Unity, inspect SCM,
or create a task or claim. An existing project-local policy remains authoritative.
`umcp workspace unregister --project <root>` removes only the user registration
and fails closed while any task, claim, Unity lease, lease waiter, or coordination
error remains.
The independent Unity companion package described above is still required for
live Editor connection; bootstrap does not install or change packages.

### Agent project lifecycle

On first use of a Unity project on each machine, resolve its canonical root and
inspect policy before any project write or live Editor operation:

```powershell
$status = umcp workspace status --project <root> | ConvertFrom-Json
if ($status.result.policy.source -eq "none") {
    umcp workspace bootstrap --project <root>
    $status = umcp workspace status --project <root> | ConvertFrom-Json
}
```

Continue only when the policy is valid, `enforcement` is `required`, and
`coordination_error` is null. A project-local policy may report source
`project`; otherwise the source is `registration`.

Before moving, renaming, or deleting a project, finish its tasks and require no
claims, Unity lease, lease waiter, freeze, or coordination error. Then remove
only the old machine-local registration:

```powershell
umcp workspace unregister --project <old-root>
```

If unregister fails, leave the project in place. After a move or rename,
bootstrap the new canonical root. If the directory was already removed, pass
its exact former absolute root to unregister. Never edit or delete files under
`workspace-registrations/` directly.

```powershell
$task = umcp workspace task start --project D:\unity\projects\POB --owner task-label --summary "Targeted Unity work" | ConvertFrom-Json
$env:UMCP_WORKSPACE_TASK_TOKEN = $task.result.task_token
try {
    umcp workspace claim acquire --project D:\unity\projects\POB --write Assets/Assets/_Scripts/_POB/MyScope
    umcp workspace claim acquire --project D:\unity\projects\POB --resource unity-live
    umcp connect --project D:\unity\projects\POB
    umcp call get_project_info --project D:\unity\projects\POB --params '{}'
    umcp run --project D:\unity\projects\POB -- status
} finally {
    umcp workspace task release --project D:\unity\projects\POB --result completed
    Remove-Item Env:UMCP_WORKSPACE_TASK_TOKEN -ErrorAction SilentlyContinue
}
```

The user registration selects `required`; the compatible project policy at
`Tools/Coordination/workspace-control.json` may instead select `audit` or
`required` and takes precedence. A task token is returned only at task creation; pass it
through `UMCP_WORKSPACE_TASK_TOKEN`, an owner-only token file, or stdin. Never
put it in argv or logs. Path claims include Unity `.meta` pairs. Overlapping
claims queue FIFO while unrelated paths remain parallel. A queued
`workspace-freeze` blocks later scope expansion and is granted only after older
writers drain. Acquire in this order: path, freeze when needed,
`vcs-maintenance`, then `unity-live`.

Classifying a pre-existing Plastic path with `workspace baseline disposition`
also requires the task token. `adopt` is bound to that task, blocks every other
task, and automatically becomes `protect` if its owner expires or releases
without submitting or resolving the path.

In `required`, `connect`, `call`, `run`, and legacy lease commands fail before
dispatch unless the token owns `unity-live`. The claim creates the existing
project lease internally; the lease ID is never public. Projects without a
workspace policy retain the pre-v0.5 lease workflow. `audit` records and shows
coordination state without making old entrypoints fail closed.

Use `umcp workspace status --project <root> --refresh-vcs` for a current
snapshot or `umcp workspace watch` for the live queue. Status includes task
links, phases, heartbeats, claims, freeze, Unity lease/queue, Plastic observation
age, unowned/protected pending, blockers, and next conditions without tokens or
private lease IDs. Heartbeat long tasks and always release them in cleanup.

Different project paths may hold leases and run concurrently. Two Editors
cannot share the same absolute project path; use a separate workspace/worktree
for true Editor parallelism.

If a business request loses its response after dispatch, `umcp` exits with code
7 and `outcome_unknown=true`; required-mode Unity commands also fence the task
as `outcome_unknown`. Keep heartbeating while checking the effect. Expiry turns
it into `orphaned_unknown`, which remains blocking until an owner records an
evidence-backed `workspace recovery resolve-unknown` disposition. Never replay
a non-idempotent write merely because the response was lost.

## Diagnosis and rollback

```powershell
umcp service status
umcp service logs
umcp doctor --project D:\unity\projects\POB
```

The tool never stops a compatible external server or an unknown listener. To
roll back, restore the previous companion commit pin and locked CLI release;
the companion dependency may also be removed without touching upstream Unity
MCP or business assets.
