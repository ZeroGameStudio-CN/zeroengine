# Unity 隔离测试调度器

- 状态：Implemented（本地实现与代理验证完成；提交、发布、安装和跨机验收待授权）
- 最后更新：2026-08-11
- 基线：ZeroEngine `3b63c7dbe2c68298d862c1e0ac243c5ea6afc227`
- 设计批准：已批准；用户于 2026-08-10 要求“自审修订后开干”
- 执行授权：Authorized；实现并完成本地验证
- 终态操作授权：Authorized；用户于 2026-08-11 要求“继续下一轮，做到毕业为止”，覆盖本 spec 的精确提交、正常 PR/合并、Supervisor 安装和个人 skill 跨开发机发布

## 目标与非目标

目标是在不放宽主 Unity 工作区安全锁的前提下，让多个 Agent 对同一项目提交的独立、定向 Unity 测试在隔离工作区并行执行。主工作区继续只有一个 `unity-live` owner；吞吐提升来自每个测试作业独占一个项目快照、Library 和 Unity 进程。

非目标：

- 不在同一 Unity Editor、AssetDatabase 或 Library 内并发测试。
- 不替代项目测试范围规则、`unity-test-router`、正式 CI 或发布构建。
- 首版不做跨机器分布式调度、自动拆分单个测试、Dashboard UI 或全量测试默认化。
- 不为测试修改 Unity 项目、项目内协调配置、生产配置或 SCM pending。

## 现状与目标行为

`umcp` 0.5.0 已提供机器级零代码项目注册、workspace task、路径 claim、`unity-live`、`vcs-maintenance`、freeze 和故障恢复。同一项目的实时 Unity 操作正确地由 `unity-live` 串行；不同项目路径可并行。

当前 `unity-test-router` 对关闭项目的一次性测试仍描述旧的 legacy lease 流程，与 `required` workspace 策略不一致，必须先修复。ZeroEngine GitHub CI 已用五个独立 `TestProject` lane 并行证明隔离项目路线可行，但该 CI 强制冷 Library，目标是确定性而非本机 Agent 反馈吞吐。

目标行为：Agent 在主项目 task 下提交精确测试，调度器从当前 VCS 基线和该 task 自有变更生成不可变快照，排入机器级 FIFO 队列，在预配置的隔离 slot 中运行。多个 slot 可并发；主项目文件、Editor、Library、pending 和租约均不受测试进程触碰。

## 已确认设计

### 边界与所有权

- `umcp` 继续拥有注册、身份、task、claim 和故障隔离；测试调度代码放在独立模块和独立 `test-farm.sqlite3`，不扩张 `workspace-control.sqlite3` 的职责。
- `unity-test-router` 继续决定一次性或实时 Editor 路线、项目 wrapper 和测试范围；隔离调度器只负责快照、排队、slot、进程和结果。
- 主项目的交互式 Editor 即使打开也不阻止隔离 slot 测试，因为 slot 是不同的 canonical project root。主项目本身仍禁止第二个 Editor 或 batchmode。
- 首版为机器本地调度。9950 与 M5 各自拥有独立队列和 slot；跨机器派发留给后续 CI adapter。

### 机器配置

- 通过 `umcp test farm provision --workers <N> [--slot-root <path>]` 一次性配置机器容量；状态只写 Supervisor 私有目录或明确的机器级 slot root。
- 不猜测安全并发数。初始 worker 数由操作者在 Unity 许可、可用内存和代表性测试探测后设置；项目无需配置。
- 每个持久 slot 拥有不同的项目目录、Library、日志目录和结果目录；worker 进程按队列批次启动并退出。不同 slot 不共享可写 Library、Temp、Logs、测试 XML或端口。
- Unity 2022.3 没有官方 Editor 参数可按 project root 重定向 `Application.persistentDataPath`；Windows Editor 默认按 OS 用户 + CompanyName/ProductName 定位。首版因此只并行经源码/合同证明不会访问 `Application.persistentDataPath`、PlayerPrefs、固定端口、固定设备或其他进程外共享可写状态的 scope。不能证明时 fail closed 到串行路线；不得仅凭不同 slot 假称外部状态已隔离。
- Unity 可执行文件由 `ProjectVersion.txt` 和现有 router 解析；找不到精确版本时作业阻塞，不使用近似版本。

### 提交接口

