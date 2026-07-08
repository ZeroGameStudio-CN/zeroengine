# ZeroEngine.BuffSystem API 文档

> **用途**: 本文档面向AI助手，提供BuffSystem模块的快速参考。
> **版本**: v3.1.0+
> **最后更新**: 2026-07-08

---

## 目录结构

```
BuffSystem/
├── BuffEnums.cs     # 枚举定义
├── BuffData.cs      # Buff配置（ScriptableObject）
├── BuffHandler.cs   # Buff运行时实例
├── BuffReceiver.cs  # 管理实体上的所有Buff
└── BuffUtils.cs     # 工具类和扩展方法 (v1.2.0+)
```

---

## 枚举定义

```csharp
public enum BuffCategory { Buff, Debuff, Neutral }

public enum BuffExpireMode
{
    RemoveAllStacks,  // 到期移除全部层数
    RemoveOneStack    // 到期移除一层，刷新时间
}

// v1.2.0+ 新增
public enum BuffStackMode
{
    Stack,    // 累加层数（默认）
    Refresh,  // 只刷新持续时间，不增加层数
    Replace   // 替换为新的 Buff
}

public enum BuffEventType
{
    Applied,    // 新 Buff 施加
    Refreshed,  // 持续时间刷新
    Stacked,    // 层数增加
    Unstacked,  // 层数减少
    Expired,    // 持续时间到期
    Removed     // 被主动移除
}
```

---

## BuffData.cs

**用途**: Buff配置数据

```csharp
[CreateAssetMenu(menuName = "ZeroEngine/Buff System/Buff Data")]
public class BuffData : ScriptableObject
{
    public string BuffId;
    public BuffCategory Category;
    public Sprite Icon;

    // 时间
    public float Duration = 10f;
    public int MaxStacks = 1;
    public float TickInterval = 1f;

    // 行为
    public BuffExpireMode ExpireMode;
    public BuffStackMode StackMode;        // v1.2.0+
    public bool RefreshOnAddStack = true;
    public bool RefreshOnRemoveStack = false;

    // 属性修改 (与 StatSystem 集成)
    public List<BuffStatModifierConfig> StatModifiers;
}

[Serializable]
public class BuffStatModifierConfig
{
    public StatId StatId;        // 字符串目录体系，v3.0.0 起替代旧 StatType 枚举
    public float Value;
    public StatModType ModType;  // Flat/PercentAdd/PercentMult
}
```

---

## BuffHandler.cs

**用途**: 单个Buff的运行时状态

```csharp
public class BuffHandler
{
    // 属性
    public BuffData Data { get; }
    public int CurrentStacks { get; }
    public float RemainingTime { get; }
    public bool IsExpired { get; }

    // 事件
    public event Action<BuffHandler> OnExpired;
    public event Action<BuffHandler> OnTick;

    // 方法
    void AddStacks(int count);
    void RemoveStacks(int count);
    void Tick(float deltaTime);
    void ClearModifiers();

    // v1.2.0+ 新增
    void RefreshDuration();
    void ResetStacks();
    void ForceExpire();

    // v3.0.0+ 新增
    void RestoreState(float remainingTime, int stacks);  // 读档/重建专用，不触发生命周期副作用，对已过期 handler 静默 no-op

    // v3.1.0+ 新增
    void OverrideDuration(float duration);  // 按施放点覆盖持续时长，之后 RefreshDuration() 优先用这个覆盖值
}
```

---

## IBuffStatTarget.cs (v3.0.0+)

**用途**: BuffHandler 应用/移除属性修饰器时依赖的最小契约，让调用方不必依赖具体的 `StatController` 实现。

```csharp
public interface IBuffStatTarget
{
    void AddModifier(StatId statId, StatModifier modifier);
    void RemoveModifier(StatId statId, StatModifier modifier);
}
```

`StatController` 实现了这个接口；任何自定义的属性容器（例如宿主项目自己的角色属性类）也可以实现它，直接传给 `BuffHandler` 构造函数，不需要是 `StatController`。

---

## BuffEventArgs (v1.2.0+)

```csharp
public struct BuffEventArgs
{
    public BuffHandler Buff;
    public BuffEventType EventType;
    public int OldStacks;
    public int NewStacks;
    public int StackDelta => NewStacks - OldStacks;
}
```

---

## BuffReceiver.cs

**用途**: 管理实体上的所有Buff

```csharp
public class BuffReceiver : MonoBehaviour
{
    [SerializeField] StatController _statController;

    // 属性
    IReadOnlyDictionary<string, BuffHandler> ActiveBuffs { get; }

    // 事件 (v1.2.0+)
    event Action<BuffEventArgs> OnBuffChanged;
    event Action<BuffHandler> OnBuffApplied;
    event Action<BuffHandler, BuffEventType> OnBuffRemoved;

    // 方法
    BuffHandler AddBuff(BuffData data, int stacks = 1);
    void RemoveBuff(string buffId, int stacks = 1);
    void RemoveBuffCompletely(string buffId);  // v1.2.0+
    void RemoveAllBuffs();                     // v1.2.0+
    bool HasBuff(string buffId);
    BuffHandler GetBuff(string buffId);
    int GetBuffStacks(string buffId);          // v1.2.0+
}
```

---

