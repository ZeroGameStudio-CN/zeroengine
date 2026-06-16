# ZeroEngine.AbilitySystem API 文档

> **用途**: 本文档面向AI助手，提供AbilitySystem模块的快速参考。

## 毕业线说明

新 TCE 正式线是 `AbilityDefinition` + `AbilityExecutor` + `IAbilityRuntimeServices`：

- `AbilityDefinition` 保存稳定 ID、触发器、条件、效果、消耗、冷却和 Boost 配置。
- `AbilityExecutor` 负责统一检查等级、冷却、资源、条件、效果执行和结构化结果。
- `IAbilityRuntimeServices` 由项目侧适配资源、阵营、Buff、伤害和治疗；`IAbilityStatRuntimeServices` 是属性条件的可选扩展。
- `AbilityDefinitionValidator` 是运行时/编辑器共用的配置校验入口。

旧 `AbilityDataSO` / `AbilityHandler` / `ComponentData` 组件线只保留 legacy 兼容，已标记 `Obsolete`；新项目内容和编辑器工具不要再扩展旧线。

---

## 目录结构

```
AbilitySystem/
├── AbilityDefinition.cs  # 正式 TCE 数据、执行器、结果和 runtime service contract
├── AbilityDataSO.cs      # legacy 技能数据（Obsolete）
├── AbilityComponents.cs  # legacy 触发器/条件/效果组件（Obsolete）
├── AbilityHandler.cs     # legacy 技能施放系统（Obsolete）
└── ...
```

---

## AbilityDataSO.cs

**用途**: 技能配置数据，使用 `[SerializeReference]` 支持多态

```csharp
[CreateAssetMenu(menuName = "ZeroEngine/Ability System/Ability Data")]
public class AbilityDataSO : ScriptableObject  // 或 SerializedScriptableObject (Odin)
{
    // 基础信息
    public string AbilityName;
    [TextArea] public string Description;
    public Sprite Icon;

    // 施放配置 (v1.1.0+)
    public float CastTime = 0f;              // 施放前摇时间
    public float RecoveryTime = 0f;          // 施放后摇时间
    public bool Interruptible = true;        // 是否可被打断
    public float BaseCooldown = 1f;          // 基础冷却时间

    // 等级配置 (v1.1.0+)
    public int MaxLevel = 5;                 // 最大等级
    public float EffectScalePerLevel = 0.1f; // 每级效果倍率增加
    public float CooldownReductionPerLevel = 0.1f;  // 每级冷却减少

    // 组件
    [SerializeReference] public List<TriggerComponentData> Triggers;
    [SerializeReference] public List<ConditionComponentData> Conditions;
    [SerializeReference] public List<EffectComponentData> Effects;

    // 方法 (v1.1.0+)
    float GetCooldown(int level);          // 获取指定等级的冷却时间
    float GetEffectMultiplier(int level);  // 获取指定等级的效果倍率
}
```

---

## AbilityComponents.cs

**用途**: 定义技能组件的抽象基类和具体实现

### 基类

```csharp
public abstract class ComponentData { }

public abstract class TriggerComponentData : ComponentData
{
    public bool TriggerMultipleTimes;
    public int TriggerTimes = 1;
}

public abstract class ConditionComponentData : ComponentData { }

public abstract class EffectComponentData : ComponentData
{
    public AbilityTargetType TargetType;  // Self, Target, All
}
```

### 内置触发器

| 类名 | 说明 | 关键字段 |
|------|------|---------|
| `ManualTriggerData` | 手动触发 | `ButtonName` |
| `IntervalTriggerData` | 间隔触发 | `Interval`, `StartImmediately` |
| `OnHitTriggerData` | 命中触发 | `TriggerOnDealingDamage`, `TriggerOnTakingDamage` |

### 内置效果

| 类名 | 说明 | 关键字段 |
|------|------|---------|
| `DamageEffectData` | 造成伤害 | `DamageAmount`, `DamageType` |
| `HealEffectData` | 治疗 | `HealAmount` |
| `SpawnProjectileEffectData` | 生成抛射物 | `ProjectilePrefab`, `Speed` |
| `ApplyBuffEffectData` | 施加Buff | `BuffToApply`, `DurationOverride` |

### 内置条件

