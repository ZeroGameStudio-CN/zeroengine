# Unity MCP 项目任务租约

- 状态：Implemented
- 更新日期：2026-08-02
- 基线：ZeroEngine `aacb47540807840fe02b75718893f19c7654f450`

## 目标

让同一台机器上的多个 Agent 任务安全共享 Unity MCP Supervisor：同一 Unity 项目在完整任务期间只有一个 live-operation owner，后来的任务在获取租约时排队；不同绝对项目路径继续并行。租约必须跨多次 `umcp` 调用、Unity 编译和 Domain Reload 保持，不依赖消费项目的 `AGENTS.md`，也不修改 Coplay Unity MCP。

## 非目标

- 不把普通 C#、文本或只读源码工作纳入租约。
- 不允许同一绝对项目路径打开多个 Unity Editor。
- 不把端口、Server-global active instance 或 Unity MCP 直连重新作为路由依据。
- 不提供恶意本机进程之间的安全边界；租约用于可靠协调互不信任程度较低的并行 Agent 任务。
- 不修改 POB 或其他消费项目的业务代码、Prefab、Scene、manifest 或现有 pending changes。

## 当前问题

`umcp v0.3.0` 已用 canonical project root 对单次 `connect`、`call`、`run` 加进程锁，同一项目的同时调用会串行，不同项目可并行。但锁在每条 CLI 命令返回时释放，无法表达“这个任务接下来还要 refresh、等待编译、跑测试、读最终 Console”。另一对话可在两条命令之间插入 live operation，导致刷新、测试、Prefab/Scene 修改或 Console 归因互相干扰。

仅在项目 `AGENTS.md` 写“单 owner”不能强制执行，也不能为所有 Unity 工作区提供一致行为。正确边界是 ZGS 自有的 `umcp` CLI 负责强制，通用 `unity-mcp-instance-bootstrap` Skill 负责任务生命周期。

## 已确认设计

1. 在 `umcp` 增加 project task lease，按 canonical absolute project root/hash 分区；状态只写入每用户 Supervisor state directory，不进入 Unity 项目。
2. CLI 增加：
   - `umcp lease acquire --project <root> --owner <label> [--wait <seconds>] [--ttl <seconds>]`
   - `umcp lease status --project <root>`
   - `umcp lease renew --project <root> --lease-id <id> [--ttl <seconds>]`
   - `umcp lease release --project <root> --lease-id <id>`
3. `acquire` 默认最多等待现有 project lock timeout（600 秒）。等待时不占用单次 operation lock，因此当前 owner 仍能继续调用并释放；租约释放或过期后，等待者通过现有 project lock 原子取得所有权。
4. 每条租约记录包含 schema version、canonical project root、随机 lease ID、owner label、acquired/renewed/expires 时间。JSON 原子写入；每项目独立 guard lock 保护读取和变更。
5. 默认 TTL 为 1800 秒。成功的 `renew` 及携带正确 lease ID 的 `connect/call/run` 都从当前时刻续租。任务正常结束必须显式 release；任务崩溃或遗忘释放时，过期记录可被下一任务回收。
6. `connect/call/run` 新增 `--lease-id`，同时接受 `UMCP_PROJECT_LEASE_ID`。执行前先检查活跃租约以快速拒绝错误 owner，取得既有单次 project lock 后再次检查并续租，关闭检查与执行之间的竞态。
7. 当项目没有活跃任务租约时，旧的无 `--lease-id` 调用继续工作，保持 CLI 向后兼容；一旦活跃租约存在，无租约或错误租约调用返回 `project_busy`、`retryable=true` 和脱敏 owner/到期信息，绝不绕过。
8. `status`、`doctor` 和 `lease status` 是只读诊断，不要求租约；它们报告 owner 和时间，但不输出 lease ID。`doctor` 仍以精确 project root/hash 为路由结论。
9. 手工 `service stop/restart` 把未过期 project lease 视为 live owner 并拒绝生命周期变更；Supervisor 自身的健康恢复逻辑保持现状。
10. 同一任务若并发发出多条 live command，仍由现有单次 project lock 串行。不同 canonical project roots 拥有不同 operation lock 与 lease record，可并行。
11. 通用 `unity-mcp-instance-bootstrap` Skill 调整为：任何 live Editor 操作前先 doctor 精确项目，再 acquire 任务租约；本任务所有 `connect/call/run` 带 lease ID；编译/Domain Reload 等待期间保持租约；最终 release。等待超时只报告当前 owner，不启动竞争 Unity 进程或改走直连。
12. POB 的项目级说明可保留作 defense in depth，但正确性不依赖它；本任务不修改 POB `AGENTS.md`。
13. 本次仅发布 `umcp` CLI `0.4.0`；Editor companion 协议与包版本保持 `0.3.0`，消费项目无需改 manifest/lock。CLI 版本测试不得再错误要求 companion 与 CLI 同版本。

