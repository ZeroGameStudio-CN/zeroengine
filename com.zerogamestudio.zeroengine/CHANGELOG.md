# ZeroEngine Changelog

All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [2.1.0] - 2026-08-10

- Reorganized the legacy Dashboard module as Core Tools with Content, Logic & Flow, World & Meta, and Diagnostics & Modding sections.
- Updated Dashboard/editor-ui dependencies to 3.1.0/1.1.0 without changing window menu paths.

## [2.0.0] - 2026-08-10

- Unified all Dashboard-exposed Editor windows on `com.zerogamestudio.zeroengine.editor-ui@1.0.0`.
- Git URL consumers must directly pin editor-ui to the same ZeroEngine commit.

## [2.7.0] - 2026-01-07

### Added

#### Skill Visual System (技能表现系统)

从 ZGSProject_5 迁移并增强的技能表现系统，基于 DOTween Sequence 实现时间轴式的视觉效果编排。

**核心架构**
- `VisualEvent` - 抽象事件基类，子类定义具体表现
- `VisualContext` - 执行上下文 (Caster/Target/Position)
- `SkillVisualDataSO` - ScriptableObject 配置资产
- `[SerializeReference]` 多态支持，无碎片文件

**内置事件类型 (8 种)**
- `SpawnVFXEvent` - 生成特效 (支持 Pool 集成)
- `PlayAnimationEvent` - Animator 动画触发
- `PlayTimelineEvent` - Unity Timeline 播放
- `PlaySoundEvent` - 音效播放 (2D/3D)
- `CameraControlEvent` - 相机震动/缩放/慢动作
- `MoveEvent` - 施法者位移 (ToTarget/DashToTarget/Return)
- `DamagePopupEvent` - 伤害数字弹出
- `PlaySpineAnimEvent` - Spine 动画 (需要 SPINE_UNITY)

**Spine 集成 (可选)**
- `SpineEventBridge` - Spine 事件桥接器
- `SpineEventMappingSO` - Spine 事件映射配置
- 需要定义 `SPINE_UNITY` 编译宏

**依赖配置**
```csharp
// 必需 (选择性启用)
#define ZEROENGINE_DOTWEEN    // DOTween 支持
#define ODIN_INSPECTOR        // Odin 编辑器增强
#define ZEROENGINE_POOL       // 对象池集成

// 可选
#define SPINE_UNITY           // Spine 动画支持
#define TEXTMESHPRO           // TMP 文字支持
```

**快速使用**
```csharp
using ZeroEngine.RPG.SkillVisual;

// 创建上下文
var context = VisualContext.Create(caster, target);

// 播放技能表现
skillVisualData.Play(context, onComplete: () => {
    Debug.Log("技能表现完成");
});

// 或创建序列手动控制
var sequence = skillVisualData.CreateSequence(context);
sequence.SetSpeedBased(true);
sequence.Play();
```

**编辑器功能**
- Odin Inspector 多态事件选择器
- "Sort Events By Delay" 一键排序
- "Calculate Duration" 自动计算时长

### Integration Points

```
SkillVisual + TurnBased:
- BattleAction 执行时播放对应 SkillVisualData
- 动画完成后触发伤害计算

SkillVisual + Combat:
- DamagePopupEvent 显示伤害数字
- CameraControlEvent 配合必杀技

SkillVisual + Pool:
- SpawnVFXEvent 自动使用 PoolManager
- VFX 自动回收

SkillVisual + Spine:
- PlaySpineAnimEvent 播放骨骼动画
- SpineEventBridge 响应动画事件
```

---

## [2.6.0] - 2026-01-07

### Added

#### Encounter System (遭遇系统)

**EncounterTableSO** - 遭遇表配置
- 权重随机敌人组合选择
- 遭遇率曲线配置 (明雷/暗雷模式)
- 条件过滤 (等级限制/时间段/天气)
- 支持 Boss 遭遇配置

**EncounterManager** - 遭遇管理器 (MonoSingleton)
- `SetEncounterTable()` - 设置当前遭遇表
- `ProcessStep()` - 处理步数判定 (暗雷模式)
- `TriggerRandomEncounter()` - 触发随机遭遇
- `TriggerBossEncounter()` - 触发 Boss 遭遇
- `SetEncounterEnabled()` - 启用/禁用遭遇
- 遭遇冷却和调试模式

**EncounterTrigger** - 遭遇触发器组件
- 4 种触发类型: StepBased/TimeBased/ZoneBased/Forced
- 区域自动设置遭遇表
- 目标追踪和距离计算
- Editor Gizmos 可视化

#### Battle Reward System (战斗结算系统)

**BattleRewardData** - 奖励数据定义
- `EnemyRewardConfig` - 敌人奖励配置 (Exp/Gold/JP)
- `ItemDropEntry` - 物品掉落条目
- `BattleRewardConfigSO` - 结算配置 ScriptableObject
- `ExpDistributionMode` - 经验分配模式 (EqualSplit/FullToAll/DamageContribution/KillBased/SurvivorsOnly)
- `JPDistributionMode` - JP 分配模式
- 等级差奖惩系统
- 精英/Boss 倍率加成
- 完美通关奖励

**BattleRewardManager** - 战斗结算管理器 (MonoSingleton)
- `Calculate()` - 计算奖励 (不发放)
- `GrantReward()` - 发放奖励
- `CalculateAndGrant()` - 一键计算并发放
- 角色升级/职业升级事件
- LootTable 集成点

### Integration Points

```
Encounter + TurnBased:
- EncounterResult.EnemyGroup -> TurnBasedBattle
- 遭遇触发启动回合制战斗

BattleReward + Party:
- BattleRewardManager -> PartyMember.AddExp()
- 经验/JP 分配给队伍成员

BattleReward + Job:
- JP 分配给当前职业
- 职业升级检测和事件

BattleReward + Economy:
- Gold -> CurrencyManager
- Items -> InventoryManager
```

---

## [2.0.0] - 2026-01-07

### Changed

#### 模块化架构重构

将 ZeroEngine 单包架构拆分为 **16 个模块化 UPM 包**，采用三层依赖架构：

**Layer 0 (无依赖)**:
- `com.zerogamestudio.zeroengine.core` (17 files) - Singleton, EventManager, Pool, Utils, Performance

**Layer 1 (依赖 Core)**:
- `com.zerogamestudio.zeroengine.data` (9 files) - StatSystem, BuffSystem
- `com.zerogamestudio.zeroengine.persistence` (7 files) - Save, Settings
- `com.zerogamestudio.zeroengine.ai` (47 files) - FSM, BehaviorTree, UtilityAI, GOAP, NPCSchedule

**Layer 2 (依赖 Layer 0-1)**:
- `com.zerogamestudio.zeroengine.combat` (46 files) - Combat.Core, DamageSystem, Targeting, Health
- `com.zerogamestudio.zeroengine.economy` (16 files) - Inventory, Loot, Shop, Crafting
- `com.zerogamestudio.zeroengine.ui` (10 files) - UI.Core, UI.MVVM
- `com.zerogamestudio.zeroengine.input` (1 file) - InputSystem
- `com.zerogamestudio.zeroengine.narrative` (47 files) - Dialog, Quest, Achievement
- `com.zerogamestudio.zeroengine.world` (8 files) - Environment, Calendar, Minimap
- `com.zerogamestudio.zeroengine.social` (5 files) - Relationship, Notification
- `com.zerogamestudio.zeroengine.character` (17 files) - Equipment, TalentTree
- `com.zerogamestudio.zeroengine.gameplay` (35 files) - Interaction, Tutorial, Command
- `com.zerogamestudio.zeroengine.audio` (5 files) - Audio
- `com.zerogamestudio.zeroengine.network` (7 files) - Network
- `com.zerogamestudio.zeroengine.localization` (3 files) - Localization

**旧主包保留模块** (`com.zerogamestudio.zeroengine`):
- Debugging/ - 调试工具
- ModSystem/ - Mod 系统
- SpineSkin/ - Spine 皮肤

### Migration Notes

- **API 保持不变** - 所有公共 API 不变，无需修改业务代码
- **命名空间保持不变** - 所有包仍使用 `ZeroEngine.*` 命名空间
- **按需引用** - 项目可按需引用模块包而非完整包
- **无循环依赖** - 通过拓扑排序验证

详见迁移报告: `.claude/specs/v2-migration-report.md`

---

## [1.17.0] - 2026-01-05

### Added

#### AI System (v1.17.0)

**Common (`AI/Common/`)**

- **IAIBrain**: AI 大脑通用接口
  - `IsActive` / `CurrentActionName` - 状态属性
  - `Initialize()` / `Tick()` / `ForceReevaluate()` - 生命周期
  - `StopCurrentAction()` / `Reset()` - 控制方法

- **AIContext**: AI 执行上下文
  - `Owner` / `Transform` / `Blackboard` - 核心引用
  - `CurrentTarget` / `TargetPosition` / `DistanceToTarget` - 目标信息
  - `IsInCombat` / `IsMoving` / `IsAlerted` - 状态标志
  - `GetCachedComponent<T>()` - 组件缓存

- **AIBlackboard**: AI 黑板数据共享
  - `Set<T>()` / `Get<T>()` / `TryGet<T>()` - 泛型操作
  - `CompareInt()` / `CompareFloat()` - 比较操作
  - `OnValueChanged` / `OnValueRemoved` - 变更事件

- **AIAgent**: AI 代理组件 (MonoBehaviour)
  - 多大脑管理和切换
  - 决策间隔和可见性暂停
  - `RegisterBrain()` / `SetActiveBrain()` / `SwitchToBrain<T>()`

**UtilityAI (`AI/UtilityAI/`)**

- **ResponseCurve**: 响应曲线
  - 11 种曲线类型: Linear/Quadratic/Polynomial/Logistic/Logit/Sine/Cosine/Exponential/Logarithmic/Threshold/Custom
  - `Evaluate()` - 输入映射到 0-1 评分
  - 工厂方法: `Linear()` / `Quadratic()` / `Logistic()` / `Threshold()` / `InverseLinear()`

- **Consideration**: 考量因素基类
  - `Weight` / `ResponseCurve` / `IsEnabled` - 配置属性
  - `Evaluate()` - 计算评分
  - 预设: `FixedConsideration` / `BlackboardConsideration` / `DistanceConsideration` / `CooldownConsideration` / `RandomConsideration`

- **UtilityAction**: 效用行动基类
  - 补偿乘法评分 (Compensated Multiplicative)
  - `CalculateScore()` / `Start()` / `Update()` / `Stop()`
  - 冷却系统和状态管理
  - 预设: `SimpleAction` / `DurationAction` / `ConditionalAction`

- **UtilityBrain**: 效用 AI 大脑 (MonoBehaviour, IAIBrain)
  - 行动评估和选择
  - 惯性加分和滞后切换
  - `OnActionChanged` / `OnEvaluationComplete` 事件

- **ZeroEngine Considerations**: 与引擎系统集成的考量
  - `StatConsideration` - StatSystem 属性值
  - `HealthConsideration` - Combat/Health 生命值
  - `BuffConsideration` - BuffSystem Buff 状态
  - `TimeConsideration` - TimeManager 时间
  - `CombatStateConsideration` - 战斗状态
  - `TargetCountConsideration` / `ResourceConsideration`

- **ZeroEngine Actions**: 预设行动
  - `IdleAction` / `WaitAction` - 空闲和等待
  - `MoveToTargetAction` / `MoveToPositionAction` - 移动
  - `PatrolAction` / `FleeAction` / `ReturnHomeAction` - 巡逻/逃跑/返回
  - `SetBlackboardAction` - 黑板操作

**GOAP (`AI/GOAP/`)** - 需要安装 crashkonijn/GOAP

- **GOAPBridge**: GOAP 桥接器
  - `IsGOAPInstalled` - 依赖检查
  - 安装方式: Git URL `https://github.com/crashkonijn/GOAP.git?path=Package#3.0.0`

- **ZeroGOAPAgent**: ZeroEngine GOAP Agent 包装器 (IAIBrain)
  - 黑板同步
  - AIContext 集成

- **ZeroGOAPGoal / ZeroGOAPAction**: 基类
  - `Context` 属性访问 AIContext
  - `GetBlackboardValue<T>()` / `SetBlackboardValue<T>()`

- **预设 Goals**: `SurviveGoal` / `AttackEnemyGoal` / `HealGoal` / `PatrolGoal` / `FleeGoal` / `IdleGoal` / `FollowScheduleGoal` / `ReturnHomeGoal`

- **预设 Actions**: `GOAPMoveToTargetAction` / `GOAPAttackAction` / `GOAPUseHealItemAction` / `GOAPFleeAction` / `GOAPFindTargetAction` / `GOAPWaitAction` / `GOAPReturnHomeAction`

**NPCSchedule (`AI/NPCSchedule/`)**

- **ScheduleEntry**: 日程条目
  - 时间范围: `StartHour` / `EndHour`
  - 日期过滤: `DayOfWeekMask` / `SeasonMask`
  - 条件和优先级
  - `IsActiveAtTime()` / `IsActiveOnDay()` / `IsActiveInSeason()` / `CheckConditions()`

- **ScheduleAction**: 日程行动基类
  - `Start()` / `Update()` / `End()` - 生命周期
  - `IsInterruptible` - 可中断标志
  - 预设: `IdleScheduleAction` / `WaitScheduleAction` / `AnimationScheduleAction` / `SequenceScheduleAction` / `RandomScheduleAction`

- **ScheduleCondition**: 日程条件基类
  - 预设: `WeatherCondition` / `RelationshipCondition` / `QuestCondition` / `ItemCondition` / `BlackboardCondition` / `ProbabilityCondition` / `CompositeCondition`

- **NPCScheduleController**: NPC 日程控制器 (MonoBehaviour, IAIBrain)
  - TimeManager 集成
  - 战斗覆盖支持
  - `OnScheduleChanged` / `OnActionStarted` / `OnActionEnded` 事件
  - `SetOverride()` / `ForceEntry()`

- **NPCScheduleSO**: 日程 ScriptableObject
  - 条目列表和默认行为
  - 预设支持
  - `GetActiveEntries()` / `Validate()`

- **SchedulePresetSO**: 日程预设 ScriptableObject
  - 可重用的日程模板
  - 分类: General/Shopkeeper/Farmer/Guard/Noble/Villager/Adventurer

- **预设 Schedule Actions**:
  - `MoveToScheduleAction` - 移动到位置
  - `SleepScheduleAction` - 睡眠恢复
  - `WorkScheduleAction` - 工作
  - `EatScheduleAction` - 进食
  - `SocializeScheduleAction` - 社交
  - `ShopkeeperScheduleAction` - 经营商店
  - `PatrolScheduleAction` - 巡逻

### Integration Notes

- **Unity Behavior**: 推荐使用 Unity 官方 `com.unity.behavior` 包作为基础行为树 (Unity 6+)
- **crashkonijn/GOAP**: 通过 `CRASHKONIJN_GOAP` 编译符号启用
- **StatSystem/BuffSystem/Combat**: 通过编译符号条件集成

## [1.16.0] - 2026-01-05

### Added

#### Projectile System (`Projectile/`)

- **ProjectileBase**: 弹道基类 (MonoBehaviour)
  - 轨迹类型: Linear/Parabolic/Homing/Spiral
  - 功能: 穿透/弹跳/爆炸/持续伤害
  - `Launch()` / `OnHit()` - 发射和命中回调

- **ProjectileManager**: 弹道管理器 (MonoSingleton)
  - 对象池管理
  - `Spawn()` / `Despawn()` - 生成/回收

- **ProjectileHitEffect / ProjectileTrailEffect**: 效果组件
  - 命中 VFX/音效/相机震动
  - 拖尾渲染管理

#### Spawner System (`Spawner/`)

**Core**
- **SpawnerBase**: 生成器抽象基类
- **SpawnManager**: 生成管理器 (MonoSingleton)
- **SpawnDataSO**: 生成配置 ScriptableObject
- **SpawnEvents**: 生成事件 (readonly struct 参数)

