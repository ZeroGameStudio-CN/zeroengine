# Talent Tree System API 文档

> 天赋树效果通过 `StatId` 对接 ZE StatSystem。属性效果不再使用 enum 属性类型。

## 目录结构

| 文件 | 说明 |
| --- | --- |
| `TalentEnums.cs` | 节点类型等枚举 |
| `TalentEvents.cs` | 事件参数 |
| `TalentNodeSO.cs` | 天赋节点定义 |
| `TalentTreeSO.cs` | 天赋树定义 |
| `TalentTreeController.cs` | 运行时控制器 |
| `Effects/` | 多态效果实现 |

## TalentNodeSO

```csharp
[CreateAssetMenu(fileName = "NewTalentNode", menuName = "ZeroEngine/TalentTree/Talent Node")]
public class TalentNodeSO : ScriptableObject
{
    public string NodeId { get; }
    public string DisplayName { get; }
    public string Description { get; }
    public Sprite Icon { get; }

    public TalentNodeType NodeType { get; }
    public int MaxLevel { get; }
    public int PointCostPerLevel { get; }
    public List<TalentNodeSO> Prerequisites { get; }
    public int PrerequisiteMinLevel { get; }
    public int RequiredCharacterLevel { get; }

    [SerializeReference]
    public List<TalentEffect> Effects { get; }

    public Vector2 EditorPosition { get; }
}
```

## 内置效果

```csharp
[Serializable]
public class StatModifierEffect : TalentEffect
{
    public StatId StatId;
    public StatModType ModType;
    public float ValuePerLevel;
    public float BaseValue;
}

[Serializable]
public class MultiStatModifierEffect : TalentEffect
{
    public List<StatEntry> Stats;
}

[Serializable]
public class BuffEffect : TalentEffect
{
    public BuffData BuffToApply;
    public bool PermanentBuff;
}

[Serializable]
public class UnlockAbilityEffect : TalentEffect
{
    public string AbilityId;
}
```

`StatModifierEffect` 和 `MultiStatModifierEffect` 会查找 owner 上的 `StatController`，按 `StatId` 添加或移除 `StatModifier`。

## TalentTreeController

```csharp
public class TalentTreeController : MonoBehaviour, ISaveable
{
    public void SetTree(TalentTreeSO tree);
    public TalentTreeSO CurrentTree { get; }

    public int AvailablePoints { get; }
    public int TotalPointsSpent { get; }
    public void AddPoints(int amount);
    public void SetPoints(int amount);

    public bool CanAllocate(TalentNodeSO node);
    public bool TryAllocatePoint(TalentNodeSO node);
    public bool TryDeallocatePoint(TalentNodeSO node);
    public int GetNodeLevel(TalentNodeSO node);
    public void Reset();

    public event Action<TalentEventArgs> OnPointAllocated;
    public event Action<TalentEventArgs> OnPointDeallocated;
    public event Action OnTreeReset;
}
```

## 使用示例

```csharp
static readonly StatId Attack = "offense.attack";

var effect = new StatModifierEffect
{
    StatId = Attack,
    ModType = StatModType.Flat,
    ValuePerLevel = 2f
};

talentController.SetTree(myTalentTree);
talentController.AddPoints(10);

if (talentController.CanAllocate(node))
{
    talentController.TryAllocatePoint(node);
}
```
