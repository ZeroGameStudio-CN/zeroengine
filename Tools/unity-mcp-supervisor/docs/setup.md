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

```powershell
$lease = umcp lease acquire --project D:\unity\projects\POB --owner task-label | ConvertFrom-Json
$env:UMCP_PROJECT_LEASE_ID = $lease.result.lease_id
try {
    umcp connect --project D:\unity\projects\POB
    umcp call get_project_info --project D:\unity\projects\POB --params '{}'
    umcp run --project D:\unity\projects\POB -- status
} finally {
    umcp lease release --project D:\unity\projects\POB
    Remove-Item Env:UMCP_PROJECT_LEASE_ID -ErrorAction SilentlyContinue
}
```

Acquire one lease before the first live Editor operation and keep it through
refresh, compilation, Domain Reload, tests, Prefab/Scene changes, and final
Console inspection. Additional tasks enter a per-project FIFO queue in
`lease acquire` for up to 600 seconds by default; expired or terminated waiters
are skipped automatically. The 30-minute lease TTL is refreshed by the owner’s
live commands and recovers abandoned leases; long idle tasks can use
`lease renew`. Always release in cleanup.

An active lease makes unclaimed or incorrectly claimed `connect`, `call`, and
`run` fail with `project_busy` before dispatch. With no active task lease,
pre-v0.4 commands remain compatible and retain their per-command serialization.
Use `umcp lease status` or `umcp doctor` to see the owner and expiry without
exposing its lease ID. Manual service stop/restart is refused while a lease is
active.

Different project paths may hold leases and run concurrently. Two Editors
cannot share the same absolute project path; use a separate workspace/worktree
for true Editor parallelism.

If a business request loses its response after dispatch, `umcp` exits with code
7 and `outcome_unknown=true`. Do not replay a write unless its effect has been
checked or the operation is known to be idempotent.

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
