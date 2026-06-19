# ZeroEngine.RPG

回合制 RPG 系统模块，提供多种回合制战斗框架和八方旅人风格的核心机制。

## 功能概览

### TurnBased - 回合制战斗框架
- **TurnManager** - 回合管理器，控制战斗流程
- **ITurnBasedCombatant** - 回合制战斗单位接口
- **ITurnOrderCalculator** - 行动顺序计算策略接口

### 支持的回合制变体
| 变体 | 说明 | 状态 |
|------|------|------|
| SpeedBased | 速度排序制 (八方旅人) | ✅ 已实现 |
| ATB | Active Time Battle (最终幻想) | ✅ 已实现 |
| CTB | Conditional Turn-Based (FFX) | ✅ 已实现 |
| ActionPoint | 行动点制 | ✅ 已实现 |

### Systems - 战斗子系统 (v2.2.0)
- **Boost** - BP 强化系统 (八方旅人) ✅
- **ShieldBreak** - 破盾系统 (八方旅人) ✅

### Systems - 战斗辅助系统 (v2.6.0)
- **Encounter** - 随机遭遇系统 ✅
- **BattleReward** - 战斗结算系统 ✅

## 配置校验

`ZeroEngine.RPG.Editor` 提供 `RpgConfigValidator`，用于在 Editor 测试或配置发布流程中检查：

- `BattleRewardConfigSO` 的经验/JP/金币倍率、上下限比例和通关奖励倍率。
- `EncounterTableSO` 的遭遇表 ID、重复 ID、区域等级、遭遇率、冷却、精英概率、敌人条目和 Boss/Elite 标记。
- `SkillVisualDataSO` 的显示名、描述、时间轴总时长、空事件、负延迟，以及动画、VFX、音效、伤害弹字、位移、相机事件参数。

Spine 和 Timeline 相关配置位于可选宏路径中，默认 validator 不直接引用这些可选类型。

## 快速开始

### 1. 实现战斗单位

```csharp
using ZeroEngine.RPG.TurnBased;
using ZeroEngine.RPG.Systems;
using ZeroEngine.Combat;

public class MyBattleUnit : TurnBasedCombatantBase
{
    [SerializeField] private float _currentHealth = 100f;
    [SerializeField] private float _maxHealth = 100f;

    // BP 和护盾组件
    private BoostComponent _boost;
    private ShieldComponent _shield;

    public override bool IsAlive => _currentHealth > 0;

    void Awake()
    {
        _boost = GetComponent<BoostComponent>();
        _shield = GetComponent<ShieldComponent>();
    }

    public override DamageResult TakeDamage(DamageData damage)
    {
        _currentHealth -= damage.BaseDamage;
        return new DamageResult(damage.BaseDamage, false, false);
    }

    public override float ReceiveHeal(float amount, ICombatant source = null)
    {
        var healed = Mathf.Min(amount, _maxHealth - _currentHealth);
        _currentHealth += healed;
        return healed;
    }
}
```

### 2. 开始战斗

```csharp
using ZeroEngine.RPG.TurnBased;

// 获取参战单位
var playerUnits = FindObjectsOfType<MyBattleUnit>()
    .Where(u => u.IsPlayerControlled);
var enemyUnits = FindObjectsOfType<MyBattleUnit>()
    .Where(u => !u.IsPlayerControlled);

// 开始战斗
TurnManager.Instance.StartBattle(playerUnits, enemyUnits);
```

### 3. 处理玩家输入

```csharp
// 监听等待玩家输入事件
TurnManager.Instance.OnWaitingForPlayerInput += OnPlayerTurn;

private void OnPlayerTurn(ITurnBasedCombatant actor)
{
    // 显示 UI，等待玩家选择
    ShowBattleUI(actor);
}

// 提交玩家行动
public void OnAttackButtonClicked(ITurnBasedCombatant target)
{
    var action = BattleAction.Attack(
        TurnManager.Instance.CurrentActor,
        target,
        boostLevel: 0  // BP 强化等级
    );
    TurnManager.Instance.SubmitPlayerAction(action);
}
```

