# Unity Workspace Scheduler

`unity-scheduler` is a machine-local control plane for agents and tools that share Unity workspaces. It coordinates task lifetime, write paths, named resources, FIFO queues, exclusive freezes, and fail-closed recovery.

It does not start or inspect Unity, run tests, call MCP, inspect version control, or install anything into a Unity project. The package has no runtime dependencies. Callers use only the command's JSON process protocol; they must not import its Python package or depend on its installation layout.

## Install

```powershell
uv tool install "unity-workspace-scheduler @ git+https://github.com/ZeroGameStudio-CN/zeroengine.git@<tested-commit>#subdirectory=Tools/unity-workspace-scheduler"
unity-scheduler --version
```

## Minimal flow

```powershell
$workspace = "D:\unity\projects\Game"
$tokenFile = Join-Path $env:TEMP "unity-work-$PID.token"

unity-scheduler workspace register --workspace $workspace
unity-scheduler task start --workspace $workspace --owner task-label --summary "Targeted Unity work" --token-file $tokenFile
unity-scheduler claim acquire --workspace $workspace --resource unity-live --write Assets/Scripts --token-file $tokenFile

# The caller independently runs the selected Unity executor here.

unity-scheduler task release --workspace $workspace --result completed --token-file $tokenFile
```

Every command except `--help` and `--version` returns one JSON envelope with `ok`, `code`, `message`, `duration_ms`, and either `result` or `details`. Task secrets are written only to an exclusive owner token file and are never returned in JSON.

See [Setup and protocol](docs/setup.md) for the complete command contract and recovery rules.
