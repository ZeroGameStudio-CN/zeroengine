# Equipment System API 文档

> 装备系统通过 `StatId` 对接 ZE StatSystem。装备数据只配置可扩展属性修饰列表，不再使用固定属性结构。

## 目录结构

| 文件 | 说明 |
| --- | --- |
| `EquipmentSlotType.cs` | 可配置装备槽位 |
| `EquipmentDataSO.cs` | 装备数据定义 |
| `EquipmentInstance.cs` | 强化、精炼、附魔、宝石运行时实例 |
| `EquipmentSetSO.cs` | 套装定义 |
| `EquipmentManager.cs` | 装备管理器 |

## EquipmentDataSO

```csharp
[Serializable]
public class StatModifierData
{
    public StatId StatId;
    public StatModType ModType;
    public float BaseValue;
    public float ValuePerLevel;

    public float GetValue(int enhanceLevel);
    public StatModifier CreateModifier(int enhanceLevel, object source);
}

public readonly struct EquipmentStatModifier
{
    public StatId StatId { get; }
    public StatModifier Modifier { get; }
}

[CreateAssetMenu(fileName = "NewEquipment", menuName = "ZeroEngine/Equipment/Equipment Data")]
public class EquipmentDataSO : InventoryItemSO
{
    public EquipmentSlotType SlotType;
    public EquipmentSetSO BelongsToSet;
    public List<StatModifierData> StatModifiers;

    public int MaxEnhanceLevel;
    public int MaxRefineLevel;
    public int GemSlotCount;
    public int GemSlotsPerRefine;
    public int RequiredLevel;

    public float GetEnhanceSuccessRate(int currentLevel);
    public int GetUnlockedGemSlots(int refineLevel);
    public EquipmentInstance CreateInstance();
}
```

## EquipmentInstance

```csharp
public class EquipmentInstance
{
    public EquipmentDataSO Data { get; }
    public int EnhanceLevel { get; }
    public int RefineLevel { get; }

    public IReadOnlyList<EquipmentStatModifier> GetCalculatedModifiers();
    public EnhanceResult Enhance(int levels = 1);
    public EnhanceResult Refine(int levels = 1);
    public bool AddEnchantment(EnchantmentData enchantment);
    public bool SocketGem(int slotIndex, InventoryItemSO gem);
    public bool RemoveGem(int slotIndex);
}
```

`GetCalculatedModifiers()` 会把装备基础修饰、强化成长、附魔和宝石效果统一折算成 `(StatId, StatModifier)`。

## EquipmentSetSO

```csharp
[CreateAssetMenu(fileName = "NewEquipmentSet", menuName = "ZeroEngine/Equipment/Equipment Set")]
public class EquipmentSetSO : ScriptableObject
{
    public string SetId;
    public string SetName;
    public Sprite Icon;
    public List<EquipmentDataSO> Pieces;
    public List<SetEffect> Effects;

    public IEnumerable<SetEffect> GetActiveEffects(int equippedCount);
}

[Serializable]
public class SetEffect
{
    public int RequiredPieces;
    public List<StatModifierData> StatBonuses;
    public string Description;
}
```

## 使用示例

```csharp
static readonly StatId Attack = "offense.attack";

equipment.StatModifiers.Add(new StatModifierData
{
    StatId = Attack,
    ModType = StatModType.Flat,
    BaseValue = 12f,
    ValuePerLevel = 2f
});

var instance = equipment.CreateInstance();
foreach (var statModifier in instance.GetCalculatedModifiers())
{
    statController.AddModifier(statModifier.StatId, statModifier.Modifier);
}
```

## 编辑器约定

- 装备属性 ID 应来自项目 stat catalog 的下拉选择。
- `ValuePerLevel` 只表达强化等级成长，不承担职业、品质、随机词条等业务规则。
- 套装效果和附魔同样输出 `EquipmentStatModifier`，由项目侧决定如何汇总到角色运行时属性。