**Patterns**
- **WaveSpawner**: 波次生成器 (阶段/奖励)
- **AreaSpawner**: 区域生成器 (Box/Circle/Sphere)
- **PointSpawner**: 定点生成器 (预设点位)
- **TriggerSpawner**: 触发生成器 (碰撞触发)

**Conditions**
- **SpawnConditionBase / CompositeCondition**: 条件基类
- **TimeCondition**: 时间条件 (延迟/范围/周期)
- **KillCountCondition**: 击杀计数条件
- **TriggerCondition**: 触发条件 (距离/生命值/状态)

## [1.15.0] - 2026-01-05

### Added

#### Combat System (v1.15.0)

**Core Module (`Combat/Core/`)**

- **ICombatant**: 战斗单位接口
  - `CombatantId` / `DisplayName` / `TeamId` - 基础属性
  - `IsAlive` / `IsTargetable` - 状态属性
  - `TakeDamage()` / `ReceiveHeal()` - 伤害/治疗方法
  - `OnEnterCombat()` / `OnExitCombat()` - 战斗状态回调

- **CombatManager**: 战斗管理器 (MonoSingleton)
  - `RegisterCombatant()` / `UnregisterCombatant()` - 单位注册
  - `DealDamage()` / `Heal()` - 伤害/治疗处理
  - `StartCombat()` / `EndCombat()` - 战斗生命周期
  - `SetTeamRelation()` / `AreHostile()` / `AreFriendly()` - 队伍关系
  - `GetHostileTargetsInRange()` / `GetFriendlyTargetsInRange()` - 范围查询
  - 事件: `OnDamageDealt` / `OnHealApplied` / `OnKillOccurred`
  - 事件: `OnCombatStarted` / `OnCombatEnded`

- **CombatContext**: 战斗上下文
  - `State` / `Duration` / `Participants` - 状态属性
  - `AddParticipant()` / `RemoveParticipant()` - 参与者管理
  - `RecordDamageDealt()` / `RecordKill()` - 统计记录
  - `GetStatistics()` / `GetDPS()` - 统计查询
  - `SetData<T>()` / `GetData<T>()` - 自定义数据存储

- **CombatTeam**: 队伍/阵营定义
  - `TeamId` / `TeamName` / `TeamColor` - 基础属性
  - `Members` / `AliveCount` / `IsDefeated` - 成员管理

- **TeamRelationManager**: 队伍关系管理器
  - `SetRelation()` / `GetRelation()` - 关系设置
  - `IsHostile()` / `IsFriendly()` - 关系查询
  - `DefaultRelation` - 默认关系配置

- **CombatEvents**: 战斗事件常量和参数
  - `DamageEventArgs` / `HealEventArgs` / `KillEventArgs` - readonly struct
  - `CombatStateEventArgs` / `TargetChangedEventArgs`

**Damage Module (`Combat/Damage/`)**

- **DamageType**: 伤害类型枚举 (Flags)
  - 基础: `Physical` / `Magical` / `True`
  - 元素: `Fire` / `Ice` / `Lightning` / `Poison` / `Holy` / `Dark` / `Nature`
  - 特殊: `Chaos` / `Void`
  - 组合: `AllElemental` / `All`

- **DamageData**: 伤害数据结构
  - `BaseDamage` / `DamageType` / `SourceType` / `Flags` - 核心属性
  - `CritChance` / `CritMultiplier` - 暴击属性
  - `Source` / `AbilityId` / `ProjectileId` - 来源追踪
  - 工厂方法: `Physical()` / `Magical()` / `True()` / `DoT()` / `Environment()`

- **DamageResult**: 伤害结果 (readonly struct)
  - `FinalDamage` / `IsCritical` / `IsDodged` / `IsKill` - 结果属性
  - `AbsorbedDamage` / `LifestealAmount` - 特殊效果
  - 静态方法: `Dodged()` / `Immune()` / `Absorbed()`

- **DamageCalculator**: 伤害计算器
  - `FormulaConfig` - 公式配置 (护甲/抗性/暴击/闪避)
  - `RegisterProcessor()` / `UnregisterProcessor()` - 处理器管理
  - `Calculate()` - 执行伤害计算管线
  - `CalculateArmorReduction()` / `CalculateResistanceReduction()` - 减免计算

- **IDamageProcessor**: 伤害处理器接口
  - `Priority` - 处理优先级
  - `ProcessDamage()` - 处理伤害数据

- **DamageCalculationContext**: 计算上下文
  - `GetAttackerStat` / `GetDefenderStat` - 属性获取器
  - `IsCritical` / `IsDodged` / `IsImmune` - 计算结果
  - `DamageMultiplier` / `DamageReduction` / `AbsorbAmount` - 修正值

- **DamageModifier**: 伤害修正器
  - `DamageBonus()` / `DamageReduction()` - 伤害增减
  - `CritChance()` / `CritDamage()` - 暴击修正
  - `ArmorPenetration()` / `ElementalResistance()` - 穿透/抗性

**Targeting Module (`Combat/Targeting/`)**

- **ITargetable**: 可选中目标接口
  - `TargetId` / `IsTargetable` / `TeamId` / `TargetPriority` - 基础属性
  - `GetTargetCenter()` / `GetTargetRadius()` - 碰撞信息

- **TargetableBase**: 可选中目标基类 (MonoBehaviour)
  - 实现 `ITargetable` 接口
  - `SetTargetable()` / `SetAlive()` / `SetTeamId()` - 状态设置

- **TargetSelector**: 目标选择器
  - `SelectTarget()` / `SelectTargets()` - 选择方法
  - `AddExclude()` / `RemoveExclude()` - 排除管理
  - `Config` / `CustomScoreFunc` - 配置

- **TargetSelectorConfig**: 选择器配置
  - `RelationType` - 目标关系 (Any/Hostile/Friendly/Self)
  - `Strategy` - 选择策略 (Nearest/Farthest/LowestHealth/Random/Custom)
  - `MaxRange` / `RequireLineOfSight` - 范围/视线配置

- **TargetFilter**: 目标过滤器 (静态工具类)
  - `FilterAlive()` / `FilterTargetable()` - 状态过滤
  - `FilterHostile()` / `FilterFriendly()` - 关系过滤
  - `FilterInRange()` / `FilterInCone()` / `FilterInRect()` - 范围过滤
  - `FilterWithLineOfSight()` - 视线过滤

- **TargetPriority**: 优先级策略 (静态工具类)
  - `SortByDistance()` / `SortByPriority()` / `SortByScore()` - 排序
  - `GetNearest()` / `GetFarthest()` / `GetRandom()` - 快速选择
  - `GetRandomMultiple()` - 随机多选

- **RangeChecker**: 范围检测工具 (静态工具类)
  - `IsInCircle()` / `IsInCone()` / `IsInRect()` - 形状检测
  - `GetDistance()` / `GetSqrDistance()` - 距离计算
  - `HasLineOfSight()` - 视线检测
  - `OverlapSphere()` / `OverlapBox()` - 物理检测

- **RangeShape**: 范围形状定义
  - `ShapeType` - 形状类型 (Circle/Cone/Rectangle)
  - `Contains()` - 检查点是否在范围内

**Health Module (`Combat/Health/`)**

- **IHealth**: 生命值接口
  - `CurrentHealth` / `MaxHealth` / `HealthPercent` - 生命值属性
  - `IsAlive` / `IsFullHealth` / `IsInvulnerable` - 状态属性
  - `TakeDamage()` / `Heal()` / `SetHealth()` - 操作方法
  - 事件: `OnHealthChanged` / `OnDamageTaken` / `OnHealed` / `OnDeath` / `OnRevived`

- **HealthComponent**: 生命值组件 (MonoBehaviour)
  - 实现 `IHealth` 接口
  - `TakePercentDamage()` / `HealPercent()` - 百分比操作
  - `Revive()` - 复活方法
  - `_allowOverkill` / `_minDamage` - 配置选项

- **DamageReceiver**: 伤害接收器 (MonoBehaviour)
  - `ReceiveDamage()` - 接收伤害 (带命中点/法线)
  - `HitZoneName` / `HitZoneMultiplier` - 部位伤害
  - `RegisterModifier()` / `UnregisterModifier()` - 修正器管理

- **DeathHandler**: 死亡处理器 (MonoBehaviour)
  - `DeathBehavior` - 死亡行为 (None/Disable/Destroy/Pool/Custom)
  - 视觉效果: `_deathEffectPrefab` / `_enableRagdoll`
  - 音效: `_deathSound`
  - 掉落: `_triggerLoot` / `_lootTableId`
  - 事件: `OnBeforeDeath` / `OnAfterDeath`

- **HealthChangeEventArgs**: 生命值变化事件参数 (readonly struct)
- **DeathEventArgs**: 死亡事件参数 (readonly struct)

### Changed

- 更新 CLAUDE.md 添加 Combat 模块文档
- 更新 ROADMAP.md 标记 v1.15.0 完成

---

## [1.14.0] - 2026-01-05

### Added

#### Interaction System (v1.14.0)

- **IInteractable**: 可交互对象接口
  - `InteractableId` / `DisplayName` / `Type` - 基础属性
  - `CanInteract()` / `OnInteract()` - 交互方法
  - `OnFocus()` / `OnUnfocus()` - 焦点回调
  - `GetInteractionPosition()` - 获取交互位置
  - `GameObject` - 获取 GameObject 引用

- **InteractableBase**: 可交互对象基类 (MonoBehaviour)
  - 实现 IInteractable 接口
  - `Conditions` - 交互条件列表 ([SerializeReference])
  - `OnInteractionSuccess` / `OnInteractionFailed` - UnityEvent
  - `SetEnabled()` / `IsInteractionEnabled` - 启用/禁用控制
  - 自动注册/注销到 InteractionManager

- **InteractionManager**: 交互管理器 (MonoSingleton)
  - `CurrentTarget` - 当前交互目标
  - `FindById()` / `GetNearbyInteractables()` - 查询方法
  - `TryInteract()` - 尝试交互
  - `Register()` / `Unregister()` - 注册/注销
  - 事件: `OnInteractableEnterRange` / `OnInteractableExitRange`
  - 事件: `OnInteractionStarted` / `OnInteractionCompleted` / `OnInteractionFailed`

- **InteractionDetector**: 交互检测器 (玩家组件)
  - `DetectionRadius` / `InteractableLayer` - 检测配置
  - `InteractKey` - 交互按键
  - `CurrentTarget` - 当前目标
  - 事件: `OnTargetChanged`
  - 使用 Physics.OverlapSphereNonAlloc 避免 GC

- **InteractionCondition**: 交互条件基类 ([SerializeReference])
  - `ItemRequiredCondition` - 需要道具
  - `QuestStateCondition` - 任务状态条件
  - `TimeCondition` - 时间条件
  - `RelationshipCondition` - 好感度条件
  - `DistanceCondition` - 距离条件
  - `CooldownCondition` - 冷却条件

- **可交互类型**:
  - `InteractableItem` - 可拾取物品 (背包集成)
  - `InteractableNPC` - NPC (对话/商店/任务)
  - `InteractableDoor` - 门/传送点/场景切换
  - `InteractableChest` - 宝箱 (Loot 系统集成)
  - `InteractableSwitch` - 开关/机关
  - `InteractableCraftStation` - 工作台

- **InteractionConfigSO**: 全局配置 ScriptableObject
- **InteractionPromptUI**: 交互提示 UI 组件

#### Tutorial System (v1.14.0)

- **TutorialStep**: 教程步骤基类 ([SerializeReference])
  - `StepId` / `Description` / `CanSkip` - 基础属性
  - `OnEnter()` / `OnUpdate()` / `OnExit()` - 生命周期
  - `IsCompleted()` - 完成检查
  - `OnSkip()` / `Validate()` - 跳过和验证

- **步骤类型**:
  - `DialogueStep` - 显示对话 (打字机效果)
  - `HighlightStep` - 高亮 UI 元素
  - `WaitInputStep` - 等待玩家输入
  - `WaitInteractionStep` - 等待交互 (Interaction 系统集成)
  - `WaitEventStep` - 等待事件触发
  - `MoveToStep` - 引导移动
  - `DelayStep` - 延时
  - `CallbackStep` - 自定义回调
  - `CompositeStep` - 组合步骤 (Sequential/Parallel/Any)

- **TutorialSequenceSO**: 教程序列 ScriptableObject
  - `SequenceId` / `DisplayName` / `Category` - 基础属性
  - `AutoStart` / `Skippable` - 行为配置
  - `StartConditions` - 启动条件列表
  - `Steps` - 步骤列表 ([SerializeReference])
  - `Rewards` - 完成奖励

- **TutorialManager**: 教程管理器 (MonoSingleton + ISaveable)
  - `IsRunning` / `CurrentSequence` / `CurrentStep` - 状态属性
  - `StartSequence()` / `SkipCurrentStep()` / `SkipCurrentSequence()` - 控制方法
  - `IsSequenceCompleted()` / `ResetProgress()` - 进度查询
  - 事件: `OnSequenceStarted` / `OnSequenceCompleted` / `OnSequenceSkipped`
  - 事件: `OnStepStarted` / `OnStepCompleted`
  - ISaveable: 教程进度持久化

- **TutorialCondition**: 教程条件基类 ([SerializeReference])
  - `FirstTimeCondition` - 首次进入
  - `LevelCondition` - 等级条件
  - `TutorialCompletedCondition` - 教程完成条件
  - `SceneCondition` - 场景条件
  - `QuestCondition` - 任务条件
  - `VariableCondition` - 变量条件

- **TutorialConfigSO**: 全局配置 ScriptableObject
  - UI 动画、对话、高亮、箭头、进度等配置

#### Tutorial UI (v1.14.0)

- **TutorialUIManager**: 教程 UI 管理器 (MonoSingleton)
  - `ShowDialogue()` / `HideDialogue()` - 对话控制
  - `ShowHighlight()` / `HideHighlight()` - 高亮控制
  - `ShowWorldArrow()` / `HideWorldArrow()` - 箭头控制
  - `ShowProgress()` / `HideProgress()` - 进度控制
  - 音效播放 API

- **TutorialDialogueView**: 对话视图
  - 打字机效果
  - 多选项支持
  - 淡入淡出动画

- **TutorialHighlightView**: 高亮遮罩视图
  - 圆形/矩形/手指/箭头/脉冲高亮类型
  - 实时跟踪目标位置
  - 点击检测

- **TutorialArrowUI**: 箭头指示器
  - 世界空间 3D 箭头
  - 屏幕边缘 2D 箭头
  - 混合模式 (屏幕内/外自动切换)
  - 距离显示

- **TutorialProgressView**: 进度指示器
  - 步骤进度显示
  - 多种位置选项
  - 完成动画

#### Tutorial GraphView Editor (v1.14.0)

- **TutorialGraphEditorWindow**: 主编辑器窗口
  - 菜单: ZeroEngine > Tutorial > Tutorial Graph Editor
  - 双击 TutorialSequenceSO 自动打开
  - 创建/打开/保存序列
  - 验证功能

- **TutorialGraphView**: GraphView 组件
  - 可视化步骤连接
  - 右键菜单添加步骤
  - 拖拽/缩放/选择
  - Ctrl+D 复制步骤

- **TutorialGraphNode**: 可视化节点
  - 不同步骤类型不同颜色
  - 显示关键属性预览
  - 选中时显示详细 Inspector

- **TutorialNodeInspector**: 节点属性面板
  - 分步骤类型的属性编辑
  - 实时预览更新

---

## [1.13.0] - 2026-01-04

### Added

#### Environment System (v1.13.0)

