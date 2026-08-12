# Setup and protocol

## Boundary

The scheduler owns only machine-local coordination state. It has no daemon, network listener, Unity package, Editor integration, test farm, executor adapter, MCP client/server, or VCS adapter. Unity projects never depend on it. Callers invoke `unity-scheduler` as an external process and parse its public JSON fields.

State defaults to `%LOCALAPPDATA%\UnityWorkspaceScheduler` on Windows and `$XDG_STATE_HOME/unity-workspace-scheduler` or `~/.local/state/unity-workspace-scheduler` on POSIX. `UNITY_SCHEDULER_STATE_DIR` and the global `--state-dir` option can select an isolated state root.

## Registration

```text
unity-scheduler workspace register --workspace <absolute-root>
unity-scheduler workspace status --workspace <absolute-root>
unity-scheduler workspace list
unity-scheduler workspace unregister --workspace <absolute-root>
```

Registration is machine-local and writes nothing into the workspace. Unregistered or moved paths fail closed. Unregister refuses any active/unknown task or open claim.

## Tasks and claims

```text
unity-scheduler task start --workspace <root> --owner <label> --summary <text> --token-file <path> [--ttl <seconds>]
unity-scheduler task heartbeat --workspace <root> --token-file <path> [--ttl <seconds>] [--note <text>]
unity-scheduler claim acquire --workspace <root> --token-file <path> [--write <path>]... [--resource <name>]... [--wait <seconds>] [--keep-queued]
unity-scheduler claim assert --workspace <root> --token-file <path> [--write <path>]... [--resource <name>]... [--freeze]
unity-scheduler claim release --workspace <root> --token-file <path> --claim-id <id>
unity-scheduler freeze acquire --workspace <root> --token-file <path> [--wait <seconds>] [--keep-queued]
unity-scheduler task release --workspace <root> --token-file <path> --result completed|failed|outcome-unknown [--note <text>]
```

Write paths are normalized inside the registered root; absolute paths outside it are rejected. Ancestor/descendant paths conflict, and an asset path conflicts with its `.meta` partner. Resource names are opaque workspace-local locks. Conflicting claims queue FIFO; non-conflicting claims can run together. A queued freeze is a fair barrier and becomes exclusive after earlier owners leave.

Token files use exclusive creation. POSIX mode is `0600`; Windows inheritance is removed and Full Control is granted only to the current identity. A normal task release removes only a file whose content still matches the task token.

## Unknown outcomes

An active task whose TTL expires while it owns a claim, or a task explicitly released as `outcome-unknown`, blocks new scheduling and preserves active claims. Recovery requires human-readable evidence:

```text
unity-scheduler recovery resolve --workspace <root> --task-id <id> --resolution completed|failed --evidence <text>
```

The command records the evidence, releases the preserved claims, and resumes the queue. A claimless expired task is closed automatically without blocking the workspace.

## Executor contract

The caller must acquire the needed claims before touching Unity or workspace files, run its independently selected executor, heartbeat during long work, and release with an honest result. No caller may import `unity_workspace_scheduler`, inspect uv tool directories, or infer ownership from process state.