### 4. 监听战斗事件

```csharp
TurnManager.Instance.OnBattleStarted += args => Debug.Log("战斗开始");
TurnManager.Instance.OnBattleEnded += args => Debug.Log($"战斗结束: {args.Result}");
TurnManager.Instance.OnTurnStarted += args => Debug.Log($"回合 {args.TurnNumber} 开始");
TurnManager.Instance.OnActionExecuted += args => Debug.Log($"{args.Action.Actor.DisplayName} 执行了 {args.Action.ActionType}");
```

---

## Dependency Pinning

When this package is consumed through Git UPM, add every
`com.zerogamestudio.*` dependency from `package.json` to the consumer project's
`Packages/manifest.json` at the same tested commit. See
[Consumer Project Setup](../docs/consumer-project-setup.md).

## Boost System (BP 强化)

八方旅人风格的 BP 强化系统。

### 核心常量
```csharp
BoostConstants.DEFAULT_MAX_BP = 5;           // 最大 BP
BoostConstants.DEFAULT_BP_PER_TURN = 1;      // 每回合恢复
BoostConstants.MAX_BOOST_LEVEL = 3;          // 单次最大消耗
BoostConstants.BOOST_MULTIPLIER_PER_POINT = 0.5f; // 每点 +50% 威力
```

### 使用方式

```csharp
using ZeroEngine.RPG.Systems;

// 获取 BP 组件
var boost = unit.GetComponent<BoostComponent>();

// 检查和消耗 BP
if (boost.HasEnoughBP(2))
{
    boost.ConsumeBP(2);  // 消耗 2 点 BP
}

// 回合开始恢复
boost.OnTurnStartBPRecovery();

// 监听 BP 变化
boost.OnBPChanged += (oldBP, newBP) => UpdateBPUI(newBP);
```

### 伤害加成

```csharp
using ZeroEngine.Combat;
using ZeroEngine.RPG.Systems;

// 创建带 Boost 的伤害
var damage = DamageData.Physical(100f, attacker)
    .WithBoost(boostLevel);  // 添加 Boost 等级

// 使用 BoostDamageProcessor 自动计算加成
// Boost Lv.1 = 1.5x, Lv.2 = 2.0x, Lv.3 = 2.5x
```

---

## ShieldBreak System (破盾系统)

八方旅人风格的弱点破盾系统。

### 核心常量
```csharp
ShieldConstants.DEFAULT_SHIELD_POINTS = 3;        // 默认护盾
ShieldConstants.BREAK_RECOVERY_TURNS = 1;         // 破盾恢复回合
ShieldConstants.BREAK_DAMAGE_MULTIPLIER = 1.5f;   // 破盾伤害 +50%
ShieldConstants.WEAKNESS_DAMAGE_MULTIPLIER = 1.2f; // 弱点伤害 +20%
```

### 弱点类型

```csharp
// 物理弱点
WeaknessType.Sword    // 剑
WeaknessType.Spear    // 枪
WeaknessType.Dagger   // 短剑
WeaknessType.Axe      // 斧
WeaknessType.Bow      // 弓
WeaknessType.Staff    // 杖

// 魔法弱点
WeaknessType.Fire       // 火
WeaknessType.Ice        // 冰
WeaknessType.Lightning  // 雷
WeaknessType.Wind       // 风
WeaknessType.Light      // 光
WeaknessType.Dark       // 暗
```

### 使用方式

```csharp
using ZeroEngine.RPG.Systems;

// 获取护盾组件
var shield = enemy.GetComponent<ShieldComponent>();

// 配置弱点
shield.AddWeakness(WeaknessType.Sword);
shield.AddWeakness(WeaknessType.Fire);

// 检查弱点
if (shield.CheckWeakness(WeaknessType.Fire))
{
    shield.ProcessWeaknessHit(WeaknessType.Fire);  // 减少护盾并检查破盾
}

// 监听破盾
shield.OnBroken += () => PlayBreakAnimation();
shield.OnRecovered += () => PlayRecoverAnimation();

// 回合结束处理
shield.OnTurnEndShieldRecovery();
```

