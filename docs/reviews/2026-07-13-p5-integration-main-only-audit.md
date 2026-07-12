# P5 ZeroEngine Integration Main-Only Audit

## 审计范围

本审计只回答：相对 P5 当前统一 pin `a61838aaa825fc01db1412302bb45f8d63eb09c3`，`origin/main` 独有的提交是否应进入本次框架稳定化快照。它不授权直接 merge 或 rebase `origin/main`。

2026-07-13 在执行 `git fetch origin main` 后记录的不可变证据：

- `origin/main`: `49262ca76e325c7addd94aa2c2954a9ff64dc785`
- P5 baseline: `a61838aaa825fc01db1412302bb45f8d63eb09c3`
- merge base: `007db4ea2c47e1fa0e2ed1cd1851aee2aeff2ec4`
- `git rev-list --left-right --count origin/main...a61838a...`: `17 62`，即 main-only 17 个、P5 baseline-only 62 个
- P5 当前消费 21 个 `com.zerogamestudio.zeroengine.*` 包，不消费 `com.zerogamestudio.analytics`，也不消费旧 monolith `com.zerogamestudio.zeroengine`

状态含义：

- `integrate`：属于本次快照，必须带入。
- `superseded`：P5 baseline 已有对应能力的独立或后续 lineage，不重复 cherry-pick。
- `defer`：与本次 Input kernel 目标无关，或缺少已确认的 P5 需求，另开任务评估。

## 逐提交结论

| Commit | Subject | 状态 | 结论 |
| --- | --- | --- | --- |
| `49262ca76e325c7addd94aa2c2954a9ff64dc785` | feat(analytics): add background feedback retry | defer | P5 不消费 Analytics；后台重试不属于本次 Input 快照。 |
| `681f90ecd6dbb7ef54315dc339ef45d0d92a3947` | chore(analytics): release 1.3.0 | defer | Analytics 发布提交，P5 当前依赖集合不含该包。 |
| `33366f7ba06f8580c0d4073501f84582b1eb8ee7` | fix(analytics): route feedback uploads by app id | defer | P5 不消费 Analytics；反馈上传路由不属于本次范围。 |
| `7d6b5266ee603fd8ee7a27f522f8e5037c7979b6` | fix(analytics): allow repeated feedback uploads | defer | P5 不消费 Analytics；不为未接入能力扩大集成面。 |
| `d7f5a634e13eec849eabb03d00b0e3fb77cfc041` | fix(analytics): bound feedback zip attachments | defer | P5 不消费 Analytics；附件约束应随未来 Analytics 接入单独验证。 |
| `ad36c9d9c7386180f7b9ad221c97ba0161ad4b4c` | Add ZeroEngine DLC foundation package | superseded | P5 baseline 已包含并消费 DLC lineage；当前对应提交为 `2ab377ae094941bfd3f731ca2523ed3e6cebe62d`，不能用 main 的旧 foundation 覆盖。 |
| `fdf04399009a45ae8e893ffb02f638152846dcf6` | Annotate service registry nullability | defer | P5 baseline 已有实际消费的 ServiceRegistry；该提交只改注解，若出现明确 nullability 问题再按当前源逐行比较，不整体摘取。 |
| `a1044509d052d3f074669cb6555e14f2b13c23c2` | Guard legacy ModSystem Steam copy on Android | defer | 属于旧 monolithic ModSystem / Android 路径；P5 的 21 包集合不消费该路径。 |
| `6bcfcde763f7558b7e69cee417f70c8e266c2a59` | Add core service registry | superseded | P5 baseline 已通过 `d9ef665f7754a61c55e79dc3a954deed8bb511a6` 拥有 ServiceRegistry、测试和后续包边界能力。 |
| `26509edbfee8971e2e175a28d26d737380bc9771` | docs: clarify zeroengine consumer setup | defer | 纯消费端说明，不影响本次 canonical runtime snapshot；确认仍准确后可单独移植。 |
| `2a6a59134e74792eaf1d87f828d19b5d5b33a730` | Guard ModSystem Steam code for Android builds | defer | P5 不消费 monolithic ModSystem；Android 条件编译另开接入任务。 |
| `cbf67d4b641e4c678737d7968ef87cd637032157` | Guard ModSystem Steam assembly from Android builds | defer | P5 不消费 monolithic ModSystem assembly，不扩大本次验证范围。 |
| `85170e9a0d363a08ace7c8e3650a83dc59277d45` | Remove ModSystem hidden documentation metas | defer | 旧 ModSystem 文档元数据清理与 P5 当前包集合无关。 |
| `669861f767816edc59ea3c6d31905a8d19803fba` | Flatten ModSystem runtime assembly | defer | 旧 monolithic ModSystem 程序集重构不属于本次 Input 恢复。 |
| `2d192d7b5595422095f818899e317ee01b184e36` | Add ZeroEngine ModSystem package | defer | P5 未消费该 monolithic package；如有 Mod 需求需先做独立包治理设计。 |
| `ab651ab14d54f3d22d507a3f03a5506295ea2e00` | Merge branch 'feature/zeroengine-tce-foundation' | superseded | P5 baseline 已包含并消费独立 TCE lineage；merge commit 不应跨分支摘取。 |
| `6e5e252106d768a1d40318f6e845ed46497b8613` | feat(tce): add generic ZeroEngine TCE package | superseded | P5 baseline 的对应 TCE 发布 lineage 为 `12b65de707fbd8be46e144c5942caaa46ededb1f`，不回退到 main foundation。 |

## 集成决定

17 个 main-only commit 均已覆盖：5 个 Analytics、6 个 ModSystem 与 1 个消费端文档提交 defer；DLC、ServiceRegistry 和 TCE 的 foundation 能力由 P5 baseline lineage supersede；ServiceRegistry nullability 注解保留为按需源比较。没有任何 main-only commit 需要进入本次集成。

本次唯一框架能力恢复继续限定为 Input graduation commit `56a4a0e5ec61e8d30931ccfc13fc48f50ddce63f`，并通过独立 capability guard 与 CI test discovery 约束防止再次退化。
