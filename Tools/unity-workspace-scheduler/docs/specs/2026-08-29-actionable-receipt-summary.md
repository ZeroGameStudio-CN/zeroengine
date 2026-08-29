# Scheduler Actionable Receipt Summary

- 状态：Implemented and published
- 最后更新：2026-08-29
- 基线：`a1b519198500ed432525cae46036f189a908a592`（Scheduler 1.4.1）
- 设计批准：已由当前 Router/Scheduler 长期稳定性修复任务授权
- 执行授权：Authorized；限 `Tools/unity-workspace-scheduler/**` 的实现、测试与文档

## 问题

Scheduler 1.4.1 的只读 `maintenance history` 将所有 `finalized_at IS NOT NULL`
且 `delivered_at IS NULL` 的回执计入 `finalized_undelivered`。任务终结时，等待中的
`claim.acquire`、`freeze.acquire`、`task.park` 以及其他已被 durable terminal fence
证明不再授权工作的生命周期回执，会在未伪造 ACK 的前提下安全写入 `retired_at`。
这些回执是有界审计历史，不再需要 Router 重放或确认交付，却仍被旧指标报告为回执债务。

## 决策与不变量

1. `receipt_summary.finalized_undelivered` 只统计同时满足以下条件的可操作回执：
   `finalized_at IS NOT NULL`、`delivered_at IS NULL`、`retired_at IS NULL`。
2. `retired_at IS NOT NULL` 的回执不计入该指标。它们仍保留在既有有界历史中，既不被
   伪造 ACK，也不由 history 查询删除或改写。
3. `pending` 和 `cleanup_pending` 的既有语义不变；token cleanup obligation 仍独立受保护。
4. `maintenance history` 继续完全只读，不运行 maintenance、receipt GC、token cleanup、
   lease renewal 或任务状态转换。
5. JSON 字段、protocol 3 和 schema 3 均不变。修复以兼容补丁版本 1.4.2 发布；Router
   现有 `>=1.4.1,<2.0.0` 协议范围可直接接收。

## 非目标

- 不新增后台进程、守护服务或数据库迁移。
- 不自动 ACK、删除或重新投递任何回执。
- 不掩盖真正 finalized、undelivered、unretired 的回执债务。
- 不修改 Scheduler 的任务、claim、queue、freeze、TTL 或清理生命周期。

## 验证与验收

1. 正常生命周期生成至少一个 finalized、undelivered、retired 回执后，history 指标不计入它。
2. 同一状态库中保留至少一个 finalized、undelivered、unretired 回执，history 指标继续精确计入。
3. history 调用前后所有 Scheduler 表逐行相同。
4. Scheduler 全套 pytest、Ruff format/check、`git diff --check` 通过。
5. Router 协议金丝雀接受 Scheduler 1.4.2；真实 POB history 的该指标从误报 1 收敛为 0，
   同时 status、watchdog、task、claim、cleanup 和 fence 状态保持安全。

## 实现验证

- Scheduler：394 passed、12 个平台限定 skipped；Ruff check/format、uv lock 和 diff check 通过。
- Router：隔离 Scheduler 1.4.2 协议金丝雀随完整 303 项套件通过。
- 发布：Scheduler 合并提交 `fca51c349f9d30cfb199df44d1fbc595d75249c1`，Router 合并提交
  `31c083d6a49075970ca93e8d7c4ef1a81d109acc`；Windows 与 M5 均从这两个权威提交安装。
- 真实验收：两机 POB history 均为 `pending=0`、`finalized_undelivered=0`、
  `cleanup_pending=0`；Windows POB 与同时工作的 ZGSProject_5 均保持 ready、无 blocker、
  fence 或 cleanup job，canonical Scheduler 1.4.2 协议金丝雀通过。