首版提供：

```text
umcp test submit --project <root> --platform EditMode \
  [--test-filter <name>] [--category <name>] [--assembly <name>] \
  [--overlay-path <task-owned-path>|--baseline-only] \
  --external-state-safe \
  [--wait] [--token-file <path>|--token-stdin]
umcp test status --job <job-id>
umcp test wait --job <job-id>
umcp test cancel --job <job-id> --token-file <path>|--token-stdin
umcp test farm status
umcp test farm watch
```

提交要求有效的 `required` workspace policy、活动 task token、至少一个精确测试过滤条件，以及 `--baseline-only` 或一组被该 task 已授予 write claim 覆盖的精确 `--overlay-path`。Git 没有 Plastic disposition，不能仅凭目录 claim 推断其下全部旧 dirty 都属于 task；Git/Plastic 均只打包调用方明确声明且 SCM 当前确认 pending 的精确路径，move/delete 和 `.meta` 也必须逐项列出。并行路线还要求调用方基于测试源码/合同明确声明 `--external-state-safe`；未声明时返回现有安全串行路线。默认禁止 Run All。返回值包含 job id、snapshot id、队列位置和 artifact root，不返回 task token、私有 lease 或 slot 内部凭据。

### 快照合同

1. 调度器读取主工作区 VCS 类型、基线 revision、Unity 版本、Packages manifest/lock 指纹和 task 的 granted write claims。
2. Git 与 Plastic adapter 分别创建或复用位于 slot root 下的干净、VCS 原生 workspace，并定位到同一基线 revision。
3. 调度器只把调用方精确声明、当前 VCS 确认为 pending 且被 task write claim 覆盖的 changed、added、moved、deleted 和 private 路径覆盖到 slot；不从目录 claim 自动推断所有权，并强制显式包含 move 两端和 Unity `.meta` 配对。
4. 其他 task、legacy-unowned 或 protected pending 不进入快照。无法证明所有权、忽略文件、外部可变 `file:` 依赖、未完成 merge/incoming changes 或 VCS 状态不一致时拒绝作业并回退到主工作区串行路线。
5. 覆盖前后记录路径、状态、大小和 SHA-256；复制期间源指纹变化则废弃并重试一次，第二次变化即报告 busy。快照完成后不再读取主工作区文件；Git slot 可从源仓库对象库读取已锁定 revision，Plastic slot 可从服务器取得该 changeset。
6. 删除使用快照 manifest 中的 tombstone 表达，不通过宽泛目录清理推断。

该快照验证 task 自身相对基线的行为，不声称验证与其他本地 pending 的集成。提交或发布前仍由项目规则决定是否需要主工作区或集成 revision 的最终门禁。

### 调度、执行和缓存

- 队列按提交顺序 FIFO；空闲 worker 原子领取一个 queued job。取消只能由原 task token 或明确维护操作执行。
- 每个 slot 同时最多一个 Unity 进程。主项目 `unity-live` 不授予 slot；worker 使用内部 job ownership，且不能调用主项目 MCP。
- router 生成精确 Unity 调用；结果必须解析 XML/summary，不能只相信 Unity 退出码。
- slot 的 Library 可跨作业增量复用，但项目内容每次先恢复到目标基线再应用快照。slot reset、VCS clean 或快照指纹失败会将 slot 标记为 quarantined，并在隔离目录内重建；绝不清理主项目。
- 每个 slot 首次遇到一组新的基线 revision、关键输入和 overlay 指纹时，同一输入各运行一次 cold 与 warm，要求测试集合、通过/失败和基础设施结论一致；不一致则禁用该输入的 warm reuse 并 quarantine slot。
- 记录 queue wait、materialize、import/compile、test、cleanup、cache hit、峰值内存、Unity 版本、测试数量和基础设施错误，供后续容量调整；不上传源码或 token。

### 结果与生产配置安全

- artifact 位于主项目之外，至少包含 snapshot manifest、调用摘要、Unity log、测试 XML、标准化 summary 和 slot mutation report。
- slot 在测试前后运行 VCS status/diff；除 router 明确声明的临时输出外，任何管理路径变化都使作业基础设施失败并 quarantine slot。主项目复用协调器的 SCM observation，只对 task overlay、ProjectVersion、manifest/lock 和项目规则指定的关键输入做强哈希；禁止为每个 job 全量哈希整个主项目。
- Console、编译、测试失败与基础设施失败分开报告。超时、崩溃、许可失败和磁盘不足不得伪装成测试失败。
- `outcome_unknown` 只用于已派发且无法证明结果的 job。该 slot 在恢复或维护裁决前不可复用，但不冻结主项目。