### 伤害加成

```csharp
using ZeroEngine.Combat;
using ZeroEngine.RPG.Systems;

// 创建带弱点类型的伤害
var damage = DamageData.Physical(100f, attacker)
    .WithAttackType(WeaknessType.Sword);

// 同时使用 Boost 和弱点
var damage = DamageData.Physical(100f, attacker)
    .WithBoostAndAttackType(boostLevel: 2, WeaknessType.Fire);

// ShieldDamageProcessor 自动计算:
// - 命中弱点: x1.2
// - 破盾状态: x1.5
// - 叠加: x1.8
```

---

## 核心类型

### BattlePhase - 战斗阶段
```csharp
public enum BattlePhase
{
    None,           // 未开始
    Initialization, // 初始化
    TurnStart,      // 回合开始
    PlayerCommand,  // 玩家输入
    EnemyAI,        // AI 决策
    ActionExecution,// 行动执行
    ActionResolution,// 行动结算
    TurnEnd,        // 回合结束
    BattleEnd       // 战斗结束
}
```

### BattleAction - 战斗行动
```csharp
// 创建不同类型的行动
var attack = BattleAction.Attack(actor, target, boostLevel);
var skill = BattleAction.Skill(actor, "fireball", targets, boostLevel);
var item = BattleAction.Item(actor, "potion", target);
var defend = BattleAction.Defend(actor);
var escape = BattleAction.Escape(actor);
```

### TurnBasedBattleContext - 战斗上下文
```csharp
var context = TurnManager.Instance.Context;

// 访问战斗状态
int turn = context.TurnCount;
var phase = context.CurrentPhase;
var actor = context.CurrentActor;

// 访问单位列表
var allAlive = context.AliveUnits;
var playerAlive = context.AlivePlayerUnits;
var enemyAlive = context.AliveEnemyUnits;

// 检查战斗结束
var result = context.CheckBattleEnd();
```

## 自定义行动顺序

```csharp
// 实现自定义计算器
public class MyTurnOrder : ITurnOrderCalculator
{
    public string StrategyName => "My Custom Order";

    public IEnumerable<ITurnBasedCombatant> CalculateOrder(
        IEnumerable<ITurnBasedCombatant> combatants,
        TurnBasedBattleContext context)
    {
        // 自定义排序逻辑
        return combatants.OrderBy(c => c.Speed);
    }

    // ... 实现其他方法
}

// 应用自定义计算器
TurnManager.Instance.SetOrderCalculator(new MyTurnOrder());
```

---

## 与 DamageCalculator 集成

```csharp
using ZeroEngine.Combat;
using ZeroEngine.RPG.Systems;

// 注册伤害处理器
DamageCalculator.Instance.RegisterProcessor(new BoostDamageProcessor());
DamageCalculator.Instance.RegisterProcessor(new ShieldDamageProcessor());

// 处理器优先级:
// - BoostDamageProcessor: 100 (先计算 BP 加成)
// - ShieldDamageProcessor: 200 (后计算弱点/破盾加成)
```

---

## 依赖

- `com.zerogamestudio.zeroengine.core` (2.0.0)
- `com.zerogamestudio.zeroengine.data` (2.0.0)
- `com.zerogamestudio.zeroengine.combat` (2.0.0)

## 回合制变体详解

### ATB - Active Time Battle (最终幻想风格)

实时填充 ATB 槽，满了可以行动。

```csharp
using ZeroEngine.RPG.TurnBased.Variants;

var atb = new ATBTurnOrder();
atb.MaxATB = 100f;                    // ATB 槽最大值
atb.BaseChargeRate = 1f;              // 基础填充速度
atb.SpeedToChargeMultiplier = 0.1f;   // 速度转换系数
atb.Mode = ATBMode.Wait;              // 等待模式 (选择时暂停)

// 每帧更新 ATB
var newlyReady = atb.UpdateATB(allUnits, Time.deltaTime);

// 检查单位状态
float percent = atb.GetATBPercent(unit);
bool isReady = atb.IsReady(unit);
```

