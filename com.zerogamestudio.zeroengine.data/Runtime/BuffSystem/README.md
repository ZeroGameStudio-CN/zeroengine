# ZeroEngine.BuffSystem API 文档

> BuffSystem 与 StatSystem 通过 `StatId` 集成。Buff 配置不再使用 enum 属性类型，也不提供旧字段兼容层。

## 目录结构

```text
BuffSystem/
├── BuffEnums.cs
├── BuffData.cs
├── BuffHandler.cs
├── BuffReceiver.cs
└── BuffUtils.cs
```

## BuffData

```csharp
[CreateAssetMenu(menuName = "ZeroEngine/Buff System/Buff Data")]
public class BuffData : ScriptableObject
{
    public string BuffId;
    public BuffCategory Category;
    public Sprite Icon;

    public float Duration = 10f;
    public int MaxStacks = 1;
    public float TickInterval = 1f;

    public BuffExpireMode ExpireMode;
    public BuffStackMode StackMode;
    public bool RefreshOnAddStack = true;
    public bool RefreshOnRemoveStack = false;

    public List<BuffStatModifierConfig> StatModifiers;
}

[Serializable]
public class BuffStatModifierConfig
{
    public StatId StatId;
    public float Value;
    public StatModType ModType;
}
```

## BuffReceiver

```csharp
public class BuffReceiver : MonoBehaviour
{
    IReadOnlyDictionary<string, BuffHandler> ActiveBuffs { get; }

    event Action<BuffEventArgs> OnBuffChanged;
    event Action<BuffHandler> OnBuffApplied;
    event Action<BuffHandler, BuffEventType> OnBuffRemoved;

    BuffHandler AddBuff(BuffData data, int stacks = 1);
    void RemoveBuff(string buffId, int stacks = 1);
    void RemoveBuffCompletely(string buffId);
    void RemoveAllBuffs();
    bool HasBuff(string buffId);
    BuffHandler GetBuff(string buffId);
    int GetBuffStacks(string buffId);
}
```

## BuffUtils

```csharp
static readonly StatId Attack = "offense.attack";
static readonly StatId Defense = "defense.defense";

var attackUp = BuffUtils.CreateStatBuff(
    "attack_up",
    Attack,
    10f,
    StatModType.Flat,
    duration: 30f);

var powerUp = BuffUtils.CreateMultiStatBuff(
    "power_up",
    duration: 30f,
    maxStacks: 3,
    (Attack, 20f, StatModType.Flat),
    (Defense, 10f, StatModType.Flat));

receiver.AddPercentBoost("rage", Attack, 30f, 10f);
receiver.AddPercentDebuff("weaken", Defense, 20f, 8f);
receiver.AddFlatBoost("shield", Defense, 100f, 15f);

var attackBuffs = receiver.GetBuffsAffectingStat(Attack);
float flatAttackBonus = receiver.GetTotalStatModification(Attack, StatModType.Flat);
```

## Stat-Buff 集成规则

1. 添加 Buff 时，`BuffReceiver` 按 `BuffData.StatModifiers` 自动向目标 `StatController` 添加 `StatModifier`。
2. 多层 Buff 会按当前层数应用多份修饰器。
3. Buff 过期、移除或清空时会按来源自动移除对应修饰器。
4. 属性 ID 必须来自项目的 stat catalog 或项目常量，不能临时拼写散落在业务逻辑里。

## Stack 模式

```csharp
attackBuffData.StackMode = BuffStackMode.Stack;   // 增加层数
shieldBuffData.StackMode = BuffStackMode.Refresh; // 只刷新时间
berserkData.StackMode = BuffStackMode.Replace;    // 替换旧 Buff
```
