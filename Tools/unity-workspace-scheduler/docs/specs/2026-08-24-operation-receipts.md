# Scheduler Durable Operation Receipts

- 状态：Implementation in progress
- 最后更新：2026-08-24
- 基线：`b320dcf0dbb3021f647a98a043b0d8f6e9b337fb` 上的 Scheduler 1.3 候选 dirty diff
- 设计批准：已由当前 Router/Scheduler 长期加固任务授权
- 执行授权：Authorized；限 `Tools/unity-workspace-scheduler/**` 的本地实现与验证
- 提交、安装、发布：N/A；本任务不执行

## 目标与非目标

为所有公开 Scheduler 状态变更提供跨进程、跨 stdout 丢失的 exactly-once mutation identity：同一高熵 operation ID 与同一规范请求只返回已持久化结果，不再次改变 task、claim、queue、epoch、heartbeat、park 或 recovery 状态；同 ID 的任何异参或异 owner 请求都 fail closed。

同时提供无副作用的 `task identify`，使 Router 能先取得唯一 task ID，再按 project+task ID 加 OS 锁并在锁内 heartbeat/assert。Scheduler 仍不启动或检查 Unity，也不替 Router 管理进程锁、executor 或派发。

## 已确认设计与不变量

1. 协议和数据库 schema 升为 3；包版本升为尚未发布的 1.4.0 候选。
2. 所有公开 mutation 强制 canonical lowercase UUIDv4 `--operation-id`。格式可承载 122 位随机性；调用方必须用 CSPRNG 生成。Scheduler 能验证格式，不能证明随机来源。
3. `operation_receipts` 以 operation ID 为全局主键，不引用 workspace/task 外键，因此 task 历史裁剪和 workspace unregister 不删除 receipt。
4. fingerprint 是 workspace identity、稳定 action 名、规范参数 JSON 与适用的 owner token SHA-256 的规范 JSON SHA-256。receipt 保存规范参数、owner identity、result、创建/终结时间、可选 token cleanup 路径和 delivered 时间。
5. mutator 在 `BEGIN IMMEDIATE` 内、任何 maintain/auth/业务写之前读取 receipt。同 ID 同 fingerprint 直接返回 stored result，标记 `replayed=true`；不执行 maintain。不存在时才执行原 mutation，并在同一事务插入 receipt。
6. 同 ID 并发调用由 SQLite 写事务串行化，只能产生一个 task/claim/freeze、一个 queue order 和一个 receipt。
7. acquire/park 首次副作用与 pending receipt 同事务提交，并固定第一次的 task/claim/freeze identity。pending receipt 不能 `receipt-only` 交付或 ack；同 ID normal retry 只续等/终结同一 identity。总等待截止时间不晚于首次 `created_at + requested_wait`，也不晚于本次 effective wait；终结用 CAS，不能被并发调用覆盖。
8. 每个成功 mutation result 都包含稳定 `operation`：`operation_id`、`fingerprint`、`delivery_digest`、`replayed`、`delivered`、`finalized`。`delivery_digest` 绑定持久化 result 与当前 terminal proof，不绑定 replay/delivery 诊断字段。
9. `receipt ack --operation-id --fingerprint --delivery-digest` 不需要新的 operation ID，且自身幂等。首次 ACK 在同一写事务内校验调用方实际 flush 的 digest 再标记 delivered；proof 在读取与 ACK 间变化时必须 fail closed。它还完成该 receipt 指定的 token 清理；completed/failed release 的 cleanup 成功后，可因果收敛该 exact task 更早的 finalized lifecycle receipt。
10. 正常 completed/failed task release 不在 mutation 回包前删除 token。receipt 保存经验证的规范 token 路径和 token hash；ack 先提交 delivered，再通过当前 handle/ACL/identity 检查读取并精确匹配删除。ack 在任一点中断都可重试；cleanup 失败返回 `recovery_required`，不回滚已提交的 terminal task 或 delivered receipt。`outcome_unknown` 不删 token。
11. pending 与 cleanup-pending receipt 永不自动 GC。finalized lifecycle receipt 只有在 durable task terminal fence 已证明它不再授权时才可安全 retired；带物理 token 的 task.start 在 exact cleanup 完成前仍受保护。recovery 不覆写 unresolved wait 的历史 result，而追加独立 resolution proof。retired/delivered 的可裁剪历史全局保留最新 10,000 条，replay-required 与 token cleanup obligation 另受硬容量约束。
12. `task identify` 读取现有安全 token 文件并查询唯一 DB-open task；不收 operation ID，不调用 maintain，不续租、不调度、不改 epoch。它只提供 identity，绝不替代锁内 heartbeat 和 exact claim assert。

## 接口与状态流