### CTB - Conditional Turn-Based (FFX 风格)

时间轴系统，行动决定下次行动的时机。

```csharp
using ZeroEngine.RPG.TurnBased.Variants;

var ctb = new CTBTurnOrder();
ctb.BaseActionDelay = 100;            // 基础延迟
ctb.SpeedDelayReduction = 0.5f;       // 速度减少延迟

// 获取时间轴预览
var timeline = ctb.GetTimelinePreview();
foreach (var (unit, tick) in timeline)
{
    Debug.Log($"{unit.DisplayName} 将在 tick {tick} 行动");
}

// 快速行动 (延迟减半)
action.CustomData = CTBActionData.Fast(0.5f);

// 慢速行动 (延迟翻倍)
action.CustomData = CTBActionData.Slow(2f);
```

### ActionPoint - 行动点制

每回合有行动点，不同行动消耗不同点数。

```csharp
using ZeroEngine.RPG.TurnBased.Variants;

var ap = new ActionPointTurnOrder();
ap.DefaultMaxAP = 100;                // 默认最大 AP
ap.SpeedToAPBonus = 1f;               // 速度转 AP 加成
ap.FullRestoreOnTurnStart = true;     // 回合开始完全恢复

// 默认行动消耗
// Attack: 30 AP, Skill: 50 AP, Item: 20 AP, Defend: 10 AP

// 检查可执行行动
var affordableActions = ap.GetAffordableActions(unit);
bool canAttack = ap.CanAffordAction(unit, BattleActionType.Attack);

// 自定义 AP 消耗
action.CustomData = APActionData.Create(25);  // 消耗 25 AP
action.CustomData = APActionData.Free();      // 免费行动
```

---

## 版本历史

### v2.6.0 (2026-01-07)
- Encounter System (随机遭遇系统)
  - `EncounterTableSO` - 遭遇表配置 (权重随机、等级过滤)
  - `EncounterManager` - 遭遇管理器 (步数判定、Boss 触发)
  - `EncounterTrigger` - 触发器组件 (4 种类型: StepBased/TimeBased/ZoneBased/Forced)
  - `EncounterEntry` / `EncounterResult` - 遭遇条目/结果数据
- BattleReward System (战斗结算系统)
  - `BattleRewardManager` - 战斗结算管理器 (计算+分配)
  - `BattleRewardConfigSO` - 结算配置 (5 种经验分配模式、等级差奖惩)
  - `EnemyRewardConfig` - 敌人奖励配置 (Exp/Gold/JP/掉落)
  - 精英/Boss 倍率、完美通关奖励
  - 支持 ISaveable 存档

### v2.3.0 (2026-01-07)
- TurnBased Variants (回合制变体)
  - `ATBTurnOrder` - ATB 系统 (最终幻想风格)
    - 实时 ATB 槽填充
    - Active/Wait/SemiActive 模式
    - 速度影响填充速度
  - `CTBTurnOrder` - CTB 系统 (FFX 风格)
    - 时间轴系统
    - 行动延迟机制
    - CTBActionData 延迟修正
  - `ActionPointTurnOrder` - 行动点制
    - 每回合 AP 预算
    - 不同行动不同消耗
    - 多次行动支持

### v2.2.0 (2026-01-07)
- Boost System (BP 强化系统)
  - `IBoostable` 接口
  - `BoostComponent` 组件
  - `BoostDamageProcessor` 伤害处理器
  - `BoostConstants` 常量定义
- ShieldBreak System (破盾系统)
  - `IShieldable` 接口
  - `ShieldComponent` 组件
  - `ShieldDamageProcessor` 伤害处理器
  - `WeaknessType` 弱点类型枚举
  - `ShieldConstants` 常量定义

