# ZeroEngine 开发内容适配

| 映射 | 项目约定 |
| --- | --- |
| 项目 ID | `zeroengine`，对应仓库 `ZeroGameStudio-CN/zeroengine`，不使用本机目录作为身份。 |
| 唯一登记权威 | [development-content-plan.json](development-content-plan.json)，项目内 JSON；未配置外部内容记录服务。 |
| 格式／状态 | `development-content` portable registry schema v1；采用原始 wire tokens。没有已确认发布计划的条目保持空 `targetReleaseId`；候选 package.json 版本单独记录。 |
| 预览／试用 | 仓库本身不是运行中的 Unity 项目。条目指向包内资源与测试；需在明确的消费者项目沿其现有 Unity 路由进行受控包验证，打开登记不启用候选。 |
| 发布证据 | `releaseEvidence` 记录实际包发布渠道与来源；消费接入另有项目 manifest／lock 与实际加载来源证据。仅有分支、测试或源码提交不建立消费端可用性。 |
| 写入／检查 | 任务只更新所属条目，保留未知扩展和其他条目；基线复核后原子写入并读回。校验共享 schema、唯一身份和版本引用，不额外增加执行队列或构建门。 |

首批记录只覆盖当前 R38 的养成与关系包候选，不冒充全仓库历史清单；P5 使用稳定 `p5` 项目 ID 关联自己的消费记录。后续统一服务须显式迁移权威，不能与此文件分别维护进度。
