# ZeroEngine Extraction Core

面向撤离搜打玩法的通用纯逻辑包，负责物品网格、所有权、Raid 会话、掉落、回收、经济和结算等领域规则。

## 边界

- 包内可以保存通用 DTO、确定性规则、事务合同与纯逻辑服务。
- 包内不得引用 POB 的 ScriptableObject、玩家组件、存档槽、场景 actor、UI、Addressables 或 Console。
- POB 的配置解析、角色投影、共享钱包、场景绑定和表现留在 POB adapter。
- 现有 `POB.Extraction.Core` assembly 名在迁移阶段保留，避免无关的引用破坏；若后续改名，必须单独提供兼容迁移。

## 安装

消费者使用经过测试的 Git commit pin，不使用本机 `file:` 路径：

```json
"com.zerogamestudio.zeroengine.extraction": "https://github.com/liuzqk/zeroengine.git?path=com.zerogamestudio.zeroengine.extraction#<tested-commit>"
```

## 验证

- 包级合同测试位于 `Tests/Editor`。
- 通用行为先在包内测试；消费者项目随后运行自己的 adapter 回归测试。
- 发布前检查包内不存在 `POB` 项目路径、Unity 场景/Prefab 或本机绝对路径依赖。

## 迁移基线

- 来源：POB Plastic `cs:16334` 的 `Packages/com.zerogamestudio.zeroengine.extraction`。
- 原始文件数：183。
- 原始总字节数：213273。
- 原始稳定树 SHA-256：`21db143662621b18e255ae3d9e5cdf1b944605b5d7a63072d7783d022f41e838`。
- 树哈希输入按相对路径排序，每项为 `relative/path + NUL + lowercase(file SHA-256) + LF` 的 UTF-8 拼接。
- 上游首提交只允许在这份原样基线上补 package metadata 与本 README；领域代码和测试不得静默改写。