### v2.1.0 (2026-01-07)
- 初始版本
- TurnBased 回合制框架
- SpeedBased 行动顺序计算器 (八方旅人风格)

### 计划中
- v2.7.0: Procedural Gen (地牢生成)

---

## Encounter System (随机遭遇系统) - v2.6.0

八方旅人风格的随机遭遇系统，支持步数/时间/区域触发。

### 核心组件

| 类 | 用途 |
|-----|------|
| `EncounterTableSO` | 遭遇表配置 (权重随机、条件过滤) |
| `EncounterManager` | 遭遇管理器 (步数判定、Boss 触发) |
| `EncounterTrigger` | 触发器组件 (4 种触发类型) |
| `EncounterEntry` | 遭遇条目 (敌人列表、权重) |

### 触发器类型

```csharp
public enum EncounterTriggerType
{
    StepBased,  // 按步数触发 (RPG 地图移动)
    TimeBased,  // 按时间触发 (实时探索)
    ZoneBased,  // 进入区域触发 (Collider)
    Forced      // 强制触发 (Boss/剧情)
}
```

### 快速使用

```csharp
using ZeroEngine.RPG.Encounter;

// 设置遭遇表
EncounterManager.Instance.SetEncounterTable(forestTable);
EncounterManager.Instance.SetPlayerLevel(10);

// 处理步数 (每移动一步调用)
var result = EncounterManager.Instance.ProcessStep();
if (result.HasValue)
{
    StartBattle(result.Value.EnemyIds);
}

// 强制触发 Boss
var bossResult = EncounterManager.Instance.TriggerBossEncounter("boss_dragon");

// 使用驱虫剂 (减少遭遇)
EncounterManager.Instance.UseRepelItem(100);  // 100 步内不遭遇

// 监听遭遇事件
EncounterManager.Instance.OnEncounterTriggered += args => {
    Debug.Log($"遭遇: {args.Result.EnemyCount} 只敌人");
};
```

### 创建遭遇表

```csharp
// 在 Project 窗口右键 -> Create -> ZeroEngine/RPG/Encounter Table

// 配置示例:
encounterTable.BaseEncounterRate = 0.1f;  // 10% 基础遭遇率
encounterTable.RatePerStep = 0.02f;       // 每步增加 2%
encounterTable.MaxEncounterRate = 0.5f;   // 最高 50%
encounterTable.CooldownSteps = 3;         // 遭遇后 3 步内不再遭遇
encounterTable.EliteChance = 0.1f;        // 10% 精英遭遇
```

### 区域触发器

```csharp
// 挂载 EncounterTrigger 到区域 GameObject

// 配置
trigger.TriggerType = EncounterTriggerType.ZoneBased;
trigger.EncounterTable = forestTable;
trigger.AutoSetTableOnEnter = true;  // 进入时自动设置遭遇表

// 监听
trigger.OnEncounterReady += result => {
    // 处理遭遇
};
```

---

## BattleReward System (战斗结算系统) - v2.6.0

战斗结束后的经验/金币/JP 计算和分配系统。

### 核心组件

| 类 | 用途 |
|-----|------|
| `BattleRewardManager` | 战斗结算管理器 (计算+分配) |
| `BattleRewardConfigSO` | 结算配置 (分配模式、倍率) |
| `EnemyRewardConfig` | 单个敌人的奖励配置 |
| `BattleRewardResult` | 结算结果 (经验/金币/掉落) |

### 经验分配模式

```csharp
public enum ExpDistributionMode
{
    EqualSplit,         // 平均分配
    FullToAll,          // 每人获得完整经验
    DamageContribution, // 按伤害贡献
    KillBased,          // 按击杀分配
    SurvivorsOnly       // 只给存活者
}
```

### 快速使用