| 类名 | 说明 | 关键字段 |
|------|------|---------|
| `CooldownConditionData` | 冷却 | `CooldownSeconds` |
| `ResourceConditionData` | 资源消耗 | `Resource`, `RequiredAmount` |

---

## 枚举

```csharp
public enum AbilityTargetType { Self, Target, All }
public enum DamageType { Physical, Magical, True }
public enum ResourceType { Health, Mana, Stamina }
```

---

## 扩展组件

创建新的效果组件：

```csharp
[Serializable]
public class MyCustomEffectData : EffectComponentData
{
    public int MyParameter;
}
```

然后在ModSystem中注册：
```csharp
typeRegistry.RegisterType("MyCustomEffectData", typeof(MyCustomEffectData));
```

---

## AbilityHandler.cs (v1.1.0 重写)

**用途**: 完整的技能施放系统，包含状态机、冷却管理和打断机制

### AbilityCastState

```csharp
public enum AbilityCastState
{
    Idle,       // 空闲，可以施放
    Casting,    // 施放中（前摇，可被打断）
    Executing,  // 执行中（效果生效）
    Recovering  // 恢复中（后摇）
}
```

### AbilityInstance

```csharp
[Serializable]
public class AbilityInstance
{
    public AbilityDataSO Data;
    public int Level = 1;
    public float CooldownRemaining;

    // 属性
    bool IsOnCooldown { get; }           // 是否在冷却中
    float Cooldown { get; }              // 当前等级的冷却时间
    float EffectMultiplier { get; }      // 当前等级的效果倍率

    // 方法
    bool TryLevelUp();                   // 升级（返回是否成功）
    void StartCooldown();                // 开始冷却
    void UpdateCooldown(float deltaTime);// 更新冷却（每帧调用）
    void ResetCooldown();                // 重置冷却
}
```

### AbilityEventArgs

```csharp
public struct AbilityEventArgs
{
    public AbilityInstance Ability;
    public IAbilityTarget Target;
    public AbilityCastState PreviousState;
    public AbilityCastState NewState;
    public string InterruptReason;       // 仅打断时有值
}
```

### AbilityHandler

```csharp
public class AbilityHandler : MonoBehaviour, IAbilitySource
{
    // 状态
    AbilityCastState CurrentState { get; }
    AbilityInstance CurrentCastingAbility { get; }
    float CastProgress { get; }                    // 0-1 施放进度
    IReadOnlyList<AbilityInstance> Abilities { get; }

    // 事件
    event Action<AbilityEventArgs> OnAbilityStateChanged;
    event Action<AbilityEventArgs> OnAbilityInterrupted;
    event Action<AbilityInstance, IAbilityTarget, float> OnAbilityExecuted;

    // 技能管理
    AbilityInstance AddAbility(AbilityDataSO data, int level = 1);
    bool RemoveAbility(AbilityDataSO data);
    AbilityInstance GetAbility(AbilityDataSO data);

    // 施放
    bool CanCast(AbilityInstance ability);
    bool TryCastAbility(AbilityDataSO data, IAbilityTarget target = null);
    bool TryCastAbility(AbilityInstance ability, IAbilityTarget target = null);
    bool TryInterrupt(string reason = "Interrupted");

    // 升级和冷却
    bool TryLevelUpAbility(AbilityDataSO data);
    void ResetCooldown(AbilityDataSO data);
    void ResetAllCooldowns();
}
```

---

## v1.1.0+ 使用示例

```csharp
// 初始化技能
var handler = GetComponent<AbilityHandler>();
var fireball = handler.AddAbility(fireballData, level: 1);

// 监听事件
handler.OnAbilityStateChanged += args =>
{
    switch (args.NewState)
    {
        case AbilityCastState.Casting:
            ShowCastBar(args.Ability.Data.CastTime);
            break;
        case AbilityCastState.Executing:
            PlayVFX(args.Ability.Data);
            break;
    }
};

handler.OnAbilityInterrupted += args =>
{
    Debug.Log($"{args.Ability.Data.AbilityName} was interrupted: {args.InterruptReason}");
};

// 施放技能
if (handler.TryCastAbility(fireballData, target))
{
    Debug.Log("Casting fireball!");
}

// 打断施放
handler.TryInterrupt("Stunned");

// 升级技能
handler.TryLevelUpAbility(fireballData);
```
