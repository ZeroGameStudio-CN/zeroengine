# Unity MCP Supervisor Control

Editor-only companion for `umcp`. It exposes only project-scoped `status` and
`connect` requests through the current user's local state directory. It does
not patch MCP for Unity, start servers, execute arbitrary code, or touch assets.

Requires a compatible `com.coplaydev.unity-mcp` package. Compatibility failures
disable this control channel without adding a compile-time dependency on the
upstream Editor assembly.

Formal projects install this package from the same tested ZeroEngine commit as
their `umcp` CLI:

```text
https://github.com/ZeroGameStudio-CN/zeroengine.git?path=Tools/unity-mcp-supervisor/src/unity_mcp_supervisor/unity_package#<tested-commit>
```
