# Unity 控制面自动恢复毕业门

- 状态：Closed
- 日期：2026-08-11
- 最后更新：2026-08-11
- 检查基线：ZeroEngine `8df99c6c`；MCP fork `4a29cdf0`
- 设计批准：用户要求“继续下一轮，做到毕业为止”
- 执行授权：Authorized；覆盖本 spec 的实现、测试、正常 PR/合并、双机安装、POB 精确 pin/checkin 与关闭

## 目标与非目标

目标是消除两类仍会让并行 Agent 人工等待的控制面故障：task token 只存在于调用进程而丢失，以及 Unity 命令已执行但响应在 HTTP/WebSocket/Domain Reload 边界丢失。成功后，正常断线能够确定性返回原结果或安全地执行一次；控制面不再靠聊天猜测、人工重放或等待 25 分钟 TTL。

非目标：

- 不允许同一 Unity project root、Library 或主 Editor 并发写。
- 不把任意 `execute_code`、菜单或第三方工具错误声明成只读。
- 不自动裁决“命令已开始、Unity 在持久化结果前崩溃”的真实歧义；该终极故障仍 fail closed 到 `outcome_unknown`。
- 不把 task token、命令参数、源码或 Unity 结果上传到外部服务。

## 已确认设计

### Task token 托管

- `umcp workspace task start` 新增 `--token-file <path>`。CLI 先以排他创建和 owner-only 权限准备 token 文件，再写入随机 token，最后用同一 token 创建 task；因此 task 一旦存在，续接凭据已经落盘。Windows 限当前用户，POSIX 权限 `0600`；使用该选项时 JSON/stdout 不再返回明文 token。
- token 文件创建或加固失败时不创建 task；task 创建失败时删除仍匹配该 token 的文件，不留下孤儿。释放 task 后也只删除调用方明确传入且内容仍匹配该 task token 的文件。
- `unity-mcp-instance-bootstrap`、`unity-test-router` 与其脚本统一使用 owner-only token file；长命令、Domain Reload 和多次 CLI 调用复用同一文件，不再手工解析/复制 token。
- 新增无凭据的 `workspace task cleanup-idle --task-id`，但只允许清理 `active`、无 granted/queued claim、无 freeze、无 queued/running farm job、且 phase 不是 `outcome_unknown` 的 task。任何资源所有权或不确定结果都会拒绝，不能借此释放别人的锁。

### 端到端命令回执

- Supervisor 为每次 `umcp call` 生成随机 command id；同一恢复链始终复用该 id、命令类型、项目 hash 与参数 SHA-256，绝不生成第二个业务命令。
- MCP Server `/api/command` 接受 caller command id，并提供同 id 的 pending/completed/ambiguous 查询与显式 ack。重复 POST 只附着到原 pending、返回缓存结果，或把同一 envelope 再交给插件做幂等判定。
- Unity 插件在 `Library/UnityMCP/CommandReceipts` 中原子保存 bounded receipt：执行前写 `started`，得到结果后先写 `completed` 再发 WebSocket。receipt 绑定 project hash、命令名和参数 SHA-256；不保存 task token。
- 插件收到重复 id 时：相同 envelope 的 `completed` 返回原结果；`started` 返回 `ambiguous` 且不重放；envelope 不同立即拒绝。插件重连会重新发布尚未 end-to-end ack 的 receipt。
- Server 只在 HTTP 调用方已收到可信结果并 ack 后通知插件清除 receipt。未 ack receipt 有时间与数量上限；达到容量时在执行新命令前 fail closed，不驱逐未确认记录。
- 注册握手公布 receipt protocol capability。新 Supervisor 对旧插件保持现有安全行为并明确报告 `receipt-protocol-unavailable`，不伪装成已自动恢复。
- RestClient 在响应丢失后于原 command id 上做 bounded recovery：等待同项目插件重连、查询/重复提交相同 envelope、取得 completed 后 ack 并返回。只有收到 `ambiguous`、envelope 冲突、恢复预算耗尽或 receipt 持久化失败时才进入 workspace `outcome_unknown`。

### 项目与发布边界