## BuffUtils.cs (v1.2.0+)

**用途**: Stat-Buff 集成工具和扩展方法

### 快速创建 Buff

```csharp
// 创建临时 Buff
var buff = BuffUtils.CreateTempBuff("temp_buff", 10f);

// 创建属性修改 Buff
var statBuff = BuffUtils.CreateStatBuff(
    "attack_up",
    StatType.Attack,
    10f,
    StatModType.Flat,
    duration: 30f
);

// 创建多属性 Buff
var multiBuff = BuffUtils.CreateMultiStatBuff(
    "power_up", 30f, maxStacks: 3,
    (StatType.Attack, 20f, StatModType.Flat),
    (StatType.Defense, 10f, StatModType.Flat)
);
```

### 常用 Buff 模式

```csharp
// 百分比增益 (20% 攻击力提升)
var boost = BuffUtils.CreateBoostBuff("atk_boost", StatType.Attack, 20f, 30f);

// 百分比减益 (10% 移速降低)
var slow = BuffUtils.CreateDebuffBuff("slow", StatType.MoveSpeed, 10f, 5f);

// 固定值增益
var flatBuff = BuffUtils.CreateFlatBuff("shield", StatType.Defense, 50f, 10f);

// DOT Buff
var dot = BuffUtils.CreateDotBuff("poison", 10f, tickInterval: 1f, maxStacks: 5);
```

### 扩展方法

```csharp
// BuffReceiver 扩展
receiver.AddPercentBoost("rage", StatType.Attack, 30f, 10f);
receiver.AddPercentDebuff("weaken", StatType.Defense, 20f, 8f);
receiver.AddFlatBoost("shield", StatType.Defense, 100f, 15f);

// 查询扩展
var allBuffs = receiver.GetAllBuffs();
var allDebuffs = receiver.GetAllDebuffs();
var attackBuffs = receiver.GetBuffsAffectingStat(StatType.Attack);

// 计算总加成
float flatBonus = receiver.GetTotalStatModification(StatType.Attack, StatModType.Flat);

// 清理扩展
receiver.RemoveAllDebuffs();
receiver.RemoveAllBuffsByCategory(BuffCategory.Buff);
```

---

## 使用示例

### 基础用法

```csharp
// 添加 Buff
var handler = buffReceiver.AddBuff(poisonBuffData, 3);

// 监听 Tick
handler.OnTick += h => DealDamage(h.CurrentStacks * 5);

// 移除
buffReceiver.RemoveBuff("poison_dot");
```

### v1.2.0+ 事件系统

```csharp
// 监听所有 Buff 变化
buffReceiver.OnBuffChanged += args =>
{
    Debug.Log($"Buff {args.Buff.Data.BuffId}: {args.EventType}");
    Debug.Log($"Stacks: {args.OldStacks} -> {args.NewStacks}");
};

// 监听新 Buff 施加
buffReceiver.OnBuffApplied += handler =>
{
    ShowBuffIcon(handler.Data.Icon);
};

// 监听 Buff 移除
buffReceiver.OnBuffRemoved += (handler, reason) =>
{
    if (reason == BuffEventType.Expired)
        Debug.Log("Buff expired naturally");
};
```

### v1.2.0+ StackMode

```csharp
// Stack 模式: 累加层数
attackBuffData.StackMode = BuffStackMode.Stack;
receiver.AddBuff(attackBuffData, 1);  // 1层
receiver.AddBuff(attackBuffData, 1);  // 2层

// Refresh 模式: 只刷新时间
shieldBuffData.StackMode = BuffStackMode.Refresh;
receiver.AddBuff(shieldBuffData, 1);  // 1层，10秒
receiver.AddBuff(shieldBuffData, 1);  // 仍然1层，时间刷新到10秒

// Replace 模式: 替换旧 Buff
berserkData.StackMode = BuffStackMode.Replace;
receiver.AddBuff(berserkData, 1);  // 1层
receiver.AddBuff(berserkData, 2);  // 重置为2层（非3层）
```

---

## Stat-Buff 集成

BuffSystem 与 StatSystem 无缝集成：

1. **自动应用修改器**: 添加 Buff 时自动向 StatController 添加 StatModifier
2. **层数感知**: 多层 Buff 会添加多份修改器
3. **自动清理**: Buff 过期/移除时自动移除所有修改器

```csharp
// BuffData 配置属性修改
buffData.StatModifiers = new List<BuffStatModifierConfig>
{
    new() { StatType = StatType.Attack, Value = 10f, ModType = StatModType.Flat },
    new() { StatType = StatType.CritRate, Value = 0.05f, ModType = StatModType.PercentAdd }
};

// 添加 3 层 Buff = 应用 3 份修改器
// Attack +30, CritRate +15%
receiver.AddBuff(buffData, 3);
```

---

## 版本历史

| 版本 | 新增功能 |
|------|----------|
| v1.0.0 | 基础 Buff 系统、层数、持续时间 |
| v1.2.0 | 事件系统、BuffStackMode、BuffUtils 工具类 |
| v3.0.0 | `StatId` 字符串目录替代 `StatType` 枚举；`IBuffStatTarget` 接口；`RestoreState` 读档恢复 |
| v3.1.0 | `BuffHandler.OverrideDuration`：按施放点覆盖持续时长 |