以下命令新增必填 `--operation-id`，并支持不进入 fingerprint 的 `--receipt-only`：workspace register/unregister；task start/heartbeat/park/release；claim acquire/release；queue cancel；freeze acquire；recovery resolve。`task park`、`claim acquire`、`freeze acquire` 另接受 Router 保存的原始 `--requested-wait`；fingerprint 绑定它，`--wait` 只表示锁耗时后的 effective wait，且不得大于 requested wait。

新增：

```text
unity-scheduler task identify --workspace <root> --token-file <path>
unity-scheduler receipt ack --operation-id <uuid-v4> --fingerprint <sha256> --delivery-digest <sha256>
```

task start 首次调用独占创建 token；同 operation ID 重试复用已存在且通过安全检查的 token 文件。正常 task release 的 token 生命周期变为 mutation receipt 未 ack 前保留、ack 后精确删除。

## 兼容、迁移、失败与恢复

- 新库直接创建 schema 3。schema 1 继续拒绝含 queued/parked 的歧义状态；安全 schema 1 和全部通过语义校验的 schema 2 在一个 rollback-journal 事务内迁移到 schema 3，再启用 WAL。
- schema 1/2 迁移只新增空 receipt 表和索引，不猜测历史 operation。任何校验、DDL 或 receipt schema 失败都回滚且保持原 DB bytes、journal mode 和 schema。
- `state verify/backup/restore` 识别 schema 1、2、3；schema 3 额外验证 operation ID、action、canonical JSON、fingerprint、result、时间、token cleanup 约束和 delivered/unacked 计数。
- stdout 丢失后 Router 先用原 operation ID、原参数与 `--receipt-only` 探测；final receipt 直接返回，缺失以 `operation-receipt-missing` fail closed 且不 maintain，pending 以 `operation-in-progress` 返回且不能 ack。缺失时才执行 task identify、task-ID 锁、锁内 heartbeat/assert 与 normal mutation。异参、异 workspace、异 action、异 owner token 均返回结构化 `operation-id-conflict`，不产生任何业务写。
- ack stdout 丢失后用同一 digest 重放 ack；已 delivered 的 ACK 幂等返回。未 delivered 的 task.start cleanup-pending receipt 拒绝 ACK，由正常 cleanup/replay 路径先生成可交付 terminal proof。任何 token cleanup 都不得根据 mtime/age 删除。

## 实现范围与顺序

- `src/unity_workspace_scheduler/state.py`：schema 3 与 1/2→3 迁移。
- `coordinator.py`：receipt/fingerprint 原语、全部 mutator、ack、identify、receipt GC。
- `cli.py` 与 `__init__.py`：协议 3、强制参数、start token reuse、release/ack token 生命周期、identify。
- `state_ops.py`：三版 schema/semantic/backup/restore 验证。
- `tests/**`：并发、stdout-loss 重放、冲突、token、park/freeze generation、迁移与离线状态矩阵。
- `README.md`、`docs/setup.md`、架构测试：Router-private 合同与恢复 runbook。

## 验证与验收

1. 每个列出的 mutation 缺少或使用非 canonical UUIDv4 operation ID 时，在任何状态写前失败。
2. 每个 mutation 首次调用与同 ID 同请求重放返回相同 durable entity/result；第二次的 `replayed=true`，DB 业务行、queue order、epoch、heartbeat、park marker 和 recovery event 不增加或重写。
3. 同 ID 的参数、workspace、action 或 owner token 任一变化均以 `operation-id-conflict` 失败且 DB 不变。
4. 至少两个并发同 ID acquire/freeze/start 调用只创建一个 receipt 和一个业务实体。
5. acquire/freeze 回包丢失模拟后重放不产生重复 active/queued claim；park 重放不会改绑新 freeze；并发 retry、pending 提前 ack、首次进程在 side effect 后崩溃都能在首次绝对 deadline 内收敛到一个 final receipt。
6. completed/failed release 回包丢失时 token 保留且 mutation 可重放；ack 后 exact token 被删除；ack/cleanup 中断可重试；mismatch 保留证据并报 recovery required。
7. workspace unregister、terminal task 裁剪后未 ack receipt 仍可重放；只有 delivered receipt 可被容量裁剪。completed/failed release ACK 只有在 token cleanup 成功后才收敛同 task 的旧 finalized start/heartbeat receipt，失败时必须原样保留。
8. schema 1 和 schema 2 正常迁移；每版畸形/冲突输入 fail closed，拒绝时 DB bytes、journal 与 schema 不变；schema 3 inspect 验证 receipt 语义。
9. `task identify` 返回唯一 open task，且前后 heartbeat、expiry、epoch、claims 完全不变；随后 heartbeat/assert 仍独立决定授权。
10. README/setup、CLI help、JSON protocol 与实现一致；Scheduler 全套 pytest、Ruff format/check、`git diff --check` 全绿。