- **WeatherManager**: 天气系统管理器 (MonoSingleton + ISaveable)
  - `CurrentWeather` / `CurrentWeatherType` - 当前天气状态
  - `SetWeather(WeatherPresetSO)` / `SetWeather(WeatherType)` - 设置天气
  - `ClearWeather()` - 清除天气效果
  - `SetFollowTarget()` - 设置 VFX 跟随目标
  - `RegisterPreset()` - 注册天气预设
  - `GetPreset()` - 获取指定类型预设
  - VFX 粒子效果管理 (跟随相机)
  - 雾效平滑过渡
  - 环境音效管理 (淡入淡出)
  - 事件: `OnEnvironmentEvent`
  - ISaveable: 天气状态持久化

- **TimeManager**: 游戏时间管理器 (MonoSingleton + ISaveable)
  - `CurrentHour` / `NormalizedTime` / `CurrentHourInt` - 时间属性
  - `CurrentTimeOfDay` - 当前时间段 (Dawn/Day/Dusk/Night)
  - `SetTime()` / `SetTimeOfDay()` - 设置时间
  - `Pause()` / `Resume()` / `SetPaused()` - 时间流控制
  - `AdvanceHours()` - 推进时间
  - `TimeScale` - 时间流速 (1秒现实 = N秒游戏)
  - `GetFormattedTime()` - 格式化时间字符串
  - 事件: `OnTimeChanged` / `OnHourChanged` / `OnTimeOfDayChanged` / `OnEnvironmentEvent`
  - ISaveable: 时间状态持久化

- **LightingManager**: 光照控制器 (MonoSingleton)
  - `Preset` / `SunLight` - 配置和太阳光源
  - `ForceRefresh()` - 强制刷新光照
  - `SetSmoothTransition()` - 设置平滑过渡
  - 太阳颜色/强度随时间变化 (Gradient + AnimationCurve)
  - 太阳旋转模拟 (日出到日落弧线)
  - 环境光和雾效联动
  - 平滑过渡或直接切换

- **WeatherPresetSO**: 天气预设 ScriptableObject
  - `WeatherType` - 8 种天气类型 (Clear/Cloudy/Rain/Snow/Fog/Storm/Sandstorm/Custom)
  - `VfxPrefab` / `VfxOffset` - VFX 粒子配置
  - `OverrideFog` / `EnableFog` / `FogColor` / `FogDensity` - 雾效配置
  - `LightIntensityMultiplier` - 光照强度系数
  - `TransitionDuration` - 过渡时长
  - `AmbientSound` / `AmbientVolume` - 环境音效

- **DayNightPresetSO**: 昼夜预设 ScriptableObject
  - `SunColorOverDay` - 太阳颜色曲线 (Gradient)
  - `SunIntensityOverDay` - 太阳强度曲线 (AnimationCurve)
  - `MaxSunIntensity` - 最大太阳强度
  - `SunriseAngle` / `SunsetAngle` - 日出/日落角度
  - `AmbientColorOverDay` - 环境光颜色曲线
  - `FogColorOverDay` / `EnableFog` - 雾效配置
  - `CreateDefault()` - 创建默认预设
  - `EvaluateSunColor()` / `EvaluateSunIntensity()` / `EvaluateAmbientColor()` / `EvaluateFogColor()` - 评估方法

- **EnvironmentEnums**: 环境系统枚举和数据类
  - `WeatherType` - 8 种天气类型
  - `TimeOfDay` - 4 种时间段 (Dawn/Day/Dusk/Night)
  - `EnvironmentEventType` - 6 种事件类型
  - `EnvironmentEventArgs` - 环境事件参数
  - `WeatherPresetData` / `DayNightPresetData` - 预设数据结构

#### Minimap System (v1.13.0)

- **MinimapController**: 小地图控制器 (MonoSingleton)
  - `MinimapCamera` / `RenderTexture` - 相机和渲染纹理
  - `FollowTarget` - 跟随目标
  - `ZoomMode` - 缩放模式 (Fixed/Dynamic/Manual)
  - `RotationMode` - 旋转模式 (NorthUp/PlayerUp)
  - `OrthographicSize` / `CurrentZoom` - 当前缩放
  - `SetZoom()` / `ZoomIn()` / `ZoomOut()` - 缩放控制
  - `GetIconConfig()` - 获取标记图标配置
  - `WorldToMinimapNormalized()` - 世界坐标转小地图坐标
  - `IsInMinimapBounds()` - 检查是否在范围内
  - `GetVisibleMarkers()` - 获取可见标记
  - `RefreshCamera()` - 刷新相机设置
  - 事件: `OnMinimapEvent`
  - 自动创建 RenderTexture 和 Camera

- **MinimapMarker**: 小地图标记组件
  - `MarkerType` - 12 种标记类型 (Player/PartyMember/Enemy/NPC/Quest/Waypoint/Building/Portal/Item/Treasure/Shop/Custom)
  - `CustomIcon` / `IconColor` / `IconScale` - 图标配置
  - `AlwaysVisible` / `VisibleRange` - 可见范围控制
  - `RotateWithObject` - 是否跟随对象旋转
  - `Label` / `ShowLabel` - 标签配置
  - `IsVisibleFrom()` - 检查可见性
  - `GetRotation()` - 获取旋转角度
  - `Setup()` - 配置标记
  - 自动注册/注销到 MinimapMarkerManager

- **MinimapMarkerManager**: 静态标记管理器
  - `Markers` - 所有活动标记列表 (IReadOnlyList)
  - `MarkerCount` - 标记数量
  - `Register()` / `Unregister()` - 注册/注销标记
  - `GetMarkersOfType()` - 按类型获取标记
  - `GetVisibleMarkers()` - 获取可见标记
  - `FindNearest()` - 查找最近标记
  - `ClearAll()` - 清除所有标记
  - 事件: `OnMarkerRegistered` / `OnMarkerUnregistered`

- **MinimapEnums**: 小地图系统枚举和数据类
  - `MinimapMarkerType` - 12 种标记类型
  - `MinimapZoomMode` - 3 种缩放模式
  - `MinimapRotationMode` - 2 种旋转模式
  - `MinimapEventType` - 4 种事件类型
  - `MinimapEventArgs` - 小地图事件参数
  - `MarkerIconConfig` - 标记图标配置

### New Files

- `Runtime/Environment/EnvironmentEnums.cs`
- `Runtime/Environment/WeatherManager.cs`
- `Runtime/Environment/TimeManager.cs`
- `Runtime/Environment/LightingManager.cs`
- `Runtime/Environment/WeatherPresetSO.cs`
- `Runtime/Environment/DayNightPresetSO.cs`
- `Runtime/Minimap/MinimapEnums.cs`
- `Runtime/Minimap/MinimapController.cs`
- `Runtime/Minimap/MinimapMarker.cs`

### Architecture

```
Environment System (v1.13.0)
├── WeatherType / TimeOfDay            # 枚举
├── EnvironmentEventArgs               # 事件参数
├── WeatherPresetData / DayNightData   # 数据结构
├── WeatherPresetSO                    # 天气预设 SO
├── DayNightPresetSO                   # 昼夜预设 SO
├── TimeManager                        # 时间管理器 (ISaveable)
│   ├── 24h Cycle                      # 24 小时循环
│   ├── Time of Day Events             # 时间段事件
│   └── Time Control                   # 时间控制
├── WeatherManager                     # 天气管理器 (ISaveable)
│   ├── VFX Management                 # VFX 管理
│   ├── Fog Transition                 # 雾效过渡
│   └── Ambient Audio                  # 环境音效
└── LightingManager                    # 光照管理器
    ├── Sun Color/Intensity            # 太阳颜色/强度
    ├── Sun Rotation                   # 太阳旋转
    ├── Ambient Light                  # 环境光
    └── Fog Sync                       # 雾效同步

Minimap System (v1.13.0)
├── MinimapMarkerType                  # 12 种标记类型
├── MinimapZoomMode / RotationMode     # 模式枚举
├── MinimapEventArgs                   # 事件参数
├── MarkerIconConfig                   # 图标配置
├── MinimapMarker                      # 标记组件
│   ├── Visibility Control             # 可见性控制
│   └── Icon/Label Config              # 图标/标签配置
├── MinimapMarkerManager               # 静态标记管理器
│   └── Marker Queries                 # 标记查询
└── MinimapController                  # 小地图控制器
    ├── Camera Setup                   # 相机设置
    ├── RenderTexture                  # 渲染纹理
    ├── Follow Target                  # 目标跟随
    ├── Zoom Control                   # 缩放控制
    └── Rotation Modes                 # 旋转模式
```

### Usage Example

```csharp
// === Environment System ===
using ZeroEngine.Environment;

// --- 时间管理 ---
// 获取当前时间
float hour = TimeManager.Instance.CurrentHour;
TimeOfDay period = TimeManager.Instance.CurrentTimeOfDay;
Debug.Log($"Time: {TimeManager.Instance.GetFormattedTime()} ({period})");

// 设置时间
TimeManager.Instance.SetTime(12f);  // 正午
TimeManager.Instance.SetTimeOfDay(TimeOfDay.Night);

// 时间控制
TimeManager.Instance.TimeScale = 120f;  // 1秒现实 = 2分钟游戏
TimeManager.Instance.Pause();
TimeManager.Instance.Resume();

// 监听时间变化
TimeManager.Instance.OnTimeOfDayChanged += timeOfDay =>
{
    Debug.Log($"Time of day changed to: {timeOfDay}");
};

// --- 天气管理 ---
// 设置天气
WeatherManager.Instance.SetWeather(WeatherType.Rain);
WeatherManager.Instance.SetWeather(myRainPreset);

// 获取当前天气
var weather = WeatherManager.Instance.CurrentWeatherType;

// 清除天气
WeatherManager.Instance.ClearWeather();

// 监听天气变化
WeatherManager.Instance.OnEnvironmentEvent += args =>
{
    if (args.Type == EnvironmentEventType.WeatherChanged)
        Debug.Log($"Weather: {args.PreviousWeather} -> {args.Weather}");
};

// --- 光照管理 ---
// 设置预设
LightingManager.Instance.Preset = myDayNightPreset;
LightingManager.Instance.SunLight = directionalLight;

// 控制过渡
LightingManager.Instance.SetSmoothTransition(true, 0.5f);

// 强制刷新
LightingManager.Instance.ForceRefresh();

// === Minimap System ===
using ZeroEngine.Minimap;

// --- 小地图控制 ---
// 缩放控制
MinimapController.Instance.SetZoom(50f);
MinimapController.Instance.ZoomIn(10f);
MinimapController.Instance.ZoomOut(10f);

// 设置跟随目标
MinimapController.Instance.FollowTarget = playerTransform;

// 设置模式
MinimapController.Instance.ZoomMode = MinimapZoomMode.Manual;
MinimapController.Instance.RotationMode = MinimapRotationMode.PlayerUp;

// 获取 RenderTexture (用于 UI)
rawImage.texture = MinimapController.Instance.RenderTexture;

// 坐标转换
Vector2 pos = MinimapController.Instance.WorldToMinimapNormalized(worldPos);
bool inBounds = MinimapController.Instance.IsInMinimapBounds(worldPos);

// --- 标记管理 ---
// 添加标记组件到游戏对象
var marker = npc.AddComponent<MinimapMarker>();
marker.Setup(MinimapMarkerType.NPC, npcIcon, Color.green);
marker.AlwaysVisible = false;
marker.VisibleRange = 50f;

// 查询标记
var enemies = new List<MinimapMarker>();
MinimapMarkerManager.GetMarkersOfType(MinimapMarkerType.Enemy, enemies);

// 查找最近标记
var nearestQuest = MinimapMarkerManager.FindNearest(playerPos, MinimapMarkerType.Quest);

// 监听标记事件
MinimapMarkerManager.OnMarkerRegistered += marker =>
{
    Debug.Log($"Marker added: {marker.MarkerType}");
};
```

### Migration Notes

- **从 ZGSProject_5 迁移**:
  - 移除了 Odin Inspector 依赖，使用标准 Unity 序列化
  - 使用 `MonoSingleton<T>` 替代自定义单例实现
  - 添加 `ISaveable` 接口支持存档系统集成
  - DOTween 动画替换为协程实现，减少第三方依赖

### Changed (v1.13.0 Patch - 2026-01-05)

#### Environment 模块重构

- **命名空间变更**: `ZeroEngine.Environment` -> `ZeroEngine.EnvironmentSystem`
  - 避免与系统命名空间冲突
- **文件合并**: `TimeManager.cs` 现包含所有类型定义
  - 合并 `EnvironmentEnums.cs` (枚举定义)
  - 合并 `WeatherPresetSO.cs` (天气预设)
  - 合并 `DayNightPresetSO.cs` (昼夜预设)
- **删除文件**:
  - `Runtime/Environment/EnvironmentEnums.cs`
  - `Runtime/Environment/WeatherPresetSO.cs`
  - `Runtime/Environment/DayNightPresetSO.cs`

#### API 兼容性修复

- **LootCondition.cs**:
  - `QuestCondition.Check()` 使用 `HasActiveQuest()` / `GetQuestState()` 替代旧 API
- **LootTableManager.cs**:
  - 使用 `EventManager.Trigger()` 替代 `TriggerEvent()`
- **ShopManager.cs**:
  - 使用 `IsFull` / `SellPrice` / `Id` 替代旧属性名
- **ShopItemSO.cs**:
  - 使用 `Item.Id` 替代 `Item.ItemId`
- **DialogIntegrationHandler.cs**:
  - 修复 `RemoveItem()` 返回值处理 (bool -> InventoryItem?)
- **SettingsEnums.cs**:
  - `ResolutionData.Apply()` 使用新 `RefreshRate` 结构体 API

#### Singleton 基类兼容

- **新增别名**: `MonoSingleton<T>` 作为 `Singleton<T>` 的别名
  - 保持与常见命名约定的兼容性
- **Manager OnDestroy**: 10 个 Manager 类添加 `OnDestroy` override
  - 正确调用 `base.OnDestroy()` 清理单例引用
  - 防止退出时的空引用警告

### Performance Notes

- **评分**: 8.5/10 (v1.13.0 patch 优化后)
- **[已修复]** `EnvironmentEventArgs` 改为 `readonly struct`，消除堆分配
- **[已修复]** `TimeManager.Update()` 不再每帧触发 `OnEnvironmentEvent(TimeChanged)`
  - 每帧时间更新使用 `OnTimeChanged` 事件 (简单 float，无 boxing)
  - `OnEnvironmentEvent` 仅在整点变更和时间段变更时触发 (低频)
- **事件选择指南**:
  - 需要每帧时间 → 使用 `OnTimeChanged` (零 GC)
  - 需要整点通知 → 使用 `OnHourChanged` 或 `OnEnvironmentEvent(HourChanged)`
  - 需要时间段变化 → 使用 `OnTimeOfDayChanged` 或 `OnEnvironmentEvent(TimeOfDayChanged)`

### Updated Files

| 文件 | 变更类型 | 说明 |
|------|----------|------|
| `Runtime/Environment/TimeManager.cs` | 修改 | 合并所有类型定义，命名空间变更 |
| `Runtime/Environment/LightingManager.cs` | 修改 | 命名空间变更 |
| `Runtime/Environment/WeatherManager.cs` | 修改 | 命名空间变更 |
| `Runtime/Loot/LootCondition.cs` | 修改 | Quest API 兼容 |
| `Runtime/Loot/LootTableManager.cs` | 修改 | EventManager API 兼容 |
| `Runtime/Shop/ShopManager.cs` | 修改 | Inventory API 兼容 |
| `Runtime/Shop/ShopItemSO.cs` | 修改 | Item.Id 属性名 |
| `Runtime/Dialog/DialogIntegrationHandler.cs` | 修改 | RemoveItem 返回值 |
| `Runtime/Settings/SettingsEnums.cs` | 修改 | RefreshRate 结构体 |
| `Runtime/Core/Singleton.cs` | 修改 | 添加 MonoSingleton 别名 |

---

## [1.12.0] - 2026-01-04

### Added

#### Calendar System (v1.12.0)