## 状态与并发流程

```text
Task A acquire(P) ──> lease A active ──> call/refresh/wait/test ──> release
                              │
Task B acquire(P, wait) ──────┴──────────── waits ──────────────> lease B

Task C acquire(Q) ───────────────────────── runs concurrently (Q != P)
```

获取算法：

1. 读取项目租约；若未过期租约存在，释放 guard 后等待并重试，绝不持有 operation lock 等待。
2. 无活跃租约时取得项目 operation lock。
3. 在 lease guard 下再次读取；仍为空才原子写入新租约，否则释放 operation lock 后继续等待。
4. 到达 wait deadline 时返回当前 owner 的 `project_busy`。

命令算法：

1. operation lock 前预检租约，错误 owner 快速失败。
2. 取得 operation lock。
3. 再检租约；正确 owner 续租，无租约则允许兼容调用，错误 owner 失败。
4. 完成命令并释放 operation lock；task lease 继续存在。

## 失败与恢复

- Agent/task 崩溃：TTL 到期后下一 acquire 删除过期记录并取得租约。
- Unity 编译或 Domain Reload：task lease 不依赖 Editor session token，Editor 重连后同一 lease ID 继续使用。
- 错误 lease ID：命令不派发到 Unity，返回当前 owner/expiry，不泄露正确 ID。
- release 时租约已过期或不存在：作为幂等清理成功，返回 `released=false`。
- release 时项目已由另一 owner 取得：返回 `project_busy`，不得删除他人租约。
- CLI 在写租约时退出：原子替换保证旧记录或完整新记录，不留下半写 JSON；损坏记录按占用冲突报告，不静默覆盖，供用户诊断。
- service stop/restart：活跃 operation 或 task lease 均阻止手工生命周期变更。

## 修改范围

ZeroEngine：

- `Tools/unity-mcp-supervisor/src/unity_mcp_supervisor/project_lease.py`：租约记录、原子持久化、获取/检查/续租/释放。
- `Tools/unity-mcp-supervisor/src/unity_mcp_supervisor/service_state.py`：lease state path 与默认 TTL。
- `Tools/unity-mcp-supervisor/src/unity_mcp_supervisor/cli.py`：lease 命令组、live command 校验、status/doctor 诊断。
- `Tools/unity-mcp-supervisor/src/unity_mcp_supervisor/supervisor.py`：手工生命周期保护。
- `Tools/unity-mcp-supervisor/tests/unit/test_project_lease.py`：状态、过期、排队、并行与错误 owner。
- `Tools/unity-mcp-supervisor/tests/unit/test_editor_control.py`：CLI/companion 独立版本合同。
- `Tools/unity-mcp-supervisor/tests/integration/test_cli_contract.py`：CLI JSON 合同与 live command 强制。
- `Tools/unity-mcp-supervisor/README.md`、`docs/setup.md`：操作流程与兼容边界。
- `Tools/unity-mcp-supervisor/pyproject.toml`、`uv.lock`：发布版本更新。

通用 Skill 源仓库：

- `skills/unity-mcp-instance-bootstrap/SKILL.md`：任务租约生命周期。
- 仅在现有 metadata 与新工作流不一致时更新该 Skill 的 `agents/openai.yaml`。

