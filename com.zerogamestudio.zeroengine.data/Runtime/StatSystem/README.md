# ZeroEngine.StatSystem API 文档

> 面向 AI 助手和玩法工程的快速参考。StatSystem 是 ZE 通用属性底座，属性身份使用 `StatId` 字符串值，不再提供 enum 桥接或旧字段兼容层。

## 毕业线说明

- `StatId` 是跨角色、装备、Buff、TCE、AI 和编辑器的唯一属性身份。
- `StatCatalogSO` 负责属性定义、展示名、分组、排序、数值类型和 Excel 列映射。
- `StatBlock` 是可序列化的 `{ StatId, Value }` 配置块，用于角色、敌人、成长、导入导出和项目侧数据。
- `Stats` 是运行时属性容器，按 `StatId` 管理 `Stat` / `CurrentStat`，并支持批量修饰器增删。
- `StatController` 是 MonoBehaviour 入口，适合组件化实体直接挂载。

## 目录结构

```text
StatSystem/
├── Stat.cs              # 单个属性、修饰器、事件和有限数值保护
├── CurrentStat.cs       # HP/MP 这类当前值 + 最大值属性
├── StatDefinitions.cs   # StatId、StatDefinition、IStatProvider
├── StatCatalogSO.cs     # 属性目录 ScriptableObject
├── StatBlock.cs         # 可序列化属性值块
├── Stats.cs             # 运行时属性容器
├── StatController.cs    # MonoBehaviour 属性控制器
└── MathFormula.cs       # 公式计算
```

## 核心类型

```csharp
[Serializable]
public struct StatId : IEquatable<StatId>
{
    public string Value { get; }
    public bool IsEmpty { get; }
    public static implicit operator StatId(string value);
    public static implicit operator string(StatId id);
}

public enum StatValueKind
{
    Integer,
    Float,
    Percent,
    Multiplier
}

[Serializable]
public sealed class StatDefinition
{
    public StatId Id;
    public string DisplayName;
    public string Group;
    public int SortOrder;
    public StatValueKind ValueKind;
    public float DefaultValue;
    public float MinValue;
    public float MaxValue;
    public string ExcelColumn;
    public bool ShowInCharacterEditor;
}

public interface IStatProvider
{
    float GetStatValue(StatId id);
}
```

`StatId` 会 trim 并转小写。项目侧推荐集中定义常量，例如 `P5StatIds.Offense.Attack`，编辑器通过 `StatCatalogSO` 提供下拉选择。

## StatBlock

```csharp
[Serializable]
public sealed class StatBlock
{
    IReadOnlyList<StatValueEntry> Values { get; }
    int Count { get; }

    float Get(StatId id, float fallback = 0f);
    bool TryGet(StatId id, out float value);
    void Set(StatId id, float value);
    void Add(StatId id, float delta);
    bool Remove(StatId id);
    void Clear();
    StatBlock Clone();
    StatBlock Scaled(float multiplier);
    StatBlock Merged(StatBlock other);
    Dictionary<StatId, float> ToDictionary();
    static StatBlock FromDictionary(Dictionary<StatId, float> values);
}
```

## Stat / StatModifier

```csharp
[Serializable]
public class StatModifier
{
    public float Value;
    public StatModType ModType;    // Flat, PercentAdd, PercentMult
    public int Order;
    public object Source;
    public MathFormula Formula;

    float GetValue(MathContext ctx = null);
    bool NeedsRuntimeValue { get; }
    StatModifier CreateRuntime(MathContext ctx);
}

[Serializable]
public class Stat
{
    public float BaseValue;
    public float MinValue;
    public float MaxValue;
    public float Value { get; }

    event Action<StatChangedEventArgs> OnValueChanged;

    void AddModifier(StatModifier mod);
    void AddModifier(StatModifier mod, object source);
    bool RemoveModifier(StatModifier mod);
    bool RemoveModifier(StatModifier mod, object source);
    bool RemoveAllModifiersFromSource(object source);
    void RemoveAllModifiers();
    void ForceRecalculate();
    void ClearEventListeners();
}
```

计算公式：`Clamp((Base + Flat) * (1 + PercentAdd) * PercentMult, MinValue, MaxValue)`。

## Stats

```csharp
public class Stats
{
    int Count { get; }

    Stat GetStat(StatId id);
    T GetStat<T>(StatId id) where T : Stat;
    T GetOrCreateAndInit<T>(StatId id, Action<T> initAction) where T : Stat, new();
    T AddStat<T>(StatId id) where T : Stat, new();
    void SetStat(StatId id, Stat stat);

    bool TryGetStatValue(StatId id, out float value);
    float GetStatValue(StatId id, float fallback = 0f);
    Dictionary<StatId, float> GetValuesSnapshot();
    StatBlock ToStatBlock();

    void AddStatModifier(
        Dictionary<StatId, List<StatModifier>> data,
        object source,
        Func<StatId, Stat> forceAddFactory = null,
        MathContext ctx = null,
        Func<StatId, IncreaseType> increaseTypeResolver = null);

    void RemoveStatModifier(Dictionary<StatId, List<StatModifier>> data, object source, bool preserveCurrentPercent = false);
    void RemoveAllModifiersFromSource(object source);
    void Clear();
}
```

## StatController

```csharp
public class StatController : MonoBehaviour, IStatProvider
{
    event Action<StatControllerChangedEventArgs> OnAnyStatChanged;

    void InitStat(StatId id, float baseValue);
    void InitStat(StatId id, float baseValue, float minValue, float maxValue);
    Stat GetStat(StatId id);
    float GetStatValue(StatId id);
    void AddModifier(StatId id, StatModifier mod);
    void RemoveModifier(StatId id, StatModifier mod);
    void RefreshAllStats();
    StatControllerSaveData ExportSaveData();
    void ImportSaveData(StatControllerSaveData data);
    void ResetStats();
}
```

## 使用示例

```csharp
static readonly StatId Attack = "offense.attack";
static readonly StatId CritRate = "combat.crit_rate";

statController.InitStat(Attack, 12f, 0f, 9999f);

var attackBuff = new StatModifier(50f, StatModType.Flat, source: buffHandler);
statController.AddModifier(Attack, attackBuff);

float attack = statController.GetStatValue(Attack);

var critRate = new Stat(0.05f, 0f, 1f);
critRate.AddModifier(new StatModifier(0.2f, StatModType.PercentAdd));

var block = new StatBlock();
block.Set(Attack, 12f);
block.Set(CritRate, 0.05f);
```