- **CalendarManager**: 日历系统管理器 (MonoSingleton + ISaveable)
  - `CurrentDate` / `CurrentTime` - 当前游戏日期和时间
  - `CurrentSeason` / `CurrentYear` / `CurrentMonth` / `CurrentDay` - 日期属性
  - `SetTimeFlow()` / `PauseTime()` / `ResumeTime()` - 时间流控制
  - `SetTimeScale()` - 设置时间流速
  - `AdvanceTime()` / `AdvanceDays()` - 推进时间
  - `SetDate()` / `SetTime()` - 设置日期时间
  - `GetEventsOnDate()` / `GetUpcomingEvents()` - 事件查询
  - `IsEventActive()` / `ClaimEventReward()` - 事件状态
  - 事件: `OnCalendarEvent` (DayChanged/SeasonChanged/EventStarted/EventEnded/Reminder)
  - ISaveable: 日期时间和事件状态持久化

- **GameDate**: 游戏日期结构体
  - `Year` / `Month` / `Day` - 日期组件
  - `TotalDays` / `Season` / `DayOfWeek` - 计算属性
  - `AddDays()` - 日期计算
  - 完整比较运算符支持

- **GameTime**: 游戏时间结构体
  - `Hour` / `Minute` - 时间组件
  - `TotalMinutes` - 总分钟数

- **CalendarEventData**: 日历事件数据
  - 6 种事件类型: OneTime/Daily/Weekly/Monthly/Yearly/Seasonal
  - `IsActiveOn()` - 检查事件在指定日期是否活跃
  - 条件/奖励/提醒配置

- **CalendarEventSO**: 日历事件 ScriptableObject

#### Notification System (v1.12.0)

- **NotificationManager**: 通知系统管理器 (MonoSingleton)
  - `Show()` - 显示通知 (多种重载)
  - `ShowSuccess()` / `ShowWarning()` / `ShowError()` - 快捷方法
  - `ShowAchievement()` / `ShowReward()` - 特殊通知
  - `Hide()` / `HideAll()` / `HideAllOfType()` - 隐藏通知
  - `ClickNotification()` - 点击处理
  - `SetMaxVisibleCount()` / `SetDefaultDuration()` - 配置
  - `DisableType()` / `EnableType()` / `SetMinPriority()` - 过滤
  - 事件: `OnNotificationEvent` (Shown/Hidden/Clicked/Expired/Queued)
  - 通知队列和优先级系统

- **NotificationData**: 通知数据类
  - `Title` / `Message` / `Icon` - 内容
  - `Type` / `Priority` / `Position` / `Animation` - 显示设置
  - `Duration` / `Closable` / `Clickable` - 行为设置
  - `OnClick` / `OnClose` - 回调

- **NotificationBuilder**: 流式 API 构建器
  - 链式调用创建通知: `Create().SetType().SetDuration().Show()`

- **NotificationConfig**: 通知系统配置
  - 显示/动画/音效/过滤设置

#### Settings System (v1.12.0)

- **SettingsManager**: 游戏设置管理器 (MonoSingleton + ISaveable)
  - `GetBool()` / `GetInt()` / `GetFloat()` / `GetString()` - 获取设置
  - `SetBool()` / `SetInt()` / `SetFloat()` / `SetString()` - 设置值
  - `ApplyChanges()` / `DiscardChanges()` - 应用/取消更改
  - `ResetCategory()` / `ResetAll()` - 重置设置
  - `ApplyGraphicsPreset()` - 应用图形预设 (Low/Medium/High/Ultra)
  - `GetKeyBinding()` / `SetKeyBinding()` / `ResetKeyBindings()` - 按键绑定
  - `IsActionPressed()` / `IsActionHeld()` - 按键检查
  - `GetDefinition()` / `GetSettingsByCategory()` - 设置查询
  - 事件: `OnSettingsEvent` (ValueChanged/Applied/Reset/Saved/Loaded)
  - ISaveable: 设置值和按键绑定持久化

- **SettingDefinition**: 设置项定义
  - 9 种设置分类: General/Graphics/Audio/Controls/Gameplay/Accessibility/Language/Network/Advanced
  - 9 种值类型: Bool/Int/Float/String/Enum/KeyBinding/Resolution/Slider/Color
  - 约束/选项/联动配置

- **KeyBindingData**: 按键绑定数据
  - `PrimaryKey` / `SecondaryKey` / `Modifier` - 按键配置
  - `IsPressed()` / `IsHeld()` - 输入检查
  - `GetDisplayString()` - 显示字符串

- **ResolutionData**: 分辨率数据
  - `Width` / `Height` / `RefreshRate` / `Fullscreen`
  - `Apply()` - 应用分辨率

- **GraphicsPresets**: 预设图形设置 (Low/Medium/High/Ultra)

- **SettingsDefinitionSO**: 设置定义 ScriptableObject

### New Files

- `Runtime/Calendar/CalendarEnums.cs`
- `Runtime/Calendar/CalendarManager.cs`
- `Runtime/Notification/NotificationEnums.cs`
- `Runtime/Notification/NotificationManager.cs`
- `Runtime/Settings/SettingsEnums.cs`
- `Runtime/Settings/SettingsManager.cs`

### Architecture

```
Calendar System (v1.12.0)
├── GameDate / GameTime          # 日期时间结构体
├── CalendarEventData            # 事件数据
├── CalendarEventSO              # 事件 SO
└── CalendarManager              # 管理器 (ISaveable)
    ├── Time Flow Control        # 时间流控制
    ├── Event System             # 事件系统
    └── Season/Day Change Events # 日期变更事件

Notification System (v1.12.0)
├── NotificationData             # 通知数据
├── NotificationConfig           # 配置
├── NotificationBuilder          # 流式 API
└── NotificationManager          # 管理器
    ├── Queue System             # 队列系统
    ├── Priority Filtering       # 优先级过滤
    └── Type Filtering           # 类型过滤

Settings System (v1.12.0)
├── SettingDefinition            # 设置定义
├── KeyBindingData               # 按键绑定
├── ResolutionData               # 分辨率
├── GraphicsPresets              # 图形预设
├── SettingsDefinitionSO         # 设置定义 SO
└── SettingsManager              # 管理器 (ISaveable)
    ├── Get/Set Values           # 值存取
    ├── Apply/Discard Changes    # 应用/取消
    ├── Key Bindings             # 按键绑定
    └── Graphics Presets         # 图形预设
```

### Usage Example

```csharp
// === Calendar System ===
using ZeroEngine.Calendar;

// 获取当前日期
var date = CalendarManager.Instance.CurrentDate;
Debug.Log($"Today: {date} ({date.Season})");

// 推进时间
CalendarManager.Instance.AdvanceDays(7);

// 监听日期变化
CalendarManager.Instance.OnCalendarEvent += args =>
{
    if (args.Type == CalendarSystemEventType.SeasonChanged)
        Debug.Log($"Season: {args.OldSeason} -> {args.Season}");
};

// === Notification System ===
using ZeroEngine.Notification;

// 简单通知
NotificationManager.Instance.ShowSuccess("Level Up!", "You reached level 10!");

// 流式 API
NotificationManager.Create("Quest Complete", "Collected all items")
    .SetType(NotificationType.Quest)
    .SetDuration(5f)
    .SetIcon(questIcon)
    .OnClick(() => OpenQuestLog())
    .Show();

// === Settings System ===
using ZeroEngine.Settings;

// 获取/设置值
float volume = SettingsManager.Instance.GetFloat("master_volume", 1f);
SettingsManager.Instance.SetFloat("master_volume", 0.8f);

// 应用图形预设
SettingsManager.Instance.ApplyGraphicsPreset("High");

// 按键绑定
SettingsManager.Instance.SetKeyBinding("jump", KeyCode.Space, KeyCode.W);
if (SettingsManager.Instance.IsActionPressed("jump"))
    Jump();
```

---

## [1.11.0] - 2026-01-04

### Added

#### Loot Table System (v1.11.0)

- **LootTableSO**: 掉落表数据定义 (ScriptableObject)
  - `TableId` / `DisplayName` - 唯一标识和显示名称
  - `DropMode` - 掉落模式 (Weight/Pity/Layered)
  - `DropCount` - 每次掉落数量
  - `Entries` - 掉落条目列表 (LootEntry)
  - `Layers` - 分层配置 (Layered 模式)
  - `GuaranteedDrops` - 保底掉落
  - `GlobalConditions` - 全局条件 ([SerializeReference])

- **LootEntry**: 掉落条目
  - `Type` - 条目类型 (Item/Table/Currency/Nothing)
  - `Item` / `NestedTable` / `CurrencyType` - 掉落内容
  - `AmountMin` / `AmountMax` - 数量范围
  - `Weight` - 权重
  - `Conditions` - 条件列表 ([SerializeReference])
  - `Pity` - 保底配置 (PityConfig)

- **LootCondition**: 多态条件基类 ([SerializeReference])
  - `LevelCondition` - 等级条件
  - `QuestCondition` - 任务状态条件
  - `ProbabilityCondition` - 概率条件
  - `HasItemCondition` - 物品检查条件
  - `FirstDropCondition` - 首次掉落条件
  - `TimeCondition` - 时间条件
  - `CompositeCondition` - 组合条件 (AND/OR)
  - `CustomLootCondition` - 自定义条件

- **LootTableManager**: 掉落表管理器 (MonoSingleton + ISaveable)
  - `Roll()` - 执行掉落 (支持批量)
  - `RollAndGrant()` - 执行掉落并发放
  - `GetPityCount()` - 获取保底计数
  - 三种掉落模式: Weight (纯权重) / Pity (保底) / Layered (分层)
  - 事件: `OnLootEvent`
  - ISaveable: 保底计数持久化

- **LootTableEditorWindow**: 可视化编辑器
  - 掉落表列表和搜索
  - 条目详情展示
  - 权重分布可视化
  - 模拟抽取功能

#### Achievement System (v1.11.0)

- **AchievementSO**: 成就数据定义 (ScriptableObject)
  - `AchievementId` / `DisplayName` / `Description` / `Icon`
  - `Category` - 成就分类 (Combat/Collection/Exploration/Social/Story/Crafting/Hidden/Other)
  - `Points` - 成就点数
  - `IsHidden` - 是否隐藏成就
  - `Conditions` - 完成条件 ([SerializeReference])
  - `Rewards` - 奖励列表 ([SerializeReference])
  - `Prerequisites` - 前置成就
  - `Repeatable` / `RepeatCooldown` - 可重复设置

- **AchievementCondition**: 多态条件基类 ([SerializeReference])
  - `CounterCondition` - 计数条件 (累计类)
  - `StateCondition` - 状态条件 (等级/物品/货币/属性)
  - `EventCondition` - 事件条件 (一次性触发)
  - `CompositeAchievementCondition` - 组合条件 (AND/OR)

- **AchievementReward**: 多态奖励基类 ([SerializeReference])
  - `ItemReward` - 物品奖励
  - `CurrencyReward` - 货币奖励
  - `ExpReward` - 经验奖励
  - `UnlockReward` - 解锁奖励
  - `BuffReward` - Buff 奖励
  - `RelationshipReward` - 好感度奖励
  - `AchievementPointReward` - 成就点数奖励
  - `CustomAchievementReward` - 自定义奖励
  - `CompositeReward` - 组合奖励

- **AchievementManager**: 成就管理器 (MonoSingleton + ISaveable)
  - `TriggerEvent()` - 触发事件 (用于条件追踪)
  - `GetState()` / `GetProgress()` - 状态查询
  - `ClaimReward()` / `ClaimAllRewards()` - 领取奖励
  - `IsCompleted()` / `IsClaimed()` - 完成检查
  - `TotalPoints` / `CompletedCount` - 统计
  - 事件: `OnAchievementEvent`
  - IAchievementProvider 支持 (Steam 等)
  - ISaveable: 进度和点数持久化

- **AchievementEditorWindow**: 可视化编辑器
  - 成就列表 (分类筛选)
  - 成就组管理
  - 统计信息展示

#### Crafting System (v1.11.0)

- **CraftingRecipeSO**: 合成配方定义 (ScriptableObject)
  - `RecipeId` / `DisplayName` / `Description` / `Icon`
  - `Category` - 配方分类 (Weapon/Armor/Consumable/Material/Tool/Decoration/Other)
  - `Ingredients` - 材料列表 (RecipeIngredient)
  - `Outputs` - 产出列表 (RecipeOutput)
  - `CraftTime` - 合成时间 (0=即时)
  - `SuccessRate` / `GreatSuccessRate` - 成功率
  - `UnlockType` - 解锁方式 (Default/Level/Quest/Achievement/Item/Relationship/Custom)
  - `SkillId` / `ExpReward` / `RequiredSkillLevel` - 技能经验

- **CraftingManager**: 合成管理器 (MonoSingleton + ISaveable)
  - `IsUnlocked()` / `TryUnlockRecipe()` - 解锁管理
  - `LearnRecipeFromItem()` - 物品学习配方
  - `CanCraft()` / `StartCraft()` - 合成操作
  - `CancelCraft()` - 取消合成
  - `GetSkillLevel()` / `GetSkillData()` - 技能查询
  - 延时合成支持 (CraftingProgress)
  - 事件: `OnCraftingEvent`
  - ISaveable: 解锁/进度/技能持久化

- **RecipeBookSO**: 配方书 (配方集合)

- **CraftingEditorWindow**: 可视化编辑器
  - 配方列表 (分类筛选)
  - 配方书管理
  - 统计信息展示

#### Relationship System (v1.11.0)

- **RelationshipDataSO**: NPC 好感度数据 (ScriptableObject)
  - `NpcId` / `DisplayName` / `Description` / `Portrait`
  - `NpcType` - NPC 类型 (Normal/Romanceable/Merchant/Quest/Companion)
  - `InitialPoints` / `DailyDecay` - 好感度设置
  - `MaxGiftsPerDay` / `MaxTalksPerDay` - 每日限制
  - `Thresholds` - 等级阈值配置
  - `LikedGifts` / `DislikedGifts` - 礼物偏好
  - `Events` - 好感度事件

- **RelationshipLevel**: 好感度等级
  - Stranger / Acquaintance / Friend / CloseFriend / BestFriend / Lover / Partner

- **RelationshipManager**: 好感度管理器 (MonoSingleton + ISaveable)
  - `GetPoints()` / `GetLevel()` - 状态查询
  - `AddPoints()` - 增加好感度
  - `TryGiveGift()` - 送礼
  - `TryTalk()` - 对话 (每日好感度)
  - `ApplyDialogueEffect()` - 对话效果
  - `TriggerEvent()` - 触发好感度事件
  - 每日重置 (送礼次数/对话次数/衰减)
  - 事件: `OnRelationshipEvent`
  - ISaveable: 进度持久化

- **RelationshipGroupSO**: NPC 组 (分类/阵营)

- **RelationshipEditorWindow**: 可视化编辑器
  - NPC 列表 (类型筛选)
  - NPC 组管理
  - 统计信息展示

#### Visual Editors (v1.11.0)

- **ZeroEngineDashboard**: 新增编辑器入口按钮
  - Loot Table Editor 按钮
  - Achievement Editor 按钮
  - Crafting Editor 按钮
  - Relationship Editor 按钮

### New Files

- `Runtime/Loot/LootEnums.cs`
- `Runtime/Loot/LootCondition.cs`
- `Runtime/Loot/LootTableSO.cs`
- `Runtime/Loot/LootTableManager.cs`
- `Editor/Loot/LootTableEditorWindow.cs`
- `Runtime/Achievement/AchievementEnums.cs`
- `Runtime/Achievement/AchievementCondition.cs`
- `Runtime/Achievement/AchievementReward.cs`
- `Runtime/Achievement/AchievementSO.cs`
- `Runtime/Achievement/AchievementManager.cs`
- `Editor/Achievement/AchievementEditorWindow.cs`
- `Runtime/Crafting/CraftingEnums.cs`
- `Runtime/Crafting/CraftingRecipeSO.cs`
- `Runtime/Crafting/CraftingManager.cs`
- `Editor/Crafting/CraftingEditorWindow.cs`
- `Runtime/Relationship/RelationshipEnums.cs`
- `Runtime/Relationship/RelationshipDataSO.cs`
- `Runtime/Relationship/RelationshipManager.cs`
- `Editor/Relationship/RelationshipEditorWindow.cs`

