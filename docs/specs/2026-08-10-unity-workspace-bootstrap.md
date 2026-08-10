# Unity 工作区零代码注册

- 状态：Implemented
- 最后更新：2026-08-10
- 基线：ZeroEngine `3abd18065a5a996b0517b6126edcedc1d195edd1`
- 执行授权：Authorized；用户 2026-08-10“可以，继续”，范围为本 spec 的实现与本地验证
- 终端操作授权：Not requested

## 目标与非目标

为任意 Unity 项目提供一次性 `umcp workspace bootstrap --project <path>`：在 Supervisor 用户级私有状态中注册标准 `required` 策略，使项目获得任务、路径锁、冻结、共享资源与 Unity 租约调度，且不写 Unity 项目目录。

不自动安装或修改 Unity 包，不修改业务代码、资源、`Packages`、`ProjectSettings` 或 SCM；不为 Git 补充 Plastic 等价的 pending 观察器。

## 决策与边界

- 注册表按规范化项目根路径的 SHA-256 标识，记录完整规范化根路径以检测哈希碰撞或误绑定。
- 项目内现有 `Tools/Coordination/workspace-control.json` 保持兼容并优先；bootstrap 不覆盖、删除或改写它。
- 新注册固定使用策略 schema 1、`required`、`unityMetaPairing=true`。这些是现有 POB 已验证标准，不增加项目参数。
- bootstrap 幂等；重复执行返回同一注册状态。
- `umcp workspace unregister` 只删除用户级注册；存在活动任务、claim、Unity lease、租约队列或协调错误时拒绝注销。存在项目内策略时不声称已禁用，也不触碰项目文件。
- 外部注册的加载必须校验记录中的项目根与请求根完全一致；损坏、版本不兼容或根不匹配时 fail-closed。
- 注册不启动 Unity、不连接 MCP、不创建任务/claim、不刷新 SCM，也不改变现有协调数据库内容。
- task start 与 unregister 共用项目互斥锁；unregister 在同一数据库事务内确认无活动协调状态后才删除注册，避免并发绕过。

## 范围

- `Tools/unity-mcp-supervisor/src/unity_mcp_supervisor/service_state.py`
- `Tools/unity-mcp-supervisor/src/unity_mcp_supervisor/workspace_control.py`
- `Tools/unity-mcp-supervisor/src/unity_mcp_supervisor/cli.py`
- 对应 `tests/unit`、`tests/integration`、README 与 setup 文档

## 状态与恢复

用户级注册文件位于 Supervisor 私有状态目录的 `workspace-registrations/`。项目策略存在时从项目加载；否则从匹配的用户注册加载；两者均不存在时维持 legacy lease 行为。注册文件采用原子替换。unregister 是回滚路径，删除后项目在下一次命令中恢复为项目内策略或 legacy 行为。

## 实施与验证

1. 增加私有注册目录、原子注册/删除和策略来源信息。
2. 增加 bootstrap/unregister CLI，并让全部策略入口使用同一状态目录解析。
3. 增加幂等、项目零写入、项目策略优先、损坏/错绑 fail-closed、注销恢复测试。
4. 运行 Supervisor 单元与 CLI 合同窄测，再运行完整 pytest、Ruff check/format check。

## 验收标准

1. 对未配置 Unity 项目执行 bootstrap 成功，项目目录逐文件快照前后相同，用户级注册存在且状态显示 `required`、来源为 registration。
2. 同一项目重复 bootstrap 不创建第二份记录、不改变结果。
3. 项目内策略存在时 bootstrap 不写用户注册、不修改项目，并明确返回项目策略已生效。
4. 注册损坏、schema 不兼容或根路径不匹配时不得静默启用其他项目策略。
5. unregister 仅在协调完全空闲时删除匹配的用户注册；项目内策略和项目内容不受影响。
6. 未注册且无项目策略的项目继续使用 legacy lease，不发生行为回归。
7. bootstrap/unregister 不启动 Unity、不创建 task/claim、不执行 SCM 命令。
8. Supervisor 完整 Python 测试与 Ruff gate 通过，diff 仅含本 spec 范围。

## 实施结果

- 已实现用户级原子注册、项目策略优先、幂等 bootstrap、空闲门控 unregister、策略来源状态与错误 fail-closed。
- CLI 合同测试证明 bootstrap/unregister 前后 Unity 项目逐文件内容完全一致，且未创建 task/claim。
- Supervisor 完整测试：`135 passed`；Ruff check 与 format check：通过；`git diff --check`：通过。
- 提交、发布和安装：Pending，未获得终端操作授权。