- 这是通用 MCP transport 能力，不需要业务项目代码、Dashboard adapter 或项目内协调文件。
- 已有项目需一次性把 `com.coplaydev.unity-mcp` pin 更新到含 receipt protocol 的受审 fork commit；之后新项目只需安装该包并按现有 machine-local bootstrap 注册。
- MCP Server 同 commit 的 Python 包由 Supervisor 固定依赖；插件与 Server capability 必须匹配。POB 只改 manifest/lock 的 MCP pin，不修改 gameplay、资源或生产配置。

## 失败、兼容与回滚

- receipt 目录不可写、损坏、容量耗尽或 envelope 不一致时，命令在可证明未执行前拒绝；已开始后则保持 unknown 与原 claims。
- Server/HTTP 崩溃但 Unity 完成命令：插件重连回传 completed receipt；Unity 崩溃在 started 与 completed 之间：返回 ambiguous，不重放。
- token 文件丢失且 task 已持有资源时继续使用原 TTL/人工证据恢复；`cleanup-idle` 不能扩大权限。标准路径通过原子 token file 避免该状态。
- 回滚可恢复上一版 Supervisor/插件 pin；旧版仍保持原 fail-closed unknown，不会读取或执行 receipt。回滚前必须先无 active task/claim/unknown。

## 实现顺序

1. Supervisor 先实现 token-file 原子托管、idle cleanup 与单元/CLI 测试。
2. MCP fork 实现 protocol capability、插件 receipt journal、Server dedupe/query/ack 与故障注入测试。
3. Supervisor 接入 caller command id 和 bounded recovery；兼容旧插件 fail closed。
4. 更新两个全局 skills，完成 current-machine 与 M5 跨机安装。
5. 正常合并 fork 与 ZeroEngine PR；双机安装 Supervisor；POB 精确更新 MCP pin/lock。
6. 在合成项目与 POB 定向菜单/只读命令上验证断线恢复、无重复执行、最终 tasks/claims/queue/unknown 为零，再关闭 spec。

## 验收标准

1. `task start --token-file` 全程不输出明文 token；异常路径不留无 claim 孤儿，正常 release 删除匹配 token 文件。
2. `cleanup-idle` 能立即清理本 spec 复现的 M5 空 task，并拒绝任何 claim、farm job、freeze 或 unknown task。
3. HTTP 响应丢失、WebSocket 断开和 Server 重启三类故障中，completed 命令返回同一 receipt/result，业务 handler 计数始终为 1。
4. started 后 Unity 崩溃返回 ambiguous，handler 不被自动重放，workspace 保留 claims 等待恢复。
5. receipt envelope 冲突、损坏、容量耗尽和旧插件均 fail closed；CLI/status 不泄露 token 或命令参数。
6. Windows 与 macOS 的文件权限、重连、ack、清理和安装合同一致。
7. Supervisor 全套测试/Ruff、MCP Server Python 测试、Unity receipt EditMode 测试、skill tests/validator 与所有 PR 必需检查通过。
8. POB 更新仅含 MCP manifest/lock 目标路径；EditorSettings 与其他 pending 不纳入，最终协调状态无本任务 task/claim/unknown。

## 自审结论

本方案没有缩短安全 TTL、自动释放有资源 task，也没有按命令名字猜执行结果。token 问题通过原子保管和仅限零资源 task 的安全清理解决；命令问题通过“执行前 started、结果先落盘、端到端 ack、同 id 去重”解决。不可消除的窗口被严格收窄为 Unity 在 started 与 completed 之间自身崩溃，此时仍保持人工 unknown。方案是 transport 级通用能力，项目只承担一次 package pin 更新，不需要业务适配。

## As-Built

Supervisor 0.7.1 已实现 owner-only `--token-file`、匹配删除、零资源 `cleanup-idle`、farm 作业生命周期保护，以及带同一 command id 的 bounded receipt recovery。MCP fork 10.1.2 已实现插件 `started/completed` 原子 journal、Server 去重/查询/ack、重连补发、envelope 冲突和容量 fail-closed；动态 custom tool 的外层和子命令同样使用确定性 durable receipt。