### Architecture

```
Loot Table System (v1.11.0)
├── LootTableSO               # 掉落表定义
│   ├── Entries               # 掉落条目
│   ├── Layers                # 分层配置
│   └── GlobalConditions      # 全局条件
├── LootCondition (abstract)  # 多态条件
│   ├── LevelCondition        # 等级条件
│   ├── ProbabilityCondition  # 概率条件
│   └── CompositeCondition    # 组合条件
└── LootTableManager          # 管理器 (ISaveable)
    ├── Roll()                # 执行掉落
    ├── Pity System           # 保底系统
    └── Layered Mode          # 分层模式

Achievement System (v1.11.0)
├── AchievementSO             # 成就定义
│   ├── Conditions            # 完成条件
│   └── Rewards               # 奖励列表
├── AchievementCondition      # 多态条件
│   ├── CounterCondition      # 计数条件
│   ├── StateCondition        # 状态条件
│   └── EventCondition        # 事件条件
├── AchievementReward         # 多态奖励
│   ├── ItemReward            # 物品奖励
│   ├── CurrencyReward        # 货币奖励
│   └── RelationshipReward    # 好感度奖励
└── AchievementManager        # 管理器 (ISaveable)
    ├── TriggerEvent()        # 事件触发
    ├── ClaimReward()         # 领取奖励
    └── Provider Support      # Steam 等

Crafting System (v1.11.0)
├── CraftingRecipeSO          # 配方定义
│   ├── Ingredients           # 材料
│   ├── Outputs               # 产出
│   └── Unlock Conditions     # 解锁条件
├── RecipeBookSO              # 配方书
└── CraftingManager           # 管理器 (ISaveable)
    ├── StartCraft()          # 开始合成
    ├── Skill System          # 技能经验
    └── Delayed Crafting      # 延时合成

Relationship System (v1.11.0)
├── RelationshipDataSO        # NPC 数据
│   ├── Thresholds            # 等级阈值
│   ├── Gift Preferences      # 礼物偏好
│   └── Events                # 好感度事件
├── RelationshipGroupSO       # NPC 组
└── RelationshipManager       # 管理器 (ISaveable)
    ├── AddPoints()           # 增加好感度
    ├── TryGiveGift()         # 送礼系统
    └── Daily Reset           # 每日重置
```

### Cross-System Integration

```
Achievement ←→ Crafting
- CraftingManager.TriggerEvent("Craft", recipeId)
- 成就条件: CounterCondition 监听合成事件

Achievement ←→ Relationship
- RelationshipManager.TriggerEvent("GiveGift", npcId)
- 成就奖励: RelationshipReward 增加好感度

Crafting ←→ Relationship
- RecipeUnlockType.Relationship 解锁配方
- 好感度达到特定等级解锁配方

Loot ←→ Achievement
- 保底掉落触发成就事件
- 首次掉落条件

All Systems ←→ Inventory
- 物品作为材料/奖励/礼物
- 统一通过 InventoryManager 操作
```

---

## [1.10.0] - 2026-01-03

### Added

#### Equipment System (v1.10.0)

- **EquipmentSlotType**: 可配置装备槽位类型 (ScriptableObject)
  - `SlotId` / `DisplayName` / `Icon` - 槽位标识和显示
  - 通过 SO 配置实现完全自定义槽位 (武器/头盔/护甲/饰品等)

- **EquipmentDataSO**: 装备数据定义 (继承 InventoryItemSO)
  - `SlotType` - 槽位类型引用
  - `BaseStats` - 基础属性列表 (List<StatModifier>)
  - `BelongsToSet` - 套装归属
  - `MaxEnhanceLevel` / `MaxRefineLevel` - 强化上限
  - `GemSlotCount` - 宝石槽数量
  - `EnchantmentPool` - 可附魔词条池

- **EquipmentInstance**: 装备运行时实例
  - `EnhanceLevel` / `RefineLevel` - 强化/精炼等级
  - `Enchantments` - 附魔词条列表
  - `GemSlots` - 宝石槽状态数组
  - `GetAllModifiers()` - 获取所有属性修饰器 (基础+强化+附魔+宝石)
  - `Enhance()` / `Refine()` / `AddEnchantment()` / `SocketGem()` - 强化操作

- **EquipmentSetSO**: 套装定义 (ScriptableObject)
  - `SetId` / `SetName` / `Icon` - 套装标识
  - `Pieces` - 套装包含的装备列表
  - `Effects` - 套装效果列表 (SetEffect)
  - `SetEffect` 结构: `RequiredPieces` + `Modifiers` + `Description`

- **EquipmentManager**: 装备管理器 (MonoSingleton + ISaveable)
  - `ConfiguredSlots` - 配置的槽位类型列表
  - `Equip()` / `Unequip()` / `UnequipAll()` - 装备操作
  - `GetEquipped()` / `GetAllEquipped()` - 查询已装备
  - `IsSlotEmpty()` / `CanEquip()` - 状态检查
  - `RegisterStatProvider()` - 属性提供者注册
  - `GetAllStatModifiers()` - 获取所有属性 (含套装效果)
  - `GetActiveSetEffects()` - 获取激活的套装效果
  - 事件: `OnEquipped`, `OnUnequipped`, `OnEnhanced`, `OnSetEffectActivated`
  - ISaveable: `SaveKey: "Equipment"`, 完整存档支持

- **EquipmentEvents**: 事件参数类
  - `EquipmentEventArgs` - 装备事件 (Slot, Equipment, EventType)
  - `SetEventArgs` - 套装事件 (Set, ActivePieces, ActivatedEffects)
  - `EnhanceEventArgs` - 强化事件 (Equipment, EnhanceType, OldLevel, NewLevel)

#### Talent Tree System (v1.10.0)

- **TalentNodeSO**: 天赋节点定义 (ScriptableObject)
  - `NodeId` / `DisplayName` / `Description` / `Icon` - 节点标识
  - `NodeType` - 类型 (Normal, Keystone, Start, Branch)
  - `MaxLevel` / `PointCostPerLevel` - 等级配置
  - `Prerequisites` - 前置节点列表
  - `PrerequisiteMinLevel` / `RequiredCharacterLevel` - 解锁条件
  - `Effects` - 效果列表 ([SerializeReference] 多态)
  - `EditorPosition` - 编辑器位置

- **TalentTreeSO**: 天赋树定义 (ScriptableObject)
  - `TreeId` / `TreeName` / `Description` - 树标识
  - `Nodes` - 节点列表
  - `Connections` - 连接列表 (TalentConnection)
  - `GetNode()` / `GetStartNodes()` / `GetPrerequisites()` - 查询方法
  - `Validate()` - 图结构验证

- **TalentEffect**: 天赋效果基类 ([SerializeReference])
  - `StatModifierEffect` - 属性修改效果 (StatType, ModType, ValuePerLevel)
  - `MultiStatModifierEffect` - 多属性修改效果
  - `BuffEffect` - Buff 应用效果 (通过 IBuffProvider)
  - `UnlockAbilityEffect` - 解锁技能效果 (通过 IAbilityProvider)
  - `CustomEffect` - 自定义回调效果 (EffectId)

- **TalentTreeController**: 天赋树运行时控制器 (ISaveable)
  - `SetTree()` / `Reset()` - 绑定/重置天赋树
  - `TryAllocatePoint()` / `TryDeallocatePoint()` - 点数分配
  - `CanAllocate()` / `GetNodeLevel()` - 状态查询
  - `AvailablePoints` / `TotalPointsSpent` - 点数统计
  - `AddPoints()` / `SetPoints()` - 点数管理
  - `RegisterStatProvider()` / `RegisterBuffProvider()` / `RegisterAbilityProvider()` - Provider 注册
  - `GetAllStatModifiers()` - 获取所有属性修饰器
  - 事件: `OnPointAllocated`, `OnPointDeallocated`, `OnTreeReset`
  - ISaveable: `SaveKey: "TalentTree"`, 完整存档支持

- **Provider Interfaces**: 解耦集成接口
  - `IStatProvider` - 属性提供者 (EquipmentManager, TalentTreeController 实现)
  - `IBuffProvider` - Buff 提供者 (BuffEffect 使用)
  - `IAbilityProvider` - 技能提供者 (UnlockAbilityEffect 使用)

#### Visual Editors (v1.10.0)

- **TalentTreeEditorWindow**: 天赋树可视化编辑器
  - 菜单入口: ZeroEngine > TalentTree > Talent Tree Editor
  - 双击 TalentTreeSO 资产自动打开
  - GraphView 节点图编辑
  - 右侧属性面板 (TalentNodeInspector)
  - 右键菜单添加节点
  - 节点颜色区分类型 (Normal/Keystone/Start/Branch)
  - 连线自动更新前置依赖

- **EquipmentEditorWindow**: 装备配置编辑器
  - 菜单入口: ZeroEngine > Equipment > Equipment Editor
  - 三标签页: Equipment / Slot Types / Sets
  - 搜索和槽位筛选
  - 装备/槽位/套装的创建和管理
  - 快速定位资产

- **ZeroEngineDashboard**: 新增编辑器入口按钮
  - Talent Tree Editor 按钮
  - Equipment Editor 按钮

### New Files

- `Runtime/Equipment/EquipmentEnums.cs`
- `Runtime/Equipment/EquipmentSlotType.cs`
- `Runtime/Equipment/EquipmentEvents.cs`
- `Runtime/Equipment/EquipmentDataSO.cs`
- `Runtime/Equipment/EquipmentInstance.cs`
- `Runtime/Equipment/EquipmentSetSO.cs`
- `Runtime/Equipment/EquipmentManager.cs`
- `Runtime/TalentTree/TalentEnums.cs`
- `Runtime/TalentTree/TalentEvents.cs`
- `Runtime/TalentTree/TalentNodeSO.cs`
- `Runtime/TalentTree/TalentTreeSO.cs`
- `Runtime/TalentTree/TalentTreeController.cs`
- `Runtime/TalentTree/Effects/TalentEffect.cs`
- `Runtime/TalentTree/Effects/StatModifierEffect.cs`
- `Runtime/TalentTree/Effects/MultiStatModifierEffect.cs`
- `Runtime/TalentTree/Effects/BuffEffect.cs`
- `Runtime/TalentTree/Effects/UnlockAbilityEffect.cs`
- `Runtime/TalentTree/Effects/CustomEffect.cs`
- `Editor/TalentTree/Graph/TalentTreeEditorWindow.cs`
- `Editor/TalentTree/Graph/TalentTreeGraphView.cs`
- `Editor/TalentTree/Graph/TalentGraphNode.cs`
- `Editor/TalentTree/Graph/TalentNodeInspector.cs`
- `Editor/TalentTree/Graph/TalentTreeStyles.uss`
- `Editor/Equipment/EquipmentEditorWindow.cs`

### Architecture

```
Equipment System (v1.10.0)
├── EquipmentSlotType (SO)     # 可配置槽位类型
├── EquipmentDataSO            # 装备数据定义
│   ├── BaseStats              # 基础属性
│   ├── Enhancement Config     # 强化配置
│   └── Set Reference          # 套装引用
├── EquipmentInstance          # 运行时实例
│   ├── EnhanceLevel           # 强化等级
│   ├── RefineLevel            # 精炼等级
│   ├── Enchantments           # 附魔词条
│   └── GemSlots               # 宝石槽
├── EquipmentSetSO             # 套装定义
│   └── SetEffect[]            # 阈值效果
└── EquipmentManager           # 管理器 (ISaveable)
    ├── Equip/Unequip          # 装备操作
    ├── GetAllStatModifiers    # 属性计算
    └── SetEffect Activation   # 套装效果

Talent Tree System (v1.10.0)
├── TalentNodeSO               # 节点定义
│   ├── Prerequisites          # 前置依赖
│   └── Effects[]              # 多态效果
├── TalentTreeSO               # 树定义
│   ├── Nodes                  # 节点列表
│   └── Connections            # 连接关系
├── TalentEffect (abstract)    # 效果基类
│   ├── StatModifierEffect     # 属性修改
│   ├── BuffEffect             # Buff 应用
│   ├── UnlockAbilityEffect    # 技能解锁
│   └── CustomEffect           # 自定义回调
└── TalentTreeController       # 控制器 (ISaveable)
    ├── AllocatePoint          # 点数分配
    ├── Provider Integration   # 系统集成
    └── GetAllStatModifiers    # 属性计算
```

### Usage Example

```csharp
// === Equipment System ===

// 1. 装备物品
var weapon = InventoryManager.Instance.GetItem(0) as EquipmentDataSO;
if (weapon != null && EquipmentManager.Instance.CanEquip(weapon))
{
    EquipmentManager.Instance.Equip(weapon);
}

// 2. 获取装备属性
var modifiers = EquipmentManager.Instance.GetAllStatModifiers();
foreach (var mod in modifiers)
{
    statController.AddModifier(mod.statType, mod);
}

// 3. 强化装备
var equipped = EquipmentManager.Instance.GetEquipped(weaponSlot);
if (equipped != null)
{
    var result = equipped.Enhance(1);
    if (result == EnhanceResult.Success)
    {
        Debug.Log($"Enhance success! Level: {equipped.EnhanceLevel}");
    }
}

// 4. 套装效果
var setEffects = EquipmentManager.Instance.GetActiveSetEffects();
foreach (var (set, modifiers) in setEffects)
{
    Debug.Log($"Set {set.SetName} active with {modifiers.Count} effects");
}

// === Talent Tree System ===

// 1. 绑定天赋树
talentController.SetTree(myTalentTree);
talentController.AddPoints(10);

// 2. 分配点数
var node = myTalentTree.GetNode("fire_mastery");
if (talentController.CanAllocate(node))
{
    talentController.TryAllocatePoint(node);
}

// 3. 注册 Provider
talentController.RegisterStatProvider(statController);
talentController.RegisterBuffProvider(buffReceiver);
talentController.RegisterAbilityProvider(abilityHandler);

// 4. 获取天赋属性
var talentMods = talentController.GetAllStatModifiers();
foreach (var mod in talentMods)
{
    statController.AddModifier(mod.statType, mod);
}
```

---

## [1.9.0] - 2026-01-03

### Added

#### Dialog System 增强 (v1.9.0)

- **DialogIntegrationHandler**: 对话系统集成处理器
  - 自动处理 `DialogCallbackNode` 回调事件
  - Quest 集成: `Quest.Accept`, `Quest.Complete`, `Quest.Submit`, `Quest.Abandon`
  - Inventory 集成: `Inventory.Give`, `Inventory.Take`, `Inventory.Check`
  - Currency 集成: `Currency.Give`, `Currency.Take`
  - Event 集成: `Event.Trigger`, `Audio.Play`
  - 参数格式: `itemId:amount` (物品), `questId` (任务), `eventName:param` (事件)
  - `OnCustomCallback` 事件 - 自定义回调扩展点
  - `DialogCallbackIds` 常量类 - 预定义回调 ID

- **DialogIntegrationUtils**: 对话系统集成工具类
  - `HasQuestInState()` - 检查任务状态
  - `HasActiveQuest()` - 检查活跃任务
  - `HasItem()` / `GetItemCount()` - 检查物品
  - `SyncVariablesToDialog()` - 同步任务/物品状态到对话变量

- **DialogBoxUI**: 增强版对话框 UI 组件
  - 立绘/Portrait 支持: 单图模式或左右对话模式
  - `PortraitDisplayMode` 枚举 (Single, LeftRight)
  - 立绘缓存系统 (Resources 加载 + 缓存)
  - 旁白模式 (无说话人时隐藏名称面板)
  - 继续指示器控制
  - 键盘/手柄输入 (Space/Enter 继续)