## 兼容、失败与回滚

- 未 provision、容量为一、VCS adapter 不支持、快照不安全或 wrapper 不兼容时，router 返回现有安全串行路线；不得静默跳过测试或改用主项目并发 batchmode。
- Git、Plastic、Windows 和 macOS 使用同一 job/snapshot schema；平台差异封装在 VCS、Unity executable 和进程 adapter。
- 现有 `umcp workspace`、`unity-live`、legacy lease 和项目内策略接口保持兼容。
- 停用测试 farm 只需停止接受新作业、等待或取消 task-owned job，并把 router 切回串行路线。slot 和测试数据库均为机器私有可重建状态；回滚不改项目。
- 对 task 外路径的写入、无法归属的 SCM pending、主项目指纹变化、slot 越界路径或 token 泄露均为立即停止条件。

## 影响范围

ZeroEngine：

- `Tools/unity-mcp-supervisor/src/unity_mcp_supervisor/cli.py`
- `Tools/unity-mcp-supervisor/src/unity_mcp_supervisor/service_state.py`
- 新增独立 test farm、snapshot、VCS adapter 和 worker 模块
- 对应 unit/integration tests、README 与 setup 文档

个人 skills 源仓库：

- `skills/unity-test-router/SKILL.md`
- `skills/unity-test-router/scripts/unity_test_router.py`
- `skills/unity-test-router/tests/test_unity_test_router.py`

POB 和其他 Unity 项目无需文件、package、AGENTS 或 adapter 变更。

## 实现顺序

1. 先把 `unity-test-router` 的关闭项目路线改为 required workspace task/claim，并保持现有串行测试可用。
2. 在 Supervisor 中实现 job/slot 状态机、FIFO、token 授权和 fake-process 测试，不启动 Unity。
3. 实现 Git/Plastic snapshot adapter、所有权证明、`.meta`、move/delete 和指纹保护。
4. 接入 router 与真实 Unity batchmode，完成结果归类、mutation guard、quarantine 和串行回退。
5. 在临时合成 Unity 项目验证两 worker 并发，再对一个真实项目运行最窄代表性测试；不得先在 POB 生产工作区试错。
6. 更新两端个人 skill、安装并回读；ZeroEngine 通过正常 PR/检查后再按测试提交安装 Supervisor。

## 验证

- Supervisor：`uv run pytest -q`、`uv run ruff check src tests`、`uv run ruff format --check src tests`、`git diff --check`。
- Skill：系统 quick validator、router unit tests、Windows installer、macOS installer-policy 和双机安装回读。
- 合成项目：覆盖 Git/Plastic、add/change/move/delete/private、`.meta`、并发队列、取消、超时、崩溃、许可失败、mutation、quarantine 和 cold/warm 一致性。
- 真实 Unity：两个精确、相互独立的 EditMode 作业在两个 slot 中时间重叠；用同一 warm slot 串行运行相同作业作为基线，并要求并行 makespan 更短且结论一致。
- 主项目：运行前后 SCM observation、task overlay 与关键输入指纹相同，已有 Editor 连接、task、claims、pending 和 production config 不变。

## 验收标准

