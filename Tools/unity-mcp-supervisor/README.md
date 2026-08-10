# Unity MCP Supervisor

`umcp` gives terminal-based agents one stable, project-safe route into multiple
Unity Editors through one loopback Unity MCP HTTP server.

The tool does not patch Unity MCP. It supervises the pinned upstream server,
routes every call by verified project path/hash, serializes live operations per
project, coordinates task/path/resource claims and fair workspace freezes,
binds Unity live access to the same task identity, and uses an independent
Editor-only companion package to connect an open but unconnected Editor without
plugin UI, restart, or a visible terminal.

On Windows, daemon, Server, Editor relaunch, passthrough, and test children are
created without console windows. A replacement supervisor can adopt only a
previously recorded owned Server whose token, PID, process creation time,
pidfile, endpoint listener, version, and REST contract all still match; this
avoids a Server or Editor restart while leaving ordinary external Servers alone.

The reviewed source of truth lives in the ZeroEngine monorepo. Pin the CLI and
companion to tested ZeroEngine commits and require compatible control protocol
versions. The pins may advance independently for CLI-only or companion-only
fixes that do not change that compatibility contract.

```powershell
# Development checkout
uv sync --locked
uv run umcp service ensure
uv run umcp control package-path
uv run umcp workspace bootstrap --project D:\unity\projects\POB
$task = uv run umcp workspace task start --project D:\unity\projects\POB --owner task-label --summary "Unity inspection" | ConvertFrom-Json
$env:UMCP_WORKSPACE_TASK_TOKEN = $task.result.task_token
uv run umcp workspace claim acquire --project D:\unity\projects\POB --resource unity-live
uv run umcp connect --project D:\unity\projects\POB
uv run umcp call get_project_info --project D:\unity\projects\POB --params '{}'
uv run umcp workspace task release --project D:\unity\projects\POB --result completed
Remove-Item Env:UMCP_WORKSPACE_TASK_TOKEN -ErrorAction SilentlyContinue
```

```powershell
# Installed tool (replace <tested-commit>)
uv tool install "unity-mcp-supervisor @ git+https://github.com/ZeroGameStudio-CN/zeroengine.git@<tested-commit>#subdirectory=Tools/unity-mcp-supervisor"
umcp service ensure
umcp workspace bootstrap --project D:\unity\projects\POB
$task = umcp workspace task start --project D:\unity\projects\POB --owner task-label --summary "Unity inspection" | ConvertFrom-Json
$env:UMCP_WORKSPACE_TASK_TOKEN = $task.result.task_token
umcp workspace claim acquire --project D:\unity\projects\POB --resource unity-live
umcp connect --project D:\unity\projects\POB
umcp workspace task release --project D:\unity\projects\POB --result completed
Remove-Item Env:UMCP_WORKSPACE_TASK_TOKEN -ErrorAction SilentlyContinue
```

`workspace bootstrap` creates an idempotent user-level `required` registration
inside Supervisor private state; it does not write the Unity project. An existing
project-local policy remains authoritative. Remove only the user registration
with `umcp workspace unregister --project <root>` after all tasks, claims, and
Unity leases have drained.

Agent lifecycle rule: on first use of a Unity project on a machine, inspect
`workspace status` and bootstrap when its policy source is `none`. Before moving,
renaming, or deleting a project, drain coordination and unregister the old root;
after a move or rename, bootstrap the new canonical root. A failed unregister
blocks the filesystem operation. Never edit registration files directly.

Projects without a workspace registration or policy retain the legacy `umcp lease` contract.
An `audit` or `required` policy enables `umcp workspace status/watch`, persistent
path/resource queues, Plastic observations, and whole-workspace freezes. In
`required`, `connect`, `call`, `run`, and legacy lease commands require the task
token and an active `unity-live` claim; the private lease ID never leaves local
state. See [docs/setup.md](docs/setup.md) for the complete workflow.

See [docs/setup.md](docs/setup.md) for bootstrap safety and operational
boundaries.

## Isolated Unity tests

`umcp test` can run exact Unity Test Framework scopes concurrently without
opening a second Unity process on the main project. Each worker uses a separate
Git or Plastic workspace, project root, Library, log, result, and artifact tree.
The project needs no package, policy, or adapter change beyond normal workspace
bootstrap.

Provision machine capacity once, then submit from an active workspace task:

```powershell
umcp test farm provision --workers 2
umcp test submit --project D:\unity\projects\Game --platform EditMode `
  --test-filter Namespace.Fixture.Test `
  --overlay-path Assets/Scripts/Changed.cs `
  --external-state-safe
umcp test farm status
umcp test wait --job <job-id>
```

Use `--baseline-only` instead of `--overlay-path` to test the checked-in
revision. Every overlay path must be current SCM pending, explicitly declared,
and covered by the submitting task's granted write claim; the farm never infers
ownership from a dirty directory. Add/delete/move and Unity `.meta` pairs are
reproduced explicitly. Unsafe snapshots, missing capacity, or absent external
state proof return the safe serial route instead of starting concurrent Unity.

Unity 2022.3 has no official Editor argument that redirects
`Application.persistentDataPath` per project root. Declare
`--external-state-safe` only after confirming the selected scope does not use
PlayerPrefs, persistent data, fixed ports/devices, or another process-external
writable singleton. The first job for each distinct baseline, critical-input,
and overlay fingerprint on a slot runs cold and warm; Library reuse starts only
when per-test outcomes match.

Design record: [Unity workspace zero-code bootstrap](../../docs/specs/2026-08-10-unity-workspace-bootstrap.md).