- **DialogUIConnector**: DialogRunner 与 DialogBoxUI 连接器
  - 自动连接 DialogRunner 事件到 UI
  - 对话开始/结束自动显示/隐藏
  - 打字机进度更新
  - 选项显示和选择

### Changed

- **GameEvents**: 新增 `CurrencySpent` 事件常量 (v1.9.0+)

### Architecture

```
Dialog System Integration (v1.9.0)
├── DialogIntegrationHandler    # 回调处理器
│   ├── Quest: Accept/Complete/Submit/Abandon
│   ├── Inventory: Give/Take/Check
│   ├── Currency: Give/Take
│   └── Event: Trigger/PlaySound
├── DialogBoxUI                 # 增强版 UI
│   ├── Portrait 立绘系统
│   ├── Narration 旁白模式
│   └── Input 键盘/手柄支持
└── DialogUIConnector           # UI 连接器
```

### Usage Example

```csharp
// 1. 在 DialogCallbackNode 中使用预定义回调
// CallbackId: "Quest.Accept", Parameter: "main_quest_001"
// CallbackId: "Inventory.Give", Parameter: "health_potion:3"
// CallbackId: "Currency.Take", Parameter: "100"

// 2. 在条件中使用集成工具
if (DialogIntegrationUtils.HasActiveQuest("main_quest_001"))
{
    // 玩家已接受任务
}

if (DialogIntegrationUtils.HasItem("key_item", 1))
{
    // 玩家有钥匙
}

// 3. 同步状态到对话变量
DialogIntegrationUtils.SyncVariablesToDialog(context.Variables);
// 现在可以在条件中使用: quest_main_quest_001 == true
```

---

## [1.8.0] - 2026-01-03

### Added

#### Save System 完全重构

- **SaveSlotManager**: 多槽位存档管理器 (v1.8.0+)
  - 支持 8+ 存档槽位 + 1 自动存档槽位
  - `ISaveable` 接口：模块化系统注册，自动收集/分发存档数据
  - `Register()` / `Unregister()` - 系统注册管理
  - `Save(slotIndex)` / `Load(slotIndex)` - 槽位存取
  - `SaveCurrent()` / `QuickSave()` / `QuickLoad()` - 便捷 API
  - `AutoSave()` - 自动存档到 slot -1
  - `NewGame(targetSlot)` - 开始新游戏，重置所有系统
  - `Delete(slotIndex)` / `DeleteAll()` - 删除存档
  - `GetAllSlotMetas()` / `GetSlotMeta()` / `HasSave()` - 槽位查询
  - `GetSlotScreenshot()` - 获取存档截图
  - `SetPlayerName()` / `SetPlayerLevel()` / `SetCustomMeta()` - 元信息设置
  - 事件: `OnSaveCompleted`, `OnLoadCompleted`, `OnSlotDeleted`
  - 会话游戏时长跟踪 (`CurrentPlayTime`)

- **SaveSlotMeta**: 存档元信息类 (v1.8.0+)
  - `SlotIndex` / `Timestamp` / `PlayTimeSeconds` / `SceneName`
  - `PlayerName` / `PlayerLevel` / `SaveVersion` / `IsValid`
  - `CustomMeta` - 自定义键值对
  - `FormattedPlayTime` / `FormattedTimestamp` - 格式化显示

- **AutoSaveController**: 自动存档控制器 (v1.8.0+)
  - 定时自动存档 (`AutoSaveTrigger.Interval`)
  - 场景切换存档 (`AutoSaveTrigger.SceneChange`)
  - 任务完成存档 (`AutoSaveTrigger.QuestComplete`)
  - 重要事件存档 (`AutoSaveTrigger.ImportantEvent`)
  - 游戏暂停/退出存档 (`AutoSaveTrigger.OnPause`)
  - `Enable()` / `Disable()` / `Pause()` / `Resume()` - 控制开关
  - `OnQuestCompleted()` / `OnImportantEvent()` - 外部触发

- **ScreenshotCapture**: 截图捕获组件 (v1.8.0+)
  - 异步截图 `CaptureAsync(callback)`
  - 同步截图 `CaptureSync()` (阻塞一帧)
  - 可配置尺寸 (默认 320x180) 和质量 (默认 75%)
  - 智能裁剪保持宽高比
  - JPG 编码减小存档体积

- **SaveSystemConfig**: 存档系统配置 (v1.8.0+)
  - `MaxSlots` - 最大槽位数 (默认 8)
  - `EnableAutoSave` / `AutoSaveInterval` / `AutoSaveTriggers`
  - `ScreenshotWidth` / `ScreenshotHeight` / `ScreenshotQuality`

- **DialogSaveAdapter**: Dialog 系统存档适配器 (v1.8.0+)
  - 将 `DialogVariables` 全局变量接入 `SaveSlotManager`

### Changed

#### 系统 ISaveable 集成

- **InventoryManager**: 实现 `ISaveable` 接口 (v1.8.0+)
  - `SaveKey: "Inventory"`
  - `ExportSaveData()` / `ImportSaveData()` / `ResetToDefault()`
  - 自动注册到 `SaveSlotManager`
  - 与槽位系统协同时跳过直接存储

- **QuestManager**: 实现 `ISaveable` 接口 (v1.8.0+)
  - `SaveKey: "Quest"`
  - `ExportSaveData()` / `ImportSaveData()` / `ResetToDefault()`
  - 自动注册到 `SaveSlotManager`

- **StatController**: 添加存档支持 (v1.8.0+)
  - `ExportSaveData()` / `ImportSaveData()` / `ResetStats()`
  - `StatControllerSaveData` / `StatSaveData` 数据类

### Architecture

```
SaveSlotManager (高层封装)
├── SlotManager         # 8 槽位 + 自动存档槽
├── AutoSaveController  # 自动存档调度
├── ScreenshotCapture   # 截图捕获
└── ES3Provider         # ES3 底层 (已有)

ISaveable 接口
├── InventoryManager.Export/Import()
├── QuestManager.Export/Import()
├── DialogSaveAdapter.Export/Import()
└── StatController.Export/Import()
```

### Usage Example

```csharp
// 1. 系统自动注册 (Start 时)
// InventoryManager, QuestManager 等自动注册到 SaveSlotManager

// 2. 保存到槽位
SaveSlotManager.Instance.Save(0, success => Debug.Log($"Saved: {success}"));

// 3. 加载槽位
SaveSlotManager.Instance.Load(0, success => Debug.Log($"Loaded: {success}"));

// 4. 获取槽位信息
var metas = SaveSlotManager.Instance.GetAllSlotMetas();
foreach (var meta in metas)
{
    if (meta.IsValid)
    {
        Debug.Log($"Slot {meta.SlotIndex}: {meta.PlayerName} Lv.{meta.PlayerLevel}");
        Debug.Log($"  Time: {meta.FormattedPlayTime} | {meta.FormattedTimestamp}");
    }
}

// 5. 获取截图
var screenshot = SaveSlotManager.Instance.GetSlotScreenshot(0);

// 6. 新游戏
SaveSlotManager.Instance.NewGame(0);
```

---

## [1.7.0] - 2026-01-03

### Added

#### Localization 增强

- **LocalizedStringExtensions**: LocalizedString 扩展方法 (v1.7.0+)
  - `GetSafe()` - 安全获取本地化文本，空值返回 `[key]` 占位符
  - `GetSafe(arg0)` / `GetSafe(arg0, arg1)` / `GetSafe(arg0, arg1, arg2)` - 参数化重载，避免 params 数组分配
  - `GetSafeParams(params object[] args)` - 4+ 参数时使用
  - `IsValid()` - 检查 LocalizedString 是否有效
  - `GetKey()` / `GetTableName()` - 获取调试信息
  - `DebugMode` - 全局开关，启用时显示 `[key]` 便于开发定位
  - `ClearCache()` - 清除格式化缓存（语言切换时调用）

- **TranslationCheckerWindow**: 翻译完整性检查工具 (v1.7.0+)
  - 菜单: `ZeroEngine > Localization > Translation Checker`
  - 扫描所有 String Table，显示各语言覆盖率
  - 进度条可视化翻译完成度
  - 缺失 key 列表展示
  - 导出 Markdown 报告功能

### Changed

#### UI 动画系统优化

- **UIViewBase**: DOTween 条件编译动画 (v1.7.0+)
  - `#if DOTWEEN` 条件编译：有 DOTween 时使用原生 API，否则回退 async/await
  - DOTween 实现：使用 `DOFade()` / `DOScale()` / `DOAnchorPos()` 消除每帧 GC 分配
  - `AsyncWaitForCompletion()` 返回 Task，保持 API 兼容
  - `SetUpdate(true)` 使用 unscaled time，暂停时动画正常播放
  - `KillActiveAnimations()` 方法：防止动画冲突
  - 自动在 Open/Close/Destroy 时清理活动动画

### Performance Notes

- **LocalizedStringExtensions**: 格式化字符串缓存，GC 分配 ~100 bytes/次 -> 0
- **UIViewBase 动画**: Task.Yield() 每帧 ~32 bytes -> DOTween 零分配
  - 0.25s 动画 @ 60fps: ~480 bytes -> 0 bytes

### Philosophy

- **配置优先**: 推荐在 Inspector 配置 LocalizedString，而非代码硬编码 key
- **轻量封装**: 不重复 Unity Localization 已有功能，仅提供便捷扩展
- **条件编译**: 可选依赖通过 `#if` 实现，无依赖时优雅降级

---

## [1.6.1] - 2026-01-02

### Changed

#### UI 框架性能优化

- **UIManager**: 消除热路径 GC 分配
  - `AllLayers` / `LayersSortedDesc` 静态数组缓存，消除 `Enum.GetValues()` 每帧分配
  - `UpdateTopView()` 使用缓存数组，GC: ~120 bytes/次 -> 0
  - `RemoveFromStack()` 使用静态 `TempViewList` 替代临时 Stack 创建，GC: ~64+ bytes/次 -> 0
  - `ViewNameCache<T>` 泛型静态类缓存 `typeof(T).Name`，消除类型名称分配
  - `LogDebug()` / `LogWarning()` 使用 `[Conditional("ZEROENGINE_DEBUG")]` 属性，Release 构建完全移除

- **UIUtility**: 消除 Raycast 分配
  - `SharedRaycastResults` 静态列表缓存，`IsPointerOverUI()` GC: ~80+ bytes/次 -> 0
  - `_cachedPointerEventData` 复用，消除每次调用的 PointerEventData 分配 (~72 bytes)

- **UIViewBase**: 缓存视图名称
  - `_cachedViewName` 惰性缓存，避免重复获取类型名称

### Performance Notes

- UI 框架性能评分: 6/10 -> 8+/10
- 所有热路径方法现在零 GC 分配
- Release 构建调试日志完全编译移除

---

## [1.6.0] - 2026-01-01

### Added

#### UI 框架 (工业级)

- **UI**: 完整 UI 框架，从 Project5 整合并适配 (v1.6.0+)
  - **UIManager** - 核心 UI 管理器 (MonoSingleton)
    - 7 层级系统: Background/Main/Screen/Popup/Overlay/Top/System
    - 面板栈管理 (入栈/出栈/暂停/恢复)
    - 4 种显示模式: Normal/HideOthers/Stack/Singleton
    - 3 种关闭模式: Hide/Destroy/Pool
    - 异步加载支持 (Addressables 可选)
    - 遮罩管理 (可配置颜色/点击关闭)
    - ESC 键关闭 (可禁用)
    - 预加载 API
    - 事件: `OnViewOpened`, `OnViewClosed`, `OnCancelInputRequested`, `OnPauseRequested`
  - **UIViewBase** - 视图基类
    - 完整生命周期: OnCreate → OnOpen → OnResume → OnPause → OnClose → OnDestroy
    - 内置动画系统: Fade/Scale/SlideLeft/SlideRight/SlideTop/SlideBottom/Custom
    - 焦点管理 (手柄/键盘导航支持)
    - 缓动函数: EaseOutBack, EaseOutCubic
    - 公共 API: `Close()`, `CloseWithResult()`, `GetData<T>()`, `Refresh()`
  - **UIViewConfig** - 视图配置类
    - `viewName` - 视图唯一标识
    - `prefab` / `prefabReference` / `assetPath` - 多种资源引用方式
    - `layer` / `showMode` / `closeMode` - 显示控制
    - `showMask` / `maskColor` / `maskClickClose` - 遮罩设置
    - `blockInput` / `allowESCClose` / `pauseGame` - 行为设置
    - `openAnimation` / `closeAnimation` / `animationDuration` - 动画设置
    - `cache` - 实例缓存
  - **UIOpenArgs** / **UICloseArgs** - 打开/关闭参数
    - `Data` - 传递数据
    - `Immediate` - 跳过动画
    - `Force` - 强制关闭
    - `OnOpened` / `OnClosed` - 回调
  - **UIViewDatabase** - ScriptableObject 配置数据库
    - 批量配置 UI 视图
    - 编辑器工具: 验证/排序/自动查找 Prefab
    - `RegisterToManager()` - 一键注册到 UIManager
    - Odin Inspector 增强 (可选)
  - **UIUtility** - 静态工具类
    - CanvasGroup: `ShowCanvasGroup()`, `HideCanvasGroup()`, `SetCanvasGroupVisible()`
    - RectTransform: `SetStretch()`, `SetCenter()`, `WorldToCanvasPosition()`, `ScreenToCanvasPosition()`
    - Layout: `ForceRebuildLayout()`, `MarkLayoutForRebuild()`
    - ScrollRect: `ScrollToTop()`, `ScrollToBottom()`, `ScrollToChild()`
    - Color: `SetAlpha()`, `ParseHexColor()`, `ColorToHex()`
    - SafeArea: `ApplySafeArea()`, `GetSafeAreaAnchors()`
    - Raycast: `IsPointerOverUI()`
  - **UIExtensions** - 扩展方法
    - `GetOrAddComponent<T>()` - 获取或添加组件
    - `SetActiveSafe()` - 安全设置活动状态
    - `DestroyAllChildren()` / `DestroyAllChildrenImmediate()` - 销毁子对象
    - `SetStretch()` / `SetCenter()` / `ForceRebuildLayout()` - RectTransform 扩展
  - **枚举**
    - `UILayer` - 7 个层级
    - `UIShowMode` - 4 种显示模式
    - `UICloseMode` - 3 种关闭模式
    - `UIAnimationType` - 7 种动画类型
    - `UIViewState` - 7 种视图状态

### New Files

- `Runtime/UI/Core/UIManager.cs`
- `Runtime/UI/Core/UIViewBase.cs`
- `Runtime/UI/Core/UIDefines.cs`
- `Runtime/UI/Core/UIUtility.cs`
- `Runtime/UI/Core/UIViewDatabase.cs`

### Notes

- 条件编译: `ZEROENGINE_ADDRESSABLES` - 启用 Addressables 支持
- 条件编译: `ODIN_INSPECTOR` - 启用 Odin Inspector 增强
- UIManager 继承自 `MonoSingleton<T>` (ZeroEngine.Core)
- 解耦设计: 通过事件 (`OnPauseRequested`) 与游戏暂停系统集成

---

## [1.5.0] - 2026-01-01

### Added

#### 可视化编辑器 (GraphView)

- **Dialog Graph Editor**: 节点式对话图编辑器 (v1.5.0+)
  - `DialogGraphEditorWindow` - 对话图主编辑器窗口
    - 菜单入口: ZeroEngine > Dialog > Dialog Graph Editor
    - 双击 DialogGraphSO 资产自动打开编辑器
    - 右键菜单添加节点
    - Ctrl+D 复制节点
    - Delete 删除节点
    - 图结构验证
  - `DialogGraphView` - GraphView 组件
    - 节点拖拽定位
    - 端口连线
    - 缩放和平移
  - `DialogGraphNode` - 可视化节点类 (8 种类型)
    - `StartNode` - 入口节点 (绿色)
    - `EndNode` - 结束节点 (红色)
    - `TextNode` - 文本节点 (蓝色)
    - `ChoiceNode` - 选项节点 (橙色)
    - `ConditionNode` - 条件分支节点 (紫色)
    - `SetVariableNode` - 变量设置节点 (黄色)
    - `RandomNode` - 随机分支节点 (青色)
    - `CallbackNode` - 回调节点 (粉色)
  - `DialogNodeInspector` - 节点属性编辑面板
    - 右侧面板编辑节点详细属性
    - 支持所有节点类型
  - `DialogGraphStyles.uss` - 编辑器样式表