1. 任意已 bootstrap 的 Git 或 Plastic Unity 项目无需项目改动即可提交精确隔离测试。
2. 同一主项目的两个 task 可在两个 slot 同时运行 Unity 测试，且 worker 时间区间发生重叠。
3. 主项目打开的 Editor、`unity-live` owner、Library、SCM observation、task overlay 和关键输入指纹在并发测试前后不变。
4. 每个 job 只包含基线 revision 与其 task-owned overlay；其他 pending 的内容和路径均不出现在 snapshot manifest。
5. add/change/move/delete/private 与 Unity `.meta` 在 Git、Plastic adapter 中均被确定性复现；模糊或未归属状态 fail closed。
6. 两个 worker 的项目根、Library、Temp、日志和结果均不共享可写路径；会访问 Unity 全局 Player 数据或其他进程外共享可写状态的 scope 不进入首版并行路线。
7. FIFO、容量限制、owner cancel、超时和崩溃不会丢 job、越权终止其他 job 或占用主项目资源。
8. warm 与 cold 对同一快照给出相同测试集合和结论；污染或项目文件 mutation 会失败并 quarantine slot。
9. 并行代表性作业的 makespan 小于相同 warm 作业串行 makespan，且不减少测试数量或验证强度。
10. 未 provision、adapter/wrapper 不支持或安全证明失败时，系统明确返回串行路线及原因，不并发启动主项目 Unity。
11. 全部测试入口使用 workspace task/token/claims；required 项目不再依赖裸 legacy lease。
12. CLI/status/artifact 不泄露 task token、lease id、凭据或主项目私有内容。
13. Windows 与 macOS 的安装、状态、作业运行和清理合同一致。
14. Supervisor 全套测试、格式检查、两端 skill 测试和 ZeroEngine 必需 PR checks 全部通过。

## 自审结论

本方案把安全不变量与吞吐优化分层：主工作区继续串行，测试通过隔离 project root 并行；没有要求项目适配，也没有把不安全的并发塞进 MCP Editor 通道。最大风险集中在“task-owned overlay 的 VCS 证明”和“warm Library 污染”，已分别用 fail-closed snapshot manifest 与 cold/warm 等价门处理。自审还将主项目全量哈希收窄为 SCM observation、task overlay 与关键输入强哈希，避免安全检查抵消吞吐收益。设计与本地执行已获用户授权，Graduation Gate 通过。

## As-Built（2026-08-11）

已实现 Supervisor 0.6.0 的机器私有 `test-farm.sqlite3`、FIFO job/slot 状态机、`umcp test`/`test farm` CLI、Git/Plastic snapshot adapter、批处理 worker、XML 结果归一化、取消/超时/崩溃隔离、mutation guard、输入绑定的 cold/warm 认证和 quarantine 重建。workspace unregister 会阻止仍 queued/running 的 farm job；原 task token 即使 task 已正常结束或过期，也只能取消该 task 自己尚未终态的 job，避免孤儿队列而不恢复其他权限。

`unity-test-router` 已兼容 required workspace task/`unity-live`，并在调用方明确证明 external-state-safe、项目为 required 且本机 farm 已 provision 时，为 one-shot 返回 `isolated`；主 Editor 是否打开不影响该隔离路线。项目 wrapper 未声明兼容、外部状态不安全、farm 不可用或 snapshot 无法证明时继续使用现有串行路线。

自审新增的 fail-closed 修订包括：拒绝 symlink overlay、复用 slot 前校验 Git/Plastic 仓库身份、运行中禁止迁移 slot root、创建 snapshot 前后复核关键输入、无过滤条件时不创建 artifact、缓存键纳入 revision/关键输入/overlay、以及只在明确 quarantine 后由 `farm provision` 重建受控 slot 目录。

本地证据：

- Supervisor：`164 passed in 229.16s`；Ruff check 与 format check 通过；仓库 diff check 通过。
- Router skill：`16 tests` 通过，系统 quick validator 返回 `Skill is valid!`，diff check 通过。
- Windows Unity 2022.3 合成 Git 项目：最新版双 slot cold/warm 认证均 `passed`、每 lane 1/1、时间区间重叠；随后 warm 并发 14.045 秒、相同 warm 串行 24.044 秒，结论与测试数一致，并发 makespan 缩短 41.6%。主 Git 项目保持基线 `e69722b3c0ed4d1fd8f0f1f75929faad9958722d` 且 clean。
- Plastic 的 status/add/change/move/delete/private 解析、仓库身份校验、switch/reset 和 overlay 应用由隔离 adapter 测试覆盖；未在 POB 或其他生产 Plastic workspace 试跑。
- 合成项目的全部 registration 已经标准 unregister；确认 farm queue/running、claims 和 Unity owner 均为 0 后，12 个临时 project/state/slot/token 路径已移入 Windows 回收站，Temp 中同前缀残留为 0。

本轮终态操作已获授权但尚未执行：两个源仓库仍未 commit、push、建 PR、合并、安装或跨机发布；macOS 实机、生产项目“主 Editor 打开时并发”以及正式 Plastic workspace 端到端验收随提交流程完成。POB 文件、Plastic 状态和 Unity 均未被本任务触碰。
