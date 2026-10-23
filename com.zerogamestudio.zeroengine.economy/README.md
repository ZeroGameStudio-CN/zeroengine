# ZeroEngine.Economy

经济/物品系统包。

## 版本
- **当前版本**: 2.0.0
- **依赖**: ZeroEngine.Core, ZeroEngine.Persistence

## 包含模块

### Inventory (背包系统)
- `InventoryManager` - 背包管理器
- `InventoryItemSO` - 物品数据
- `InventoryInstanceData` - 稳定实例身份与领域载荷；普通定义项与不可堆叠实例共用槽位、原子交换和保存合同
- 多容器、堆叠、过滤

### Loot (掉落系统)
- `LootTableManager` - 掉落表管理器
- `LootTableSO` - 掉落表数据
- 权重、保底、分层

### Shop (商店系统)
- `ShopManager` - 商店管理器
- 买卖、折扣、库存

### Crafting (合成系统)
- `CraftingManager` - 合成管理器
- `CraftingRecipeSO` - 配方数据
- 技能经验系统

## 快速使用

```csharp
using ZeroEngine.Inventory;
using ZeroEngine.Loot;

// 添加物品
InventoryManager.Instance.AddItem(itemData, amount);

// 掉落
var results = LootTableManager.Instance.Roll(lootTable);

// 合成
CraftingManager.Instance.StartCraft(recipe);
```

## Crafting transactions and consumer ownership

Before `Start`, a consumer with its own save pipeline calls
`InventoryManager.ConfigurePersistence(...)` and
`CraftingManager.ConfigureDependencies(inventory, registerSave, unregisterSave, unlockCondition)`.
Registration/unregistration delegates must belong to the same save owner. Null paired delegates
select explicitly external save ownership. Configured operations do not create legacy save owners
or write the default save file; restore inventory through `ImportSaveData`.
Unconfigured consumers retain the legacy startup route, while destruction only unregisters
from the owner that actually registered the component.

`CanCraft` validates positive checked batches, aggregate duplicate ingredients, reusable tools,
recipe data, workbench, skill and final inventory capacity. `StartCraft` accepts an optional
explicit request ID overload; repeating an identical committed request returns its receipt.
A different payload using the same ID is rejected. Materials, result, experience and pending
state commit before observers receive events. A capacity rejection leaves slots unchanged.

Timed work stores the rolled outcome and consumed/output item IDs and amounts. Full inventory
at completion retains the pending work without rerolling or awarding experience. Cancel always
refunds consumed escrow independently of the failed-craft material policy; an incomplete refund
leaves the craft pending. Use the stable progress ID overload for UI requests; the index overload
remains for older callers. Save exports and progress views are detached copies. Restore validates
duplicate identities and pending/receipt consistency before replacing live state; older progress
is upgraded once when its recipe is available. Its serialized list position distinguishes identical concurrent legacy rows; migrated IDs remain unchanged on later saves. Missing external unlock predicates fail closed.

Retained package regressions cover transactions in `Tests/Editor/CraftingTransactionTests.cs` and real Start/Update/OnDestroy ownership in `Tests/PlayMode/CraftingLifecycleTests.cs`. Enable the
`com.zerogamestudio.zeroengine.economy` package in a consumer's UPM `testables` for Unity Test
Framework execution. Real consumer UI, save/continue and device evidence remain separate gates.

Inventory instance regressions are in `Tests/Editor/InventoryInstanceTests.cs`. Definition count,
consumption and recipe queries exclude instances. Instance exchange requires an exact definition,
identity and payload match; capacity is evaluated after consumption. Save import validates every
slot against registered definitions before replacing live state, and exports/imports clone lists,
slots and instance payloads. See `Runtime/Inventory/README.md` for the public contracts.