- **BehaviorTree Graph Editor**: 可视化行为树编辑器 (v1.5.0+)
  - `BTTreeAsset` - 可序列化行为树资产 (ScriptableObject)
    - `CreateRuntime()` - 从资产构建运行时 BehaviorTree
    - 支持 Composite/Decorator/Leaf 节点存储
    - 位置和连接信息序列化
  - `BTGraphEditorWindow` - BT 图主编辑器窗口
    - 菜单入口: ZeroEngine > BehaviorTree > BT Graph Editor
    - 双击 BTTreeAsset 资产自动打开编辑器
    - 右键菜单添加节点
    - 设置 Root 节点
  - `BTGraphView` - GraphView 组件
    - 层级可视化布局
    - 节点父子关系连线
    - 自动布局算法
  - `BTGraphNode` - 可视化节点类 (3 类)
    - `CompositeNode` - 组合节点 (Sequence, Selector, Parallel)
    - `DecoratorNode` - 装饰节点 (Repeater, Inverter, Conditional 等)
    - `LeafNode` - 叶节点 (ActionNode, WaitNode, LogNode)
  - `BTGraphStyles.uss` - 编辑器样式表

- **ZeroEngineDashboard**: 新增编辑器入口按钮
  - Dialog Graph Editor 按钮 - 打开对话图编辑器
  - BT Graph Editor 按钮 - 打开行为树图编辑器

### New Files

- `Editor/Dialog/Graph/DialogGraphEditorWindow.cs`
- `Editor/Dialog/Graph/DialogGraphView.cs`
- `Editor/Dialog/Graph/DialogGraphNode.cs`
- `Editor/Dialog/Graph/DialogNodeInspector.cs`
- `Editor/Dialog/Graph/DialogGraphStyles.uss`
- `Runtime/BehaviorTree/Core/BTTreeAsset.cs`
- `Editor/BehaviorTree/Graph/BTGraphEditorWindow.cs`
- `Editor/BehaviorTree/Graph/BTGraphView.cs`
- `Editor/BehaviorTree/Graph/BTGraphNode.cs`
- `Editor/BehaviorTree/Graph/BTGraphStyles.uss`

### Fixed

- **BTTreeAsset.BuildParallel()**: 修复 `ParallelSuccessPolicy`/`ParallelFailurePolicy` 到 `ParallelPolicy` 枚举的转换逻辑，确保 Parallel 节点运行时策略正确应用
- **BuffMonitor.CreateDebugData()**: 修复 `BuffData` 属性访问，使用 `name` 替代不存在的 `BuffName` 属性
- **ZeroEngine.Tests.Editor.asmdef**: 修复程序集引用，从 `ZeroEngine.Runtime` 改为 `ZeroEngine`，解决编辑器测试编译错误

---

## [1.4.0] - 2026-01-01

### Added

#### Dialog 对话系统升级

- **Dialog**: 节点图对话系统 (v1.4.0+)
  - **变量系统** (`DialogVariables`):
    - `DialogVariableType` 枚举 (Bool, Int, Float, String)
    - `DialogVariable` 结构体 - 类型化存储，避免 boxing
      - `GetBool()` / `GetInt()` / `GetFloat()` / `GetString()` - 类型特化获取方法
      - `GetAsDouble()` - 数值比较专用
      - `IsTruthy()` - 零装箱真值检查
      - `Create(name, value)` - 静态工厂方法 (bool/int/float/string 重载)
    - `DialogVariables` 类 - 变量容器
      - 作用域: Local (会话级) / Global (持久化，`$` 前缀)
      - `SetLocal()` / `GetLocal()` / `HasLocal()` / `ClearLocal()` - 本地变量操作
      - `SetGlobal()` / `GetGlobal()` / `HasGlobal()` / `ClearGlobal()` - 全局变量操作
      - `Set()` / `Get()` / `Has()` - 统一访问 (自动识别 `$` 前缀)
      - `IsTruthy()` - 变量真值检查
      - `ExportLocal()` / `ImportLocal()` - 存档支持
      - `ExportGlobal()` / `ImportGlobal()` - 全局变量存档
      - `OnVariableChanged` 事件
  - **条件表达式** (`DialogCondition`):
    - `ComparisonOperator` 枚举 (==, !=, <, <=, >, >=)
    - `LogicalOperator` 枚举 (&&, ||)
    - `DialogConditionExpression` - 单条件表达式
    - `DialogConditionGroup` - 条件组 (AND/OR 组合)
    - `DialogConditionParser` 静态解析器:
      - `Evaluate(expression, variables)` - 直接求值，避免临时对象分配
      - 支持语法: `hasKey`, `!hasKey`, `gold >= 100`, `name == "Hero"`, `gold >= 100 && hasKey`
  - **节点类型** (`DialogNode`):
    - `DialogNodeType` 枚举 (Start, Text, Choice, Condition, SetVariable, Random, Callback, End)
    - `DialogNodeResult` 结构体 - 节点执行结果
    - `DialogNode` 抽象基类
    - `DialogStartNode` - 入口节点
    - `DialogTextNode` - 文本节点 (Speaker, Text, Portrait, Voice, Metadata)
      - `SubstituteVariables()` - 变量替换 (ThreadStatic StringBuilder 优化)
    - `DialogChoiceNode` - 选项节点 (条件过滤, 禁用显示)
    - `DialogConditionNode` - 条件分支节点
    - `DialogSetVariableNode` - 变量设置节点 (Set/Add/Subtract/Multiply/Divide/Toggle)
    - `DialogRandomNode` - 随机分支节点 (权重支持)
    - `DialogCallbackNode` - 外部回调节点 (可等待完成)
    - `DialogEndNode` - 结束节点 (EndTag 支持)
  - **执行上下文** (`DialogGraphContext`):
    - `Variables` - 变量存储
    - `CurrentNode` / `LastLine` / `EndTag` - 状态追踪
    - `HasVisited()` / `GetVisitCount()` - O(1) 访问记录查询 (HashSet + Dictionary)
    - `RecordVisit()` - 记录节点访问
    - `TriggerCallback()` / `CompleteCallback()` - 回调管理
    - `OnCallback` 事件
  - **对话图** (`DialogGraphSO`):
    - ScriptableObject 容器，支持 `[SerializeReference]` 多态节点
    - `GetStartNode()` / `GetNode(id)` / `GetNodes<T>()` - 节点查询
    - `AddNode()` / `RemoveNode()` - 编辑器 API
    - `Validate()` - 图结构验证
    - `CreateDefaultGraph()` - 创建默认图
    - `CreateNode(type)` - 节点工厂
  - **运行器** (`DialogRunner`):
    - MonoBehaviour 高级运行器
    - 事件: `OnDialogStart`, `OnDialogEnd`, `OnLineDisplay`, `OnTypewriterUpdate`, `OnTypewriterComplete`, `OnChoicesAvailable`, `OnChoiceSelected`, `OnCallback`, `OnVariableChanged`
    - API: `StartDialog()`, `Advance()`, `SelectChoice()`, `StopDialog()`, `SkipTypewriter()`, `CompleteCallback()`, `SetVariable()`, `GetVariable<T>()`
    - 内置打字机效果 (可配置字符速度)
  - **Provider** (`DialogGraphProvider`):
    - `IDialogProvider` 实现
    - `Begin()` / `End()` / `CanContinue` / `HasChoices` - 生命周期
    - `Continue()` / `GetChoices()` / `SelectChoice()` - 对话控制
    - `SetVariable()` / `GetVariable()` - 变量访问
    - `AdvanceFromCurrentNode()` / `CompleteCallback()` - 高级控制

- **Performance Notes**: Dialog 系统性能优化
  - `DialogVariable` 类型特化 Get 方法避免 boxing
  - `DialogTextNode.SubstituteVariables()` 使用 ThreadStatic StringBuilder
  - `DialogConditionParser` 直接求值避免临时对象分配
  - `DialogGraphContext` 使用 HashSet/Dictionary 实现 O(1) 访问记录查询
  - 所有字符串比较使用 `StringComparison.OrdinalIgnoreCase` 替代 `ToLower()`

#### Editor 编辑器工具

- **Editor**: 编辑器工具集增强 (v1.4.0+)
  - `BehaviorTreeViewerWindow` - BT 运行时可视化查看器:
    - `RegisterTree(BehaviorTree)` - 注册行为树以供查看
    - `UnregisterTree(BehaviorTree)` - 取消注册行为树
    - 树形结构展示 - 可折叠的节点层级视图
    - 状态颜色指示 - Running(蓝色)/Success(绿色)/Failure(红色)/Idle(灰色)
    - 实时刷新 - Play Mode 下自动更新节点状态
    - 多树支持 - 可追踪多个行为树实例
    - 菜单入口: ZeroEngine > BehaviorTree Viewer
  - `ZeroEngineDashboard` 增强:
    - `DrawEditorTools()` 方法 - 统一编辑器工具入口区域
    - 快捷按钮: BehaviorTree Viewer, Buff Editor, Ability Editor, Inventory Editor, Quest Editor, Global Search
  - `Editor/README.md` - 编辑器工具文档
    - 所有工具菜单路径和说明
    - 使用代码示例
    - 依赖项说明表
    - 文件结构说明

#### Debugging 运行时调试模块

- **Debugging**: 运行时调试和监控系统
  - `IDebugModule` 接口:
    - `ModuleName` / `IsEnabled` - 模块标识和开关
    - `Update()` - 更新数据
    - `GetSummary()` / `GetEntries()` - 获取调试信息
    - `Clear()` - 清空数据
  - `DebugEntry` 结构体 (Label, Value, Type)
  - `DebugEntryType` 枚举 (Info, Warning, Error, Success)
  - `DebugManager` 静态管理器:
    - `IsEnabled` / `UpdateInterval` - 全局开关和更新间隔
    - `RegisterModule()` / `UnregisterModule()` - 模块注册/注销
    - `GetModule<T>()` / `GetAllModules()` - 模块查询
    - `Update()` - 更新所有模块
    - `GetAllSummaries()` - 获取所有模块摘要
    - `ClearAll()` / `Reset()` - 清空/重置
  - `BTNodeDebugData` 结构体:
    - `NodeName` / `NodeType` / `State` / `Depth` / `IsActive`
  - `FSMTransitionRecord` 结构体:
    - `FromState` / `ToState` / `Timestamp` / `Trigger`
  - `BuffDebugData` 结构体:
    - `BuffId` / `BuffName` / `CurrentStacks` / `MaxStacks` / `RemainingTime` / `Duration`
  - `StatDebugData` 结构体:
    - `StatName` / `BaseValue` / `CurrentValue` / `ModifierCount` / `Modifiers`
  - `StatModifierDebugData` 结构体:
    - `Source` / `Type` / `Value`
  - `PoolDebugData` 结构体:
    - `PoolName` / `PooledCount` / `ActiveCount` / `TotalCreated` / `HitRate`
  - `BTDebugger` BehaviorTree 调试器:
    - `TrackTree()` / `UntrackTree()` - 追踪/取消追踪行为树
    - `GetNodeData()` - 获取节点数据列表
    - `OnNodeExecuted` 事件 - 节点执行回调
  - `FSMDebugger` FSM 状态机调试器:
    - `TrackFSM()` / `UntrackFSM()` - 追踪/取消追踪状态机
    - `GetTransitions()` - 获取转换记录列表
    - `OnStateTransition` 事件 - 状态转换回调
  - `BuffMonitor` Buff 系统监控:
    - `TrackReceiver()` / `UntrackReceiver()` - 追踪/取消追踪 BuffReceiver
    - `GetBuffData()` - 获取 Buff 数据列表
    - `OnBuffEvent` 事件 - Buff 事件回调
  - `StatMonitor` Stat 系统监控:
    - `TrackController()` / `UntrackController()` - 追踪/取消追踪 StatController
    - `GetStatData()` - 获取属性数据列表
    - `OnStatChanged` 事件 - 属性变化回调
  - `PoolProfiler` 对象池性能分析:
    - `LastStats` - 最新性能统计
    - `CacheHitRate` - 缓存命中率
    - 自动采集 ZeroGC 集合池统计

- **StatSystem**: 调试 API 增强
  - `Stat.ModifierCount` 属性 - 获取修饰器数量
  - `Stat.GetModifiers()` 方法 - 获取修饰器列表（只读）

#### Performance 性能优化模块

- **Performance**: 零 GC 集合池和缓存系统
  - `ZeroGC` 统一入口类:
    - `GetList<T>()` / `GetListRaw<T>()` - 获取池化 List (自动/手动归还)
    - `GetDictionary<K,V>()` / `GetDictionaryRaw<K,V>()` - 获取池化 Dictionary
    - `GetStringBuilder()` / `BuildString(Action)` - StringBuilder 池
    - `Return<T>(List)` / `Return<K,V>(Dict)` / `Return(StringBuilder)` - 归还对象
    - `CacheFormattedValue<T>()` - 获取或缓存格式化值
    - `InvalidateCache(keyPrefix)` - 使指定前缀的缓存失效
    - `WarmUpLists<T>()` / `WarmUpDictionaries<K,V>()` / `WarmUpStringBuilders()` - 类型预热
    - `WarmUpCommon()` - 预热常用类型池
    - `GetStats()` - 获取全局性能统计
    - `ResetStats()` / `ClearAll()` - 重置统计/清理所有池
  - `ListPool<T>` - 线程安全泛型 List 对象池
    - `Get(capacity)` / `Return(list)` - 获取/归还
    - `WarmUp(count, capacity)` - 预热
    - `GetStats()` - 获取池统计
    - 池最大容量 64，对象最大容量 1024
  - `DictionaryPool<TKey, TValue>` - 线程安全泛型 Dictionary 对象池
    - `Get(capacity)` / `Return(dict)` - 获取/归还
    - `WarmUp(count, capacity)` - 预热
    - `GetStats()` - 获取池统计
    - 池最大容量 32，对象最大容量 512
  - `StringBuilderPool` - StringBuilder 对象池
    - `Get(capacity)` / `Return(sb)` - 获取/归还
    - `Build(Action<StringBuilder>)` - 自动获取、使用、归还并返回字符串
    - `WarmUp(count, capacity)` - 预热
    - 池最大容量 32，对象最大容量 8192
  - `PooledList<T>` - IDisposable List 包装器
    - 实现 `IList<T>`, `IReadOnlyList<T>`
    - `using var list = ZeroGC.GetList<T>()` - 自动归还模式
    - `Inner` 属性 - 访问内部 List
    - 扩展方法: `Sort()`, `Find()`, `FindAll()`, `ToArray()` 等
  - `PooledDictionary<TKey, TValue>` - IDisposable Dictionary 包装器
    - 实现 `IDictionary<TKey, TValue>`
    - `using var dict = ZeroGC.GetDictionary<K,V>()` - 自动归还模式
    - `Inner` 属性 - 访问内部 Dictionary
  - `FormattedValueCache` - 格式化值缓存
    - `GetOrCreate<T>(key, value, formatter)` - 获取或创建缓存
    - `GetOrCreate<T>(key, value, format)` - 使用格式字符串
    - `Invalidate(keyPrefix)` - 使指定前缀缓存失效
    - `Cleanup()` - 清理过期条目
    - `HitRate` / `Count` - 命中率/条目数统计
    - 默认最大 1000 条目，60 秒过期
  - `PerformanceStats` 结构 - 全局性能统计
    - `ListPoolStats` / `DictionaryPoolStats` / `StringBuilderPoolStats`
    - `CacheHitRate` / `CacheEntryCount`
  - `PoolStats` 结构 - 对象池统计
    - `PooledCount` / `ActiveCount` / `TotalCreated`
    - `GetCount` / `ReturnCount` / `HitRate`

