# ZeroEngine.Economy

经济/物品系统包。

## 版本
- **当前版本**: 2.0.0
- **依赖**: ZeroEngine.Core, ZeroEngine.Persistence

## 包含模块

### Inventory (背包系统)
- `InventoryManager` - 背包管理器
- `InventoryItemSO` - 物品数据
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

## Editor Tooling

- `ZeroEngine.Economy.Editor` contains package-level config validation.
- `EconomyConfigValidator` checks item IDs, display names, stack/price ranges,
  crafting ingredients and outputs, unlock requirements, loot table entries,
  pity settings, shop item prices, stock, limits, schedules, and duplicate IDs.

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

## Dependency Pinning

When this package is consumed through Git UPM, add every
`com.zerogamestudio.*` dependency from `package.json` to the consumer project's
`Packages/manifest.json` at the same tested commit. See
[Consumer Project Setup](../docs/consumer-project-setup.md).
