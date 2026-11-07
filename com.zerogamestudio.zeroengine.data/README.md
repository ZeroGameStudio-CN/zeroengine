# ZeroEngine.Data

属性和 Buff 的通用运行时包。当前候选版本为 2.2.0，依赖 ZeroEngine.Core 2.0.0、ZeroEngine.Formula 0.1.0 与已有的 ZeroEngine.Persistence 2.0.0 合同。

## 属性合同

- `StatId` 提供稳定标识、比较、散列和 Unity 序列化。
- `Stat`、`CurrentStat` 和 `StatModifier` 处理数值、当前量、修正器及约束。
- `Stats` 持有运行时属性实例，`StatBlock` 提供按标识组织的作者数据。
- `StatController` 是已有的 MonoBehaviour 适配。项目自己的角色状态也可直接组合 `Stats`。

```csharp
using ZeroEngine.StatSystem;

var hpId = new StatId("core.max_hp");
var stats = new Stats();
var hp = stats.GetOrCreateAndInit<CurrentStat>(
    hpId,
    value => value.InitCurrent(100f, 100f, false));

hp.SetCurrent(75f);
var currentHp = stats.GetStat<CurrentStat>(hpId).CurrentValueInt;
```

## 标识规范化

`StatId` 沿用去除首尾空白与 invariant 小写的规则。null、空串和纯空白得到空标识。构造值、旧序列化 `_value`、默认值、比较与散列使用同一语义。

2.0.1 对已经规范化的 ASCII 标识复用原字符串。包含大写或非 ASCII 字符的输入继续使用原有 invariant 转换。序列化字段、属性数值及 Buff 合同保持兼容。

## Buff 合同

`BuffData` 描述效果，`BuffHandler` 持有一次应用的运行时状态，`BuffReceiver` 提供已有组件适配。项目通过 `IBuffStatTarget` 将修正器接到自己的属性所有者。

```csharp
using ZeroEngine.BuffSystem;

var handler = new BuffHandler(buffData, target);
handler.AddStacks(1);
handler.Tick(deltaTime);
handler.RemoveStacks(1);
```

`target` 由消费者提供，属性修正通过 `IBuffStatTarget` 的添加／移除入口进入已有状态。角色、装备和具体成长规则由消费者配置。

## 来源分组与时钟内核

2.2.0 收敛 POB Data 2.1.0 的无引擎 `ZeroEngine.BuffSystem.Core`。`BuffStackState`、`BuffSourceGroup`、`BuffClock` 和 `BuffLifecycle` 提供来源层数、作用域移除、快照复制、时钟选择与代次；宿主持有数值、结算、存档与表现副作用。

既有 `BuffHandler` 共用聚合层数和时钟算术，同时保留 `IBuffStatTarget`／`StatId`、时长覆盖、恢复、旧 signed count 算术及先过期后 Tick 的顺序。`StatBlock`、规范化标识与旧序列化不变。不为项目增加第二套属性或 Buff 兼容适配。

## 验证与采用

包内 `BuffHandlerStatIdTests` 覆盖 Buff 对标识的消费、作者／旧序列化标识等价，以及规范化 ASCII 的字符串复用。P5 的属性用例和真实探索／战斗调用用于验证消费者。

消费者正式引用固定的 Git commit，并与同项目其他直接 ZE 包统一。隔离测试可临时选择本地候选，验证结束后恢复 manifest 与 lock。
