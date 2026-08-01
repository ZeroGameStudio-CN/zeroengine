# Unity MCP Supervisor

`umcp` gives terminal-based agents one stable, project-safe route into multiple
Unity Editors through one loopback Unity MCP HTTP server.

The tool does not patch Unity MCP. It supervises the pinned upstream server,
routes every call by verified project path/hash, serializes live operations per
project, and uses an independent Editor-only companion package to connect an
open but unconnected Editor without plugin UI, restart, or a visible terminal.

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
uv run umcp connect --project D:\unity\projects\POB
uv run umcp call get_project_info --project D:\unity\projects\POB --params '{}'
```

```powershell
# Installed tool (replace <tested-commit>)
uv tool install "unity-mcp-supervisor @ git+https://github.com/ZeroGameStudio-CN/zeroengine.git@<tested-commit>#subdirectory=Tools/unity-mcp-supervisor"
umcp service ensure
umcp connect --project D:\unity\projects\POB
```

See [docs/setup.md](docs/setup.md) for bootstrap safety and operational
boundaries.
