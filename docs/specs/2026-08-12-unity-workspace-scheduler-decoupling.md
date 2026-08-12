# Unity 工作区调度器解耦与破坏性迁移

- 状态：Final
- 日期：2026-08-12
- 最后更新：2026-08-12
- 设计批准：用户要求清理全部 MCP/UMCP 关联、不保留兼容，并明确要求低耦合、不要依赖和被依赖
- 执行授权：Authorized；覆盖实现、测试、正常 Git 发布、双机安装与旧 UMCP 精确清理

## 目标与非目标

把 `unity-mcp-supervisor` 一刀切替换为独立的 `unity-workspace-scheduler`。它是机器本地控制面，只协调共享 Unity 工作区的任务和资源所有权，不是 MCP、Unity 执行器、测试运行器或项目包。

非目标：

- 不启动、连接或控制 Unity Editor，不封装官方 Unity CLI。
- 不运行 Unity Test Framework，不保留隔离测试农场。
- 不读取 Plastic/Git pending，不替调用方判断业务结果。
- 不在 Unity 项目中安装伴生包、配置或 SDK。
- 不保留 `umcp`、`umcpd`、旧 Python 模块、旧状态目录或任何兼容入口。

## 已确认设计

### 单向外部协议

- 发布名为 `unity-workspace-scheduler`，唯一入口为 `unity-scheduler`，Python 模块为 `unity_workspace_scheduler`。
- 运行时仅使用 Python 标准库；不依赖 MCP、Unity、官方 Unity CLI、测试框架、VCS CLI 或项目代码。
- 调用方只允许把 `unity-scheduler` 当外部进程调用并消费稳定 JSON；禁止导入其 Python 模块、解析 uv 内部安装路径或依赖实现文件。
- Unity 项目不引用调度器包，也不写入调度器配置。注册和状态只存在于用户本机状态目录。

### 调度职责

- 工作区必须先显式注册；未注册、路径漂移或状态损坏一律 fail closed。
- task 代表一个有 TTL 的工作单元，凭据只写 owner-only token file，不在 JSON/stdout 输出明文。
- claim 原子声明相对写路径和不透明资源名；路径按祖先/后代冲突，Unity `.meta` 与对应资产只作为通用路径配对规则处理。
- 冲突 claim FIFO 排队；freeze 是公平屏障，阻止其后的新 claim 越过。
- task 正常结束释放其 claim；持有资源的 task 失联或报告 `outcome-unknown` 时工作区硬阻塞，只有带证据的恢复命令能解除。
- status、list、task、claim、freeze、queue、recovery 和 unregister 构成完整公开命令面；不提供 executor、service、connect、call、run、doctor、lease 或 test 命令。

### 状态与并发

- 新状态根为 `UnityWorkspaceScheduler`，环境变量前缀为 `UNITY_SCHEDULER_`，schema 从 1 开始。
- 使用单个 SQLite 数据库和 `BEGIN IMMEDIATE` 保证跨进程原子调度，不需要常驻 daemon 或第三方锁库。
- 所有公开结果使用 `{ok, code, message, result}` JSON envelope；status 永不暴露 token hash 或私有凭据。
- 调度器不主动扫描 Unity、进程或 VCS；调用方先用调度器取得所有权，再自行选择和运行官方 Unity CLI、已有 Editor 或 batchmode。

### 破坏性迁移

- 安装新工具前只读盘点旧注册、active task/claim/queue/unknown、服务、测试农场与进程；存在活动所有权时停止迁移。
- 将仍存在的旧注册路径逐个显式注册到新状态；这是一次性运维迁移，不进入产品兼容代码。
- 全部调用方切到 `unity-scheduler` 并验证后，停止旧服务，卸载 `unity-mcp-supervisor`，删除其启动项和精确状态根。
- 删除 ZeroEngine 的 Unity MCP 根依赖、伴生包、旧 workflow 和文档当前入口；已关闭历史 spec 保留为历史证据。
- 删除 `unity-mcp-instance-bootstrap` 兼容 skill，并同步更新 `unity-workspace-router`、`unity-qa`、ZGS 执行与设备路由。

## 失败与回滚

- 新工具不可用、注册缺失、数据库损坏、token 不匹配或 claim 冲突时均不运行 Unity 写操作。
- 迁移阶段在新安装和调用方验证完成前保留旧状态备份；不得同时让新旧调度器授予所有权。
- 代码回滚只允许回到本变更前 Git 提交；不恢复 MCP 运行服务。若新调度器验证失败，停止迁移并保留旧状态证据等待修复。

## 实现顺序

1. 在 ZeroEngine 新建标准库调度器和独立测试，删除 MCP/执行器/测试农场代码及仓库当前引用。
2. 更新全局 skills 和脚本到外部 `unity-scheduler` JSON 协议，删除旧兼容 skill 和内部 uv 路径兜底。
3. 跑调度器全套测试、静态无耦合检查、skill tests/validator 和两仓 diff 审查。
4. 正常提交、推送并安装当前机器；迁移旧注册，在无活动所有权前提下停服、卸载和清理旧状态。
5. 按 personal skills 发布契约同步 M5；验证两机只存在新入口、状态可注册/调度/释放且无旧进程与安装。

## 验收标准

1. 仓库当前代码、配置、workflow 和 active docs 不再出现 MCP、UMCP、旧包、旧模块或旧命令；关闭的历史 spec 可保留。
2. `pyproject.toml` 无运行时依赖，且包内没有 Unity、MCP、VCS、网络、daemon、service、executor 或 test-farm 实现。
3. 两个并发进程对冲突路径/资源只能有一个 active；非冲突可并行；FIFO、freeze、TTL unknown 和证据恢复有直接测试。
4. token 只落 owner-only 文件，正常 release 精确删除；status/list 不泄露 token 或 hash。
5. 调用方只解析公开 JSON，不导入调度器模块，不解析 uv 安装目录；未注册或无 claim 时 fail closed。
6. 当前机器旧 13 个注册在无活动 task/claim/queue/unknown 后一次性迁移；旧 MCP 服务、农场、进程、入口、安装和状态根全部消失。
7. Windows 与 M5 安装同一已发布提交；两机 `unity-scheduler --version`、注册、task/claim/release smoke 通过，最终无活动所有权。

## 自审结论

边界按控制面最小职责切开：调度器不知道 Unity 如何执行，Unity 项目也不知道调度器实现；二者只在调用方编排层通过进程协议相遇。删除兼容层避免新命名继续背负 MCP、daemon、测试农场和内部安装布局。SQLite 与标准库足够完成本机原子调度，没有引入新的常驻服务或依赖链。

## As-Built

- `Tools/unity-mcp-supervisor` 已由 `Tools/unity-workspace-scheduler` 取代；公开面仅保留 `unity-scheduler` JSON CLI，运行时依赖为空。
- 旧 daemon、MCP server、Unity 伴生包、Editor 控制、VCS 扫描、测试农场和兼容入口均未进入新实现；根 Unity manifest/lock 已移除 MCP 包。
- 调用方迁移到外部进程协议，未导入调度器模块或解析 uv 安装布局；旧 bootstrap/test-router skill 已删除。
- 调度器本地验证为 Ruff 通过、格式通过、pytest `12 passed`、构建通过；并发冲突、FIFO、freeze、TTL unknown、证据恢复和 token 生命周期均有直接测试。
- 实现与批准设计无偏差；发布提交、双机安装和旧运行时清理结果由本次任务最终交付记录给出。