全局 `unity-mcp-instance-bootstrap` 与 `unity-test-router` 已统一为 delegated token-file 路线；router 不再解析或回显 task token。Windows ACL 实测只有当前用户 `(F)`，macOS 使用 `0600`。M5 的真实丢失-token 空 task 在 TTL 后由标准 unregister 收口，其项目、slot 与安装临时文件已精确清理。

M5 首次安装验收发现 0.7.0 会对已存在的 POSIX token 父目录执行 `chmod 0700`，导致 `/private/tmp` 正确拒绝；失败发生在 task 创建前，无孤儿或 token 泄露。0.7.1 改为只用 `0700` 创建缺失的末级父目录，绝不改既有目录权限；排他创建失败也不删除调用方原有文件，并新增 POSIX 与既有文件回归覆盖。

实现期证据：Supervisor `172 passed in 265.29s`，Ruff check/format 通过；MCP Server `1351 passed, 3 skipped`，receipt 定向测试 9 项通过；Unity 2022.3.62f3 receipt EditMode 5/5；router 17 项与两项 skill validator 通过。MCP fork PR #3 的 4 项检查全绿并正常合并为 `a67a2f3ab447a769d08c7e5498e905dc1c347fb2`；personal-codex-skills PR #27 正常合并为 `7cb9f4aa2cdea569831a02547ae836f2f5857f36`，Windows 与 M5 已安装同一技能版本。

终审补强为此前只有实现证据、缺少直接负向覆盖的边界新增回归：`cleanup-idle` 明确拒绝 freeze、`outcome_unknown`、未完成 farm job 与 adopted pending；新 Supervisor 明确拒绝不支持 receipt protocol 的旧插件。Supervisor 最终全套为 `177 passed in 271.94s`，Ruff check/format 与 diff check 通过。MCP fork PR #4 为 Server 内存态丢失后的 completed receipt 恢复、损坏 journal 保留证据、容量耗尽不驱逐未确认 receipt 新增直接测试；Python 全套 `1352 passed, 3 skipped`，3 项 PR 检查全绿，正常合并为 `4a29cdf0681fffb216b8aca75465bfcee2bea937`。这些仅为测试与文档补强，不改变已部署的 Supervisor/Server/插件版本，也不需要更新 POB pin。

ZeroEngine 0.7.0 主功能已由 PR #34 的 11 项检查全绿后正常合并为 `75a7239bdb48de7053813ac71c71d008ec7f37c9`。0.7.1 POSIX 修复由 PR #35 正常合并为 `620804fa0adb5cf9b2cc936cd8f2ecd1b304f00f`；Supervisor/POSIX 测试和五条 Unity lane 最终全绿，其中 `Modules without Dashboard` 首轮仅因 `packages.unity.com` `ECONNRESET` 失败，失败 job 重跑后通过。Windows 9950 与 M5 均已安装 Supervisor 0.7.1、Server 10.1.2，并返回 `healthy-owned`。

M5 真实 `/tmp` token 验收为 `0600`、owner `lzq`，未改变既有父目录权限；删除 token 后 `cleanup-idle` 立即收口空 task，正常 release 返回 `token_file_removed=true`。Windows token ACL 仅当前用户 FullControl，正常 release 同样删除 token。setup 文档已收窄为实现实际保证：缺失的末级父目录请求 `0700`、token 为 `0600`，既有父目录和祖先目录权限不变，不再声称整条缺失父链都会 owner-only。

两机最终均无本任务 task、claim 或 Unity owner。POB 已把插件 pin/lock 精确提交为 Plastic `cs:16819`，changeset 仅含两目标文件；实机公布 receipt protocol v1、只读命令成功、Console 0 error、receipt ack 后 journal 为空。该次提交与联调检查时 EditorSettings 为基线 SHA-256 `4dd4fa6d…4488a` 且未纳入；终审只读复核发现其后来已有无关 `CO+CH`，当前 hash 为 `ee88f9bf…84b33`。依据配置安全规则，本任务未回滚、未提交该文件；它不影响 `cs:16819` 的两文件范围，但作为已知残留明确保留。全部验收门已有直接证据，本 spec 关闭。