## 实现顺序

1. 先添加失败的租约单元测试与 CLI 合同测试。
2. 实现机器本地租约状态和并发算法，使单元测试通过。
3. 接入 `connect/call/run`、诊断与 service 生命周期保护。
4. 更新版本与文档，运行完整 Supervisor 测试、lint 和 CLI smoke。
5. 更新并验证通用 Skill；安装新版 `umcp` 和 Skill 到 9950。
6. 提交并推送两个源仓库，从 GitHub 同步并安装 M5；最后清理任务 worktree/branch。

## 验证

- 基线：`uv run pytest tests/unit/test_locking.py tests/integration/test_cli_contract.py`。
- 实现：`uv run pytest`。
- 静态：`uv run ruff check src tests`。
- CLI smoke：两个临时 Unity project root；同项目第二租约等待/超时，释放后可取得；不同项目可同时取得；错误/缺失 ID 的 live command 不到达 fake server。
- Skill：运行 `skill-creator` 的 `quick_validate.py`，检查 SKILL 与 metadata 一致。
- 安装：9950 从已推送 ZeroEngine commit 安装 `uv tool`，执行 `umcp --help`、`umcp lease --help` 和临时 state smoke；个人 Skill 运行 `install.ps1`。
- 跨机器：M5 从 GitHub 更新两个源提交并安装，执行同等版本/帮助 smoke。

## 实现结果

- `umcp 0.4.0` 已实现按 canonical project root 隔离的任务租约、600 秒默认排队、1800 秒 TTL、owner 命令续租、过期回收、幂等 release 和脱敏诊断。
- `connect/call/run` 在 operation lock 前后校验租约；活跃 owner 存在时，无/错误 ID 返回可重试 `project_busy`，没有租约时保持 v0.3 调用兼容。
- 手工 service stop/restart 会拒绝未过期租约；不同项目继续独立运行。
- CLI 版本升至 `0.4.0`，Editor companion 协议和包保持 `0.3.0`，消费项目 manifest/lock 无需变化。
- Windows 全量验证：ruff 通过，pytest 95/95 通过；CLI 临时 state smoke 验证同项目排他、跨项目并行、release 后 inactive，且临时状态已清理。
- `unity-mcp-instance-bootstrap` 已改为 doctor/acquire/use/release 的完整任务生命周期，并通过 `skill-creator` quick validation。

## 验收标准

1. 同一 canonical project root 同时最多一个未过期 task lease。
2. 第二任务可在 acquire 阶段等待，且等待期间不阻止当前 owner 继续 call 或 release。
3. 当前 owner release 后，等待任务能取得新 lease；超出 wait deadline 返回可重试 `project_busy`。
4. 不同 canonical project roots 的 lease 获取和 live commands 不互相阻塞。
5. 活跃租约期间，无 lease ID 或错误 ID 的 `connect/call/run` 在派发 Unity 前失败。
6. 正确 lease ID 的 `connect/call/run` 成功并刷新 expiry；同一任务的多命令仍逐条串行。
7. 无活跃租约时，v0.3.0 形式的 `connect/call/run` 保持兼容。
8. 租约跨 Unity 编译、Domain Reload 和 CLI 进程退出保持，直到 release 或 TTL 到期。
9. 过期租约可自动回收；幂等 release 不会删除后来 owner 的租约。
10. `status`、`doctor` 可查看脱敏 owner/expiry，任何诊断都不输出 lease ID。
11. 手工 service stop/restart 在活跃 task lease 期间被拒绝。
12. 不修改 Coplay Unity MCP、Unity 项目业务文件或消费项目 `AGENTS.md`。
13. Supervisor 完整 pytest 与 ruff 通过，CLI 临时 state smoke 通过。
14. 通用 Skill 强制完整 acquire/use/release 流程，验证通过并安装到 9950 与 M5。
15. 两个源仓库提交均推送；最终交付列出 commit、安装机器和任何未同步项。