```csharp
using ZeroEngine.RPG.BattleReward;

// 创建击败敌人信息
var defeatedEnemies = new List<DefeatedEnemyInfo>
{
    new DefeatedEnemyInfo
    {
        EnemyId = "goblin",
        Level = 5,
        IsElite = false,
        IsBoss = false
    },
    new DefeatedEnemyInfo
    {
        EnemyId = "goblin_chief",
        Level = 8,
        IsElite = true,
        IsBoss = false
    }
};

// 队伍成员
var partyMembers = new List<string> { "hero", "mage", "warrior" };

// 计算奖励
var result = BattleRewardManager.Instance.Calculate(
    defeatedEnemies,
    partyMembers,
    averagePartyLevel: 6,
    noDamageClear: false,
    fullClear: true
);

// 查看结果
Debug.Log($"总经验: {result.TotalExp}");
Debug.Log($"总金币: {result.TotalGold}");
Debug.Log($"总JP: {result.TotalJP}");

// 发放奖励 (调用外部系统)
BattleRewardManager.Instance.GrantReward(result);

// 或一键计算并发放
var result = BattleRewardManager.Instance.CalculateAndGrant(
    defeatedEnemies, partyMembers, 6, false, true
);
```

### 配置奖励

```csharp
// 在 Project 窗口右键 -> Create -> ZeroEngine/RPG/Battle Reward Config

// 配置示例:
config.ExpMode = ExpDistributionMode.FullToAll;
config.ApplyLevelPenalty = true;       // 等级差惩罚
config.LevelPenaltyPerLevel = 0.1f;    // 每级差减少 10%
config.MinExpRatio = 0.1f;             // 最低 10%
config.ApplyLevelBonus = true;         // 等级差奖励
config.LevelBonusPerLevel = 0.15f;     // 每级差增加 15%
config.MaxExpRatio = 2f;               // 最高 200%

// 精英/Boss 倍率
config.EliteExpMultiplier = 2f;        // 精英经验 x2
config.BossExpMultiplier = 5f;         // Boss 经验 x5

// 完美通关奖励
config.NoDamageExpBonus = 1.5f;        // 无伤 +50% 经验
config.FullClearGoldBonus = 1.2f;      // 全歼 +20% 金币
```

### 注册敌人奖励

```csharp
// 注册敌人奖励配置
BattleRewardManager.Instance.RegisterEnemyReward(new EnemyRewardConfig
{
    EnemyId = "dragon",
    BaseExp = 1000,
    BaseGold = 500,
    BaseJP = 50,
    ExpLevelScaling = 0.15f,
    LootTableId = "dragon_loot"
});
```

### 监听事件

```csharp
// 奖励计算完成
BattleRewardManager.Instance.OnRewardCalculated += result => {
    ShowRewardPreview(result);
};

// 奖励发放完成
BattleRewardManager.Instance.OnRewardGranted += result => {
    ShowRewardAnimation(result);
};

// 角色升级
BattleRewardManager.Instance.OnLevelUp += info => {
    Debug.Log($"{info.MemberId} 升级! {info.OldLevel} -> {info.NewLevel}");
};

// 职业升级
BattleRewardManager.Instance.OnJobLevelUp += info => {
    Debug.Log($"{info.MemberId} 的 {info.JobType} 升级!");
};
```

### 自定义发放逻辑

继承 `BattleRewardManager` 并重写发放方法:

```csharp
public class MyBattleRewardManager : BattleRewardManager
{
    protected override LevelUpInfo? GrantExpToMember(string memberId, int exp)
    {
        var member = PartyManager.Instance?.GetMember(memberId);
        if (member == null) return null;

        int oldLevel = member.Level;
        member.AddExp(exp);

        if (member.Level > oldLevel)
        {
            return new LevelUpInfo
            {
                MemberId = memberId,
                OldLevel = oldLevel,
                NewLevel = member.Level
            };
        }
        return null;
    }

    protected override void GrantGold(int amount)
    {
        CurrencyManager.Instance?.AddCurrency("Gold", amount);
    }

    protected override void GrantItem(string itemId, int amount)
    {
        InventoryManager.Instance?.AddItem(itemId, amount);
    }
}
```

---