## [1.3.0] - 2026-01-01

### Added

#### v1.2.0 遗留工作完成

- **UI.MVVM**: 格式化 + 验证 + 双向绑定
  - `ValidationResult` struct - 验证结果封装 (IsValid, ErrorMessage)
  - `Validators` 静态类 - 常用验证器集合
    - 字符串验证: `NotEmpty`, `MinLength`, `MaxLength`, `LengthRange`, `Regex`, `Email`
    - 数值验证: `IntRange`, `FloatRange`, `Positive`, `NonNegative`
    - 组合验证: `All` (全部通过), `Any` (任一通过)
  - `BindableProperty<T>` 增强:
    - `WithFormat(Func<T, string>)` - 自定义格式化函数
    - `WithFormat(string)` - 格式化字符串，如 `"{0:F2}"`
    - `WithValidation(Func<T, ValidationResult>)` - 设置验证器
    - `WithValidation(Func<T, bool>, string)` - 简单条件验证
    - `OnValidationChanged` 事件 - 验证状态变化通知
    - `ValidationState` / `IsValid` 属性 - 查询验证状态
    - `SetValueWithoutValidation()` - 跳过验证强制设值
    - `FormattedValue` 属性 - 获取格式化后的字符串
  - `BindingContext` 新绑定方法:
    - `BindTextFormatted<T>()` - 绑定格式化值到 Text
    - `BindValidation<T>()` - 绑定验证错误到 Text
    - `BindValidationColor<T>()` - 绑定验证状态到 Graphic 颜色
    - `BindTwoWay<T>()` - 双向绑定两个属性

- **BehaviorTree**: 性能优化 (零装箱)
  - `Blackboard` 类型化存储字典:
    - `_intData`, `_floatData`, `_boolData` - 值类型专用字典
    - `_vector3Data`, `_vector2Data` - 向量类型专用字典
  - `Blackboard` 类型化访问器 (零装箱):
    - `SetInt()` / `GetInt()` - int 值存取
    - `SetFloat()` / `GetFloat()` - float 值存取
    - `SetBool()` / `GetBool()` - bool 值存取
    - `SetVector3()` / `GetVector3()` - Vector3 值存取
    - `SetVector2()` / `GetVector2()` - Vector2 值存取
  - `Parallel` 节点优化:
    - `AddChild()` 同步扩容 `_childStates` 数组
    - `OnStart()` 仅重置状态不改列表大小

- **Tests**: EditMode 单元测试基础设施
  - `Tests/Editor/ZeroEngine.Tests.Editor.asmdef` - 测试程序集定义
  - `Tests/Editor/TestHelpers.cs` - 测试辅助工具
    - `EventCounter` 类 - 事件计数器
    - `AssertThrows<T>()` - 异常断言
    - `AssertApproximatelyEqual()` - 浮点数近似比较
  - `Tests/Editor/BehaviorTree/BlackboardTests.cs` - 黑板数据存储测试
  - `Tests/Editor/BehaviorTree/BehaviorTreeTests.cs` - 行为树节点测试
  - `Tests/Editor/FSM/StateMachineTests.cs` - 状态机测试
  - `Tests/Editor/MVVM/BindablePropertyTests.cs` - 可绑定属性测试
  - `Tests/Editor/MVVM/ValidatorsTests.cs` - 验证器测试

#### BehaviorTree 模块

- **BehaviorTree**: Complete behavior tree module with FSM integration
  - Core types:
    - `NodeState` enum (Running, Success, Failure)
    - `AbortMode` flags (None, Self, LowerPriority, Both)
    - `IBlackboard` interface and `Blackboard` implementation
    - `BTContext` execution context with Owner, Blackboard, DeltaTime
    - `IBTNode` interface and `BTNode` base class
    - `BehaviorTree` controller with Start/Stop/Tick/Restart
  - Composite nodes:
    - `BTComposite` base class with Fluent API (AddChild, AddChildren)
    - `Sequence` - sequential execution, all must succeed
    - `Selector` - fallback execution, first success wins
    - `Parallel` - concurrent execution with configurable policies
  - Decorator nodes:
    - `BTDecorator` base class with SetChild fluent API
    - `Repeater(n)` - repeat n times (-1 for infinite)
    - `Inverter` - invert Success/Failure
    - `Conditional` - condition check with AbortMode support
    - `AlwaysSucceed` / `AlwaysFail` - force result
  - Leaf nodes:
    - `BTLeaf` base class
    - `ActionNode` - execute delegate with NodeState return
    - `WaitNode` - wait for specified seconds
    - `LogNode` - debug logging
  - FSM Integration (BehaviorTree.Integration):
    - `FSMBlackboardAdapter` - adapt StateMachine blackboard to IBlackboard
    - `RunFSMNode` - run FSM within behavior tree, with completion condition
    - `BTStateNode` - run behavior tree within FSM state
    - Shared blackboard support for BT-FSM data exchange

### Performance Notes
- Overall score: 78/100
- Critical: Blackboard value type boxing (consider typed accessors for hot paths)
- Warning: Parallel node list operations, Integration layer object creation

## [1.2.0] - 2025-12-31

### Added
- **BuffSystem**: Event-driven architecture upgrade
  - `BuffEventArgs` struct with Buff/EventType/OldStacks/NewStacks/StackDelta
  - `BuffEventType` enum (Applied, Refreshed, Stacked, Unstacked, Expired, Removed)
  - `BuffStackMode` enum (Stack, Refresh, Replace)
  - `BuffReceiver.OnBuffChanged` - any buff state change event
  - `BuffReceiver.OnBuffApplied` - new buff applied event
  - `BuffReceiver.OnBuffRemoved` - buff removed event (with reason)
  - `BuffReceiver.RemoveBuffCompletely()` - force remove all stacks
  - `BuffReceiver.RemoveAllBuffs()` - clear all buffs
  - `BuffReceiver.GetBuffStacks()` - query stack count
  - `BuffHandler.RefreshDuration()` - public duration refresh
  - `BuffHandler.ResetStacks()` - reset stacks for Replace mode
  - `BuffHandler.ForceExpire()` - force expiration
  - `BuffData.StackMode` - configurable stacking behavior

- **BuffSystem**: Stat-Buff integration utilities
  - `BuffUtils` static class with quick buff creation
  - `CreateTempBuff()` - runtime buff data creation
  - `CreateStatBuff()` - single stat modifier buff
  - `CreateMultiStatBuff()` - multi-stat modifier buff
  - `CreateBoostBuff()` / `CreateDebuffBuff()` - percentage buffs
  - `CreateFlatBuff()` - flat value buffs
  - `CreateDotBuff()` / `CreateHotBuff()` - tick-based buffs
  - Extension methods: `AddPercentBoost()`, `AddPercentDebuff()`, `AddFlatBoost()`
  - Query extensions: `GetAllBuffs()`, `GetAllDebuffs()`, `GetBuffsAffectingStat()`
  - `GetTotalStatModification()` - calculate total buff contribution
  - `RemoveAllDebuffs()`, `RemoveAllBuffsByCategory()`

- **Inventory**: Event system and classification
  - `ItemCategory` enum (Weapon, Armor, Accessory, Potion, Food, etc.)
  - `ItemRarity` enum (Common, Uncommon, Rare, Epic, Legendary, Mythic)
  - `InventoryEventType` enum (ItemAdded, ItemRemoved, ItemUsed, etc.)
  - `SlotChangedEventArgs` struct with full change info
  - `InventoryManager.OnSlotChanged` - slot change event
  - `InventoryManager.OnItemAdded` / `OnItemRemoved` - item events
  - `InventoryManager.OnInventoryFull` - overflow event
  - `InventoryManager.RegisterItem()` - single item registration
  - `InventoryManager.GetItemData()` - lookup by ID
  - `InventoryManager.AddItem(string)` - add by ID overload
  - `InventoryManager.RemoveItem(SO)` - remove by SO overload
  - `InventoryManager.ClearSlot()` / `ClearAll()` - slot clearing
  - `InventoryManager.HasItem()` - quantity check
  - `InventoryManager.FindSlot()` / `GetSlot()` - slot lookup
  - `InventoryManager.GetEmptySlotCount()` / `IsFull` - capacity check
  - `InventoryManager.GetItemsByType()` / `GetItemsByCategory()` / `GetItemsByRarity()` - queries
  - `InventoryManager.SwapSlots()` - slot swapping
  - `InventoryManager.UseItem()` - use item at slot
  - `InventoryManager.Sort()` - inventory sorting

- **Inventory**: Item enhancements
  - `InventoryItemSO.Category` - item category
  - `InventoryItemSO.Rarity` - item rarity
  - `InventoryItemSO.BuyPrice` / `SellPrice` - economy fields
  - `InventoryItemSO.IsStackable` - convenience property
  - `InventoryItemSO.GetRarityColor()` - rarity color helper

- **Inventory**: Slot enhancements
  - `InventorySlot.SlotIndex` - slot position tracking
  - `InventorySlot.IsFull` / `AvailableSpace` - capacity properties
  - `InventorySlot.Clone()` - slot cloning
  - `InventorySlot.SwapWith()` - swap with another slot
  - `InventorySlot.TryMergeInto()` - merge stacks

- **Quest**: Polymorphic condition/reward system
  - `QuestCondition` abstract base class with `[SerializeReference]` support
  - `QuestEvents` constants class for event types
  - `ConditionEventData` struct for structured event data
  - Built-in conditions: `KillCondition`, `CollectCondition`, `InteractCondition`, `ReachCondition`
  - `QuestReward` abstract base class for extensible rewards
  - Built-in rewards: `ExpReward`, `CurrencyReward`, `ItemReward`
  - `CurrencyType` enum (Gold, Gem, Token)
  - `QuestManager.OnConditionProgress` - condition progress event
  - `QuestManager.OnConditionCompleted` - condition completion event
  - `QuestManager.ProcessConditionEvent()` - process condition events
  - `QuestManager.AbandonQuest()` - abandon active quest
  - `QuestManager.GetConditionProgress()` - query condition progress
  - `QuestConfigSO.Conditions` - polymorphic condition list
  - `QuestConfigSO.Rewards` - polymorphic reward list
  - `QuestConfigSO.UsesNewConditionSystem` / `UsesNewRewardSystem` - system detection
  - Legacy system compatibility maintained

- **Core**: Event constants
  - `EventManager.CurrencyGained` - currency gain event constant

### Changed
- **Inventory**: LINQ to for-loop optimization
  - `GetItemsByType/Category/Rarity/MinRarity` - iterator-based, zero allocation
  - `GetAllItems` - iterator-based
  - `GetEmptySlotCount` - for-loop counting
  - `FindSlot` - for-loop search
  - `Sort` - reusable temp list to avoid allocation
  - `AddItem` / `RemoveItem` - reusable buffer for affected slots

- **Quest**: LINQ to for-loop optimization and zero-alloc APIs
  - Added `FindActiveQuest()` / `FindHistory()` helper methods
  - `QuestRuntimeData.Progress` dictionary for v1.2.0+ condition system
  - `GetConditionProgressNonAlloc()` - zero-allocation variant
  - `GetConditionProgressCached()` - uses internal buffer
  - Removed LINQ dependencies

- **Utils**: Conditional debug logging
  - `DebugUtils` class with `[Conditional]` attribute methods
  - Methods compiled out when `ZEROENGINE_DEBUG` not defined
  - `DebugUtils.Log()` / `LogWarning()` / `Assert()` / `LogPerf()`

- **Utils**: Unified logging system
  - `ZeroLog` class with consistent `[ZeroEngine.{Module}]` format
  - `ZeroLog.Modules` constants for module names
  - `ZeroLog.Info()` / `Warning()` / `Error()` / `Exception()`
  - `ZeroLog.WarnIf()` / `ErrorIf()` - conditional logging
  - `ZeroLog.Enabled` / `MinLevel` - runtime configuration
  - Core modules migrated: InventoryManager, QuestManager, SaveManager

- **Documentation**: XML comments for public APIs
  - BuffSystem: BuffHandler, BuffData, BuffReceiver, BuffEnums
  - StatSystem: Stat, StatModifier, StatModType

- **Samples**: Basic usage examples
  - StatSystemExample - stat initialization, modifiers, events
  - BuffSystemExample - buff operations, runtime buffs
  - InventoryExample - item management, queries
  - QuestSystemExample - quest flow, condition system

## [1.1.0] - 2024-12-31

### Added
- **StatSystem**: Event-driven architecture
  - `StatChangedEventArgs` struct with OldValue/NewValue/Delta
  - `Stat.OnValueChanged` event for value change notifications
  - `Stat.ForceRecalculate()` for manual recalculation and event firing
  - `Stat.ClearEventListeners()` for cleanup when disposing stat owners
  - `StatControllerChangedEventArgs` for controller-level events
  - `StatController.OnAnyStatChanged` aggregated event
  - `StatController.RefreshAllStats()` to force recalculate all stats
  - `StatController.InitStat(type, baseValue, minValue, maxValue)` overload with constraints

- **Save**: Provider abstraction pattern
  - `ISaveProvider` interface for implementing different backends
  - `JsonSaveProvider` - pure JSON implementation without external dependencies
  - `ES3SaveProvider` - wrapper for Easy Save 3 (when available)
  - `SaveManager.Provider` property with auto-selection (ES3 > JSON)
  - `SaveManager.UseJsonProvider()` to force JSON backend

- **Network**: Convenience wrappers and reconnection
  - `ZeroNetworkBehaviour` base class for networked behaviours
    - `ServerOnly(Action)` - execute only on server
    - `OwnerOnly(Action)` - execute only on owning client
    - `RemoteOnly(Action)` - execute only on non-owner clients
    - `RequestOwnership()` - request ownership from server
    - `OnNetworkReady()` / `OnOwnershipGained()` / `OnOwnershipLost()` virtual methods
  - `ReconnectionHandler` component for automatic reconnection
    - Configurable max attempts and delays
    - Exponential backoff algorithm
    - `ReconnectionState` enum (Idle/Attempting/Success/Failed)
    - `OnReconnectionStateChanged` event

- **AbilitySystem**: Complete casting system rewrite
  - `AbilityDataSO` new fields: CastTime, RecoveryTime, Interruptible, BaseCooldown, MaxLevel, EffectScalePerLevel, CooldownReductionPerLevel
  - `AbilityDataSO.GetCooldown(level)` and `GetEffectMultiplier(level)` methods
  - `AbilityInstance` runtime class with Level, CooldownRemaining, TryLevelUp(), cooldown management
  - `AbilityCastState` enum (Idle/Casting/Executing/Recovering)
  - `AbilityEventArgs` for ability events
  - `AbilityHandler` rewritten with full state machine:
    - `TryCastAbility()` with target support
    - `TryInterrupt(reason)` for cast interruption
    - `CanCast()` validation
    - `TryLevelUpAbility()` for leveling
    - `ResetCooldown()` / `ResetAllCooldowns()`
    - Events: OnAbilityStateChanged, OnAbilityInterrupted, OnAbilityExecuted

## [1.0.0] - 2024-12-30

### Added
- Initial UPM package release
- Core: Singleton, EventManager
- StatSystem: Stat with MinValue/MaxValue limits
- BuffSystem: Buff management
- AbilitySystem: Trigger/Condition/Effect components
- Inventory: Item management
- Quest: Quest management
- UI.MVVM: ViewModel, BindableProperty, BindingContext
- Command: Undo/Redo support
- FSM: State machine with blackboard
- Save: EasySave3 wrapper
- Audio: BGM/SFX management
- Network: NGO wrapper, Lobby, Chat
- Localization: Unity Localization wrapper
- InputSystem: Input management
- ModSystem: Mod loading, hot reload, Lua scripting, Steam Workshop
- Pool: Object pooling
- SpineSkin: Spine skin management
